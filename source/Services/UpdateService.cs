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

            // Applies on next restart rather than forcing one now, so an update
            // never yanks the window away from the user mid-session.
            manager.WaitExitThenApplyUpdates(newVersion, restart: false);
        }
        catch
        {
            // A failed update check (offline, GitHub rate limit, etc.) must never
            // block or crash the app — it just tries again on the next launch.
        }
    }
}
