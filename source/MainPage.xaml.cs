using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.UI;
using Windows.UI.Text;
using Claudium.Models;
using Claudium.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Claudium;

/// <summary>
/// Display row for the workspace list. Kept separate from <see cref="WorkspaceProfile"/>
/// so the XAML DataTemplate only ever sees plain, pre-formatted values.
/// </summary>
/// <summary>Display option for a theme picker ComboBox (Id/Name pair).</summary>
public sealed class ThemeOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class WorkspaceListItem
{
    /// <summary>
    /// Builds the theme option list, with the "use app default" entry labeled with the
    /// app's currently resolved default theme name (rather than a bare "Standard") so the
    /// picker always shows what value will actually be used.
    /// </summary>
    public static IReadOnlyList<ThemeOption> BuildThemeOptions(string appDefaultThemeName)
    {
        return new List<ThemeOption>
        {
            new ThemeOption { Id = string.Empty, Name = "App-standard (" + appDefaultThemeName + ")" },
            new ThemeOption { Id = "dark_blue", Name = "Mörkblå" },
            new ThemeOption { Id = "near_black", Name = "Kolsvart" },
            new ThemeOption { Id = "warm_amber", Name = "Bärnsten" },
            new ThemeOption { Id = "deep_teal", Name = "Djuphav" },
            new ThemeOption { Id = "deep_violet", Name = "Violett" }
        }.AsReadOnly();
    }

    /// <summary>Set once per <see cref="MainPage.RefreshWorkspaceList"/> call so x:Bind can see it.</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; set; } = Array.Empty<ThemeOption>();

    public IReadOnlyList<ThemeOption> PermissionModeOptions => ClaudeOptions.PermissionModeOptions;
    public IReadOnlyList<ThemeOption> ModelOptions => ClaudeOptions.ModelOptions;
    public IReadOnlyList<ThemeOption> EffortOptions => ClaudeOptions.EffortOptions;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string StarGlyph { get; set; } = "☆";
    public string ThemeId { get; set; } = string.Empty;
    public string PermissionMode { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Effort { get; set; } = string.Empty;
    public Brush StartButtonBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xFF, 0x3D, 0x8B, 0xFD));
}

/// <summary>Shared option lists for the permission-mode/model/effort pickers (launcher row and live tab-bar switcher).</summary>
public static class ClaudeOptions
{
    public static readonly IReadOnlyList<ThemeOption> PermissionModeOptions = new List<ThemeOption>
    {
        new ThemeOption { Id = string.Empty, Name = "Claude Codes standard" },
        new ThemeOption { Id = "auto", Name = "Auto" },
        new ThemeOption { Id = "acceptEdits", Name = "Acceptera ändringar" },
        new ThemeOption { Id = "plan", Name = "Plan" },
        new ThemeOption { Id = "dontAsk", Name = "Fråga inte" },
        new ThemeOption { Id = "bypassPermissions", Name = "Hoppa över behörigheter" },
        new ThemeOption { Id = "manual", Name = "Manuell" }
    }.AsReadOnly();

    public static readonly IReadOnlyList<ThemeOption> ModelOptions = new List<ThemeOption>
    {
        new ThemeOption { Id = string.Empty, Name = "Claude Codes standard" },
        new ThemeOption { Id = "sonnet", Name = "Sonnet" },
        new ThemeOption { Id = "opus", Name = "Opus" },
        new ThemeOption { Id = "fable", Name = "Fable" }
    }.AsReadOnly();

    public static readonly IReadOnlyList<ThemeOption> EffortOptions = new List<ThemeOption>
    {
        new ThemeOption { Id = string.Empty, Name = "Claude Codes standard" },
        new ThemeOption { Id = "low", Name = "Low" },
        new ThemeOption { Id = "medium", Name = "Medium" },
        new ThemeOption { Id = "high", Name = "High" },
        new ThemeOption { Id = "xhigh", Name = "X-High" },
        new ThemeOption { Id = "max", Name = "Max" }
    }.AsReadOnly();
}

/// <summary>
/// A tab's Claude activity, driven by Claude Code hooks (UserPromptSubmit/Stop/Notification)
/// relayed through the terminal helper, with a couple of local heuristics (see
/// <see cref="MainPage.SetSessionStatus"/>) filling the gaps between hook firings.
/// </summary>
public enum SessionActivityStatus
{
    /// <summary>Not doing anything; waiting for the user to type a new prompt.</summary>
    Idle,

    /// <summary>Claude is actively generating a response or running a tool.</summary>
    Working,

    /// <summary>Blocked on a permission prompt or other input Claude needs from the user.</summary>
    Waiting
}

/// <summary>
/// Display row for the tab strip. Kept separate from <see cref="TerminalSessionInfo"/>
/// so the XAML DataTemplate only ever sees plain, pre-formatted values.
/// </summary>
public sealed class TerminalTabItem
{
    private static readonly Brush TransparentBrush = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    private static readonly Brush WorkingBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x7E, 0xDB, 0xFF));
    private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xD7, 0x87));
    private static readonly FontWeight NormalWeight = new() { Weight = 400 };
    private static readonly FontWeight SemiBoldWeight = new() { Weight = 600 };

    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Brush Background { get; set; } = TransparentBrush;
    public Brush AccentBarBrush { get; set; } = TransparentBrush;
    public FontWeight NameFontWeight { get; set; } = NormalWeight;
    public Visibility StatusDotVisibility { get; set; } = Visibility.Collapsed;
    public Brush StatusDotBrush { get; set; } = TransparentBrush;
    public string StatusDotAnimation { get; set; } = "static";
    public string StatusDotTooltip { get; set; } = string.Empty;

    /// <summary>Parses a "#RRGGBB" hex string into a <see cref="Color"/>, optionally overriding alpha.</summary>
    public static Color ParseHex(string hex, byte alpha = 0xFF)
    {
        string value = hex.TrimStart('#');
        byte r = Convert.ToByte(value.Substring(0, 2), 16);
        byte g = Convert.ToByte(value.Substring(2, 2), 16);
        byte b = Convert.ToByte(value.Substring(4, 2), 16);
        return Color.FromArgb(alpha, r, g, b);
    }

    public static TerminalTabItem For(
        string sessionId,
        string name,
        bool isActive,
        SessionActivityStatus status,
        bool showDoneFlash,
        string workspaceAccentHex)
    {
        var item = new TerminalTabItem
        {
            SessionId = sessionId,
            Name = name,
            Background = isActive ? new SolidColorBrush(ParseHex(workspaceAccentHex, 0x30)) : TransparentBrush,
            // Every tab shows its own project's accent color, not just the active one — dimmer
            // when inactive so the active tab still stands out, but never fully transparent,
            // so a tab's project stays visually identifiable after switching away from it.
            AccentBarBrush = new SolidColorBrush(ParseHex(workspaceAccentHex, isActive ? (byte)0xFF : (byte)0x70)),
            NameFontWeight = isActive ? SemiBoldWeight : NormalWeight
        };

        switch (status)
        {
            case SessionActivityStatus.Working:
                item.StatusDotVisibility = Visibility.Visible;
                item.StatusDotBrush = WorkingBrush;
                item.StatusDotAnimation = "pulse";
                item.StatusDotTooltip = "Arbetar";
                break;
            case SessionActivityStatus.Waiting:
                item.StatusDotVisibility = Visibility.Visible;
                item.StatusDotBrush = (Brush)Application.Current.Resources["AppFavoriteBrush"];
                item.StatusDotAnimation = "pulse";
                item.StatusDotTooltip = "Väntar på svar";
                break;
            default:
                if (showDoneFlash)
                {
                    item.StatusDotVisibility = Visibility.Visible;
                    item.StatusDotBrush = DoneBrush;
                    item.StatusDotAnimation = "static";
                    item.StatusDotTooltip = "Färdig";
                }
                break;
        }

        return item;
    }
}

/// <summary>
/// Runtime (non-persisted) state for one open tab: which workspace it runs in, and the
/// last status text the helper reported for it (shown if this tab is re-activated while
/// its session is still starting or has failed).
/// </summary>
public sealed class TerminalSessionInfo
{
    public Guid SessionId { get; set; }
    public WorkspaceProfile Profile { get; set; } = null!;
    public string? LastStatus { get; set; }
    public SessionActivityStatus Status { get; set; } = SessionActivityStatus.Idle;

    /// <summary>
    /// True for a few seconds right after transitioning to <see cref="SessionActivityStatus.Idle"/>
    /// from a non-idle state, so the tab briefly shows a "done" dot instead of just going quiet.
    /// Cleared by the timer started in <see cref="MainPage.SetSessionStatus"/>.
    /// </summary>
    public bool ShowDoneFlash { get; set; }

    /// <summary>
    /// Model/effort this session is actually running with — seeded from the launch
    /// profile, then kept in sync (optimistically) whenever changed via the live
    /// tab-bar switcher, so the flyout can show what's active instead of a blank picker.
    /// </summary>
    public string? CurrentModel { get; set; }
    public string? CurrentEffort { get; set; }

    /// <summary>
    /// True once the terminal helper has reported this session's xterm view as visible
    /// (its "visible:" message). Drives whether <see cref="MainPage.LoadingOverlay"/> —
    /// a single control shared across all tabs, not one per session — should be shown
    /// when this session becomes the active tab.
    /// </summary>
    public bool IsReady { get; set; }
}

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WorkspaceStore _workspaceStore = new();
    private List<WorkspaceProfile> _workspaces = new();

    private readonly AppSettingsStore _appSettingsStore = new();
    private AppSettings _appSettings = new();
    private bool _suppressAppThemeSelectionEvent;

    private Process? _helperProcess;
    private StreamWriter? _helperInput;
    private bool _webReady;
    private WorkspaceProfile? _queuedProfile;
    private DateTimeOffset? _lastUsageUpdatedAt;
    private DispatcherTimer? _usageRefreshTimer;

    private readonly Dictionary<Guid, TerminalSessionInfo> _sessions = new();
    private readonly List<Guid> _sessionOrder = new();
    private const string DefaultModel = "sonnet";
    private const string DefaultEffort = "medium";
    private const string DefaultPermissionMode = "auto";

    private Guid? _activeSessionId;
    private bool _suppressTabSelectionEvent;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
        SizeChanged += MainPage_SizeChanged;
        SetupUsageRefreshTimer();

        Services.UpdateService.UpdateReady += OnAppUpdateReady;

        _appSettings = _appSettingsStore.Load();
        _workspaces = _workspaceStore.Load();
        BackfillDefaultClaudeSettings();

        _suppressAppThemeSelectionEvent = true;
        AppThemeComboBox.SelectedValue = _appSettings.ThemeId;
        _suppressAppThemeSelectionEvent = false;

        ApplyAppTheme();
        ShowVersionInfo();
    }

    /// <summary>
    /// The version bumped in Claudium.csproj plus the running DLL's own last-write time —
    /// together they let you confirm at a glance, from the launcher page, that a change
    /// actually made it into the build currently running (see publish.ps1).
    /// </summary>
    private void ShowVersionInfo()
    {
        try
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionText = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "okänd";

            // Assembly.Location is unreliable for this deployment model (came back empty
            // in testing, silently blanking this out) — AppContext.BaseDirectory + the
            // known dll name is what the rest of this file already uses to find its own
            // files (see the Assets\Terminal path below) and works consistently here.
            string dllPath = Path.Combine(AppContext.BaseDirectory, "Claudium.dll");
            string buildStamp = File.Exists(dllPath)
                ? File.GetLastWriteTime(dllPath).ToString("yyyy-MM-dd HH:mm")
                : "okänt datum";
            VersionText.Text = $"Claudium v{versionText} · byggd {buildStamp}";
        }
        catch (Exception)
        {
            VersionText.Text = string.Empty;
        }
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;
        _ = RunUpdateCheckAsync(isAutomatic: true);
        await InitializeTerminalAsync();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Services.UpdateService.UpdateReady -= OnAppUpdateReady;
        StopHelper();
    }

    /// <summary>
    /// Fires off the UI thread (UpdateService runs the check/download in the background),
    /// so the banner itself is shown via DispatcherQueue.
    /// </summary>
    private void OnAppUpdateReady(string newVersion)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AppUpdateBannerText.Text = $"Claudium {newVersion} har laddats ner och är redo. Starta om för att uppdatera.";
            AppUpdateBanner.Visibility = Visibility.Visible;
        });
    }

    private void AppUpdateRestartButton_Click(object sender, RoutedEventArgs e)
    {
        Services.UpdateService.RestartNow();
    }

    private void AppUpdateDismissButton_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = ForceTerminalFitAsync();
    }

    private async System.Threading.Tasks.Task InitializeTerminalAsync()
    {
        try
        {
            string browserVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(browserVersion))
            {
                UpdateStatus("WebView2-runtime hittades inte.");
                return;
            }

            await TerminalView.EnsureCoreWebView2Async();
            TerminalView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            TerminalView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            TerminalView.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Auto-grant clipboard-read to our own local content — needed so terminal.html
            // can pull an image straight off the clipboard on Ctrl+V (see attachfile: below).
            // Without this WebView2 would show a permission prompt on first paste.
            TerminalView.CoreWebView2.PermissionRequested += (_, permissionArgs) =>
            {
                if (permissionArgs.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
                {
                    permissionArgs.State = CoreWebView2PermissionState.Allow;
                }
            };

            string terminalFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal");
            TerminalView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "terminal.local",
                terminalFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            TerminalView.Source = new Uri("https://terminal.local/terminal.html");
        }
        catch (Exception ex)
        {
            UpdateStatus("WebView2 kunde inte starta: " + ex.Message);
        }
    }

    private void CoreWebView2_WebMessageReceived(Microsoft.UI.Xaml.Controls.WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string message = args.TryGetWebMessageAsString();
        if (message == "ready")
        {
            _webReady = true;

            if (_queuedProfile != null)
            {
                WorkspaceProfile profile = _queuedProfile;
                _queuedProfile = null;
                OpenNewTab(profile);
            }

            return;
        }

        if (message.StartsWith("input:", StringComparison.Ordinal))
        {
            if (TrySplitSessionMessage(message, 6, out Guid sessionId, out string _) &&
                _sessions.TryGetValue(sessionId, out TerminalSessionInfo? session) &&
                session.Status == SessionActivityStatus.Waiting)
            {
                SetSessionStatus(sessionId, SessionActivityStatus.Working);
            }

            WriteHelperLine(message);
            return;
        }

        if (message.StartsWith("init:", StringComparison.Ordinal) ||
            message.StartsWith("resize:", StringComparison.Ordinal))
        {
            WriteHelperLine(message);
        }

        if (message.StartsWith("attachfile:", StringComparison.Ordinal))
        {
            HandleAttachFileMessage(message);
            return;
        }

        if (message.StartsWith("visible:", StringComparison.Ordinal))
        {
            string sessionIdText = message.Substring("visible:".Length);
            if (Guid.TryParse(sessionIdText, out Guid sessionId) &&
                _sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
            {
                session.IsReady = true;
                if (sessionId == _activeSessionId)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    // ----- Image/file drop & clipboard-paste attachments -----

    // Claude CLI can't take image bytes directly, but its Read tool can load an image
    // given a local file path — so a dropped/pasted image is saved to disk and its path
    // is typed into the terminal (like a normal paste), for the model to read on request.
    private const long MaxAttachmentBytes = 25 * 1024 * 1024;

    /// <summary>Parses "attachfile:&lt;sessionId&gt;:&lt;base64 filename&gt;:&lt;base64 bytes&gt;".</summary>
    private void HandleAttachFileMessage(string message)
    {
        if (!TrySplitSessionMessage(message, "attachfile:".Length, out Guid sessionId, out string remainder) ||
            !_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
        {
            return;
        }

        int separatorIndex = remainder.IndexOf(':');
        if (separatorIndex < 0)
        {
            return;
        }

        string fileName;
        byte[] fileBytes;
        try
        {
            fileName = Encoding.UTF8.GetString(Convert.FromBase64String(remainder.Substring(0, separatorIndex)));
            fileBytes = Convert.FromBase64String(remainder.Substring(separatorIndex + 1));
        }
        catch (FormatException)
        {
            return;
        }

        if (fileBytes.Length == 0 || fileBytes.Length > MaxAttachmentBytes)
        {
            return;
        }

        string filePath;
        try
        {
            string attachmentsDir = Path.Combine(Path.GetTempPath(), "Claudium", "attachments");
            Directory.CreateDirectory(attachmentsDir);
            filePath = Path.Combine(attachmentsDir, Guid.NewGuid().ToString("N") + "_" + SanitizeFileName(fileName));
            File.WriteAllBytes(filePath, fileBytes);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        InsertPathIntoSession(sessionId, session, filePath);
    }

    private static string SanitizeFileName(string fileName)
    {
        string name = string.IsNullOrWhiteSpace(fileName) ? "attachment" : Path.GetFileName(fileName);
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrEmpty(name) ? "attachment" : name;
    }

    /// <summary>Types a file path (quoted if it contains spaces) into the session's prompt, as if pasted.</summary>
    private void InsertPathIntoSession(Guid sessionId, TerminalSessionInfo session, string filePath)
    {
        if (session.Status == SessionActivityStatus.Waiting)
        {
            SetSessionStatus(sessionId, SessionActivityStatus.Working);
        }

        string pathText = filePath.Contains(' ') ? "\"" + filePath + "\"" : filePath;
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(pathText + " "));
        WriteHelperLine("input:" + sessionId.ToString("N") + ":" + payload);
    }

    /// <summary>
    /// Dragging a file from Explorer onto a WebView2 hosted in a WinUI3 desktop app is a
    /// long-standing, unresolved platform bug (the cursor shows "not allowed" regardless of
    /// what the app does — see microsoft/microsoft-ui-xaml#7366 and #10576), so there is no
    /// reliable code-level fix for drag-and-drop here. A native file picker is the reliable
    /// substitute: it inserts the picked file's path into the prompt exactly like a paste.
    /// </summary>
    private void AttachFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSessionId is not Guid sessionId || !_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
        {
            return;
        }

        IntPtr hwnd = App.CurrentWindow != null
            ? WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow)
            : IntPtr.Zero;

        string? filePath = NativeFolderPicker.PickFile(hwnd);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        InsertPathIntoSession(sessionId, session, filePath);
    }

    // ----- Workspace list (launcher) -----

    private void RefreshWorkspaceList()
    {
        AppTheme appDefaultTheme = AppThemes.Resolve(_appSettings.ThemeId);
        var startButtonBrush = new SolidColorBrush(TerminalTabItem.ParseHex(appDefaultTheme.AccentHex));
        IReadOnlyList<ThemeOption> themeOptions = WorkspaceListItem.BuildThemeOptions(appDefaultTheme.Name);

        List<WorkspaceListItem> items = _workspaces
            .OrderByDescending(w => w.IsFavorite)
            .ThenByDescending(w => w.LastUsedAt ?? w.CreatedAt)
            .Select(w => new WorkspaceListItem
            {
                Id = w.Id,
                Name = w.Name,
                Path = w.Path,
                StarGlyph = w.IsFavorite ? "★" : "☆",
                ThemeId = w.ThemeId ?? string.Empty,
                ThemeOptions = themeOptions,
                PermissionMode = w.PermissionMode ?? string.Empty,
                Model = w.Model ?? string.Empty,
                Effort = w.Effort ?? string.Empty,
                StartButtonBrush = startButtonBrush
            })
            .ToList();

        WorkspaceListView.ItemsSource = items;
        EmptyStateText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AppThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAppThemeSelectionEvent)
        {
            return;
        }

        if (AppThemeComboBox.SelectedValue is string themeId && themeId != _appSettings.ThemeId)
        {
            _appSettings.ThemeId = themeId;
            _appSettingsStore.Save(_appSettings);
            ApplyAppTheme();
        }
    }

    private bool _updateCheckInProgress;

    private enum UpdateResultKind
    {
        Neutral,
        Updated,
        Error
    }

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunUpdateCheckAsync(isAutomatic: false);
    }

    /// <summary>
    /// Runs "claude update" and reflects progress/result in the status chip. On automatic
    /// (startup) runs, only an actual update or an in-progress check is worth surfacing —
    /// "already up to date" and transient errors (e.g. offline) stay silent so the launcher
    /// doesn't nag on every app start.
    /// </summary>
    private async System.Threading.Tasks.Task RunUpdateCheckAsync(bool isAutomatic)
    {
        if (_updateCheckInProgress)
        {
            return;
        }

        _updateCheckInProgress = true;
        CheckForUpdatesButton.IsEnabled = false;
        ShowUpdateStatus("Söker efter uppdateringar...", UpdateResultKind.Neutral, showSpinner: true);

        // Deliberately a standalone one-off process, independent of _helperProcess (which is
        // shared across all open terminal tabs) — this must never touch a running session.
        var psi = new ProcessStartInfo
        {
            FileName = "claude.exe",
            ArgumentList = { "update" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var errorOutput = new StringBuilder();
        var exitedTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                errorOutput.AppendLine(args.Data);
            }
        };
        process.Exited += (_, _) => exitedTcs.TrySetResult(true);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _updateCheckInProgress = false;
            CheckForUpdatesButton.IsEnabled = true;
            ShowUpdateStatus("Kunde inte starta uppdateringen: " + ex.Message, UpdateResultKind.Error, showSpinner: false);
            process.Dispose();
            return;
        }

        await exitedTcs.Task;

        _updateCheckInProgress = false;
        CheckForUpdatesButton.IsEnabled = true;

        if (process.ExitCode == 0)
        {
            // claude update's own stdout is not fit to show directly — its "last line"
            // can be the raw winget command it ran rather than a result sentence (winget's
            // progress-bar output doesn't redirect cleanly either), so a plain status
            // sentence is synthesized here instead, pulling out a version number if one
            // is present anywhere in the output.
            string combinedOutput = output.ToString();
            bool alreadyUpToDate = combinedOutput.Contains("up to date", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("already", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("redan", StringComparison.OrdinalIgnoreCase);

            if (alreadyUpToDate)
            {
                if (isAutomatic)
                {
                    HideUpdateStatus();
                }
                else
                {
                    ShowUpdateStatus("Claude CLI är redan uppdaterad.", UpdateResultKind.Neutral, showSpinner: false);
                    _ = FadeOutUpdateStatusAfterDelayAsync();
                }
            }
            else
            {
                Match versionMatch = Regex.Match(combinedOutput, @"(?<![\w.])\d+\.\d+\.\d+(?![\w.])");
                string message = versionMatch.Success
                    ? $"Claude CLI uppdaterad till version {versionMatch.Value}."
                    : "Claude CLI har uppdaterats.";
                ShowUpdateStatus(message, UpdateResultKind.Updated, showSpinner: false);
            }
        }
        else if (isAutomatic)
        {
            HideUpdateStatus();
        }
        else
        {
            string errorText = errorOutput.ToString().Trim();
            if (string.IsNullOrEmpty(errorText))
            {
                errorText = "Kunde inte uppdatera Claude CLI.";
            }
            ShowUpdateStatus(errorText, UpdateResultKind.Error, showSpinner: false);
        }

        process.Dispose();
    }

    private void ShowUpdateStatus(string text, UpdateResultKind kind, bool showSpinner)
    {
        UpdateStatusChip.Visibility = Visibility.Visible;
        UpdateStatusText.Text = text;

        UpdateProgressRing.IsActive = showSpinner;
        UpdateProgressRing.Visibility = showSpinner ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatusIcon.Visibility = showSpinner ? Visibility.Collapsed : Visibility.Visible;

        Brush color = kind switch
        {
            UpdateResultKind.Updated => (Brush)Application.Current.Resources["AppSuccessBrush"],
            UpdateResultKind.Error => new SolidColorBrush(Color.FromArgb(255, 0xE3, 0x8C, 0x8C)),
            _ => new SolidColorBrush(Color.FromArgb(255, 0x90, 0x99, 0xBC))
        };

        UpdateStatusText.Foreground = color;
        UpdateStatusIcon.Foreground = color;
        UpdateStatusIcon.Glyph = kind switch
        {
            UpdateResultKind.Updated => "", // checkmark
            UpdateResultKind.Error => "", // error
            _ => "" // info
        };
    }

    private void HideUpdateStatus()
    {
        UpdateStatusChip.Visibility = Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task FadeOutUpdateStatusAfterDelayAsync()
    {
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
        if (!_updateCheckInProgress)
        {
            HideUpdateStatus();
        }
    }

    private void WorkspaceThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.Tag is not string workspaceId)
        {
            return;
        }

        WorkspaceProfile? profile = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (profile == null)
        {
            return;
        }

        string selectedThemeId = comboBox.SelectedValue as string ?? string.Empty;
        string? newThemeId = string.IsNullOrEmpty(selectedThemeId) ? null : selectedThemeId;
        if (profile.ThemeId == newThemeId)
        {
            return;
        }

        profile.ThemeId = newThemeId;
        SaveWorkspaces();

        foreach (KeyValuePair<Guid, TerminalSessionInfo> entry in _sessions)
        {
            if (entry.Value.Profile.Id == workspaceId)
            {
                PushSessionTheme(entry.Key, ResolveWorkspaceTheme(entry.Value.Profile));
            }
        }

        ApplyActiveTheme();
        RefreshTabStrip();
    }

    private void WorkspacePermissionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.Tag is not string workspaceId)
        {
            return;
        }

        WorkspaceProfile? profile = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (profile == null)
        {
            return;
        }

        string selected = comboBox.SelectedValue as string ?? string.Empty;
        profile.PermissionMode = string.IsNullOrEmpty(selected) ? null : selected;
        SaveWorkspaces();
    }

    private void WorkspaceModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.Tag is not string workspaceId)
        {
            return;
        }

        WorkspaceProfile? profile = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (profile == null)
        {
            return;
        }

        string selected = comboBox.SelectedValue as string ?? string.Empty;
        profile.Model = string.IsNullOrEmpty(selected) ? null : selected;
        SaveWorkspaces();
    }

    private void WorkspaceEffortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.Tag is not string workspaceId)
        {
            return;
        }

        WorkspaceProfile? profile = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (profile == null)
        {
            return;
        }

        string selected = comboBox.SelectedValue as string ?? string.Empty;
        profile.Effort = string.IsNullOrEmpty(selected) ? null : selected;
        SaveWorkspaces();
    }

    private AppTheme ResolveWorkspaceTheme(WorkspaceProfile profile)
    {
        string themeId = string.IsNullOrEmpty(profile.ThemeId) ? _appSettings.ThemeId : profile.ThemeId;
        return AppThemes.Resolve(themeId);
    }

    private void PushSessionTheme(Guid sessionId, AppTheme theme)
    {
        string id = sessionId.ToString("N");
        string script = "window.appSetSessionTheme('" + id + "', '" +
            EscapeForJavaScript(theme.TerminalBackgroundHex) + "', '" +
            EscapeForJavaScript(theme.TerminalForegroundHex) + "', '" +
            EscapeForJavaScript(theme.TerminalCursorHex) + "', '" +
            EscapeForJavaScript(theme.TerminalSelectionRgba) + "');";
        _ = ExecuteScriptAsync(script);
    }

    /// <summary>
    /// Applies the selected AppTheme as the app-wide default. Rather than replacing brushes
    /// on individual elements (which silently misses anything added later), this mutates the
    /// shared brush resources in App.xaml in place — every element bound via
    /// {StaticResource AppXyzBrush} repaints automatically, including inside flyouts.
    /// The actual chrome (header, loading page, title bar) still ends up showing whichever
    /// tab is active — see <see cref="ApplyActiveTheme"/>, called at the end of this — so a
    /// tab with its own per-workspace theme override isn't stomped by a global theme change.
    /// </summary>
    private void ApplyAppTheme()
    {
        AppTheme theme = AppThemes.Resolve(_appSettings.ThemeId);

        RefreshWorkspaceList();
        RefreshTabStrip();

        foreach (KeyValuePair<Guid, TerminalSessionInfo> entry in _sessions)
        {
            if (string.IsNullOrEmpty(entry.Value.Profile.ThemeId))
            {
                PushSessionTheme(entry.Key, theme);
            }
        }

        ApplyActiveTheme();
    }

    /// <summary>
    /// Repaints the shared chrome brushes (header/tab-strip background, buttons, the
    /// "Startar Claude Code..." loading page, native title bar) to match whichever tab is
    /// currently active — its own per-workspace theme override if it has one, otherwise the
    /// app-wide default — or the app-wide default when no tab is open (showing the launcher).
    /// Called whenever the active tab changes (open/switch/close) and whenever a theme
    /// (app-wide or per-workspace) is edited.
    /// </summary>
    private void ApplyActiveTheme()
    {
        AppTheme theme = _activeSessionId is Guid activeId && _sessions.TryGetValue(activeId, out TerminalSessionInfo? activeSession)
            ? ResolveWorkspaceTheme(activeSession.Profile)
            : AppThemes.Resolve(_appSettings.ThemeId);

        SetSharedBrushColor("AppPageBrush", TerminalTabItem.ParseHex(theme.PageBackgroundHex));
        SetSharedBrushColor("AppSurfaceBrush", TerminalTabItem.ParseHex(theme.PanelBackgroundHex));
        SetSharedBrushColor("AppSurfaceOverlayBrush", TerminalTabItem.ParseHex(theme.PanelBackgroundHex, 0xCC));
        SetSharedBrushColor("AppAccentBrush", TerminalTabItem.ParseHex(theme.AccentHex));
        SetSharedBrushColor("AppNeutralButtonBrush", Lighten(TerminalTabItem.ParseHex(theme.PanelBackgroundHex), 0.14));

        (App.CurrentWindow as MainWindow)?.ApplyTitleBarTheme(theme);
    }

    private static void SetSharedBrushColor(string resourceKey, Color color)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out object? value) && value is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private static Color Lighten(Color color, double amount)
    {
        byte Blend(byte channel) => (byte)(channel + (255 - channel) * amount);
        return Color.FromArgb(color.A, Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private WorkspaceProfile? FindWorkspace(object? sender)
    {
        if (sender is not Button button || button.Tag is not string id)
        {
            return null;
        }

        return _workspaces.FirstOrDefault(w => w.Id == id);
    }

    private void SaveWorkspaces()
    {
        _workspaceStore.Save(_workspaces);
    }

    /// <summary>
    /// Older saved workspaces (and any workspace that hasn't had these fields touched)
    /// have null Model/Effort/PermissionMode, which the launcher showed as an opaque
    /// "Claude Codes standard" — leaving it unclear what would actually be used. Fill
    /// them in with the same concrete defaults new workspaces get, so the picker always
    /// shows a real value.
    /// </summary>
    private void BackfillDefaultClaudeSettings()
    {
        bool changed = false;

        foreach (WorkspaceProfile profile in _workspaces)
        {
            if (string.IsNullOrWhiteSpace(profile.Model))
            {
                profile.Model = DefaultModel;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(profile.Effort))
            {
                profile.Effort = DefaultEffort;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(profile.PermissionMode))
            {
                profile.PermissionMode = DefaultPermissionMode;
                changed = true;
            }
        }

        if (changed)
        {
            SaveWorkspaces();
        }
    }

    private void StartWorkspace_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceProfile? profile = FindWorkspace(sender);
        if (profile != null)
        {
            LaunchProfile(profile);
        }
    }

    private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceProfile? profile = FindWorkspace(sender);
        if (profile == null)
        {
            return;
        }

        profile.IsFavorite = !profile.IsFavorite;
        SaveWorkspaces();
        RefreshWorkspaceList();
    }

    private async void RenameWorkspace_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceProfile? profile = FindWorkspace(sender);
        if (profile == null)
        {
            return;
        }

        string? newName = await PromptForNameAsync("Byt namn", profile.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        profile.Name = newName.Trim();
        SaveWorkspaces();
        RefreshWorkspaceList();
        RefreshTabStrip();
    }

    private async void RemoveWorkspace_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceProfile? profile = FindWorkspace(sender);
        if (profile == null)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = this.XamlRoot,
            Title = "Ta bort katalog",
            Content = $"Ta bort \"{profile.Name}\" från sparade kataloger? Själva mappen påverkas inte.",
            PrimaryButtonText = "Ta bort",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _workspaces.RemoveAll(w => w.Id == profile.Id);
        SaveWorkspaces();
        RefreshWorkspaceList();
    }

    private async void AddDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        string? folderPath = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        string suggestedName = new DirectoryInfo(folderPath).Name;
        string? name = await PromptForNameAsync("Namnge katalogen", suggestedName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var profile = new WorkspaceProfile
        {
            Name = name.Trim(),
            Path = folderPath,
            Model = DefaultModel,
            Effort = DefaultEffort,
            PermissionMode = DefaultPermissionMode
        };

        _workspaces.Add(profile);
        SaveWorkspaces();
        RefreshWorkspaceList();
    }

    private async void BrowseAndStartButton_Click(object sender, RoutedEventArgs e)
    {
        string? folderPath = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var adHocProfile = new WorkspaceProfile
        {
            Name = new DirectoryInfo(folderPath).Name,
            Path = folderPath,
            Model = DefaultModel,
            Effort = DefaultEffort,
            PermissionMode = DefaultPermissionMode
        };

        LaunchProfile(adHocProfile);
    }

    private void AddTabButton_Click(object sender, RoutedEventArgs e)
    {
        ShowLauncherForNewTab();
    }

    private void CancelAddTabButton_Click(object sender, RoutedEventArgs e)
    {
        LauncherPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowLauncherForNewTab()
    {
        CancelAddTabButton.Visibility = _sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LauncherPanel.Visibility = Visibility.Visible;
        RefreshWorkspaceList();
    }

    private async System.Threading.Tasks.Task<string?> PromptForNameAsync(string title, string initialValue)
    {
        var textBox = new TextBox
        {
            Text = initialValue,
            PlaceholderText = "Namn"
        };

        ContentDialog dialog = new()
        {
            XamlRoot = this.XamlRoot,
            Title = title,
            Content = textBox,
            PrimaryButtonText = "Spara",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        IntPtr hwnd = App.CurrentWindow != null
            ? WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow)
            : IntPtr.Zero;

        // Windows.Storage.Pickers.FolderPicker (the WinRT picker) throws COMException
        // 0x80004005 in this unpackaged app — it needs package identity we don't have.
        return System.Threading.Tasks.Task.FromResult(NativeFolderPicker.PickFolder(hwnd));
    }

    // ----- Tab lifecycle -----

    private void LaunchProfile(WorkspaceProfile profile)
    {
        profile.LastUsedAt = DateTimeOffset.Now;
        if (_workspaces.Any(w => w.Id == profile.Id))
        {
            SaveWorkspaces();
        }

        LauncherPanel.Visibility = Visibility.Collapsed;

        if (!_webReady)
        {
            _queuedProfile = profile;
            return;
        }

        OpenNewTab(profile);
    }

    private void OpenNewTab(WorkspaceProfile profile)
    {
        Guid sessionId = Guid.NewGuid();
        var session = new TerminalSessionInfo
        {
            SessionId = sessionId,
            Profile = profile,
            CurrentModel = string.IsNullOrWhiteSpace(profile.Model) ? DefaultModel : profile.Model,
            CurrentEffort = string.IsNullOrWhiteSpace(profile.Effort) ? DefaultEffort : profile.Effort
        };
        _sessions[sessionId] = session;
        _sessionOrder.Add(sessionId);

        EnsureHelperRunning();
        SendOpenToHelper(sessionId, profile);

        _activeSessionId = sessionId;
        ApplyActiveTheme();
        StatusOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        RefreshTabStrip();

        AppTheme theme = ResolveWorkspaceTheme(profile);
        string id = sessionId.ToString("N");
        string script = "window.appOpenSession('" + id + "', '" +
            EscapeForJavaScript(theme.TerminalBackgroundHex) + "', '" +
            EscapeForJavaScript(theme.TerminalForegroundHex) + "', '" +
            EscapeForJavaScript(theme.TerminalCursorHex) + "', '" +
            EscapeForJavaScript(theme.TerminalSelectionRgba) + "'); " +
            "window.appSwitchSession('" + id + "');";
        _ = ExecuteScriptAsync(script);
        TerminalView.Focus(FocusState.Programmatic);
    }

    private void SwitchToTab(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
        {
            return;
        }

        _activeSessionId = sessionId;
        ApplyActiveTheme();
        LauncherPanel.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = session.IsReady ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrEmpty(session.LastStatus))
        {
            StatusOverlay.Visibility = Visibility.Visible;
            UpdateStatus(session.LastStatus);
        }
        else
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
        }

        _ = ExecuteScriptAsync("window.appSwitchSession('" + sessionId.ToString("N") + "');");
        TerminalView.Focus(FocusState.Programmatic);
        RefreshTabStrip();
    }

    private void CloseTab(Guid sessionId)
    {
        if (!_sessions.Remove(sessionId))
        {
            return;
        }

        _sessionOrder.Remove(sessionId);
        WriteHelperLine("close:" + sessionId.ToString("N"));
        _ = ExecuteScriptAsync("window.appCloseSession('" + sessionId.ToString("N") + "');");

        if (_activeSessionId != sessionId)
        {
            RefreshTabStrip();
            return;
        }

        _activeSessionId = null;

        if (_sessionOrder.Count > 0)
        {
            SwitchToTab(_sessionOrder[^1]);
        }
        else
        {
            ReturnToLauncher();
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string sessionIdText &&
            Guid.TryParse(sessionIdText, out Guid sessionId))
        {
            CloseTab(sessionId);
        }
    }

    private void TabStripListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelectionEvent)
        {
            return;
        }

        if (TabStripListView.SelectedItem is TerminalTabItem item &&
            Guid.TryParse(item.SessionId, out Guid sessionId) &&
            sessionId != _activeSessionId)
        {
            SwitchToTab(sessionId);
        }
    }

    private void RefreshTabStrip()
    {
        List<TerminalTabItem> items = _sessionOrder
            .Where(id => _sessions.ContainsKey(id))
            .Select(id =>
            {
                TerminalSessionInfo session = _sessions[id];
                string accentHex = ResolveWorkspaceTheme(session.Profile).AccentHex;
                return TerminalTabItem.For(
                    id.ToString("N"),
                    session.Profile.Name,
                    id == _activeSessionId,
                    session.Status,
                    session.ShowDoneFlash,
                    accentHex);
            })
            .ToList();

        _suppressTabSelectionEvent = true;
        TabStripListView.ItemsSource = items;
        TabStripListView.SelectedItem = items.FirstOrDefault(i =>
            _activeSessionId.HasValue && i.SessionId == _activeSessionId.Value.ToString("N"));
        _suppressTabSelectionEvent = false;

        TabStripBar.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Moves a session to a new activity state and refreshes the tab strip. Transitioning
    /// into <see cref="SessionActivityStatus.Idle"/> from a non-idle state briefly flags the
    /// tab as "just finished" (<see cref="TerminalSessionInfo.ShowDoneFlash"/>) so it shows a
    /// done indicator for a few seconds instead of just going quiet.
    /// </summary>
    private void SetSessionStatus(Guid sessionId, SessionActivityStatus newStatus)
    {
        if (!_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session) || session.Status == newStatus)
        {
            return;
        }

        SessionActivityStatus previousStatus = session.Status;
        session.Status = newStatus;

        if (newStatus == SessionActivityStatus.Idle && previousStatus != SessionActivityStatus.Idle)
        {
            session.ShowDoneFlash = true;
            var doneFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            doneFlashTimer.Tick += (_, _) =>
            {
                doneFlashTimer.Stop();
                if (session.Status == SessionActivityStatus.Idle)
                {
                    session.ShowDoneFlash = false;
                    RefreshTabStrip();
                }
            };
            doneFlashTimer.Start();
        }

        RefreshTabStrip();
    }

    /// <summary>
    /// Starts (or leaves off) the tab-strip status dot's pulse animation. Driven by the
    /// Ellipse's own Tag (set from <see cref="TerminalTabItem.StatusDotAnimation"/>) rather
    /// than a bound trigger, since WinUI3 data templates have no built-in way to react to a
    /// bound value change with a storyboard.
    /// </summary>
    private void StatusDot_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: "pulse" } dot)
        {
            return;
        }

        var pulse = new DoubleAnimation
        {
            From = 1.0,
            To = 0.3,
            Duration = TimeSpan.FromSeconds(0.7),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase()
        };
        Storyboard.SetTarget(pulse, dot);
        Storyboard.SetTargetProperty(pulse, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(pulse);
        storyboard.Begin();
    }

    private void ReturnToLauncher()
    {
        _activeSessionId = null;
        ApplyActiveTheme();
        RefreshTabStrip();

        UsageOverlay.Visibility = Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        CancelAddTabButton.Visibility = Visibility.Collapsed;
        LauncherPanel.Visibility = Visibility.Visible;
        RefreshWorkspaceList();
    }

    private void SendOpenToHelper(Guid sessionId, WorkspaceProfile profile)
    {
        var request = new ClaudeSessionRequest
        {
            WindowsPath = profile.Path,
            PluginDirWindowsPath = string.IsNullOrWhiteSpace(profile.PluginDir) ? null : profile.PluginDir,
            PermissionMode = string.IsNullOrWhiteSpace(profile.PermissionMode) ? null : profile.PermissionMode,
            Model = string.IsNullOrWhiteSpace(profile.Model) ? null : profile.Model,
            Effort = string.IsNullOrWhiteSpace(profile.Effort) ? null : profile.Effort,
            ExtraArgs = string.IsNullOrWhiteSpace(profile.ExtraArgs) ? null : profile.ExtraArgs
        };

        string json = JsonSerializer.Serialize(request, SessionJsonOptions);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        WriteHelperLine("open:" + sessionId.ToString("N") + ":" + payload);
    }

    private void SendSlashCommandToActiveSession(string command)
    {
        if (_activeSessionId is not Guid sessionId)
        {
            return;
        }

        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(command + "\r"));
        WriteHelperLine("input:" + sessionId.ToString("N") + ":" + payload);
    }

    private void SessionSettingsFlyout_Opening(object sender, object e)
    {
        if (_activeSessionId is not Guid sessionId || !_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
        {
            SessionModelComboBox.SelectedValue = null;
            SessionEffortComboBox.SelectedValue = null;
            return;
        }

        SessionModelComboBox.SelectedValue = session.CurrentModel;
        SessionEffortComboBox.SelectedValue = session.CurrentEffort;
    }

    private void SessionModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionModelComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string model &&
            _activeSessionId is Guid sessionId &&
            _sessions.TryGetValue(sessionId, out TerminalSessionInfo? session) &&
            session.CurrentModel != model)
        {
            session.CurrentModel = model;
            SendSlashCommandToActiveSession("/model " + model);
        }
    }

    private void SessionEffortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionEffortComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string effort &&
            _activeSessionId is Guid sessionId &&
            _sessions.TryGetValue(sessionId, out TerminalSessionInfo? session) &&
            session.CurrentEffort != effort)
        {
            session.CurrentEffort = effort;
            SendSlashCommandToActiveSession("/effort " + effort);
        }
    }

    private void EnsureHelperRunning()
    {
        if (_helperProcess != null && !_helperProcess.HasExited)
        {
            return;
        }

        try
        {
            // Bundled under Assets\Terminal\node\ by scripts\fetch-node.ps1 (see publish.ps1
            // and the release workflow) so Claudium doesn't depend on Node.js being installed
            // system-wide. Falls back to PATH resolution for dev setups that predate this.
            string bundledNodeExe = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal", "node", "node.exe");
            string nodeExe = File.Exists(bundledNodeExe) ? bundledNodeExe : "node.exe";

            var psi = new ProcessStartInfo
            {
                FileName = nodeExe,
                Arguments = "\"" + Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal", "terminal-helper.js") + "\"",
                WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal"),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            _helperProcess = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };
            _helperProcess.OutputDataReceived += HelperProcess_OutputDataReceived;
            _helperProcess.ErrorDataReceived += HelperProcess_ErrorDataReceived;
            _helperProcess.Exited += HelperProcess_Exited;

            if (!_helperProcess.Start())
            {
                UpdateStatus("Kunde inte starta Node-hjälpprocessen.");
                return;
            }

            _helperInput = _helperProcess.StandardInput;
            _helperProcess.BeginOutputReadLine();
            _helperProcess.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            UpdateStatus("Terminalen kunde inte starta: " + ex.Message);
        }
    }

    private static bool TrySplitSessionMessage(string data, int prefixLength, out Guid sessionId, out string remainder)
    {
        string rest = data.Substring(prefixLength);
        int separatorIndex = rest.IndexOf(':');
        if (separatorIndex < 0)
        {
            sessionId = Guid.Empty;
            remainder = string.Empty;
            return false;
        }

        string sessionIdText = rest.Substring(0, separatorIndex);
        remainder = rest.Substring(separatorIndex + 1);
        return Guid.TryParse(sessionIdText, out sessionId);
    }

    private void HelperProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
        {
            return;
        }

        if (e.Data.StartsWith("data:", StringComparison.Ordinal))
        {
            if (!TrySplitSessionMessage(e.Data, 5, out Guid sessionId, out string payload))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(async () =>
            {
                if (_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
                {
                    session.LastStatus = null;

                    // A permission prompt is dismissed by a keypress (e.g. "y"/Enter), not by
                    // any hook — the pty simply echoes it back as ordinary output. Treat new
                    // output arriving while waiting as Claude resuming work; the Stop hook will
                    // correct this to Idle shortly if it turns out nothing more is happening.
                    if (session.Status == SessionActivityStatus.Waiting)
                    {
                        SetSessionStatus(sessionId, SessionActivityStatus.Working);
                    }
                }

                if (sessionId == _activeSessionId && StatusOverlay.Visibility == Visibility.Visible)
                {
                    StatusOverlay.Visibility = Visibility.Collapsed;
                }

                await ExecuteScriptAsync("window.appReceiveData('" + sessionId.ToString("N") + "', '" + EscapeForJavaScript(payload) + "');");
            });
            return;
        }

        if (e.Data.StartsWith("status:", StringComparison.Ordinal))
        {
            if (!TrySplitSessionMessage(e.Data, 7, out Guid sessionId, out string status))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_sessions.TryGetValue(sessionId, out TerminalSessionInfo? session))
                {
                    session.LastStatus = status;
                }

                if (sessionId == _activeSessionId)
                {
                    StatusOverlay.Visibility = Visibility.Visible;
                    UpdateStatus(status);
                }
            });
            return;
        }

        if (e.Data.StartsWith("usage:", StringComparison.Ordinal))
        {
            string payload = e.Data.Substring(6);
            DispatcherQueue.TryEnqueue(() => UpdateUsage(payload));
            return;
        }

        if (e.Data.StartsWith("exit:", StringComparison.Ordinal))
        {
            if (!TrySplitSessionMessage(e.Data, 5, out Guid sessionId, out string _))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() => CloseTab(sessionId));
            return;
        }

        if (e.Data.StartsWith("activity:", StringComparison.Ordinal))
        {
            if (!TrySplitSessionMessage(e.Data, 9, out Guid sessionId, out string kind))
            {
                return;
            }

            SessionActivityStatus? newStatus = kind switch
            {
                "working" => SessionActivityStatus.Working,
                "waiting" => SessionActivityStatus.Waiting,
                "idle" => SessionActivityStatus.Idle,
                _ => null
            };

            if (newStatus is not SessionActivityStatus status)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() => SetSessionStatus(sessionId, status));
            return;
        }
    }

    private void HelperProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
        {
            return;
        }

        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Data + Environment.NewLine));
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_activeSessionId is Guid sessionId)
            {
                await ExecuteScriptAsync("window.appReceiveData('" + sessionId.ToString("N") + "', '" + EscapeForJavaScript(payload) + "');");
            }
            else
            {
                UpdateStatus(e.Data);
            }
        });
    }

    private void HelperProcess_Exited(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _sessions.Clear();
            _sessionOrder.Clear();
            _activeSessionId = null;
            _helperInput = null;

            UpdateStatus("Claude-processen avslutades oväntat.");
            StatusOverlay.Visibility = Visibility.Visible;
            UsageOverlay.Visibility = Visibility.Collapsed;
            CancelAddTabButton.Visibility = Visibility.Collapsed;
            LauncherPanel.Visibility = Visibility.Visible;
            RefreshTabStrip();
            RefreshWorkspaceList();
        });
    }

    private void WriteHelperLine(string line)
    {
        try
        {
            if (_helperInput == null)
            {
                return;
            }

            _helperInput.WriteLine(line);
            _helperInput.Flush();
        }
        catch (Exception ex)
        {
            UpdateStatus("Kommunikation med terminalen misslyckades: " + ex.Message);
        }
    }

    private void StopHelper()
    {
        try
        {
            if (_helperInput != null)
            {
                try
                {
                    _helperInput.WriteLine("shutdown");
                    _helperInput.Flush();
                }
                catch
                {
                }

                _helperInput.Dispose();
                _helperInput = null;
            }

            if (_helperProcess != null)
            {
                if (!_helperProcess.HasExited)
                {
                    _helperProcess.Kill();
                    _helperProcess.WaitForExit(1500);
                }

                _helperProcess.Dispose();
                _helperProcess = null;
            }
        }
        catch
        {
        }

        _sessions.Clear();
        _sessionOrder.Clear();
        _activeSessionId = null;
    }

    private async System.Threading.Tasks.Task ForceTerminalFitAsync()
    {
        if (!_webReady)
        {
            return;
        }

        await ExecuteScriptAsync("window.appForceFit && window.appForceFit();");
    }

    private async System.Threading.Tasks.Task ExecuteScriptAsync(string script)
    {
        if (!_webReady || TerminalView.CoreWebView2 == null)
        {
            return;
        }

        await TerminalView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void UpdateStatus(string text)
    {
        StatusText.Text = text;
    }

    private void SetupUsageRefreshTimer()
    {
        _usageRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _usageRefreshTimer.Tick += (_, _) => RefreshUsageRelativeTimes();
        _usageRefreshTimer.Start();
    }

    private void UpdateUsage(string encodedPayload)
    {
        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPayload));
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            bool ok = root.TryGetProperty("ok", out JsonElement okElement) && okElement.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                UsageOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            UsageEntry? session = ParseUsageEntry(root, "session");
            UsageEntry? allModels = ParseUsageEntry(root, "all_models");
            if (session == null && allModels == null)
            {
                UsageOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            UsageEntry? visibleEntry = session ?? allModels;
            if (visibleEntry != null)
            {
                SessionArcPath.Data = BuildPieGeometry(ClampPercent(visibleEntry.Percent));
                string tooltip = BuildCombinedUsageTooltip(session, allModels);
                ToolTipService.SetToolTip(SessionArcPath, tooltip);
                ToolTipService.SetToolTip(SessionUsageIcon, tooltip);
            }
            else
            {
                SessionArcPath.Data = null;
                ToolTipService.SetToolTip(SessionArcPath, null);
                ToolTipService.SetToolTip(SessionUsageIcon, null);
            }

            _lastUsageUpdatedAt = DateTimeOffset.Now;
            RefreshUsageRelativeTimes();
            UsageOverlay.Visibility = Visibility.Visible;
        }
        catch
        {
            UsageOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshUsageRelativeTimes()
    {
        if (_lastUsageUpdatedAt == null || UsageOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        string updated = FormatUpdatedAt(_lastUsageUpdatedAt.Value);
        ToolTipService.SetToolTip(SessionArcPath, AppendUpdatedToTooltip(ToolTipService.GetToolTip(SessionArcPath), updated));
        ToolTipService.SetToolTip(SessionUsageIcon, AppendUpdatedToTooltip(ToolTipService.GetToolTip(SessionUsageIcon), updated));
    }

    private static UsageEntry? ParseUsageEntry(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement entry) || entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double? percent = null;
        DateTimeOffset? resetsAt = null;

        if (entry.TryGetProperty("percent", out JsonElement percentElement) && percentElement.TryGetDouble(out double parsedPercent))
        {
            percent = parsedPercent;
        }

        if (entry.TryGetProperty("resets_at", out JsonElement resetElement) && resetElement.ValueKind == JsonValueKind.String)
        {
            string? value = resetElement.GetString();
            if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, out DateTimeOffset parsedReset))
            {
                resetsAt = parsedReset.ToLocalTime();
            }
        }

        return new UsageEntry(percent, resetsAt);
    }

    private static string FormatPercent(double? percent)
    {
        if (percent == null)
        {
            return "--%";
        }

        return $"{Math.Round(percent.Value):0}%";
    }

    private static double ClampPercent(double? percent)
    {
        if (percent == null)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(100, percent.Value));
    }

    private static string FormatReset(DateTimeOffset? resetTime)
    {
        if (resetTime == null)
        {
            return "Reset okand";
        }

        TimeSpan delta = resetTime.Value - DateTimeOffset.Now;
        if (delta.TotalMinutes <= 0)
        {
            return "Resettar snart";
        }

        if (delta.TotalHours < 24)
        {
            int hours = (int)Math.Floor(delta.TotalHours);
            int minutes = Math.Max(0, delta.Minutes);
            if (hours <= 0)
            {
                return $"Reset om {minutes} min";
            }

            return $"Reset om {hours} h {minutes} min";
        }

        return "Reset " + resetTime.Value.ToString("ddd HH:mm");
    }

    private static string FormatUpdatedAt(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.Now - timestamp;
        if (elapsed.TotalMinutes < 1)
        {
            return "Uppdaterad nu";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"Uppdaterad for {Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes))} min sedan";
        }

        return $"Uppdaterad for {Math.Max(1, (int)Math.Floor(elapsed.TotalHours))} h sedan";
    }

    private static string BuildUsageTooltip(string label, UsageEntry entry)
    {
        return $"{label}: {FormatPercent(entry.Percent)}\n{FormatReset(entry.ResetsAt)}";
    }

    private static string BuildCombinedUsageTooltip(UsageEntry? session, UsageEntry? allModels)
    {
        var lines = new List<string>();

        if (session != null)
        {
            lines.Add($"Current session: {FormatPercent(session.Percent)}");
            lines.Add(FormatReset(session.ResetsAt));
        }

        if (allModels != null)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"All models: {FormatPercent(allModels.Percent)}");
            lines.Add(FormatReset(allModels.ResetsAt));
        }

        return string.Join("\n", lines);
    }

    private static string AppendUpdatedToTooltip(object? tooltip, string updated)
    {
        string baseText = tooltip as string ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseText))
        {
            return updated;
        }

        int markerIndex = baseText.IndexOf("\nUppdaterad", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            baseText = baseText[..markerIndex];
        }

        return baseText + "\n" + updated;
    }

    private static Geometry? BuildPieGeometry(double percent)
    {
        if (percent <= 0)
        {
            return null;
        }

        const double size = 18;
        const double center = size / 2;
        const double radius = 9;

        double clamped = Math.Max(0, Math.Min(100, percent));
        double sweepAngle = Math.Min(359.99, 360 * (clamped / 100.0));
        double startAngle = -90;
        double endAngle = startAngle + sweepAngle;

        Windows.Foundation.Point startPoint = PointOnCircle(center, center, radius, startAngle);
        Windows.Foundation.Point endPoint = PointOnCircle(center, center, radius, endAngle);

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(center, center),
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new LineSegment
        {
            Point = startPoint
        });

        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Windows.Foundation.Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepAngle >= 180
        });

        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(center, center)
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Windows.Foundation.Point PointOnCircle(double centerX, double centerY, double radius, double degrees)
    {
        double radians = Math.PI * degrees / 180.0;
        return new Windows.Foundation.Point(
            centerX + radius * Math.Cos(radians),
            centerY + radius * Math.Sin(radians));
    }

    private static string EscapeForJavaScript(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private sealed record UsageEntry(double? Percent, DateTimeOffset? ResetsAt);
}
