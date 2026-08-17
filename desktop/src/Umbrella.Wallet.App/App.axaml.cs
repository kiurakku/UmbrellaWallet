using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.App.Views;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Relocate anything an earlier version wrote to the system drive before the vault
            // is opened, so the wallet keeps working and stops growing C:.
            AppPaths.MigrateLegacyData();

            // Theme and language before the first window paints, so nothing flashes the
            // default palette on the way to the user's choice.
            UiSettings.LoadAndApply();

            // Show a small animated intro window first; the main window is left null so the lifetime
            // doesn't auto-show it. Stay alive while only the splash is open (OnLastWindowClose), then
            // the app exits when the main window is later closed.
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            var splash = new SplashWindow();
            splash.Show();

            // Let the intro animate for a beat, then build the main window (its view-model init runs
            // here) and — after the animation finishes — reveal it and close the splash.
            DispatcherTimer.RunOnce(() =>
            {
                var main = new MainWindow
                {
                    DataContext = new MainViewModel(new WalletRegistry()),
                };
                desktop.MainWindow = main;

                DispatcherTimer.RunOnce(() =>
                {
                    main.Show();
                    main.Activate();
                    splash.Close();
                }, TimeSpan.FromMilliseconds(950));
            }, TimeSpan.FromMilliseconds(1050));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
