using System.Collections.Generic;
using System.Linq;

namespace Claudium.Models;

/// <summary>
/// A curated color scheme for both the native WinUI chrome (launcher, tab strip, overlays)
/// and the xterm.js terminal palette. Fixed, hand-picked set — no custom/user-defined themes.
/// </summary>
public sealed class AppTheme
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>Root page / main chrome background.</summary>
    public string PageBackgroundHex { get; init; } = string.Empty;

    /// <summary>Tab strip and launcher-adjacent panel background (slightly darker than page).</summary>
    public string PanelBackgroundHex { get; init; } = string.Empty;

    /// <summary>Accent used for buttons and the active-tab underline. Chosen for contrast with white text.</summary>
    public string AccentHex { get; init; } = string.Empty;

    /// <summary>xterm.js terminal background.</summary>
    public string TerminalBackgroundHex { get; init; } = string.Empty;

    /// <summary>xterm.js terminal foreground (default text color).</summary>
    public string TerminalForegroundHex { get; init; } = string.Empty;

    /// <summary>xterm.js cursor color.</summary>
    public string TerminalCursorHex { get; init; } = string.Empty;

    /// <summary>xterm.js selection background, as an rgba(...) CSS color string.</summary>
    public string TerminalSelectionRgba { get; init; } = string.Empty;
}

/// <summary>
/// The fixed catalog of themes available in the app. To add a theme, add an entry here —
/// no other code needs to change (the launcher's picker and each workspace row's picker are
/// generated from <see cref="All"/>).
/// </summary>
public static class AppThemes
{
    public const string DefaultThemeId = "dark_blue";

    public static readonly IReadOnlyList<AppTheme> All = new List<AppTheme>
    {
        new AppTheme
        {
            Id = "dark_blue",
            Name = "Mörkblå",
            PageBackgroundHex = "#272C44",
            PanelBackgroundHex = "#1D2238",
            AccentHex = "#3D8BFD",
            TerminalBackgroundHex = "#272C44",
            TerminalForegroundHex = "#E8ECF5",
            TerminalCursorHex = "#58D0AA",
            TerminalSelectionRgba = "rgba(109, 125, 189, 0.32)"
        },
        new AppTheme
        {
            Id = "near_black",
            Name = "Kolsvart",
            PageBackgroundHex = "#161616",
            PanelBackgroundHex = "#101010",
            AccentHex = "#3E76C4",
            TerminalBackgroundHex = "#141414",
            TerminalForegroundHex = "#E6E6E6",
            TerminalCursorHex = "#5EC2FF",
            TerminalSelectionRgba = "rgba(150, 150, 150, 0.28)"
        },
        new AppTheme
        {
            Id = "warm_amber",
            Name = "Bärnsten",
            PageBackgroundHex = "#241C14",
            PanelBackgroundHex = "#1B140F",
            AccentHex = "#D97706",
            TerminalBackgroundHex = "#231A12",
            TerminalForegroundHex = "#F3E6D0",
            TerminalCursorHex = "#FFB454",
            TerminalSelectionRgba = "rgba(255, 169, 77, 0.25)"
        },
        new AppTheme
        {
            Id = "deep_teal",
            Name = "Djuphav",
            PageBackgroundHex = "#0E2A2E",
            PanelBackgroundHex = "#0A2124",
            AccentHex = "#0F9488",
            TerminalBackgroundHex = "#0D2528",
            TerminalForegroundHex = "#DFF7F3",
            TerminalCursorHex = "#2DD4BF",
            TerminalSelectionRgba = "rgba(45, 212, 191, 0.22)"
        },
        new AppTheme
        {
            Id = "deep_violet",
            Name = "Violett",
            PageBackgroundHex = "#241A33",
            PanelBackgroundHex = "#1B1327",
            AccentHex = "#7C5CFF",
            TerminalBackgroundHex = "#221930",
            TerminalForegroundHex = "#EFE7FA",
            TerminalCursorHex = "#C9A8FF",
            TerminalSelectionRgba = "rgba(180, 140, 255, 0.25)"
        }
    }.AsReadOnly();

    public static AppTheme Default => All.First(t => t.Id == DefaultThemeId);

    public static AppTheme Resolve(string? themeId)
    {
        if (string.IsNullOrEmpty(themeId))
        {
            return Default;
        }

        return All.FirstOrDefault(t => t.Id == themeId) ?? Default;
    }
}
