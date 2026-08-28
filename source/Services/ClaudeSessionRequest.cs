namespace Claudium.Services;

/// <summary>
/// Wire payload sent to terminal-helper.js (as a base64-encoded JSON "open:" line)
/// describing how to launch Claude Code for this session. claude.exe is spawned natively
/// on Windows, so paths are used as-is — no WSL path translation.
/// </summary>
public sealed class ClaudeSessionRequest
{
    public string WindowsPath { get; set; } = string.Empty;

    public string? PluginDirWindowsPath { get; set; }

    public string? PermissionMode { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public string? ExtraArgs { get; set; }
}
