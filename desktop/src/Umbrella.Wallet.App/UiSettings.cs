using System;
using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Interface preferences (theme, language). Deliberately separate from the vault: these are not
/// secrets, they must be readable before unlock so the login screen already looks and reads right.
/// </summary>
public sealed class UiSettings
{
    public string Theme { get; set; } = "umbrella";
    public string Language { get; set; } = "en";
    public string Currency { get; set; } = "USD";
    public string SidebarPosition { get; set; } = "Left";
    /// <summary>Phone-style compact layout on the desktop: a narrow centred column and a bottom
    /// tab bar, in a phone-sized window. Off = the normal wide desktop layout.</summary>
    public bool MobileMode { get; set; } = false;
    public bool AnimationsEnabled { get; set; } = true;
    /// <summary>Individual motion toggles (gated by the master AnimationsEnabled above).</summary>
    public bool RainEnabled { get; set; } = true;
    public bool StickersEnabled { get; set; } = true;
    /// <summary>Soft drifting "aurora" glow behind the content. Opt-in (off by default) so the default
    /// look stays clean.</summary>
    public bool AuroraEnabled { get; set; } = false;

    /// <summary>Idle minutes before the vault auto-locks; 0 disables auto-lock entirely.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>Tor-only kill-switch: when on, the wallet refuses any request that would go to clearnet
    /// (fail-closed), so a dropped or disabled Tor can never silently de-anonymise you.</summary>
    public bool TorOnlyMode { get; set; } = false;

    /// <summary>Route all traffic through a user-supplied SOCKS5 proxy instead of the bundled Tor.</summary>
    public bool CustomProxyEnabled { get; set; } = false;
    /// <summary>The user's SOCKS5 proxy, e.g. "socks5://127.0.0.1:9050" (host:port also accepted).</summary>
    public string CustomProxyUri { get; set; } = "";
    /// <summary>IP family for direct connections: "auto", "ipv4" or "ipv6".</summary>
    public string IpMode { get; set; } = "auto";
    /// <summary>Seconds after which a copied address is auto-wiped from the clipboard; 0 = never.</summary>
    public int ClipboardAutoClearSeconds { get; set; } = 45;
    /// <summary>Lock the vault immediately whenever the window is minimized, so a shoulder-surfer or
    /// screen-share never catches an unlocked wallet left in the background.</summary>
    public bool LockOnMinimize { get; set; } = false;
    /// <summary>Start every unlock with balances hidden (••••), so amounts aren't shown until you
    /// choose to reveal them — good for use in public.</summary>
    public bool HideBalancesDefault { get; set; } = false;
    /// <summary>Opt-in market-data connector (CoinGecko): adds market cap / FDV / volume to token
    /// pages. Off by default so the privacy-first wallet never contacts a third party you didn't enable.</summary>
    public bool RichMarketData { get; set; } = false;

    /// <summary>A user-chosen label for this wallet, shown in the top bar; blank uses the brand only.</summary>
    public string WalletName { get; set; } = "";

    /// <summary>Absolute paths to the user's own profile images (copied into the data folder when
    /// chosen), so their photos work without the app ever bundling them. Blank = use the defaults.</summary>
    public string AvatarPath { get; set; } = "";
    public string BannerPath { get; set; } = "";
    public string SidebarBackgroundPath { get; set; } = "";

    /// <summary>The user's own lock-screen (unlock) background; blank uses the bundled default.</summary>
    public string LockBackgroundPath { get; set; } = "";
    /// <summary>When true, the lock screen shows no background image at all (flat).</summary>
    public bool LockScreenPlain { get; set; } = false;

    private static string Path => System.IO.Path.Combine(AppPaths.DataRoot, "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            // Fresh install (incl. after a delete + re-download): pick the OS language if we translate
            // it, so a Ukrainian/Russian/… user isn't dropped into English with no setting to restore.
            if (!File.Exists(Path)) return new UiSettings { Language = DefaultLanguage() };
            return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(Path)) ?? new UiSettings();
        }
        catch
        {
            // Preferences are never worth failing startup over.
            return new UiSettings();
        }
    }

    /// <summary>The OS UI language if Umbrella ships a translation for it, otherwise English.</summary>
    private static string DefaultLanguage()
    {
        try
        {
            var os = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return Loc.Languages.Any(l => l.Code == os) ? os : "en";
        }
        catch
        {
            return "en";
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            File.WriteAllText(Path, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Read-only install directory: run with defaults rather than crash.
        }
    }

    /// <summary>Applies the stored preferences and returns them.</summary>
    public static UiSettings LoadAndApply()
    {
        var settings = Load();
        if (Theming.IsKnown(settings.Theme)) Theming.Apply(settings.Theme);
        else Theming.ApplyDefaults();
        Loc.Instance.CurrentCode = settings.Language;
        Fx.SetLanguage(settings.Language); // fiat amounts follow the UI language's number format
        return settings;
    }
}
