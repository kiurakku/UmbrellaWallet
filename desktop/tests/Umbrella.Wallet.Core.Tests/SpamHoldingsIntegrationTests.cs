using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The spam heuristic is only worth anything once it reaches the list the user reads. This covers the
/// wiring the detector's own unit tests cannot: that flagged rows leave Holdings, that the user is
/// always told how many were folded away, that one toggle brings them back, and — the part that keeps
/// it honest — that nothing is ever removed from the account list itself.
/// </summary>
public sealed class SpamHoldingsIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-spam-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    private static WalletAccountViewModel Token(string symbol, string name, double price, bool spam) =>
        new(symbol, name, "Ready", "TXaddress", "TRC20 on TRON", price, 100, "TRON", 0, IsSuspectedSpam: spam);

    /// <summary>Rebuilds Holdings through a public command (RefreshHoldings itself is private).</summary>
    private static void Rebuild(MainViewModel vm) => vm.SetHoldingsSortCommand.Execute("Name");

    [Fact]
    public void A_flagged_airdrop_is_folded_out_of_holdings_but_kept_in_accounts()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Token("USDT", "Tether USD · TRC20", 1.0, spam: false));
        vm.Accounts.Add(Token("HA138", "Hash gambling at Ha138Com · TRC20", 0.0, spam: true));

        Rebuild(vm);

        Assert.DoesNotContain(vm.Holdings, h => h.Symbol == "HA138");
        Assert.Contains(vm.Holdings, h => h.Symbol == "USDT");

        // Never deleted — the row is still there, just not presented as an asset.
        Assert.Contains(vm.Accounts, a => a.Symbol == "HA138");
    }

    [Fact]
    public void The_user_is_told_how_many_were_hidden()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Token("USDT", "Tether USD · TRC20", 1.0, spam: false));
        vm.Accounts.Add(Token("HA138", "Hash gambling at Ha138Com · TRC20", 0.0, spam: true));
        vm.Accounts.Add(Token("FREE", "Visit free-x.top to claim · TRC20", 0.0, spam: true));

        Rebuild(vm);

        Assert.Equal(2, vm.SpamTokenCount);
        Assert.True(vm.HasSpamTokens);
        Assert.Contains("2", vm.SpamTokenNotice);
    }

    [Fact]
    public void Toggling_brings_them_back_and_folds_them_away_again()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Token("USDT", "Tether USD · TRC20", 1.0, spam: false));
        vm.Accounts.Add(Token("HA138", "Hash gambling at Ha138Com · TRC20", 0.0, spam: true));

        Rebuild(vm);
        Assert.DoesNotContain(vm.Holdings, h => h.Symbol == "HA138");

        vm.ToggleSpamTokensCommand.Execute(null);
        Assert.Contains(vm.Holdings, h => h.Symbol == "HA138");

        vm.ToggleSpamTokensCommand.Execute(null);
        Assert.DoesNotContain(vm.Holdings, h => h.Symbol == "HA138");
    }

    [Fact]
    public void Nothing_is_reported_as_hidden_when_there_is_no_spam()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Token("USDT", "Tether USD · TRC20", 1.0, spam: false));

        Rebuild(vm);

        Assert.Equal(0, vm.SpamTokenCount);
        Assert.False(vm.HasSpamTokens);
    }

    [Fact]
    public void The_count_reflects_the_current_view_not_the_whole_wallet()
    {
        // Looking at a Bitcoin-only view, a notice saying "1 hidden" would be a lie: the TRON airdrop
        // was never part of that view to begin with.
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(new WalletAccountViewModel(
            "BTC", "Bitcoin", "Ready", "bc1q", "BIP84", 50_000, 1, "Bitcoin", 0));
        vm.Accounts.Add(Token("HA138", "Hash gambling at Ha138Com · TRC20", 0.0, spam: true));

        vm.SetChainFilterCommand.Execute("Bitcoin");

        Assert.Equal(0, vm.SpamTokenCount);
        Assert.False(vm.HasSpamTokens);

        vm.SetChainFilterCommand.Execute("All");
        Assert.Equal(1, vm.SpamTokenCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
