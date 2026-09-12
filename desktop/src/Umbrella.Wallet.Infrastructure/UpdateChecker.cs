using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Checks GitHub for a newer release. Deliberately manual (never a background ping) and routed
/// through <see cref="PublicHttp"/>, so when Tor is on the check goes over Tor and the request
/// leaks nothing about the user. It only reads the published <c>VERSION</c> file and compares —
/// it never touches the vault or the data directory, so updating can never lose funds or settings.
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesUrl = "https://github.com/kiurakku/umbrella-wallet/releases";
    private const string VersionUrl =
        "https://raw.githubusercontent.com/kiurakku/umbrella-wallet/main/VERSION";

    public sealed record Result(bool Available, string Latest, string? Error);

    public static async Task<Result> CheckAsync(string current, CancellationToken ct = default)
    {
        try
        {
            using var res = await PublicHttp.For(PublicHttp.NetworkPurpose.Maintenance).GetAsync(VersionUrl, ct);
            if (!res.IsSuccessStatusCode)
            {
                return new Result(false, current, $"Update server returned {(int)res.StatusCode}.");
            }

            var latest = (await res.Content.ReadAsStringAsync(ct)).Trim();
            if (!Version.TryParse(latest, out var lv) || !Version.TryParse(current, out var cv))
            {
                return new Result(false, latest, "Could not read the latest version.");
            }

            return new Result(lv > cv, latest, null);
        }
        catch (Exception ex)
        {
            return new Result(false, current, ex.Message);
        }
    }
}
