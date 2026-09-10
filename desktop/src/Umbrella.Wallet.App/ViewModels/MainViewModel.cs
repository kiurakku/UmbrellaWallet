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
using Umbrella.Wallet.Core.Amounts;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>Must match EncryptedFileSeedVault.ValidatePassword, which throws below this.</summary>
    public const int MinPasswordLength = 12;

    [ObservableProperty] private bool _hasVault;
    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _password = string.Empty;
    // Optional BIP39 passphrase entered on the unlock screen. Empty opens the normal wallet; any value
    // opens a separate hidden wallet. Never stored — only held long enough to derive this session.
    [ObservableProperty] private string _unlockPassphrase = string.Empty;
    // The passphrase field hides behind "Advanced" so a shoulder-surfer doesn't even see it exists.
    [ObservableProperty] private bool _showUnlockAdvanced;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _formError = string.Empty;
    [ObservableProperty] private string _importPhrase = string.Empty;
    [ObservableProperty] private string _recoveryPhrase = string.Empty;
    [ObservableProperty] private bool _isRecoveryPhraseVisible;
    [ObservableProperty] private string _statusMessage = "Vault is locked";
    [ObservableProperty] private string _activeSection = "Portfolio";
    [ObservableProperty] private bool _isBalanceHidden;
    // Compact portfolio 24h change for the overview ring (real, not a hardcoded 0.00%).
    [ObservableProperty] private string _portfolioChangePercent = "—";
    [ObservableProperty] private string _portfolioChangeColor = "#8A9099";
    // Home dashboard stat tiles (computed in RecalcBalance) — they fill the portfolio with real,
    // at-a-glance numbers rather than leaving it as just the balance + a list.
    [ObservableProperty] private string _portfolioAssetCount = "0";
    [ObservableProperty] private string _portfolioNetworkCount = "0";
    [ObservableProperty] private string _portfolioBestLabel = "—";
    [ObservableProperty] private string _portfolioBestSymbol = "—";
    [ObservableProperty] private string _portfolioBestColor = "#8A9099";
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _chainFilter = "All";
    /// <summary>How the Holdings list is ordered: "Default" (catalog), "Value", "Change" or "Name".</summary>
    [ObservableProperty] private string _holdingsSort = "Default";
    [ObservableProperty] private string _walletLabel = "Umbrella Wallet";
    [ObservableProperty] private string _shortAddress = "—";
    [ObservableProperty] private string _totalBalanceMain = "0";
    [ObservableProperty] private string _totalBalanceCents = "00";
    [ObservableProperty] private string _change24hLabel = "· —";
    [ObservableProperty] private string _watchChain = "ETH";
    [ObservableProperty] private string _watchAddress = string.Empty;
    [ObservableProperty] private string _watchLabel = string.Empty;
    [ObservableProperty] private string _sendChain = "ETH";
    [ObservableProperty] private SendOption? _selectedSendAsset;
    [ObservableProperty] private SendOption? _selectedWatchNetwork;
    [ObservableProperty] private string _sendTo = string.Empty;
    [ObservableProperty] private string _sendAmount = string.Empty;
    [ObservableProperty] private Bitmap? _receiveQr;
    [ObservableProperty] private string _selectedReceiveAddress = string.Empty;
    [ObservableProperty] private string _selectedReceiveSymbol = "ETH";
    [ObservableProperty] private string _selectedReceiveNetwork = string.Empty;
    // HD receive rotation: only offered on chains the wallet can fully discover AND spend across every
    // issued address (BTC/LTC). A fresh address per request reduces on-chain linking.
    [ObservableProperty] private bool _canRotateReceive;
    [ObservableProperty] private string _receivePathLabel = string.Empty;
    // Optional "requested amount" folded into a standards payment URI (BIP21) so the sender's wallet
    // pre-fills it. Only offered on the UTXO chains whose URI scheme is universally recognised.
    [ObservableProperty] private string _receiveAmount = string.Empty;
    // The derivation path is developer detail, so it hides behind an "Advanced" toggle by default.
    [ObservableProperty] private bool _showReceiveAdvanced;
    // Address-reuse warning (secure roadmap 3.3): the shown address is checked against the chain and
    // the banner appears only when it PROVABLY has history — an unreachable explorer says nothing.
    [ObservableProperty] private bool _receiveAddressReused;
    [ObservableProperty] private bool _receiveAddressCheckPending;
    private System.Threading.CancellationTokenSource? _reuseCheckCts;
    public System.Collections.ObjectModel.ObservableCollection<string> ReceiveHistory { get; } = new();
    /// <summary>True once more than the base address has been issued, so the "Previous addresses" list is worth showing.</summary>
    public bool HasReceiveHistory => ReceiveHistory.Count > 1;
    /// <summary>Requested-amount is only encoded where the payment-URI scheme is a recognised standard (BIP21).</summary>
    public bool CanRequestAmount => SelectedReceiveSymbol is "BTC" or "LTC" or "DOGE" or "BCH";
    /// <summary>Tokens live on one specific chain; sending them over the wrong network burns them. Warn loudly.</summary>
    public bool IsTokenReceive => SelectedReceiveSymbol is "USDT" or "USDC";
    private ChainId? _receiveChain;
    [ObservableProperty] private string _marketStatus = "Loading market…";
    [ObservableProperty] private string _settingsPassword = string.Empty;
    [ObservableProperty] private string _deleteConfirmation = string.Empty;
    [ObservableProperty] private string _sendError = string.Empty;

    // Tor is bundled with the app and run as a child process — nothing to install.
    [ObservableProperty] private bool _torEnabled;
    [ObservableProperty] private string _torStatus = "Direct connection · traffic is NOT anonymised";
    [ObservableProperty] private string _torStatusColor = "#E7CA83";

    /// <summary>A compact connection state for the always-visible sidebar chip — the first clause of
    /// the live Tor/proxy status, e.g. "Tor connected" / "Direct connection" (roadmap §7.1).</summary>
    public string ConnectionLabel => (TorStatus ?? string.Empty).Split('·')[0].Trim();

    partial void OnTorStatusChanged(string value) => OnPropertyChanged(nameof(ConnectionLabel));

    /// <summary>True when a broadcast would go over clearnet — Tor is off and the Tor-only kill-switch
    /// isn't forcing it. Shown as an anonymity reminder on the Send review: the node you broadcast to
    /// would see your IP, linking it to the transaction.</summary>
    public bool ShowSendClearnetNote => !TorEnabled && !TorOnly;

    partial void OnTorEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowSendClearnetNote));

    // In-app documentation panel toggle.
    [ObservableProperty] private bool _isDocsVisible;

    /// <summary>Receive QR shown as a centred popup rather than a cramped side panel.</summary>
    [ObservableProperty] private bool _isQrPopupOpen;

    // --- Exchange connections (READ-ONLY API keys) ---------------------------
    [ObservableProperty] private string _exchangeName = "Binance";
    [ObservableProperty] private string _exchangeLabel = string.Empty;
    [ObservableProperty] private string _exchangeApiKey = string.Empty;
    [ObservableProperty] private string _exchangeApiSecret = string.Empty;
    [ObservableProperty] private string _exchangePassphrase = string.Empty;
    [ObservableProperty] private string _exchangeError = string.Empty;
    [ObservableProperty] private string _exchangeStatus = string.Empty;

    public IReadOnlyList<string> SupportedExchanges => ExchangeConnectors.Supported;

    /// <summary>OKX additionally needs the passphrase set when the key was created.</summary>
    public bool ExchangeNeedsPassphrase => ExchangeConnectors.RequiresPassphrase(ExchangeName);

    /// <summary>CryptoBot authenticates with a single token, so it hides the secret field.</summary>
    public bool ExchangeNeedsSecret => ExchangeConnectors.RequiresSecret(ExchangeName);

    /// <summary>Where to create a read-only key on the selected venue.</summary>
    public string ExchangeKeyHint => ExchangeConnectors.KeyHint(ExchangeName);

    partial void OnExchangeNameChanged(string value)
    {
        OnPropertyChanged(nameof(ExchangeNeedsPassphrase));
        OnPropertyChanged(nameof(ExchangeNeedsSecret));
        OnPropertyChanged(nameof(ExchangeKeyHint));
    }

    /// <summary>Connected exchanges, shown so the user can see and remove them.</summary>
    public ObservableCollection<ExchangeCredential> Exchanges { get; } = [];

    private readonly UiSettings _uiSettings = UiSettings.Load();

    /// <summary>Selected UI language code; setting it re-reads every localized binding.</summary>
    public string LanguageCode
    {
        get => Loc.Instance.CurrentCode;
        set
        {
            if (Loc.Instance.CurrentCode == value) return;
            Loc.Instance.CurrentCode = value;
            Fx.SetLanguage(value); // switch fiat number formatting to the new language's locale
            _uiSettings.Language = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeleteKeyword));
            RefreshHoldings();     // re-render money labels (Fx.Money/Price) in the new locale
            RecalcBalance();
            BuildGuide(); // the guide reads in the wallet's language
            if (IsUnlocked)
                PushActivity("Settings", "Language",
                    Loc.Languages.FirstOrDefault(l => l.Code == value)?.Name ?? value, "changed", "now");
        }
    }

    /// <summary>The in-app guide, in the wallet's current language (English fallback). Rebuilt when
    /// the language changes so the documentation always matches the chosen interface language.</summary>
    public ObservableCollection<GuideSection> GuideSections { get; } = [];

    private void BuildGuide()
    {
        GuideSections.Clear();
        foreach (var section in GuideContent.For(Loc.Instance.CurrentCode)) GuideSections.Add(section);
    }

    public IReadOnlyList<Loc.Language> Languages => Loc.Languages;

    // --- Display currency ---------------------------------------------------
    public IReadOnlyList<Fx.Currency> Currencies => Fx.Currencies;

    /// <summary>The chosen fiat's symbol, bound where the UI shows a "$" prefix.</summary>
    public string CurrencySymbol => Fx.Symbol;

    /// <summary>"TOTAL BALANCE · &lt;currency&gt;" caption above the balance.</summary>
    public string TotalBalanceCaption => $"TOTAL BALANCE · {_uiSettings.Currency}";

    /// <summary>Fiat to show balances in. Prices stay USD internally; <see cref="Fx"/> converts.</summary>
    public string CurrencyCode
    {
        get => _uiSettings.Currency;
        set
        {
            if (_uiSettings.Currency == value || Fx.Currencies.All(c => c.Code != value)) return;
            _uiSettings.Currency = value;
            _uiSettings.Save();
            OnPropertyChanged();
            if (IsUnlocked) PushActivity("Settings", "Currency", value, "changed", "now");
            _ = ApplyCurrencyAsync();
        }
    }

    /// <summary>Loads the USD→currency rate and repaints every money figure in the new currency.</summary>
    private async Task ApplyCurrencyAsync()
    {
        Fx.Symbol = Fx.SymbolFor(_uiSettings.Currency);
        Fx.Rate = await _rates.GetFiatRateAsync(_uiSettings.Currency);
        OnPropertyChanged(nameof(CurrencySymbol));
        OnPropertyChanged(nameof(TotalBalanceCaption));
        RefreshHoldings();   // rebuild Holdings rows so their Fx-based labels re-read the new rate
        RecalcBalance();
        _ = RefreshMarketAsync(); // market rows re-read prices in the new currency
    }

    /// <summary>Selected colour theme; repaints every themed surface immediately.</summary>
    public string ThemeId
    {
        get => Theming.Current;
        set
        {
            if (Theming.Current == value || !Theming.IsKnown(value)) return;
            Theming.Apply(value);
            _uiSettings.Theme = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LogoImage));
            // Theme changes are not real wallet events — logging them spammed the activity feed with
            // "Theme Appearance changed" rows that read like a developer log, so they're no longer logged.
        }
    }

    public IReadOnlyList<Theming.ThemeOption> ThemeOptions => Theming.Themes;

    // --- Navigation panel placement ----------------------------------------
    public IReadOnlyList<string> SidebarPositions { get; } = ["Left", "Right", "Top", "Bottom"];

    /// <summary>Motion toggle, persisted. Drives every looping sticker via <see cref="LottieRepeat"/>.</summary>
    public bool AnimationsEnabled
    {
        get => _uiSettings.AnimationsEnabled;
        set
        {
            if (_uiSettings.AnimationsEnabled == value) return;
            _uiSettings.AnimationsEnabled = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LottieRepeat));
            OnPropertyChanged(nameof(RainVisible));
            OnPropertyChanged(nameof(AuroraVisible));
            OnPropertyChanged(nameof(PortfolioVideoOn));
            if (IsUnlocked) PushActivity("Settings", "Animations", value ? "on" : "off", "changed", "now");
        }
    }

    /// <summary>Rain layer toggle — individual, gated by the master motion toggle.</summary>
    public bool RainEnabled
    {
        get => _uiSettings.RainEnabled;
        set
        {
            if (_uiSettings.RainEnabled == value) return;
            _uiSettings.RainEnabled = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RainVisible));
        }
    }

    /// <summary>Sticker (Lottie) toggle — individual, gated by the master motion toggle.</summary>
    public bool StickersEnabled
    {
        get => _uiSettings.StickersEnabled;
        set
        {
            if (_uiSettings.StickersEnabled == value) return;
            _uiSettings.StickersEnabled = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LottieRepeat));
        }
    }

    /// <summary>Soft drifting aurora glow — individual, gated by the master motion toggle. Off by default.</summary>
    public bool AuroraEnabled
    {
        get => _uiSettings.AuroraEnabled;
        set
        {
            if (_uiSettings.AuroraEnabled == value) return;
            _uiSettings.AuroraEnabled = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AuroraVisible));
        }
    }

    /// <summary>Portfolio-card video toggle — individual, gated by the master motion toggle. On swaps
    /// the still photo for the looping rain footage behind the balance.</summary>
    public bool PortfolioVideo
    {
        get => _uiSettings.PortfolioVideo;
        set
        {
            if (_uiSettings.PortfolioVideo == value) return;
            _uiSettings.PortfolioVideo = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PortfolioVideoOn));
        }
    }

    /// <summary>The ambient rain shows only when both the master motion toggle and the rain toggle are on.</summary>
    public bool RainVisible => AnimationsEnabled && RainEnabled;
    /// <summary>The aurora glow shows only when both the master motion toggle and the aurora toggle are on.</summary>
    public bool AuroraVisible => AnimationsEnabled && AuroraEnabled;
    /// <summary>The balance card plays the rain footage only when both the master motion toggle and the
    /// portfolio-video toggle are on; otherwise it shows a still photo.</summary>
    public bool PortfolioVideoOn => AnimationsEnabled && PortfolioVideo;

    /// <summary>-1 = loop forever (stickers on); 0 = play once and settle. Off if either the master or
    /// the sticker toggle is disabled.</summary>
    public int LottieRepeat => AnimationsEnabled && StickersEnabled ? -1 : 0;

    /// <summary>Where the navigation panel sits. Persisted like the theme.</summary>
    public string SidebarPosition
    {
        get => _uiSettings.SidebarPosition;
        set
        {
            if (_uiSettings.SidebarPosition == value || !SidebarPositions.Contains(value)) return;
            _uiSettings.SidebarPosition = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SidebarDock));
            OnPropertyChanged(nameof(IsSidebarVertical));
            OnPropertyChanged(nameof(IsSidebarHorizontal));
            OnPropertyChanged(nameof(IsDesktopHorizontalNav));
            if (IsUnlocked) PushActivity("Settings", "Nav panel", value, "changed", "now");
        }
    }

    /// <summary>Idle auto-lock, in minutes; 0 = never. Persisted; the window reads it to arm the
    /// timer, and re-reads whenever it changes (see MainWindow.OnViewModelPropertyChanged).</summary>
    public IReadOnlyList<string> AutoLockOptions { get; } =
        ["Off", "1 minute", "5 minutes", "15 minutes", "30 minutes", "1 hour"];

    private static readonly Dictionary<string, int> AutoLockMap = new()
    {
        ["Off"] = 0, ["1 minute"] = 1, ["5 minutes"] = 5, ["15 minutes"] = 15, ["30 minutes"] = 30, ["1 hour"] = 60,
    };

    public string AutoLockChoice
    {
        get => AutoLockMap.FirstOrDefault(kv => kv.Value == _uiSettings.AutoLockMinutes).Key ?? "5 minutes";
        set
        {
            if (!AutoLockMap.TryGetValue(value, out var minutes) || minutes == _uiSettings.AutoLockMinutes) return;
            _uiSettings.AutoLockMinutes = minutes;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoLockMinutes));
            OnPropertyChanged(nameof(AutoLockLabel));
            if (IsUnlocked) PushActivity("Security", "Auto-lock", value, "changed", "now");
        }
    }

    public int AutoLockMinutes => _uiSettings.AutoLockMinutes;

    /// <summary>The idle interval as a LOCALIZED duration ("5 хв" / "1 год"), for display — the picker
    /// values themselves stay English keys. Fixes "5 minutes" showing in a non-English UI.</summary>
    public string AutoLockDurationLabel => _uiSettings.AutoLockMinutes switch
    {
        0 => Loc.Instance["sec.autoLockOff2"],
        60 => $"1 {Loc.Instance["unit.hour"]}",
        var m => $"{m} {Loc.Instance["unit.min"]}",
    };

    public string AutoLockLabel => _uiSettings.AutoLockMinutes == 0
        ? "Auto-lock · off"
        : $"Auto-lock · {AutoLockDurationLabel} idle";

    // ---- Privacy: custom SOCKS proxy, IP family, clipboard auto-clear ----

    [ObservableProperty] private string _proxyStatus = string.Empty;
    [ObservableProperty] private string _proxyStatusColor = "#8B909A";

    /// <summary>Route traffic through a user-supplied SOCKS5 proxy instead of the bundled Tor.</summary>
    public bool CustomProxyEnabled
    {
        get => _uiSettings.CustomProxyEnabled;
        set
        {
            if (_uiSettings.CustomProxyEnabled == value) return;
            _uiSettings.CustomProxyEnabled = value;
            _uiSettings.Save();
            OnPropertyChanged();
            ApplyCustomProxy();
        }
    }

    /// <summary>The user's SOCKS5 proxy string; "host:port" or a full socks5:// URI.</summary>
    public string CustomProxyUri
    {
        get => _uiSettings.CustomProxyUri;
        set
        {
            if (_uiSettings.CustomProxyUri == value) return;
            _uiSettings.CustomProxyUri = value ?? string.Empty;
            _uiSettings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Tor-only kill-switch: when on, any request that would hit clearnet is refused
    /// (fail-closed), so turning Tor off or a dropped Tor circuit never silently de-anonymises you.</summary>
    public bool TorOnly
    {
        get => _uiSettings.TorOnlyMode;
        set
        {
            if (_uiSettings.TorOnlyMode == value) return;
            _uiSettings.TorOnlyMode = value;
            _uiSettings.Save();
            OnPropertyChanged();
            PublicHttp.SetRequireProxy(value);
            OnPropertyChanged(nameof(TorOnlyStatus));
            OnPropertyChanged(nameof(ShowSendClearnetNote));
            if (IsUnlocked) PushActivity("Security", "Tor-only", value ? "on" : "off",
                value ? "clearnet blocked" : "clearnet allowed", "now");
            // Turning it on with Tor still off means everything is blocked until Tor connects — nudge.
            if (value && !TorEnabled) TorEnabled = true;
            _ = RefreshMarketAsync();
        }
    }

    /// <summary>One-line explanation shown under the Tor-only toggle.</summary>
    public string TorOnlyStatus => TorOnly
        ? (TorEnabled ? Loc.Instance["priv.torOnlyOn"] : Loc.Instance["priv.torOnlyBlocked"])
        : Loc.Instance["priv.torOnlyOff"];

    // ---- Verify Tor: prove the wallet's traffic really exits through Tor (read-only check) ----
    [ObservableProperty] private string _torCheckStatus = string.Empty;
    [ObservableProperty] private string _torCheckColor = "#8B909A";
    [ObservableProperty] private bool _torChecking;

    /// <summary>Asks check.torproject.org (through the wallet's own shared client, so it takes the exact
    /// same route as every balance/price call) whether this exit is a Tor node — the honest proof that
    /// the anonymity toggle is actually doing something. With Tor-only on and Tor down the request is
    /// blocked by the kill-switch, which the result reports as "blocked (kill-switch working)".</summary>
    [RelayCommand]
    private async Task VerifyTorAsync()
    {
        if (TorChecking) return;
        TorChecking = true;
        TorCheckStatus = Loc.Instance["priv.torCheckRunning"];
        TorCheckColor = "#8B909A";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var res = await PublicHttp.Shared.GetAsync("https://check.torproject.org/api/ip", cts.Token);
            var body = (await res.Content.ReadAsStringAsync(cts.Token))
                .Replace(" ", "").Replace("\n", "").ToLowerInvariant();
            if (body.Contains("\"istor\":true"))
            {
                TorCheckStatus = Loc.Instance["priv.torCheckOk"];
                TorCheckColor = "#8FCB9B";
            }
            else
            {
                TorCheckStatus = Loc.Instance["priv.torCheckNo"];
                TorCheckColor = "#E7CA83";
            }
        }
        catch
        {
            // Tor-only + Tor off → the kill-switch refused the request. That's success, not failure.
            TorCheckStatus = TorOnly && !TorEnabled
                ? Loc.Instance["priv.torCheckBlocked"]
                : Loc.Instance["priv.torCheckFail"];
            TorCheckColor = TorOnly && !TorEnabled ? "#8FCB9B" : "#E09A9A";
        }
        finally
        {
            TorChecking = false;
        }
    }

    /// <summary>Normalises "host:port" or a socks URI into a canonical socks URI, or null if invalid.</summary>
    private static string? NormalizeProxyUri(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        if (!s.Contains("://", StringComparison.Ordinal)) s = "socks5://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("socks5" or "socks5h" or "socks4" or "socks4a")) return null;
        if (uri.Port <= 0 || string.IsNullOrEmpty(uri.Host)) return null;
        return $"{scheme}://{uri.Host}:{uri.Port}";
    }

    /// <summary>The custom proxy URI if it's enabled and valid, otherwise null.</summary>
    private string? EffectiveCustomProxy() =>
        CustomProxyEnabled ? NormalizeProxyUri(CustomProxyUri) : null;

    [RelayCommand]
    private void ApplyCustomProxy()
    {
        if (!CustomProxyEnabled)
        {
            // Hand routing back to Tor (its proxy if on, else direct).
            PublicHttp.SetProxy(TorEnabled ? _tor.ProxyUri : null);
            ProxyStatus = string.Empty;
            _ = RefreshMarketAsync();
            return;
        }

        var normalized = EffectiveCustomProxy();
        if (normalized is null)
        {
            ProxyStatus = "Enter a valid SOCKS proxy, e.g. socks5://127.0.0.1:9050";
            ProxyStatusColor = "#E09A9A";
            return;
        }

        // A custom proxy and the bundled Tor are mutually exclusive routes.
        if (TorEnabled)
        {
            TorEnabled = false;
            _tor.Stop();
            TorStatus = "Off · using your custom proxy instead";
            TorStatusColor = "#E7CA83";
        }

        PublicHttp.SetProxy(normalized);
        ProxyStatus = $"Routing through {normalized}";
        ProxyStatusColor = "#8FCB9B";
        if (IsUnlocked) PushActivity("Security", "Proxy", "on", normalized, "now");
        _ = RefreshMarketAsync();
    }

    public System.Collections.Generic.IReadOnlyList<string> IpModeOptions { get; } =
        new[] { "Automatic", "IPv4 only", "IPv6 only" };

    /// <summary>Which IP family direct connections may use. Persisted; applied immediately.</summary>
    public string IpModeChoice
    {
        get => _uiSettings.IpMode switch
        {
            "ipv4" => "IPv4 only",
            "ipv6" => "IPv6 only",
            _ => "Automatic",
        };
        set
        {
            var code = value switch { "IPv4 only" => "ipv4", "IPv6 only" => "ipv6", _ => "auto" };
            if (_uiSettings.IpMode == code) return;
            _uiSettings.IpMode = code;
            _uiSettings.Save();
            OnPropertyChanged();
            PublicHttp.SetIpPreference(PublicHttp.ParseIpMode(code));
            if (IsUnlocked) PushActivity("Security", "IP version", value, "changed", "now");
            _ = RefreshMarketAsync();
        }
    }

    private static readonly Dictionary<string, int> ClipboardClearMap = new()
    {
        ["Never"] = 0, ["30 seconds"] = 30, ["45 seconds"] = 45, ["1 minute"] = 60, ["2 minutes"] = 120,
    };

    public System.Collections.Generic.IReadOnlyList<string> ClipboardClearOptions { get; } =
        new[] { "Never", "30 seconds", "45 seconds", "1 minute", "2 minutes" };

    /// <summary>Start each unlock with balances hidden. Persisted.</summary>
    public bool HideBalancesDefault
    {
        get => _uiSettings.HideBalancesDefault;
        set
        {
            if (_uiSettings.HideBalancesDefault == value) return;
            _uiSettings.HideBalancesDefault = value;
            _uiSettings.Save();
            OnPropertyChanged();
            if (value) IsBalanceHidden = true;
        }
    }

    /// <summary>Lock the vault the moment the window is minimized. Persisted; the window reads it.</summary>
    public bool LockOnMinimize
    {
        get => _uiSettings.LockOnMinimize;
        set
        {
            if (_uiSettings.LockOnMinimize == value) return;
            _uiSettings.LockOnMinimize = value;
            _uiSettings.Save();
            OnPropertyChanged();
            if (IsUnlocked) PushActivity("Security", "Lock on minimize", value ? "on" : "off", "changed", "now");
        }
    }

    /// <summary>Clipboard auto-wipe delay in seconds; 0 = never wiped. Read-only view of the setting.</summary>
    public int ClipboardAutoClearSeconds => _uiSettings.ClipboardAutoClearSeconds;

    /// <summary>How long a copied address stays on the clipboard before it's auto-wiped. Persisted.</summary>
    public string ClipboardClearChoice
    {
        get => ClipboardClearMap.FirstOrDefault(kv => kv.Value == _uiSettings.ClipboardAutoClearSeconds).Key
               ?? "45 seconds";
        set
        {
            if (!ClipboardClearMap.TryGetValue(value, out var secs) ||
                secs == _uiSettings.ClipboardAutoClearSeconds) return;
            _uiSettings.ClipboardAutoClearSeconds = secs;
            _uiSettings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>A user-chosen name for this wallet, shown in the top bar. Persisted.</summary>
    public string WalletName
    {
        get => _uiSettings.WalletName;
        set
        {
            if (_uiSettings.WalletName == value) return;
            _uiSettings.WalletName = value ?? "";
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WalletTitle));
            OnPropertyChanged(nameof(HasWalletName));
        }
    }

    /// <summary>The top-bar identity: the chosen wallet name, or the brand when none is set.</summary>
    public string WalletTitle =>
        string.IsNullOrWhiteSpace(_uiSettings.WalletName) ? "UMBRELLA WALLET" : _uiSettings.WalletName.ToUpperInvariant();

    public bool HasWalletName => !string.IsNullOrWhiteSpace(_uiSettings.WalletName);

    // Mobile mode forces a bottom tab bar regardless of the saved SidebarPosition, so the phone
    // layout is consistent; the user's real preference is untouched and returns when it's turned off.
    public Dock SidebarDock => MobileMode ? Dock.Bottom : SidebarPosition switch
    {
        "Right" => Dock.Right,
        "Top" => Dock.Top,
        "Bottom" => Dock.Bottom,
        _ => Dock.Left,
    };

    /// <summary>
    /// Left and right keep the tall panel; top and bottom switch to a compact horizontal bar,
    /// because a 248px-wide column laid on its side would eat most of the window height. Mobile mode
    /// is always horizontal (a bottom bar).
    /// </summary>
    public bool IsSidebarVertical => !MobileMode && SidebarPosition is "Left" or "Right";

    public bool IsSidebarHorizontal => !IsSidebarVertical;

    /// <summary>The dedicated phone tab bar (icons) shows only in mobile mode.</summary>
    public bool IsMobileNav => MobileMode;

    /// <summary>The desktop text-chip bar shows for Top/Bottom desktop layouts, but never in mobile —
    /// there the icon tab bar takes over.</summary>
    public bool IsDesktopHorizontalNav => IsSidebarHorizontal && !MobileMode;

    /// <summary>Phone-style compact layout on the desktop: a narrow centred column and a bottom tab
    /// bar in a phone-sized window (the window itself is resized by the view). Persisted.</summary>
    public bool MobileMode
    {
        get => _uiSettings.MobileMode;
        set
        {
            if (_uiSettings.MobileMode == value) return;
            _uiSettings.MobileMode = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SidebarDock));
            OnPropertyChanged(nameof(IsSidebarVertical));
            OnPropertyChanged(nameof(IsSidebarHorizontal));
            OnPropertyChanged(nameof(IsMobileNav));
            OnPropertyChanged(nameof(IsDesktopHorizontalNav));
            OnPropertyChanged(nameof(ContentMaxWidth));
            OnPropertyChanged(nameof(QuickActionColumns));
            OnPropertyChanged(nameof(ShowSideRail));
            if (!value) IsMoreSheetOpen = false;
            if (IsUnlocked) PushActivity("Settings", "Layout", value ? "mobile" : "desktop", "changed", "now");
        }
    }

    /// <summary>Width cap for the main dashboard column — a phone-like column in mobile mode, the
    /// roomy desktop width otherwise.</summary>
    public double ContentMaxWidth => MobileMode ? 460 : 1120;

    /// <summary>The quick-action tiles wrap to two columns on the narrow phone layout so their
    /// labels don't clip; four across on the desktop.</summary>
    public int QuickActionColumns => MobileMode ? 2 : 4;

    /// <summary>
    /// The in-app wordmark on the welcome and unlock screens — the ORIGINAL Umbrella Wallet logo,
    /// swapped for the dark version on light themes (the solid-white one is invisible on white). The
    /// new full-colour umbrella art is used only for the desktop app / taskbar icon (umbrella.ico),
    /// not inside the app.
    /// </summary>
    public Bitmap LogoImage => LoadAsset(
        Theming.IsLightTheme(Theming.Current)
            ? "umbrella-logo-black.png"
            : "umbrella-logo-solidwhite.png");

    /// <summary>The "the fear" maker's mark — the gold ghost (transparent background).</summary>
    public Bitmap FearMark => LoadAsset("thefear-ghost.png");

    private static readonly Dictionary<string, Bitmap> AssetCache = [];

    private static Bitmap LoadAsset(string name)
    {
        if (AssetCache.TryGetValue(name, out var cached)) return cached;
        var bitmap = new Bitmap(AssetLoader.Open(
            new Uri($"avares://Umbrella.Wallet.App/Assets/{name}")));
        AssetCache[name] = bitmap;
        return bitmap;
    }

    // Settings tab: appearance (theme + language), kept separate so it is easy to find.
    public bool IsTabAppearance => SettingsTab == "Appearance";

    // Settings is split into panes so nothing important (the danger zone especially) ends up
    // buried at the bottom of one very long scroll.
    [ObservableProperty] private string _settingsTab = "Appearance";

    public bool IsTabWallets => SettingsTab == "Wallets";
    public bool IsTabSecurity => SettingsTab == "Security";
    public bool IsTabPrivacy => SettingsTab == "Privacy";
    public bool IsTabBackup => SettingsTab == "Backup";
    public bool IsTabGuide => SettingsTab == "Guide";
    public bool IsTabDanger => SettingsTab == "Danger";

    partial void OnSettingsTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsTabAppearance));
        OnPropertyChanged(nameof(IsTabWallets));
        OnPropertyChanged(nameof(IsTabSecurity));
        OnPropertyChanged(nameof(IsTabPrivacy));
        OnPropertyChanged(nameof(IsTabBackup));
        OnPropertyChanged(nameof(IsTabGuide));
        OnPropertyChanged(nameof(IsTabDanger));

        // Leaving the backup pane must drop any revealed secret from the screen.
        if (value != "Backup")
        {
            SettingsRevealedPhrase = string.Empty;
            IsSettingsPhraseVisible = false;
            HideMoneroKeys();
        }
    }

    [RelayCommand]
    private void SelectSettingsTab(string tab) => SettingsTab = tab;

    /// <summary>Jump straight to the wallet switcher (Settings → Wallets) from the sidebar.</summary>
    [RelayCommand]
    private void OpenWallets()
    {
        SettingsTab = "Wallets";
        SelectSection("Settings");
    }

    // --- Settings search -----------------------------------------------------
    /// <summary>Everything you can find in Settings, with the pane each lives in. Drives the search box.</summary>
    private static readonly SettingsShortcut[] SettingsCatalog =
    [
        new("Language", "Appearance", "interface language english ukrainian"),
        new("Theme", "Appearance", "colors dark brand uniswap binance bitcoin telegram tron"),
        new("Display currency", "Appearance", "usd eur uah rub fiat money symbol"),
        new("Wallets", "Wallets", "switch multiple accounts binance"),
        new("Add a wallet", "Wallets", "new wallet import second savings"),
        new("Rename wallet", "Wallets", "label name"),
        new("Auto-lock timer", "Security", "idle lock minutes"),
        new("Reveal recovery phrase", "Backup", "seed words 24 mnemonic show"),
        new("Backup & restore", "Backup", "export import vault file"),
        new("Monero keys", "Backup", "xmr spend view secret"),
        new("Tor / network privacy", "Privacy", "tor onion routing ip"),
        new("Screenshot protection", "Privacy", "hide seed capture screen"),
        new("Guide & docs", "Guide", "help documentation how to"),
        new("Delete wallet", "Danger", "erase wipe remove everything"),
        new("Clear history", "Danger", "activity transactions log"),
        new("Disconnect all", "Danger", "watch addresses exchanges unlink"),
    ];

    public ObservableCollection<SettingsShortcut> SettingsResults { get; } = [];
    public bool HasSettingsResults => SettingsResults.Count > 0;

    [ObservableProperty] private string _settingsSearch = string.Empty;

    partial void OnSettingsSearchChanged(string value)
    {
        SettingsResults.Clear();
        var q = value?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            foreach (var s in SettingsCatalog)
            {
                if (s.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    s.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    SettingsResults.Add(s);
                }
            }
        }
        OnPropertyChanged(nameof(HasSettingsResults));
    }

    /// <summary>Open a settings pane from a search result and clear the query.</summary>
    [RelayCommand]
    private void OpenSetting(string? tab)
    {
        if (!string.IsNullOrWhiteSpace(tab)) SettingsTab = tab;
        SettingsSearch = string.Empty;
    }

    // --- Developer fee -------------------------------------------------------
    // Baked into the build (DeveloperFeeConfig): the recipient address is obfuscated and never shown
    // in the UI. The fee percentage is still disclosed in the send review before the user confirms.
    private readonly DeveloperFeeConfig _devFee = DeveloperFeeConfig.Load();

    /// <summary>Version shown in the status bar — read from the assembly so it never drifts from the csproj.</summary>
    public string AppVersionLabel =>
        $"Umbrella Wallet v{CurrentVersion} · the fear";

    private static string CurrentVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.8.0";

    // --- In-app update check (manual, Tor-aware; data is never touched) ------
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private bool _updateAvailable;

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        UpdateAvailable = false;
        UpdateStatus = "Checking for updates…";
        var r = await UpdateChecker.CheckAsync(CurrentVersion);
        if (r.Error is not null)
        {
            UpdateStatus = $"Could not check right now: {r.Error}";
            return;
        }

        if (r.Available)
        {
            UpdateAvailable = true;
            UpdateStatus =
                $"Update available: v{r.Latest} (you have v{CurrentVersion}). Your vault, keys and " +
                "settings are kept — an update only replaces the app files, never the data folder.";
        }
        else
        {
            UpdateStatus = $"You're on the latest version (v{CurrentVersion}).";
        }
    }

    [RelayCommand]
    private async Task CopyReleasesLink()
    {
        await CopyTextAsync(UpdateChecker.ReleasesUrl);
        StatusMessage = "Download link copied — open it in your browser (or Tor Browser) to get the new build.";
    }

    // ETH send flow: quote → explicit confirm → broadcast result.
    [ObservableProperty] private bool _hasSendQuote;
    [ObservableProperty] private string _sendQuoteSummary = string.Empty;
    [ObservableProperty] private string _sendQuoteFee = string.Empty;
    [ObservableProperty] private string _sendSuccess = string.Empty;
    private EthSendQuote? _sendQuote;
    private BtcSendQuote? _btcQuote;
    // The HD spend plan behind the pending BTC/LTC quote: the exact inputs (drawn from every owned
    // address) and request that Confirm signs — so nothing is re-selected between review and broadcast.
    private UtxoSpendPlan? _btcPlan;
    private UtxoSpendRequest? _btcRequest;
    private string? _btcPlanSymbol;
    private SolSendQuote? _solQuote;
    private TronSendQuote? _tronQuote;
    private TonSendQuote? _tonQuote;
    private AdaSendQuote? _adaQuote;
    private string _sendSymbol = "ETH";
    private decimal _moneroAmount;
    private string _moneroTo = string.Empty;
    // Validated developer fee for the pending XMR send (second destination in the same tx).
    private string? _moneroFeeTo;
    private decimal _moneroFeeAmount;

    // Monero wallet service (bundled monero-wallet-rpc) — real balance and sending.
    [ObservableProperty] private bool _moneroEnabled;
    [ObservableProperty] private string _moneroStatus = "Monero wallet service is off";
    [ObservableProperty] private string _moneroStatusColor = "#8A9099";

    // Monero keys export (password-gated, like the seed phrase).
    [ObservableProperty] private bool _isMoneroKeysVisible;
    [ObservableProperty] private string _moneroAddress = string.Empty;
    [ObservableProperty] private string _moneroSpendKey = string.Empty;
    [ObservableProperty] private string _moneroViewKey = string.Empty;

    // Onboarding is a small state machine of full-screen pages (no sidebar) rather than a pile
    // of cards stacked over the workspace: Welcome → Create/Import → (Backup) → Workspace.
    [ObservableProperty] private string _setupStage = "Welcome"; // Welcome | Create | Import
    [ObservableProperty] private bool _pendingPhraseBackup;

    // Settings-only phrase reveal, kept separate so it can NEVER show without a password.
    [ObservableProperty] private string _settingsRevealedPhrase = string.Empty;
    [ObservableProperty] private bool _isSettingsPhraseVisible;

    // Market detail chart (shown when a coin row is clicked).
    [ObservableProperty] private string _selectedMarketSymbol = string.Empty;
    [ObservableProperty] private string _selectedMarketName = string.Empty;
    [ObservableProperty] private string _selectedMarketPriceLabel = string.Empty;
    [ObservableProperty] private string _selectedMarketChangeLabel = string.Empty;
    [ObservableProperty] private string _selectedMarketChangeColor = "#8A9099";
    [ObservableProperty] private bool _hasChart;
    [ObservableProperty] private bool _isChartLoading;
    [ObservableProperty] private System.Collections.Generic.List<Avalonia.Point> _chartPoints = new();

    /// <summary>Whether the open chart is up over its window — drives the up/down market sticker.</summary>
    [ObservableProperty] private bool _chartIsUp = true;

    // Uniswap-style token stats under the chart (24h high/low/volume from the same Binance feed).
    [ObservableProperty] private bool _hasMarketStats;
    [ObservableProperty] private string _statHigh24h = "—";
    [ObservableProperty] private string _statLow24h = "—";
    [ObservableProperty] private string _statVolume24h = "—";

    // Richer stats from the optional CoinGecko connector (market cap / FDV), only when enabled.
    [ObservableProperty] private bool _hasRichStats;
    [ObservableProperty] private string _statMarketCap = "—";
    [ObservableProperty] private string _statFdv = "—";

    /// <summary>Opt-in market-data connector (CoinGecko) for richer token stats. Persisted; off by
    /// default so the wallet contacts no third party unless the user turns it on.</summary>
    public bool RichMarketData
    {
        get => _uiSettings.RichMarketData;
        set
        {
            if (_uiSettings.RichMarketData == value) return;
            _uiSettings.RichMarketData = value;
            _uiSettings.Save();
            OnPropertyChanged();
            if (!value) HasRichStats = false;
            if (IsUnlocked) PushActivity("Settings", "Market data", value ? "on" : "off", "CoinGecko", "now");
        }
    }

    private string? _unlockedMnemonic;
    // The BIP39 passphrase of the currently-open wallet (empty = the normal wallet). Held in memory
    // only for this session (never persisted — that IS the hidden-wallet deniability) and pushed onto
    // the shared deriver so every derivation uses it. Wiped on lock.
    private string _unlockedPassphrase = "";
    // One common login password for the whole app: captured on unlock/create so additional wallets
    // reuse it and switching between wallets doesn't re-prompt. Wiped on lock alongside the seed.
    private string? _sessionPassword;
    private readonly WalletRegistry _registry;
    private EncryptedFileSeedVault _vault;
    // Add-wallet flow: while true, a create/import writes an *additional* wallet rather than the first.
    private string? _pendingNewWalletId;
    private string? _previousActiveWalletId;
    private readonly Bip39MnemonicService _mnemonics = new();
    private readonly HdAddressDeriver _deriver = new();
    private readonly PublicChainBalanceClient _balances = new();
    private readonly PublicMarketRatesClient _rates = new();
    private readonly WatchAddressStore _watchStore = new();
    private readonly ActivityStore _activityStore = new();
    private readonly AddressBookStore _addressBook = new();
    private readonly OnChainHistoryClient _history = new();
    // Latest fetched USD prices, snapshotted on each live refresh, so the Send screen can show a fiat
    // equivalent for the amount without re-fetching (roadmap §4).
    private IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> _priceUsd =
        new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
    // On-chain transactions fetched from explorers for the user's own addresses (incl. ones made
    // before the wallet was ever opened). Merged into the Transactions list, deduped by explorer URL.
    private readonly List<ActivityRowViewModel> _onChainRows = new();
    private readonly BalanceStore _balanceStore = new();
    private readonly MarketCache _marketCache = new();
    private readonly ExchangeCredentialStore _exchangeStore = new();
    private readonly EthTransactionSender _ethSender = new();
    // The UTXO scanner and Bitcoin sender share the ONE deriver (with its ambient passphrase), so a
    // hidden wallet scans, shows and spends the exact same addresses — no call site can drift.
    private readonly BitcoinTransactionSender _btcSender;
    private readonly UtxoAccountScanner _utxoScanner;
    private readonly AddressIndexStore _addrIndex = new();
    // Decides whether a receive address on screen has already been used (secure roadmap 3.3).
    private readonly AddressReuseInspector _reuseInspector = new();
    // Last full UTXO scan per BTC/LTC symbol (all external + internal addresses). Populated by the
    // balance refresh and reused by the send path, so a transfer spends the same discovered set —
    // including internal change — that the shown balance is computed from.
    private readonly Dictionary<string, UtxoScanResult> _utxoScans = new(StringComparer.OrdinalIgnoreCase);
    private readonly SolanaTransactionSender _solSender = new();
    private readonly TronTransactionSender _tronSender = new();
    private readonly TonTransactionSender _tonSender = new();
    private readonly CardanoTransactionSender _adaSender = new();
    private readonly EmbeddedTorService _tor = new();
    private readonly MoneroRpcService _monero = new();
    private CancellationTokenSource? _refreshCts;
    private Avalonia.Threading.DispatcherTimer? _autoRefreshTimer;

    // --- Multi-wallet (Binance-style) ---------------------------------------
    /// <summary>Every wallet on this PC, for the switcher. Each is an independent encrypted vault.</summary>
    public ObservableCollection<WalletListItemViewModel> Wallets { get; } = [];
    public string ActiveWalletLabel => _registry.Active?.Label ?? "Main wallet";
    public bool HasMultipleWallets => _registry.Wallets.Count > 1;
    /// <summary>Label typed when adding a new wallet.</summary>
    [ObservableProperty] private string _newWalletLabel = string.Empty;
    /// <summary>New label typed when renaming the active wallet.</summary>
    [ObservableProperty] private string _renameWalletLabel = string.Empty;
    /// <summary>True while the create/import onboarding is setting up an additional wallet (so the
    /// onboarding can offer a Cancel back to the existing wallet).</summary>
    [ObservableProperty] private bool _isAddingWallet;

    public bool HasSessionPassword => !string.IsNullOrEmpty(_sessionPassword);
    /// <summary>An additional wallet silently reuses the one app password — but only when we actually
    /// have it. Without it, the password fields must show so the user is never stuck.</summary>
    public bool ReuseAppPassword => IsAddingWallet && HasSessionPassword;
    /// <summary>Whether the create/import screens show the password fields (hidden only when reusing).</summary>
    public bool ShowVaultPasswordFields => !ReuseAppPassword;

    /// <summary>Single place that changes the in-memory app password, so every dependent flag updates.</summary>
    private void SetSessionPassword(string? pw)
    {
        _sessionPassword = pw;
        OnPropertyChanged(nameof(HasSessionPassword));
        OnPropertyChanged(nameof(ReuseAppPassword));
        OnPropertyChanged(nameof(ShowVaultPasswordFields));
    }

    partial void OnIsAddingWalletChanged(bool value)
    {
        OnPropertyChanged(nameof(ReuseAppPassword));
        OnPropertyChanged(nameof(ShowVaultPasswordFields));
    }

    /// <summary>Compatibility overload (tests / callers with a single vault): wraps that vault as the
    /// one-and-only "Main" wallet in a registry rooted beside it.</summary>
    public MainViewModel(EncryptedFileSeedVault vault)
        : this(RegistryForSingleVault(vault))
    {
    }

    private static WalletRegistry RegistryForSingleVault(EncryptedFileSeedVault vault)
    {
        var dir = System.IO.Path.GetDirectoryName(vault.VaultPath) ?? ".";
        return new WalletRegistry(
            System.IO.Path.Combine(dir, "wallets.json"),
            vault.VaultPath,
            id => System.IO.Path.Combine(dir, "wallets", id + ".vault.json"));
    }

    public MainViewModel(WalletRegistry registry)
    {
        // Scanner + Bitcoin sender share the ONE deriver, so a hidden-wallet passphrase (set on it at
        // unlock) drives balance scanning and spending too — the shown, scanned and spent addresses
        // can never disagree.
        _btcSender = new BitcoinTransactionSender(_deriver);
        _utxoScanner = new UtxoAccountScanner(_deriver);

        // Mobile layout is retired on desktop (it was a phone-shaped desktop, not a real mobile
        // platform). Force it off so any previously-saved state can't strand a desktop user.
        if (_uiSettings.MobileMode) { _uiSettings.MobileMode = false; _uiSettings.Save(); }

        _registry = registry;
        _vault = BuildActiveVault();
        SelfHealWallets();
        HasVault = _vault.Exists;
        RefreshWalletList();
        StatusMessage = HasVault
            ? "Local vault found · unlock to load live balances"
            : "Create or import a BIP39 wallet · keys stay on this PC";

        foreach (var chain in ChainCatalog.All)
        {
            Accounts.Add(MakeLockedAccount(chain));
            Market.Add(MarketRowViewModel.Pending(chain));
        }

        foreach (var (sym, name, holdable) in ExtraMarketCoins)
            Market.Add(MarketRowViewModel.PendingCoin(sym, name, holdable));

        RestoreMarketCache(); // show last-seen prices instantly; the live refresh corrects them

        SelectedSendAsset = SendableAssets[0];
        SelectedWatchNetwork = WatchableNetworks[0];
        BuildGuide();
        LoadProfileImages();

        // Apply saved privacy routing before any network call goes out.
        PublicHttp.SetIpPreference(PublicHttp.ParseIpMode(_uiSettings.IpMode));
        // Tor-only kill-switch first: if it was left on, clearnet stays blocked until Tor connects, so
        // no startup request can leak before the proxy is up.
        PublicHttp.SetRequireProxy(_uiSettings.TorOnlyMode);
        if (EffectiveCustomProxy() is { } startupProxy)
        {
            PublicHttp.SetProxy(startupProxy);
            ProxyStatus = $"Routing through {startupProxy}";
            ProxyStatusColor = "#8FCB9B";
        }

        Fx.Symbol = Fx.SymbolFor(_uiSettings.Currency); // right symbol immediately; rate loads next
        _ = LoadWatchAddressesAsync();
        _ = ApplyCurrencyAsync(); // fetches the USD→currency rate, then refreshes market/holdings
        RefreshHoldings();
        RecalcBalance();
        StartAutoRefresh();
    }

    /// <summary>
    /// Market and balances refresh themselves on a timer — the user asked for no manual button.
    /// Market prices are public, so they update even while locked; balances only when unlocked.
    /// </summary>
    private void StartAutoRefresh()
    {
        try
        {
            _autoRefreshTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60),
            };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                await RefreshMarketAsync();
                if (IsUnlocked && !PendingPhraseBackup)
                {
                    await RefreshLiveDataAsync();
                }
            };
            _autoRefreshTimer.Start();
        }
        catch
        {
            // No Avalonia dispatcher (e.g. unit tests) — auto-refresh is a UI convenience only.
        }
    }

    public ObservableCollection<WalletAccountViewModel> Accounts { get; } = [];
    public ObservableCollection<HoldingRowViewModel> Holdings { get; } = [];
    public ObservableCollection<ActivityRowViewModel> Activity { get; } = [];
    /// <summary>The five most-recent events, for the Portfolio right-rail card.</summary>
    public ObservableCollection<ActivityRowViewModel> RecentActivity { get; } = [];
    /// <summary>Activity narrowed by the selected filter tab.</summary>
    public ObservableCollection<ActivityRowViewModel> FilteredActivity { get; } = [];
    /// <summary>Money movements only (sends, receives, swaps) — the Transactions section + history.</summary>
    public ObservableCollection<ActivityRowViewModel> Transactions { get; } = [];
    public bool HasTransactions => Transactions.Count > 0;

    public IReadOnlyList<string> ActivityFilters { get; } =
        ["All", "Transactions", "Connections", "Settings", "System"];
    [ObservableProperty] private string _activityFilter = "All";
    partial void OnActivityFilterChanged(string value) => RebuildFilteredActivity();

    // Roadmap §6: the merged Activity feed filters by asset, confirmation status and date range as well
    // as by category. Each dropdown re-narrows the same unified list (local events + real on-chain history).
    /// <summary>Assets present in the feed, "All" first — built from the rows so it never offers an empty filter.</summary>
    public ObservableCollection<string> ActivityAssets { get; } = ["All"];
    [ObservableProperty] private string _activityAssetFilter = "All";
    partial void OnActivityAssetFilterChanged(string value) => RebuildFilteredActivity();

    public IReadOnlyList<string> ActivityStatuses { get; } = ["All", "Confirmed", "Pending", "Failed"];
    [ObservableProperty] private string _activityStatusFilter = "All";
    partial void OnActivityStatusFilterChanged(string value) => RebuildFilteredActivity();

    public IReadOnlyList<string> ActivityDateRanges { get; } = ["All time", "Last 24h", "Last 7 days", "Last 30 days"];
    [ObservableProperty] private string _activityDateFilter = "All time";
    partial void OnActivityDateFilterChanged(string value) => RebuildFilteredActivity();

    /// <summary>When the on-chain history was last refreshed, shown on the Activity screen ("—" until synced).</summary>
    [ObservableProperty] private string _lastHistorySync = "—";
    /// <summary>True while on-chain history is being fetched, so the Activity screen can say "loading"
    /// instead of a bare "nothing here" (roadmap §6/§8 — the empty state was ambiguous).</summary>
    [ObservableProperty] private bool _historyLoading;
    /// <summary>True once at least one sync has completed, so the empty state can distinguish
    /// "still loading" from "synced, but genuinely nothing on-chain".</summary>
    [ObservableProperty] private bool _historySynced;

    /// <summary>What the total is made of — top assets by value, for the Portfolio-overview ring.</summary>
    public ObservableCollection<PortfolioSlice> PortfolioBreakdown { get; } = [];
    public bool HasBreakdown => PortfolioBreakdown.Count > 0;
    /// <summary>"N assets" line under the allocation legend — a small at-a-glance summary of the mix.</summary>
    public string AllocationSummary
    {
        get
        {
            var n = Holdings.Count(h => h.Value > 0);
            return n == 0 ? string.Empty : (n == 1 ? "1 asset" : $"{n} assets");
        }
    }

    /// <summary>NFT collections held at the wallet's Ethereum address (names + counts, no images).</summary>
    public ObservableCollection<NftHolding> Nfts { get; } = [];
    public bool HasNfts => Nfts.Count > 0;

    /// <summary>Stakeable coins the wallet holds keys for, with the network's typical (approximate)
    /// reward and how staking is done. Informational — not live positions.</summary>
    private static readonly IReadOnlyList<StakingOption> StakingCatalog =
    [
        new("ETH", "Ethereum", "~3–4%", "Beacon-chain staking (32 ETH solo) or a liquid-staking pool"),
        new("SOL", "Solana", "~6–7%", "Delegate to a validator — native, unbonds in a few days"),
        new("ADA", "Cardano", "~3%", "Delegate to a stake pool — native, no lock-up, keys stay yours"),
        new("TON", "Toncoin", "~3–4%", "Stake through a nominator pool"),
        new("TRX", "TRON", "~4–5%", "Freeze TRX for resources and vote for a Super Representative"),
        new("MATIC", "Polygon", "~4%", "Delegate to a validator on the Polygon staking contract"),
    ];

    /// <summary>Staking, personalised to what you actually hold: coins you own are shown first with an
    /// estimated yearly reward from their live value; the rest are listed as available. Rebuilt on every
    /// holdings refresh, so it's driven by your wallet, not a fixed table.</summary>
    public ObservableCollection<StakingRowViewModel> StakingRows { get; } = [];

    private void RebuildStaking()
    {
        var held = Holdings
            .GroupBy(h => h.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(x => x.Amount), Value: g.Sum(x => x.Value)),
                StringComparer.OrdinalIgnoreCase);

        var rows = StakingCatalog.Select(o =>
        {
            held.TryGetValue(o.Symbol, out var h);
            var has = h.Amount > 0;
            string hold;
            if (has)
            {
                var mid = AprMidpoint(o.Apr);
                var estYear = h.Value * mid / 100.0;
                hold = $"{Loc.Instance["staking.youHold"]} {h.Amount:0.####} {o.Symbol} · ~{FormatCompactMoney(estYear)}/yr";
            }
            else hold = "";
            return new StakingRowViewModel(o.Symbol, o.Name, o.Apr, o.Method, hold, has);
        })
        .OrderByDescending(r => r.HasHolding)
        .ToList();

        StakingRows.Clear();
        foreach (var r in rows) StakingRows.Add(r);
    }

    /// <summary>Rough midpoint of an APR string like "~3–4%" or "~6%" → 3.5 / 6.</summary>
    private static double AprMidpoint(string apr)
    {
        var nums = System.Text.RegularExpressions.Regex.Matches(apr ?? "", @"\d+(\.\d+)?")
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        return nums.Count == 0 ? 0 : nums.Average();
    }

    /// <summary>Drives the Activity empty-state; raised whenever the log changes.</summary>
    public bool HasActivity => Activity.Count > 0;

    public ObservableCollection<WatchAddress> WatchAddresses { get; } = [];

    /// <summary>Every coin the wallet accepts, with live price — readable while locked.</summary>
    public ObservableCollection<MarketRowViewModel> Market { get; } = [];

    /// <summary>Popular coins shown in Market beyond the wallet's own chains — priced + charted; their
    /// balances still surface via the EVM/token paths, so nothing here is a dead "adapter pending" row.</summary>
    // Holdable = the wallet actually derives an address for it (EVM coins share the 0x address; the
    // tokens live at the ETH/TRON address). XRP/DOT/BCH are market-only until each gets its own chain.
    private static readonly (string Symbol, string Name, bool Holdable)[] ExtraMarketCoins =
    [
        ("BNB", "BNB", true), ("MATIC", "Polygon", true), ("AVAX", "Avalanche", true),
        ("FTM", "Fantom", true), ("CRO", "Cronos", true),
        ("USDT", "Tether", true), ("USDC", "USD Coin", true), ("LINK", "Chainlink", true), ("UNI", "Uniswap", true),
        ("XRP", "XRP", false), ("DOT", "Polkadot", false), ("BCH", "Bitcoin Cash", false),
    ];

    /// <summary>Product news, shown in the News section. Curated, offline; no network needed.
    /// Click an item to read the full note. Newest first.</summary>
    /// <summary>Official Telegram channel — news, releases and contact.</summary>
    public string ChannelUrl => "https://t.me/UmbrellaWallet";

    public ObservableCollection<NewsItemViewModel> News { get; } =
    [
        new("4.0", "Version 4.0 — on-chain history, richer token pages, more",
            "A big update:\n\n" +
            "• On-chain transaction history. The Transactions tab now pulls your real history straight from the chain for your own addresses — including transactions from before you first opened the wallet. Covers TRX (native + USDT/TRC-20), Bitcoin, Ethereum and Litecoin, keyless and through Tor/your proxy.\n" +
            "• Richer token pages. Open any coin for 24h High / Low / Volume. Turn on the optional CoinGecko connector (Settings → Privacy — off by default) to also see Market Cap, FDV and Volume.\n" +
            "• Colour-tag your wallets (Settings → Wallets) — a coloured ring so you can tell them apart at a glance.\n" +
            "• Your new stickers everywhere — greeting by the logo, GhostPepe, encryption, NFT, Receive, the send animation, and up/down on the market chart. All toggle with the other animations.\n" +
            "• More privacy: lock on minimize, hide balances by default, custom SOCKS5 proxy, IPv4/IPv6, clipboard auto-clear.\n" +
            "• Lots of fixes: Top/Bottom nav, green-chart line colour, centred unlock, tidier tiles and Activity cards, toasts across the app, new Telegram news logo.\n\n" +
            "Still coming, done deliberately with your testing: in-page swap + swap between all pairs, Telegram NFTs, SOL/DOGE history, single-coin wallets, and per-coin logos. 146/146 tests pass.",
            "2026-08-15"),
        new("NEW", "Token pages now show 24h high, low and volume",
            "Version 3.5.1:\n\n" +
            "• Open any coin in Market and you'll see a stats row under the chart — 24h High, 24h Low and 24h Volume — in your chosen currency. It comes from the same price feed, so there's no new tracking and it still goes through Tor/your proxy.\n\n" +
            "Coming next as an optional, off-by-default connector (so nothing calls a third party unless you switch it on): richer token data (market cap, FDV, TVL, 52-week range) and an in-page swap widget, plus real on-chain transaction history.",
            "2026-08-15"),
        new("NEW", "A Uniswap-style mobile UI + a batch of interface fixes",
            "Version 3.5.0 — mobile polish and fixes from your screenshots:\n\n" +
            "• Uniswap-style phone nav. The mobile layout now has a floating pill bottom bar with five fixed tabs (Portfolio · Receive · Send · Market · More) — no more sideways scrolling. The rest of the sections open in a tidy 'More' sheet.\n" +
            "• Cleaner phone screen. The wide side panel is hidden on mobile, so it's one clean column instead of a cramped split.\n" +
            "• the fear logo now sits beside the umbrella on the welcome screen.\n" +
            "• Quick-action tiles no longer cut off their labels, and the Activity list now shows each event as its own rounded card with spacing.\n" +
            "• Toasts are quicker and stay top-centre.\n" +
            "• New: Hide balances by default (Settings → Security) — every unlock starts with amounts hidden.\n" +
            "• More non-custodial P2P/DEX venues: SushiSwap and Raydium.\n\n" +
            "Still ahead: real on-chain transaction history (including before you connected), Telegram-gift NFTs, one-coin wallets, and wider swap coverage. 139/139 tests pass.",
            "2026-08-15"),
        new("NEW", "A real phone layout, clearer backup, lock-on-minimize",
            "Version 3.4.2 — polish from your feedback:\n\n" +
            "• Mobile layout now feels like a phone. It has a proper bottom icon tab bar (scroll it for every section) instead of a squished desktop menu, and the dashboard actions wrap to 2×2. There's also a soft glow behind the the-fear logo on the welcome screen.\n" +
            "• Backup made clear. The recovery phrase and the optional Monero keys are now one card that explains, in plain words, that your 24 words are the real backup and the Monero keys are an advanced extra most people never touch.\n" +
            "• Activity feed cleaned up. It no longer logs a 'Sync · OK' line every minute — that was just noise.\n" +
            "• Lock on minimize. Settings → Privacy: lock the wallet the instant the window is minimized.\n" +
            "• More P2P/DEX venues, all non-custodial: CoW Swap, Matcha, Curve, Osmosis, plus Haveno (Monero), Vexl and LocalCoinSwap.\n" +
            "• More of the app follows your language (welcome screen and section headers).\n\n" +
            "139/139 tests pass.",
            "2026-08-15"),
        new("NEW", "Use Umbrella like a phone app on your PC",
            "Version 3.4:\n\n" +
            "• Mobile layout. Settings → Appearance → Mobile layout turns the whole wallet into a phone-style app on your desktop — a narrow centred column, a bottom tab bar, and a phone-sized window. Flip it off and you're back to the full wide desktop layout instantly.\n" +
            "• It remembers your setup. Mobile mode docks the menu to the bottom for that phone feel, but your saved menu position comes back untouched when you switch off.\n" +
            "• Tidier on a narrow screen. The dashboard's quick actions now wrap to a 2×2 grid instead of squashing four across, and 'Prices & charts' is translated in every language.\n\n" +
            "This is the first step toward a real phone build — the same layout that a future native Android app will use. 139/139 tests pass.",
            "2026-08-14"),
        new("SECURITY", "Security review, a custom proxy, and IP controls",
            "Version 3.3 — a hardening + privacy release:\n\n" +
            "• Security review. A full pass over the wallet's crypto and storage. The vault now rejects out-of-range key-derivation parameters, so a tampered or foreign vault file can no longer stall or exhaust the app when you try to unlock it. The screenshot/screen-share blackout, the on-device Argon2id + AES-256-GCM vault and the sign-then-wipe key handling were all re-verified.\n" +
            "• Custom proxy. Settings → Privacy now lets you route every request through your own SOCKS5 proxy — a VPN, an SSH tunnel or another Tor instance — instead of the bundled Tor. Enter host:port and apply; Tor and your proxy are mutually exclusive.\n" +
            "• IP version control. Force outgoing connections onto IPv4 or IPv6, or leave it automatic — handy on networks where one family is broken or leaks.\n" +
            "• Clipboard auto-clear. A copied address is now wiped from the clipboard after a delay you choose (off / 30s / 45s / 1m / 2m), so it doesn't linger for other apps to read.\n" +
            "• More of the app is translated. The onboarding screens (create / import / unlock / restore / back-up), the section titles and the holdings columns now follow your language in all six locales instead of showing English.\n" +
            "• Trustworthier installer. The Windows setup now shows the licence, carries proper publisher details, and on uninstall tells you exactly where your encrypted wallet data is kept and how to erase it yourself.",
            "2026-08-14"),
        new("NEW", "Custom lock screen + instant market",
            "Version 3.2:\n\n" +
            "• Lock-screen background is yours: Settings → Appearance → Lock screen — pick your own image, use the default, or turn it off for a flat lock screen.\n" +
            "• The Market now opens instantly with your last-seen prices instead of filling in dash-by-dash; live data updates in the background.\n\n" +
            "Still coming (in order of value): developer-fee routing on TRON/ETH/TON, more security options (custom proxy, IPv4/IPv6), a native Android build, Telegram-gift NFTs, deeper Swap/Staking, and full translations.",
            "2026-08-09"),
        new("NEW", "Your language by default + 13 more currencies",
            "Version 3.1:\n\n" +
            "• Fresh installs now follow your system language automatically — after a reinstall you're no longer dropped into English.\n" +
            "• 13 more display currencies (CAD, AUD, CHF, BRL, KRW, AED, KZT and more) — 23 in total.\n" +
            "• The the-fear logo is back with its blue background.\n\n" +
            "Still on the roadmap and coming next: a native Android build, lock-screen background customization, Telegram-gift NFTs, deeper Swap/Staking, more security options (custom proxy, IPv4/IPv6), and developer-fee routing on TRON/ETH/TON.",
            "2026-08-09"),
        new("NEW", "Faster balances, instant totals, cleaner alerts",
            "Speed and polish:\n\n" +
            "• Balances load much faster — they're fetched all at once instead of one by one, and the total appears right after the quick native pass.\n" +
            "• No more $0 flash: your last totals are cached on this device and shown instantly when you unlock or switch wallets, then refreshed in the background.\n" +
            "• Errors and notices now appear as a toast at the top-center of the window, so you actually see them.\n" +
            "• The the-fear mark and the Telegram-channel icon now use the real logo artwork (background removed).",
            "2026-08-09"),
        new("NEW", "Settings fully translated + easier wallet switching",
            "Polish across the app:\n\n" +
            "• Settings are fully translated now — the Wallets tab and the Danger zone were still English whatever language you picked; that's fixed.\n" +
            "• Clearer wording: \"Delete vault\" is now \"Erase from this PC\", and the danger zone explains that you can't actually delete a wallet — it lives on your recovery phrase. This only wipes this device's copy, which your phrase brings back.\n" +
            "• Switch wallets from anywhere: when the menu is at the top or bottom, there's now a wallet chip in the bar (shows the active wallet, one tap to switch) — no need to open Settings.\n" +
            "• Every theme has a distinct name now (27 total), and there's a new opt-in \"Aurora glow\" ambient animation alongside the rain and stickers.",
            "2026-08-09"),
        new("NOTICE", "Web version paused — desktop is the focus",
            "Heads-up on where Umbrella is going:\n\n" +
            "• The web version is paused and closed for an indefinite period. We're concentrating everything on the desktop apps — Windows and Linux now, Android planned — where your keys stay fully on your own device with no server in the middle.\n" +
            "• Nothing changes for your wallet: it was always self-custody and local-first. If you used the web preview, your funds live on-chain under your recovery phrase, not on any server.\n" +
            "• Follow the official Telegram channel for news, releases and contact: t.me/UmbrellaWallet — that's the one official channel; ignore anything else claiming to be us.",
            "2026-08-09"),
        new("3.0", "Version 3.0 — Telegram/TON import, full translation, more themes",
            "A big one:\n\n" +
            "• Import your Telegram / TON wallet. Umbrella now speaks the TON-native recovery standard (Telegram Wallet, Tonkeeper, TON Space). Paste that 24-word phrase and it imports as a Toncoin wallet showing the very same TON address those wallets do — receive and send included. The derivation is pinned byte-for-byte against the official @ton libraries.\n" +
            "• Fully translated menu. Buy, Swap, P2P & DEX, NFTs, Staking and Transactions are now translated too — no more English mixed into the Ukrainian sidebar.\n" +
            "• Turn animations on/off individually — the ambient rain and the stickers each have their own switch in Settings → Appearance.\n" +
            "• Six new themes: Solana, Ethereum, Monero, Kraken, Nord and Dracula — 27 in total.\n\n" +
            "138/138 tests pass, including a TON reference vector and every chain's derivation, signing and fees.",
            "2026-08-09"),
        new("NEW", "Fixed: create/import was stuck — plus password tools",
            "A blocking bug and two much-requested features:\n\n" +
            "• Fixed the stuck onboarding. Adding a wallet could leave the create/import screen demanding a password it had hidden, which blocked creating OR importing any wallet. Your app password is now kept through the add step and reused directly, so it can't be wiped out from under you.\n" +
            "• Self-healing. If an add-wallet was interrupted, the app now falls back to a wallet that actually exists instead of stranding you on the welcome screen.\n" +
            "• Change password. Settings → Wallets lets you set a new app password; every wallet on your current password is re-encrypted, so one password still unlocks them all.\n" +
            "• Forgot your password? The unlock screen now has \"Forgot your password?\" — enter your recovery phrase and a new password to restore access. Same phrase, same funds.\n\n" +
            "On going fully password-less: we don't offer that, because it would leave your seed effectively unencrypted on this PC. Pick a simple password and write it down — and now, if you forget it, your phrase gets you back in.",
            "2026-08-08"),
        new("NEW", "Importing wallets just got much easier",
            "Bringing another wallet in should just work now:\n\n" +
            "• Paste-proof import. A recovery phrase from any BIP39 wallet — Kraken Wallet, MetaMask, Trust, Ledger, Exodus, Coinbase Wallet and most others — imports even if you paste it with numbers (\"1. word 2. word\"), commas or line breaks. The words are pulled out cleanly.\n" +
            "• It tells you what's wrong. If one word is mistyped, the error names that exact word instead of a vague \"invalid phrase\".\n" +
            "• Telegram / TON. If a phrase looks right but is rejected, it's almost certainly from Telegram Wallet / Tonkeeper — those use a non-BIP39 standard and can't be imported here. Your coins are safe in that wallet; native TON import is planned.\n\n" +
            "Tip: after importing, your BTC/ETH/SOL and other main-chain addresses match the source wallet. If a balance looks empty, it may be on a network Umbrella doesn't sync yet.",
            "2026-08-08"),
        new("NEW", "One password for all wallets, plus fixes",
            "Follow-ups from your feedback:\n\n" +
            "• One login password. Adding a wallet now reuses your app password instead of asking for a new one, and switching between wallets unlocks instantly. The password lives only in memory while you're unlocked and is wiped on lock.\n" +
            "• Telegram / TON wallets. If you paste a recovery phrase from Telegram Wallet or Tonkeeper and it says \"invalid\", that's expected — TON wallets use the same words but a different (non-BIP39) standard, so they can't be imported here. Your coins are safe in that wallet. The message now explains this, and full TON import is planned.\n" +
            "• Rain, fixed. The ambient streaks fell in stiff synchronised rows; now they drift at varied speeds like real rain, and stay subtle.\n" +
            "• Settings search. A search box up top finds any setting and jumps to it.",
            "2026-08-08"),
        new("NEW", "Multiple wallets, like Binance accounts",
            "You can now keep several independent wallets on this device and switch between them:\n\n" +
            "• Open Settings → Wallets (or tap ⇄ Wallets in the sidebar) to add a new wallet, switch, rename or remove one.\n" +
            "• Each wallet is completely independent — its own 24-word phrase and its own password. Switching locks the current wallet and asks for the other's password, so nothing is ever mixed up.\n" +
            "• Your existing wallet is automatically kept as “Main” and can never be deleted by accident; even a corrupt index falls back to it, so you can't be locked out.\n\n" +
            "This is Stage 1 — separate wallets. Next up: sub-accounts inside a single seed (one phrase, Account 1/2/3 like MetaMask).",
            "2026-08-08"),
        new("NEW", "Buy crypto with a card, plus a sidebar fix",
            "A quick follow-up:\n\n" +
            "• New Buy section. Top up with a card or bank transfer through regulated on-ramps that send crypto straight to your own address — Onramper (compares them all), MoonPay, Ramp, Transak, Banxa, Mercuryo and Guardarian. There's a 3-step guide and one-tap copy of your receive address. Umbrella holds nothing and takes no fee.\n" +
            "• Fixed a sidebar glitch. On shorter windows the menu could run into the footer (Settings overlapping the Lock button). The menu now scrolls on its own and the footer stays put at any window height.\n" +
            "• NFTs now explain where they come from — read-only from the Ethereum chain against your own address, with more chains planned.\n\n" +
            "On-ramps are independent third parties listed for convenience, not endorsements, and will ask for ID. No on-ramp ever needs your recovery phrase.",
            "2026-08-08"),
        new("NEW", "Pro charts, a P2P & DEX hub, and Market search",
            "This release is all about trading and looking at prices like a pro:\n\n" +
            "• Charts got serious. Hover any coin's chart for a crosshair with a live price + time readout, flip between Line and Candles, and enjoy a soft gradient fill and a change-over-window badge (first→last). It feels like a real trading terminal now.\n" +
            "• New P2P & DEX section. A curated hub of non-custodial ways to trade: the in-wallet THORChain swap first, then on-chain DEXes (Uniswap, THORSwap, Jupiter, 1inch, PancakeSwap) and peer-to-peer escrow (Bisq, Hodl Hodl, RoboSats, Peach). Every one keeps custody with you; each shows its model and opens in your browser.\n" +
            "• Market search. Filter the coin list by name or ticker instantly, with one-tap refresh — the prices keep updating live while you type.\n\n" +
            "We list third-party venues for convenience, not as endorsements — Umbrella takes no fee and holds nothing. No trade ever needs your recovery phrase.",
            "2026-08-08"),
        new("NEW", "History that stays, smart wallet linking, and 8 brand themes",
            "A round of fixes and polish:\n\n" +
            "• History persists — your activity and transactions no longer vanish when you close the wallet. They're kept on this device only (never a server), with real timestamps, and you can wipe them any time in Settings → Danger zone.\n" +
            "• Linking a wallet now auto-detects the network from the address you paste — a T… address is tracked as TRON, bc1… as Bitcoin, 0x… as EVM. (Linking watches an external address read-only; it never changes your own receive addresses.)\n" +
            "• Danger zone does more than delete: Clear history and Disconnect all (removes linked addresses/exchanges), both keeping your vault and funds.\n" +
            "• Eight brand themes with the real colours — Uniswap (exact pink), Binance, Bybit, OKX, Telegram, TON · Gram, TRON, WhiteBit and Bitcoin — 21 themes in all.\n\n" +
            "Umbrella is free, independent, self-custody software by the fear — not affiliated with any exchange or brand; those theme names just identify a colour style.",
            "2026-08-01"),
        new("NEW", "Your currency, a Transactions view, and a fixed Market",
            "Three things people asked for:\n\n" +
            "• Display currency — Settings → Appearance now lets you show everything in USD, EUR, UAH (₴), RUB, GBP, CNY, JPY and more. The total, holdings, breakdown and market all convert; your coins don't change, only how their value reads. The rate is fetched anonymously (through Tor when it's on).\n" +
            "• Transactions — a dedicated section lists your money movements (sends, receives, swaps) with a one-click copy of each explorer link, and the Activity feed now has a filter (All / Transactions / Connections / Settings / System).\n" +
            "• Market — prices now come from Binance first (CoinGecko's free tier was rate-limiting and blanking rows), so every coin prices and charts again; the market-only coins (XRP/DOT/BCH) now say so honestly.\n\n" +
            "Also: two new themes — Uniswap (magenta-pink) and Ocean (teal).",
            "2026-08-01"),
        new("NEW", "Send on every EVM chain — BNB, Polygon, Avalanche and more",
            "You can now send native BNB (BSC), MATIC (Polygon), AVAX (Avalanche), FTM (Fantom) and CRO (Cronos) — not just Ethereum. They all share your 0x address, so a wallet imported from MetaMask can now spend across six EVM networks from one place.\n\n" +
            "The signing is the exact same EIP-155 code that's pinned byte-for-byte to the official Ethereum test vector — only the chain id, RPC and explorer change, so a wrong byte can never move funds. Nonce, gas and the balance check come from each chain's public RPCs (with fallbacks), and every request goes through the built-in Tor when it's on, so sending stays anonymous.",
            "2026-08-01"),
        new("NEW", "Many more coins, every token, NFTs, staking",
            "The wallet now surfaces essentially everything you hold.\n\n" +
            "• Coins — on top of BTC, ETH, LTC, DOGE, SOL, TON, TRON, ADA and XMR, every major EVM network at the same address now shows: BNB (BSC), MATIC (Polygon), AVAX, FTM (Fantom), CRO (Cronos), and ETH on the Arbitrum, Optimism and Base L2s.\n" +
            "• Tokens — every ERC-20 (Ethereum) and TRC-20 (TRON) token appears automatically: USDC, USDT, LINK, UNI, DAI, SHIB, PEPE and the rest. This is what most 'my balance is missing' reports were.\n" +
            "• NFTs — the ERC-721/1155 collections on your Ethereum address are listed (names and counts; no images are fetched, so it never leaks your IP).\n" +
            "• Staking — the coins you can stake with your keys, with each network's typical reward and how to do it.\n" +
            "• Portfolio overview — the ring now shows a real breakdown of what your balance is made of, with a live 24h change; hiding the balance now blanks every figure everywhere, not just the top total.\n" +
            "• Activity — sends, swaps, connecting a wallet or exchange, theme/language/settings changes and Tor toggles are all logged now.",
            "2026-08-01"),
        new("NEW", "Cardano (ADA) sending is live",
            "You can now send ADA, not just receive it. The Cardano transaction is built, signed and broadcast entirely on this device.\n\n" +
            "Cardano uses its own extended-key signature scheme (BIP32-Ed25519), so it was implemented from the elliptic-curve operations and then checked byte-for-byte against Emurgo's cardano-serialization-lib — the transaction body, its hash, the signature and the final signed transaction all match the reference exactly, so the network runs precisely the transfer the reference would build. UTXOs, the validity window and submission go through Koios, and change returns to you. ADA is now a full coin (receive, balance and send); Monero stays receive-only.",
            "2026-08-01"),
        new("NEW", "In-wallet swaps + a security hardening pass",
            "You can now swap coins inside the wallet — cross-chain, over THORChain. It's decentralised and non-custodial: no account, no API key, no KYC. Your coin is sent to a THORChain vault with a signed memo, and the network delivers the swapped coin straight to your own address here. Nobody ever holds your funds. Pay from BTC or LTC; receive BTC, ETH, LTC or DOGE — with a live quote (rate, fee, ETA) you review before anything is sent, and a fresh re-quote at the moment you confirm.\n\n" +
            "Under the hood, a security pass fixed two fund-safety bugs: TON recipient addresses now have their checksum verified (a mistyped address is rejected instead of silently sending to the wrong account), and the TON cell parser was hardened against malformed data. Added a fuzz harness over the address/parser code, CodeQL + secret-scanning in CI, and fixed a web dependency advisory.",
            "2026-07-31"),
        new("UPDATE", "Real candlesticks, backdrops, and a floating dock",
            "The Market chart is now real OHLC candlesticks (green up, red down) drawn from live Binance data — not a flat single-colour line. Prices and the 24h change were always live (CoinGecko); nothing is faked.\n\n" +
            "The sidebar and the lock screen now have artwork behind them instead of flat black, and you can set your own via Settings → Appearance → Profile. The bottom bar became a floating dock, cards lift and glow on hover, and the hero drifts gently with the cursor.",
            "2026-07-31"),
        new("UPDATE", "NFTs and Staking sections, brighter hero",
            "The navigation now includes NFTs and Staking to match the full design — both with honest 'coming soon' pages for now (the features aren't built yet, so nothing is faked). The Portfolio hero backdrop is brighter, with the glowing portal to the right by the brand mark.",
            "2026-07-30"),
        new("UPDATE", "Dashboard rail, price ticker, and a monochrome primary",
            "The Portfolio now has a right-hand rail — a portfolio-overview ring, recent activity, and a live market overview — and a price ticker runs along the bottom, matching the reference dashboard.\n\n" +
            "The primary theme is now monochrome noir (near-black with cool-white accents, so the main buttons read white on black); the red editorial look lives on as the Ember theme. This is a structural pass — spacing and the overview ring will be refined next.",
            "2026-07-30"),
        new("UPDATE", "A cinematic new Portfolio hero",
            "The balance now sits on a cinematic backdrop — a dark horizon with a glowing portal — with the brand mark beside it, and the quick actions became wide cards (icon, title and a short subtitle: Get crypto, Send crypto, Link wallets, Prices & charts).\n\n" +
            "This is the first step of a bigger dashboard refresh toward the reference design; the right-hand rail (portfolio breakdown, market overview, recent activity) and the live price ticker are next.",
            "2026-07-30"),
        new("UPDATE", "Themes now restyle the buttons too — and Linux is here",
            "Themes no longer change only the colours: the primary buttons now take on each theme's accent (orange on The fear, cyan on Blue, mint on Green, violet on Gradient, teal on Slate), with the label colour picked automatically for contrast, and the quick-action tiles glow in the accent on hover.\n\n" +
            "The wallet also ships for Linux now, built from the same code as Windows — identical features, themes and bundled Tor/Monero. The mobile experience is the web app (it's responsive and runs as a Telegram mini-app); there is no separate Android build.",
            "2026-07-30"),
        new("UPDATE", "A cleaner window and a full theme refresh",
            "The window chrome is gone — no top strip, no bottom status bar — so the wallet is all content, edge to edge. Status and errors now surface where they belong (the unlock form and the review step), not in a bar.\n\n" +
            "Every theme except the primary was re-skinned to match the app's mood: neon-cyan glass (Blue), mint/emerald (Green), vivid violet (Gradient) and teal cyber (Slate), plus the red editorial primary (The fear · noir) and Ember. Coin badges now show each coin's own symbol mark.",
            "2026-07-30"),
        new("UPDATE", "Umbrella 2.1 — TON sending is live",
            "You can now send TON, not just receive it. The wallet builds and signs the v4R2 transfer on this device and broadcasts only the signed bytes.\n\n" +
            "The transaction is checked byte-for-byte against the reference @ton library — the order and signing-message hashes and the signatures all match — so the network runs exactly the transfer the reference would produce. The very first send from a fresh wallet also deploys it in the same transaction, automatically.\n\n" +
            "Cardano (ADA) sending is the next chain to get the same treatment.",
            "2026-07-30"),
        new("UPDATE", "A moodier look — glass, rounder edges and ambient rain",
            "The interface leans into the theme: buttons and tiles get a glassy top-edge sheen and larger radii, cards round further, and a faint 'rain' drifts over the window.\n\n" +
            "All of it respects the motion toggle in Settings → Appearance — turn animations off and the rain settles with everything else. There's a new Ember (red) theme to match, and the top bar is cleaner.",
            "2026-07-30"),
        new("GUIDE", "Make it yours — name your wallet and set auto-lock",
            "Settings now lets you name this wallet (the name shows in the top bar) and choose exactly when it auto-locks after you step away — 1, 5, 15, 30 or 60 minutes, or off entirely if you prefer.\n\n" +
            "Auto-lock wipes the decrypted seed from memory on idle; unlocking again needs your vault password.",
            "2026-07-30"),
        new("UPDATE", "Umbrella 2.0 — TON and Cardano join the wallet",
            "TON and Cardano (ADA) now derive real receive addresses from your existing seed phrase — no separate wallet needed.\n\n" +
            "TON uses the standard wallet v4R2 contract; Cardano uses Icarus / CIP-1852. Both were checked byte-for-byte against the reference libraries (tonweb and Cardano's own serialization library), so the addresses match what a compatible wallet produces — your funds are never sent to an address you can't control.\n\n" +
            "For now TON and ADA are receive-only (you can see the address and its live balance). Sending for both is the next step. Because the keys come from your seed, the funds are always recoverable in any compatible wallet (Trust Wallet for TON, Eternl for ADA).",
            "2026-07-28"),
        new("SECURITY", "Fully anonymous — no Google, nothing phones home",
            "The web wallet no longer loads anything from Google. Fonts are self-hosted, so simply opening the wallet contacts no third party at all.\n\n" +
            "There is no account, no email, no OAuth, no analytics and no telemetry anywhere. Combined with the built-in Tor switch, opening and using the wallet leaks nothing about you.",
            "2026-07-28"),
        new("UPDATE", "A cleaner desktop — new icon and tidy full-screen layout",
            "A new black low-poly umbrella icon replaces the old one. The content now sits in a centred column with a sensible maximum width, so a maximised or full-screen window looks tidy instead of stretching everything edge-to-edge. Buttons are a touch bolder.",
            "2026-07-28"),
        new("GUIDE", "How your keys are protected",
            "Your 24-word seed is generated on this computer and encrypted with Argon2id (a memory-hard password hash) plus AES-256-GCM before it ever touches the disk. The vault password never leaves this device, and neither does the seed.\n\n" +
            "Balances are read straight from public block explorers — no API keys, no middle-man — and hidden behind Tor when it's on. The seed is a standard BIP39 phrase, so you can restore it in any compatible wallet if you ever need to.",
            "2026-07-27"),
        new("SECURITY", "Tor is built into the wallet",
            "One switch in Settings → Privacy routes all wallet traffic through the Tor network — no separate install. It runs on its own port so your Tor Browser is untouched. Your IP stays out of your finances.",
            "2026-07-20"),
        new("UPDATE", "Full Monero wallet, powered by Monero's own engine",
            "XMR is a first-class coin here: real private balance and sending, with the Monero daemon running locally. Your keys never leave this device. Because amounts are hidden on-chain, Settings can export your address and spend/view keys for 'Restore from keys' in Feather or the Monero CLI.",
            "2026-07-18"),
        new("GUIDE", "Why a USDT (TRC-20) transfer can cost several dollars",
            "That fee is TRON's energy cost, not ours. A fresh, unstaked TRON account burns TRX for every USDT transfer, which can be $3–8. To avoid it: stake TRX to get free energy, or use USDT on a cheaper network (ERC-20 gas, or USDC on Solana for cents).",
            "2026-07-15"),
        new("UPDATE", "Ten themes, six languages, movable navigation",
            "Make the wallet yours: pick from ten colour themes, six interface languages (EN/UK/RU/ZH/ES/DE), and park the navigation panel on any edge — all in Settings.",
            "2026-07-12"),
    ];

    // Clicking a news item opens it as a full-article overlay.
    [ObservableProperty] private NewsItemViewModel? _selectedNews;
    public bool IsNewsOpen => SelectedNews is not null;
    partial void OnSelectedNewsChanged(NewsItemViewModel? value) => OnPropertyChanged(nameof(IsNewsOpen));

    [RelayCommand]
    private void OpenNews(NewsItemViewModel? item) => SelectedNews = item;

    [RelayCommand]
    private void CloseNews() => SelectedNews = null;

    /// <summary>
    /// The single source of truth for which symbols this build can actually build, sign and
    /// broadcast. Both the Send picker (<see cref="SendableAssets"/>) and the Send guard read this,
    /// and a test pins them together — so the UI can never offer a send the code rejects (the old
    /// ADA / EVM bug) nor hide one it supports.
    /// </summary>
    public static readonly IReadOnlySet<string> SendableSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "BTC", "LTC", "BCH", "DOGE",                 // UTXO HD wallet (BCH signs with SIGHASH_FORKID)
        "ETH", "BNB", "MATIC", "AVAX", "FTM", "CRO", // Ethereum + EVM side-chains (shared key/address)
        "ARB", "BASE", "OP",                         // Ethereum L2 rollups — native ETH, same 0x address
        "SOL", "TON", "ADA",                         // account-based
        "TRX", "USDT",                               // TRON + TRC-20
        "XMR",                                       // Monero (local wallet-rpc)
    };

    /// <summary>
    /// Assets that can actually be sent, as a pick-list. Typing a ticker by hand is how people
    /// send on the wrong network, so the UI only offers what this build can really broadcast — the
    /// symbols here are pinned to <see cref="SendableSymbols"/> by a test.
    /// </summary>
    public IReadOnlyList<SendOption> SendableAssets { get; } =
    [
        new("ETH", "Ethereum", "Ethereum network (ERC-20 compatible)"),
        new("BTC", "Bitcoin", "Bitcoin network · native SegWit"),
        new("LTC", "Litecoin", "Litecoin network · native SegWit"),
        new("BCH", "Bitcoin Cash", "Bitcoin Cash network · CashAddr"),
        new("DOGE", "Dogecoin", "Dogecoin network · UTXO spend (BlockCypher)"),
        new("SOL", "Solana", "Solana network"),
        new("TON", "Toncoin", "TON network · wallet v4R2"),
        new("XMR", "Monero", "Monero network · needs the Monero service on"),
        new("TRX", "TRON", "TRON network"),
        new("USDT", "Tether (TRC-20)", "TRON network · fee paid in TRX"),
        new("ADA", "Cardano", "Cardano network"),
        new("BNB", "BNB", "BNB Smart Chain (BEP-20 address)"),
        new("MATIC", "Polygon", "Polygon network"),
        new("AVAX", "Avalanche", "Avalanche C-Chain"),
        new("FTM", "Fantom", "Fantom Opera"),
        new("CRO", "Cronos", "Cronos EVM"),
        // Ethereum L2 rollups — the coin is ETH, on the same 0x address; only the network differs.
        new("ARB", "ETH · Arbitrum", "Arbitrum One · native ETH (same 0x address)"),
        new("BASE", "ETH · Base", "Base · native ETH (same 0x address)"),
        new("OP", "ETH · Optimism", "Optimism · native ETH (same 0x address)"),
    ];

    /// <summary>Networks a watch-only address can be added for.</summary>
    public IReadOnlyList<SendOption> WatchableNetworks { get; } =
    [
        new("ETH", "Ethereum", "Ethereum network (ERC-20)"),
        new("BTC", "Bitcoin", "Bitcoin network"),
        new("LTC", "Litecoin", "Litecoin network"),
        new("DOGE", "Dogecoin", "Dogecoin network"),
        new("TRC20", "TRON / USDT", "TRON network — also reads USDT (TRC-20)"),
        new("SOL", "Solana", "Solana network"),
    ];

    public string VaultLocation => _vault.VaultPath;
    public bool IsPortfolio => ActiveSection == "Portfolio";

    /// <summary>The 330px right rail only makes sense on the wide desktop layout — on the narrow phone
    /// layout it would crowd the content, so it's hidden there and the column stays clean.</summary>
    public bool ShowSideRail => IsPortfolio && !MobileMode;

    /// <summary>The mobile "More" sheet: overflow sections that don't fit the 5-slot bottom bar.</summary>
    [ObservableProperty] private bool _isMoreSheetOpen;

    [RelayCommand]
    private void ToggleMoreSheet() => IsMoreSheetOpen = !IsMoreSheetOpen;

    [RelayCommand]
    private void CloseMoreSheet() => IsMoreSheetOpen = false;
    public bool IsReceive => ActiveSection == "Receive";
    public bool IsSend => ActiveSection == "Send";
    public bool IsSwap => ActiveSection == "Swap";
    public bool IsActivity => ActiveSection == "Activity";
    public bool IsTransactions => ActiveSection == "Transactions";
    public bool IsSettings => ActiveSection == "Settings";
    /// <summary>Asset details: one coin's holding, price, capabilities and its own activity.</summary>
    public bool IsAsset => ActiveSection == "Asset";

    /// <summary>Security Center: one screen that reports what is actually protecting the wallet.</summary>
    public bool IsSecurity => ActiveSection == "Security";
    public bool IsConnect => ActiveSection == "Connect";
    public bool IsMarket => ActiveSection == "Market";
    public bool IsNews => ActiveSection == "News";
    public bool IsNfts => ActiveSection == "Nfts";
    public bool IsStaking => ActiveSection == "Staking";
    public bool IsP2p => ActiveSection == "P2p";
    public bool IsBuy => ActiveSection == "Buy";
    // Discover is a hub over Market + the external Buy / P2P·DEX catalogues + News, so the primary nav
    // stays focused on wallet actions (roadmap §6.1).
    public bool IsDiscover => ActiveSection == "Discover";
    // The Discover nav item stays highlighted while the user is inside any of its sub-pages.
    public bool IsDiscoverGroup => IsDiscover || IsMarket || IsBuy || IsP2p || IsNews;
    // Five-item primary nav (§6.1): Receive/Send live under Wallet, Transactions under Activity, so
    // those nav items stay highlighted while the user is on a sub-screen reached from them.
    public bool IsWalletGroup => IsPortfolio || IsReceive || IsSend;
    public bool IsActivityGroup => IsActivity || IsTransactions;

    // --- Onboarding state machine: each is a full-screen page, sidebar only in the workspace ---
    public bool IsWelcomeStage => !HasVault && SetupStage == "Welcome";
    public bool IsCreateStage => !HasVault && SetupStage == "Create";
    public bool IsImportStage => !HasVault && SetupStage == "Import";
    public bool IsUnlockStage => HasVault && !IsUnlocked;
    public bool IsBackupStage => IsUnlocked && PendingPhraseBackup;
    public bool IsWorkspace => IsUnlocked && !PendingPhraseBackup;
    public bool ShowSidebar => IsWorkspace;

    // --- Settings (real values, not decoration) -------------------------------
    public string VaultCryptoLabel =>
        "Argon2id · m=64 MiB · t=4 · p=2 → AES-256-GCM (AEAD, versioned associated data)";

    public string SeedSchemeLabel => "BIP39 24-word · 256-bit entropy · RNG from OS CSPRNG";

    public string SupportedChainsLabel =>
        string.Join(", ", ChainCatalog.Supported.Select(c => c.Symbol));

    public string PlannedChainsLabel =>
        string.Join(", ", ChainCatalog.Planned.Select(c => c.Symbol));

    public string NetworkLabel =>
        "Public RPC / explorers, no API keys: cloudflare-eth.com, blockstream.info, " +
        "litecoinspace.org, blockcypher.com, tronscanapi.com";
    public string BalanceDisplayMain => IsBalanceHidden ? "•••••••" : TotalBalanceMain;
    public string BalanceDisplayCents => IsBalanceHidden ? "" : $".{TotalBalanceCents}";
    public string HideBalanceLabel => IsBalanceHidden ? "Show" : "Hide";
    /// <summary>False while the balance is hidden — used to blank every money figure, not just the total.</summary>
    public bool AreValuesVisible => !IsBalanceHidden;

    partial void OnActiveSectionChanged(string value)
    {
        NotifySectionFlags();
        if (value == "Receive" && IsUnlocked)
        {
            SelectFirstReceive();
        }

        // The Security Center reads live state, so it is rebuilt every time it is opened rather than
        // cached — a stale "protected" row would be worse than no row at all.
        if (value == "Security") RefreshSecurityChecks();
    }

    partial void OnHasVaultChanged(bool value) => NotifySectionFlags();
    partial void OnIsUnlockedChanged(bool value)
    {
        NotifySectionFlags();
        // If the user asked for it, every unlock starts with balances hidden.
        if (value && _uiSettings.HideBalancesDefault) IsBalanceHidden = true;
    }
    partial void OnSetupStageChanged(string value) => NotifySectionFlags();
    partial void OnPendingPhraseBackupChanged(bool value) => NotifySectionFlags();

    // Typing must visibly clear the error and move the strength meter. Without this the
    // form gave no feedback at all and a rejected password looked like a dead button.
    partial void OnPasswordChanged(string value)
    {
        FormError = string.Empty;
        OnPropertyChanged(nameof(PasswordMeterLabel));
        OnPropertyChanged(nameof(PasswordMeterColor));
        OnPropertyChanged(nameof(CanSubmitVaultForm));
    }

    partial void OnConfirmPasswordChanged(string value)
    {
        FormError = string.Empty;
        OnPropertyChanged(nameof(PasswordMeterLabel));
        OnPropertyChanged(nameof(CanSubmitVaultForm));
    }

    partial void OnFormErrorChanged(string value) => OnPropertyChanged(nameof(HasFormError));

    // The pickers drive the underlying chain strings, so nothing downstream has to change.
    partial void OnSelectedSendAssetChanged(SendOption? value)
    {
        if (value is not null)
        {
            SendChain = value.Symbol;
            SendError = string.Empty;
            HasSendQuote = false;
        }
        SendFiatAmount = string.Empty; // a fresh asset starts the fiat quick-entry empty
        OnPropertyChanged(nameof(SelectedSendBalance));
        OnPropertyChanged(nameof(SelectedSendBalanceLabel));
        OnPropertyChanged(nameof(SendAmountFiat));
        OnPropertyChanged(nameof(FiatInputAvailable));
        OnPropertyChanged(nameof(SendFiatCoinEquiv));
        RebuildSendAddressBook();
        ValidateSendAddress();
        ResetCoinControl();
    }

    // --- Send financial transparency (§6.3): show the available balance and a fee-aware Max. ---
    private WalletAccountViewModel? SelectedSendAccount() =>
        Accounts.FirstOrDefault(a => a.Symbol == (SelectedSendAsset?.Symbol ?? string.Empty)
            && a.SupportStatus is "Ready" or "Receive only" && IsRealAddress(a.Address));

    public decimal SelectedSendBalance => (decimal)(SelectedSendAccount()?.Amount ?? 0d);

    public string SelectedSendBalanceLabel => SelectedSendAsset is null
        ? string.Empty
        : $"{Loc.Instance["send.available"]}: {Fmt(SelectedSendBalance)} {SelectedSendAsset.Symbol}";

    // --- Send review breakdown (§4): full destination, amount + fiat, kept separate from the fee. ---
    /// <summary>The destination shown in review, ALWAYS in full (never shortened) so the user can verify
    /// every character — long Monero addresses included.</summary>
    [ObservableProperty] private string _sendReviewTo = string.Empty;
    [ObservableProperty] private string _sendReviewAmount = string.Empty;
    [ObservableProperty] private string _sendReviewFiat = string.Empty;
    /// <summary>Plain statement of what actually leaves the wallet, so the total debit reads separately
    /// from the network fee above it.</summary>
    [ObservableProperty] private string _sendReviewDebit = string.Empty;

    // --- Privacy Radar (local): a per-send privacy read, chain-analysis FOR the user. Shown on the
    // review for UTXO chains, where spending from several addresses at once links them on-chain. ---
    /// <summary>True when a privacy assessment is available for the pending send (UTXO chains).</summary>
    [ObservableProperty] private bool _hasSendPrivacy;
    /// <summary>One-line privacy verdict (e.g. "Strong privacy — …").</summary>
    [ObservableProperty] private string _sendPrivacyHeadline = string.Empty;
    /// <summary>Accent colour for the verdict: green (strong), amber (moderate), red (weak).</summary>
    [ObservableProperty] private string _sendPrivacyColor = "#8FCB9B";
    /// <summary>The findings behind the verdict, worst-case first, each explaining what it means.</summary>
    public System.Collections.ObjectModel.ObservableCollection<SendPrivacyFindingVm> SendPrivacyFindings { get; } = new();

    private const string PrivGood = "#8FCB9B";
    private const string PrivWarn = "#E7CA83";
    private const string PrivBad = "#E09A9A";

    /// <summary>
    /// Runs the local <see cref="Umbrella.Wallet.Core.Safety.SendPrivacyInspector"/> over the addresses
    /// funding a pending send and surfaces the result on the review. Pure/offline — no network.
    /// </summary>
    private void ApplySendPrivacy(IEnumerable<string> inputAddresses)
    {
        var report = Umbrella.Wallet.Core.Safety.SendPrivacyInspector.Inspect(
            inputAddresses.ToList(), TorEnabled);

        var L = Loc.Instance;
        SendPrivacyFindings.Clear();
        foreach (var f in report.Findings)
        {
            // The finding carries a language-neutral code; the wording lives in the translation table
            // (§8.2 — no hardcoded English in the send flow). Only "linkMany" needs the address count.
            var title = L[$"priv.{f.Code}.title"];
            var detail = f.Code == "linkMany"
                ? string.Format(L["priv.linkMany.detail"], f.Count)
                : L[$"priv.{f.Code}.detail"];
            SendPrivacyFindings.Add(new SendPrivacyFindingVm(
                f.Glyph, title, detail, f.IsWeakness ? PrivWarn : PrivGood));
        }

        var levelKey = report.Level switch
        {
            Umbrella.Wallet.Core.Safety.SendPrivacyLevel.Strong => "strong",
            Umbrella.Wallet.Core.Safety.SendPrivacyLevel.Moderate => "moderate",
            _ => "weak",
        };
        SendPrivacyHeadline = L[$"priv.headline.{levelKey}"];
        SendPrivacyColor = report.Level switch
        {
            Umbrella.Wallet.Core.Safety.SendPrivacyLevel.Strong => PrivGood,
            Umbrella.Wallet.Core.Safety.SendPrivacyLevel.Moderate => PrivWarn,
            _ => PrivBad,
        };
        HasSendPrivacy = true;
    }

    /// <summary>Live fiat estimate for the amount being typed, shown under the amount field.</summary>
    public string SendAmountFiat
    {
        get
        {
            if (SelectedSendAsset is null) return string.Empty;
            // The estimate has to read the field exactly as the send path will, or the two disagree.
            if (!AmountInput.TryParsePositive(SendAmount, out var amt)) return string.Empty;
            return FiatEquivalentLabel(SelectedSendAsset.Symbol, amt);
        }
    }

    /// <summary>"≈ $123.45" for an asset amount, from the latest fetched USD price (stablecoins = $1).
    /// Empty when there is no price, so the UI simply omits it rather than showing a wrong number.</summary>
    private string FiatEquivalentLabel(string symbol, decimal amount)
    {
        decimal usdEach;
        if (symbol is "USDT" or "USDC") usdEach = 1m;
        else if (_priceUsd.TryGetValue(symbol, out var p) && p.Usd > 0) usdEach = p.Usd;
        else return string.Empty;
        return "≈ " + Fx.Money((double)(amount * usdEach));
    }

    // --- Fiat quick-entry (§6.3 convenience): type a USD amount and the coin amount fills in below.
    // The coin field (SendAmount) stays the SINGLE authoritative value the send path signs — this only
    // writes into it, so the fund path is unchanged and the user always sees the coin amount to confirm. ---
    [ObservableProperty] private string _sendFiatAmount = string.Empty;

    /// <summary>The USD price of one unit of the selected asset (stablecoins = $1), or 0 when unknown.</summary>
    private decimal PriceForSelected()
    {
        var sym = SelectedSendAsset?.Symbol;
        if (sym is null) return 0m;
        if (sym is "USDT" or "USDC") return 1m;
        return _priceUsd.TryGetValue(sym, out var p) && p.Usd > 0 ? p.Usd : 0m;
    }

    /// <summary>Offer the fiat quick-entry only when we have a price to convert with — otherwise the field
    /// would silently do nothing.</summary>
    public bool FiatInputAvailable => PriceForSelected() > 0m;

    /// <summary>"= 0.00063 BTC" under the fiat field: the coin amount the typed USD converts to.</summary>
    public string SendFiatCoinEquiv
    {
        get
        {
            var coin = FiatConvert.FiatToCoinAmount(SendFiatAmount, PriceForSelected());
            return coin.Length == 0 ? string.Empty : $"= {coin} {SelectedSendAsset?.Symbol}";
        }
    }

    partial void OnSendFiatAmountChanged(string value)
    {
        // Fiat only fills the coin field; an empty/invalid fiat value leaves the coin amount untouched, so
        // it can never silently wipe a coin amount the user typed directly.
        var coin = FiatConvert.FiatToCoinAmount(value, PriceForSelected());
        if (coin.Length > 0) SendAmount = coin;
        OnPropertyChanged(nameof(SendFiatCoinEquiv));
    }

    // --- Network-check-on-paste (§4): a non-blocking sanity check that the destination matches the
    // selected network, so a coin is not sent to an address for the wrong chain. ---
    [ObservableProperty] private string _sendAddressWarning = string.Empty;

    // --- Scam control: address-poisoning / own-address check on the destination. Advisory only. ---
    [ObservableProperty] private string _sendSafetyTitle = string.Empty;
    [ObservableProperty] private string _sendSafetyText = string.Empty;
    [ObservableProperty] private string _sendSafetyColor = "#E7CA83";
    [ObservableProperty] private bool _sendSafetyShown;
    // The positive counterpart: a green line when the destination is a trusted one (a saved contact or
    // one you have paid before), so a known address reads as safe and the warnings stand out by contrast.
    [ObservableProperty] private string _sendKnownContact = string.Empty;
    [ObservableProperty] private bool _sendKnownContactShown;

    partial void OnSendToChanged(string value)
    {
        HasSendQuote = false;
        ValidateSendAddress();
        EvaluateSendSafety();
    }

    /// <summary>
    /// Scam control: checks the destination against the addresses this wallet already trusts — its own
    /// receive addresses, the address book and everyone it has paid before — via the offline
    /// <see cref="Umbrella.Wallet.Core.Safety.AddressSafetyInspector"/>. The loud case is
    /// address-poisoning: a destination that looks almost exactly like a known address (same start and
    /// end, different middle) is very likely the attacker's lookalike seeded into your history. Also
    /// flags sending to one of your own addresses. Never blocks a send — it only warns.
    /// </summary>
    private void EvaluateSendSafety()
    {
        SendSafetyShown = false;
        SendSafetyTitle = SendSafetyText = string.Empty;
        SendKnownContactShown = false;
        SendKnownContact = string.Empty;

        var dest = SendTo?.Trim() ?? string.Empty;
        if (dest.Length == 0) return;

        var own = Accounts.Where(a => IsRealAddress(a.Address)).Select(a => a.Address).ToList();
        var known = _addressBookAll.Select(e => e.Address)
            .Concat(Transactions.Where(t => !string.IsNullOrWhiteSpace(t.Counterparty)).Select(t => t.Counterparty!))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = Umbrella.Wallet.Core.Safety.AddressSafetyInspector.Inspect(dest, own, known);
        switch (result.Level)
        {
            case Umbrella.Wallet.Core.Safety.AddressSafetyLevel.Lookalike:
                SendSafetyTitle = Loc.Instance["send.safetyPoisonTitle"];
                SendSafetyText = string.Format(Loc.Instance["send.safetyPoisonBody"], Shorten(result.SimilarTo ?? ""));
                SendSafetyColor = "#E09A9A"; // red — the serious one
                SendSafetyShown = true;
                break;
            case Umbrella.Wallet.Core.Safety.AddressSafetyLevel.OwnAddress:
                SendSafetyTitle = Loc.Instance["send.safetyOwnTitle"];
                SendSafetyText = Loc.Instance["send.safetyOwnBody"];
                SendSafetyColor = "#E7CA83"; // amber — a caution, not an alarm
                SendSafetyShown = true;
                break;
            case Umbrella.Wallet.Core.Safety.AddressSafetyLevel.NewRecipient:
                // A first-time note, but only worth showing when it means something: the address is
                // actually well-formed for the chosen chain AND you have some history to be "new"
                // against. On a fresh wallet with no contacts, everyone is new — that would be noise.
                var sym = SelectedSendAsset?.Symbol;
                if (known.Count > 0 &&
                    DestinationAddressCheck.Check(sym, dest) == AddressShape.Matches)
                {
                    SendSafetyTitle = Loc.Instance["send.firstTitle"];
                    SendSafetyText = Loc.Instance["send.firstBody"];
                    SendSafetyColor = "#8FB8CB"; // teal — informational, not a warning
                    SendSafetyShown = true;
                }
                break;
            case Umbrella.Wallet.Core.Safety.AddressSafetyLevel.Known:
                // A trusted destination: name the saved contact, or just note you've paid it before.
                var contact = _addressBookAll.FirstOrDefault(
                    e => string.Equals(e.Address?.Trim(), dest, StringComparison.OrdinalIgnoreCase));
                SendKnownContact = contact is not null && !string.IsNullOrWhiteSpace(contact.Label)
                    ? string.Format(Loc.Instance["send.savedContact"], contact.Label)
                    : Loc.Instance["send.sentBefore"];
                SendKnownContactShown = true;
                break;
        }
    }

    partial void OnSendAmountChanged(string value)
    {
        HasSendQuote = false;
        OnPropertyChanged(nameof(SendAmountFiat));
    }

    /// <summary>Warns when the destination does not look like an address on the selected network.
    /// The rules live in <see cref="DestinationAddressCheck"/> (Core, unit-tested); this only decides
    /// whether to show the warning. Advisory only — the authoritative validation still happens per
    /// chain when preparing the quote.</summary>
    private static readonly HashSet<string> EvmSendSymbols =
        new(StringComparer.OrdinalIgnoreCase) { "ETH", "BNB", "MATIC", "AVAX", "FTM", "CRO" };

    private void ValidateSendAddress()
    {
        SendAddressWarning = string.Empty;
        if (SelectedSendAsset is null) return;

        var sym = SelectedSendAsset.Symbol;
        if (DestinationAddressCheck.IsProbablyWrongNetwork(sym, SendTo))
        {
            SendAddressWarning = string.Format(Loc.Instance["send.addrMismatch"], sym);
            return;
        }

        // EIP-55 checksum: a mixed-case EVM address whose casing doesn't match its checksum has almost
        // certainly been mistyped or altered — warn before it can be signed (Core.Chains.EvmAddress).
        if (EvmSendSymbols.Contains(sym) &&
            EvmAddress.Check(SendTo) == EvmChecksumState.Invalid)
        {
            SendAddressWarning = Loc.Instance["send.badChecksum"];
        }
    }

    // --- Local address book (§4): reuse saved destinations instead of re-pasting. Public addresses
    // only, stored on this device. ---
    private readonly List<AddressBookEntry> _addressBookAll = new();
    /// <summary>Saved destinations for the asset currently selected in Send.</summary>
    public ObservableCollection<AddressBookEntry> SendAddressBook { get; } = [];
    public bool HasSendAddressBook => SendAddressBook.Count > 0;
    [ObservableProperty] private string _sendAddressLabel = string.Empty;

    private void LoadAddressBook()
    {
        _addressBookAll.Clear();
        _addressBookAll.AddRange(_addressBook.Load());
        RebuildSendAddressBook();
    }

    private void RebuildSendAddressBook()
    {
        SendAddressBook.Clear();
        var sym = SelectedSendAsset?.Symbol;
        if (!string.IsNullOrEmpty(sym))
            foreach (var e in _addressBookAll.Where(e =>
                         string.Equals(e.Chain, sym, StringComparison.OrdinalIgnoreCase)))
                SendAddressBook.Add(e);
        OnPropertyChanged(nameof(HasSendAddressBook));
    }

    /// <summary>Saves the current destination for reuse. Auto-labels with the shortened address when no
    /// label is given; a repeat address just updates its label rather than duplicating.</summary>
    [RelayCommand]
    private void SaveSendAddress()
    {
        var addr = SendTo?.Trim() ?? string.Empty;
        var sym = SelectedSendAsset?.Symbol;
        if (addr.Length == 0 || string.IsNullOrEmpty(sym)) { SendError = Loc.Instance["send.errDestFirst"]; return; }

        var label = string.IsNullOrWhiteSpace(SendAddressLabel) ? Shorten(addr) : SendAddressLabel.Trim();
        _addressBookAll.RemoveAll(e =>
            string.Equals(e.Address, addr, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Chain, sym, StringComparison.OrdinalIgnoreCase));
        _addressBookAll.Add(new AddressBookEntry(label, addr, sym));
        _addressBook.Save(_addressBookAll);
        SendAddressLabel = string.Empty;
        RebuildSendAddressBook();
        ShowToast(Loc.Instance["send.addrSavedOk"], isError: false);
    }

    [RelayCommand]
    private void UseSendAddress(AddressBookEntry? entry)
    {
        if (entry is null) return;
        SendTo = entry.Address; // triggers validation + clears any stale quote
    }

    [RelayCommand]
    private void RemoveSendAddress(AddressBookEntry? entry)
    {
        if (entry is null) return;
        _addressBookAll.RemoveAll(e =>
            string.Equals(e.Address, entry.Address, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Chain, entry.Chain, StringComparison.OrdinalIgnoreCase));
        _addressBook.Save(_addressBookAll);
        RebuildSendAddressBook();
    }

    /// <summary>How much to leave behind on "Max" so the network fee cannot overrun the balance. Tokens
    /// (USDT/USDC) reserve nothing — their fee is paid in the chain's native coin.</summary>
    private static decimal SendMaxReserve(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" or "LTC" => 0.0003m,
        "ETH" or "BNB" or "MATIC" or "AVAX" or "FTM" or "CRO" => 0.002m,
        "SOL" => 0.002m,
        "TON" => 0.05m,
        "TRX" => 2m,
        "XMR" => 0.001m,
        "ADA" => 1m,
        _ => 0m,
    };

    [RelayCommand]
    private void SetMaxAmount()
    {
        if (SelectedSendAsset is null) return;
        var bal = SelectedSendBalance;
        if (bal <= 0) { SendError = Loc.Instance["send.nothingToSend"]; return; }
        SendAmount = Fmt(Math.Max(0m, bal - SendMaxReserve(SelectedSendAsset.Symbol)));
        SendError = string.Empty;
    }

    /// <summary>Quick amount presets (25% / 50%): a share of the balance, always below the fee-aware Max,
    /// written into the coin field the user still confirms. Leaves the field untouched if there's nothing
    /// to send.</summary>
    [RelayCommand]
    private void SetAmountPercent(string? percent)
    {
        if (SelectedSendAsset is null) return;
        if (!int.TryParse(percent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct)) return;
        var bal = SelectedSendBalance;
        if (bal <= 0) { SendError = Loc.Instance["send.nothingToSend"]; return; }
        var amount = AmountPresets.Of(bal, pct);
        if (amount.Length == 0) return;
        SendAmount = amount;
        SendError = string.Empty;
    }

    partial void OnSelectedWatchNetworkChanged(SendOption? value)
    {
        if (value is not null) WatchChain = value.Symbol;
    }

    public bool HasFormError => !string.IsNullOrEmpty(FormError);
    public bool CanSubmitVaultForm => Password.Length >= MinPasswordLength;

    /// <summary>Live counter — also proves the Password binding is actually updating.</summary>
    public string PasswordMeterLabel => Password.Length switch
    {
        0 => $"0 / {MinPasswordLength} characters",
        var n when n < MinPasswordLength => $"{n} / {MinPasswordLength} characters · too short",
        var n when n < 16 => $"{n} characters · ok",
        var n => $"{n} characters · strong",
    };

    public string PasswordMeterColor => Password.Length switch
    {
        0 => "#8B909A",
        var n when n < MinPasswordLength => "#E09A9A",
        var n when n < 16 => "#E7CA83",
        _ => "#8FCB9B",
    };
    partial void OnIsBalanceHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(BalanceDisplayMain));
        OnPropertyChanged(nameof(BalanceDisplayCents));
        OnPropertyChanged(nameof(HideBalanceLabel));
        OnPropertyChanged(nameof(AreValuesVisible));
    }
    partial void OnTotalBalanceMainChanged(string value) => OnPropertyChanged(nameof(BalanceDisplayMain));
    partial void OnTotalBalanceCentsChanged(string value) => OnPropertyChanged(nameof(BalanceDisplayCents));
    partial void OnSearchQueryChanged(string value) => RefreshHoldings();

    private void NotifySectionFlags()
    {
        OnPropertyChanged(nameof(IsPortfolio));
        OnPropertyChanged(nameof(IsReceive));
        OnPropertyChanged(nameof(IsSend));
        OnPropertyChanged(nameof(IsSwap));
        OnPropertyChanged(nameof(IsActivity));
        OnPropertyChanged(nameof(IsTransactions));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsSecurity));
        OnPropertyChanged(nameof(IsAsset));
        OnPropertyChanged(nameof(IsConnect));
        OnPropertyChanged(nameof(IsMarket));
        OnPropertyChanged(nameof(IsNews));
        OnPropertyChanged(nameof(IsNfts));
        OnPropertyChanged(nameof(IsStaking));
        OnPropertyChanged(nameof(IsP2p));
        OnPropertyChanged(nameof(IsBuy));
        OnPropertyChanged(nameof(IsDiscover));
        OnPropertyChanged(nameof(IsDiscoverGroup));
        OnPropertyChanged(nameof(IsWalletGroup));
        OnPropertyChanged(nameof(IsActivityGroup));
        OnPropertyChanged(nameof(IsWelcomeStage));
        OnPropertyChanged(nameof(IsCreateStage));
        OnPropertyChanged(nameof(IsImportStage));
        OnPropertyChanged(nameof(IsUnlockStage));
        OnPropertyChanged(nameof(ShowDefaultLockBg));
        OnPropertyChanged(nameof(ShowCustomLockBg));
        OnPropertyChanged(nameof(IsBackupStage));
        OnPropertyChanged(nameof(IsWorkspace));
        OnPropertyChanged(nameof(ShowSidebar));
        OnPropertyChanged(nameof(ShowSideRail));
    }

    // --- Onboarding navigation (full-screen pages) ----------------------------
    [RelayCommand]
    private void GoToCreate()
    {
        ClearPasswordFields();
        SetupStage = "Create";
    }

    [RelayCommand]
    private void GoToImport()
    {
        ClearPasswordFields();
        ImportPhrase = string.Empty;
        SetupStage = "Import";
    }

    [RelayCommand]
    private void GoToWelcome()
    {
        ClearPasswordFields();
        SetupStage = "Welcome";
    }

    [RelayCommand]
    private async Task CreateWalletAsync()
    {
        var pw = ReuseAppPassword ? _sessionPassword! : Password;
        if (!ValidateVaultPassword(pw)) return;
        await RunBusyAsync(async () =>
        {
            var mnemonic = _mnemonics.Generate();
            await _vault.CreateAsync(mnemonic, pw);
            SetSessionPassword(pw);
            HasVault = true;
            FinalizeWalletRegistration();
            SetUnlocked(mnemonic);
            // Gate the workspace behind an explicit "I wrote it down" step so the seed is
            // actually backed up before the user starts using the wallet.
            RecoveryPhrase = mnemonic;
            PendingPhraseBackup = true;
            ClearPasswordFields();
            ActiveSection = "Portfolio";
            StatusMessage = "Write down all 24 words offline, then continue";
            await RefreshLiveDataAsync();
        });
    }

    /// <summary>Leaves the post-create backup page and enters the workspace.</summary>
    [RelayCommand]
    private void ConfirmPhraseBackup()
    {
        RecoveryPhrase = string.Empty;
        PendingPhraseBackup = false;
        StatusMessage = "Wallet ready · keep your offline backup safe";
    }

    [RelayCommand]
    private async Task ImportWalletAsync()
    {
        // Accept either a BIP39 seed (multi-chain) or a TON-native mnemonic (Telegram Wallet /
        // Tonkeeper → a TON-only wallet).
        string normalized;
        var bip39 = _mnemonics.Validate(ImportPhrase);
        if (bip39.IsValid && bip39.NormalizedMnemonic is not null)
        {
            normalized = bip39.NormalizedMnemonic;
        }
        else if (TonMnemonic.IsTonMnemonic(ImportPhrase))
        {
            normalized = TonMnemonic.Normalize(ImportPhrase);
        }
        else
        {
            Fail(bip39.Error ?? "Recovery phrase is invalid");
            return;
        }

        var pw = ReuseAppPassword ? _sessionPassword! : Password;
        if (!ValidateVaultPassword(pw)) return;

        await RunBusyAsync(async () =>
        {
            await _vault.CreateAsync(normalized, pw);
            SetSessionPassword(pw);
            HasVault = true;
            FinalizeWalletRegistration();
            SetUnlocked(normalized);
            // Imported wallets already have a backup — go straight to the workspace.
            RecoveryPhrase = string.Empty;
            PendingPhraseBackup = false;
            ImportPhrase = string.Empty;
            ClearPasswordFields();
            ActiveSection = "Portfolio";
            StatusMessage = _isTonWallet
                ? "TON wallet imported · fetching your Toncoin balance"
                : "Wallet imported · fetching live balances";
            await RefreshLiveDataAsync();
        });
    }

    [RelayCommand]
    private void ToggleUnlockAdvanced() => ShowUnlockAdvanced = !ShowUnlockAdvanced;

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (Password.Length < MinPasswordLength)
        {
            Fail($"Enter your vault password ({MinPasswordLength}+ characters).");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var mnemonic = await _vault.UnlockAsync(Password);
            _sessionPassword = Password;
            var passphrase = UnlockPassphrase ?? string.Empty; // capture before the fields are cleared
            ClearPasswordFields();
            SetUnlocked(mnemonic, passphrase);
            ActiveSection = "Portfolio";
            StatusMessage = passphrase.Length > 0
                ? "Hidden wallet unlocked · loading chain balances"
                : "Vault unlocked · loading chain balances";
            await RefreshLiveDataAsync();
        });
    }

    /// <summary>
    /// Re-derives the phrase from the vault by re-entering the password, rather than keeping
    /// the unlocked mnemonic reachable from a button. Wrong password fails closed.
    /// </summary>
    [RelayCommand]
    private async Task RevealPhraseAsync()
    {
        if (!HasVault)
        {
            Fail("No vault on this PC yet.");
            return;
        }

        if (SettingsPassword.Length < MinPasswordLength)
        {
            Fail($"Enter your vault password ({MinPasswordLength}+ characters) to reveal the phrase.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            // Decrypt on demand into a Settings-only field. This can never render without a
            // correct password because it is set only here, after UnlockAsync succeeds.
            var mnemonic = await _vault.UnlockAsync(SettingsPassword);
            SettingsPassword = string.Empty;
            SettingsRevealedPhrase = mnemonic;
            IsSettingsPhraseVisible = true;
            StatusMessage = "Phrase revealed · hide it as soon as you have written it down";
        });
    }

    /// <summary>
    /// Reveals the Monero account's secret keys after a password check. These three values are
    /// what Feather / monero-wallet-cli need for "Restore from keys", which is how the user
    /// actually spends XMR — Umbrella receives it but cannot build Monero transactions.
    /// </summary>
    [RelayCommand]
    private async Task RevealMoneroKeysAsync()
    {
        if (!HasVault)
        {
            Fail("No vault on this PC yet.");
            return;
        }

        if (SettingsPassword.Length < MinPasswordLength)
        {
            Fail($"Enter your vault password ({MinPasswordLength}+ characters) to export Monero keys.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var mnemonic = await _vault.UnlockAsync(SettingsPassword);
            SettingsPassword = string.Empty;
            var monero = _deriver.DeriveMoneroWallet(mnemonic);
            MoneroAddress = monero.Address;
            MoneroSpendKey = monero.SecretSpendKeyHex;
            MoneroViewKey = monero.SecretViewKeyHex;
            IsMoneroKeysVisible = true;
            StatusMessage = "Monero keys revealed · treat the spend key like your seed phrase";
        });
    }

    /// <summary>
    /// Starts the bundled monero-wallet-rpc and restores the Monero account from the keys we
    /// derive, which is what turns XMR from receive-only into a full coin (balance + send).
    /// Requires the vault password because the secret spend key has to be handed to the daemon.
    /// </summary>
    [RelayCommand]
    private async Task EnableMoneroAsync()
    {
        if (!MoneroEnabled)
        {
            _monero.Stop();
            MoneroStatus = "Monero wallet service is off";
            MoneroStatusColor = "#8A9099";
            return;
        }

        if (!MoneroRpcService.IsBundlePresent)
        {
            MoneroStatus = "Bundled monero-wallet-rpc is missing from this build.";
            MoneroStatusColor = "#E09A9A";
            MoneroEnabled = false;
            return;
        }

        if (_unlockedMnemonic is null)
        {
            MoneroStatus = "Unlock the vault first.";
            MoneroStatusColor = "#E09A9A";
            MoneroEnabled = false;
            return;
        }

        MoneroStatusColor = "#8B909A";
        var progress = new Progress<string>(m => MoneroStatus = m);
        var wallet = _deriver.DeriveMoneroWallet(_unlockedMnemonic);

        // The daemon needs a password for its own wallet file; derive one from the seed so the
        // user never has to remember a second secret and it never touches disk in plaintext.
        var filePassword = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("umbrella-monero-file:" + wallet.SecretViewKeyHex)))[..32];

        var (ok, message) = await _monero.StartAsync(
            wallet.Address, wallet.SecretSpendKeyHex, wallet.SecretViewKeyHex, filePassword, progress);

        if (!ok)
        {
            MoneroStatus = message;
            MoneroStatusColor = "#E09A9A";
            MoneroEnabled = false;
            return;
        }

        MoneroStatus = message;
        MoneroStatusColor = "#8FCB9B";
        await RefreshMoneroAsync();
    }

    /// <summary>Pulls the Monero balance and reports scan progress rather than a misleading 0.</summary>
    private async Task RefreshMoneroAsync()
    {
        if (!_monero.IsRunning) return;

        var balance = await _monero.GetBalanceAsync();
        if (balance is null) return;

        MoneroStatus = balance.Synced
            ? $"Synced · {balance.Unlocked:0.############} XMR spendable"
            : $"Scanning… {balance.PercentSynced}% ({balance.ScannedHeight:N0}/{balance.ChainHeight:N0})";
        MoneroStatusColor = balance.Synced ? "#8FCB9B" : "#E7CA83";

        var (usd, change) = (0m, 0m);
        var prices = await _rates.GetUsdPricesAsync(new[] { "XMR" }, CancellationToken.None);
        if (prices.TryGetValue("XMR", out var xmrPrice)) (usd, change) = xmrPrice;

        var existing = Accounts.FirstOrDefault(a => a.Symbol == "XMR");
        if (existing is not null)
        {
            Accounts[Accounts.IndexOf(existing)] = existing with
            {
                // Only a synced wallet may claim a balance.
                SupportStatus = balance.Synced ? "Ready" : "Receive only",
                Amount = (double)balance.Total,
                Price = (double)usd,
                Change24h = (double)change,
            };
            RefreshHoldings();
            RecalcBalance();
        }
    }

    /// <summary>Stops the Monero daemon — called when the window closes.</summary>
    public void ShutdownMonero() => _monero.Stop();

    [RelayCommand]
    private void HideMoneroKeys()
    {
        MoneroAddress = string.Empty;
        MoneroSpendKey = string.Empty;
        MoneroViewKey = string.Empty;
        IsMoneroKeysVisible = false;
        StatusMessage = "Monero keys hidden";
    }

    [RelayCommand]
    private void HideSettingsPhrase()
    {
        SettingsRevealedPhrase = string.Empty;
        IsSettingsPhraseVisible = false;
        StatusMessage = "Recovery phrase hidden";
    }

    /// <summary>
    /// Starts/stops the Tor client that ships with the app and routes ALL public traffic
    /// (balances, prices, broadcasts) through it. Nothing external needs to be installed.
    /// </summary>
    [RelayCommand]
    private async Task ApplyTorAsync()
    {
        if (!TorEnabled)
        {
            _tor.Stop();
            // Fall back to the custom proxy if the user has one, otherwise go direct.
            var fallback = EffectiveCustomProxy();
            PublicHttp.SetProxy(fallback);
            TorStatus = fallback is null
                ? "Direct connection · traffic is NOT anonymised"
                : $"Off · using your custom proxy ({fallback})";
            TorStatusColor = "#E7CA83";
            if (IsUnlocked) PushActivity("Security", "Tor", "off", "direct connection", "now");
            OnPropertyChanged(nameof(TorOnlyStatus)); // Tor-only + Tor off = clearnet now blocked
            _ = RefreshMarketAsync();
            return;
        }

        // Tor and a custom proxy are mutually exclusive — turning Tor on takes over the route.
        if (CustomProxyEnabled)
        {
            CustomProxyEnabled = false;
            ProxyStatus = "Off · Tor is handling the route";
        }

        if (!EmbeddedTorService.IsBundlePresent)
        {
            TorStatus = "Bundled Tor is missing from this build.";
            TorStatusColor = "#E09A9A";
            TorEnabled = false;
            return;
        }

        TorStatusColor = "#8B909A";
        TorStatus = "Starting bundled Tor…";
        var progress = new Progress<string>(message => TorStatus = message);
        var (ok, resultMessage) = await _tor.StartAsync(progress);
        if (!ok)
        {
            TorStatus = resultMessage;
            TorStatusColor = "#E09A9A";
            TorEnabled = false;
            PublicHttp.SetProxy(null);
            return;
        }

        PublicHttp.SetProxy(_tor.ProxyUri);
        TorStatus = $"{resultMessage} · your IP is hidden from explorers";
        TorStatusColor = "#8FCB9B";
        if (IsUnlocked) PushActivity("Security", "Tor", "on", "IP hidden from explorers", "now");
        OnPropertyChanged(nameof(TorOnlyStatus));
        _ = RefreshMarketAsync();
        if (IsUnlocked) _ = RefreshLiveDataAsync();
    }

    /// <summary>Stops the bundled Tor process — called when the window closes.</summary>
    public void ShutdownTor() => _tor.Stop();

    [RelayCommand]
    private void ToggleDocs() => IsDocsVisible = !IsDocsVisible;

    [RelayCommand]
    private void OpenVaultFolder()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_vault.VaultPath);
            if (string.IsNullOrEmpty(dir)) return;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
            StatusMessage = $"Opened {dir}";
        }
        catch (Exception ex)
        {
            Fail($"Could not open the vault folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Destroys the local vault. Requires typing DELETE, because without the seed backup this
    /// is unrecoverable — there is no server-side copy by design.
    /// </summary>
    /// <summary>The word the user types to confirm deletion, in their language (also accepts the
    /// English DELETE), so a Ukrainian/Russian user isn't blocked by an English-only keyword.</summary>
    public string DeleteKeyword => Loc.Instance.CurrentCode switch
    {
        "uk" => "ВИДАЛИТИ",
        "ru" => "УДАЛИТЬ",
        _ => "DELETE",
    };

    /// <summary>Danger zone: wipe the local activity / transaction history (keeps the wallet + funds).</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        Activity.Clear();
        RecentActivity.Clear();
        _onChainRows.Clear();       // fetched chain history too — it re-pulls on the next Refresh
        _activityStore.Clear();
        LastHistorySync = "—";
        RebuildActivityAssets();
        RebuildFilteredActivity();
        RebuildTransactions();
        OnPropertyChanged(nameof(HasActivity));
        StatusMessage = "Activity & transaction history cleared from this device";
    }

    /// <summary>Danger zone: remove every linked watch-only address and connected exchange (keeps the vault).</summary>
    [RelayCommand]
    private async Task DisconnectAllAsync()
    {
        var count = WatchAddresses.Count + Exchanges.Count;
        WatchAddresses.Clear();
        await _watchStore.SaveAsync(WatchAddresses);
        Exchanges.Clear();
        if (_unlockedMnemonic is not null) await _exchangeStore.SaveAsync(Exchanges, _unlockedMnemonic);
        StatusMessage = $"Disconnected {count} linked address(es) / exchange(s)";
        if (IsUnlocked) await RefreshLiveDataAsync();
    }

    [RelayCommand]
    private void DeleteVault()
    {
        var typed = DeleteConfirmation.Trim();
        if (!typed.Equals(DeleteKeyword, StringComparison.OrdinalIgnoreCase) &&
            !typed.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            Fail($"Type {DeleteKeyword} to confirm — the seed is not recoverable without your backup.");
            return;
        }

        try
        {
            LockVault();
            ShutdownMonero(); // release the Monero wallet-dir so it can be removed
            var result = DataWiper.WipeAll();

            // Every vault file (Main + additional) was just erased — rebuild the registry from the now
            // empty state and reset the add-wallet flags so onboarding starts clean.
            IsAddingWallet = false;
            _pendingNewWalletId = null;
            _registry.ReloadFromDisk();
            _vault = BuildActiveVault();
            RefreshWalletList();

            HasVault = false;
            SetupStage = "Welcome";              // a fresh start, not an empty portfolio
            ActiveSection = "Portfolio";
            DeleteConfirmation = string.Empty;
            // The profile images were just deleted — drop them from memory so nothing points at them.
            AvatarImage = BannerImage = SidebarBgImage = null;

            StatusMessage = result.Failed.Count == 0
                ? $"Wallet and all data erased — {result.Removed} item(s) removed. Restore from your 24-word phrase."
                : $"Erased {result.Removed} item(s); {result.Failed.Count} were in use. Close the wallet and delete the data folder to finish.";
        }
        catch (Exception ex)
        {
            Fail($"Could not delete the wallet: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Lock() => LockVault();

    public void LockVault()
    {
        _refreshCts?.Cancel();
        if (_unlockedMnemonic is not null)
        {
            _unlockedMnemonic = string.Empty;
            _unlockedMnemonic = null;
        }
        _unlockedPassphrase = "";        // forget the hidden-wallet passphrase
        _deriver.ActivePassphrase = "";  // and reset derivation back to the base wallet
        SetSessionPassword(null);
        IsResettingPassword = false;

        IsUnlocked = false;
        PendingPhraseBackup = false;
        IsQrPopupOpen = false;
        // Exchange API secrets must not survive a lock in memory.
        Exchanges.Clear();
        ExchangeApiKey = string.Empty;
        ExchangeApiSecret = string.Empty;
        ExchangePassphrase = string.Empty;
        ClearSendQuotes();
        HideMoneroKeys();
        SendSuccess = string.Empty;
        RecoveryPhrase = string.Empty;
        IsRecoveryPhraseVisible = false;
        SettingsRevealedPhrase = string.Empty;
        IsSettingsPhraseVisible = false;
        SettingsPassword = string.Empty;
        ReceiveQr = null;
        SelectedReceiveAddress = string.Empty;
        StatusMessage = "Vault locked";
        ResetAddresses();
        RecalcBalance();
    }

    // --- Multi-wallet: switcher, add, rename, remove -------------------------

    /// <summary>The vault of the currently-active wallet (or the first-run legacy location).</summary>
    private EncryptedFileSeedVault BuildActiveVault()
    {
        var active = _registry.Active;
        var path = active is not null ? _registry.VaultPathFor(active) : _registry.FirstWalletVaultPath;
        return new EncryptedFileSeedVault(path);
    }

    /// <summary>Startup self-heal: if the active wallet's vault is missing (e.g. an add-wallet was
    /// interrupted before its seed was written — the state that stranded the onboarding), switch to a
    /// wallet that actually has a vault, and drop any leftover managed wallets with no vault so the
    /// switcher stays clean. Only ever changes which wallet is selected; never touches a seed.</summary>
    private void SelfHealWallets()
    {
        try
        {
            if (!_vault.Exists)
            {
                var existing = _registry.Wallets
                    .FirstOrDefault(w => System.IO.File.Exists(_registry.VaultPathFor(w)));
                if (existing is not null)
                {
                    _registry.SetActive(existing.Id);
                    _vault = BuildActiveVault();
                }
            }

            foreach (var w in _registry.Wallets
                         .Where(w => !w.IsLegacy
                                     && w.Id != _registry.Active?.Id
                                     && !System.IO.File.Exists(_registry.VaultPathFor(w)))
                         .ToList())
            {
                try { _registry.Remove(w.Id); } catch { /* leftover entry is harmless */ }
            }
        }
        catch { /* self-heal is best-effort and must never block startup */ }
    }

    private void RefreshWalletList()
    {
        Wallets.Clear();
        var activeId = _registry.Active?.Id;
        foreach (var w in _registry.Wallets)
        {
            Wallets.Add(new WalletListItemViewModel(w.Id, w.Label, w.Id == activeId, w.IsLegacy, w.Color));
        }
        OnPropertyChanged(nameof(ActiveWalletLabel));
        OnPropertyChanged(nameof(HasMultipleWallets));
        RebuildWalletCoinToggles();
    }

    /// <summary>Coins the active wallet is set to accept — a checkbox row in Settings → Wallets.
    /// Empty selection means "all coins".</summary>
    public ObservableCollection<CoinToggle> WalletCoinToggles { get; } = [];

    /// <summary>Whether a coin is shown for the active wallet (all coins when no restriction is set).</summary>
    private bool IsWalletCoinEnabled(string symbol)
    {
        var coins = _registry.Active?.Coins;
        return coins is null || coins.Count == 0
            || coins.Contains(symbol, StringComparer.OrdinalIgnoreCase);
    }

    private void RebuildWalletCoinToggles()
    {
        WalletCoinToggles.Clear();
        var coins = _registry.Active?.Coins;
        var restricted = coins is { Count: > 0 };
        foreach (var chain in ChainCatalog.All)
        {
            var on = !restricted || coins!.Contains(chain.Symbol, StringComparer.OrdinalIgnoreCase);
            WalletCoinToggles.Add(new CoinToggle(chain.Symbol, chain.Name, on));
        }
        OnPropertyChanged(nameof(WalletCoinsAllLabel));
    }

    /// <summary>Summary line: "All coins" or "N coins".</summary>
    public string WalletCoinsAllLabel
    {
        get
        {
            var coins = _registry.Active?.Coins;
            return coins is { Count: > 0 }
                ? $"{coins.Count}"
                : Loc.Instance["settings.walletCoinsAll"];
        }
    }

    /// <summary>Toggle a coin for the active wallet, persist, and re-derive so the change is immediate.</summary>
    [RelayCommand]
    private void ToggleWalletCoin(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        var active = _registry.Active;
        if (active is null || string.IsNullOrEmpty(_unlockedMnemonic)) return;

        // Start from the current effective set (all coins if unrestricted), then flip this one.
        var set = new HashSet<string>(
            (active.Coins is { Count: > 0 } c ? c : ChainCatalog.All.Select(x => x.Symbol)),
            StringComparer.OrdinalIgnoreCase);
        if (!set.Remove(symbol)) set.Add(symbol);

        // Never allow an empty wallet: an empty selection means "all coins".
        var full = ChainCatalog.All.Select(x => x.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string>? value = set.Count == 0 || set.SetEquals(full) ? null : set.ToList();

        _registry.SetCoins(active.Id, value);
        DeriveAccounts(_unlockedMnemonic!);   // re-derive so hidden coins disappear immediately
        RebuildWalletCoinToggles();
        if (IsUnlocked) PushActivity("Settings", "Wallet coins", active.Label, symbol, "now");
    }

    /// <summary>Colour-tag the active wallet (pass "clear" to remove the tag). Persisted.</summary>
    [RelayCommand]
    private void SetWalletColor(string? color)
    {
        var active = _registry.Active;
        if (active is null) return;
        var value = string.Equals(color, "clear", StringComparison.OrdinalIgnoreCase) ? null : color;
        _registry.SetColor(active.Id, value);
        RefreshWalletList();
        if (IsUnlocked) PushActivity("Settings", "Wallet colour", active.Label, value ?? "cleared", "now");
    }

    /// <summary>Switch to another wallet. With one common password, the target is unlocked seamlessly;
    /// if it happens to use a different password, we fall back to the unlock screen.</summary>
    [RelayCommand]
    private async Task SwitchWalletAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == _registry.Active?.Id) return;
        var pw = _sessionPassword;               // capture before LockVault wipes it
        _registry.SetActive(id);
        LockVault();
        _vault = BuildActiveVault();
        HasVault = _vault.Exists;
        RefreshWalletList();

        // Seamless switch when the common password matches (the normal case).
        if (HasVault && !string.IsNullOrEmpty(pw))
        {
            try
            {
                var mnemonic = await _vault.UnlockAsync(pw);
                SetSessionPassword(pw);
                SetUnlocked(mnemonic);
                ActiveSection = "Portfolio";
                StatusMessage = $"Switched to “{ActiveWalletLabel}”";
                await RefreshLiveDataAsync();
                return;
            }
            catch
            {
                // This wallet uses a different password — ask for it below.
            }
        }

        SetupStage = HasVault ? SetupStage : "Welcome";
        StatusMessage = HasVault
            ? $"Switched to “{ActiveWalletLabel}” · enter its password"
            : $"“{ActiveWalletLabel}” · create or import to set it up";
    }

    /// <summary>Begin adding a new, independent wallet: registers it, makes it active, locks the current
    /// wallet and drops into the create/import onboarding for the empty vault.</summary>
    [RelayCommand]
    private void BeginAddWallet()
    {
        var label = string.IsNullOrWhiteSpace(NewWalletLabel) ? $"Wallet {_registry.Wallets.Count + 1}" : NewWalletLabel.Trim();
        var pw = _sessionPassword;             // capture the app password before LockVault wipes it
        _previousActiveWalletId = _registry.Active?.Id;
        var entry = _registry.Add(label);
        _pendingNewWalletId = entry.Id;
        _registry.SetActive(entry.Id);
        LockVault();                       // clears the seed + session password of the current wallet…
        SetSessionPassword(pw);            // …but keep the app password so the new wallet reuses it
        IsAddingWallet = true;
        _vault = BuildActiveVault();       // points at the new (not-yet-created) vault → HasVault=false
        HasVault = false;
        NewWalletLabel = string.Empty;
        SetupStage = "Welcome";
        RefreshWalletList();
        StatusMessage = $"New wallet “{label}” · create or import its seed";
    }

    /// <summary>Abort an in-progress add-wallet: de-registers the pending wallet and returns to the
    /// previous one's unlock screen.</summary>
    [RelayCommand]
    private async Task CancelAddWalletAsync()
    {
        if (!IsAddingWallet) return;
        var pw = _sessionPassword;
        if (_previousActiveWalletId is not null) _registry.SetActive(_previousActiveWalletId);
        if (_pendingNewWalletId is not null)
        {
            try { _registry.Remove(_pendingNewWalletId); } catch { /* it may never have been created */ }
        }
        IsAddingWallet = false;
        _pendingNewWalletId = null;
        _vault = BuildActiveVault();
        HasVault = _vault.Exists;
        RefreshWalletList();

        // Slip straight back into the previous wallet if the app password still opens it.
        if (HasVault && !string.IsNullOrEmpty(pw))
        {
            try
            {
                var mnemonic = await _vault.UnlockAsync(pw);
                SetSessionPassword(pw);
                SetUnlocked(mnemonic);
                ActiveSection = "Portfolio";
                StatusMessage = $"Back to “{ActiveWalletLabel}”";
                await RefreshLiveDataAsync();
                return;
            }
            catch { /* fall back to the unlock screen */ }
        }

        StatusMessage = $"Back to “{ActiveWalletLabel}”";
    }

    [RelayCommand]
    private void RenameActiveWallet()
    {
        var active = _registry.Active;
        if (active is null || string.IsNullOrWhiteSpace(RenameWalletLabel)) return;
        _registry.Rename(active.Id, RenameWalletLabel.Trim());
        RenameWalletLabel = string.Empty;
        RefreshWalletList();
        StatusMessage = $"Renamed to “{ActiveWalletLabel}”";
    }

    /// <summary>Remove another (non-active) wallet, deleting only its own encrypted vault. The active
    /// wallet and the Main wallet's seed file are protected by the registry.</summary>
    [RelayCommand]
    private void RemoveWallet(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        try
        {
            _registry.Remove(id);
            RefreshWalletList();
            StatusMessage = "Wallet removed from this device.";
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    // --- Change password ----------------------------------------------------
    [ObservableProperty] private string _changePwCurrent = string.Empty;
    [ObservableProperty] private string _changePwNew = string.Empty;
    [ObservableProperty] private string _changePwConfirm = string.Empty;

    /// <summary>Changes the app password by re-encrypting, in place, every wallet that opens with the
    /// current password — so the one common password stays unified. Requires the current password;
    /// wallets on a different password are left untouched.</summary>
    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (ChangePwNew.Length < MinPasswordLength)
        {
            Fail($"New password needs at least {MinPasswordLength} characters."); return;
        }
        if (!string.Equals(ChangePwNew, ChangePwConfirm, StringComparison.Ordinal))
        {
            Fail("The two new passwords do not match."); return;
        }

        await RunBusyAsync(async () =>
        {
            var changed = 0;
            foreach (var w in _registry.Wallets.ToList())
            {
                var path = _registry.VaultPathFor(w);
                if (!System.IO.File.Exists(path)) continue;
                var v = new EncryptedFileSeedVault(path);
                try
                {
                    var seed = await v.UnlockAsync(ChangePwCurrent);
                    await v.CreateAsync(seed, ChangePwNew);   // re-encrypts the same seed with the new password
                    changed++;
                }
                catch { /* this wallet uses a different password — leave it as is */ }
            }

            if (changed == 0)
            {
                Fail("Current password is incorrect."); return;
            }

            SetSessionPassword(ChangePwNew);
            _vault = BuildActiveVault();
            ChangePwCurrent = ChangePwNew = ChangePwConfirm = string.Empty;
            StatusMessage = changed > 1 ? $"Password changed for {changed} wallets." : "Password changed.";
        });
    }

    // --- Forgot password: restore this wallet from its recovery phrase -------
    [ObservableProperty] private bool _isResettingPassword;

    /// <summary>From the unlock screen: "forgot password" — reveal the phrase + new-password form.</summary>
    [RelayCommand]
    private void BeginPasswordReset()
    {
        ImportPhrase = string.Empty;
        ClearPasswordFields();
        FormError = string.Empty;
        IsResettingPassword = true;
    }

    [RelayCommand]
    private void CancelPasswordReset()
    {
        IsResettingPassword = false;
        ImportPhrase = string.Empty;
        ClearPasswordFields();
    }

    /// <summary>Recovers access without the old password: re-creates the active wallet's vault from the
    /// entered recovery phrase and a new password. The seed is the wallet, so the same phrase restores
    /// the same addresses and funds; a correct phrase is the user's responsibility.</summary>
    [RelayCommand]
    private async Task ResetWithSeedAsync()
    {
        string normalized;
        var bip39 = _mnemonics.Validate(ImportPhrase);
        if (bip39.IsValid && bip39.NormalizedMnemonic is not null) normalized = bip39.NormalizedMnemonic;
        else if (TonMnemonic.IsTonMnemonic(ImportPhrase)) normalized = TonMnemonic.Normalize(ImportPhrase);
        else { Fail(bip39.Error ?? "Recovery phrase is invalid"); return; }

        if (!ValidatePasswords()) return;

        await RunBusyAsync(async () =>
        {
            await _vault.CreateAsync(normalized, Password); // overwrite the active vault
            SetSessionPassword(Password);
            IsResettingPassword = false;
            SetUnlocked(normalized);
            ImportPhrase = string.Empty;
            ClearPasswordFields();
            ActiveSection = "Portfolio";
            StatusMessage = "Wallet restored from your recovery phrase with a new password.";
            await RefreshLiveDataAsync();
        });
    }

    /// <summary>Called after a create/import succeeds, to keep the registry in step.</summary>
    private void FinalizeWalletRegistration()
    {
        if (IsAddingWallet)
        {
            IsAddingWallet = false;
            _pendingNewWalletId = null;
        }
        else
        {
            // First-run: register the legacy vault we just wrote as the Main wallet.
            _registry.EnsureLegacyRegistered();
        }
        RefreshWalletList();
    }

    [RelayCommand]
    private void HideRecoveryPhrase()
    {
        RecoveryPhrase = string.Empty;
        IsRecoveryPhraseVisible = false;
        StatusMessage = "Recovery phrase hidden · keep your offline backup safe";
    }

    [RelayCommand]
    private void SelectSection(string section)
    {
        // Navigating anywhere other than Settings must drop any revealed phrase from the screen.
        if (section != "Settings")
        {
            SettingsRevealedPhrase = string.Empty;
            IsSettingsPhraseVisible = false;
            SettingsPassword = string.Empty;
            HideMoneroKeys();
        }

        // Never leave the QR popup floating over a different section.
        IsQrPopupOpen = false;
        // Choosing a section from the mobile "More" sheet closes it.
        IsMoreSheetOpen = false;

        ActiveSection = section;
        StatusMessage = section switch
        {
            "Send" => "Send · ETH transfers sign locally and broadcast via public RPC",
            "Receive" => "Receive · share a derived address or QR",
            "Connect" => "Connect · add watch-only addresses from MetaMask / explorers",
            "Market" => $"Market · live prices · {ChartRange} charts, click a coin for detail",
            "Swap" => "Swap · non-custodial cross-chain swaps route through THORChain vaults",
            "P2p" => "P2P & DEX · non-custodial venues to trade — your keys never leave this device",
            "Buy" => "Buy · card/bank on-ramps deliver straight to your own address — nothing is held here",
            _ => StatusMessage,
        };

        // Refresh real on-chain history when the user opens Transactions.
        if (section == "Transactions" && IsUnlocked && !_isTonWallet) _ = LoadOnChainHistoryAsync();
    }

    /// <summary>Opens an external URL in the user's default browser. Used by the P2P/DEX directory and
    /// guide links — public destinations only, no wallet data ever travels in the URL.</summary>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            StatusMessage = $"Opened {url} in your browser";
            ShowToast(Loc.Instance["toast.opened"], isError: false);
        }
        catch (Exception ex)
        {
            Fail($"Could not open the link: {ex.Message}");
        }
    }

    /// <summary>Curated non-custodial venues for the P2P & DEX page. Every entry keeps custody with the
    /// user (on-chain DEX or P2P escrow) — no custodial order-book exchanges are listed, in keeping with
    /// the wallet's self-custody stance. These are external sites the user opens in their own browser.</summary>
    public ObservableCollection<P2pVenue> P2pVenues { get; } =
    [
        new("THORSwap", "DEX aggregator", "Non-custodial",
            "Cross-chain swaps (BTC ↔ ETH ↔ more) routed over THORChain — the same engine behind this wallet's own Swap tab.",
            "https://www.thorswap.finance", "TS", "#0A9E8E"),
        new("Uniswap", "DEX", "Non-custodial",
            "The largest Ethereum & L2 DEX. Connect a wallet and swap any ERC-20 on-chain; liquidity from pooled AMMs.",
            "https://app.uniswap.org", "UNI", "#FF007A"),
        new("Jupiter", "DEX aggregator", "Non-custodial",
            "Best-route Solana swaps across every Solana AMM in one transaction. Fast and cheap.",
            "https://jup.ag", "JUP", "#22C55E"),
        new("1inch", "DEX aggregator", "Non-custodial",
            "Splits an order across many DEXes for the best on-chain price across Ethereum and other EVM chains.",
            "https://app.1inch.io", "1IN", "#1B67F0"),
        new("PancakeSwap", "DEX", "Non-custodial",
            "The main BNB Chain DEX (also on Ethereum and more) — swap, provide liquidity, all on-chain.",
            "https://pancakeswap.finance", "CAKE", "#D1884F"),
        new("CoW Swap", "DEX · MEV-protected", "Non-custodial",
            "Settles Ethereum & L2 swaps through batch auctions that shield you from front-running/MEV, and only fills at your limit price. Privacy- and price-friendly.",
            "https://swap.cow.fi", "COW", "#0F4DC4"),
        new("Matcha", "DEX aggregator", "Non-custodial",
            "0x-powered aggregator that routes across dozens of DEXes on Ethereum, Base, Arbitrum and more for the best on-chain fill.",
            "https://matcha.xyz", "MAT", "#2E7DF7"),
        new("Curve", "DEX · stablecoins", "Non-custodial",
            "The deepest liquidity for stablecoin and pegged-asset swaps, with minimal slippage. All on-chain.",
            "https://curve.finance", "CRV", "#F5C542"),
        new("Osmosis", "DEX · Cosmos", "Non-custodial",
            "The main Cosmos-ecosystem DEX — cross-chain swaps over IBC, fast and low-fee, custody stays with you.",
            "https://app.osmosis.zone", "OSMO", "#7A5CFF"),
        new("SushiSwap", "DEX", "Non-custodial",
            "Long-running multi-chain DEX (Ethereum, Arbitrum, Base, Polygon and more) — swap and pool on-chain, no account.",
            "https://www.sushi.com/swap", "SUSHI", "#E5568F"),
        new("Raydium", "DEX · Solana", "Non-custodial",
            "A leading Solana AMM/DEX — fast, cheap on-chain swaps across the Solana ecosystem.",
            "https://raydium.io/swap", "RAY", "#3AB7E0"),
        new("Bisq", "P2P exchange", "Non-custodial · P2P",
            "Desktop, account-free Bitcoin ↔ fiat over a secured peer network with security deposits. Nothing is held by a company.",
            "https://bisq.network", "BSQ", "#25B135"),
        new("Hodl Hodl", "P2P escrow", "Non-custodial · escrow",
            "Global Bitcoin P2P with multisig escrow and no KYC — funds sit in escrow you co-sign, never in custody.",
            "https://hodlhodl.com", "HH", "#F59E0B"),
        new("RoboSats", "P2P · Lightning", "Non-custodial · P2P",
            "Private Bitcoin Lightning P2P using hold invoices. Nickname-only, no accounts, no data collection.",
            "https://robosats.com", "ROBO", "#8A5FD6"),
        new("Peach", "P2P", "Non-custodial · escrow",
            "Bitcoin ↔ fiat peer-to-peer with escrow, mobile-first, many local payment methods.",
            "https://peachbitcoin.com", "PCH", "#F97362"),
        new("Haveno", "P2P · Monero", "Non-custodial · P2P",
            "Decentralised Monero ↔ fiat/crypto exchange with multisig escrow and no accounts — the private-coin counterpart to Bisq.",
            "https://haveno.exchange", "HAV", "#F26822"),
        new("Vexl", "P2P · no-KYC", "Non-custodial · P2P",
            "Buy/sell Bitcoin peer-to-peer through your own social circle, phone-based, no accounts and no data harvesting — privacy first.",
            "https://vexl.it", "VEXL", "#EAB308"),
        new("LocalCoinSwap", "P2P escrow", "Non-custodial · escrow",
            "Global multi-coin P2P with non-custodial escrow and hundreds of payment methods; you hold the keys throughout.",
            "https://localcoinswap.com", "LCS", "#16A34A"),
    ];

    /// <summary>Card/bank fiat on-ramps for the Buy page. Every one delivers the crypto straight to a
    /// self-custody address you paste at checkout — Umbrella never holds funds or takes a cut. KYC and
    /// availability depend on the provider and your region; the aggregator is listed first for best rates.</summary>
    public ObservableCollection<P2pVenue> OnRampVenues { get; } =
    [
        new("Onramper", "Aggregator", "Delivers to your address",
            "Compares MoonPay, Ramp, Transak, Banxa and more in one place and routes you to the cheapest for your card and country.",
            "https://onramper.com", "ONR", "#3B82F6"),
        new("MoonPay", "On-ramp", "Delivers to your address",
            "Buy 100+ coins with card, Apple/Google Pay or bank transfer. Widely supported, quick verification.",
            "https://www.moonpay.com/buy", "MOON", "#7B61FF"),
        new("Ramp Network", "On-ramp", "Delivers to your address",
            "Card and open-banking purchases with competitive fees; strong European coverage.",
            "https://ramp.network", "RAMP", "#21BF73"),
        new("Transak", "On-ramp", "Delivers to your address",
            "160+ countries, many local payment rails; buy direct to your wallet address.",
            "https://transak.com", "TRSK", "#0052FF"),
        new("Banxa", "On-ramp", "Delivers to your address",
            "Regulated global on-ramp with bank transfer and card, plus lots of local methods.",
            "https://banxa.com", "BNXA", "#0E7C86"),
        new("Mercuryo", "On-ramp", "Delivers to your address",
            "Fast card purchases with a flat, transparent fee; good for smaller top-ups.",
            "https://mercuryo.io", "MERC", "#6E56CF"),
        new("Guardarian", "On-ramp", "Delivers to your address",
            "Buy and sell fiat ↔ crypto, no account required for many amounts; delivered to your address.",
            "https://guardarian.com", "GRD", "#12B886"),
    ];

    /// <summary>
    /// Market prices are public data, so this runs with the vault locked too — the user can
    /// see which coins the wallet accepts before committing to creating a vault.
    /// </summary>
    /// <summary>Fill the Market rows with the last-seen prices so the list reads instantly on open,
    /// before the live fetch returns (kills the "prices populate one by one" flicker).</summary>
    private void RestoreMarketCache()
    {
        var cached = _marketCache.Load();
        if (cached.Count == 0) return;
        var bySym = cached.ToDictionary(e => e.Symbol, e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var chain in ChainCatalog.All)
        {
            if (!bySym.TryGetValue(chain.Symbol, out var e)) continue;
            var idx = Market.ToList().FindIndex(m => m.Symbol == chain.Symbol);
            if (idx >= 0) Market[idx] = MarketRowViewModel.Live(chain, e.Price, e.Change) with { Spark = Market[idx].Spark };
        }
        foreach (var (sym, name, holdable) in ExtraMarketCoins)
        {
            if (!bySym.TryGetValue(sym, out var e)) continue;
            var idx = Market.ToList().FindIndex(m => m.Symbol == sym);
            if (idx >= 0) Market[idx] = MarketRowViewModel.LiveCoin(sym, name, e.Price, e.Change, holdable) with { Spark = Market[idx].Spark };
        }

        ApplyWatchlist();
    }

    private void SaveMarketCache() =>
        _marketCache.Save(Market.Where(m => m.Price > 0).Select(m => new MarketCache.Entry(m.Symbol, m.Price, m.Change24h)));

    [RelayCommand]
    private async Task RefreshMarketAsync()
    {
        try
        {
            var symbols = ChainCatalog.All.Select(c => c.Symbol)
                .Concat(ExtraMarketCoins.Select(e => e.Symbol))
                .ToList();
            var prices = await _rates.GetUsdPricesAsync(symbols, CancellationToken.None);
            if (prices.Count == 0)
            {
                MarketStatus = "Market feed unreachable — prices unavailable, wallet still works offline";
                return;
            }

            foreach (var chain in ChainCatalog.All)
            {
                var idx = Market.ToList().FindIndex(m => m.Symbol == chain.Symbol);
                if (idx < 0) continue;
                var (usd, change) = prices.GetValueOrDefault(chain.Symbol);
                // Keep any sparkline we already fetched so the row doesn't blink empty on refresh.
                var existingSpark = Market[idx].Spark;
                Market[idx] = MarketRowViewModel.Live(chain, (double)usd, (double)change) with
                {
                    Spark = existingSpark,
                };
            }

            foreach (var (sym, name, holdable) in ExtraMarketCoins)
            {
                var idx = Market.ToList().FindIndex(m => m.Symbol == sym);
                if (idx < 0) continue;
                var (usd, change) = prices.GetValueOrDefault(sym);
                var existingSpark = Market[idx].Spark;
                Market[idx] = MarketRowViewModel.LiveCoin(sym, name, (double)usd, (double)change, holdable) with
                {
                    Spark = existingSpark,
                };
            }

            MarketStatus = $"Live · {prices.Count} coins · updated {DateTime.Now:HH:mm:ss}";
            SaveMarketCache();
            ApplyWatchlist();
            _ = LoadSparklinesAsync();
        }
        catch (Exception ex)
        {
            MarketStatus = $"Market feed failed: {ex.Message}";
        }
    }

    /// <summary>From a holdings row's "›": jump to Market and open that coin's chart — the reference's
    /// "tap a coin to see its detail" behaviour, reusing the full Market chart rather than a new page.</summary>
    [RelayCommand]
    private async Task OpenAssetChartAsync(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        SelectSection("Market");
        var row = Market.FirstOrDefault(m => string.Equals(m.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (row is not null) await SelectMarketCoinAsync(row);
    }

    /// <summary>Click a market row → fetch its price series at the selected window and draw a chart.</summary>
    [RelayCommand]
    private async Task SelectMarketCoinAsync(MarketRowViewModel? row)
    {
        if (row is null) return;
        SelectedMarketSymbol = row.Symbol;
        SelectedMarketName = row.Name;
        SelectedMarketPriceLabel = row.PriceLabel;
        SelectedMarketChangeLabel = row.ChangeLabel;
        SelectedMarketChangeColor = row.ChangeColor;
        HasChart = true;
        IsChartLoading = true;
        ChartPoints = new System.Collections.Generic.List<Avalonia.Point>();

        try
        {
            var candles = await _rates.GetCandlesAsync(row.Symbol, ChartRange, CancellationToken.None);
            BuildDetailChart(candles);
            if (ChartPoints.Count == 0)
            {
                MarketStatus = $"No chart data for {row.Symbol} right now";
            }
        }
        catch (Exception ex)
        {
            MarketStatus = $"Chart failed: {ex.Message}";
        }
        finally
        {
            IsChartLoading = false;
        }

        // Uniswap-style 24h stats under the chart, from the same Binance feed (best-effort).
        HasMarketStats = false;
        try
        {
            var stats = await _rates.GetMarketStatsAsync(row.Symbol, CancellationToken.None);
            if (stats is not null)
            {
                StatHigh24h = Fx.Price((double)stats.High);
                StatLow24h = Fx.Price((double)stats.Low);
                StatVolume24h = FormatCompactMoney((double)stats.QuoteVolume);
                HasMarketStats = true;
            }
        }
        catch { /* stats are a nicety; never break the detail view over them */ }

        // Optional richer stats (market cap / FDV) — only if the user enabled the connector.
        HasRichStats = false;
        if (RichMarketData)
        {
            try
            {
                var md = await _rates.GetTokenMarketDataAsync(row.Symbol, CancellationToken.None);
                if (md is not null && md.MarketCap > 0)
                {
                    StatMarketCap = FormatCompactMoney((double)md.MarketCap);
                    StatFdv = md.Fdv > 0 ? FormatCompactMoney((double)md.Fdv) : StatMarketCap;
                    if (md.Volume24h > 0) StatVolume24h = FormatCompactMoney((double)md.Volume24h);
                    HasRichStats = true;
                }
            }
            catch { /* connector is best-effort */ }
        }
    }

    /// <summary>Compact money in the display currency: 4.6B, 1.5T, 32.4K…</summary>
    private static string FormatCompactMoney(double usd)
    {
        var v = usd * (double)Fx.Rate;
        var (num, suffix) = v switch
        {
            >= 1e12 => (v / 1e12, "T"),
            >= 1e9 => (v / 1e9, "B"),
            >= 1e6 => (v / 1e6, "M"),
            >= 1e3 => (v / 1e3, "K"),
            _ => (v, ""),
        };
        return $"{Fx.Symbol}{num:0.##}{suffix}";
    }

    /// <summary>
    /// Fetches a price series for every listed coin once, so each market row draws its own
    /// sparkline. Sequential with a small gap because CoinGecko rate-limits bursts.
    /// </summary>
    private async Task LoadSparklinesAsync(bool force = false)
    {
        foreach (var row in Market.ToList())
        {
            if (row.HasSpark && !force) continue;
            try
            {
                // Real candles at the selected window, not a fixed 7-day daily series.
                var series = await _rates.GetPriceSeriesAsync(
                    row.Symbol, ChartRange, CancellationToken.None);
                if (series.Count < 2) continue;
                var idx = Market.ToList().FindIndex(m => m.Symbol == row.Symbol);
                if (idx < 0) continue;
                Market[idx] = Market[idx] with
                {
                    Spark = BuildChartPoints(series, SparkWidth, SparkHeight),
                };
            }
            catch
            {
                // a missing sparkline is cosmetic — never break the market list over it
            }

            await Task.Delay(250);
        }
    }

    private const double SparkWidth = 110;
    private const double SparkHeight = 30;

    [RelayCommand]
    private void ToggleBalanceHidden() => IsBalanceHidden = !IsBalanceHidden;

    [RelayCommand]
    private void SetChainFilter(string filter)
    {
        ChainFilter = filter;
        RefreshHoldings();
    }

    /// <summary>Sets how Holdings are ordered. Picking the active sort again toggles back to the catalog
    /// order, so the chips double as an on/off.</summary>
    [RelayCommand]
    private void SetHoldingsSort(string sort)
    {
        HoldingsSort = string.Equals(HoldingsSort, sort, StringComparison.OrdinalIgnoreCase)
            ? HoldingsSorter.Default : sort;
        RefreshHoldings();
    }

    private string ActiveWalletCacheKey => _registry.Active?.Id ?? "main";

    /// <summary>Apply the last-seen balances/prices for the active wallet so the total is right the
    /// instant it unlocks — before the live refresh returns — instead of flashing $0. Matched to
    /// accounts by symbol + address; the refresh overwrites with authoritative data moments later.</summary>
    private void RestoreCachedBalances()
    {
        var cached = _balanceStore.Load(ActiveWalletCacheKey);
        if (cached.Count == 0) return;
        var byKey = cached.ToDictionary(e => e.Symbol + "|" + e.Address, e => e);
        var touched = false;
        for (var i = 0; i < Accounts.Count; i++)
        {
            var a = Accounts[i];
            if (byKey.TryGetValue(a.Symbol + "|" + a.Address, out var e))
            {
                Accounts[i] = a with { Amount = e.Amount, Price = e.Price, Change24h = e.Change };
                touched = true;
            }
        }
        if (touched) { RefreshHoldings(); RecalcBalance(); }
    }

    /// <summary>Persist the current balances/prices so the next unlock/switch shows them instantly.</summary>
    private void SaveBalanceCache()
    {
        var entries = Accounts
            .Where(a => a.Amount > 0 || a.Price > 0)
            .Select(a => new BalanceStore.Entry(a.Symbol, a.Address, a.Amount, a.Price, a.Change24h));
        _balanceStore.Save(ActiveWalletCacheKey, entries);
    }

    [RelayCommand]
    private async Task RefreshLiveDataAsync()
    {
        if (!IsUnlocked) return;
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;
        IsBusy = true;
        StatusMessage = "Refreshing live prices & balances…";
        try
        {
            // Price every symbol we will show, including the chains behind watch-only addresses.
            // Those rows are appended later in this method, so pricing only the current Accounts
            // list would leave a freshly linked wallet unpriced — and therefore worth $0.
            var symbols = Accounts.Select(a => a.Symbol)
                .Concat(WatchAddresses
                    .Select(w => ParseChain(w.Chain))
                    .Where(c => c is not null)
                    .Select(c => SymbolFor(c!.Value)))
                .Concat(["USDT", "BNB", "MATIC", "AVAX", "FTM", "CRO"])
                .Distinct()
                .ToList();
            var prices = await _rates.GetUsdPricesAsync(symbols, ct);
            _priceUsd = prices; // snapshot for the Send fiat estimate
            OnPropertyChanged(nameof(SendAmountFiat));
            OnPropertyChanged(nameof(FiatInputAvailable)); // the fiat quick-entry appears once priced
            OnPropertyChanged(nameof(SendFiatCoinEquiv));

            // Fetch every account's balance CONCURRENTLY, then apply on the UI thread. Sequential
            // awaits here were the main reason the total took many seconds to appear after unlock /
            // wallet switch; firing them together cuts that to roughly the slowest single call.
            // (Receive-only chains TON/ADA have public balance APIs; XMR returns null safely.)
            // BTC/LTC are handled by the HD scan below (aggregated across every address), not by the
            // single-address balance call — otherwise change sent to an internal address would vanish
            // from the shown balance.
            var balanceTargets = Accounts.ToList()
                .Where(a => a.SupportStatus is "Ready" or "Receive only" && ParseChain(a.Symbol) is not null
                            && a.Symbol is not ("BTC" or "LTC"))
                .ToList();
            var balanceResults = await Task.WhenAll(
                balanceTargets.Select(a => _balances.GetBalanceAsync(ParseChain(a.Symbol)!.Value, a.Address, ct)));

            for (var k = 0; k < balanceTargets.Count; k++)
            {
                var account = balanceTargets[k];
                var amount = balanceResults[k] is { } bal ? bal.NativeAmount : (decimal)account.Amount;
                var (usd, change) = prices.GetValueOrDefault(account.Symbol);
                var idx = Accounts.IndexOf(account);
                if (idx >= 0)
                {
                    Accounts[idx] = account with
                    {
                        Amount = (double)amount,
                        Price = (double)usd,
                        Change24h = (double)change,
                    };
                }
            }
            // Show the total from native balances immediately, before the slower token/NFT/watch passes.
            RefreshHoldings();
            RecalcBalance();

            // BTC/LTC: scan every derived address (external + internal) and aggregate — the balance
            // the wallet shows is exactly the set it can find and spend.
            await RefreshUtxoWalletsAsync(prices, ct);

            // Every TRC-20 token on our OWN derived TRON account — not just USDT. Reward tokens,
            // other stablecoins and any TRC-20 asset now appear next to the native coins, which is
            // what most "my TRON balance is missing" reports actually are.
            var tronAccount = Accounts.FirstOrDefault(a => a.Symbol == "TRX" && a.SupportStatus == "Ready");
            if (tronAccount is not null && IsRealAddress(tronAccount.Address))
            {
                await AddTronTokenRowsAsync(tronAccount.Address, "Ready", prices, ct);
            }

            // Same for ERC-20 tokens on our OWN Ethereum account — any token, not just native ETH —
            // plus the native coins of the major EVM side-chains (BNB, MATIC, AVAX) at the same address.
            var ethAccount = Accounts.FirstOrDefault(a => a.Symbol == "ETH" && a.SupportStatus == "Ready");
            if (ethAccount is not null && IsRealAddress(ethAccount.Address))
            {
                await AddEthTokenRowsAsync(ethAccount.Address, "Ready", prices, ct);
                await AddEvmSideRowsAsync(ethAccount.Address, prices, ct);
                await RefreshNftsAsync(ethAccount.Address, ct);
            }

            // Watch-only
            foreach (var watch in WatchAddresses.ToList())
            {
                var chain = ParseChain(watch.Chain);
                if (chain is null) continue;
                // Canonical ticker, never the raw user input — see SymbolFor.
                var nativeSymbol = SymbolFor(chain.Value);
                var bal = await _balances.GetBalanceAsync(chain.Value, watch.Address, ct);
                var (usd, change) = prices.GetValueOrDefault(nativeSymbol);
                var existing = Accounts.FirstOrDefault(a =>
                    a.Address.Equals(watch.Address, StringComparison.OrdinalIgnoreCase) &&
                    a.Symbol == nativeSymbol);
                var row = new WalletAccountViewModel(
                    nativeSymbol,
                    string.IsNullOrWhiteSpace(watch.Label) ? $"Watch · {nativeSymbol}" : watch.Label,
                    "Watch",
                    watch.Address,
                    "external",
                    (double)usd,
                    bal is null ? 0 : (double)bal.NativeAmount,
                    nativeSymbol,
                    (double)change);
                if (existing is null) Accounts.Add(row);
                else
                {
                    var i = Accounts.IndexOf(existing);
                    Accounts[i] = row;
                }

                // A watched TRON address can hold any TRC-20 tokens — show them all, not just USDT.
                if (IsTronLike(watch.Chain))
                {
                    await AddTronTokenRowsAsync(watch.Address, "Watch", prices, ct);
                }
                else if (chain.Value == ChainId.Eth)
                {
                    await AddEthTokenRowsAsync(watch.Address, "Watch", prices, ct);
                }
            }

            await RefreshExchangeBalancesAsync(ct);

            RefreshHoldings();
            RecalcBalance();
            SaveBalanceCache(); // remember these totals so the next unlock/switch is instant
            // NOTE: deliberately no "Sync" activity entry here. This runs every 60s on a timer, and
            // logging it flooded the Activity feed with identical "Sync · OK" rows. The live status
            // line below already shows the last-updated time; the Activity feed is for real events.
            StatusMessage = $"Live · {Holdings.Count} assets · updated {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            /* ignored */
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Adds/refreshes a Holdings row for every TRC-20 token at a TRON address (own or watch-only),
    /// so any token — reward tokens, other stablecoins, tokenised assets — shows up, not just USDT.
    /// </summary>
    private async Task AddTronTokenRowsAsync(
        string address, string status,
        IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices, CancellationToken ct) =>
        AddTokenRows(await _balances.GetTronTokensAsync(address, ct),
            address, status, prices, marker: "TRC20 on TRON", chain: "TRON", suffix: "TRC20");

    private async Task AddEthTokenRowsAsync(
        string address, string status,
        IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices, CancellationToken ct) =>
        AddTokenRows(await _balances.GetEthTokensAsync(address, ct),
            address, status, prices, marker: "ERC20 on Ethereum", chain: "Ethereum", suffix: "ERC20");

    /// <summary>Refreshes the NFT list from the wallet's Ethereum address (names + counts only).</summary>
    private async Task RefreshNftsAsync(string address, CancellationToken ct)
    {
        var nfts = await _balances.GetEthNftsAsync(address, ct);
        Nfts.Clear();
        foreach (var n in nfts) Nfts.Add(n);
        OnPropertyChanged(nameof(HasNfts));
    }

    /// <summary>Adds Holdings rows for BNB / MATIC / AVAX held at our Ethereum (0x) address.</summary>
    private async Task AddEvmSideRowsAsync(
        string address, IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices, CancellationToken ct)
    {
        foreach (var stale in Accounts
                     .Where(a => a.Derivation == "EVM side-chain" &&
                                 a.Address.Equals(address, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            Accounts.Remove(stale);
        }

        foreach (var (symbol, amount, network) in await _balances.GetEvmSideBalancesAsync(address, ct))
        {
            var (usd, change) = prices.GetValueOrDefault(symbol);
            Accounts.Add(new WalletAccountViewModel(
                symbol, $"{symbol} · {network}", "Ready", address, "EVM side-chain",
                (double)usd, (double)amount, network, (double)change));
        }
    }

    /// <summary>Reconciles the Holdings rows for a set of tokens at one address (TRC-20 or ERC-20).</summary>
    private void AddTokenRows(
        IReadOnlyList<TokenBalance> tokens, string address, string status,
        IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices,
        string marker, string chain, string suffix)
    {
        // Drop previous token rows for this address+network so removed or zeroed tokens don't linger.
        foreach (var stale in Accounts
                     .Where(a => a.Derivation == marker &&
                                 a.Address.Equals(address, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            Accounts.Remove(stale);
        }

        foreach (var tok in tokens)
        {
            // Price known tokens from the live feed; treat the major stablecoins as $1; an unknown
            // token shows its real amount at $0 rather than an invented price.
            var usd = prices.TryGetValue(tok.Symbol, out var pr)
                ? (double)pr.Usd
                : tok.Symbol is "USDT" or "USDC" or "DAI" or "TUSD" or "USDD" ? 1.0 : 0.0;
            Accounts.Add(new WalletAccountViewModel(
                tok.Symbol, $"{tok.Name} · {suffix}", status,
                address, marker, usd, (double)tok.Amount, chain, 0));
        }
    }

    /// <summary>
    /// Pulls balances from every connected exchange and folds them into Holdings, so exchange
    /// funds sit next to on-chain funds and count toward the same total.
    ///
    /// Assets the price feed doesn't cover are still listed with their real amount at $0 — the
    /// quantity is true, and inventing a price would be worse than showing none.
    /// </summary>
    private async Task RefreshExchangeBalancesAsync(CancellationToken ct)
    {
        if (Exchanges.Count == 0) return;

        // Drop previous exchange rows so removed or zeroed assets don't linger.
        foreach (var stale in Accounts.Where(a => a.SupportStatus == "Exchange").ToList())
        {
            Accounts.Remove(stale);
        }

        foreach (var credential in Exchanges.ToList())
        {
            var result = await ExchangeConnectors.FetchBalancesAsync(
                credential.Exchange, credential.ApiKey, credential.ApiSecret, credential.Passphrase, ct);

            if (!result.Ok)
            {
                ExchangeError = result.Error ?? $"{credential.Exchange}: could not refresh.";
                continue;
            }

            if (result.Assets.Count == 0) continue;

            var prices = await _rates.GetUsdPricesAsync(
                result.Assets.Select(a => a.Symbol).Distinct().ToList(), ct);

            foreach (var asset in result.Assets)
            {
                var (usd, change) = prices.GetValueOrDefault(asset.Symbol);
                Accounts.Add(new WalletAccountViewModel(
                    asset.Symbol,
                    $"{credential.Label} · {asset.Symbol}",
                    "Exchange",
                    credential.Label,
                    credential.Exchange,
                    (double)usd,
                    (double)asset.Amount,
                    credential.Exchange,
                    (double)change));
            }
        }
    }

    [RelayCommand]
    private async Task CopyPrimaryAddressAsync()
    {
        var ready = Accounts.FirstOrDefault(a =>
            (a.SupportStatus is "Ready" or "Watch") && IsRealAddress(a.Address));
        if (ready is null)
        {
            StatusMessage = "No address to copy yet";
            return;
        }

        await CopyTextAsync(ready.Address);
        StatusMessage = $"Copied {ready.Symbol} address";
        ShowToast($"{ready.Symbol} · {Loc.Instance["toast.copied"]}", isError: false);
    }

    [RelayCommand]
    private async Task CopyAddressAsync(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !IsRealAddress(address)) return;
        await CopyTextAsync(address);
        StatusMessage = "Address copied";
        ShowToast(Loc.Instance["toast.copied"], isError: false);
    }

    [RelayCommand]
    private void SelectReceiveAccount(WalletAccountViewModel? account)
    {
        if (!SetReceiveTarget(account)) return;
        // Only an explicit click opens the popup — pre-selecting on unlock must not.
        IsQrPopupOpen = true;
    }

    [RelayCommand]
    private void CloseQrPopup() => IsQrPopupOpen = false;

    // --- Backup / restore ---------------------------------------------------
    [ObservableProperty] private string _backupStatus = string.Empty;
    [ObservableProperty] private string _backupError = string.Empty;

    /// <summary>Set by the view so the view-model can raise a file dialog without knowing about windows.
    /// Args: suggested file name, save (true) vs open (false), and a kind ("json" backup, "csv" history)
    /// so the dialog offers the right extension/filter. Returns the chosen local path, or null.</summary>
    public Func<string, bool, string, Task<string?>>? PickFileAsync { get; set; }

    /// <summary>Raised by the view to pick an image file (avatar/banner/background) to open.</summary>
    public Func<Task<string?>>? PickImageAsync { get; set; }

    // --- Profile customisation: the user's own avatar, banner and sidebar background. Each is a
    // file they pick; we copy it into the data folder so it survives and never has to be bundled. ---
    [ObservableProperty] private Bitmap? _avatarImage;
    [ObservableProperty] private Bitmap? _bannerImage;
    [ObservableProperty] private Bitmap? _sidebarBgImage;
    [ObservableProperty] private Bitmap? _lockBgImage;

    public bool HasAvatar => AvatarImage is not null;
    public bool HasBanner => BannerImage is not null;
    public bool HasSidebarBg => SidebarBgImage is not null;

    /// <summary>The user picked a custom lock-screen background.</summary>
    public bool HasLockBg => LockBgImage is not null;
    /// <summary>Lock screen shows no background at all (flat) — user chose to remove it.</summary>
    public bool LockScreenPlain
    {
        get => _uiSettings.LockScreenPlain;
        set
        {
            if (_uiSettings.LockScreenPlain == value) return;
            _uiSettings.LockScreenPlain = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowDefaultLockBg));
            OnPropertyChanged(nameof(ShowCustomLockBg));
        }
    }
    /// <summary>Show the bundled default lock backdrop: locked, not plain, and no custom image set.</summary>
    public bool ShowDefaultLockBg => IsUnlockStage && !LockScreenPlain && !HasLockBg;
    /// <summary>Show the user's custom lock backdrop: locked, not plain, custom image present.</summary>
    public bool ShowCustomLockBg => IsUnlockStage && !LockScreenPlain && HasLockBg;

    partial void OnAvatarImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasAvatar));
    partial void OnBannerImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasBanner));
    partial void OnSidebarBgImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasSidebarBg));
    partial void OnLockBgImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasLockBg));
        OnPropertyChanged(nameof(ShowDefaultLockBg));
        OnPropertyChanged(nameof(ShowCustomLockBg));
    }

    private static string ProfileDir => System.IO.Path.Combine(AppPaths.DataRoot, "profile");

    private static Bitmap? LoadBitmap(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) ? null : new Bitmap(path); }
        catch { return null; }
    }

    public void LoadProfileImages()
    {
        AvatarImage = LoadBitmap(_uiSettings.AvatarPath);
        BannerImage = LoadBitmap(_uiSettings.BannerPath);
        SidebarBgImage = LoadBitmap(_uiSettings.SidebarBackgroundPath);
        LockBgImage = LoadBitmap(_uiSettings.LockBackgroundPath);
    }

    private async Task PickProfileImageAsync(string name, Action<string> setPath)
    {
        if (PickImageAsync is null) return;
        var src = await PickImageAsync();
        if (string.IsNullOrWhiteSpace(src) || !System.IO.File.Exists(src)) return;
        try
        {
            System.IO.Directory.CreateDirectory(ProfileDir);
            var dest = System.IO.Path.Combine(ProfileDir, name + System.IO.Path.GetExtension(src));
            System.IO.File.Copy(src, dest, overwrite: true);
            setPath(dest);
            _uiSettings.Save();
            LoadProfileImages();
            StatusMessage = "Profile image updated";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not set the image: {ex.Message}";
        }
    }

    [RelayCommand] private Task PickAvatar() => PickProfileImageAsync("avatar", p => _uiSettings.AvatarPath = p);
    [RelayCommand] private Task PickBanner() => PickProfileImageAsync("banner", p => _uiSettings.BannerPath = p);
    [RelayCommand] private Task PickSidebarBg() => PickProfileImageAsync("sidebar", p => _uiSettings.SidebarBackgroundPath = p);
    [RelayCommand] private Task PickLockBg() => PickProfileImageAsync("lockbg", p => _uiSettings.LockBackgroundPath = p);

    /// <summary>Revert the lock screen to the bundled default background.</summary>
    [RelayCommand]
    private void ClearLockBg()
    {
        _uiSettings.LockBackgroundPath = "";
        _uiSettings.Save();
        LockBgImage = null;
        LoadProfileImages();
    }

    [RelayCommand]
    private void ClearProfileImages()
    {
        _uiSettings.AvatarPath = _uiSettings.BannerPath = _uiSettings.SidebarBackgroundPath = "";
        _uiSettings.Save();
        LoadProfileImages();
    }

    // Password typed to verify a backup can actually be decrypted. Held only for the check, then cleared.
    [ObservableProperty] private string _backupVerifyPassword = string.Empty;

    /// <summary>
    /// Proves a chosen backup file is genuinely restorable — it decrypts with the given password and
    /// holds a valid recovery phrase — without ever revealing the seed. A backup you cannot restore is
    /// worthless, so this lets the user confirm it BEFORE they rely on it.
    /// </summary>
    [RelayCommand]
    private async Task VerifyBackupAsync()
    {
        BackupStatus = BackupError = string.Empty;
        if (PickFileAsync is null) return;

        var pw = BackupVerifyPassword ?? string.Empty;
        if (pw.Length == 0) { BackupError = Loc.Instance["backup.errPassword"]; return; }

        var path = await PickFileAsync(string.Empty, false, "json");
        if (string.IsNullOrWhiteSpace(path)) return;

        var result = await VaultBackup.VerifyAsync(path, pw);
        BackupVerifyPassword = string.Empty; // don't keep the password around after the check

        if (!result.Ok) { BackupError = BackupMessage(result); return; }

        var extras = new List<string>();
        if (result.ExportedUtc is { } dt)
            extras.Add(string.Format(Loc.Instance["backup.made"], dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
        if (result.HasWatchAddresses) extras.Add(Loc.Instance["backup.hasWatch"]);
        if (result.HasExchanges) extras.Add(Loc.Instance["backup.hasKeys"]);
        var verdict = BackupMessage(result);
        BackupStatus = extras.Count > 0 ? $"{verdict} · {string.Join(" · ", extras)}" : verdict;
    }

    /// <summary>
    /// The backup verdict in the user’s language. The infrastructure layer reports *why* as an enum;
    /// only the UI knows about languages, so the mapping lives here. An unrecognised reason falls back
    /// to the layer’s own sentence rather than showing nothing (roadmap §8.2).
    /// </summary>
    private static string BackupMessage(VaultBackupVerification result) => result.Reason switch
    {
        VaultBackupReason.Verified => Loc.Instance["backup.verified"],
        VaultBackupReason.FileMissing => Loc.Instance["backup.fileMissing"],
        VaultBackupReason.NotABackup => Loc.Instance["backup.notABackup"],
        VaultBackupReason.NoVault => Loc.Instance["backup.noVault"],
        VaultBackupReason.WrongPassword => Loc.Instance["backup.wrongPassword"],
        VaultBackupReason.DamagedPhrase => Loc.Instance["backup.damagedPhrase"],
        VaultBackupReason.CorruptVault => Loc.Instance["backup.corruptVault"],
        _ => result.Message,
    };

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        BackupStatus = BackupError = string.Empty;
        if (PickFileAsync is null) return;

        var path = await PickFileAsync(VaultBackup.SuggestedFileName(), true, "json");
        if (string.IsNullOrWhiteSpace(path)) return;

        var (ok, message) = await VaultBackup.ExportAsync(path);
        if (ok) BackupStatus = message; else BackupError = message;
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        BackupStatus = BackupError = string.Empty;
        if (PickFileAsync is null) return;

        var path = await PickFileAsync(string.Empty, false, "json");
        if (string.IsNullOrWhiteSpace(path)) return;

        var (ok, message) = await VaultBackup.RestoreAsync(path);
        if (!ok) { BackupError = message; return; }

        // The vault on disk changed underneath us, so drop the unlocked session rather than
        // leaving the UI showing the previous wallet's accounts.
        LockVault();
        BackupStatus = message;
    }

    /// <summary>Points the QR/address at an account. Opens on the base receive address (#0); on the
    /// full-HD chains (BTC/LTC) the user can then rotate to a fresh address — safe now that the wallet
    /// discovers and spends across every issued index, so funds on #1+ are found and spendable.</summary>
    private bool SetReceiveTarget(WalletAccountViewModel? account)
    {
        if (account is null || !IsRealAddress(account.Address)) return false;
        ReceiveAmount = string.Empty; // a fresh target starts with no requested amount
        ShowReceiveAdvanced = false;  // collapse developer detail on every new target
        SelectedReceiveAddress = account.Address;
        SelectedReceiveSymbol = account.Symbol;
        SelectedReceiveNetwork = $"{account.Symbol} · {account.NetworkLabel}";
        ReceiveQr = BuildQr(BuildReceivePayload(account.Address));

        // Rotation is only safe where the wallet fully spends across addresses (BTC/LTC).
        _receiveChain = ParseChain(account.Symbol);
        CanRotateReceive = account.Symbol is "BTC" or "LTC"
                           && _receiveChain is not null && _unlockedMnemonic is not null;
        ReceivePathLabel = CanRotateReceive ? $"{account.Symbol} receive address #0" : string.Empty;
        RebuildReceiveHistory(account.Symbol);
        _receiveIndex = 0;
        QueueReuseCheck(account.Address, 0);
        return true;
    }

    /// <summary>The external HD index of the receive address currently on screen (0 = the default one).</summary>
    private uint _receiveIndex;

    /// <summary>
    /// Raises the address-reuse warning when the address on screen provably already has on-chain
    /// history (secure roadmap 3.3). Reusing a receive address lets any observer tie the two senders
    /// to the same wallet, so the user is told and pointed at "Generate new address".
    ///
    /// Only runs where a fresh address can actually be offered (BTC/LTC) — warning without a remedy
    /// is noise. Fail-closed on *silence*, not on doubt: an unreachable explorer leaves the banner
    /// off and simply reports nothing, and it never claims an address is fresh without evidence.
    /// </summary>
    private void QueueReuseCheck(string address, uint index)
    {
        _reuseCheckCts?.Cancel();
        _reuseCheckCts = null;
        ReceiveAddressReused = false;
        ReceiveAddressCheckPending = false;

        if (!CanRotateReceive || string.IsNullOrWhiteSpace(address)) return;

        var cts = new CancellationTokenSource();
        _reuseCheckCts = cts;
        var symbol = SelectedReceiveSymbol;
        var walletId = _registry.Active?.Id ?? "default";
        uint? floor = null;
        try { floor = _addrIndex.GetState(walletId, symbol).LastSeenUsedExternalIndex; } catch { }
        ReceiveAddressCheckPending = true;

        _ = Task.Run(async () =>
        {
            AddressUseState verdict;
            try
            {
                verdict = await _reuseInspector.InspectAsync(
                    UtxoExplorerFor(symbol), address, index, floor, cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch { verdict = AddressUseState.Unknown; }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // A newer address may have been shown while this check was in flight — ignore a
                // stale verdict rather than warning about an address that is no longer on screen.
                if (cts.IsCancellationRequested || !string.Equals(SelectedReceiveAddress, address, StringComparison.Ordinal))
                    return;
                ReceiveAddressCheckPending = false;
                ReceiveAddressReused = verdict == AddressUseState.Used;
            });
        });
    }

    [RelayCommand]
    private void ToggleReceiveAdvanced() => ShowReceiveAdvanced = !ShowReceiveAdvanced;

    // Notify the token-warning and requested-amount gates whenever the receive asset changes.
    partial void OnSelectedReceiveSymbolChanged(string value)
    {
        OnPropertyChanged(nameof(IsTokenReceive));
        OnPropertyChanged(nameof(CanRequestAmount));
    }

    // Re-render the QR the moment the requested amount changes so what's on screen always matches the field.
    partial void OnReceiveAmountChanged(string value)
    {
        if (!string.IsNullOrEmpty(SelectedReceiveAddress))
            ReceiveQr = BuildQr(BuildReceivePayload(SelectedReceiveAddress));
    }

    /// <summary>Encodes the receive target as a wallet payment URI. With a valid requested amount on a
    /// BIP21 chain (BTC/LTC/DOGE/BCH) it returns e.g. <c>bitcoin:addr?amount=0.5</c>; otherwise the plain
    /// address, so a scan can never carry a malformed or wrong-scheme payload.</summary>
    private string BuildReceivePayload(string address)
    {
        if (!CanRequestAmount || string.IsNullOrWhiteSpace(ReceiveAmount)) return address;

        if (!AmountInput.TryParsePositive(ReceiveAmount, out var amount)) return address;

        var amountStr = amount.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

        // BCH's CashAddr already carries the "bitcoincash:" scheme, so its payment URI is the address
        // itself plus the amount — prepending a scheme again would double the prefix.
        if (SelectedReceiveSymbol == "BCH")
            return address.Contains(':')
                ? $"{address}?amount={amountStr}"
                : $"bitcoincash:{address}?amount={amountStr}";

        var scheme = SelectedReceiveSymbol switch
        {
            "BTC" => "bitcoin",
            "LTC" => "litecoin",
            "DOGE" => "dogecoin",
            _ => null,
        };
        if (scheme is null) return address;

        return $"{scheme}:{address}?amount={amountStr}";
    }

    /// <summary>Lists the receive addresses already handed out (index 0..last issued), so the user can
    /// see and re-copy a past address. Re-derived from the seed; the indices come from durable state.</summary>
    private void RebuildReceiveHistory(string symbol)
    {
        ReceiveHistory.Clear();
        if (!CanRotateReceive || _receiveChain is null || _unlockedMnemonic is null) return;

        var walletId = _registry.Active?.Id ?? "default";
        var lastIssued = _addrIndex.GetState(walletId, symbol).LastIssuedExternalIndex ?? 0;
        for (uint i = 0; i <= lastIssued; i++)
            ReceiveHistory.Add(_deriver.DeriveBitcoinLikeAt(_unlockedMnemonic!, _receiveChain.Value, 0, i).Address);
        OnPropertyChanged(nameof(HasReceiveHistory));
    }

    /// <summary>
    /// Hands out a fresh receive address by reserving the next external HD index. The index is
    /// persisted BEFORE the address is shown and reserving throws on write failure, so a crash can
    /// never lose an address the user has already published. Bumping the issued index also raises the
    /// discovery scan floor, so funds received here are found and spendable.
    /// </summary>
    [RelayCommand]
    private void NewReceiveAddress()
    {
        if (!CanRotateReceive || _receiveChain is null || _unlockedMnemonic is null) return;

        var symbol = SelectedReceiveSymbol;
        var walletId = _registry.Active?.Id ?? "default";
        try
        {
            var index = _addrIndex.ReserveNextExternalIndex(walletId, symbol);
            var addr = _deriver.DeriveBitcoinLikeAt(_unlockedMnemonic!, _receiveChain.Value, 0, index).Address;
            SelectedReceiveAddress = addr;
            ReceiveQr = BuildQr(BuildReceivePayload(addr));
            ReceivePathLabel = $"{symbol} receive address #{index}";
            if (!ReceiveHistory.Contains(addr)) ReceiveHistory.Add(addr);
            OnPropertyChanged(nameof(HasReceiveHistory));
            _receiveIndex = index;
            QueueReuseCheck(addr, index);
            ShowToast(Loc.Instance["receive.newAddr"], isError: false);
        }
        catch
        {
            // Fail-closed: if the new index could not be persisted, do NOT show an address we might forget.
            ShowToast(Loc.Instance["receive.newAddrFail"], isError: true);
        }
    }

    /// <summary>
    /// Connects an exchange with READ-ONLY API keys. The keys are verified against the exchange
    /// before being stored, so a bad key fails here rather than silently showing an empty balance.
    /// </summary>
    [RelayCommand]
    private async Task ConnectExchangeAsync()
    {
        ExchangeError = string.Empty;
        ExchangeStatus = string.Empty;

        if (_unlockedMnemonic is null)
        {
            ExchangeError = "Unlock the vault first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ExchangeApiKey))
        {
            ExchangeError = ExchangeNeedsSecret
                ? "Enter both the API key and the API secret."
                : "Enter the CryptoBot API token.";
            return;
        }

        if (ExchangeNeedsSecret && string.IsNullOrWhiteSpace(ExchangeApiSecret))
        {
            ExchangeError = "Enter the API secret.";
            return;
        }

        if (ExchangeNeedsPassphrase && string.IsNullOrWhiteSpace(ExchangePassphrase))
        {
            ExchangeError = "OKX also needs the API passphrase you chose when creating the key.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            ExchangeStatus = $"Checking the {ExchangeName} key…";
            var result = await ExchangeConnectors.FetchBalancesAsync(
                ExchangeName, ExchangeApiKey.Trim(), ExchangeApiSecret.Trim(),
                string.IsNullOrWhiteSpace(ExchangePassphrase) ? null : ExchangePassphrase.Trim());

            if (!result.Ok)
            {
                ExchangeError = result.Error ?? "Could not reach the exchange.";
                ExchangeStatus = string.Empty;
                return;
            }

            var credential = new ExchangeCredential(
                ExchangeName,
                string.IsNullOrWhiteSpace(ExchangeLabel) ? ExchangeName : ExchangeLabel.Trim(),
                ExchangeApiKey.Trim(),
                ExchangeApiSecret.Trim(),
                string.IsNullOrWhiteSpace(ExchangePassphrase) ? null : ExchangePassphrase.Trim());

            Exchanges.Add(credential);
            await _exchangeStore.SaveAsync(Exchanges, _unlockedMnemonic!);

            // Don't keep the secret sitting in the form after it's been stored encrypted.
            ExchangeApiKey = string.Empty;
            ExchangeApiSecret = string.Empty;
            ExchangePassphrase = string.Empty;
            ExchangeLabel = string.Empty;

            ExchangeStatus = $"Connected · {result.Assets.Count} assets found";
            PushActivity("Connected", ExchangeName, "exchange", $"{result.Assets.Count} assets · read-only", "now");
            await RefreshLiveDataAsync();
        });
    }

    [RelayCommand]
    private async Task RemoveExchangeAsync(ExchangeCredential? credential)
    {
        if (credential is null || _unlockedMnemonic is null) return;
        Exchanges.Remove(credential);
        await _exchangeStore.SaveAsync(Exchanges, _unlockedMnemonic);

        // Drop its rows immediately so the total doesn't keep counting a removed account.
        foreach (var row in Accounts.Where(a => a.SupportStatus == "Exchange" &&
                                                a.Address == credential.Label).ToList())
        {
            Accounts.Remove(row);
        }

        RefreshHoldings();
        RecalcBalance();
        ExchangeStatus = "Exchange disconnected";
    }

    /// <summary>Guesses a chain from an address's shape, so a pasted watch address is tracked on the
    /// right network even if the dropdown was left elsewhere. Returns null when it's ambiguous.</summary>
    private static string? DetectChain(string a)
    {
        if (a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && a.Length == 42) return "ETH";
        if (a.StartsWith("bc1", StringComparison.OrdinalIgnoreCase)) return "BTC";
        if (a.StartsWith("ltc1", StringComparison.OrdinalIgnoreCase) || a.StartsWith("M", StringComparison.Ordinal)) return "LTC";
        if (a.StartsWith("addr1", StringComparison.OrdinalIgnoreCase)) return "ADA";
        if (a.StartsWith('T') && a.Length == 34) return "TRX";
        if (a.Length == 48 && (a.StartsWith("UQ") || a.StartsWith("EQ") || a.StartsWith("kQ") || a.StartsWith("0Q"))) return "TON";
        if ((a.StartsWith('4') || a.StartsWith('8')) && a.Length is 95 or 106) return "XMR";
        if (a.StartsWith('D') && a.Length == 34) return "DOGE";
        if (a.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase)) return "BCH";
        if (a.StartsWith("t1", StringComparison.Ordinal) && a.Length == 35) return "ZEC"; // Zcash transparent
        if ((a.StartsWith('1') || a.StartsWith('3')) && a.Length is >= 26 and <= 35) return "BTC";
        return null; // Solana / other base58 is ambiguous — keep the selected network
    }

    [RelayCommand]
    private async Task AddWatchAddressAsync()
    {
        var address = WatchAddress.Trim();
        // Auto-detect from the address itself (a T… address is TRON, bc1… is BTC, 0x… is EVM, …),
        // falling back to the dropdown only when the shape is ambiguous.
        var chain = DetectChain(address) ?? WatchChain.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(address) || address.Length < 10)
        {
            StatusMessage = "Paste a valid public address";
            return;
        }

        if (WatchAddresses.Any(w => w.Address.Equals(address, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "Address already linked";
            return;
        }

        var label = string.IsNullOrWhiteSpace(WatchLabel) ? Shorten(address) : WatchLabel.Trim();
        WatchAddresses.Add(new WatchAddress(chain, address, WatchLabel.Trim()));
        await _watchStore.SaveAsync(WatchAddresses);
        WatchAddress = string.Empty;
        WatchLabel = string.Empty;
        StatusMessage = $"Linked watch-only {chain} address";
        PushActivity("Connected", chain, "watch-only", label, "now");
        if (IsUnlocked) await RefreshLiveDataAsync();
    }

    [RelayCommand]
    private async Task RemoveWatchAddressAsync(WatchAddress? row)
    {
        if (row is null) return;
        WatchAddresses.Remove(row);
        await _watchStore.SaveAsync(WatchAddresses);
        var match = Accounts.FirstOrDefault(a => a.Address.Equals(row.Address, StringComparison.OrdinalIgnoreCase));
        if (match is not null) Accounts.Remove(match);
        RefreshHoldings();
        RecalcBalance();
        StatusMessage = "Watch address removed";
    }

    // True when the unlocked wallet is a TON-native mnemonic (Telegram Wallet / Tonkeeper) rather than
    // a BIP39 seed — it derives ONLY a TON address, not the multi-chain BIP39 set.
    private bool _isTonWallet;

    private void SetUnlocked(string mnemonic, string passphrase = "")
    {
        _unlockedMnemonic = mnemonic;
        // Push the BIP39 passphrase onto the shared deriver BEFORE any derivation, so accounts,
        // balance scans and signing all target the same (possibly hidden) wallet.
        _unlockedPassphrase = passphrase ?? "";
        _deriver.ActivePassphrase = _unlockedPassphrase;
        _isTonWallet = !_mnemonics.Validate(mnemonic).IsValid && TonMnemonic.IsTonMnemonic(mnemonic);
        IsUnlocked = true;
        RefreshWalletList(); // reflect which wallet is now active in the switcher
        // Exchange keys are encrypted with a key derived from the seed, so they can only be
        // read once the wallet is unlocked.
        _ = LoadExchangesAsync(mnemonic);
        DeriveAccounts(mnemonic);
        RestoreCachedBalances(); // show last-known totals instantly; the live refresh corrects them
        SelectFirstReceive();
        LoadActivity(); // restore the saved history before logging this unlock on top
        LoadAddressBook(); // saved Send destinations for this device
        PushActivity("Security", "Vault", "unlocked", "this device", "now");
        _onChainRows.Clear();
        HistorySynced = false; // this wallet's history hasn't been pulled yet → show "loading", not "empty"
        if (!_isTonWallet) _ = LoadOnChainHistoryAsync(); // real on-chain history across the user's addresses
    }

    private void SelectFirstReceive()
    {
        var first = Accounts.FirstOrDefault(a => a.SupportStatus == "Ready" && IsRealAddress(a.Address));
        if (first is not null) SetReceiveTarget(first);
    }

    private void DeriveAccounts(string mnemonic)
    {
        Accounts.Clear();

        // A TON-native wallet (imported from Telegram Wallet / Tonkeeper) derives only its TON address.
        if (_isTonWallet)
        {
            var (address, _) = TonMnemonic.DeriveWallet(mnemonic);
            Accounts.Add(new WalletAccountViewModel(
                "TON", "Toncoin", "Ready", address, "TON mnemonic · wallet v4R2",
                0, 0, "The Open Network", 0));
            ShortAddress = Shorten(address);
            WalletLabel = ActiveWalletLabel;
            RefreshHoldings();
            RecalcBalance();
            return;
        }

        foreach (var chain in ChainCatalog.All)
        {
            // Single-coin / selected-coin wallets: only derive the coins this wallet is set to accept.
            if (!IsWalletCoinEnabled(chain.Symbol)) continue;

            if (!ChainCatalog.HasRealAddress(chain.Id))
            {
                Accounts.Add(new WalletAccountViewModel(
                    chain.Symbol, chain.Name, "Planned",
                    "Adapter pending — no fake address",
                    chain.DerivationScheme ?? "Pending",
                    0, 0, chain.Name, 0));
                continue;
            }

            ReceiveAddress account;
            try
            {
                account = _deriver.DeriveReceiveAddress(mnemonic, chain.Id);
            }
            catch (PassphraseUnsupportedException)
            {
                // Hidden wallet (a passphrase is active) on a chain whose scheme can't honour it
                // (Cardano/Icarus): omit the account entirely rather than show the base wallet's address.
                continue;
            }
            // "Ready" means the wallet can both receive AND send. A chain with a real address but no
            // send path (Dogecoin) or no public balance sync (Monero) is shown as "Receive only" so it
            // never looks spendable — the send picker only offers "Ready" accounts (roadmap §5.1).
            var status = (chain.Support == ChainSupportLevel.ReceiveOnly || !chain.CanSend)
                ? "Receive only"
                : "Ready";
            Accounts.Add(new WalletAccountViewModel(
                chain.Symbol, chain.Name, status,
                account.Address, account.DerivationPath,
                0, 0, chain.Name, 0));
        }

        var primary = Accounts.FirstOrDefault(a => a.SupportStatus == "Ready");
        if (primary is not null)
        {
            ShortAddress = Shorten(primary.Address);
            WalletLabel = ActiveWalletLabel;
        }

        RefreshHoldings();
        RecalcBalance();
    }

    private void ResetAddresses()
    {
        Accounts.Clear();
        foreach (var chain in ChainCatalog.All)
        {
            Accounts.Add(MakeLockedAccount(chain));
        }

        ShortAddress = "—";
        RefreshHoldings();
    }

    private static WalletAccountViewModel MakeLockedAccount(ChainInfo chain) =>
        new(chain.Symbol, chain.Name,
            chain.Support switch
            {
                ChainSupportLevel.Supported => "Ready",
                ChainSupportLevel.ReceiveOnly => "Receive only",
                _ => "Planned",
            },
            "Unlock wallet to derive address",
            chain.DerivationScheme ?? "Desktop adapter pending",
            0, 0, chain.Name, 0);

    private void RefreshHoldings()
    {
        Holdings.Clear();
        var rows = Accounts.Where(a => a.SupportStatus is "Ready" or "Watch" or "Exchange" or "Receive only");
        if (!string.Equals(ChainFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(a =>
                a.Chain.Contains(ChainFilter, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(ChainFilter, StringComparison.OrdinalIgnoreCase) ||
                a.Symbol.Contains(ChainFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            rows = rows.Where(a =>
                a.Symbol.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                a.Address.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        var built = rows.Select(a => new HoldingRowViewModel(
            a.Symbol, a.Name, a.Chain, a.Price, a.Amount,
            a.Price * a.Amount, a.Change24h, a.Address, a.SupportStatus));
        foreach (var h in HoldingsSorter.Order(built, HoldingsSort))
            Holdings.Add(h);

        RebuildStaking(); // keep the staking list driven by what the user actually holds
    }

    private void RecalcBalance()
    {
        var total = Holdings.Sum(h => h.Value);                 // USD
        var displayTotal = total * (double)Fx.Rate;             // in the chosen currency
        var parts = displayTotal.ToString("N2", CultureInfo.InvariantCulture).Split('.');
        TotalBalanceMain = parts[0];
        TotalBalanceCents = parts.Length > 1 ? parts[1] : "00";
        double weighted = 0;
        double weight = 0;
        foreach (var h in Holdings)
        {
            if (h.Value <= 0) continue;
            weighted += h.Change24h * h.Value;
            weight += h.Value;
        }

        var avg = weight > 0 ? weighted / weight : 0;
        var delta = displayTotal * (avg / 100.0);
        Change24hLabel = Holdings.Count == 0
            ? "· unlock for live rates"
            : $"{(avg >= 0 ? "▲" : "▼")} {Math.Abs(avg):0.00}%   {(delta >= 0 ? "+" : "-")}{Fx.Symbol}{Math.Abs(delta):N2} · 24h";
        PortfolioChangePercent = weight <= 0
            ? "—"
            : $"{(avg >= 0 ? "▲" : "▼")} {Math.Abs(avg):0.00}%";
        PortfolioChangeColor = weight <= 0 ? "#8A9099" : avg >= 0 ? "#7DCF8F" : "#E08A8A";
        OnPropertyChanged(nameof(BalanceDisplayMain));
        OnPropertyChanged(nameof(BalanceDisplayCents));

        // Dashboard stat tiles. These read from the holdings/market data the wallet already has, so
        // they work even at a zero balance (24h moves exist regardless of what you hold).
        PortfolioAssetCount = Holdings.Count.ToString(CultureInfo.InvariantCulture);
        PortfolioNetworkCount = Holdings
            .Select(h => h.Chain)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString(CultureInfo.InvariantCulture);

        var best = Holdings
            .Where(h => h.Price > 0 && h.Change24h != 0)
            .OrderByDescending(h => h.Change24h)
            .FirstOrDefault();
        if (best is not null)
        {
            PortfolioBestSymbol = best.Symbol;
            PortfolioBestLabel = $"{(best.Change24h >= 0 ? "▲" : "▼")} {Math.Abs(best.Change24h):0.00}%";
            PortfolioBestColor = best.Change24h >= 0 ? "#7DCF8F" : "#E08A8A";
        }
        else
        {
            PortfolioBestSymbol = "—";
            PortfolioBestLabel = "—";
            PortfolioBestColor = "#8A9099";
        }

        RebuildBreakdown(total);
    }

    private static readonly string[] SliceColors =
        ["#E7CA83", "#7DCF8F", "#5AC8B4", "#8A5FD6", "#D14A55", "#E0863C", "#5A9BD6", "#B76EC8"];

    /// <summary>
    /// Rebuilds the "what is my money made of" breakdown: the top assets by USD value, each with its
    /// share of the total and a colour. Assets past the top few are folded into an "Other" slice.
    /// </summary>
    private void RebuildBreakdown(double total)
    {
        PortfolioBreakdown.Clear();
        if (total > 0)
        {
            var byAsset = Holdings
                .Where(h => h.Value > 0)
                .GroupBy(h => h.Symbol)
                .Select(g => (Symbol: g.Key, Value: g.Sum(h => h.Value)))
                .OrderByDescending(x => x.Value)
                .ToList();

            var top = byAsset.Take(7).ToList();
            var i = 0;
            foreach (var a in top)
            {
                PortfolioBreakdown.Add(new PortfolioSlice(
                    a.Symbol, a.Value / total * 100.0, a.Value,
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(SliceColors[i % SliceColors.Length]))));
                i++;
            }

            if (byAsset.Count > top.Count)
            {
                var restVal = byAsset.Skip(top.Count).Sum(x => x.Value);
                PortfolioBreakdown.Add(new PortfolioSlice(
                    "Other", restVal / total * 100.0, restVal,
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6A6A72"))));
            }
        }

        OnPropertyChanged(nameof(HasBreakdown));
        OnPropertyChanged(nameof(AllocationSummary));
    }

    private async Task LoadExchangesAsync(string mnemonic)
    {
        try
        {
            var stored = await _exchangeStore.LoadAsync(mnemonic);
            Exchanges.Clear();
            foreach (var credential in stored) Exchanges.Add(credential);
        }
        catch
        {
            // Never block unlocking over exchange credentials.
        }
    }

    private async Task LoadWatchAddressesAsync()
    {
        try
        {
            var rows = await _watchStore.LoadAsync();
            WatchAddresses.Clear();
            foreach (var row in rows) WatchAddresses.Add(row);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// Renders a branded, still-scannable receive QR (see <see cref="Controls.QrRenderer"/>): dark
    /// rounded modules on white, styled finder eyes, the Umbrella mark in the centre. Error-correction
    /// H keeps it readable under the centre mark. Falls back to a plain QR if the branded render ever
    /// throws, so the receive screen can never end up with no code at all.
    /// </summary>
    private static Bitmap? BuildQr(string payload)
    {
        // A very dark navy rather than pure black: reads as part of the app, still maximal contrast.
        var dark = Avalonia.Media.Color.FromRgb(0x0B, 0x0E, 0x17);
        try
        {
            return Controls.QrRenderer.Render(payload, dark, QrCenterMark);
        }
        catch
        {
            try
            {
                using var gen = new QRCodeGenerator();
                using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
                var png = new PngByteQRCode(data);
                using var ms = new System.IO.MemoryStream(png.GetGraphic(8));
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>The mark drawn in the middle of every receive QR — the umbrella glyph in dark, so it
    /// reads on the white centre pad. Loaded once and reused.</summary>
    private static Bitmap? QrCenterMark
    {
        get
        {
            try { return LoadAsset("umbrella-mark-black.png"); }
            catch { return null; }
        }
    }

    private async Task CopyTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { Clipboard: { } clipboard }
            })
        {
            await clipboard.SetTextAsync(text);
            var seconds = _uiSettings.ClipboardAutoClearSeconds;
            if (seconds > 0) ScheduleClipboardClear(clipboard, text, seconds);
        }
    }

    /// <summary>Wipes the clipboard after a delay, but only if it still holds exactly what we put
    /// there — so a later copy the user makes is never clobbered. Best-effort; never throws.</summary>
    private static void ScheduleClipboardClear(
        Avalonia.Input.Platform.IClipboard clipboard, string original, int seconds)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var current = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
                    if (current == original) await clipboard.ClearAsync();
                });
            }
            catch
            {
                // Clipboard may be unavailable or held by another app — clearing is best-effort.
            }
        });
    }

    private static bool IsRealAddress(string address) =>
        !string.IsNullOrWhiteSpace(address) &&
        !address.StartsWith("Unlock", StringComparison.Ordinal) &&
        !address.StartsWith("Adapter", StringComparison.Ordinal);

    /// <summary>
    /// Resolves whatever the user typed into a chain. Accepts tickers, token-standard names and
    /// full coin names — someone linking a wallet is as likely to type "Ethereum" or "ERC20" as
    /// "ETH", and an unrecognised string silently drops the address from the portfolio entirely.
    /// </summary>
    private static ChainId? ParseChain(string symbol) => symbol.Trim().ToUpperInvariant() switch
    {
        "BTC" or "BITCOIN" or "XBT" => ChainId.Btc,
        "ETH" or "ERC20" or "ERC-20" or "ETHEREUM" => ChainId.Eth,
        "LTC" or "LITECOIN" => ChainId.Ltc,
        "DOGE" or "DOGECOIN" => ChainId.Doge,
        "TRX" or "TRON" or "TRC20" or "TRC-20" => ChainId.Tron,
        "SOL" or "SOLANA" or "SPL" => ChainId.Sol,
        "TON" or "TONCOIN" => ChainId.Ton,
        "XMR" or "MONERO" => ChainId.Xmr,
        "ADA" or "CARDANO" => ChainId.Ada,
        "BCH" or "BITCOIN CASH" or "BITCOINCASH" => ChainId.Bch,
        "ZEC" or "ZCASH" => ChainId.Zec,
        _ => null,
    };

    /// <summary>The right UTXO explorer for a chain: BlockCypher for Dogecoin, Haskoin for Bitcoin Cash
    /// (neither has an Esplora instance; BCH's Blockchair also rate-limits), Esplora (Blockstream /
    /// litecoinspace) for BTC and LTC.</summary>
    private static Umbrella.Wallet.Core.Utxo.IUtxoExplorer UtxoExplorerFor(string symbol) =>
        symbol.Trim().ToUpperInvariant() switch
        {
            "DOGE" => BlockCypherUtxoExplorer.For(symbol),
            "BCH" => HaskoinUtxoExplorer.For(symbol),
            _ => EsploraUtxoExplorer.For(symbol),
        };

    /// <summary>USDT-TRC20 addresses live on TRON, so a TRC20/TRON watch address may hold USDT.</summary>
    private static bool IsTronLike(string chain) =>
        chain.ToUpperInvariant() is "TRX" or "TRON" or "TRC20";

    /// <summary>
    /// The canonical ticker for a chain. Watch addresses must be priced by THIS, not by whatever
    /// the user typed — "ERC20" or "Ethereum" resolve to the right chain but would miss the price
    /// table, leaving the row at $0 and silently dropping it out of the total balance.
    /// </summary>
    /// <summary>
    /// Live USDT price, falling back to $1.00 only if the feed has no quote. Tether is normally
    /// a cent either side of a dollar, so using the real quote keeps the total honest.
    /// </summary>
    private static decimal UsdtPrice(IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices) =>
        prices.TryGetValue("USDT", out var quote) && quote.Usd > 0 ? quote.Usd : 1.0m;

    /// <summary>
    /// The ticker a watch row must be priced under, for any chain text the user might type
    /// ("ETH", "ERC20", "Ethereum"). Null when the text names no chain we support.
    /// Exposed so the regression test can pin this mapping.
    /// </summary>
    public static string? CanonicalSymbolForChain(string chainText)
    {
        var chain = ParseChain(chainText);
        return chain is null ? null : SymbolFor(chain.Value);
    }

    /// <summary>Recomputes Holdings and the total. Test hook for the watch-balance regression.</summary>
    public void RecomputeHoldingsForTest()
    {
        RefreshHoldings();
        RecalcBalance();
    }

    private static string SymbolFor(ChainId chain) => chain switch
    {
        ChainId.Btc => "BTC",
        ChainId.Eth => "ETH",
        ChainId.Ltc => "LTC",
        ChainId.Doge => "DOGE",
        ChainId.Tron => "TRX",
        ChainId.Sol => "SOL",
        ChainId.Ton => "TON",
        ChainId.Xmr => "XMR",
        ChainId.Ada => "ADA",
        ChainId.Bch => "BCH",
        ChainId.Zec => "ZEC",
        _ => chain.ToString().ToUpperInvariant(),
    };

    private static string Shorten(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length < 12) return address;
        return $"{address[..6]}…{address[^4..]}";
    }

    /// <summary>Validates the password actually used to encrypt a new/imported wallet. When an additional
    /// wallet is silently reusing the one app password, that password is already valid, so we skip the
    /// on-screen confirm check; otherwise the normal two-field validation runs.</summary>
    private bool ValidateVaultPassword(string pw)
    {
        if (ReuseAppPassword)
        {
            if (string.IsNullOrEmpty(pw))
            {
                Fail("Your app password isn't available — unlock a wallet first, then add another.");
                return false;
            }
            return true;
        }

        return ValidatePasswords();
    }

    private bool ValidatePasswords()
    {
        if (Password.Length == 0)
        {
            Fail("Enter a vault password — this is what encrypts your seed on this PC.");
            return false;
        }

        if (Password.Length < MinPasswordLength)
        {
            Fail($"Password is {Password.Length} characters — {MinPasswordLength} is the minimum.");
            return false;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            Fail("The two passwords do not match.");
            return false;
        }

        return true;
    }

    /// <summary>Errors go next to the form, not only to the title bar where nobody sees them.</summary>
    private void Fail(string message)
    {
        FormError = message;
        StatusMessage = message;
        ShowToast(message, isError: true);
    }

    // --- Centered top toast: surfaces errors and key notices where the user actually looks ---
    [ObservableProperty] private string _toast = string.Empty;
    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private bool _toastIsError;
    private Avalonia.Threading.DispatcherTimer? _toastTimer;

    public void ShowToast(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Toast = message;
        ToastIsError = isError;
        ToastVisible = true;
        _toastTimer ??= new Avalonia.Threading.DispatcherTimer();
        _toastTimer.Stop();
        // Kept short so a toast is a quick "спливашка", not something that lingers.
        _toastTimer.Interval = TimeSpan.FromSeconds(isError ? 3.5 : 2.2);
        _toastTimer.Tick -= HideToastTick;
        _toastTimer.Tick += HideToastTick;
        _toastTimer.Start();
    }

    private void HideToastTick(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        ToastVisible = false;
    }

    [RelayCommand]
    private void DismissToast() => ToastVisible = false;

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception error)
        {
            Fail(error switch
            {
                UnauthorizedAccessException => "Incorrect password or damaged vault.",
                ArgumentException => error.Message,
                IOException io => $"Cannot write the vault to disk: {io.Message}",
                _ => $"Operation failed: {error.Message}",
            });
        }
        finally { IsBusy = false; }
    }

    private void ClearPasswordFields()
    {
        Password = string.Empty;
        UnlockPassphrase = string.Empty;
        ConfirmPassword = string.Empty;
        FormError = string.Empty;
    }
}

