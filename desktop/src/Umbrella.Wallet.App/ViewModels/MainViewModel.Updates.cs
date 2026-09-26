using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Updates that arrive by themselves — and are still never installed behind the user's back.
///
/// The wallet looks for a newer release shortly after it starts and twice a day after that, downloads
/// it, and keeps it only if it matches both published checksums (<see cref="UpdateService"/>). Then it
/// says so, and waits. Replacing the program always takes the user's click: a wallet that could swap its
/// own binary unasked would be the most attractive update channel there is to attack. Both halves can be
/// switched off, and every request goes through the same route as everything else, Tor included.
/// </summary>
public partial class MainViewModel
{
    /// <summary>First look this long after start: the wallet has more urgent things to fetch first.</summary>
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(45);

    /// <summary>Then this often. Releases are days apart; there is nothing to gain from asking more.</summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);

    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateLatestVersion = string.Empty;
    [ObservableProperty] private string _updateNotes = string.Empty;
    [ObservableProperty] private bool _isUpdateDownloading;
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private bool _updateReady;
    [ObservableProperty] private bool _updateBannerDismissed;

    private ReleaseInfo? _latestRelease;
    private VerifiedUpdate? _verifiedUpdate;
    private Avalonia.Threading.DispatcherTimer? _updateTimer;
    private CancellationTokenSource? _updateCts;
    private bool _updateCheckRunning;

    /// <summary>Look for new releases without being asked. Persisted; on unless switched off.</summary>
    public bool AutoUpdateCheck
    {
        get => _uiSettings.AutoUpdateCheck;
        set
        {
            if (_uiSettings.AutoUpdateCheck == value) return;
            _uiSettings.AutoUpdateCheck = value;
            _uiSettings.Save();
            OnPropertyChanged();
            ScheduleUpdateChecks();
        }
    }

    /// <summary>Download a release as soon as it is found, so installing it is one click. Persisted.</summary>
    public bool AutoUpdateDownload
    {
        get => _uiSettings.AutoUpdateDownload;
        set
        {
            if (_uiSettings.AutoUpdateDownload == value) return;
            _uiSettings.AutoUpdateDownload = value;
            _uiSettings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The strip across the top of the workspace: shown while an update is waiting, until the
    /// user closes it for this session.</summary>
    public bool ShowUpdateBanner => (UpdateAvailable || UpdateReady) && !UpdateBannerDismissed;

    /// <summary>The banner's sentence.</summary>
    public string UpdateBannerText => UpdateReady
        ? string.Format(Loc.Instance["update.ready"], UpdateLatestVersion)
        : IsUpdateDownloading
            ? string.Format(Loc.Instance["update.downloading"], UpdateLatestVersion, (int)UpdateProgress)
            : string.Format(Loc.Instance["update.available"], UpdateLatestVersion, CurrentVersion);

    public bool HasUpdateNotes => UpdateNotes.Length > 0;

    /// <summary>The Download button, when there is something to download and it is not already here.</summary>
    public bool CanDownloadUpdate => UpdateAvailable && !UpdateReady && !IsUpdateDownloading;

    partial void OnUpdateAvailableChanged(bool value) => RaiseUpdateBanner();
    partial void OnUpdateReadyChanged(bool value) => RaiseUpdateBanner();
    partial void OnIsUpdateDownloadingChanged(bool value) => RaiseUpdateBanner();
    partial void OnUpdateProgressChanged(double value) => OnPropertyChanged(nameof(UpdateBannerText));
    partial void OnUpdateBannerDismissedChanged(bool value) => OnPropertyChanged(nameof(ShowUpdateBanner));
    partial void OnUpdateNotesChanged(string value) => OnPropertyChanged(nameof(HasUpdateNotes));

    private void RaiseUpdateBanner()
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(CanDownloadUpdate));
    }

    /// <summary>Where downloaded releases wait to be installed. Beside the wallet's data, never inside a vault.</summary>
    private static string UpdatesDirectory => Path.Combine(AppPaths.DataRoot, "updates");

    /// <summary>
    /// Starts (or stops) the automatic checks. Called once at start-up and whenever the setting changes.
    /// Leftovers from an update that already happened are cleared here too.
    /// </summary>
    private void ScheduleUpdateChecks()
    {
        try
        {
            UpdateService.CleanUpAfterUpdate();
            RemoveInstalledUpdateDownloads();

            _updateTimer?.Stop();
            if (!AutoUpdateCheck) return;

            _updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = FirstUpdateCheckDelay };
            _updateTimer.Tick += async (_, _) =>
            {
                _updateTimer!.Interval = UpdateCheckInterval;
                await RunUpdateCheckAsync(userAsked: false);
            };
            _updateTimer.Start();
        }
        catch
        {
            // No dispatcher (unit tests) — automatic checks are a convenience, never a requirement.
        }
    }

    /// <summary>Deletes downloads for versions this copy already is (or is newer than).</summary>
    private static void RemoveInstalledUpdateDownloads()
    {
        try
        {
            if (!Directory.Exists(UpdatesDirectory) || !UpdateService.TryParseVersion(CurrentVersion, out var running)) return;
            foreach (var dir in Directory.GetDirectories(UpdatesDirectory))
            {
                if (UpdateService.TryParseVersion(Path.GetFileName(dir), out var v) && v <= running)
                    Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still in use; next start tries again.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    [RelayCommand]
    private Task CheckForUpdates() => RunUpdateCheckAsync(userAsked: true);

    /// <summary>
    /// One check. A background check that fails stays quiet — the network being down is not news — while
    /// one the user asked for says what happened.
    /// </summary>
    private async Task RunUpdateCheckAsync(bool userAsked)
    {
        if (_updateCheckRunning) return;
        _updateCheckRunning = true;
        try
        {
            if (userAsked) UpdateStatus = Loc.Instance["update.checking"];
            var result = await UpdateService.CheckAsync(CurrentVersion);

            if (result.Error is not null)
            {
                if (userAsked) UpdateStatus = string.Format(Loc.Instance["update.checkFailed"], result.Error);
                return;
            }

            if (!result.Available || result.Release is null)
            {
                UpdateAvailable = false;
                if (userAsked) UpdateStatus = string.Format(Loc.Instance["update.latest"], CurrentVersion);
                return;
            }

            var release = result.Release;
            var newVersion = !string.Equals(UpdateLatestVersion, VersionText(release.Version), StringComparison.Ordinal);

            // A release newer than the one already downloaded supersedes it. Keeping the old file as
            // "ready" would name the new version on the banner and install the old one on the click.
            if (UpdateService.Supersedes(release, _verifiedUpdate))
            {
                _verifiedUpdate = null;
                UpdateReady = false;
            }

            _latestRelease = release;
            UpdateLatestVersion = VersionText(release.Version);
            UpdateNotes = TrimNotes(release.Notes);
            UpdateAvailable = true;
            if (newVersion) UpdateBannerDismissed = false;
            UpdateStatus = string.Format(Loc.Instance["update.available"], UpdateLatestVersion, CurrentVersion);

            if (!UpdateReady && (userAsked || AutoUpdateDownload)) await DownloadUpdateAsync();
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    [RelayCommand]
    private Task DownloadUpdate() => DownloadUpdateAsync();

    private async Task DownloadUpdateAsync()
    {
        if (_latestRelease is null || IsUpdateDownloading || UpdateReady) return;
        var stale = false;

        IsUpdateDownloading = true;
        UpdateProgress = 0;
        _updateCts = new CancellationTokenSource();
        try
        {
            var release = _latestRelease;
            var kind = UpdateService.DetectInstallKind();
            var progress = new Progress<double>(p => UpdateProgress = Math.Round(p * 100));
            var (update, error) = await UpdateService.DownloadAsync(
                release, kind, Path.Combine(UpdatesDirectory, VersionText(release.Version)), progress, _updateCts.Token);

            if (update is null)
            {
                UpdateStatus = string.Format(Loc.Instance["update.downloadFailed"], error);
                return;
            }

            // A newer release may have been found while this one downloaded. The file is then already
            // out of date: it is dropped and the newer one fetched, so the banner and the file it installs
            // can never name two different versions.
            if (_latestRelease.Version != update.Release.Version)
            {
                stale = true;
                TryDeleteFile(update.FilePath);
                return;
            }

            _verifiedUpdate = update;
            UpdateReady = true;
            UpdateStatus = string.Format(Loc.Instance["update.ready"], UpdateLatestVersion) + " " +
                           Loc.Instance[kind == InstallKind.Linux ? "update.linuxNote" : "update.verifiedNote"];
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = Loc.Instance["update.cancelled"];
        }
        finally
        {
            IsUpdateDownloading = false;
            _updateCts?.Dispose();
            _updateCts = null;
        }

        if (stale) await DownloadUpdateAsync();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover in the updates folder; removed with the rest on a later start.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    /// <summary>
    /// Hands over to the verified update and closes the wallet, so its files can be replaced. The vault
    /// is locked first: the new copy opens with the password, like any other start.
    /// </summary>
    [RelayCommand]
    private void InstallUpdate()
    {
        if (_verifiedUpdate is null) return;

        if (_verifiedUpdate.Kind == InstallKind.Linux)
        {
            OpenUpdateFolder();
            return;
        }

        // Locked BEFORE anything is started: the new copy must never run beside an unlocked old one.
        LockVault();
        var (started, error) = UpdateService.Install(_verifiedUpdate);
        if (!started)
        {
            UpdateStatus = string.Format(Loc.Instance["update.installFailed"], error);
            ShowToast(UpdateStatus, isError: true);   // the wallet is locked now, so Settings is out of sight
            return;
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    [RelayCommand]
    private void DismissUpdateBanner() => UpdateBannerDismissed = true;

    /// <summary>From the banner to the full story: the release notes, the checks, the buttons.</summary>
    [RelayCommand]
    private void ShowUpdateDetails()
    {
        SettingsTab = "Appearance";
        SelectSection("Settings");
    }

    /// <summary>Shows the folder a downloaded update is in (Linux, or when installing it failed).</summary>
    [RelayCommand]
    private void OpenUpdateFolder()
    {
        var folder = _verifiedUpdate is null ? UpdatesDirectory : Path.GetDirectoryName(_verifiedUpdate.FilePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            UpdateStatus = folder;   // at least say where it is
        }
    }

    [RelayCommand]
    private async Task CopyReleasesLink()
    {
        await CopyTextAsync(UpdateService.ReleasesUrl);
        StatusMessage = Loc.Instance["status.downloadLinkCopied"];
    }

    private static string VersionText(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    /// <summary>Release notes are markdown and can be long; the banner's details show the start of them.</summary>
    private static string TrimNotes(string notes)
    {
        var text = notes.Replace("\r\n", "\n").Trim();
        return text.Length <= 1200 ? text : text[..1200].TrimEnd() + "…";
    }
}
