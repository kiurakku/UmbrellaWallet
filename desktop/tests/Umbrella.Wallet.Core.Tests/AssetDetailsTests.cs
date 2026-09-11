using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The asset page must describe the coin the wallet really has: capabilities come from the chain
/// catalog (the same source the Send picker reads), and an action is only offered when there is an
/// account behind it. A coin the wallet knows nothing about still opens, saying exactly that.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class AssetDetailsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-asset-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    [Fact]
    public void Opening_a_coin_switches_to_the_asset_page_and_names_it()
    {
        var vm = NewViewModel();

        vm.OpenAssetDetailsCommand.Execute("btc");

        Assert.True(vm.IsAsset);
        Assert.Equal("BTC", vm.AssetSymbol);
        Assert.Equal("Bitcoin", vm.AssetName);
        Assert.Contains("Bitcoin", vm.AssetNetwork);
    }

    /// <summary>The capability line is read from ChainCatalog, so it cannot drift from the truth.</summary>
    [Fact]
    public void Capabilities_come_from_the_chain_catalog()
    {
        var vm = NewViewModel();

        vm.OpenAssetDetailsCommand.Execute("BTC");

        Assert.Contains(Loc.Instance["asset.capReceive"], vm.AssetCapabilityLine);
        Assert.Contains(Loc.Instance["asset.capSend"], vm.AssetCapabilityLine);
        Assert.Contains(Loc.Instance["asset.capSwap"], vm.AssetCapabilityLine);
        Assert.False(string.IsNullOrWhiteSpace(vm.AssetPrivacyNote));
    }

    /// <summary>Monero is receive-only here; the page must not offer a swap for it.</summary>
    [Fact]
    public void A_receive_only_coin_does_not_advertise_a_swap()
    {
        var vm = NewViewModel();

        vm.OpenAssetDetailsCommand.Execute("XMR");

        Assert.False(vm.AssetCanSwap);
        Assert.DoesNotContain(Loc.Instance["asset.capSwap"], vm.AssetCapabilityLine);
    }

    /// <summary>
    /// While the vault is locked the account rows are placeholders, not addresses. Offering Send from
    /// one of those would promise something the wallet cannot do, so every action stays off.
    /// </summary>
    [Fact]
    public void A_placeholder_account_offers_no_address_and_no_send_action()
    {
        var vm = NewViewModel();

        vm.OpenAssetDetailsCommand.Execute("BTC");

        Assert.False(vm.IsUnlocked);
        Assert.False(vm.AssetHasAddress);
        Assert.False(vm.AssetCanSend);
        Assert.False(vm.AssetCanSwap);
    }

    /// <summary>A ticker the wallet has no chain for says so instead of inventing capabilities.</summary>
    [Fact]
    public void An_unknown_ticker_says_it_is_not_part_of_this_wallet()
    {
        var vm = NewViewModel();

        vm.OpenAssetDetailsCommand.Execute("ZZZ");

        Assert.True(vm.IsAsset);
        Assert.Equal(Loc.Instance["asset.capNone"], vm.AssetCapabilityLine);
        Assert.False(vm.AssetCanSend);
        Assert.False(vm.AssetHasHistory);
        Assert.Empty(vm.AssetPrivacyNote);
    }

    [Fact]
    public void An_empty_symbol_is_ignored()
    {
        var vm = NewViewModel();
        var section = vm.ActiveSection;

        vm.OpenAssetDetailsCommand.Execute("   ");

        Assert.Equal(section, vm.ActiveSection);
        Assert.False(vm.IsAsset);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }
}
