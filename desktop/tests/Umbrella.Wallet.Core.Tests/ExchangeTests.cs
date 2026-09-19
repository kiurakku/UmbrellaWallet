using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Exchange balances must behave like any other holding — visible AND counted in the total.
/// The whole point of connecting an exchange is that its funds stop being invisible.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ExchangeTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";
    private const string Mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-exch-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    [Fact]
    public async Task Exchange_rows_are_counted_in_the_total()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.Accounts.Add(new WalletAccountViewModel(
            "BTC", "Binance · BTC", "Exchange", "Binance", "Binance",
            Price: 60000, Amount: 0.25, Chain: "Binance", Change24h: 0,
            // The row stands in for one built from a successful exchange read, so it carries a
            // live reading — an unread row is deliberately excluded from the total (P0.6).
            Balance: Umbrella.Wallet.App.BalanceRead.Live));

        vm.RecomputeHoldingsForTest();

        var row = vm.Holdings.FirstOrDefault(h => h.SupportStatus == "Exchange");
        Assert.NotNull(row);
        Assert.Equal(15000, row!.Value);
    }

    [Fact]
    public async Task Credentials_round_trip_encrypted_and_are_unreadable_with_another_seed()
    {
        var store = new ExchangeCredentialStore(Path.Combine(_directory, "exchanges.bin"));
        var credentials = new List<ExchangeCredential>
        {
            new("Binance", "Main", "key-abc", "secret-xyz", null),
            new("OKX", "OKX", "okx-key", "okx-secret", "pass"),
        };

        await store.SaveAsync(credentials, Mnemonic);

        // The secret must not be recoverable by reading the file.
        var raw = await File.ReadAllBytesAsync(Path.Combine(_directory, "exchanges.bin"));
        Assert.DoesNotContain("secret-xyz", System.Text.Encoding.UTF8.GetString(raw));

        var loaded = await store.LoadAsync(Mnemonic);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("secret-xyz", loaded[0].ApiSecret);
        Assert.Equal("pass", loaded[1].Passphrase);

        // A different seed must not decrypt them (AES-GCM auth fails → treated as "none").
        const string otherSeed =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        Assert.NotEqual(Mnemonic, otherSeed);
        Assert.Empty(await store.LoadAsync(otherSeed));
    }

    [Theory]
    [InlineData("OKX", true)]
    [InlineData("KuCoin", true)]
    [InlineData("Bitget", true)]
    [InlineData("Binance", false)]
    [InlineData("Bybit", false)]
    [InlineData("Kraken", false)]
    [InlineData("Gate.io", false)]
    [InlineData("MEXC", false)]
    public void Passphrase_is_required_only_where_the_venue_issues_one(string exchange, bool needed)
    {
        Assert.Equal(needed, ExchangeConnectors.RequiresPassphrase(exchange));
    }

    [Fact]
    public void CryptoBot_is_the_only_venue_authenticated_by_a_bare_token()
    {
        // Asking for a secret it doesn't have would just block the user on a field they can't fill.
        Assert.False(ExchangeConnectors.RequiresSecret("Telegram CryptoBot"));
        Assert.True(ExchangeConnectors.RequiresSecret("Binance"));
        Assert.True(ExchangeConnectors.RequiresSecret("Kraken"));
    }

    [Fact]
    public void Every_supported_venue_is_routed_and_documented()
    {
        Assert.Contains("Kraken", ExchangeConnectors.Supported);
        Assert.Contains("Telegram CryptoBot", ExchangeConnectors.Supported);

        foreach (var exchange in ExchangeConnectors.Supported)
        {
            // A venue in the list with no key hint would drop the user into a blank form.
            Assert.False(string.IsNullOrWhiteSpace(ExchangeConnectors.KeyHint(exchange)), exchange);
        }
    }

    [Fact]
    public async Task Unsupported_exchange_is_refused_rather_than_silently_empty()
    {
        var result = await ExchangeConnectors.FetchBalancesAsync("Coinbase", "k", "s", null);
        Assert.False(result.Ok);
        Assert.Contains("Unsupported", result.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
