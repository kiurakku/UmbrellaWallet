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
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _chainFilter = "All";
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
    [ObservableProperty] private string _marketStatus = "Loading market…";
    [ObservableProperty] private string _settingsPassword = string.Empty;
    [ObservableProperty] private string _deleteConfirmation = string.Empty;
    [ObservableProperty] private string _sendError = string.Empty;

    // Tor is bundled with the app and run as a child process — nothing to install.
    [ObservableProperty] private bool _torEnabled;
    [ObservableProperty] private string _torStatus = "Direct connection · traffic is NOT anonymised";
    [ObservableProperty] private string _torStatusColor = "#E7CA83";

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
            _uiSettings.Language = value;
            _uiSettings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeleteKeyword));
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
            if (IsUnlocked)
                PushActivity("Theme", "Appearance",
                    Theming.Themes.FirstOrDefault(t => t.Id == value)?.Name ?? value, "changed", "now");
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

    /// <summary>The ambient rain shows only when both the master motion toggle and the rain toggle are on.</summary>
    public bool RainVisible => AnimationsEnabled && RainEnabled;
    /// <summary>The aurora glow shows only when both the master motion toggle and the aurora toggle are on.</summary>
    public bool AuroraVisible => AnimationsEnabled && AuroraEnabled;

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

    public string AutoLockLabel => _uiSettings.AutoLockMinutes == 0
        ? "Auto-lock · off"
        : $"Auto-lock · {AutoLockChoice} idle";

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

    public Dock SidebarDock => SidebarPosition switch
    {
        "Right" => Dock.Right,
        "Top" => Dock.Top,
        "Bottom" => Dock.Bottom,
        _ => Dock.Left,
    };

    /// <summary>
    /// Left and right keep the tall panel; top and bottom switch to a compact horizontal bar,
    /// because a 248px-wide column laid on its side would eat most of the window height.
    /// </summary>
    public bool IsSidebarVertical => SidebarPosition is "Left" or "Right";

    public bool IsSidebarHorizontal => !IsSidebarVertical;

    /// <summary>
    /// The wordmark, swapped for the dark version on light themes — the solid-white logo is
    /// invisible on a white background.
    /// </summary>
    public Bitmap LogoImage => LoadAsset(
        Theming.IsLightTheme(Theming.Current)
            ? "umbrella-logo-black.png"
            : "umbrella-logo-solidwhite.png");

    /// <summary>The "the fear" maker's mark.</summary>
    public Bitmap FearMark => LoadAsset("thefear-logo.png");

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

    private string? _unlockedMnemonic;
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
    private readonly BalanceStore _balanceStore = new();
    private readonly ExchangeCredentialStore _exchangeStore = new();
    private readonly EthTransactionSender _ethSender = new();
    private readonly BitcoinTransactionSender _btcSender = new();
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

        SelectedSendAsset = SendableAssets[0];
        SelectedWatchNetwork = WatchableNetworks[0];
        BuildGuide();
        LoadProfileImages();

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

    /// <summary>What the total is made of — top assets by value, for the Portfolio-overview ring.</summary>
    public ObservableCollection<PortfolioSlice> PortfolioBreakdown { get; } = [];
    public bool HasBreakdown => PortfolioBreakdown.Count > 0;

    /// <summary>NFT collections held at the wallet's Ethereum address (names + counts, no images).</summary>
    public ObservableCollection<NftHolding> Nfts { get; } = [];
    public bool HasNfts => Nfts.Count > 0;

    /// <summary>Stakeable coins the wallet holds keys for, with the network's typical (approximate)
    /// reward and how staking is done. Informational — not live positions.</summary>
    public IReadOnlyList<StakingOption> StakingOptions { get; } =
    [
        new("ETH", "Ethereum", "~3–4%", "Beacon-chain staking (32 ETH solo) or a liquid-staking pool"),
        new("SOL", "Solana", "~6–7%", "Delegate to a validator — native, unbonds in a few days"),
        new("ADA", "Cardano", "~3%", "Delegate to a stake pool — native, no lock-up, keys stay yours"),
        new("TON", "Toncoin", "~3–4%", "Stake through a nominator pool"),
        new("TRX", "TRON", "~4–5%", "Freeze TRX for resources and vote for a Super Representative"),
        new("MATIC", "Polygon", "~4%", "Delegate to a validator on the Polygon staking contract"),
    ];

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
    /// Assets that can actually be sent, as a pick-list. Typing a ticker by hand is how people
    /// send on the wrong network, so the UI only offers what this build can really broadcast.
    /// </summary>
    public IReadOnlyList<SendOption> SendableAssets { get; } =
    [
        new("ETH", "Ethereum", "Ethereum network (ERC-20 compatible)"),
        new("BTC", "Bitcoin", "Bitcoin network · native SegWit"),
        new("LTC", "Litecoin", "Litecoin network · native SegWit"),
        new("SOL", "Solana", "Solana network"),
        new("XMR", "Monero", "Monero network · needs the Monero service on"),
        new("TRX", "TRON", "TRON network"),
        new("USDT", "Tether (TRC-20)", "TRON network · fee paid in TRX"),
        new("ADA", "Cardano", "Cardano network"),
        new("BNB", "BNB", "BNB Smart Chain (BEP-20 address)"),
        new("MATIC", "Polygon", "Polygon network"),
        new("AVAX", "Avalanche", "Avalanche C-Chain"),
        new("FTM", "Fantom", "Fantom Opera"),
        new("CRO", "Cronos", "Cronos EVM"),
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
    public bool IsReceive => ActiveSection == "Receive";
    public bool IsSend => ActiveSection == "Send";
    public bool IsSwap => ActiveSection == "Swap";
    public bool IsActivity => ActiveSection == "Activity";
    public bool IsTransactions => ActiveSection == "Transactions";
    public bool IsSettings => ActiveSection == "Settings";
    public bool IsConnect => ActiveSection == "Connect";
    public bool IsMarket => ActiveSection == "Market";
    public bool IsNews => ActiveSection == "News";
    public bool IsNfts => ActiveSection == "Nfts";
    public bool IsStaking => ActiveSection == "Staking";
    public bool IsP2p => ActiveSection == "P2p";
    public bool IsBuy => ActiveSection == "Buy";

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
    }

    partial void OnHasVaultChanged(bool value) => NotifySectionFlags();
    partial void OnIsUnlockedChanged(bool value) => NotifySectionFlags();
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
        OnPropertyChanged(nameof(IsConnect));
        OnPropertyChanged(nameof(IsMarket));
        OnPropertyChanged(nameof(IsNews));
        OnPropertyChanged(nameof(IsNfts));
        OnPropertyChanged(nameof(IsStaking));
        OnPropertyChanged(nameof(IsP2p));
        OnPropertyChanged(nameof(IsBuy));
        OnPropertyChanged(nameof(IsWelcomeStage));
        OnPropertyChanged(nameof(IsCreateStage));
        OnPropertyChanged(nameof(IsImportStage));
        OnPropertyChanged(nameof(IsUnlockStage));
        OnPropertyChanged(nameof(IsBackupStage));
        OnPropertyChanged(nameof(IsWorkspace));
        OnPropertyChanged(nameof(ShowSidebar));
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
            ClearPasswordFields();
            SetUnlocked(mnemonic);
            ActiveSection = "Portfolio";
            StatusMessage = "Vault unlocked · loading chain balances";
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
            PublicHttp.SetProxy(null);
            TorStatus = "Direct connection · traffic is NOT anonymised";
            TorStatusColor = "#E7CA83";
            if (IsUnlocked) PushActivity("Security", "Tor", "off", "direct connection", "now");
            _ = RefreshMarketAsync();
            return;
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
        _activityStore.Clear();
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
            Wallets.Add(new WalletListItemViewModel(w.Id, w.Label, w.Id == activeId, w.IsLegacy));
        }
        OnPropertyChanged(nameof(ActiveWalletLabel));
        OnPropertyChanged(nameof(HasMultipleWallets));
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
            _ = LoadSparklinesAsync();
        }
        catch (Exception ex)
        {
            MarketStatus = $"Market feed failed: {ex.Message}";
        }
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

    // --- Detail chart geometry (exchange-style: gridded plot with labelled axes) ----
    // The grid is always five levels and five ticks, so their pixel positions are constants and
    // the view can place them directly. An ItemsControl over a Canvas does not position its
    // generated containers, which silently dropped the whole grid.
    private const double PlotLeft = 58;    // room for price labels
    private const double PlotRight = 860;
    private const double PlotTop = 10;
    private const double PlotBottom = 130; // room for time labels below

    /// <summary>Closed polygon under the price line, so the chart reads as an area not a wire.</summary>
    [ObservableProperty] private List<Avalonia.Point> _chartArea = [];

    /// <summary>Candlesticks (real OHLC) drawn over the area — green up, red down, like a pro chart.</summary>
    [ObservableProperty] private List<CandleVm> _chartCandles = [];

    // Price levels, top to bottom.
    [ObservableProperty] private string _chartLevel0 = string.Empty;
    [ObservableProperty] private string _chartLevel1 = string.Empty;
    [ObservableProperty] private string _chartLevel2 = string.Empty;
    [ObservableProperty] private string _chartLevel3 = string.Empty;
    [ObservableProperty] private string _chartLevel4 = string.Empty;

    // Time ticks, oldest to newest.
    [ObservableProperty] private string _chartTime0 = string.Empty;
    [ObservableProperty] private string _chartTime1 = string.Empty;
    [ObservableProperty] private string _chartTime2 = string.Empty;
    [ObservableProperty] private string _chartTime3 = string.Empty;
    [ObservableProperty] private string _chartTime4 = string.Empty;

    [ObservableProperty] private string _chartHigh = string.Empty;
    [ObservableProperty] private string _chartLow = string.Empty;

    // --- Chart view mode (Candles ⇄ Line), like a pro charting UI ---------------
    [ObservableProperty] private string _chartViewMode = "Candles";
    public bool IsCandleView => ChartViewMode == "Candles";
    public bool IsLineView => ChartViewMode == "Line";
    partial void OnChartViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCandleView));
        OnPropertyChanged(nameof(IsLineView));
    }

    [RelayCommand]
    private void SetChartView(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode)) ChartViewMode = mode;
    }

    // --- Change over the selected window (first→last close), shown in the header --
    [ObservableProperty] private string _chartChangeLabel = string.Empty;
    [ObservableProperty] private string _chartChangeColor = "#8A9099";

    /// <summary>Soft vertical gradient under the price line — the change colour fading to nothing,
    /// like Kraken/TradingView. Rebuilt each time a chart is drawn so it tracks the up/down colour.</summary>
    [ObservableProperty] private Avalonia.Media.IBrush _chartAreaBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Transparent);

    // --- Crosshair (hover) state, driven from the view code-behind ----------------
    [ObservableProperty] private bool _crosshairVisible;
    [ObservableProperty] private double _crosshairLineLeft;
    [ObservableProperty] private double _crosshairDotLeft;
    [ObservableProperty] private double _crosshairDotTop;
    [ObservableProperty] private double _crosshairLabelLeft;
    [ObservableProperty] private string _crosshairPrice = string.Empty;
    [ObservableProperty] private string _crosshairTime = string.Empty;

    // Raw candles + scale of the open chart, so the crosshair can map pixels back to price/time.
    private IReadOnlyList<PriceCandle> _detailCandles = [];
    private double _chartMin;
    private double _chartMax;

    /// <summary>
    /// Turns a price series into a plotted chart: gridlines with price labels, time labels along
    /// the bottom, a stroked line and the filled area beneath it.
    /// </summary>
    private void BuildDetailChart(IReadOnlyList<PriceCandle> candles)
    {
        ChartArea = [];
        ChartPoints = [];
        ChartCandles = [];
        ChartHigh = ChartLow = string.Empty;
        CrosshairVisible = false;
        _detailCandles = candles;
        if (candles.Count < 2) return;

        var min = candles.Min(c => c.Low);
        var max = candles.Max(c => c.High);
        var range = max - min;
        // A dead-flat series would divide by zero; give it a nominal band so it renders centred.
        if (range <= 0) { min -= 1; max += 1; range = max - min; }
        _chartMin = min;
        _chartMax = max;

        var plotW = PlotRight - PlotLeft;
        var plotH = PlotBottom - PlotTop;
        double Y(double price) => PlotTop + (1 - (price - min) / range) * plotH;

        // Faint close-line + area behind the candles.
        var line = new List<Avalonia.Point>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var x = PlotLeft + plotW * i / (candles.Count - 1);
            line.Add(new Avalonia.Point(x, Y(candles[i].Close)));
        }

        ChartPoints = line;
        ChartArea = new List<Avalonia.Point>(line) { new(PlotRight, PlotBottom), new(PlotLeft, PlotBottom) };

        // Candlesticks: green when close ≥ open, red otherwise (TradingView colours).
        var w = Math.Max(1.5, plotW / candles.Count * 0.62);
        var built = new List<CandleVm>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            var xc = PlotLeft + plotW * (i + 0.5) / candles.Count;
            var yHigh = Y(c.High);
            var yLow = Y(c.Low);
            var bodyTop = Math.Min(Y(c.Open), Y(c.Close));
            built.Add(new CandleVm(
                xc - (w / 2), yHigh, w, Math.Max(1, yLow - yHigh),
                (w / 2) - 0.7, bodyTop - yHigh, Math.Max(1, Math.Abs(Y(c.Close) - Y(c.Open))),
                c.Close >= c.Open ? "#26A69A" : "#EF5350"));
        }

        ChartCandles = built;

        // Five price levels, top to bottom.
        ChartLevel0 = FormatPrice(max);
        ChartLevel1 = FormatPrice(max - range * 0.25);
        ChartLevel2 = FormatPrice(max - range * 0.5);
        ChartLevel3 = FormatPrice(max - range * 0.75);
        ChartLevel4 = FormatPrice(min);

        // Time axis derived from the selected window — the series is evenly spaced within it.
        var ticks = TimeAxisLabels(ChartRange);
        ChartTime0 = ticks[0];
        ChartTime1 = ticks[1];
        ChartTime2 = ticks[2];
        ChartTime3 = ticks[3];
        ChartTime4 = ticks[4];

        ChartHigh = $"H {FormatPrice(max)}";
        ChartLow = $"L {FormatPrice(min)}";

        // Change across the whole window (first open → last close), like the header on an exchange.
        var open0 = candles[0].Open != 0 ? candles[0].Open : candles[0].Close;
        var closeN = candles[^1].Close;
        var pct = open0 != 0 ? (closeN - open0) / open0 * 100 : 0;
        var up = pct >= 0;
        ChartChangeColor = up ? "#26A69A" : "#EF5350";
        ChartChangeLabel = $"{(up ? "▲" : "▼")} {Math.Abs(pct):0.00}% · {ChartRange}";

        // Gradient fill under the line: change-colour → transparent, top to bottom.
        var baseColor = Avalonia.Media.Color.Parse(up ? "#26A69A" : "#EF5350");
        ChartAreaBrush = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(0x66, baseColor.R, baseColor.G, baseColor.B), 0),
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(0x1F, baseColor.R, baseColor.G, baseColor.B), 0.55),
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(0x00, baseColor.R, baseColor.G, baseColor.B), 1),
            },
        };
    }

    /// <summary>Called from the view as the pointer moves over the chart: snaps to the nearest candle
    /// and updates the crosshair line, dot and floating price/time readout.</summary>
    public void UpdateCrosshair(double canvasX)
    {
        var n = _detailCandles.Count;
        if (n < 2) { CrosshairVisible = false; return; }

        var plotW = PlotRight - PlotLeft;
        var plotH = PlotBottom - PlotTop;
        var range = _chartMax - _chartMin;
        if (range <= 0) { CrosshairVisible = false; return; }

        var frac = Math.Clamp((canvasX - PlotLeft) / plotW, 0, 1);
        var i = Math.Clamp((int)Math.Round(frac * (n - 1)), 0, n - 1);
        var c = _detailCandles[i];

        var x = PlotLeft + plotW * i / (double)(n - 1);
        var y = PlotTop + (1 - (c.Close - _chartMin) / range) * plotH;

        CrosshairLineLeft = x;
        CrosshairDotLeft = x - 4;
        CrosshairDotTop = y - 4;
        CrosshairLabelLeft = Math.Clamp(x - 62, PlotLeft, PlotRight - 124);
        CrosshairPrice = FormatPrice(c.Close);
        CrosshairTime = CrosshairTimeLabel(i, n);
        CrosshairVisible = true;
    }

    public void HideCrosshair() => CrosshairVisible = false;

    /// <summary>Approximate wall-clock label for a hovered candle: the window is evenly spaced, so
    /// candle i of n maps to now − span·(1 − i/(n−1)).</summary>
    private string CrosshairTimeLabel(int i, int n)
    {
        var frac = (double)i / (n - 1);
        var span = ChartRange switch
        {
            "1H" => TimeSpan.FromHours(1),
            "24H" => TimeSpan.FromHours(24),
            "7D" => TimeSpan.FromDays(7),
            "30D" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(365),
        };
        var t = DateTime.Now - TimeSpan.FromTicks((long)(span.Ticks * (1 - frac)));
        return ChartRange switch
        {
            "1H" or "24H" => t.ToString("HH:mm", CultureInfo.InvariantCulture),
            "7D" or "30D" => t.ToString("MMM d · HH:mm", CultureInfo.InvariantCulture),
            _ => t.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
        };
    }

    private static string FormatPrice(double value) => value switch
    {
        >= 1000 => value.ToString("N0", CultureInfo.InvariantCulture),
        >= 1 => value.ToString("N2", CultureInfo.InvariantCulture),
        _ => value.ToString("N6", CultureInfo.InvariantCulture),
    };

    /// <summary>Evenly spaced ticks labelled for the selected window, oldest on the left.</summary>
    private static string[] TimeAxisLabels(string range) => range switch
    {
        "1H" => ["-60m", "-45m", "-30m", "-15m", "now"],
        "24H" => ["-24h", "-18h", "-12h", "-6h", "now"],
        "7D" => ["-7d", "-5d", "-3d", "-2d", "now"],
        "30D" => ["-30d", "-22d", "-15d", "-7d", "now"],
        _ => ["-1y", "-9m", "-6m", "-3m", "now"],
    };

    /// <summary>Free-text Market filter — matches ticker or name. Drives per-row visibility via
    /// <see cref="MarketFilterConverter"/>, so live price updates (indexed by symbol) are untouched.</summary>
    [ObservableProperty] private string _marketQuery = string.Empty;

    [RelayCommand]
    private void ClearMarketQuery() => MarketQuery = string.Empty;

    /// <summary>Selected chart window. Changing it reloads every chart at the new resolution.</summary>
    [ObservableProperty] private string _chartRange = "24H";

    public IReadOnlyList<string> ChartRanges => PublicMarketRatesClient.ChartRanges;

    [RelayCommand]
    private async Task SelectChartRangeAsync(string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == ChartRange) return;
        ChartRange = range;
        OnPropertyChanged(nameof(IsRange1H));
        OnPropertyChanged(nameof(IsRange24H));
        OnPropertyChanged(nameof(IsRange7D));
        OnPropertyChanged(nameof(IsRange30D));
        OnPropertyChanged(nameof(IsRange1Y));

        StatusMessage = $"Loading {range} charts…";
        await LoadSparklinesAsync(force: true);
        if (HasChart)
        {
            var open = Market.FirstOrDefault(m => m.Symbol == SelectedMarketSymbol);
            if (open is not null) await SelectMarketCoinAsync(open);
        }
        StatusMessage = $"Charts showing the last {range}";
    }

    public bool IsRange1H => ChartRange == "1H";
    public bool IsRange24H => ChartRange == "24H";
    public bool IsRange7D => ChartRange == "7D";
    public bool IsRange30D => ChartRange == "30D";
    public bool IsRange1Y => ChartRange == "1Y";

    [RelayCommand]
    private void CloseChart()
    {
        HasChart = false;
        CrosshairVisible = false;
        _detailCandles = [];
        ChartPoints = new System.Collections.Generic.List<Avalonia.Point>();
    }

    private const double ChartWidth = 620;
    private const double ChartHeight = 150;

    /// <summary>Scales a price series into polyline points inside the chart box (top-left origin).</summary>
    private static System.Collections.Generic.List<Avalonia.Point> BuildChartPoints(
        System.Collections.Generic.IReadOnlyList<double> series, double width, double height)
    {
        var points = new System.Collections.Generic.List<Avalonia.Point>();
        if (series.Count < 2) return points;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in series)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var range = max - min;
        const double pad = 10;
        var usableH = height - 2 * pad;
        for (var i = 0; i < series.Count; i++)
        {
            var x = width * i / (series.Count - 1);
            // Flat series → draw a centred line rather than dividing by zero.
            var norm = range > 0 ? (series[i] - min) / range : 0.5;
            var y = pad + (1 - norm) * usableH;
            points.Add(new Avalonia.Point(x, y));
        }

        return points;
    }

    [RelayCommand]
    private void ToggleBalanceHidden() => IsBalanceHidden = !IsBalanceHidden;

    [RelayCommand]
    private void SetChainFilter(string filter)
    {
        ChainFilter = filter;
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

            // Fetch every account's balance CONCURRENTLY, then apply on the UI thread. Sequential
            // awaits here were the main reason the total took many seconds to appear after unlock /
            // wallet switch; firing them together cuts that to roughly the slowest single call.
            // (Receive-only chains TON/ADA have public balance APIs; XMR returns null safely.)
            var balanceTargets = Accounts.ToList()
                .Where(a => a.SupportStatus is "Ready" or "Receive only" && ParseChain(a.Symbol) is not null)
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
            PushActivity("Sync", "All", "OK", "Public RPC / explorers", "now");
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
    }

    [RelayCommand]
    private async Task CopyAddressAsync(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !IsRealAddress(address)) return;
        await CopyTextAsync(address);
        StatusMessage = "Address copied";
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

    /// <summary>Set by the view so the view-model can raise a file dialog without knowing about windows.</summary>
    public Func<string, bool, Task<string?>>? PickFileAsync { get; set; }

    /// <summary>Raised by the view to pick an image file (avatar/banner/background) to open.</summary>
    public Func<Task<string?>>? PickImageAsync { get; set; }

    // --- Profile customisation: the user's own avatar, banner and sidebar background. Each is a
    // file they pick; we copy it into the data folder so it survives and never has to be bundled. ---
    [ObservableProperty] private Bitmap? _avatarImage;
    [ObservableProperty] private Bitmap? _bannerImage;
    [ObservableProperty] private Bitmap? _sidebarBgImage;

    public bool HasAvatar => AvatarImage is not null;
    public bool HasBanner => BannerImage is not null;
    public bool HasSidebarBg => SidebarBgImage is not null;

    partial void OnAvatarImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasAvatar));
    partial void OnBannerImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasBanner));
    partial void OnSidebarBgImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasSidebarBg));

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

    [RelayCommand]
    private void ClearProfileImages()
    {
        _uiSettings.AvatarPath = _uiSettings.BannerPath = _uiSettings.SidebarBackgroundPath = "";
        _uiSettings.Save();
        LoadProfileImages();
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        BackupStatus = BackupError = string.Empty;
        if (PickFileAsync is null) return;

        var path = await PickFileAsync(VaultBackup.SuggestedFileName(), true);
        if (string.IsNullOrWhiteSpace(path)) return;

        var (ok, message) = await VaultBackup.ExportAsync(path);
        if (ok) BackupStatus = message; else BackupError = message;
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        BackupStatus = BackupError = string.Empty;
        if (PickFileAsync is null) return;

        var path = await PickFileAsync(string.Empty, false);
        if (string.IsNullOrWhiteSpace(path)) return;

        var (ok, message) = await VaultBackup.RestoreAsync(path);
        if (!ok) { BackupError = message; return; }

        // The vault on disk changed underneath us, so drop the unlocked session rather than
        // leaving the UI showing the previous wallet's accounts.
        LockVault();
        BackupStatus = message;
    }

    /// <summary>Points the QR/address at an account without showing the popup.</summary>
    private bool SetReceiveTarget(WalletAccountViewModel? account)
    {
        if (account is null || !IsRealAddress(account.Address)) return false;
        SelectedReceiveAddress = account.Address;
        SelectedReceiveSymbol = account.Symbol;
        SelectedReceiveNetwork = $"{account.Symbol} · {account.NetworkLabel}";
        ReceiveQr = BuildQr(account.Address);
        return true;
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

    /// <summary>
    /// Step 1 of the send flow: validate, fetch live nonce/gas/balance, and show a quote.
    /// Nothing is signed here. ETH only — other chains refuse honestly.
    /// </summary>
    [RelayCommand]
    private async Task PrepareSendAsync()
    {
        SendError = string.Empty;
        SendSuccess = string.Empty;
        HasSendQuote = false;
        _sendQuote = null;
        _tonQuote = null;

        if (!IsUnlocked || _unlockedMnemonic is null)
        {
            SendError = "Unlock the vault first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SendTo) || string.IsNullOrWhiteSpace(SendAmount))
        {
            SendError = "Enter a destination address and an amount.";
            return;
        }

        if (!decimal.TryParse(SendAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            SendError = "Amount must be a positive number.";
            return;
        }

        var chain = SendChain.Trim().ToUpperInvariant();
        if (chain == "ETHEREUM") chain = "ETH";
        if (chain == "BITCOIN") chain = "BTC";
        if (chain == "LITECOIN") chain = "LTC";
        if (chain == "SOLANA") chain = "SOL";

        if (chain == "MONERO") chain = "XMR";

        if (chain == "XMR")
        {
            if (!_monero.IsRunning)
            {
                SendError = "Turn on the Monero wallet service in Settings → Privacy first.";
                return;
            }

            if (!MoneroKeys.TryDecodeAddress(SendTo.Trim(), out _, out _, out _))
            {
                SendError = "That is not a valid Monero address (checksum failed).";
                return;
            }

            _sendSymbol = "XMR";
            _moneroAmount = amount;
            _moneroTo = SendTo.Trim();

            // Developer fee as a second destination. Only kept if its address is a valid Monero
            // address — otherwise the whole transfer would fail, so the user's send comes first.
            _moneroFeeTo = null;
            _moneroFeeAmount = 0m;
            var xmrFee = _devFee.QuoteFee("XMR", amount);
            if (xmrFee is { } f && MoneroKeys.TryDecodeAddress(f.Address, out _, out _, out _))
            {
                _moneroFeeTo = f.Address;
                _moneroFeeAmount = f.Amount;
            }

            HasSendQuote = true;
            SendQuoteSummary = $"Send {Fmt(amount)} XMR  →  {Shorten(_moneroTo)}";
            SendQuoteFee = _moneroFeeTo is not null
                ? $"Network fee is set by Monero at broadcast · service fee {_devFee.FeePercent:0.##}% ≈ " +
                  $"{Fmt(_moneroFeeAmount)} XMR to the developer (same transaction)."
                : "Fee is set by the Monero network at broadcast (priority: normal).";
            StatusMessage = "Review the transfer, then confirm to broadcast";
            return;
        }

        if (chain is "TRX" or "TRON" or "USDT" or "TRC20")
        {
            var symbol = chain is "USDT" or "TRC20" ? "USDT" : "TRX";
            var tronAccount = Accounts.FirstOrDefault(a => a.Symbol == "TRX" && a.SupportStatus == "Ready");
            if (tronAccount is null || !IsRealAddress(tronAccount.Address))
            {
                SendError = "No TRON account is available.";
                return;
            }

            _sendSymbol = symbol;
            await RunBusyAsync(async () =>
            {
                StatusMessage = "Building the TRON transaction…";
                var (quote, error) = await _tronSender.PrepareAsync(
                    symbol, tronAccount.Address, SendTo.Trim(), amount);
                if (quote is null) { SendError = error ?? "Could not prepare the transaction."; return; }

                _tronQuote = quote;
                HasSendQuote = true;
                SendQuoteSummary = $"Send {Fmt(amount)} {symbol}  →  {quote.To}";
                SendQuoteFee = symbol == "USDT"
                    ? "USDT moves on the TRON network — the fee is paid in TRX (energy/bandwidth). Keep a little TRX on this address."
                    : "Fee is paid in TRX bandwidth.";
                StatusMessage = "Review the transfer, then confirm to broadcast";
            });
            return;
        }

        if (chain is not ("ETH" or "BTC" or "LTC" or "SOL" or "TON"))
        {
            SendError =
                $"Sending {chain} is not available. This build broadcasts ETH, BTC, LTC, SOL, TON, XMR, TRX and USDT (TRC-20).";
            return;
        }

        var from = Accounts.FirstOrDefault(a => a.Symbol == chain && a.SupportStatus == "Ready");
        if (from is null || !IsRealAddress(from.Address))
        {
            SendError = $"No {chain} account is available.";
            return;
        }

        _sendSymbol = chain;
        await RunBusyAsync(async () =>
        {
            StatusMessage = "Fetching balance and network fees…";
            switch (chain)
            {
                case "ETH":
                case "BNB":
                case "MATIC":
                case "AVAX":
                case "FTM":
                case "CRO":
                {
                    var evm = EthTransactionSender.Chains[chain];
                    var (quote, error) = await _ethSender.PrepareAsync(from.Address, SendTo.Trim(), amount, evm);
                    if (quote is null) { SendError = error ?? "Could not prepare the transaction."; return; }
                    _sendQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.AmountEth)} {quote.Symbol}  →  {quote.To}";
                    SendQuoteFee =
                        $"Network fee ≈ {Fmt(quote.MaxFeeEth)} {quote.Symbol} · {evm.Name} · nonce {quote.Nonce} · via {new Uri(quote.Rpc).Host}";
                    break;
                }

                case "BTC":
                case "LTC":
                {
                    var devFee = _devFee.QuoteFee(chain, amount);
                    var (quote, error) = await _btcSender.PrepareAsync(
                        chain, from.Address, SendTo.Trim(), amount, devFee?.Address, devFee?.Amount ?? 0m);
                    if (quote is null) { SendError = error ?? "Could not prepare the transaction."; return; }
                    _btcQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.Amount)} {chain}  →  {quote.To}";
                    // Disclosure is driven off the quote (the source of truth for what is actually sent).
                    SendQuoteFee = quote.DevFeeSat > 0
                        ? $"Network fee ≈ {Fmt(quote.FeeAmount)} {chain} · service fee {_devFee.FeePercent:0.##}% ≈ " +
                          $"{Fmt(quote.DevFeeSat / 100_000_000m)} {chain} to the developer · change returns to you"
                        : $"Network fee ≈ {Fmt(quote.FeeAmount)} {chain} · {quote.InputCount} input(s) · change returns to you";
                    break;
                }

                case "SOL":
                {
                    var devFee = _devFee.QuoteFee("SOL", amount);
                    var (quote, error) = await _solSender.PrepareAsync(
                        from.Address, SendTo.Trim(), amount, devFee?.Address, devFee?.Amount ?? 0m);
                    if (quote is null) { SendError = error ?? "Could not prepare the transaction."; return; }
                    _solQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.AmountSol)} SOL  →  {quote.To}";
                    SendQuoteFee = quote.DevFeeLamports > 0
                        ? $"Network fee ≈ {Fmt(quote.FeeSol)} SOL · service fee {_devFee.FeePercent:0.##}% ≈ " +
                          $"{Fmt(quote.DevFeeLamports / 1_000_000_000m)} SOL to the developer (same transaction)"
                        : $"Network fee ≈ {Fmt(quote.FeeSol)} SOL";
                    break;
                }

                case "TON":
                {
                    var (quote, error) = await _tonSender.PrepareAsync(from.Address, SendTo.Trim(), amount);
                    if (quote is null) { SendError = error ?? "Could not prepare the transaction."; return; }
                    _tonQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.AmountTon)} TON  →  {quote.To}";
                    SendQuoteFee = quote.Deploy
                        ? $"Network fee ≈ {Fmt(quote.FeeTon)} TON · first send also deploys your wallet (seqno 0)"
                        : $"Network fee ≈ {Fmt(quote.FeeTon)} TON · seqno {quote.Seqno}";
                    break;
                }

                case "ADA":
                {
                    var (quote, error) = await _adaSender.PrepareAsync(from.Address, SendTo.Trim(), amount);
                    if (quote is null) { SendError = error ?? "Could not prepare the transaction."; return; }
                    _adaQuote = quote;
                    SendQuoteSummary = $"Send {Fmt(quote.Amount)} ADA  →  {quote.To}";
                    SendQuoteFee = $"Network fee ≈ {Fmt(quote.Fee / 1_000_000m)} ADA · {quote.Inputs.Count} input(s) · change returns to you";
                    break;
                }
            }

            HasSendQuote = true;
            StatusMessage = "Review the transfer, then confirm to broadcast";
        });
    }

    private static string Fmt(decimal value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Step 2: the user explicitly confirms — derive the key, sign locally, broadcast, zero the key.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmSendAsync()
    {
        var haveQuote = _sendQuote is not null || _btcQuote is not null || _solQuote is not null
                        || _tonQuote is not null || _tronQuote is not null || _adaQuote is not null
                        || (_sendSymbol == "XMR" && _moneroAmount > 0);
        if (_unlockedMnemonic is null || !haveQuote)
        {
            SendError = "Prepare the transfer first.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            StatusMessage = "Signing locally and broadcasting…";
            switch (_sendSymbol)
            {
                case "ETH" or "BNB" or "MATIC" or "AVAX" or "FTM" or "CRO" when _sendQuote is not null:
                {
                    var quote = _sendQuote;
                    // Every EVM chain shares the same Ethereum key and 0x address.
                    var priv = _deriver.DeriveEthereumPrivateKey(_unlockedMnemonic!);
                    try
                    {
                        var result = await _ethSender.SignAndBroadcastAsync(quote, priv);
                        var explorer = EthTransactionSender.Chains.TryGetValue(quote.Symbol, out var c)
                            ? c.ExplorerTx + result.TxHash
                            : $"etherscan.io/tx/{result.TxHash}";
                        await FinishSendAsync(result.Ok, result.TxHash, result.Error,
                            quote.Symbol, quote.AmountEth, quote.To, explorer);
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                    }

                    break;
                }

                case "BTC" or "LTC" when _btcQuote is not null:
                {
                    var quote = _btcQuote;
                    var chainId = _sendSymbol == "BTC" ? ChainId.Btc : ChainId.Ltc;
                    var key = _deriver.DeriveBitcoinLikeKey(_unlockedMnemonic!, chainId);
                    var (ok, txid, error) = await _btcSender.SignAndBroadcastAsync(quote, key);
                    var explorer = _sendSymbol == "BTC"
                        ? $"blockstream.info/tx/{txid}"
                        : $"litecoinspace.org/tx/{txid}";
                    await FinishSendAsync(ok, txid, error, quote.Symbol, quote.Amount, quote.To, explorer);
                    break;
                }

                case "SOL" when _solQuote is not null:
                {
                    var quote = _solQuote;
                    var priv = _deriver.DeriveSolanaPrivateKey(_unlockedMnemonic!);
                    try
                    {
                        var (ok, signature, error) = await _solSender.SignAndBroadcastAsync(quote, priv);
                        await FinishSendAsync(ok, signature, error,
                            "SOL", quote.AmountSol, quote.To, $"solscan.io/tx/{signature}");
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                    }

                    break;
                }

                case "TRX" or "USDT" when _tronQuote is not null:
                {
                    var quote = _tronQuote;
                    var key = _deriver.DeriveTronKey(_unlockedMnemonic!);
                    var (ok, txId, error) = await _tronSender.SignAndBroadcastAsync(quote, key);
                    await FinishSendAsync(ok, txId, error, quote.Symbol, quote.Amount, quote.To,
                        txId is null ? "" : $"tronscan.org/#/transaction/{txId}");
                    break;
                }

                case "TON" when _tonQuote is not null:
                {
                    var quote = _tonQuote;
                    // A TON-native wallet signs with the TON-mnemonic seed; a BIP39 wallet uses its
                    // m/44'/607'/0' key. Both are the 32-byte ed25519 seed the sender expects.
                    var priv = _isTonWallet
                        ? TonMnemonic.ToSeed(_unlockedMnemonic!)
                        : _deriver.DeriveTonPrivateKey(_unlockedMnemonic!);
                    try
                    {
                        var (ok, _, error) = await _tonSender.SignAndBroadcastAsync(quote, priv);
                        await FinishSendAsync(ok, ok ? quote.To : null, error,
                            "TON", quote.AmountTon, quote.To, $"tonviewer.com/{quote.From}");
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                    }

                    break;
                }

                case "ADA" when _adaQuote is not null:
                {
                    var quote = _adaQuote;
                    var extendedKey = AdaKeys.PaymentKey(_unlockedMnemonic!);
                    try
                    {
                        var (ok, txId, error) = await _adaSender.SignAndBroadcastAsync(quote, extendedKey);
                        await FinishSendAsync(ok, txId, error, "ADA", quote.Amount, quote.To,
                            txId is null ? "" : $"cardanoscan.io/transaction/{txId}");
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(extendedKey);
                    }

                    break;
                }

                case "XMR":
                {
                    // monero-wallet-rpc builds, signs and relays the RingCT transaction itself.
                    // The developer fee (if any) rides along as a second destination — disclosed above.
                    var result = await _monero.SendAsync(_moneroTo, _moneroAmount, _moneroFeeTo, _moneroFeeAmount);
                    await FinishSendAsync(result.Ok, result.TxHash, result.Error,
                        "XMR", _moneroAmount, _moneroTo,
                        result.TxHash is null ? "" : $"xmrchain.net/tx/{result.TxHash}");
                    if (result.Ok)
                    {
                        _moneroAmount = 0;
                        _moneroTo = string.Empty;
                        _moneroFeeTo = null;
                        _moneroFeeAmount = 0m;
                        await RefreshMoneroAsync();
                    }

                    break;
                }

                default:
                    SendError = "Prepare the transfer first.";
                    break;
            }
        });
    }

    private async Task FinishSendAsync(
        bool ok, string? reference, string? error, string symbol, decimal amount, string to, string explorer)
    {
        if (ok && reference is not null)
        {
            ClearSendQuotes();
            SendTo = string.Empty;
            SendAmount = string.Empty;
            SendSuccess = $"Broadcast ✓  {reference}\nTrack it: {explorer}";
            StatusMessage = "Transaction broadcast · it will confirm shortly";
            var link = string.IsNullOrWhiteSpace(explorer) ? null
                : explorer.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? explorer : $"https://{explorer}";
            PushActivity("Sent", symbol, $"-{Fmt(amount)}", Shorten(to), "now", link);
            await RefreshLiveDataAsync();
        }
        else
        {
            SendError = error ?? "Broadcast failed.";
            StatusMessage = "Broadcast failed — nothing was sent";
        }
    }

    private void ClearSendQuotes()
    {
        HasSendQuote = false;
        _sendQuote = null;
        _btcQuote = null;
        _solQuote = null;
        _tronQuote = null;
        _tonQuote = null;
        _adaQuote = null;
    }

    [RelayCommand]
    private void CancelSendQuote()
    {
        ClearSendQuotes();
        SendError = string.Empty;
        StatusMessage = "Transfer cancelled — nothing was signed";
    }

    // ===== Swap (THORChain — decentralised, non-custodial cross-chain) =============================
    // The wallet never holds the funds: it sends the source coin to a THORChain inbound vault with a
    // memo (OP_RETURN), and the network delivers the target coin to the user's own receive address.

    private readonly ThorchainSwapClient _thorchain = new();
    private SwapQuote? _swapQuote;

    public ObservableCollection<string> SwapFromOptions { get; } = new(ThorchainSwapClient.SendableFrom);
    public ObservableCollection<string> SwapToOptions { get; } = new(ThorchainSwapClient.ReceivableTo);

    [ObservableProperty] private string _swapFromSymbol = "BTC";
    [ObservableProperty] private string _swapToSymbol = "ETH";
    [ObservableProperty] private string _swapAmount = string.Empty;
    [ObservableProperty] private bool _hasSwapQuote;
    [ObservableProperty] private bool _swapBusy;
    [ObservableProperty] private string _swapError = string.Empty;
    [ObservableProperty] private string _swapSuccess = string.Empty;
    [ObservableProperty] private string _swapExpectedOut = string.Empty;
    [ObservableProperty] private string _swapRateText = string.Empty;
    [ObservableProperty] private string _swapFeeText = string.Empty;
    [ObservableProperty] private string _swapEtaText = string.Empty;
    [ObservableProperty] private string _swapDestination = string.Empty;
    [ObservableProperty] private string _swapExpiryText = string.Empty;
    [ObservableProperty] private string _swapWarning = string.Empty;

    partial void OnSwapFromSymbolChanged(string value) => InvalidateSwap();
    partial void OnSwapToSymbolChanged(string value) => InvalidateSwap();
    partial void OnSwapAmountChanged(string value) => InvalidateSwap();

    private void InvalidateSwap()
    {
        HasSwapQuote = false;
        _swapQuote = null;
        SwapSuccess = string.Empty;
    }

    private static ChainId? SwapChainId(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => ChainId.Btc,
        "LTC" => ChainId.Ltc,
        "ETH" => ChainId.Eth,
        "DOGE" => ChainId.Doge,
        _ => null,
    };

    /// <summary>Step 1: fetch a live, non-binding THORChain quote for the chosen pair and amount.</summary>
    [RelayCommand]
    private async Task GetSwapQuoteAsync()
    {
        SwapError = string.Empty;
        SwapSuccess = string.Empty;
        SwapWarning = string.Empty;
        InvalidateSwap();

        if (_unlockedMnemonic is null) { SwapError = "Unlock the wallet first."; return; }
        var from = (SwapFromSymbol ?? "").ToUpperInvariant();
        var to = (SwapToSymbol ?? "").ToUpperInvariant();
        if (from == to) { SwapError = "Choose two different assets."; return; }
        if (!decimal.TryParse(SwapAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            SwapError = "Enter a valid amount.";
            return;
        }

        var toChain = SwapChainId(to);
        if (toChain is null) { SwapError = $"Cannot receive {to}."; return; }
        var destination = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, toChain.Value).Address;

        SwapBusy = true;
        try
        {
            var (quote, error) = await _thorchain.GetQuoteAsync(from, to, amount, destination);
            if (quote is null) { SwapError = error ?? "Could not get a quote."; return; }

            _swapQuote = quote;
            SwapExpectedOut = $"{Fmt(quote.ExpectedOut)} {to}";
            SwapRateText = $"1 {from} ≈ {Fmt(quote.ExpectedOut / amount)} {to}";
            SwapFeeText = $"{Fmt(quote.TotalFee)} {to} · {quote.TotalBps / 100.0:0.##}%";
            SwapEtaText = quote.EtaSeconds >= 60 ? $"~{quote.EtaSeconds / 60} min" : $"~{quote.EtaSeconds} s";
            SwapDestination = Shorten(destination);
            var mins = Math.Max(0, (int)(quote.Expiry - DateTimeOffset.UtcNow).TotalMinutes);
            SwapExpiryText = $"quote valid ~{mins} min";
            SwapWarning = quote.BelowMinimum
                ? $"Below the recommended minimum (~{Fmt(quote.RecommendedMinIn)} {from}) — the rate will be poor and the swap may refund."
                : string.Empty;
            HasSwapQuote = true;
        }
        finally
        {
            SwapBusy = false;
        }
    }

    /// <summary>Step 2: re-quote for safety, then sign and broadcast the deposit to THORChain's vault.</summary>
    [RelayCommand]
    private async Task ConfirmSwapAsync()
    {
        if (_unlockedMnemonic is null || _swapQuote is null) { SwapError = "Get a quote first."; return; }
        var shown = _swapQuote;
        var from = shown.FromSymbol;
        var to = shown.ToSymbol;

        if (!ThorchainSwapClient.SendableFrom.Contains(from)) { SwapError = $"Swapping from {from} isn't supported yet."; return; }
        var fromChain = SwapChainId(from);
        var toChain = SwapChainId(to);
        if (fromChain is null || toChain is null) { SwapError = "Unsupported asset."; return; }

        await RunBusyAsync(async () =>
        {
            SwapError = string.Empty;
            StatusMessage = "Refreshing the swap quote…";

            // A fresh quote immediately before sending: THORChain vaults rotate and quotes expire, so a
            // stale inbound address or memo would send the deposit into the void.
            var destination = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, toChain.Value).Address;
            var (fresh, error) = await _thorchain.GetQuoteAsync(from, to, shown.AmountIn, destination);
            if (fresh is null) { SwapError = error ?? "Could not refresh the quote."; return; }
            if (fresh.IsExpired) { SwapError = "The quote expired — get a new one."; return; }

            // Refuse if the rate moved materially against the user since they saw it (>3%).
            if (fresh.ExpectedOut < shown.ExpectedOut * 0.97m)
            {
                _swapQuote = fresh;
                SwapExpectedOut = $"{Fmt(fresh.ExpectedOut)} {to}";
                SwapError = "The rate moved against you — review the updated quote and confirm again.";
                StatusMessage = "Swap not sent — the rate changed";
                return;
            }

            StatusMessage = "Signing locally and broadcasting the swap deposit…";
            var fromAddr = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, fromChain.Value).Address;
            var (quote, prepErr) = await _btcSender.PrepareAsync(from, fromAddr, fresh.InboundAddress, shown.AmountIn, memo: fresh.Memo);
            if (quote is null) { SwapError = prepErr ?? "Could not build the swap deposit."; return; }

            var key = _deriver.DeriveBitcoinLikeKey(_unlockedMnemonic!, fromChain.Value);
            var (ok, txid, sendErr) = await _btcSender.SignAndBroadcastAsync(quote, key);
            if (ok && txid is not null)
            {
                var track = ThorchainSwapClient.TrackUrl(txid);
                SwapSuccess = $"Swap sent ✓  {txid}\nTHORChain will deliver ~{Fmt(fresh.ExpectedOut)} {to} to your wallet.\nTrack: {track}";
                StatusMessage = "Swap deposit broadcast · THORChain is processing it";
                InvalidateSwap();
                SwapAmount = string.Empty;
                PushActivity("Swap", $"{from}→{to}", $"-{Fmt(shown.AmountIn)}", Shorten(fresh.InboundAddress), "now", track);
                await RefreshLiveDataAsync();
            }
            else
            {
                SwapError = sendErr ?? "Broadcast failed.";
                StatusMessage = "Swap failed — nothing was sent";
            }
        });
    }

    [RelayCommand]
    private void CancelSwap()
    {
        InvalidateSwap();
        SwapError = string.Empty;
        SwapWarning = string.Empty;
        StatusMessage = "Swap cancelled — nothing was signed";
    }

    // True when the unlocked wallet is a TON-native mnemonic (Telegram Wallet / Tonkeeper) rather than
    // a BIP39 seed — it derives ONLY a TON address, not the multi-chain BIP39 set.
    private bool _isTonWallet;

    private void SetUnlocked(string mnemonic)
    {
        _unlockedMnemonic = mnemonic;
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
        PushActivity("Security", "Vault", "unlocked", "this device", "now");
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
            if (!ChainCatalog.HasRealAddress(chain.Id))
            {
                Accounts.Add(new WalletAccountViewModel(
                    chain.Symbol, chain.Name, "Planned",
                    "Adapter pending — no fake address",
                    chain.DerivationScheme ?? "Pending",
                    0, 0, chain.Name, 0));
                continue;
            }

            var account = _deriver.DeriveReceiveAddress(mnemonic, chain.Id);
            // Monero has a real address but no public balance sync — say so rather than
            // showing a confident 0.00 next to coins we genuinely track.
            var status = chain.Support == ChainSupportLevel.ReceiveOnly ? "Receive only" : "Ready";
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

        foreach (var a in rows)
        {
            Holdings.Add(new HoldingRowViewModel(
                a.Symbol, a.Name, a.Chain, a.Price, a.Amount,
                a.Price * a.Amount, a.Change24h, a.Address, a.SupportStatus));
        }
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

    private void PushActivity(string kind, string asset, string amount, string counter, string when, string? explorer = null)
    {
        // Real timestamp so persisted history reads correctly after a restart (callers pass "now").
        var stamp = string.Equals(when, "now", StringComparison.OrdinalIgnoreCase)
            ? DateTime.Now.ToString("MMM d · HH:mm", CultureInfo.InvariantCulture)
            : when;
        Activity.Insert(0, new ActivityRowViewModel(kind, asset, amount, counter, stamp, explorer));
        while (Activity.Count > 60) Activity.RemoveAt(Activity.Count - 1);

        RecentActivity.Clear();
        foreach (var row in Activity.Take(5)) RecentActivity.Add(row);
        RebuildFilteredActivity();
        RebuildTransactions();
        OnPropertyChanged(nameof(HasActivity));
        PersistActivity();
    }

    private void PersistActivity() =>
        _activityStore.Save(Activity.Select(a =>
            new ActivityStore.Entry(a.Kind, a.Asset, a.Amount, a.Counterparty, a.When, a.Explorer)));

    /// <summary>Loads the saved activity/transaction history from the data folder into the feeds.</summary>
    private void LoadActivity()
    {
        Activity.Clear();
        foreach (var e in _activityStore.Load())
            Activity.Add(new ActivityRowViewModel(e.Kind, e.Asset, e.Amount, e.Counterparty, e.When, e.Explorer));
        RecentActivity.Clear();
        foreach (var row in Activity.Take(5)) RecentActivity.Add(row);
        RebuildFilteredActivity();
        RebuildTransactions();
        OnPropertyChanged(nameof(HasActivity));
    }

    public bool HasFilteredActivity => FilteredActivity.Count > 0;

    private void RebuildFilteredActivity()
    {
        FilteredActivity.Clear();
        foreach (var row in Activity.Where(a => ActivityFilter == "All" || a.Category == ActivityFilter))
            FilteredActivity.Add(row);
        OnPropertyChanged(nameof(HasFilteredActivity));
    }

    private void RebuildTransactions()
    {
        Transactions.Clear();
        foreach (var row in Activity.Where(a => a.IsTransaction)) Transactions.Add(row);
        OnPropertyChanged(nameof(HasTransactions));
    }

    /// <summary>Copy a transaction's explorer link to the clipboard — deliberately not opened in
    /// the system browser, which would bypass Tor. Paste it into Tor Browser to view.</summary>
    [RelayCommand]
    private async Task CopyActivityLink(ActivityRowViewModel? row)
    {
        if (row?.Explorer is not { Length: > 0 } url) return;
        await CopyTextAsync(url);
        StatusMessage = "Explorer link copied — paste it into your browser to view the transaction";
    }

    private static Bitmap? BuildQr(string payload)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data);
            var bytes = png.GetGraphic(8);
            using var ms = new System.IO.MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private static async Task CopyTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { Clipboard: { } clipboard }
            })
        {
            await clipboard.SetTextAsync(text);
        }
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
        _ => null,
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
        _toastTimer.Interval = TimeSpan.FromSeconds(isError ? 5 : 3);
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
        ConfirmPassword = string.Empty;
        FormError = string.Empty;
    }
}

/// <summary>One slice of the portfolio-overview breakdown: an asset, its share, value and colour.</summary>
public sealed record PortfolioSlice(string Symbol, double Percent, double Value, Avalonia.Media.IBrush Brush)
{
    public string PercentLabel => $"{Percent:0.#}%";
    public string ValueLabel => Fx.Money(Value);
    /// <summary>Segment width for a fixed 232 px stacked bar (min 3 px so a tiny slice stays visible).</summary>
    public double BarWidth => System.Math.Max(3, Percent / 100.0 * 232.0);
}

/// <summary>A stakeable coin: symbol, name, typical (approximate) reward and how staking is done.</summary>
public sealed record StakingOption(string Symbol, string Name, string Apr, string Method);

public sealed record WalletAccountViewModel(
    string Symbol,
    string Name,
    string SupportStatus,
    string Address,
    string Derivation,
    double Price,
    double Amount,
    string Chain,
    double Change24h)
{
    /// <summary>Colour hint for the Receive list so status reads at a glance.</summary>
    public string StatusColor => SupportStatus switch
    {
        "Ready" => "#8FCB9B",        // green — real address, balance tracked
        "Receive only" => "#8FB8CB", // teal — real address, no balance sync (Monero)
        "Watch" => "#9AB0D6",        // blue — external watch-only
        _ => "#8A9099",               // muted — adapter pending
    };

    public bool IsReady => SupportStatus is "Ready" or "Watch" or "Receive only";
    public string StatusLabel => SupportStatus == "Planned" ? "Not ready" : SupportStatus;

    /// <summary>
    /// Which chain this address actually lives on. Sending a coin over the wrong network is one
    /// of the most common ways people lose funds, so every row states it explicitly.
    /// </summary>
    public string NetworkLabel => CoinNetworks.For(Symbol, Chain);
}

/// <summary>
/// Single source of truth for the human-readable network behind a ticker, so Holdings, Receive
/// and the pickers can never disagree about which chain a coin is on.
/// </summary>
public static class CoinNetworks
{
    public static string For(string symbol, string fallback) => symbol.ToUpperInvariant() switch
    {
        "BTC" => "Bitcoin network · BIP84 native SegWit",
        "ETH" => "Ethereum network (ERC-20 compatible)",
        "LTC" => "Litecoin network · BIP84 native SegWit",
        "DOGE" => "Dogecoin network",
        "TRX" => "TRON network (TRC-20 compatible)",
        "SOL" => "Solana network (SPL compatible)",
        "USDT" => "TRC-20 · Tether on the TRON network",
        "XMR" => "Monero network · private by default",
        "TON" => "TON network",
        "ADA" => "Cardano network",
        _ => fallback,
    };
}

/// <summary>One candlestick, pre-computed to pixel coordinates: the container sits at (ItemX,ItemY)
/// with size W×H; inside, the wick is a thin bar at WickLocalX and the body starts at BodyLocalY.</summary>
public sealed record CandleVm(
    double ItemX, double ItemY, double W, double H,
    double WickLocalX, double BodyLocalY, double BodyH, string Color);

/// <summary>One row in the P2P & DEX directory — a self-custody venue the user opens externally.</summary>
public sealed record P2pVenue(
    string Name, string Kind, string Custody, string Description, string Url, string Tag, string Accent);

/// <summary>A searchable Settings entry: a label, the pane it lives in, and hidden keywords.</summary>
public sealed record SettingsShortcut(string Label, string Tab, string Keywords)
{
    public string TabLabel => Tab;
}

/// <summary>One wallet in the multi-wallet switcher.</summary>
public sealed record WalletListItemViewModel(string Id, string Label, bool IsActive, bool IsLegacy)
{
    public string Badge => IsLegacy ? "MAIN" : Label.Length > 0 ? Label[..1].ToUpperInvariant() : "W";
    public string StatusLabel => IsActive
        ? Umbrella.Wallet.App.Loc.Instance["settings.walletActive"]
        : Umbrella.Wallet.App.Loc.Instance["settings.walletLocked"];
    public bool CanRemove => !IsActive; // the active wallet (and the Main seed file) are protected
}

public sealed record HoldingRowViewModel(
    string Symbol,
    string Name,
    string Chain,
    double Price,
    double Amount,
    double Value,
    double Change24h,
    string Address,
    string SupportStatus)
{
    public string PriceLabel => Fx.Price(Price);
    public string AmountLabel => $"{Amount.ToString("N6", CultureInfo.InvariantCulture)} {Symbol}";
    public string ValueLabel => Fx.Money(Value);
    public string ChangeLabel =>
        $"{(Change24h > 0 ? "▲" : Change24h < 0 ? "▼" : "·")} {Math.Abs(Change24h):0.00}%";
    public string ChangeColor =>
        Change24h > 0 ? "#8FCB9B" : Change24h < 0 ? "#E09A9A" : "#8A9099";

    /// <summary>The chain this holding sits on — shown under the coin name.</summary>
    public string NetworkLabel => CoinNetworks.For(Symbol, Chain);

    /// <summary>Each coin's own brand colour, so the token badges read at a glance instead of
    /// being ten identical grey discs. Falls back to the app violet for anything unmapped.</summary>
    public string BadgeColor => Symbol.ToUpperInvariant() switch
    {
        "BTC" => "#F7931A",
        "ETH" => "#627EEA",
        "LTC" => "#4C6FB1",
        "DOGE" => "#C2A633",
        "TRX" => "#EF0027",
        "SOL" => "#14A87A",
        "TON" => "#0098EA",
        "ADA" => "#2A5ED9",
        "XMR" => "#F26822",
        "USDT" => "#26A17B",
        _ => "#6E5FB8",
    };

    /// <summary>A soft glow of the coin's own colour under its badge — the thematic, bolder touch.
    /// Typed as <see cref="Avalonia.Media.BoxShadows"/> so the binding is an identity assignment;
    /// a raw string would not runtime-convert to the BoxShadow property the way a colour string does.</summary>
    public Avalonia.Media.BoxShadows BadgeShadow =>
        Avalonia.Media.BoxShadows.Parse($"0 0 15 0 #59{BadgeColor[1..]}");

    /// <summary>A single recognizable mark for each coin, replacing the 3-letter ticker on the badge.
    /// Uses each coin's own currency/symbol glyph where one exists (rendered in a symbol-capable
    /// font); anything unmapped falls back to its initial.</summary>
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
}

/// <summary>Coin badge marks — the coins' own currency symbols, so the token badges read as logos
/// rather than tickers, without shipping any external icon set.</summary>
public static class CoinGlyphs
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "₿",   // ₿
        ["ETH"] = "Ξ",   // Ξ
        ["LTC"] = "Ł",   // Ł
        ["DOGE"] = "Ð",  // Ð
        ["ADA"] = "₳",   // ₳
        ["USDT"] = "₮",  // ₮
        ["XMR"] = "ɱ",   // ɱ
        ["SOL"] = "◎",   // ◎
        ["TON"] = "◈",   // ◈
        ["TRX"] = "▲",   // ▲
        ["BNB"] = "◆",   // ◆
        ["MATIC"] = "⬡", // ⬡
    };

    public static string For(string symbol) =>
        Map.TryGetValue(symbol ?? "", out var g) ? g
        : string.IsNullOrEmpty(symbol) ? "?" : symbol[..1].ToUpperInvariant();
}

/// <summary>One entry in an asset / network picker.</summary>
public sealed record SendOption(string Symbol, string Name, string Network)
{
    public string Display => $"{Symbol} · {Name}";
}

public sealed record ActivityRowViewModel(
    string Kind,
    string Asset,
    string Amount,
    string Counterparty,
    string When,
    string? Explorer = null)
{
    /// <summary>A block-explorer link exists, so the row is actionable (copy the URL).</summary>
    public bool HasLink => !string.IsNullOrWhiteSpace(Explorer);

    /// <summary>True for money movements (used by the Transactions section and the "Transactions" filter).</summary>
    public bool IsTransaction => Kind is "Sent" or "Received" or "Swap";

    /// <summary>Which activity filter tab this row belongs to.</summary>
    public string Category => Kind switch
    {
        "Sent" or "Received" or "Swap" => "Transactions",
        "Connected" => "Connections",
        "Theme" or "Settings" => "Settings",
        _ => "System",
    };

    /// <summary>Colour the type at a glance: outgoing red, incoming/connected teal, swap violet,
    /// theme/settings/security/sync housekeeping muted.</summary>
    public string KindColor => Kind switch
    {
        "Sent" => "#E08A8A",
        "Received" => "#5AC8B4",
        "Connected" => "#5AC8B4",
        "Swap" => "#8A5FD6",
        "Theme" => "#E7CA83",
        _ => "#8A9099",
    };
}

/// <summary>One entry in the News section: a tagged, dated product note.</summary>
public sealed record NewsItemViewModel(string Tag, string Title, string Body, string Date)
{
    /// <summary>Tag accent colour, so update/security/guide read at a glance.</summary>
    public string TagColor => Tag switch
    {
        "SECURITY" => "#E7CA83",
        "GUIDE" => "#5AC8B4",
        _ => "#8A5FD6",
    };
}

/// <summary>
/// One coin in the market list. <see cref="IsSupported"/> states plainly whether this wallet can
/// actually hold the coin today — a "Planned" coin has no address adapter, so claiming
/// otherwise would invite someone to send funds nowhere.
/// </summary>
public sealed record MarketRowViewModel(
    string Symbol,
    string Name,
    double Price,
    double Change24h,
    bool IsSupported,
    bool HasPrice)
{
    public static MarketRowViewModel Pending(ChainInfo chain) =>
        new(chain.Symbol, chain.Name, 0, 0, chain.Support == ChainSupportLevel.Supported, false);

    public static MarketRowViewModel Live(ChainInfo chain, double price, double change) =>
        new(chain.Symbol, chain.Name, price, change, chain.Support == ChainSupportLevel.Supported, price > 0);

    /// <summary>A market-only coin (priced + charted, held via the token/EVM balance paths, not a
    /// separate wallet chain). Marked supported since its balance already surfaces in Holdings.</summary>
    public static MarketRowViewModel PendingCoin(string symbol, string name, bool holdable) =>
        new(symbol, name, 0, 0, holdable, false);

    public static MarketRowViewModel LiveCoin(string symbol, string name, double price, double change, bool holdable) =>
        new(symbol, name, price, change, holdable, price > 0);

    public string PriceLabel => HasPrice ? Fx.Price(Price) : "—";

    public string ChangeLabel => HasPrice
        ? $"{(Change24h > 0 ? "▲" : Change24h < 0 ? "▼" : "·")} {Math.Abs(Change24h):0.00}%"
        : "";

    public string ChangeColor =>
        !HasPrice ? "#8A9099" : Change24h > 0 ? "#8FCB9B" : Change24h < 0 ? "#E09A9A" : "#8A9099";

    public string Accepts => IsSupported ? "Accepted · address ready" : "Not yet · adapter pending";
    public string AcceptsColor => IsSupported ? "#8FCB9B" : "#8A9099";

    /// <summary>Inline sparkline drawn in every row, so no coin is left without a chart.</summary>
    public System.Collections.Generic.List<Avalonia.Point> Spark { get; init; } = new();

    public bool HasSpark => Spark.Count > 1;

    /// <summary>The chain this coin settles on — same wording as Holdings and Receive.</summary>
    public string NetworkLabel => CoinNetworks.For(Symbol, Name);
}
