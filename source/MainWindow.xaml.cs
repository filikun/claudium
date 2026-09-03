using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinRT.Interop;
using Claudium.Models;
using Claudium.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Claudium;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Bound once for the window's lifetime — see the remarks on <see cref="UpdateTitleBarTabs"/>
    /// for why this must never be reassigned wholesale.
    /// </summary>
    private readonly ObservableCollection<TerminalTabItem> _titleBarTabs = new();

    public MainWindow()
    {
        InitializeComponent();

        TitleBarTabsView.TabItemsSource = _titleBarTabs;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(980, 860));
        UpdateTitleBarContentWidth(AppWindow.Size.Width);

        // Without a floor, the window can be dragged down to a size where the fixed
        // title-bar margins (see MainWindow.xaml) leave zero room for the session tabs,
        // making them appear to vanish. Clamping here keeps them reachable at any size.
        AppWindow.Changed += AppWindow_Changed;

        // App.CurrentWindow isn't assigned until after this constructor returns, so
        // MainPage.ApplyAppTheme() (which runs during Navigate below) can't reach this
        // window yet to color the title bar. Set the initial color directly here instead;
        // ApplyTitleBarTheme is called again on every later theme change via CurrentWindow.
        AppSettings initialSettings = new AppSettingsStore().Load();
        ApplyTitleBarTheme(AppThemes.Resolve(initialSettings.ThemeId));

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    /// <summary>
    /// Recolors the native title bar chrome (min/max/close buttons, background) to match
    /// the selected AppTheme. This is Win32 window-chrome state, not XAML — it can't be
    /// bound via StaticResource like the rest of the app, so MainPage.ApplyAppTheme calls
    /// this explicitly whenever the theme changes.
    /// </summary>
    public void ApplyTitleBarTheme(AppTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        Windows.UI.Color surface = ColorFromHex(theme.PanelBackgroundHex);
        Windows.UI.Color textPrimary = ColorFromHex("#F7F8FC");
        Windows.UI.Color textSecondary = ColorFromHex("#BEC4D6");
        Windows.UI.Color hover = Lighten(surface, 0.10);
        Windows.UI.Color pressed = Lighten(surface, 0.16);

        AppWindow.TitleBar.ButtonBackgroundColor = surface;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = surface;
        AppWindow.TitleBar.ButtonForegroundColor = textPrimary;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = textSecondary;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hover;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = pressed;
        AppWindow.TitleBar.BackgroundColor = surface;
        AppWindow.TitleBar.ForegroundColor = textPrimary;
        AppWindow.TitleBar.InactiveBackgroundColor = surface;
        AppWindow.TitleBar.InactiveForegroundColor = textSecondary;
    }

    private const int MinWindowWidth = 960;
    private const int MinWindowHeight = 760;
    private const int TitleBarSideReserve = 260;
    private bool _isSynchronizingTitleBarTabs;

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange)
        {
            return;
        }

        SizeInt32 size = sender.Size;
        int width = System.Math.Max(size.Width, MinWindowWidth);
        int height = System.Math.Max(size.Height, MinWindowHeight);
        UpdateTitleBarContentWidth(width);
        if (width != size.Width || height != size.Height)
        {
            sender.Resize(new SizeInt32(width, height));
        }
    }

    private void UpdateTitleBarContentWidth(int windowWidth)
    {
        // TitleBar centers custom content. Giving it the full usable width keeps the
        // tab strip's left edge anchored just after the native icon and app title.
        TitleBarContentHost.Width = System.Math.Max(0, windowWidth - TitleBarSideReserve);
    }

    /// <summary>
    /// Keeps the title-bar tabs synchronized with the page's session list.
    /// </summary>
    /// <remarks>
    /// The caller rebuilds its whole <see cref="TerminalTabItem"/> list from scratch on
    /// every refresh (open/close/status tick/switch — this runs very often). Wholesale
    /// reassigning <see cref="TabView.TabItemsSource"/> each time forces TabView to tear
    /// down and re-realize every tab container; when that races a container still being
    /// realized (typically the most-recently-touched, i.e. active, tab), that tab's
    /// container can end up never added to the visual tree and the tab simply vanishes.
    /// Instead we keep one persistent <see cref="ObservableCollection{T}"/> bound for the
    /// lifetime of the window and reconcile it in place (insert/remove/update-in-place via
    /// <see cref="TerminalTabItem.UpdateFrom"/>), so TabView only ever sees the specific,
    /// small change that actually happened instead of a full teardown.
    /// </remarks>
    public void UpdateTitleBarTabs(IReadOnlyList<TerminalTabItem> items)
    {
        _isSynchronizingTitleBarTabs = true;
        try
        {
            for (int i = _titleBarTabs.Count - 1; i >= 0; i--)
            {
                if (!items.Any(item => item.SessionId == _titleBarTabs[i].SessionId))
                {
                    _titleBarTabs.RemoveAt(i);
                }
            }

            for (int targetIndex = 0; targetIndex < items.Count; targetIndex++)
            {
                TerminalTabItem source = items[targetIndex];
                int currentIndex = -1;
                for (int i = 0; i < _titleBarTabs.Count; i++)
                {
                    if (_titleBarTabs[i].SessionId == source.SessionId)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                if (currentIndex < 0)
                {
                    _titleBarTabs.Insert(targetIndex, source);
                }
                else
                {
                    if (currentIndex != targetIndex)
                    {
                        TerminalTabItem existing = _titleBarTabs[currentIndex];
                        _titleBarTabs.RemoveAt(currentIndex);
                        _titleBarTabs.Insert(targetIndex, existing);
                    }

                    _titleBarTabs[targetIndex].UpdateFrom(source);
                }
            }

            TerminalTabItem? active = _titleBarTabs.FirstOrDefault(item => item.IsActive);
            if (!ReferenceEquals(TitleBarTabsView.SelectedItem, active))
            {
                TitleBarTabsView.SelectedItem = active;
            }
        }
        finally
        {
            _isSynchronizingTitleBarTabs = false;
        }
    }

    private MainPage? CurrentPage => RootFrame.Content as MainPage;

    private void TitleBarTab_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string sessionId })
        {
            CurrentPage?.ActivateTabFromTitleBar(sessionId);
        }
    }

    private void TitleBarCloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string sessionId })
        {
            CurrentPage?.CloseTabFromTitleBar(sessionId);
        }
    }

    private void TitleBarAddTabButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentPage?.StartNewTabFromTitleBar();
    }

    private void TitleBarTabsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingTitleBarTabs && TitleBarTabsView.SelectedItem is TerminalTabItem item)
        {
            CurrentPage?.ActivateTabFromTitleBar(item.SessionId);
        }
    }

    private void TitleBarTabsView_AddTabButtonClick(TabView sender, object args)
    {
        CurrentPage?.StartNewTabFromTitleBar();
    }

    private void TitleBarTabsView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TerminalTabItem item)
        {
            CurrentPage?.CloseTabFromTitleBar(item.SessionId);
        }
    }

    private static Windows.UI.Color ColorFromHex(string hex)
    {
        string value = hex.TrimStart('#');
        int offset = value.Length == 8 ? 2 : 0;
        byte r = System.Convert.ToByte(value.Substring(offset, 2), 16);
        byte g = System.Convert.ToByte(value.Substring(offset + 2, 2), 16);
        byte b = System.Convert.ToByte(value.Substring(offset + 4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private static Windows.UI.Color Lighten(Windows.UI.Color color, double amount)
    {
        byte Blend(byte channel) => (byte)(channel + (255 - channel) * amount);
        return Windows.UI.Color.FromArgb(255, Blend(color.R), Blend(color.G), Blend(color.B));
    }
}
