using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;
using FootballSimulation.Wpf.Views;

namespace FootballSimulation.Wpf;

public partial class MainWindow : Window
{
    private GameFlowState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        UpdateThemeToggleButton();
        ShowMainMenu();
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

    private void ShellSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is DashboardView dashboardView)
        {
            dashboardView.SaveGame();
        }
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

    private void ShowMainMenu()
    {
        Navigate(new MainMenuView(StartNewGame, ShowLoadGame, Close));
    }

    private void StartNewGame()
    {
        _state = new GameFlowState();
        ShowTeamSelection();
    }

    private void ShowTeamSelection()
    {
        Navigate(new TeamSelectionView(_state, Navigate));
    }

    private void ShowLoadGame()
    {
        Navigate(new LoadGameView(LoadSavedGame, ShowMainMenu));
    }

    private void LoadSavedGame(SaveGameData saveData)
    {
        var league = SaveGameService.CreateLeague(saveData);
        var selectedTeam = league.Teams.FirstOrDefault(team =>
            string.Equals(team.Name, saveData.SelectedClubName, StringComparison.OrdinalIgnoreCase));

        if (selectedTeam is null)
        {
            MessageBox.Show("The selected club could not be found in this save file.", "Load Game", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowLoadGame();
            return;
        }

        _state = new GameFlowState
        {
            Teams = league.Teams,
            League = league,
            SelectedTeam = selectedTeam,
            CurrentFixture = FindNextFixtureForTeam(league, selectedTeam),
            CurrentMatch = null
        };

        Navigate(new DashboardView(_state, Navigate));
    }

    private void Navigate(UserControl view)
    {
        MainContent.Content = view;
        ShellSaveButton.Visibility = view is DashboardView ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Fixture? FindNextFixtureForTeam(League league, Team selectedTeam)
    {
        return league.Fixtures
            .Where(fixture => !fixture.IsPlayed &&
                (fixture.HomeTeam == selectedTeam || fixture.AwayTeam == selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .FirstOrDefault();
    }

    private void ApplyWindowChromeTheme()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
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
