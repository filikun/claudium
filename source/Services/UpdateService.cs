using Velopack;
using Velopack.Sources;

namespace Claudium.Services;

/// <summary>
/// Checks GitHub Releases for a newer Claudium build and applies it in the
/// background. Safe to call from a dev/unpackaged run — UpdateManager.IsInstalled
/// is false there, so the check is skipped instead of throwing.
/// </summary>
public static class UpdateService
{
    private const string RepoUrl = "https://github.com/filikun/claudium";

    private static UpdateManager? _armedManager;
    private static string? _armedVersion;

    // Guards the startup check and the Settings button from running at the same
    // time (or twice over) — two concurrent WaitExitThenApplyUpdates calls would
    // arm two updaters against the same install.
    private static readonly SemaphoreSlim _checkLock = new(1, 1);

    public enum UpdateOutcome
    {
        /// <summary>Not a Velopack install (dev run) — there is nothing to update.</summary>
        NotInstalled,
        UpToDate,
        /// <summary>A newer build was downloaded and is armed to apply on exit.</summary>
        Ready,
        /// <summary>Offline, GitHub rate limit, etc.</summary>
        Failed
    }

    /// <summary><see cref="Version"/> is set for <see cref="UpdateOutcome.Ready"/>, <see cref="Error"/> for <see cref="UpdateOutcome.Failed"/>.</summary>
    public readonly record struct UpdateResult(UpdateOutcome Outcome, string? Version = null, string? Error = null);

    /// <summary>
    /// Raised off the UI thread once a downloaded update is armed and ready — the update
    /// applies automatically the next time this process exits, whether that's the user
    /// clicking "restart now" (<see cref="RestartNow"/>) or just closing the app normally.
    /// The argument is the new version (e.g. "1.1.6").
    /// </summary>
    public static event Action<string>? UpdateReady;

    /// <summary>True once an update has been downloaded and armed this session.</summary>
    public static bool IsUpdateReady => _armedManager != null;

    public static async Task<UpdateResult> CheckForUpdatesAsync()
    {
        // Already downloaded and armed earlier this session — re-running the check would
        // just re-arm the same update, so report the pending version straight back.
        if (_armedManager != null)
        {
            return new UpdateResult(UpdateOutcome.Ready, _armedVersion);
        }

        await _checkLock.WaitAsync();
        try
        {
            if (_armedManager != null)
            {
                return new UpdateResult(UpdateOutcome.Ready, _armedVersion);
            }

            var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!manager.IsInstalled)
            {
                return new UpdateResult(UpdateOutcome.NotInstalled);
            }

            var newVersion = await manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                return new UpdateResult(UpdateOutcome.UpToDate);
            }

            await manager.DownloadUpdatesAsync(newVersion);

            // Arms the updater to apply + relaunch automatically on the next process
            // exit — restart:true so a plain close (not just the explicit button) also
            // picks up the update rather than requiring the user to notice and act.
            manager.WaitExitThenApplyUpdates(newVersion, silent: false, restart: true);
            _armedManager = manager;
            _armedVersion = newVersion.TargetFullRelease.Version.ToString();

            UpdateReady?.Invoke(_armedVersion);
            return new UpdateResult(UpdateOutcome.Ready, _armedVersion);
        }
        catch (Exception ex)
        {
            // A failed update check (offline, GitHub rate limit, etc.) must never
            // block or crash the app — it just tries again on the next launch. The
            // message is returned so a manual check can show why it failed; the
            // automatic startup check ignores it.
            return new UpdateResult(UpdateOutcome.Failed, Error: ex.Message);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    /// <summary>
    /// Closes the app so the already-armed updater (see <see cref="CheckForUpdatesAsync"/>)
    /// can apply the update and relaunch Claudium automatically.
    /// </summary>
    public static void RestartNow()
    {
        if (_armedManager == null)
        {
            return;
        }

        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
