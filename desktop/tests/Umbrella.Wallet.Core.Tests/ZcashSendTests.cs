using System.Text.Json;
using NBitcoin;
using NBitcoin.Crypto;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Transparent Zcash sending (roadmap N.8).
///
/// The signature hash itself is pinned to Zcash's own vectors in <see cref="ZcashSigHashTests"/>.
/// What is checked here is everything around it, and the one thing a wallet must never get wrong: the
/// key that signs a spend belongs to the address the user was shown. That address is pinned to a BIP32
/// derivation worked out independently of the library under test, so a change in NBitcoin cannot move
/// the wallet's addresses without this failing.
/// </summary>
public class ZcashSendTests
{
    private const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    /// <summary>m/44'/133'/0'/0/0 for the phrase above, derived from first principles (see the PR).</summary>
    private const string Address = "t1XVXWCvpMgBvUaed4XDqWtgQgJSu1Ghz7F";

    private const string Hash160 = "9564d9fed247986b15a2f57d0b3b032eeb28476c";

    [Fact]
    public void ReceiveAddressMatchesAnIndependentDerivation()
    {
        var derived = new HdAddressDeriver().DeriveReceiveAddress(Mnemonic, ChainId.Zec);
        Assert.Equal(Address, derived.Address);
        Assert.Equal("m/44'/133'/0'/0/0", derived.DerivationPath);
    }

    [Fact]
    public void SigningKeyBelongsToTheDisplayedAddress()
    {
        using var key = new HdAddressDeriver().DeriveZcashKey(Mnemonic);
        Assert.Equal(Address, ZcashAddress.EncodePublicKeyHash(key.PubKey.Hash.ToBytes()));
    }

    [Fact]
    public void PublicKeyHashAddressDecodesToItsScript()
    {
        var (decoded, error) = ZcashAddress.TryDecode(Address);
        Assert.Null(error);
        Assert.NotNull(decoded);
        Assert.Equal(ZcashAddressKind.PublicKeyHash, decoded!.Kind);
        Assert.Equal(Hash160, Convert.ToHexString(decoded.Hash160).ToLowerInvariant());
        Assert.Equal($"76a914{Hash160}88ac", Convert.ToHexString(decoded.ScriptPubKey).ToLowerInvariant());
    }

    [Fact]
    public void ScriptHashAddressDecodesToTheScriptTheChainRecords()
    {
        // A real mainnet t3 address, with the scriptPubKey the chain itself reports for it.
        var (decoded, error) = ZcashAddress.TryDecode("t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow");
        Assert.Null(error);
        Assert.Equal(ZcashAddressKind.ScriptHash, decoded!.Kind);
        Assert.Equal(
            "a914c20cd5bdf7964ca61764db66bc2531b1792a084d87",
            Convert.ToHexString(decoded.ScriptPubKey).ToLowerInvariant());
    }

    [Fact]
    public void ShieldedAddressIsRefusedByName()
    {
        var (decoded, error) = ZcashAddress.TryDecode(
            "zs1z7rejlpsa98s2rrrfkwmaxu53e4ue0ulcrw0h4x5g8jl04tak0d3mm47vdtahatqrlkngh9sly");
        Assert.Null(decoded);
        Assert.Contains("shielded", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BitcoinAddressIsNotMistakenForZcash()
    {
        // Valid Base58Check, valid on another chain, and one byte of version prefix instead of two.
        var (decoded, error) = ZcashAddress.TryDecode("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2");
        Assert.Null(decoded);
        Assert.Contains("another chain", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptedAddressFailsItsChecksum()
    {
        var corrupted = Address[..^1] + (Address[^1] == 'a' ? 'b' : 'a');
        var (decoded, error) = ZcashAddress.TryDecode(corrupted);
        Assert.Null(decoded);
        Assert.Contains("checksum", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Zcash's own activation heights, each checked on both sides of the boundary.
    [InlineData(0u, 0x00000000u)]
    [InlineData(419_199u, 0x5BA81B19u)]   // still Overwinter
    [InlineData(419_200u, 0x76B809BBu)]   // Sapling activates
    [InlineData(1_687_104u, 0xC2D6D0B4u)] // NU5
    [InlineData(2_726_400u, 0xC8E71055u)] // NU6
    [InlineData(3_146_400u, 0x4DEC4DF0u)] // NU6.1
    [InlineData(3_364_599u, 0x4DEC4DF0u)] // one block before NU6.2
    [InlineData(3_364_600u, 0x5437F330u)] // NU6.2
    [InlineData(9_000_000u, 0x5437F330u)] // beyond the table: the last known upgrade still applies
    public void BranchIdIsTheUpgradeInForceAtThatHeight(uint height, uint expected)
        => Assert.Equal(expected, ZcashTransactions.ConsensusBranchId(height));

    [Theory]
    // Nowhere near an upgrade: the full 40 blocks.
    [InlineData(3_496_663u, 3_496_703u)]
    // NU6.2 activates at 3,364,600 inside the window: expire on the last block before it, as zcashd does.
    [InlineData(3_364_580u, 3_364_599u)]
    // The activation block itself is already under the new rules, so the window is whole again.
    [InlineData(3_364_600u, 3_364_640u)]
    public void ExpiryNeverCrossesTheNextUpgrade(uint nextHeight, uint expected)
        => Assert.Equal(expected, ZcashTransactions.ExpiryHeight(nextHeight));

    [Fact]
    public void NoTransactionIsBuiltInTheLastBlocksBeforeAnUpgrade()
    {
        // Two blocks before NU6.2 the capped expiry would be inside the window nodes call "expiring
        // soon" and refuse — so the wallet says to wait instead of signing something nobody relays.
        Assert.Null(ZcashTransactions.ExpiryHeight(3_364_598));
    }

    [Theory]
    [InlineData(1, 1, 10_000UL)]   // below the grace, so the grace applies
    [InlineData(1, 2, 10_000UL)]   // the ordinary spend: 0.0001 ZEC
    [InlineData(3, 2, 15_000UL)]
    [InlineData(2, 9, 45_000UL)]
    public void ConventionalFeeFollowsZip317(int inputs, int outputs, ulong expected)
        => Assert.Equal(expected, ZcashTransactions.ConventionalFee(inputs, outputs));

    [Theory]
    [InlineData(25, 54UL)]  // P2PKH, the size Zcash's own comment works through
    [InlineData(23, 54UL)]  // P2SH
    public void DustThresholdMatchesZcashsRule(int scriptLength, ulong expected)
        => Assert.Equal(expected, ZcashSendRules.DustThreshold(scriptLength));

    [Fact]
    public void PlanRefusesAnAmountBelowTheDustLimit()
    {
        var (plan, error) = ZcashSendRules.Plan([Coin(1_000_000, 100)], 10, ToScript, ChangeScript);
        Assert.Null(plan);
        Assert.Contains("dust", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanWillNotSpendAnUnconfirmedCoin()
    {
        var (plan, error) = ZcashSendRules.Plan(
            [new ZcashUtxo(new string('a', 64), 0, 5_000_000, null)], 1_000_000, ToScript, ChangeScript);
        Assert.Null(plan);
        Assert.Contains("unconfirmed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanTakesTheLargestCoinsFirst()
    {
        var (plan, error) = ZcashSendRules.Plan(
            [Coin(100_000, 1), Coin(9_000_000, 2), Coin(200_000, 3)], 5_000_000, ToScript, ChangeScript);

        Assert.Null(error);
        var single = Assert.Single(plan!.Inputs);
        Assert.Equal(9_000_000UL, single.Zatoshi);
        Assert.Equal(10_000UL, plan.FeeZat);
        Assert.Equal(9_000_000UL - 5_000_000 - 10_000, plan.ChangeZat);
        Assert.False(plan.ChangeSweptToFee);
    }

    [Fact]
    public void ChangeTooSmallToPayOutGoesToTheFeeInstead()
    {
        // 20 zatoshi left after the fee — below the 54-zatoshi dust limit, so no node would take it
        // as an output. It is added to the fee rather than being built into a transaction that fails.
        var (plan, error) = ZcashSendRules.Plan([Coin(1_010_020, 100)], 1_000_000, ToScript, ChangeScript);

        Assert.Null(error);
        Assert.True(plan!.ChangeSweptToFee);
        Assert.Equal(0UL, plan.ChangeZat);
        Assert.Equal(10_020UL, plan.FeeZat);
        Assert.Equal(plan.TotalInputZat, plan.AmountZat + plan.FeeZat);
    }

    [Fact]
    public void ShortfallSaysWhatIsHeldAndWhatIsNeeded()
    {
        var (plan, error) = ZcashSendRules.Plan([Coin(1_000_000, 100)], 2_000_000, ToScript, ChangeScript);
        Assert.Null(plan);
        Assert.Contains("0.01", error);      // what the address holds
        Assert.Contains("0.0201", error);    // the amount plus the fee
    }

    [Fact]
    public void AmountPlusFeeIsRefusedEvenWhenTheAmountAloneFits()
    {
        var (plan, error) = ZcashSendRules.Plan([Coin(1_000_000, 100)], 1_000_000, ToScript, ChangeScript);
        Assert.Null(plan);
        Assert.Contains("network fee", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaxSendableLeavesExactlyTheFeeBehind()
    {
        var coins = new[] { Coin(1_000_000, 10), Coin(2_000_000, 11) };
        Assert.Equal(3_000_000UL - 10_000, ZcashSendRules.MaxSendable(coins));

        // An address whose whole balance is smaller than the fee can send nothing at all.
        Assert.Equal(0UL, ZcashSendRules.MaxSendable([Coin(500, 10)]));
    }

    [Fact]
    public void SignedTransactionCarriesTheSaplingHeaderAndAVerifiableSignature()
    {
        using var key = new HdAddressDeriver().DeriveZcashKey(Mnemonic);
        var quote = Quote(key, amountZat: 1_000_000, changeZat: 490_000);
        var (raw, txId) = ZcashTransactionSender.Build(quote, key);

        // v4, with the overwintered bit set, and the Sapling version group.
        Assert.Equal("0400008085202f89", Convert.ToHexString(raw[..8]).ToLowerInvariant());
        Assert.Equal(64, txId.Length);

        // The signature in the script has to verify against the digest the signer claims to have signed.
        var inputs = quote.Inputs
            .Select(u => new ZcashInput(Reverse(u.TxId), u.Index, u.Zatoshi, quote.FromScript))
            .ToList();
        var outputs = new List<ZcashOutput>
        {
            new(quote.AmountZat, quote.ToScript),
            new(quote.ChangeZat, quote.FromScript),
        };
        var digest = ZcashTransactions.SigHash(
            inputs, outputs, 0, quote.FromScript, ZcashTransactions.SigHashAll,
            lockTime: 0, expiryHeight: quote.ExpiryHeight, branchId: quote.BranchId);

        var scriptSig = ExtractScriptSig(raw);
        var derLength = scriptSig[0];
        var der = scriptSig[1..(derLength)];            // the push, without its trailing hash type
        var hashType = scriptSig[derLength];
        var pubKey = scriptSig[(derLength + 2)..];

        Assert.Equal(ZcashTransactions.SigHashAll, hashType);
        Assert.Equal(key.PubKey.ToBytes(), pubKey);
        Assert.True(key.PubKey.Verify(new uint256(digest), ECDSASignature.FromDER(der)));
    }

    [Fact]
    public async Task KeyFromAnotherWalletIsRefusedBeforeAnythingIsSigned()
    {
        // No network call happens: the wallet checks the key against the reviewed address first.
        using var key = new HdAddressDeriver().DeriveZcashKey(Mnemonic, addressIndex: 7);
        var quote = Quote(key, 1_000_000, 490_000) with { From = Address };

        var result = await new ZcashTransactionSender().SignAndBroadcastAsync(quote, key);
        Assert.False(result.Ok);
        Assert.False(result.Unclear);
        Assert.Contains("unlocked wallet", result.Error);
    }

    [Fact]
    public void ExplorerCoinsAreReadAndUnconfirmedOnesAreLeftOut()
    {
        // The shape Blockchair actually returns, including its -1 for a coin still in the mempool.
        const string json = """
        {
          "data": {
            "t1XVXWCvpMgBvUaed4XDqWtgQgJSu1Ghz7F": {
              "address": { "balance": 12500000 },
              "utxo": [
                { "block_id": 3494857, "transaction_hash": "3462440762722d8ea8e4e6597ded7178f95a09e9c445f30f25a0ed23b8f5afbd", "index": 0, "value": 12500000 },
                { "block_id": -1, "transaction_hash": "1111111111111111111111111111111111111111111111111111111111111111", "index": 3, "value": 700 }
              ]
            }
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var utxos = ZcashTransactionSender.ParseUtxos(doc.RootElement);

        Assert.Equal(2, utxos.Count);
        Assert.Equal(3_494_857L, utxos[0].Height);
        Assert.Null(utxos[1].Height);

        // The unconfirmed one is read, but never spent.
        var (plan, _) = ZcashSendRules.Plan(utxos, 1_000_000, ToScript, ChangeScript);
        Assert.Single(plan!.Inputs);
        Assert.Equal(12_500_000UL, plan.Inputs[0].Zatoshi);
    }

    [Fact]
    public void ExplorerErrorIsReportedInItsOwnWords()
    {
        Assert.Equal(
            "Transaction is invalid",
            ZcashTransactionSender.Explain("""{"data":null,"context":{"code":400,"error":"Transaction is invalid"}}"""));
    }

    [Fact]
    public void ZcashIsListedAsSpendable()
    {
        var zec = ChainCatalog.All.Single(c => c.Symbol == "ZEC");
        Assert.True(zec.CanSend);
        Assert.Contains("transparent", zec.PrivacyNote, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static byte[] ToScript => ZcashAddress.TryDecode("t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow").Address!.ScriptPubKey;

    private static byte[] ChangeScript => ZcashAddress.TryDecode(Address).Address!.ScriptPubKey;

    private static ZcashUtxo Coin(ulong zatoshi, long height) =>
        new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(zatoshi + (ulong)height)))
            .ToLowerInvariant(), 0, zatoshi, height);

    private static ZecSendQuote Quote(Key key, ulong amountZat, ulong changeZat) => new(
        From: ZcashAddress.EncodePublicKeyHash(key.PubKey.Hash.ToBytes()),
        To: "t3cFfPt1Bcvgez9ZbMBFWeZsskxTkPzGCow",
        Amount: ZcashTransactions.ToZec(amountZat),
        AmountZat: amountZat,
        FeeZat: 10_000,
        FeeZec: 0.0001m,
        ChangeZat: changeZat,
        ExpiryHeight: 3_494_900,
        BranchId: ZcashTransactions.ConsensusBranchId(3_494_900),
        Inputs: [Coin(amountZat + changeZat + 10_000, 3_494_800)],
        FromScript: ZcashAddress.TryDecode(ZcashAddress.EncodePublicKeyHash(key.PubKey.Hash.ToBytes())).Address!.ScriptPubKey,
        ToScript: ToScript,
        ChangeSweptToFee: false);

    private static byte[] Reverse(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Array.Reverse(bytes);
        return bytes;
    }

    /// <summary>The first input's signature script, read back out of the serialised transaction.</summary>
    private static byte[] ExtractScriptSig(byte[] raw)
    {
        // 4 version + 4 group id + 1 input count + 32 txid + 4 index, then the script's length prefix.
        var offset = 4 + 4 + 1 + 32 + 4;
        var length = raw[offset];
        return raw[(offset + 1)..(offset + 1 + length)];
    }
}
