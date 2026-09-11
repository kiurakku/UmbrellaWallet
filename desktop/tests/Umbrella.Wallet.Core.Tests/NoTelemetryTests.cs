using System.Text.RegularExpressions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The wallet tells the user, on three separate screens, that it has no analytics, no crash reporting
/// and no account. That is currently true — and until now nothing stopped it becoming false.
///
/// A claim like this does not break loudly when it stops holding. Somebody adds a crash reporter to
/// diagnose a hard bug, entirely in good faith, and the Security Center goes on saying "nothing about
/// you leaves this device" while a stack trace containing a file path, a wallet id or a window title
/// is posted to a third party. The people who most need that claim to be true are exactly the ones who
/// will never find out.
///
/// So the claim is enforced. Adding any of these to the project fails the build, and the failure names
/// the package — at which point the copy has to change or the package has to go.
/// </summary>
public sealed class NoTelemetryTests
{
    /// <summary>Package-name fragments that would make the "no telemetry" claim false.</summary>
    private static readonly string[] ForbiddenPackages =
    [
        "Sentry", "Firebase", "ApplicationInsights", "AppCenter", "Mixpanel", "Amplitude",
        "PostHog", "Bugsnag", "Raygun", "Datadog", "NewRelic", "GoogleAnalytics", "Segment",
        "OpenTelemetry", "Elastic.Apm", "Rollbar", "Countly", "Matomo",
    ];

    /// <summary>API shapes that ship data off the device outside the wallet's own network layer.</summary>
    private static readonly string[] ForbiddenCalls =
    [
        "TrackEvent(", "CaptureException(", "CaptureMessage(", "LogEvent(", "SetUserId(",
        "Analytics.", "Crashlytics", "TelemetryClient",
    ];

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        Directory.GetFiles(Path.Combine(root, "desktop"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_analytics_or_crash_reporting_package_is_referenced()
    {
        var root = FindRepoRoot();
        if (root is null) return; // not a source checkout

        var offenders = new List<string>();
        foreach (var csproj in SourceFiles(root, "*.csproj"))
        {
            var text = File.ReadAllText(csproj);
            foreach (var forbidden in ForbiddenPackages)
            {
                if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{Path.GetFileName(csproj)} references {forbidden}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_code_calls_an_analytics_or_crash_reporting_api()
    {
        // A package reference is the obvious route; a direct HTTP post to a collector is the other. The
        // call shapes below catch the common SDK surfaces even if the package were vendored in.
        var root = FindRepoRoot();
        if (root is null) return;

        var offenders = new List<string>();
        foreach (var file in SourceFiles(root, "*.cs"))
        {
            // This test names the very things it forbids, so it must not accuse itself.
            if (Path.GetFileName(file) == "NoTelemetryTests.cs") continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var call in ForbiddenCalls)
                {
                    if (lines[i].Contains(call, StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {call}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Certificate_validation_is_never_disabled()
    {
        // One line turns every HTTPS connection this wallet makes into something a proxy can read —
        // including the addresses it asks explorers about. It is the kind of line that gets added to
        // debug a corporate network and then forgotten.
        var root = FindRepoRoot();
        if (root is null) return;

        var dangerous = new Regex(
            @"ServerCertificateCustomValidationCallback|DangerousAcceptAnyServerCertificateValidator|CheckCertificateRevocationList\s*=\s*false",
            RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in SourceFiles(root, "*.cs"))
        {
            if (Path.GetFileName(file) == "NoTelemetryTests.cs") continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (dangerous.IsMatch(lines[i])) offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Nothing_writes_a_log_file_that_could_hold_wallet_data()
    {
        // The wallet keeps no application log at all, on purpose: a log is the easiest place for an
        // address, an amount or a wallet id to end up sitting in the clear. The bundled Monero daemon
        // writes its own log, which is its business and is named here as the one exception.
        var root = FindRepoRoot();
        if (root is null) return;

        var writers = new Regex(@"File\.AppendAll|StreamWriter\s*\(|Trace\.Write|Console\.Write", RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in SourceFiles(root, "*.cs"))
        {
            var name = Path.GetFileName(file);
            if (name == "NoTelemetryTests.cs") continue;
            // Tests write fixtures and scratch files; this rule is about the shipped app.
            if (file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (writers.IsMatch(lines[i])) offenders.Add($"{name}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.Empty(offenders);
    }
}
