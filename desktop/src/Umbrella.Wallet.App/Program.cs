using Avalonia;
using System;

namespace Umbrella.Wallet.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        WaitForThePreviousCopy(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// A portable update starts the new exe while the old one is still closing. The old copy holds the
    /// bundled Tor's port and the data folder until it has exited, so the new one waits for it — briefly,
    /// and only when it was started as an update (<c>--after-update &lt;pid&gt;</c>).
    /// </summary>
    private static void WaitForThePreviousCopy(string[] args)
    {
        var at = Array.IndexOf(args, "--after-update");
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var pid)) return;
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(pid);
            previous.WaitForExit(20_000);
        }
        catch (ArgumentException)
        {
            // Already gone — nothing to wait for.
        }
        catch (InvalidOperationException)
        {
            // Same.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
