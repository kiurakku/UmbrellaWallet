using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Proves the address-poisoning / own-address scam check is actually wired into the Send screen —
/// typing a destination runs it and raises the banner state. The rules themselves are pinned in
/// <see cref="AddressSafetyTests"/>; this is the integration.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SendSafetyIntegrationTests : IDisposable
{
    private const string Password = "umbrella-safety-2026";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-safety-{Guid.NewGuid():N}");

    public SendSafetyIntegrationTests()
    {
        // The address book is device-global (AppPaths.DataRoot), so a contact saved by one test would
        // leak into the next run's "fresh" wallet. Clear it before each test so the safety checks see
        // exactly the history the test sets up.
        new AddressBookStore().Save(Array.Empty<AddressBookEntry>());
    }

    private async Task<MainViewModel> UnlockedAsync()
    {
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        return vm;
    }

    private static string OwnAddress(MainViewModel vm) =>
        vm.Accounts.First(a => a.Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)).Address;

    [Fact]
    public async Task Sending_to_your_own_address_raises_the_safety_banner()
    {
        var vm = await UnlockedAsync();
        var mine = OwnAddress(vm);

        vm.SendTo = mine;

        Assert.True(vm.SendSafetyShown);
        Assert.False(string.IsNullOrWhiteSpace(vm.SendSafetyTitle));
    }

    [Fact]
    public async Task A_lookalike_of_your_own_address_is_flagged_as_poisoning()
    {
        var vm = await UnlockedAsync();
        var mine = OwnAddress(vm);

        // Craft a poisoned twin: same first 6 and last 5 characters, different middle, same length.
        var twin = mine[..6] + new string('0', mine.Length - 11) + mine[^5..];
        Assert.NotEqual(mine, twin);

        vm.SendTo = twin;

        Assert.True(vm.SendSafetyShown);
        Assert.Contains("…", vm.SendSafetyText); // the shortened similar address is named
    }

    [Fact]
    public async Task A_normal_new_destination_does_not_raise_the_banner()
    {
        var vm = await UnlockedAsync();

        vm.SendTo = "0x1111111111111111111111111111111111111111";

        Assert.False(vm.SendSafetyShown);
    }

    [Fact]
    public async Task Clearing_the_destination_clears_the_banner()
    {
        var vm = await UnlockedAsync();
        vm.SendTo = OwnAddress(vm);
        Assert.True(vm.SendSafetyShown);

        vm.SendTo = string.Empty;

        Assert.False(vm.SendSafetyShown);
    }

    [Fact]
    public async Task A_first_time_note_shows_only_once_you_have_history_to_compare_against()
    {
        var vm = await UnlockedAsync();
        vm.SelectedSendAsset = vm.SendableAssets.First(o => o.Symbol == "ETH");

        // Fresh wallet, no contacts: a valid new address is not annotated (would be noise).
        vm.SendTo = "0x2222222222222222222222222222222222222222";
        Assert.False(vm.SendSafetyShown);

        // Save a contact, then send to a DIFFERENT valid address -> first-time note appears.
        vm.SendTo = "0x1111111111111111111111111111111111111111";
        vm.SaveSendAddressCommand.Execute(null);
        vm.SendTo = "0x2222222222222222222222222222222222222222";

        Assert.True(vm.SendSafetyShown);
        Assert.Equal(Umbrella.Wallet.App.Loc.Instance["send.firstTitle"], vm.SendSafetyTitle);
    }

    [Fact]
    public async Task A_saved_contact_shows_the_positive_trust_badge_with_its_label()
    {
        var vm = await UnlockedAsync();
        vm.SelectedSendAsset = vm.SendableAssets.First(o => o.Symbol == "ETH");
        const string addr = "0x3333333333333333333333333333333333333333";

        // Save it as a named contact.
        vm.SendAddressLabel = "MyExchange";
        vm.SendTo = addr;
        vm.SaveSendAddressCommand.Execute(null);

        // Re-enter the same address so the safety check runs against the now-known contact.
        vm.SendTo = string.Empty;
        vm.SendTo = addr;

        Assert.True(vm.SendKnownContactShown);
        Assert.Contains("MyExchange", vm.SendKnownContact);
        Assert.False(vm.SendSafetyShown); // a trusted address is not also a warning
    }

    public void Dispose()
    {
        try { new AddressBookStore().Save(Array.Empty<AddressBookEntry>()); } catch { }
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }
}
