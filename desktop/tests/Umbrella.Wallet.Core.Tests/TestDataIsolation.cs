using System.Runtime.CompilerServices;

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

    private static string[] SafeEnumerate(string root, string pattern)
    {
        try { return Directory.GetDirectories(root, pattern); }
        catch { return []; }
    }
}
