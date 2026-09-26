using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The update channel against the real release page. <c>Category=Live</c>: CI skips it, because the
/// suite must not fail when GitHub rate-limits a runner. Run it by hand after cutting a release — it is
/// the check that the files the wallet will look for are the files the release actually has.
/// </summary>
[Trait("Category", "Live")]
public sealed class UpdateServiceLiveTests
{
    /// <summary>The offline suite arms the kill-switch; these are the runs that must reach GitHub.</summary>
    private static void Online()
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    [Fact]
    public async Task The_latest_release_is_found_and_only_offered_to_older_copies()
    {
        Online();
        var older = await UpdateService.CheckAsync("0.0.1");
        Assert.Null(older.Error);
        Assert.True(older.Available);
        Assert.NotNull(older.Release);

        var v = older.Release.Version;
        var same = await UpdateService.CheckAsync($"{v.Major}.{v.Minor}.{v.Build}");
        Assert.Null(same.Error);
        Assert.False(same.Available);
    }

    [Fact]
    public async Task Every_file_the_wallet_would_fetch_is_listed_and_githubs_digest_agrees()
    {
        Online();
        var check = await UpdateService.CheckAsync("0.0.1");
        var release = check.Release!;
        Assert.NotEmpty(release.Assets);

        using var http = new HttpClient();
        var sums = UpdateService.ParseSums(
            await http.GetStringAsync(UpdateService.DownloadUrl(release, UpdateService.SumsNameFor(release.Version))));
        Assert.NotNull(sums);

        foreach (var kind in new[] { InstallKind.WindowsInstaller, InstallKind.WindowsPortable, InstallKind.Linux })
        {
            var name = UpdateService.AssetNameFor(kind, release.Version);
            var asset = Assert.Single(release.Assets, a => a.Name == name);
            var (hash, error) = UpdateService.ExpectedHash(sums, name, asset.Sha256);
            Assert.True(hash is not null, error);
        }
    }

    [Fact]
    public async Task A_real_download_is_verified_and_kept()
    {
        Online();
        var check = await UpdateService.CheckAsync("0.0.1");
        var dir = Path.Combine(Path.GetTempPath(), $"umbrella-update-live-{Guid.NewGuid():N}");
        try
        {
            var (update, error) = await UpdateService.DownloadAsync(check.Release!, InstallKind.WindowsInstaller, dir);
            Assert.True(update is not null, error);
            Assert.True(File.Exists(update.FilePath));
            Assert.True(UpdateService.StillMatches(update));
            Assert.False(File.Exists(update.FilePath + ".part"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
