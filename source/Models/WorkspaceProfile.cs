using System;

namespace Claudium.Models;

/// <summary>
/// A saved (or ad-hoc) directory that Claude Code can be launched in.
/// The optional fields exist so future start profiles (different plugin dirs,
/// permission modes, extra CLI flags, ...) can be added without another storage migration.
/// </summary>
public sealed class WorkspaceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Windows path to the working directory Claude Code is launched in.</summary>
    public string Path { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    /// <summary>Windows path passed to Claude Code as --plugin-dir, if set.</summary>
    public string? PluginDir { get; set; }

    /// <summary>Value passed to Claude Code as --permission-mode, if set.</summary>
    public string? PermissionMode { get; set; }

    /// <summary>Value passed to Claude Code as --model, if set (e.g. "opus", "sonnet", "fable").</summary>
    public string? Model { get; set; }

    /// <summary>Value passed to Claude Code as --effort, if set (low, medium, high, xhigh, max).</summary>
    public string? Effort { get; set; }

    /// <summary>Raw extra CLI flags appended verbatim to the claude invocation.</summary>
    public string? ExtraArgs { get; set; }

    /// <summary>
    /// Id of the <see cref="Claudium.Models.AppTheme"/> to use for this workspace's
    /// terminal colors. Null means "use the app-wide default theme".
    /// </summary>
    public string? ThemeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? LastUsedAt { get; set; }
}
