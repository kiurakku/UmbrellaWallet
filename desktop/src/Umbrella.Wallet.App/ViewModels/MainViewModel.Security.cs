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
using Umbrella.Wallet.Core.Safety;
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

    // --- Privacy Radar: a privacy-WEIGHTED headline over the wallet's live network/metadata settings
    // (Tor is the single biggest lever, not one row among many). Distinct from the protection COUNT above
    // — it answers "how anonymous am I?", the flagship differentiator, and names the single biggest win. ---
    [ObservableProperty] private int _privacyScoreValue;
    [ObservableProperty] private string _privacyScoreLabel = string.Empty; // "35 / 100"
    [ObservableProperty] private string _privacyGradeLabel = string.Empty; // Strong / Moderate / Exposed
    [ObservableProperty] private string _privacyGradeColor = "#8FCB9B";
    [ObservableProperty] private string _privacyTopFix = string.Empty;

    /// <summary>
    /// Every signal behind the grade, each with what it does NOT do underneath it (roadmap P1.11).
    ///
    /// A grade on its own is the kind of reassurance MANIFESTO §2 exists to forbid: "Strong" reads as
    /// "I am anonymous", when what it means is that the servers being asked see an exit node instead
    /// of a home address — and nothing at all about the addresses those servers were handed, which
    /// stay on the chain forever.
    /// </summary>
    public ObservableCollection<PrivacyFindingVm> PrivacyFindings { get; } = [];

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
            "https://github.com/thefear078/UmbrellaWallet/blob/main/docs/BUILD_VERIFY.md");

        SecurityScoreDone = good;
        SecurityScoreTotal = scored;
        SecurityScoreLabel = string.Format(L["sec.scoreFmt"], good, scored);
        SecurityScoreColor = good == scored ? SecGood : good * 2 >= scored ? SecWarn : "#E09A9A";

        // Privacy Radar headline: grade the network/metadata posture (Tor-weighted), from the same live
        // settings, and name the single biggest win. Pure inspector — this screen stays a mirror.
        var privacy = PrivacyScoreInspector.Evaluate(new PrivacySignals(TorEnabled, TorOnly, RichMarketData));
        PrivacyScoreValue = privacy.Value;
        PrivacyScoreLabel = string.Format(L["pscore.of100"], privacy.Value);
        PrivacyGradeLabel = L["pscore.grade." + privacy.Grade];
        PrivacyGradeColor = privacy.Grade switch
        {
            PrivacyGrade.Strong => SecGood,
            PrivacyGrade.Moderate => SecWarn,
            _ => "#E09A9A",
        };
        PrivacyTopFix = privacy.TopFixCode is { } fix ? L["pscore.fix." + fix] : L["pscore.allGood"];

        // Each finding renders with its limit. Never one without the other: a protection shown alone
        // is how somebody ends up trusting it for something it does not do.
        PrivacyFindings.Clear();
        foreach (var f in privacy.Findings)
        {
            PrivacyFindings.Add(new PrivacyFindingVm(
                L["pscore.f." + f.Code], L["pscore.lim." + f.LimitCode], f.IsStrength));
        }
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
