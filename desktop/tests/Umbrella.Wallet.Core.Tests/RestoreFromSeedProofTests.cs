using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P0.0 — the restore proof. The claim a self-custody wallet lives or dies on is that the
/// recovery phrase alone is enough: lose the machine, the vault file, the address indexes, and the
/// money is still reachable. Everything else in this repository is a convenience by comparison.
///
/// So the scenario is run end to end, offline: a wallet hands out twenty receive addresses through
/// the real <see cref="AddressIndexStore"/>, the money arrives on the sixteenth of them (external
/// #15), every trace of local state is deleted, and the wallet is rebuilt from the phrase alone. It
/// must find the full balance by gap-scanning — and then sign a spend of it, because an address the
/// wallet can see but not spend from is money the user watches on an explorer and cannot move
/// (MANIFESTO §6).
///
/// The last test here is the honest counterweight: beyond the gap limit, a stateless scan genuinely
/// does not find funds, and the stored floor is what recovers them. Documenting the limit is the
/// point — a restore proof that quietly passed in a case where a real user would lose money would be
/// worse than no proof at all.
/// </summary>
public sealed class RestoreFromSeedProofTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    /// <summary>The index the money lands on: far enough in to be a real rotation, inside the gap.</summary>
    private const uint FundedIndex = 15;

    private const long FundedSat = 5_000_000;

    private static readonly HdAddressDeriver Deriver = new();

    /// <summary>Every chain whose balance comes from a full HD scan — the ones that issue fresh
    /// receive addresses, and therefore the ones this proof has to hold for.</summary>
    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Bch)]
    [InlineData(ChainId.Doge)]
    public async Task Funds_on_the_sixteenth_issued_address_survive_losing_every_local_file(ChainId chain)
    {
        var symbol = chain.ToString().ToUpperInvariant();
        var statePath = TempPath();

        try
        {
            // 1. The wallet issues twenty receive addresses. Index 0 is implicit, so this reserves
            //    #1..#20 and persists each one BEFORE it could be handed out.
            var store = new AddressIndexStore(statePath);
            for (var i = 0; i < 20; i++) store.ReserveNextExternalIndex("wallet-1", symbol);
            Assert.Equal(20u, store.GetState("wallet-1", symbol).LastIssuedExternalIndex);

            // 2. Somebody pays the address at external #15. Nothing else is funded.
            var funded = Deriver.DeriveBitcoinLikeAt(Phrase, chain, change: 0, index: FundedIndex).Address;
            var explorer = new FakeExplorer();
            explorer.Fund(funded, FundedSat);

            // 3. The machine is gone: the address-index file goes with it.
            File.Delete(statePath);
            Assert.False(File.Exists(statePath));
            var restoredState = new AddressIndexStore(statePath).GetState("wallet-1", symbol);
            Assert.Null(restoredState.LastIssuedExternalIndex);
            Assert.Null(restoredState.LastSeenUsedExternalIndex);

            // 4. Restore from the phrase alone — no floors, production gap limit.
            var scanner = new UtxoAccountScanner(Deriver);
            var scan = await scanner.ScanAsync(Phrase, chain, explorer, UtxoScanFloors.None);

            Assert.False(scan.Partial);                       // a full answer, not a degraded one
            Assert.Equal(FundedSat, scan.TotalSat);           // the whole balance, not the first address
            Assert.Equal(FundedIndex, scan.HighestUsedExternalIndex);
            var found = Assert.Single(scan.Utxos);
            Assert.Equal(FundedIndex, found.Path.Index);
            Assert.False(found.Path.IsChange);
        }
        finally
        {
            Delete(statePath);
        }
    }

    /// <summary>
    /// Finding the money is half of it. The restored wallet must also be able to derive the key for
    /// the address it discovered and sign a spend from it — that is the cardinal rule of this wallet,
    /// and it is the half that silently breaks when a derivation path is changed.
    /// </summary>
    [Fact]
    public async Task A_restored_wallet_can_also_spend_what_it_rediscovered()
    {
        var explorer = new FakeExplorer();
        var funded = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, FundedIndex).Address;
        explorer.Fund(funded, FundedSat);

        var scan = await new UtxoAccountScanner(Deriver)
            .ScanAsync(Phrase, ChainId.Btc, explorer, UtxoScanFloors.None);

        // BIP173's example address — a real mainnet P2WPKH that is not ours.
        const string destination = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";
        var spender = new HdUtxoSpender(Deriver);
        var request = new UtxoSpendRequest(ChainId.Btc, destination, AmountSat: 1_000_000, FeeRateSatPerVByte: 2);

        var (plan, planError) = spender.PlanSpend(ChainId.Btc, scan.Utxos, request);
        Assert.Null(planError);
        Assert.NotNull(plan);

        // The input is the rediscovered one: index #15 on the external chain.
        var input = Assert.Single(plan!.Inputs);
        Assert.Equal(FundedIndex, input.Path.Index);

        // Change goes to a fresh internal address, as it must — never back to the funded one.
        var changeAddress = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 1, index: 0).Address;
        var (tx, buildError) = spender.BuildSigned(Phrase, plan, request, changeAddress);

        Assert.Null(buildError);
        Assert.NotNull(tx);   // BuildSigned runs NBitcoin's own Verify(): the signature is real

        var toScript = BitcoinAddress.Create(destination, Network.Main).ScriptPubKey;
        var paid = Assert.Single(tx!.Outputs, o => o.ScriptPubKey == toScript);
        Assert.Equal(1_000_000, paid.Value.Satoshi);
    }

    /// <summary>
    /// The limit, stated rather than hidden. Money parked past the gap limit is NOT found by a
    /// stateless scan — twenty empty addresses in a row is where BIP44 says to stop, and no wallet
    /// walks the chain forever. What recovers it is the wallet's own record of how far it had issued,
    /// which is why that record is written before an address is ever shown.
    ///
    /// This is also the probe that keeps the test above meaningful: if the scanner ever started
    /// finding everything regardless, this case would fail and say so.
    /// </summary>
    [Fact]
    public async Task Past_the_gap_limit_the_stored_issue_floor_is_what_saves_the_funds()
    {
        const uint farIndex = 40;
        var explorer = new FakeExplorer();
        explorer.Fund(Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, farIndex).Address, FundedSat);

        var scanner = new UtxoAccountScanner(Deriver);

        var blind = await scanner.ScanAsync(Phrase, ChainId.Btc, explorer, UtxoScanFloors.None);
        Assert.Equal(0, blind.TotalSat);          // honestly unreachable from the phrase alone

        var withFloor = await scanner.ScanAsync(
            Phrase, ChainId.Btc, explorer, new UtxoScanFloors(farIndex, null, null, null));
        Assert.Equal(FundedSat, withFloor.TotalSat);
        Assert.Equal(farIndex, withFloor.HighestUsedExternalIndex);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"umbrella-restore-{Guid.NewGuid():N}.json");

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private sealed class FakeExplorer : IUtxoExplorer
    {
        private readonly Dictionary<string, List<ExplorerUtxo>> _utxos = new();
        private int _txCounter;

        public void Fund(string address, long sat)
        {
            if (!_utxos.TryGetValue(address, out var list))
            {
                list = new List<ExplorerUtxo>();
                _utxos[address] = list;
            }

            list.Add(new ExplorerUtxo((++_txCounter).ToString("x64"), 0, sat, true));
        }

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            var used = _utxos.ContainsKey(address);
            return Task.FromResult(new AddressActivity(used, used ? 1 : 0));
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            IReadOnlyList<ExplorerUtxo> r = _utxos.TryGetValue(address, out var l) ? l : Array.Empty<ExplorerUtxo>();
            return Task.FromResult(r);
        }
    }
}
