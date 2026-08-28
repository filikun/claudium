using System;
using System.IO;

namespace Claudium.Services;

/// <summary>
/// Resolves where Claudium's own user data (settings, workspaces) lives. Kept in a
/// "UserData" subfolder separate from %LocalAppData%\Claudium\ itself — that top-level
/// folder is owned by Velopack (current\, packages\, Update.exe, Claudium.exe) and its
/// contents get replaced wholesale on update/uninstall, so app data doesn't belong there
/// directly.
/// </summary>
internal static class UserDataPaths
{
    private static readonly string ClaudiumRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Claudium");

    private static readonly string Root = Path.Combine(ClaudiumRoot, "UserData");

    /// <summary>
    /// Resolves the path for <paramref name="fileName"/> under UserData\, migrating a
    /// flat file left over from installs that predate this subfolder (it sat directly in
    /// %LocalAppData%\Claudium\) so upgrading doesn't silently reset the user's settings.
    /// </summary>
    public static string ResolveFile(string fileName)
    {
        string newPath = Path.Combine(Root, fileName);
        if (File.Exists(newPath))
        {
            return newPath;
        }

        string legacyPath = Path.Combine(ClaudiumRoot, fileName);
        if (File.Exists(legacyPath))
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.Move(legacyPath, newPath);
            }
            catch
            {
                // Migration failing shouldn't block startup — worst case the user's
                // settings/workspaces reset to defaults once.
            }
        }

        return newPath;
    }
}
