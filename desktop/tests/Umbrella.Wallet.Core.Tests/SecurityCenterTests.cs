using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Security Center is a mirror, not a reassurance: every row has to follow live wallet state, a
/// protection that is off must say so and offer the remedy, and the score must not be padded with
/// facts the user cannot switch on.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SecurityCenterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-sec-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel()
    {
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
        vm.SelectSectionCommand.Execute("Security");
        return vm;
    }

    [Fact]
    public void Opening_the_section_builds_the_checks()
    {
        var vm = NewViewModel();

        Assert.True(vm.IsSecurity);
        Assert.NotEmpty(vm.SecurityChecks);
        Assert.All(vm.SecurityChecks, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.Detail));
            Assert.False(string.IsNullOrWhiteSpace(c.StateLabel));
        });
    }

    /// <summary>The score counts only switchable protections — never the always-on facts.</summary>
    [Fact]
    public void The_score_counts_only_switchable_protections()
    {
        var vm = NewViewModel();
        var on = Loc.Instance["sec.on"];
        var off = Loc.Instance["sec.off"];

        var scored = vm.SecurityChecks.Count(c => c.StateLabel == on || c.StateLabel == off);
        var active = vm.SecurityChecks.Count(c => c.StateLabel == on);

        Assert.Equal(scored, vm.SecurityScoreTotal);
        Assert.Equal(active, vm.SecurityScoreDone);
        Assert.True(vm.SecurityScoreDone <= vm.SecurityScoreTotal);
        Assert.Contains(vm.SecurityScoreTotal.ToString(), vm.SecurityScoreLabel);
    }

    /// <summary>A protection that is off is useless without a way to switch it on.</summary>
    [Fact]
    public void Every_off_row_offers_a_remedy()
    {
        var vm = NewViewModel();
        var off = Loc.Instance["sec.off"];

        Assert.All(vm.SecurityChecks.Where(c => c.StateLabel == off), c =>
        {
            Assert.False(c.IsGood);
            Assert.False(string.IsNullOrWhiteSpace(c.ActionLabel));
            Assert.False(string.IsNullOrWhiteSpace(c.ActionTarget));
        });
    }

    /// <summary>The rows must follow the real settings rather than describing an ideal wallet.</summary>
    [Fact]
    public void Rows_report_the_live_setting_not_a_wish()
    {
        var vm = NewViewModel();
        var on = Loc.Instance["sec.on"];

        var autoLock = vm.SecurityChecks.Single(c => c.Title == Loc.Instance["sec.autolock"]);
        Assert.Equal(vm.AutoLockMinutes > 0, autoLock.StateLabel == on);

        var tor = vm.SecurityChecks.Single(c => c.Title == Loc.Instance["sec.tor"]);
        Assert.Equal(vm.TorEnabled, tor.StateLabel == on);

        var killSwitch = vm.SecurityChecks.Single(c => c.Title == Loc.Instance["sec.kill"]);
        Assert.Equal(vm.TorOnly, killSwitch.StateLabel == on);

        var clipboard = vm.SecurityChecks.Single(c => c.Title == Loc.Instance["sec.clip"]);
        Assert.Equal(vm.ClipboardAutoClearSeconds > 0, clipboard.StateLabel == on);
    }

    /// <summary>Re-checking must not duplicate rows (the page can be refreshed any number of times).</summary>
    [Fact]
    public void Re_checking_rebuilds_rather_than_appends()
    {
        var vm = NewViewModel();
        var first = vm.SecurityChecks.Count;

        vm.RefreshSecurityCommand.Execute(null);
        vm.RefreshSecurityCommand.Execute(null);

        Assert.Equal(first, vm.SecurityChecks.Count);
    }

    /// <summary>The palette can reach the page, so Ctrl+K → "security" is a real route.</summary>
    [Fact]
    public void The_command_palette_can_reach_the_security_center()
    {
        var vm = NewViewModel();
        vm.OpenCommandPaletteCommand.Execute(null);
        vm.CommandQuery = "secur";

        Assert.Contains(vm.CommandResults, c => c.Target == "Security");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }
}
