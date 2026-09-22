using System.Security.Cryptography;
using System.Text;
using NBitcoin;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>One Cosmos bank transfer, as it will be signed. Amounts are in the chain's base unit (uatom).</summary>
public sealed record CosmosSend(
    string From,
    string To,
    ulong Amount,
    string Denom,
    string Memo,
    ulong TimeoutHeight,
    byte[] PublicKey,
    ulong AccountNumber,
    ulong Sequence,
    ulong Fee,
    ulong GasLimit,
    string ChainId);

/// <summary>
/// Cosmos transactions (roadmap N.6, send): the protobuf TxBody carrying one MsgSend, the AuthInfo with
/// one SIGN_MODE_DIRECT signer, the SignDoc, its secp256k1 signature, and the TxRaw a node accepts —
/// byte-for-byte what cosmjs builds, pinned to its own test vectors (<c>CosmosSendTests</c>).
///
/// Only a bank MsgSend can be expressed: no delegation, no IBC, no authz grant, no contract call.
/// Nothing a server answers can be turned into any of those.
/// </summary>
public static class CosmosTransactions
{
    /// <summary>The Cosmos Hub's chain id. A server claiming any other is not the Hub, and a signature
    /// made for it would be valid on whatever chain it is.</summary>
    public const string HubChainId = "cosmoshub-4";

    /// <summary>The Hub's memo limit, in characters (the SDK's default, which the Hub keeps).</summary>
    public const int MaxMemoCharacters = 256;

    private const string MsgSendType = "/cosmos.bank.v1beta1.MsgSend";
    private const string PubKeyType = "/cosmos.crypto.secp256k1.PubKey";
    private const ulong SignModeDirect = 1;

    /// <summary>A positive ATOM amount with at most six decimal places, as uatom.</summary>
    public static bool TryToMicro(decimal atom, out ulong micro)
    {
        micro = 0;
        if (atom <= 0) return false;
        var scaled = atom * CosmosHub.MicroPerAtom;
        if (scaled != decimal.Truncate(scaled) || scaled > ulong.MaxValue) return false;
        micro = (ulong)scaled;
        return true;
    }

    public static decimal ToAtom(ulong micro) => micro / CosmosHub.MicroPerAtom;

    /// <summary>Whether a memo fits the Hub's limit and has no control characters.</summary>
    public static bool TryValidateMemo(string? memo, out string value, out string? error)
    {
        value = memo?.Trim() ?? "";
        error = null;
        if (value.Length > MaxMemoCharacters)
        {
            error = $"A Cosmos memo is at most {MaxMemoCharacters} characters.";
            return false;
        }

        if (value.Any(char.IsControl))
        {
            error = "A memo cannot contain line breaks or control characters.";
            return false;
        }

        return true;
    }

    /// <summary>The TxBody: one MsgSend, then the memo and the timeout height when they are set.</summary>
    public static byte[] BodyBytes(CosmosSend s)
    {
        if (!CosmosHub.IsValidAddress(s.From) || !CosmosHub.IsValidAddress(s.To))
            throw new ArgumentException("Not a Cosmos address.", nameof(s));
        if (s.Amount == 0) throw new ArgumentException("A transfer moves a positive amount.", nameof(s));

        var coin = new Proto().String(1, s.Denom).String(2, s.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var msg = new Proto().String(1, s.From).String(2, s.To).Message(3, coin);
        var any = new Proto().String(1, MsgSendType).Bytes(2, msg.ToArray());
        return new Proto().Message(1, any).String(2, s.Memo).UInt64(3, s.TimeoutHeight).ToArray();
    }

    /// <summary>The AuthInfo: this key signing in direct mode at this sequence, and the fee.</summary>
    public static byte[] AuthInfoBytes(CosmosSend s)
    {
        if (s.PublicKey.Length != 33) throw new ArgumentException("A compressed secp256k1 key is 33 bytes.", nameof(s));

        var pubKey = new Proto().Bytes(1, s.PublicKey);
        var pubKeyAny = new Proto().String(1, PubKeyType).Bytes(2, pubKey.ToArray());
        var modeInfo = new Proto().Message(1, new Proto().UInt64(1, SignModeDirect));
        var signer = new Proto().Message(1, pubKeyAny).Message(2, modeInfo).UInt64(3, s.Sequence);
        var feeCoin = new Proto().String(1, s.Denom).String(2, s.Fee.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var fee = new Proto().Message(1, feeCoin).UInt64(2, s.GasLimit);
        return new Proto().Message(1, signer).Message(2, fee).ToArray();
    }

    /// <summary>The SignDoc: what is hashed and signed. The chain id and account number bind the
    /// signature to this chain and this account.</summary>
    public static byte[] SignDocBytes(byte[] body, byte[] authInfo, string chainId, ulong accountNumber) =>
        new Proto().Bytes(1, body).Bytes(2, authInfo).String(3, chainId).UInt64(4, accountNumber).ToArray();

    /// <summary>
    /// Signs: SHA-256 of the SignDoc, a deterministic (RFC 6979) low-S secp256k1 signature as r ‖ s,
    /// and the TxRaw carrying it. The key must be the one the sending address comes from. Returns the
    /// transaction bytes and the hash explorers show (SHA-256 of those bytes, upper-case hex).
    /// </summary>
    public static (byte[] TxBytes, string Hash) Sign(CosmosSend s, Key key)
    {
        var own = key.PubKey.Compress().ToBytes();
        if (!own.AsSpan().SequenceEqual(s.PublicKey) ||
            CosmosHub.AddressFromAccountId(key.PubKey.Compress().Hash.ToBytes()) != s.From)
            throw new InvalidOperationException("The signing key does not belong to the sending address.");

        var body = BodyBytes(s);
        var authInfo = AuthInfoBytes(s);
        var digest = SHA256.HashData(SignDocBytes(body, authInfo, s.ChainId, s.AccountNumber));
        var signature = key.SignCompact(new uint256(digest), forceLowR: false).Signature;   // r ‖ s, 64 bytes

        var tx = new Proto().Bytes(1, body).Bytes(2, authInfo).Bytes(3, signature).ToArray();
        return (tx, Convert.ToHexString(SHA256.HashData(tx)));
    }

    /// <summary>
    /// The transaction as a node simulates it for its gas: the same body and auth info, with an empty
    /// signature (a simulation checks the key and the sequence, never the signature). Nothing signed.
    /// </summary>
    public static byte[] SimulationBytes(CosmosSend s) =>
        new Proto().Bytes(1, BodyBytes(s)).Bytes(2, AuthInfoBytes(s)).Bytes(3, [], always: true).ToArray();

    /// <summary>A minimal protobuf writer: the three wire forms a transfer needs, proto3 style (a field
    /// holding its default — zero, or empty — is left out, as every other encoder leaves it out).</summary>
    private sealed class Proto
    {
        private readonly MemoryStream _ms = new();

        public Proto UInt64(int field, ulong value)
        {
            if (value == 0) return this;
            Varint((ulong)(field << 3));
            Varint(value);
            return this;
        }

        public Proto String(int field, string value) =>
            value.Length == 0 ? this : Bytes(field, Encoding.UTF8.GetBytes(value));

        public Proto Message(int field, Proto message) => Bytes(field, message.ToArray(), always: true);

        public Proto Bytes(int field, byte[] value, bool always = false)
        {
            if (value.Length == 0 && !always) return this;
            Varint((ulong)((field << 3) | 2));
            Varint((ulong)value.Length);
            _ms.Write(value);
            return this;
        }

        public byte[] ToArray() => _ms.ToArray();

        private void Varint(ulong v)
        {
            while (v >= 0x80)
            {
                _ms.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }

            _ms.WriteByte((byte)v);
        }
    }
}
