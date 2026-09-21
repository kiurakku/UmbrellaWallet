using System.Runtime.CompilerServices;
using Umbrella.Wallet.App;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Points every test at a throwaway data directory.
///
/// <c>AppPaths.DataRoot</c> defaults to a <c>data</c> folder beside the executable, which under
/// <c>dotnet test</c> is the test assembly's own bin folder — one directory shared by every test in
/// the run AND by every run after it. So the balance cache, market cache, activity log and UI
/// settings written by one test were read by the next, and by tomorrow's run.
///
/// That is not theoretical. Two view-model tests started failing with "an item with the same key has
/// already been added" against unchanged code, because a JSON file left behind by an earlier run held
/// a shape a later build no longer expected. The tests were fine; the ground under them had moved.
///
/// A module initializer runs before any test touches <c>AppPaths</c>, and <c>DataRoot</c> is resolved
/// once and cached, so setting the override here is enough to give the whole run a clean directory
/// that never collides with a developer's real wallet either.
/// </summary>
internal static class TestDataIsolation
{
    [ModuleInitializer]
    internal static void UseAThrowawayDataDirectory()
    {
        // Only if the harness has not already chosen one — CI or a developer debugging a specific
        // state should still be able to point the run somewhere deliberate.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UMBRELLA_DATA_DIR"))) return;

        var directory = Path.Combine(Path.GetTempPath(), $"umbrella-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("UMBRELLA_DATA_DIR", directory);
        WriteOfflineSettings(directory);

        GoOffline();

        // Best-effort sweep of directories left by previous runs. Skipped silently on anything still
        // locked: a stale temp folder is untidy, never a failure worth breaking a test run over.
        foreach (var stale in SafeEnumerate(Path.GetTempPath(), "umbrella-tests-*"))
        {
            if (stale == directory) continue;
            try
            {
                if (Directory.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1))
                    Directory.Delete(stale, recursive: true);
            }
            catch
            {
                // in use, or not ours to delete
            }
        }
    }

    /// <summary>
    /// Takes the whole suite off the network, using the wallet's own kill-switch.
    ///
    /// <c>SetRequireProxy(true)</c> with no proxy configured is exactly the Tor-only fail-closed state:
    /// the shared client refuses every connection in its ConnectCallback, before DNS and before a
    /// socket. Nothing goes out, and nothing waits for a timeout.
    ///
    /// Using the production switch rather than a test-only flag is deliberate — the suite exercises the
    /// same path a user in Tor-only mode gets, and there is no special build behaviour to drift.
    ///
    /// Pointing the configurable chains at a closed port stays as well: it covers the same ground from
    /// the other end and keeps working for any test that deliberately turns the kill-switch off.
    /// </summary>
    internal static void GoOffline()
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(true);
        PointEveryChainAtNothing();
    }

    /// <summary>
    /// Writes a settings file that leaves the kill-switch ON.
    ///
    /// Arming it once at start-up was not enough: MainViewModel's constructor applies the SAVED
    /// Tor-only preference - <c>PublicHttp.SetRequireProxy(_uiSettings.TorOnlyMode)</c> - and with no
    /// settings file that preference is false, so every view-model the suite built quietly switched
    /// the network back on. The tests then spent fifteen seconds per connect timeout against hosts
    /// that are not in the endpoint registry and cannot be redirected: Blockscout, CoinGecko, the EVM
    /// side-chain RPCs. Nineteen view-model tests took five and a half minutes between them.
    ///
    /// Configuring the app rather than special-casing it keeps the suite on the same code path a real
    /// Tor-only user gets, and means a view-model can be constructed as many times as a test likes
    /// without reopening the network.
    /// </summary>
    /// <summary>
    /// Restores the run's baseline settings, including the first-run acknowledgement.
    ///
    /// Needed because "delete everything" genuinely deletes the settings file — that is the behaviour
    /// DataWiperTests exist to prove — and a wiped install is a first run again, disclaimer and all.
    /// A test about unlocking that happened to run after a wipe would otherwise open on the
    /// acknowledgement screen and fail for a reason that has nothing to do with it.
    /// </summary>
    internal static void RestoreBaselineSettings() => WriteOfflineSettings(AppPaths.DataRoot);

    private static void WriteOfflineSettings(string directory)
    {
        try
        {
            // AcceptedTermsVersion stands the run up as an install that has already seen the
            // first-run disclaimer (roadmap L.1/L.3/L.8) — otherwise every screen behind that gate
            // would be unreachable in tests about something else. The gate itself is proved by
            // FirstRunDisclaimerTests, which puts a view model in front of an unaccepted install.
            File.WriteAllText(
                Path.Combine(directory, "ui-settings.json"),
                $$"""{"TorOnlyMode":true,"Language":"en","AcceptedTermsVersion":{{FirstRunConsent.CurrentVersion}}}""");
        }
        catch
        {
            // Not worth failing a run over; the overrides above still cover the chain lookups.
        }
    }

    /// <summary>
    /// Sends every chain lookup to a closed port on this machine.
    ///
    /// These tests were never offline — they were quietly querying Blockstream, TronGrid, CoinGecko
    /// and the rest on every wallet they created, and only LOOKED fast because those calls were
    /// failing. The moment a Bitcoin scan started succeeding, the suite went from two and a half
    /// minutes to eleven, because it had begun walking real gap limits over the real internet.
    ///
    /// That was never the deal. A unit suite must not depend on somebody else's uptime, must not put
    /// load on free public explorers every time anybody runs it, and must not change its answer
    /// because a server is rate-limiting today. A refused connection on loopback returns immediately,
    /// with no DNS and no timeout, and the scanner treats it exactly as it treats any unreachable
    /// explorer: "unknown", never "empty".
    ///
    /// The live tests clear this in their own constructors, which is what makes them live.
    /// </summary>
    internal static void PointEveryChainAtNothing()
    {
        foreach (var symbol in ChainEndpoints.Configurable)
        {
            // Port 1 is reserved and never listened on; the OS refuses instantly.
            ChainEndpoints.SetOverride(symbol, "http://127.0.0.1:1");
        }
    }

    private static string[] SafeEnumerate(string root, string pattern)
    {
        try { return Directory.GetDirectories(root, pattern); }
        catch { return []; }
    }
}
