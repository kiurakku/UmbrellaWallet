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
    public string Theme { get; set; } = "purple";
    public string Language { get; set; } = "en";
    public string Currency { get; set; } = "USD";
    public string SidebarPosition { get; set; } = "Left";
    public bool AnimationsEnabled { get; set; } = true;
    /// <summary>Individual motion toggles (gated by the master AnimationsEnabled above).</summary>
    public bool RainEnabled { get; set; } = true;
    public bool StickersEnabled { get; set; } = true;
    /// <summary>Soft drifting "aurora" glow behind the content. Opt-in (off by default) so the default
    /// look stays clean.</summary>
    public bool AuroraEnabled { get; set; } = false;

    /// <summary>Idle minutes before the vault auto-locks; 0 disables auto-lock entirely.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>A user-chosen label for this wallet, shown in the top bar; blank uses the brand only.</summary>
    public string WalletName { get; set; } = "";

    /// <summary>Absolute paths to the user's own profile images (copied into the data folder when
    /// chosen), so their photos work without the app ever bundling them. Blank = use the defaults.</summary>
    public string AvatarPath { get; set; } = "";
    public string BannerPath { get; set; } = "";
    public string SidebarBackgroundPath { get; set; } = "";

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
        return settings;
    }
}
