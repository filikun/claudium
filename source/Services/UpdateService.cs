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

    /// <summary>
    /// Raised off the UI thread once a downloaded update is armed and ready — the update
    /// applies automatically the next time this process exits, whether that's the user
    /// clicking "restart now" (<see cref="RestartNow"/>) or just closing the app normally.
    /// The argument is the new version (e.g. "1.1.6").
    /// </summary>
    public static event Action<string>? UpdateReady;

    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!manager.IsInstalled)
            {
                return;
            }

            var newVersion = await manager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(newVersion);

            // Arms the updater to apply + relaunch automatically on the next process
            // exit — restart:true so a plain close (not just the explicit button) also
            // picks up the update rather than requiring the user to notice and act.
            manager.WaitExitThenApplyUpdates(newVersion, silent: false, restart: true);
            _armedManager = manager;

            UpdateReady?.Invoke(newVersion.TargetFullRelease.Version.ToString());
        }
        catch
        {
            // A failed update check (offline, GitHub rate limit, etc.) must never
            // block or crash the app — it just tries again on the next launch.
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
