using System.Numerics;
using Org.BouncyCastle.Crypto.Digests;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>One DOT transfer on Asset Hub, as it will be signed. Amounts in planck (10^-10 DOT).</summary>
public sealed record DotTransfer(
    byte[] From,
    byte[] To,
    BigInteger Planck,
    uint Nonce,
    uint SpecVersion,
    uint TransactionVersion,
    byte[] GenesisHash,
    uint EraBlock,
    byte[] EraBlockHash);

/// <summary>
/// Polkadot transfers (roadmap N.8, send): a signed v4 extrinsic carrying
/// <c>Balances.transfer_keep_alive</c> on Polkadot Asset Hub — where DOT balances have lived since the
/// 2025 migration — signed with sr25519.
///
/// Everything structural comes from the running runtime's metadata (<see cref="RuntimeMetadata"/>):
/// the pallet and call indices, the shape of the call's arguments, and the list of transaction
/// extensions. The extensions must be exactly the ones this wallet knows how to fill in, with the
/// types it expects; a runtime upgrade that changes them makes the wallet refuse to sign rather than
/// guess. Before anything is broadcast, the node itself validates the signed transaction
/// (<c>TaggedTransactionQueue_validate_transaction</c>).
///
/// Only transfer_keep_alive can be expressed — it cannot empty the sending account below the
/// existential deposit, and nothing else (no staking, no XCM, no batch) can be built here.
/// </summary>
public static class PolkadotTransactions
{
    /// <summary>The extensions, in order, this wallet fills in — Asset Hub's list since the migration
    /// (runtime 2005000). Those not named in <see cref="CheckExtensions"/> must carry no data at all.</summary>
    public static readonly string[] KnownExtensions =
    [
        "AuthorizeCall", "CheckNonZeroSender", "CheckSpecVersion", "CheckTxVersion", "CheckGenesis",
        "CheckMortality", "CheckNonce", "CheckWeight", "ChargeAssetTxPayment", "PrevalidateAttests",
        "CheckMetadataHash", "EthSetOrigin", "StorageWeightReclaim",
    ];

    /// <summary>How many blocks a signed transfer stays valid for: 64, about six minutes on Asset Hub.
    /// After that it can never be included, which settles a submission whose answer never came.</summary>
    public const uint EraPeriod = 64;

    /// <summary>
    /// Null when the runtime's extensions are exactly the known ones, in order, each with the extra and
    /// signed-extra types this wallet encodes; otherwise what differs.
    /// </summary>
    public static string? CheckExtensions(RuntimeMetadata md)
    {
        if (md.ExtrinsicVersion != 4) return $"extrinsic version {md.ExtrinsicVersion}";
        var ids = md.Extensions.Select(e => e.Identifier).ToArray();
        if (!ids.SequenceEqual(KnownExtensions)) return $"transaction extensions [{string.Join(", ", ids)}]";

        foreach (var e in md.Extensions)
        {
            var ok = e.Identifier switch
            {
                "CheckSpecVersion" or "CheckTxVersion" =>
                    md.IsEmpty(e.Type) && md.Resolve(e.AdditionalSigned) is { Kind: RuntimeMetadata.Kind.Primitive, Primitive: 5 },   // u32
                "CheckGenesis" => md.IsEmpty(e.Type) && IsHash(md, e.AdditionalSigned),
                "CheckMortality" => md.Resolve(e.Type) is { Kind: RuntimeMetadata.Kind.Variant } era &&
                                    era.Path.LastOrDefault() == "Era" && IsHash(md, e.AdditionalSigned),
                "CheckNonce" => md.Resolve(e.Type) is { Kind: RuntimeMetadata.Kind.Compact } && md.IsEmpty(e.AdditionalSigned),
                "ChargeAssetTxPayment" => IsTipAndAsset(md, e.Type) && md.IsEmpty(e.AdditionalSigned),
                "CheckMetadataHash" => IsModeDisabledFirst(md, e.Type) && IsOptionHash(md, e.AdditionalSigned),
                _ => md.IsEmpty(e.Type) && md.IsEmpty(e.AdditionalSigned),
            };
            if (!ok) return $"the {e.Identifier} extension's types";
        }

        return null;
    }

    /// <summary>
    /// The call bytes: Balances.transfer_keep_alive(MultiAddress::Id(to), Compact(planck)) at the indices
    /// the runtime reports, after checking its arguments are those types. Null with a reason otherwise.
    /// </summary>
    public static (byte[]? Call, string? Problem) TransferCall(RuntimeMetadata md, byte[] to, BigInteger planck)
    {
        if (to.Length != 32) throw new ArgumentException("An account id is 32 bytes.", nameof(to));
        if (planck.Sign <= 0) throw new ArgumentException("A transfer moves a positive amount.", nameof(planck));

        if (md.Call("Balances", "transfer_keep_alive") is not { } call) return (null, "Balances.transfer_keep_alive is missing");
        if (call.Fields.Count != 2 || call.Fields[0].Name != "dest" || call.Fields[1].Name != "value")
            return (null, "transfer_keep_alive's arguments changed");

        // dest: MultiAddress, whose "Id" variant (index 0) holds a 32-byte account id.
        var dest = md.Types[call.Fields[0].Type];
        var id = dest.Kind == RuntimeMetadata.Kind.Variant ? dest.Variants.FirstOrDefault(v => v.Name == "Id") : null;
        if (id is not { Index: 0, Fields.Count: 1 } || !IsBytes(md, id.Fields[0].Type, 32))
            return (null, "the MultiAddress type changed");

        // value: Compact<u128>.
        if (md.Types[call.Fields[1].Type] is not { Kind: RuntimeMetadata.Kind.Compact } value ||
            md.Types[value.Element] is not { Kind: RuntimeMetadata.Kind.Primitive, Primitive: 7 })
            return (null, "the transfer amount type changed");

        return ([call.Pallet, call.Call, 0x00, .. to, .. Scale.Compact(planck)], null);
    }

    /// <summary>A mortal era of <see cref="EraPeriod"/> blocks starting at <paramref name="block"/>.</summary>
    public static byte[] MortalEra(uint block)
    {
        const uint period = EraPeriod;
        var phase = block % period;
        var quantize = Math.Max(period >> 12, 1);
        var encoded = (ushort)(Math.Min(15, Math.Max(1, BitOperations.TrailingZeroCount(period) - 1)) | ((phase / quantize) << 4));
        return [(byte)encoded, (byte)(encoded >> 8)];
    }

    /// <summary>The extensions' own data, in order: era, nonce, a zero tip paid in DOT (no asset), and
    /// the metadata-hash check left off. The others carry nothing.</summary>
    public static byte[] Extra(DotTransfer t) =>
        [.. MortalEra(t.EraBlock), .. Scale.Compact(t.Nonce), 0x00, 0x00, 0x00];

    /// <summary>What the signature also covers without it being sent: spec and transaction versions, the
    /// genesis hash, the era's starting block hash, and "no metadata hash".</summary>
    public static byte[] Additional(DotTransfer t)
    {
        if (t.GenesisHash.Length != 32 || t.EraBlockHash.Length != 32) throw new ArgumentException("Hashes are 32 bytes.");
        return [.. Scale.U32(t.SpecVersion), .. Scale.U32(t.TransactionVersion), .. t.GenesisHash, .. t.EraBlockHash, 0x00];
    }

    /// <summary>What sr25519 signs: call ‖ extra ‖ additional, hashed with BLAKE2b-256 when over 256 bytes.</summary>
    public static byte[] SigningPayload(byte[] call, byte[] extra, byte[] additional)
    {
        byte[] payload = [.. call, .. extra, .. additional];
        return payload.Length > 256 ? Blake2b256(payload) : payload;
    }

    /// <summary>The signed v4 extrinsic, length-prefixed: signer, sr25519 signature, extra, call.</summary>
    public static byte[] Extrinsic(byte[] from, byte[] signature, byte[] extra, byte[] call)
    {
        if (from.Length != 32 || signature.Length != 64) throw new ArgumentException("A 32-byte signer and a 64-byte signature.");
        byte[] body = [0x84, 0x00, .. from, 0x01, .. signature, .. extra, .. call];
        return [.. Scale.Compact(body.Length), .. body];
    }

    /// <summary>The extrinsic hash explorers show: BLAKE2b-256 of the extrinsic bytes.</summary>
    public static byte[] Hash(byte[] extrinsic) => Blake2b256(extrinsic);

    /// <summary>Signs a transfer with the key the sending address comes from, verifies the signature, and
    /// returns the extrinsic and its hash.</summary>
    public static (byte[] Extrinsic, byte[] Hash) Sign(RuntimeMetadata md, DotTransfer t, Sr25519.Keypair key)
    {
        if (!key.PublicKey.AsSpan().SequenceEqual(t.From))
            throw new InvalidOperationException("The signing key does not belong to the sending address.");
        if (CheckExtensions(md) is { } changed)
            throw new InvalidOperationException($"The network changed how transactions are built ({changed}).");
        var (call, problem) = TransferCall(md, t.To, t.Planck);
        if (call is null) throw new InvalidOperationException($"The network changed how transfers are built ({problem}).");

        var extra = Extra(t);
        var payload = SigningPayload(call, extra, Additional(t));
        var signature = Sr25519.Sign(key, payload);
        if (!Sr25519.Verify(signature, payload, t.From))
            throw new InvalidOperationException("The signature did not verify.");

        var extrinsic = Extrinsic(t.From, signature, extra, call);
        return (extrinsic, Hash(extrinsic));
    }

    /// <summary>The same extrinsic with an all-zero signature — what a fee estimate is asked for, since a
    /// fee does not depend on the signature and nothing unsigned should look signed.</summary>
    public static byte[]? UnsignedForFee(RuntimeMetadata md, DotTransfer t)
    {
        var (call, _) = TransferCall(md, t.To, t.Planck);
        return call is null ? null : Extrinsic(t.From, new byte[64], Extra(t), call);
    }

    private static byte[] Blake2b256(byte[] data)
    {
        var digest = new Blake2bDigest(256);
        digest.BlockUpdate(data, 0, data.Length);
        var output = new byte[32];
        digest.DoFinal(output, 0);
        return output;
    }

    private static bool IsBytes(RuntimeMetadata md, int type, uint length) =>
        md.Resolve(type) is { Kind: RuntimeMetadata.Kind.Array } a && a.Length == length &&
        md.Types[a.Element] is { Kind: RuntimeMetadata.Kind.Primitive, Primitive: 3 };

    private static bool IsHash(RuntimeMetadata md, int type) => IsBytes(md, type, 32);

    private static bool IsOption(RuntimeMetadata.TypeDef d) =>
        d.Kind == RuntimeMetadata.Kind.Variant && d.Path.LastOrDefault() == "Option" &&
        d.Variants.Any(v => v is { Name: "None", Index: 0, Fields.Count: 0 }) &&
        d.Variants.Any(v => v is { Name: "Some", Index: 1, Fields.Count: 1 });

    private static bool IsOptionHash(RuntimeMetadata md, int type) =>
        md.Types[type] is var d && IsOption(d) && IsHash(md, d.Variants.First(v => v.Name == "Some").Fields[0].Type);

    /// <summary>ChargeAssetTxPayment: { tip: Compact&lt;u128&gt;, asset_id: Option&lt;…&gt; }.</summary>
    private static bool IsTipAndAsset(RuntimeMetadata md, int type) =>
        md.Types[type] is { Kind: RuntimeMetadata.Kind.Composite } d && d.Fields.Count == 2 &&
        d.Fields[0].Name == "tip" && md.Types[d.Fields[0].Type].Kind == RuntimeMetadata.Kind.Compact &&
        d.Fields[1].Name == "asset_id" && IsOption(md.Types[d.Fields[1].Type]);

    /// <summary>CheckMetadataHash: { mode: Mode } where Disabled is variant 0 with no data.</summary>
    private static bool IsModeDisabledFirst(RuntimeMetadata md, int type) =>
        md.Types[type] is { Kind: RuntimeMetadata.Kind.Composite } d && d.Fields.Count == 1 &&
        md.Types[d.Fields[0].Type] is { Kind: RuntimeMetadata.Kind.Variant } mode &&
        mode.Variants.Any(v => v is { Name: "Disabled", Index: 0, Fields.Count: 0 });
}
