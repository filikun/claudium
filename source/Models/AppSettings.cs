namespace Claudium.Models;

/// <summary>
/// App-wide, non-workspace-specific settings persisted between launches.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Id of the <see cref="AppTheme"/> used for native chrome and for any
    /// workspace that hasn't set its own <see cref="WorkspaceProfile.ThemeId"/>.</summary>
    public string ThemeId { get; set; } = AppThemes.DefaultThemeId;
}
