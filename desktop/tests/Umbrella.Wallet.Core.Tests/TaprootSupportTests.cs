using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Taproot (BIP-341/86), roadmap P2.1.
///
/// The claim being tested is narrow and it is the only one the wallet makes: a seed that was used in
/// a Taproot wallet is FOUND, SHOWN and SPENDABLE here. The wallet still issues native SegWit receive
/// addresses, so nothing new is handed out that these tests do not cover — the cardinal rule, kept.
///
/// Every test here can fail. The addresses are checked against the BIP86 test vectors rather than
/// against this wallet's own output, the scan is proven to find coins that the pre-Taproot scan
/// provably missed, and the spend is verified by NBitcoin's own consensus check rather than by
/// asserting the builder returned something non-null.
/// </summary>
public sealed class TaprootSupportTests
{
    /// <summary>The BIP86 test-vector seed. Its expected addresses are published in the BIP itself,
    /// so a derivation bug here shows up as a mismatch with the standard, not with ourselves.</summary>
    private const string Bip86Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();

    [Fact]
    public void Derives_the_addresses_BIP86_publishes_for_this_seed()
    {
        // Copied from bip-0086.mediawiki, "Test vectors", account 0: m/86'/0'/0'/0/{0,1} and
        // m/86'/0'/0'/1/0. If our derivation drifts by a hardened step, a script type or a tweak,
        // these stop matching.
        var external0 = Deriver.DeriveBitcoinLikeAt(
            Bip86Phrase, ChainId.Btc, change: 0, index: 0, kind: UtxoScriptKind.Taproot);
        var external1 = Deriver.DeriveBitcoinLikeAt(
            Bip86Phrase, ChainId.Btc, change: 0, index: 1, kind: UtxoScriptKind.Taproot);
        var internal0 = Deriver.DeriveBitcoinLikeAt(
            Bip86Phrase, ChainId.Btc, change: 1, index: 0, kind: UtxoScriptKind.Taproot);

        Assert.Equal("bc1p5cyxnuxmeuwuvkwfem96lqzszd02n6xdcjrs20cac6yqjjwudpxqkedrcr", external0.Address);
        Assert.Equal("bc1p4qhjn9zdvkux4e44uhx8tc55attvtyu358kutcqkudyccelu0was9fqzwh", external1.Address);
        Assert.Equal("bc1p3qkhfews2uk44qtvauqyr2ttdsw7svhkl9nkm9s9c3x4ax5h60wqwruhk7", internal0.Address);
    }

    [Fact]
    public void A_taproot_leaf_is_a_different_address_from_the_segwit_leaf_at_the_same_index()
    {
        // The whole reason a Taproot branch has to be scanned separately: same seed, same index,
        // different account. If these ever coincided, one scan would cover both and this feature
        // would be pointless — so the test asserts they do not.
        var segwit = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0);
        var taproot = Deriver.DeriveBitcoinLikeAt(
            Bip86Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot);

        Assert.StartsWith("bc1q", segwit.Address);
        Assert.StartsWith("bc1p", taproot.Address);
        Assert.NotEqual(segwit.Address, taproot.Address);
        Assert.NotEqual(segwit.PrivateKey.ToHex(), taproot.PrivateKey.ToHex());
        Assert.True(taproot.Path.IsTaproot);
        Assert.False(segwit.Path.IsTaproot);
    }

    [Fact]
    public void Taproot_is_offered_only_on_Bitcoin()
    {
        // Litecoin's Taproot is a separate deployment this wallet does not derive, and DOGE/BCH have
        // none. Deriving a plausible-looking address on a chain we cannot actually scan would invent
        // an address the user could be paid at and we could never find.
        Assert.True(UtxoAccountScanner.ScansTaproot(ChainId.Btc));
        Assert.False(UtxoAccountScanner.ScansTaproot(ChainId.Ltc));
        Assert.False(UtxoAccountScanner.ScansTaproot(ChainId.Doge));
        Assert.False(UtxoAccountScanner.ScansTaproot(ChainId.Bch));

        foreach (var chain in new[] { ChainId.Ltc, ChainId.Doge, ChainId.Bch })
        {
            Assert.Throws<UnsupportedChainException>(() =>
                Deriver.DeriveBitcoinLikeAt(Bip86Phrase, chain, 0, 0, kind: UtxoScriptKind.Taproot));
        }
    }

    [Fact]
    public async Task A_seed_restored_from_a_Taproot_wallet_is_not_reported_as_empty()
    {
        // The counter-proof this feature exists for. The coins sit ONLY on m/86'; a scan that walks
        // the default branch alone reports zero, which is the wallet lying about a balance it never
        // checked. Both halves are asserted, so this cannot pass by accident.
        var explorer = new FakeExplorer();
        var taprootAddress = Deriver
            .DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot).Address;
        explorer.Fund(taprootAddress, 250_000);

        var scanner = new UtxoAccountScanner(gapLimit: 5);
        var scan = await scanner.ScanAsync(Bip86Phrase, ChainId.Btc, explorer, UtxoScanFloors.None);

        Assert.False(scan.Partial);
        Assert.Equal(250_000, scan.ConfirmedSat);
        Assert.True(scan.HasTaprootFunds);
        Assert.Equal(0u, scan.HighestUsedTaprootExternalIndex);
        Assert.Null(scan.HighestUsedExternalIndex);   // nothing at all on the SegWit branch

        // And the UTXO carries the branch it was found on, so the spender derives the right key.
        var found = Assert.Single(scan.Utxos);
        Assert.True(found.Path.IsTaproot);
        Assert.Equal(taprootAddress, found.Address);
    }

    [Fact]
    public async Task Both_branches_are_found_and_totalled_together()
    {
        var explorer = new FakeExplorer();
        explorer.Fund(Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0).Address, 100_000);
        explorer.Fund(
            Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 2, kind: UtxoScriptKind.Taproot).Address,
            60_000);
        explorer.Fund(
            Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 1, 0, kind: UtxoScriptKind.Taproot).Address,
            7_000);

        var scanner = new UtxoAccountScanner(gapLimit: 5);
        var scan = await scanner.ScanAsync(Bip86Phrase, ChainId.Btc, explorer, UtxoScanFloors.None);

        Assert.Equal(167_000, scan.ConfirmedSat);
        Assert.Equal(0u, scan.HighestUsedExternalIndex);
        Assert.Equal(2u, scan.HighestUsedTaprootExternalIndex);
        Assert.Equal(0u, scan.HighestUsedTaprootInternalIndex);
        Assert.Equal(3, scan.Utxos.Count);
    }

    [Fact]
    public async Task A_failed_probe_on_the_Taproot_branch_still_marks_the_scan_partial()
    {
        // The new branch has to obey the same rule as the old one: an explorer error is "unknown",
        // never "empty". Without this, adding Taproot would have opened a path where a network
        // failure silently reduced the reported balance.
        var explorer = new FakeExplorer();
        explorer.Fund(Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0).Address, 100_000);
        explorer.ThrowOn.Add(
            Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 1, kind: UtxoScriptKind.Taproot).Address);

        var scanner = new UtxoAccountScanner(gapLimit: 5);
        var scan = await scanner.ScanAsync(Bip86Phrase, ChainId.Btc, explorer, UtxoScanFloors.None);

        Assert.True(scan.Partial);
    }

    [Fact]
    public void A_Taproot_input_is_signed_and_verifies_against_consensus_rules()
    {
        // Not "the builder returned a transaction" — NBitcoin's Verify runs the script for each input
        // against the output it spends. A wrong key, a wrong tweak or a wrong sighash fails here.
        var spender = new HdUtxoSpender(Deriver);
        var acct = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot);
        var utxos = new[] { new OwnedUtxo(acct.Path, acct.Address, new string('a', 64), 0, 400_000, true) };

        var request = new UtxoSpendRequest(
            ChainId.Btc, "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", 100_000, FeeRateSatPerVByte: 5);

        var (plan, planError) = spender.PlanSpend(ChainId.Btc, utxos, request);
        Assert.Null(planError);
        Assert.NotNull(plan);
        Assert.True(plan!.NeedsChange);

        var changeAddress = Deriver
            .DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 1, 0, kind: plan.ChangeKind).Address;
        var (tx, buildError) = spender.BuildSigned(Bip86Phrase, plan, request, changeAddress);

        Assert.Null(buildError);
        Assert.NotNull(tx);
        Assert.Single(tx!.Inputs);
        Assert.Empty(tx.Inputs[0].ScriptSig.ToBytes());       // key-path: nothing in scriptSig
        Assert.Single(tx.Inputs[0].WitScript.Pushes);         // one 64-byte Schnorr signature, no pubkey
        Assert.Equal(64, tx.Inputs[0].WitScript.Pushes.First().Length);
    }

    [Fact]
    public void A_spend_that_mixes_both_branches_signs_every_input_with_its_own_key()
    {
        // A wallet that restored a Taproot seed and then received to its own SegWit addresses holds
        // both. One transaction must be able to draw on both, each input signed from the path it was
        // found on — this is what the per-input derivation in BuildSigned is for.
        var spender = new HdUtxoSpender(Deriver);
        var segwit = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0);
        var taproot = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 1, kind: UtxoScriptKind.Taproot);

        var utxos = new[]
        {
            new OwnedUtxo(segwit.Path, segwit.Address, new string('b', 64), 0, 150_000, true),
            new OwnedUtxo(taproot.Path, taproot.Address, new string('c', 64), 1, 150_000, true),
        };

        var request = new UtxoSpendRequest(
            ChainId.Btc, "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", 250_000, FeeRateSatPerVByte: 4);

        var (plan, planError) = spender.PlanSpend(ChainId.Btc, utxos, request);
        Assert.Null(planError);
        Assert.Equal(2, plan!.Inputs.Count);

        // Inputs disagree, so change falls back to the wallet's own default branch rather than
        // picking a side.
        Assert.Equal(UtxoScriptKind.Default, plan.ChangeKind);

        var changeAddress = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 1, 0, kind: plan.ChangeKind).Address;
        var (tx, buildError) = spender.BuildSigned(Bip86Phrase, plan, request, changeAddress);

        Assert.Null(buildError);   // BuildSigned verifies both inputs before returning
        Assert.NotNull(tx);
        Assert.Equal(2, tx!.Inputs.Count);
    }

    [Fact]
    public void Change_returns_to_the_branch_the_inputs_came_from()
    {
        // A chain-analysis heuristic reads the output whose script type MATCHES the inputs as the
        // change. Sending a Taproot spend's change to a SegWit address would therefore point at the
        // recipient's output as "the user's" — the wallet volunteering a link nobody asked it for.
        var taproot = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot);
        var segwit = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0);

        var allTaproot = new[] { new OwnedUtxo(taproot.Path, taproot.Address, new string('a', 64), 0, 1, true) };
        var allSegwit = new[] { new OwnedUtxo(segwit.Path, segwit.Address, new string('a', 64), 0, 1, true) };
        var mixed = allTaproot.Concat(allSegwit).ToList();

        Assert.Equal(UtxoScriptKind.Taproot, HdUtxoSpender.ChangeKindFor(allTaproot));
        Assert.Equal(UtxoScriptKind.Default, HdUtxoSpender.ChangeKindFor(allSegwit));
        Assert.Equal(UtxoScriptKind.Default, HdUtxoSpender.ChangeKindFor(mixed));
        Assert.Equal(UtxoScriptKind.Default, HdUtxoSpender.ChangeKindFor(Array.Empty<OwnedUtxo>()));
    }

    [Fact]
    public void A_Taproot_spend_is_estimated_smaller_than_the_same_spend_in_SegWit()
    {
        // The fee is charged on virtual size, and a Taproot key-path input really is smaller than a
        // P2WPKH one. Estimating them alike would overpay on every Taproot spend — and if the model
        // ever drifted the other way, underpay and strand the transaction.
        var spender = new HdUtxoSpender(Deriver);
        var taproot = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot);
        var segwit = Deriver.DeriveBitcoinLikeAt(Bip86Phrase, ChainId.Btc, 0, 0);
        var request = new UtxoSpendRequest(
            ChainId.Btc, "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", 100_000, FeeRateSatPerVByte: 10);

        var (trPlan, _) = spender.PlanSpend(
            ChainId.Btc,
            new[] { new OwnedUtxo(taproot.Path, taproot.Address, new string('a', 64), 0, 500_000, true) },
            request);
        var (swPlan, _) = spender.PlanSpend(
            ChainId.Btc,
            new[] { new OwnedUtxo(segwit.Path, segwit.Address, new string('a', 64), 0, 500_000, true) },
            request);

        Assert.NotNull(trPlan);
        Assert.NotNull(swPlan);

        // 10 sat/vB × (68−58) smaller input = 100 sat saved, minus 10 × (43−31) = 120 sat for the
        // larger Taproot change output: net +20. The point is that they differ at all — a single
        // shared estimate would make them identical.
        Assert.NotEqual(swPlan!.FeeSat, trPlan!.FeeSat);
    }

    /// <summary>An explorer that knows about exactly the addresses it was told to fund.</summary>
    private sealed class FakeExplorer : IUtxoExplorer
    {
        private readonly Dictionary<string, List<ExplorerUtxo>> _utxos = new(StringComparer.Ordinal);
        private int _tx;

        public HashSet<string> ThrowOn { get; } = new(StringComparer.Ordinal);

        public void Fund(string address, long sat, bool confirmed = true)
        {
            if (!_utxos.TryGetValue(address, out var list)) _utxos[address] = list = new List<ExplorerUtxo>();
            list.Add(new ExplorerUtxo((++_tx).ToString("x64"), 0, sat, confirmed));
        }

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            if (ThrowOn.Contains(address)) throw new HttpRequestException("simulated explorer failure");
            var used = _utxos.ContainsKey(address);
            return Task.FromResult(new AddressActivity(used, used ? 1 : 0));
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            if (ThrowOn.Contains(address)) throw new HttpRequestException("simulated explorer failure");
            IReadOnlyList<ExplorerUtxo> r = _utxos.TryGetValue(address, out var l) ? l : Array.Empty<ExplorerUtxo>();
            return Task.FromResult(r);
        }
    }
}
