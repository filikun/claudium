using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(980, 860));

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

    private static Windows.UI.Color ColorFromHex(string hex)
    {
        string value = hex.TrimStart('#');
        byte r = System.Convert.ToByte(value.Substring(0, 2), 16);
        byte g = System.Convert.ToByte(value.Substring(2, 2), 16);
        byte b = System.Convert.ToByte(value.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private static Windows.UI.Color Lighten(Windows.UI.Color color, double amount)
    {
        byte Blend(byte channel) => (byte)(channel + (255 - channel) * amount);
        return Windows.UI.Color.FromArgb(255, Blend(color.R), Blend(color.G), Blend(color.B));
    }
}
