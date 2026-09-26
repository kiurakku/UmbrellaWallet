using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The update channel is the one path by which new code reaches a wallet that holds money, so every
/// rule it keeps is pinned here, offline: only a NEWER, published, non-draft release is offered; only
/// this project's own download location is trusted; and a file is kept only when its SHA-256 matches the
/// release's checksum list AND GitHub's own digest — a disagreement between the two means one was
/// changed after the release, and neither is believed.
/// </summary>
public sealed class UpdateServiceTests
{
    private const string Hash = "91f88decda26a7e23332514feea3ec7f536fe14de53a1dd6d7b40b290342624f";

    /// <summary>The shape of GitHub's real answer for v4.9.0, trimmed.</summary>
    private const string ReleaseJson = """
        {
          "tag_name": "v4.9.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-09-23T15:02:11Z",
          "body": "Every coin sends.",
          "assets": [
            { "name": "SHA256SUMS-4.9.0.txt", "size": 309,
              "digest": "sha256:0617be2b2d7a59da5332ada676f897b605fbbb81cb5608adefaad3388aa7f168",
              "browser_download_url": "https://github.com/thefear078/UmbrellaWallet/releases/download/v4.9.0/SHA256SUMS-4.9.0.txt" },
            { "name": "UmbrellaWallet-Setup-4.9.0.exe", "size": 54740820,
              "digest": "sha256:91f88decda26a7e23332514feea3ec7f536fe14de53a1dd6d7b40b290342624f",
              "browser_download_url": "https://github.com/thefear078/UmbrellaWallet/releases/download/v4.9.0/UmbrellaWallet-Setup-4.9.0.exe" }
          ]
        }
        """;

    [Fact]
    public void A_published_release_is_read_with_its_files_and_their_digests()
    {
        var release = UpdateService.ParseRelease(ReleaseJson);

        Assert.NotNull(release);
        Assert.Equal(new Version(4, 9, 0), release.Version);
        Assert.Equal("v4.9.0", release.Tag);
        Assert.Equal("Every coin sends.", release.Notes);
        var setup = Assert.Single(release.Assets, a => a.Name == "UmbrellaWallet-Setup-4.9.0.exe");
        Assert.Equal(Hash, setup.Sha256);
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    public void A_draft_or_prerelease_is_never_offered(string from, string to) =>
        Assert.Null(UpdateService.ParseRelease(ReleaseJson.Replace(from, to)));

    [Theory]
    [InlineData("v4.9.0", true)]
    [InlineData("4.10.2", true)]
    [InlineData("v4.9", false)]          // two parts: not how releases are tagged
    [InlineData("v4.9.0.1", false)]      // four parts: nor this
    [InlineData("v4.9.0-rc1", false)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    public void Only_a_three_part_version_is_a_version(string tag, bool ok) =>
        Assert.Equal(ok, UpdateService.TryParseVersion(tag, out _));

    [Fact]
    public void Versions_compare_as_numbers_not_as_text()
    {
        // "4.10.0" < "4.9.0" as text; a wallet comparing strings would never offer 4.10.
        UpdateService.TryParseVersion("4.10.0", out var ten);
        UpdateService.TryParseVersion("4.9.0", out var nine);
        Assert.True(ten > nine);
    }

    [Theory]
    [InlineData("https://github.com/thefear078/UmbrellaWallet/releases/tag/v4.9.0", "v4.9.0")]
    [InlineData("https://github.com/thefear078/UmbrellaWallet/releases/tag/v4.10.1/", "v4.10.1")]
    [InlineData("https://github.com/thefear078/UmbrellaWallet/releases", null)]
    public void The_release_page_redirect_names_the_tag(string url, string? tag) =>
        Assert.Equal(tag, UpdateService.TagFromReleaseUrl(url));

    [Theory]
    [InlineData(InstallKind.WindowsInstaller, "UmbrellaWallet-Setup-4.9.0.exe")]
    [InlineData(InstallKind.WindowsFolder, "UmbrellaWallet-Setup-4.9.0.exe")]
    [InlineData(InstallKind.WindowsPortable, "UmbrellaWallet-4.9.0-win-x64-portable.exe")]
    [InlineData(InstallKind.Linux, "UmbrellaWallet-4.9.0-linux-x64.tar.gz")]
    public void Each_install_gets_the_file_the_release_process_names_for_it(InstallKind kind, string name) =>
        Assert.Equal(name, UpdateService.AssetNameFor(kind, new Version(4, 9, 0)));

    [Fact]
    public void The_asset_names_match_what_the_release_workflow_publishes()
    {
        // The workflow is the other half of this contract. If it renames a file, the wallet would look
        // for one that does not exist — so the names are checked against the workflow itself.
        var root = FindRepoRoot();
        if (root is null) return;
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.Contains("UmbrellaWallet-Setup-", workflow);
        Assert.Contains("-win-x64-portable.exe", workflow);
        Assert.Contains("-linux-x64.tar.gz", workflow);
        Assert.Contains("SHA256SUMS-", workflow);
    }

    [Fact]
    public void A_sha256sum_manifest_is_read_by_file_name()
    {
        var sums = UpdateService.ParseSums(
            $"{Hash}  UmbrellaWallet-Setup-4.9.0.exe\n" +
            "8c0160063fee32dc5478122ab9f89084ad9ff0d2e6357b1f6bf97a774a1806e5 *UmbrellaWallet-4.9.0-linux-x64.tar.gz\r\n" +
            "not a line\n");

        Assert.NotNull(sums);
        Assert.Equal(Hash, sums["UmbrellaWallet-Setup-4.9.0.exe"]);
        Assert.Equal("8c0160063fee32dc5478122ab9f89084ad9ff0d2e6357b1f6bf97a774a1806e5", sums["UmbrellaWallet-4.9.0-linux-x64.tar.gz"]);
        Assert.Equal(2, sums.Count);
    }

    [Fact]
    public void A_manifest_naming_one_file_twice_with_different_hashes_is_not_used()
    {
        var other = new string('a', 64);
        Assert.Null(UpdateService.ParseSums($"{Hash}  f.exe\n{other}  f.exe\n"));
    }

    [Fact]
    public void The_manifest_and_githubs_digest_must_agree()
    {
        var sums = UpdateService.ParseSums($"{Hash}  f.exe\n")!;

        Assert.Equal(Hash, UpdateService.ExpectedHash(sums, "f.exe", Hash).Hash);
        Assert.Equal(Hash, UpdateService.ExpectedHash(sums, "f.exe", null).Hash);   // no digest: the manifest alone

        var (hash, error) = UpdateService.ExpectedHash(sums, "f.exe", new string('b', 64));
        Assert.Null(hash);
        Assert.Contains("disagree", error);
    }

    [Fact]
    public void A_file_the_manifest_does_not_name_is_not_downloaded()
    {
        var sums = UpdateService.ParseSums($"{Hash}  f.exe\n")!;
        var (hash, error) = UpdateService.ExpectedHash(sums, "g.exe", null);
        Assert.Null(hash);
        Assert.Contains("g.exe", error);
    }

    [Theory]
    [InlineData("https://github.com/thefear078/UmbrellaWallet/releases/download/v4.9.0/x.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/2", true)]
    [InlineData("https://github.com/someone-else/UmbrellaWallet/releases/download/v4.9.0/x.exe", false)]
    [InlineData("http://github.com/thefear078/UmbrellaWallet/releases/download/v4.9.0/x.exe", false)]
    [InlineData("https://github.com.evil.example/thefear078/UmbrellaWallet/releases/download/v4.9.0/x.exe", false)]
    [InlineData("https://objects.githubusercontent.com.evil.example/x", false)]
    public void Only_this_projects_release_downloads_are_trusted(string url, bool trusted) =>
        Assert.Equal(trusted, UpdateService.IsTrustedDownload(new Uri(url)));

    [Fact]
    public void Download_urls_point_at_this_projects_releases()
    {
        var release = UpdateService.ParseRelease(ReleaseJson)!;
        var url = UpdateService.DownloadUrl(release, "UmbrellaWallet-Setup-4.9.0.exe");

        Assert.Equal("https://github.com/thefear078/UmbrellaWallet/releases/download/v4.9.0/UmbrellaWallet-Setup-4.9.0.exe", url);
        Assert.True(UpdateService.IsTrustedDownload(new Uri(url)));
    }

    [Fact]
    public void A_file_changed_after_it_was_verified_is_not_run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umbrella-update-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(path, "verified bytes");
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            var release = UpdateService.ParseRelease(ReleaseJson)!;
            var update = new VerifiedUpdate(release, InstallKind.WindowsInstaller, path, sha);

            Assert.True(UpdateService.StillMatches(update));

            File.WriteAllText(path, "swapped bytes");
            Assert.False(UpdateService.StillMatches(update));
            var (started, error) = UpdateService.Install(update);
            Assert.False(started);
            Assert.Contains("changed", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_newer_release_supersedes_the_update_already_waiting()
    {
        var v49 = UpdateService.ParseRelease(ReleaseJson)!;
        var v410 = UpdateService.ParseRelease(ReleaseJson.Replace("v4.9.0", "v4.10.0"))!;
        var waiting = new VerifiedUpdate(v49, InstallKind.WindowsInstaller, "setup.exe", Hash);

        Assert.True(UpdateService.Supersedes(v410, waiting));    // drop 4.9.0, fetch 4.10.0
        Assert.False(UpdateService.Supersedes(v49, waiting));    // the same release: keep what is here
        Assert.False(UpdateService.Supersedes(v410, null));      // nothing waiting
    }

    [Fact]
    public void A_portable_swap_that_cannot_start_puts_the_old_program_back()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"umbrella-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var current = Path.Combine(dir, "Umbrella.exe");
            var fresh = Path.Combine(dir, "new.exe");
            File.WriteAllText(current, "old program");
            File.WriteAllText(fresh, "new program");

            Assert.Throws<InvalidOperationException>(() =>
                UpdateService.SwapExecutable(current, fresh, _ => throw new InvalidOperationException("cannot start")));

            // The copy that runs next time is still the working one, and nothing is left half-swapped.
            Assert.Equal("old program", File.ReadAllText(current));
            Assert.False(File.Exists(current + ".old"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_portable_swap_that_starts_leaves_the_new_program_in_place()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"umbrella-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var current = Path.Combine(dir, "Umbrella.exe");
            var fresh = Path.Combine(dir, "new.exe");
            File.WriteAllText(current, "old program");
            File.WriteAllText(fresh, "new program");
            string? started = null;

            var (ok, _) = UpdateService.SwapExecutable(current, fresh, path => started = path);

            Assert.True(ok);
            Assert.Equal(current, started);
            Assert.Equal("new program", File.ReadAllText(current));
            Assert.Equal("old program", File.ReadAllText(current + ".old"));   // removed on the next start
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github"))) dir = dir.Parent;
        return dir?.FullName;
    }
}
