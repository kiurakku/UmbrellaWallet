using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The Security Center: the live report of what is protecting this wallet.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // ================= SECURITY CENTER =================
    // One screen that answers "what is actually protecting this wallet right now?". Every row is
    // computed from live settings and services — nothing here is decorative, and a protection that is
    // off says so plainly instead of being omitted (roadmap §7: the user must see the privacy state).

    public ObservableCollection<SecurityCheckVm> SecurityChecks { get; } = [];

    /// <summary>e.g. "5 of 7 protections active" — only the rows the user can actually turn on count.</summary>
    [ObservableProperty] private string _securityScoreLabel = string.Empty;
    [ObservableProperty] private string _securityScoreColor = "#8FCB9B";
    [ObservableProperty] private int _securityScoreDone;
    [ObservableProperty] private int _securityScoreTotal;

    private const string SecGood = "#8FCB9B";
    private const string SecWarn = "#E7CA83";
    private const string SecInfo = "#8B909A";

    /// <summary>
    /// Rebuilds the Security Center from live state. Rows that are always true (the vault cipher, the
    /// seed's entropy source, no telemetry) are reported as facts and are NOT scored — the score only
    /// counts protections the user can switch on, so it can never be padded to look better.
    /// </summary>
    private void RefreshSecurityChecks()
    {
        var L = Loc.Instance;
        SecurityChecks.Clear();

        string On() => L["sec.on"];
        string Off() => L["sec.off"];
        string Fact() => L["sec.fact"];

        var scored = 0;
        var good = 0;

        void Scored(string glyph, string title, string detail, bool ok, string actionLabel = "", string target = "")
        {
            scored++;
            if (ok) good++;
            SecurityChecks.Add(new SecurityCheckVm(
                glyph, title, detail, ok ? On() : Off(), ok ? SecGood : SecWarn, ok,
                ok ? string.Empty : actionLabel, ok ? string.Empty : target));
        }

        void Note(string glyph, string title, string detail, string actionLabel = "", string target = "")
            => SecurityChecks.Add(new SecurityCheckVm(
                glyph, title, detail, Fact(), SecInfo, true, actionLabel, target));

        // --- Keys ---
        Note("🔐", L["sec.vault"], VaultCryptoLabel);
        Note("🎲", L["sec.seed"], SeedSchemeLabel);

        // --- Network ---
        Scored("🧅", L["sec.tor"], TorEnabled ? L["sec.torOnBody"] : L["sec.torOffBody"],
            TorEnabled, L["sec.openSettings"], "Settings");
        Scored("⛔", L["sec.kill"], TorOnlyStatus, TorOnly, L["sec.openSettings"], "Settings");

        if (CustomProxyEnabled && !string.IsNullOrWhiteSpace(CustomProxyUri))
            Note("🛰", L["sec.proxy"], CustomProxyUri);
        else
            Note("🛰", L["sec.proxy"], L["sec.proxyOffBody"]);

        Scored("📈", L["sec.market"], RichMarketData ? L["sec.marketOnBody"] : L["sec.marketOffBody"],
            !RichMarketData, L["sec.openSettings"], "Settings");
        Note("📡", L["sec.telemetry"], L["sec.telemetryBody"]);

        // --- This device ---
        Scored("⏱", L["sec.autolock"],
            AutoLockMinutes > 0
                ? string.Format(L["sec.autoLockOnBody"], AutoLockDurationLabel)
                : L["sec.autoLockOffBody"],
            AutoLockMinutes > 0, L["sec.openSettings"], "Settings");

        Scored("🗕", L["sec.minimize"],
            LockOnMinimize ? L["sec.minimizeOnBody"] : L["sec.minimizeOffBody"],
            LockOnMinimize, L["sec.openSettings"], "Settings");

        var clipSeconds = ClipboardAutoClearSeconds;
        Scored("📋", L["sec.clip"],
            clipSeconds > 0
                ? string.Format(L["sec.clipOnBody"], ClipboardClearChoice)
                : L["sec.clipOffBody"],
            clipSeconds > 0, L["sec.openSettings"], "Settings");

        // Capture blocking is a Windows display-affinity feature; elsewhere it is honestly unavailable
        // rather than quietly claimed. Not scored: the user cannot switch it on.
        Note("🚫", L["sec.capture"],
            OperatingSystem.IsWindows() ? L["sec.captureOnBody"] : L["sec.captureOffBody"]);

        // --- On-chain privacy ---
        Note("🔀", L["sec.rotate"], L["sec.rotateBody"], L["sec.openReceive"], "Receive");

        // --- Recovery & trust ---
        Note("💾", L["sec.backup"], L["sec.backupBody"], L["sec.openSettings"], "Settings");
        Note("🧾", L["sec.verify"], L["sec.verifyBody"], L["sec.openGuide"],
            "https://github.com/kiurakku/umbrella-wallet/blob/main/docs/BUILD_VERIFY.md");

        SecurityScoreDone = good;
        SecurityScoreTotal = scored;
        SecurityScoreLabel = string.Format(L["sec.scoreFmt"], good, scored);
        SecurityScoreColor = good == scored ? SecGood : good * 2 >= scored ? SecWarn : "#E09A9A";
    }

    /// <summary>Follows a Security Center row: a section name navigates, an https link opens outside.</summary>
    [RelayCommand]
    private void OpenSecurityAction(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            OpenUrl(target);
            return;
        }

        SelectSection(target);
    }

    /// <summary>Re-runs the checks after the user changes something (the button on the page).</summary>
    [RelayCommand]
    private void RefreshSecurity() => RefreshSecurityChecks();
}
