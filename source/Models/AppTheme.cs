namespace Claudium.Models;

/// <summary>
/// The app's single, fixed color scheme for native WinUI chrome and the xterm.js terminal
/// palette. There is no theme picker — every workspace and the app chrome always use these
/// same values.
/// </summary>
public static class AppTheme
{
    public const string PanelBackgroundHex = "#E01B2638";
    public const string TerminalBackgroundHex = "#1B2638";
    public const string TerminalForegroundHex = "#D5DEEE";
    public const string TerminalCursorHex = "#58D0AA";
    public const string TerminalSelectionRgba = "rgba(103, 146, 225, 0.28)";
}
