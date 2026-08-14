using Avalonia.Platform.Storage;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _autoLockTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private MainViewModel? _observed;

    // SetWindowDisplayAffinity: WDA_EXCLUDEFROMCAPTURE (0x11) makes the window render black in
    // screenshots and screen-share while the seed phrase is visible. WDA_NONE (0) turns it off.
    private const uint WdaNone = 0x00;
    private const uint WdaExcludeFromCapture = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public MainWindow()
    {
        InitializeComponent();
        _autoLockTimer.Tick += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.LockVault();
            }
        };
        PointerPressed += OnUserActivity;
        KeyDown += OnUserActivity;
        Opened += (_, _) => ResetAutoLock();
        // Privacy: lock the vault the instant the window is minimized, if the user opted in.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty
                && (WindowState)e.NewValue! == WindowState.Minimized
                && DataContext is MainViewModel { IsUnlocked: true, LockOnMinimize: true } vm)
            {
                vm.LockVault();
            }
        };
        // NOTE: the file dialogs are wired in OnDataContextChanged, NOT here — the window is created
        // with `new MainWindow { DataContext = vm }`, so DataContext is still null in the constructor.

        Closed += (_, _) =>
        {
            _autoLockTimer.Stop();
            // Never leave the bundled Tor / Monero processes running after the wallet closes.
            if (DataContext is MainViewModel vm)
            {
                vm.ShutdownTor();
                vm.ShutdownMonero();
            }
        };
        DataContextChanged += OnDataContextChanged;
    }

    // --- Title-bar dragging (custom chrome) ----------------------------------
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    // --- Screenshot protection while the seed phrase is on screen -------------
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observed = DataContext as MainViewModel;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnViewModelPropertyChanged;
            UpdateCaptureProtection();
            ApplyMobileMode();
            WirePickers(_observed);
        }
    }

    /// <summary>Resizes the window to a phone shape (and back) when the mobile-layout setting flips.
    /// The layout itself — bottom tab bar and narrow column — is driven by the view-model.</summary>
    private void ApplyMobileMode()
    {
        if (_observed is null) return;
        if (_observed.MobileMode)
        {
            WindowState = WindowState.Normal;
            MinWidth = 360;
            MinHeight = 640;
            Width = 430;
            Height = 900;
        }
        else
        {
            MinWidth = 1000;
            MinHeight = 700;
            Width = 1240;
            Height = 820;
        }
        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea;
            var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
            var w = Width * scale;
            var h = Height * scale;
            Position = new Avalonia.PixelPoint(
                area.X + (int)Math.Max(0, (area.Width - w) / 2),
                area.Y + (int)Math.Max(0, (area.Height - h) / 2));
        }
        catch
        {
            // Positioning is cosmetic — never let it throw.
        }
    }

    /// <summary>Supplies the view-model with file dialogs (backup + profile images). Wired here, when
    /// the DataContext is actually set, because the window is built with an object initializer.</summary>
    private void WirePickers(MainViewModel vm)
    {
        vm.PickFileAsync = async (suggested, save) =>
        {
            var storage = StorageProvider;
            if (save)
            {
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Umbrella backup",
                    SuggestedFileName = suggested,
                    DefaultExtension = "json",
                    FileTypeChoices = [new FilePickerFileType("Umbrella backup") { Patterns = ["*.json"] }],
                });
                return file?.TryGetLocalPath();
            }

            var opened = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Restore Umbrella backup",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Umbrella backup") { Patterns = ["*.json"] }],
            });
            return opened.Count > 0 ? opened[0].TryGetLocalPath() : null;
        };

        // Profile images (avatar / banner / sidebar background): the user picks their own file.
        vm.PickImageAsync = async () =>
        {
            var opened = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose an image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"],
                    },
                ],
            });
            return opened.Count > 0 ? opened[0].TryGetLocalPath() : null;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsBackupStage)
            or nameof(MainViewModel.IsSettingsPhraseVisible)
            or nameof(MainViewModel.IsMoneroKeysVisible))
        {
            UpdateCaptureProtection();
        }

        // Re-arm the idle timer when the user changes the auto-lock preference.
        if (e.PropertyName is nameof(MainViewModel.AutoLockMinutes))
        {
            ResetAutoLock();
        }

        // Reshape the window when the mobile-layout setting is toggled.
        if (e.PropertyName is nameof(MainViewModel.MobileMode))
        {
            ApplyMobileMode();
        }
    }

    private void UpdateCaptureProtection()
    {
        if (_observed is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetHandle();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Any on-screen secret (seed phrase or Monero spend key) blocks screen capture.
        var secretVisible = _observed.IsBackupStage
            || _observed.IsSettingsPhraseVisible
            || _observed.IsMoneroKeysVisible;
        SetWindowDisplayAffinity(handle, secretVisible ? WdaExcludeFromCapture : WdaNone);
    }

    private IntPtr TryGetHandle()
    {
        try
        {
            return TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    // --- Parallax hero: the backdrop drifts a few px opposite the cursor, easing via the transform's
    // own transitions, and settles back when the pointer leaves. ---
    private Avalonia.Media.TranslateTransform? _heroShift;

    private void OnHeroPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control card) return;
        _heroShift ??= this.FindControl<Image>("HeroBg")?.RenderTransform as Avalonia.Media.TranslateTransform;
        if (_heroShift is null) return;
        var p = e.GetPosition(card);
        var nx = (p.X / Math.Max(1, card.Bounds.Width)) - 0.5;
        var ny = (p.Y / Math.Max(1, card.Bounds.Height)) - 0.5;
        _heroShift.X = -nx * 14;
        _heroShift.Y = -ny * 10;
    }

    private void OnHeroPointerExited(object? sender, PointerEventArgs e)
    {
        if (_heroShift is null) return;
        _heroShift.X = 0;
        _heroShift.Y = 0;
    }

    // --- Market detail chart crosshair: map the pointer X to the nearest candle. ---
    private void OnChartPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Control canvas) return;
        vm.UpdateCrosshair(e.GetPosition(canvas).X);
    }

    private void OnChartPointerExited(object? sender, PointerEventArgs e)
    {
        (DataContext as MainViewModel)?.HideCrosshair();
    }

    private void OnUserActivity(object? sender, EventArgs eventArgs) => ResetAutoLock();

    private void ResetAutoLock()
    {
        _autoLockTimer.Stop();
        // 0 = the user disabled auto-lock; leave the timer stopped so the vault never locks on idle.
        var minutes = (DataContext as MainViewModel)?.AutoLockMinutes ?? 5;
        if (minutes <= 0) return;
        _autoLockTimer.Interval = TimeSpan.FromMinutes(minutes);
        _autoLockTimer.Start();
    }
}
