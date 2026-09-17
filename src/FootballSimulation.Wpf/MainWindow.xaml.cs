using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;
using FootballSimulation.Wpf.Views;

namespace FootballSimulation.Wpf;

public partial class MainWindow : Window
{
    private const double StealthWindowWidth = 520;
    private const double StealthWindowHeight = 380;
    private const double StealthContentWidth = 1040;
    private const double StealthContentHeight = 670;
    private const double StealthContentScale = 0.5;
    private const double HiddenWindowOpacity = 0.06;

    private GameFlowState _state = new();
    private readonly TransferMarketService _transferMarketService = new();
    private Rect _normalWindowBounds;
    private WindowState _normalWindowState;
    private ResizeMode _normalResizeMode;
    private double _normalMinWidth;
    private double _normalMinHeight;
    private bool _hasStoredNormalWindowBounds;
    private bool _isHoverVisibilityEnabled;

    public bool IsStealthMode { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        MouseEnter += MainWindow_MouseEnter;
        MouseLeave += MainWindow_MouseLeave;
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        UpdateThemeToggleButton();
        UpdateStealthModeButton();
        UpdateHoverVisibilityButton();
        UpdatePinToTopButton();
        UpdateMaximizeButton();
        ShowMainMenu();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ToggleStealthMode();
            e.Handled = true;
        }
    }

    private void StealthModeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleStealthMode();
    }

    private void HoverVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        _isHoverVisibilityEnabled = !_isHoverVisibilityEnabled;
        UpdateHoverVisibilityButton();

        if (!_isHoverVisibilityEnabled)
        {
            AnimateWindowOpacity(1.0);
        }
    }

    private void MainWindow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isHoverVisibilityEnabled)
        {
            AnimateWindowOpacity(1.0);
        }
    }

    private void MainWindow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isHoverVisibilityEnabled)
        {
            AnimateWindowOpacity(HiddenWindowOpacity);
        }
    }

    private void AnimateWindowOpacity(double targetOpacity)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(
            targetOpacity,
            TimeSpan.FromMilliseconds(140)));
    }

    private void ToggleStealthMode()
    {
        if (IsStealthMode)
        {
            ExitStealthMode();
        }
        else
        {
            EnterStealthMode();
        }
    }

    private void EnterStealthMode()
    {
        if (MainContent.Content is MatchLiveView liveMatchView)
        {
            liveMatchView.PrepareForStealthMode();
        }

        _normalWindowState = WindowState;
        _normalWindowBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        _normalResizeMode = ResizeMode;
        _normalMinWidth = MinWidth;
        _normalMinHeight = MinHeight;
        _hasStoredNormalWindowBounds = true;

        WindowState = WindowState.Normal;
        MinWidth = StealthWindowWidth;
        MinHeight = StealthWindowHeight;
        Width = StealthWindowWidth;
        Height = StealthWindowHeight;
        ResizeMode = ResizeMode.NoResize;

        ApplicationContentRoot.Width = StealthContentWidth;
        ApplicationContentRoot.Height = StealthContentHeight;
        ApplicationContentRoot.LayoutTransform = new ScaleTransform(StealthContentScale, StealthContentScale);

        IsStealthMode = true;
        UpdateStealthModeButton();
        UpdateMaximizeButton();
    }

    private void ExitStealthMode()
    {
        ApplicationContentRoot.LayoutTransform = Transform.Identity;
        ApplicationContentRoot.Width = double.NaN;
        ApplicationContentRoot.Height = double.NaN;

        IsStealthMode = false;
        if (_hasStoredNormalWindowBounds)
        {
            var restoredBounds = ClampToVisibleWorkArea(_normalWindowBounds);
            ResizeMode = _normalResizeMode;
            MinWidth = 0;
            MinHeight = 0;
            Left = restoredBounds.Left;
            Top = restoredBounds.Top;
            Width = restoredBounds.Width;
            Height = restoredBounds.Height;
            MinWidth = _normalMinWidth;
            MinHeight = _normalMinHeight;
            WindowState = _normalWindowState;
            _hasStoredNormalWindowBounds = false;
        }

        UpdateStealthModeButton();
        UpdateMaximizeButton();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowChromeTheme();
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeToggleButton();
        UpdatePinToTopButton();
        ApplyWindowChromeTheme();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleTheme();
    }

    private void PinToTopButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinToTopButton();
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsStealthMode)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeButton();
    }

    private void UpdatePinToTopButton()
    {
        PinToTopButton.ToolTip = Topmost
            ? "Unpin window from top"
            : "Pin window on top";
        PinToTopButton.Opacity = Topmost ? 1.0 : 0.65;
        PinToTopButton.Background = Topmost
            ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
            : Brushes.Transparent;
        PinToTopButton.Foreground = Topmost
            ? Brushes.White
            : Brushes.White;
    }

    private void UpdateMaximizeButton()
    {
        MaximizeWindowButton.IsEnabled = !IsStealthMode;
        MaximizeWindowButton.Content = WindowState == WindowState.Maximized
            ? "\uE923"
            : "\uE922";
        MaximizeWindowButton.ToolTip = WindowState == WindowState.Maximized
            ? "Restore"
            : "Maximize";
    }

    private void UpdateStealthModeButton()
    {
        StealthModeButton.ToolTip = IsStealthMode
            ? "Exit Stealth Mode (Ctrl+Shift+S)"
            : "Enter Stealth Mode (Ctrl+Shift+S)";
        StealthModeButton.Opacity = IsStealthMode ? 1.0 : 0.65;
        StealthModeButton.Background = IsStealthMode
            ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
            : Brushes.Transparent;
    }

    private void UpdateHoverVisibilityButton()
    {
        HoverVisibilityButton.ToolTip = _isHoverVisibilityEnabled
            ? "Keep window visible"
            : "Hide window when the pointer leaves";
        HoverVisibilityButton.Opacity = _isHoverVisibilityEnabled ? 1.0 : 0.65;
        HoverVisibilityButton.Background = _isHoverVisibilityEnabled
            ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
            : Brushes.Transparent;
    }

    private void ShellSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainContent.Content is DashboardView dashboardView)
        {
            dashboardView.SaveGame();
        }
    }

    private void ShellStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        Navigate(new LeaguePlayerStatsView(_state, Navigate));
    }

    private void ShellTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        Navigate(new TransferMarketView(_state, Navigate));
    }

    private void UpdateThemeToggleButton()
    {
        (ThemeToggleButton.Content, ThemeToggleButton.ToolTip) = ThemeManager.CurrentTheme switch
        {
            AppTheme.Dark => ("\u2600", "Switch to Light Mode"),
            AppTheme.Light => ("\u25A6", "Switch to Work Mode"),
            _ => ("\U0001F319", "Switch to Dark Mode")
        };
    }

    private void ShowMainMenu()
    {
        Navigate(new MainMenuView(StartNewGame, ShowLoadGame, Close));
    }

    private void StartNewGame(string leagueId)
    {
        _state = new GameFlowState
        {
            SelectedLeagueId = leagueId
        };
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

    private void LoadSavedGame(SaveGameData saveData, int slotNumber)
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

        var transferMarket = saveData.TransferMarketState.Leagues.Count > 0
            ? saveData.TransferMarketState
            : _transferMarketService.CreateInitialState(league);
        _transferMarketService.BindActiveLeague(transferMarket, league);

        _state = new GameFlowState
        {
            Teams = league.Teams,
            League = league,
            TransferMarket = transferMarket,
            SelectedLeagueId = string.IsNullOrWhiteSpace(league.LeagueId) ? LeagueDataService.DefaultLeagueId : league.LeagueId,
            SelectedLeagueDefinition = TryGetLeagueDefinition(league.LeagueId),
            SelectedTeam = selectedTeam,
            CurrentFixture = FindNextFixtureForTeam(league, selectedTeam),
            CurrentMatch = null,
            CurrentSaveSlotNumber = slotNumber
        };

        Navigate(new DashboardView(_state, Navigate));
    }

    private void Navigate(UserControl view)
    {
        MainContent.Content = view;
        ShellActionsPanel.Visibility = Visibility.Visible;
        ShellSaveButton.Visibility = Visibility.Collapsed;
        ShellStatsButton.Visibility = view is LeaguePlayerStatsView ? Visibility.Visible : Visibility.Collapsed;
        ShellTransferButton.Visibility = view is TransferMarketView ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Fixture? FindNextFixtureForTeam(League league, Team selectedTeam)
    {
        return league.Fixtures
            .Where(fixture => !fixture.IsPlayed &&
                (fixture.HomeTeam == selectedTeam || fixture.AwayTeam == selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .FirstOrDefault();
    }

    private static LeagueDefinition? TryGetLeagueDefinition(string? leagueId)
    {
        try
        {
            return new LeagueDataService().GetLeagueDefinition(leagueId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    private Rect ClampToVisibleWorkArea(Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var nativeBounds = new NativeRect
        {
            Left = (int)Math.Round(bounds.Left * dpi.DpiScaleX),
            Top = (int)Math.Round(bounds.Top * dpi.DpiScaleY),
            Right = (int)Math.Round(bounds.Right * dpi.DpiScaleX),
            Bottom = (int)Math.Round(bounds.Bottom * dpi.DpiScaleY)
        };

        var monitor = MonitorFromRect(ref nativeBounds, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return bounds;
        }

        var workArea = new Rect(
            monitorInfo.WorkArea.Left / dpi.DpiScaleX,
            monitorInfo.WorkArea.Top / dpi.DpiScaleY,
            (monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left) / dpi.DpiScaleX,
            (monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top) / dpi.DpiScaleY);
        var width = Math.Min(Math.Max(bounds.Width, _normalMinWidth), workArea.Width);
        var height = Math.Min(Math.Max(bounds.Height, _normalMinHeight), workArea.Height);
        var left = Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height);

        return new Rect(left, top, width, height);
    }

    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
