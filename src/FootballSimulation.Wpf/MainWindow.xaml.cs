using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;
using FootballSimulation.Wpf.Views;

namespace FootballSimulation.Wpf;

public partial class MainWindow : Window
{
    private readonly GameFlowState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        UpdateThemeToggleButton();
        ShowTeamSelection();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowChromeTheme();
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeToggleButton();
        ApplyWindowChromeTheme();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleTheme();
    }

    private void UpdateThemeToggleButton()
    {
        ThemeToggleButton.Content = ThemeManager.CurrentTheme == AppTheme.Dark
            ? "☀"
            : "🌙";
        ThemeToggleButton.ToolTip = ThemeManager.CurrentTheme == AppTheme.Dark
            ? "Switch to Light Mode"
            : "Switch to Dark Mode";
    }

    private void ShowTeamSelection()
    {
        Navigate(new TeamSelectionView(_state, Navigate));
    }

    private void Navigate(UserControl view)
    {
        MainContent.Content = view;
    }

    private void ApplyWindowChromeTheme()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var enabled = ThemeManager.CurrentTheme == AppTheme.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>()) != 0)
        {
            _ = DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, Marshal.SizeOf<int>());
        }

        var captionColor = ToColorRef(ThemeManager.GetColor("ThemeWindowChromeColor", Color.FromRgb(7, 18, 38)));
        var textColor = ToColorRef(ThemeManager.GetColor("ThemeWindowChromeTextColor", Colors.White));
        var borderColor = ToColorRef(ThemeManager.GetColor("ThemeWindowChromeBorderColor", Color.FromRgb(17, 24, 39)));

        _ = DwmSetWindowAttribute(windowHandle, DwmCaptionColor, ref captionColor, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(windowHandle, DwmTextColor, ref textColor, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(windowHandle, DwmBorderColor, ref borderColor, Marshal.SizeOf<int>());
    }

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
