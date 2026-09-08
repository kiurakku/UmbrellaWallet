using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Umbrella.Wallet.App.Views;

/// <summary>A small, borderless, centred intro window shown while the main window is built.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
