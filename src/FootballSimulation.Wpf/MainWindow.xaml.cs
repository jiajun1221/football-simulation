using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
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
    private const double StealthCompactLiveWindowWidth = 260;
    private const double StealthCompactLiveWindowHeight = 540;
    private const double StealthCompactLiveContentWidth = 420;
    private const double StealthCompactLiveScale = 0.6;
    private const double StealthCompactLiveContentHeight = (StealthCompactLiveWindowHeight - 30) / StealthCompactLiveScale;

    private GameFlowState _state = new();
    private readonly TransferMarketService _transferMarketService = new();
    private Rect _normalWindowBounds;
    private WindowState _normalWindowState;
    private ResizeMode _normalResizeMode;
    private double _normalMinWidth;
    private double _normalMinHeight;
    private bool _hasStoredNormalWindowBounds;
    private bool _isHoverVisibilityEnabled;
    private readonly DispatcherTimer _hiddenWindowPointerTimer = new() { Interval = TimeSpan.FromMilliseconds(75) };
    private NativeRect _hiddenWindowBounds;
    private IntPtr _hiddenWindowHandle;
    private bool _isNativeWindowHidden;
    private FrameworkElement? _stealthHoverTarget;
    private Transform? _stealthHoverOriginalTransform;
    private Point _stealthHoverOriginalTransformOrigin;
    private int _stealthHoverOriginalZIndex;
    private Popup? _stealthPlayerPopover;

    public bool IsStealthMode { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        MouseEnter += MainWindow_MouseEnter;
        MouseLeave += MainWindow_MouseLeave;
        PreviewMouseMove += MainWindow_PreviewMouseMove;
        _hiddenWindowPointerTimer.Tick += HiddenWindowPointerTimer_Tick;
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
            RestoreHiddenWindow();
        }
    }

    private void MainWindow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isHoverVisibilityEnabled)
        {
            Opacity = 1.0;
        }
    }

    private void MainWindow_MouseLeave(object sender, MouseEventArgs e)
    {
        ClearStealthHoverZoom();

        if (_isHoverVisibilityEnabled)
        {
            HideWindowIfPointerIsOutside();
        }
    }

    private void HideWindowIfPointerIsOutside()
    {
        if (_isNativeWindowHidden)
        {
            return;
        }

        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero ||
            !GetWindowRect(windowHandle, out _hiddenWindowBounds) ||
            !GetCursorPos(out var pointerPosition) ||
            IsPointInside(_hiddenWindowBounds, pointerPosition))
        {
            return;
        }

        var emptyRegion = CreateRectRgn(0, 0, 0, 0);
        if (emptyRegion == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(windowHandle, emptyRegion, true) == 0)
        {
            DeleteObject(emptyRegion);
            return;
        }

        _hiddenWindowHandle = windowHandle;
        _isNativeWindowHidden = true;
        _hiddenWindowPointerTimer.Start();
    }

    private void HiddenWindowPointerTimer_Tick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var pointerPosition))
        {
            return;
        }

        if (IsPointInside(_hiddenWindowBounds, pointerPosition))
        {
            RestoreHiddenWindow();
        }
    }

    private static bool IsPointInside(NativeRect bounds, NativePoint point)
    {
        return point.X >= bounds.Left &&
               point.X < bounds.Right &&
               point.Y >= bounds.Top &&
               point.Y < bounds.Bottom;
    }

    private void RestoreHiddenWindow()
    {
        _hiddenWindowPointerTimer.Stop();
        Opacity = 1.0;
        if (_isNativeWindowHidden && _hiddenWindowHandle != IntPtr.Zero)
        {
            SetWindowRgn(_hiddenWindowHandle, IntPtr.Zero, true);
            _isNativeWindowHidden = false;
            _hiddenWindowHandle = IntPtr.Zero;
        }

        if (!IsVisible)
        {
            var showActivated = ShowActivated;
            ShowActivated = false;
            Show();
            ShowActivated = showActivated;
        }
    }

    private void MainWindow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsStealthMode)
        {
            ClearStealthHoverZoom();
            return;
        }

        var target = FindStealthHoverTarget(e.OriginalSource as DependencyObject);
        if (ReferenceEquals(target, _stealthHoverTarget))
        {
            return;
        }

        ClearStealthHoverZoom();
        if (target is null)
        {
            return;
        }

        _stealthHoverTarget = target;
        _stealthHoverOriginalTransform = target.RenderTransform;
        _stealthHoverOriginalTransformOrigin = target.RenderTransformOrigin;
        _stealthHoverOriginalZIndex = Panel.GetZIndex(target);

        if (IsPlayerCard(target.DataContext))
        {
            ShowStealthPlayerPopover(target);
            return;
        }

        const double zoom = 1.3;
        target.RenderTransformOrigin = GetStealthHoverTransformOrigin(target);
        target.RenderTransform = new ScaleTransform(zoom, zoom);
        Panel.SetZIndex(target, 1000);
    }

    private void ShowStealthPlayerPopover(FrameworkElement target)
    {
        const double playerPreviewWidth = 120;
        var cardTemplate = target switch
        {
            ListBoxItem => target.TryFindResource("SubstituteCardTemplate") as DataTemplate,
            Button button => button.ContentTemplate,
            _ => null
        };
        var cardPreview = new ContentControl
        {
            Width = playerPreviewWidth,
            Content = target.DataContext,
            ContentTemplate = cardTemplate,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false
        };
        var preview = new Border
        {
            Width = playerPreviewWidth,
            Child = cardPreview,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(10),
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.45,
                Color = Colors.Black
            },
            IsHitTestVisible = false
        };

        _stealthPlayerPopover = new Popup
        {
            AllowsTransparency = true,
            Child = preview,
            HorizontalOffset = 8,
            IsHitTestVisible = false,
            Placement = GetStealthPlayerPopoverPlacement(target),
            PlacementTarget = target,
            StaysOpen = true,
            IsOpen = true
        };
    }

    private static PlacementMode GetStealthPlayerPopoverPlacement(FrameworkElement target)
    {
        for (var current = GetParent(target); current is not null; current = GetParent(current))
        {
            if (current is not ListBox listBox || listBox.ActualWidth <= 0)
            {
                continue;
            }

            var center = target.TranslatePoint(new Point(target.ActualWidth / 2, 0), listBox);
            return center.X <= listBox.ActualWidth / 2 ? PlacementMode.Right : PlacementMode.Left;
        }

        return PlacementMode.Right;
    }

    private FrameworkElement? FindStealthHoverTarget(DependencyObject? source)
    {
        Button? nearestButton = null;
        Button? playerCardButton = null;
        Border? playerCardBorder = null;
        ListBoxItem? playerCardContainer = null;

        for (var current = source; current is not null && current != ApplicationContentRoot; current = GetParent(current))
        {
            if (current is Button button)
            {
                nearestButton ??= button;
                if (IsPlayerCard(button.DataContext))
                {
                    playerCardButton = button;
                }
            }

            if (current is Border border && IsPlayerCard(border.DataContext))
            {
                playerCardBorder = border;
            }

            if (current is ListBoxItem listBoxItem && IsPlayerCard(listBoxItem.DataContext))
            {
                playerCardContainer = listBoxItem;
            }
        }

        var target = (FrameworkElement?)playerCardContainer ??
                     playerCardButton ??
                     (FrameworkElement?)playerCardBorder ??
                     nearestButton;
        return target is not null && ApplicationContentRoot.IsAncestorOf(target) ? target : null;
    }

    private static Point GetStealthHoverTransformOrigin(FrameworkElement target)
    {
        for (var current = GetParent(target); current is not null; current = GetParent(current))
        {
            if (current is not ListBox listBox || listBox.ActualWidth <= 0 || listBox.ActualHeight <= 0)
            {
                continue;
            }

            var center = target.TranslatePoint(
                new Point(target.ActualWidth / 2, target.ActualHeight / 2),
                listBox);
            return new Point(
                center.X <= listBox.ActualWidth / 2 ? 0 : 1,
                center.Y <= listBox.ActualHeight / 2 ? 0 : 1);
        }

        return new Point(0.5, 0.5);
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is FrameworkContentElement contentElement)
        {
            return contentElement.Parent;
        }

        return child is Visual
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
    }

    private static bool IsPlayerCard(object? dataContext)
    {
        if (dataContext is null)
        {
            return false;
        }

        var typeName = dataContext.GetType().Name;
        if (string.Equals(typeName, "LivePlayerIconViewModel", StringComparison.Ordinal))
        {
            return false;
        }

        return typeName.Contains("PlayerCard", StringComparison.Ordinal) ||
               typeName.Contains("PlayerIcon", StringComparison.Ordinal);
    }

    private void ClearStealthHoverZoom()
    {
        if (_stealthPlayerPopover is not null)
        {
            _stealthPlayerPopover.IsOpen = false;
            _stealthPlayerPopover.Child = null;
            _stealthPlayerPopover = null;
        }

        if (_stealthHoverTarget is null)
        {
            return;
        }

        _stealthHoverTarget.RenderTransform = _stealthHoverOriginalTransform ?? Transform.Identity;
        _stealthHoverTarget.RenderTransformOrigin = _stealthHoverOriginalTransformOrigin;
        Panel.SetZIndex(_stealthHoverTarget, _stealthHoverOriginalZIndex);
        _stealthHoverTarget = null;
        _stealthHoverOriginalTransform = null;
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

        if (MainContent.Content is DashboardView dashboardView)
        {
            dashboardView.SetCompactMode(true);
        }

        if (MainContent.Content is PreMatchView preMatchView)
        {
            preMatchView.SetCompactMode(true);
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

    internal void SetStealthLiveMatchCompactMode(bool isCompact)
    {
        if (!IsStealthMode)
        {
            return;
        }

        MinWidth = isCompact ? StealthCompactLiveWindowWidth : StealthWindowWidth;
        MinHeight = isCompact ? StealthCompactLiveWindowHeight : StealthWindowHeight;
        Width = isCompact ? StealthCompactLiveWindowWidth : StealthWindowWidth;
        Height = isCompact ? StealthCompactLiveWindowHeight : StealthWindowHeight;
        ApplicationContentRoot.Width = isCompact ? StealthCompactLiveContentWidth : StealthContentWidth;
        ApplicationContentRoot.Height = isCompact ? StealthCompactLiveContentHeight : StealthContentHeight;
        ApplicationContentRoot.LayoutTransform = isCompact
            ? new ScaleTransform(StealthCompactLiveScale, StealthCompactLiveScale)
            : new ScaleTransform(StealthContentScale, StealthContentScale);
    }

    private void ExitStealthMode()
    {
        ClearStealthHoverZoom();
        if (MainContent.Content is DashboardView dashboardView)
        {
            dashboardView.SetCompactMode(false);
        }
        if (MainContent.Content is PreMatchView preMatchView)
        {
            preMatchView.SetCompactMode(false);
        }
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
        if (IsStealthMode && view is DashboardView dashboardView)
        {
            dashboardView.SetCompactMode(true);
        }
        if (IsStealthMode && view is PreMatchView preMatchView)
        {
            preMatchView.SetCompactMode(true);
        }
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
    private struct NativePoint
    {
        public int X;
        public int Y;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(
        IntPtr windowHandle,
        IntPtr windowRegion,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
