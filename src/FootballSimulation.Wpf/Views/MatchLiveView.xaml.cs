using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class MatchLiveView : UserControl
{
    private const int FastBaseDelayMilliseconds = 3000;
    private const int OnePointFiveBaseDelayMilliseconds = 4000;
    private const int MediumBaseDelayMilliseconds = 6000;
    private const int VerySlowBaseDelayMilliseconds = 10000;
    private const int MinSpeedLevel = 0;
    private const int DefaultSpeedLevel = 1;
    private const int MaxSpeedLevel = 4;
    private const int FirstHalfEndMinute = 45;
    private const int FullTimeMinute = 90;
    private const double PlayerIconSlotWidth = 72;
    private const double PlayerIconSlotHeight = 76;
    private const int MaxPendingSubstitutions = 3;
    private const double CompactLiveViewWidth = 420;
    private const double CompactLiveViewMinWidth = 380;

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly bool _isSecondHalf;
    private readonly GameSessionService _gameSessionService = new();
    private readonly TransferMarketService _transferMarketService = new();
    private readonly SquadSelectionService _squadSelectionService = new();
    private readonly MatchEventFactory _matchEventFactory = new();
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly PlayerFormStatusService _playerFormStatusService = new();
    private readonly ObservableCollection<MatchFeedItem> _visibleEvents = [];
    private readonly ObservableCollection<LivePlayerIconViewModel> _pitchPlayers = [];
    private readonly ObservableCollection<PendingSubstitutionViewModel> _pendingSubstitutions = [];
    private readonly Dictionary<string, DisplayedPitchStats> _displayedPitchStats = [];
    private readonly Dictionary<string, LivePlayerStats> _livePlayerStatsById = [];
    private readonly List<MatchEvent> _pendingPlaybackEvents = [];

    private CancellationTokenSource? _playbackCancellation;
    private int _speedLevel = DefaultSpeedLevel;
    private bool _hasNavigated;
    private bool _isPlaybackPaused;
    private bool _isPausedForSubstitution;
    private bool _isPausedForTacticalAdjustment;
    private bool _fixtureCompleted;
    private bool _isCompactLiveMatchView;
    private bool _hasStoredExpandedWindowSize;
    private bool _isCancellingPendingSubstitution;
    private bool _hasLoggedMinute24OpponentStamina;
    private Player? _selectedStarterForSubstitution;
    private Player? _selectedBenchForSubstitution;
    private Player? _mandatoryInjurySubstitutionPlayer;
    private Team? _currentPossessionTeam;
    private LiveMatchStatus _currentLiveStatus = LiveMatchStatus.Neutral;
    private MatchTeamColorPalettes? _matchTeamColors;
    private EventType? _lastDisplayedEventType;
    private string? _selectedPitchPlayerKey;
    private double _pitchWidth;
    private double _pitchHeight;
    private double _expandedWindowWidth;
    private double _expandedWindowMinWidth;

    private sealed record PitchSlotAssignment(Player Player, PitchPosition Position);
    private WindowState _expandedWindowState;

    public MatchLiveView(GameFlowState state, Action<UserControl> navigate, bool isSecondHalf)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        _isSecondHalf = isSecondHalf;

        MatchEventsListBox.ItemsSource = _visibleEvents;
        PitchPlayersItemsControl.ItemsSource = _pitchPlayers;

        InitializeSpeedControls();
        InitializeTacticalControls();
        PrepareContinueButton();
        ApplyCompactLiveMatchView(_state.IsCompactLiveMatchView, resizeWindow: false);

        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        Loaded += MatchLiveView_Loaded;
        Unloaded += MatchLiveView_Unloaded;
    }

    private async void MatchLiveView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MatchLiveView_Loaded;

        if (!InitializeLiveMatchContext())
        {
            return;
        }

        RefreshPlayerPanels();
        UpdatePlaybackControls();
        ApplyCompactWindowSize(_isCompactLiveMatchView);
        await ResumePlaybackAsync();
    }

    private void MatchLiveView_Unloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
        RestoreExpandedWindowSize();
        CancelPlayback();
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
    {
        RefreshVisibleFeedTheme();
        UpdatePlaybackControls();
    }

    private void CompactViewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCompactLiveMatchView(!_isCompactLiveMatchView, resizeWindow: true);
    }

    private void ApplyCompactLiveMatchView(bool isCompact, bool resizeWindow)
    {
        _isCompactLiveMatchView = isCompact;
        _state.IsCompactLiveMatchView = isCompact;

        LiveMatchRootGrid.Width = double.NaN;
        LiveMatchRootGrid.Margin = isCompact ? new Thickness(12) : new Thickness(20);
        LiveTrackerPanel.Margin = isCompact ? new Thickness(0) : new Thickness(0, 0, 16, 0);
        LiveTrackerColumnDefinition.Width = isCompact ? new GridLength(1, GridUnitType.Star) : new GridLength(320);
        FullMatchColumnDefinition.Width = isCompact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        FullMatchLayoutGrid.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        FullMatchLayoutGrid.Opacity = isCompact ? 0 : 1;
        CompactScoreControlsPanel.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;
        ScoreboardPanel.Padding = isCompact ? new Thickness(10) : new Thickness(12);
        ScoreboardPanel.Margin = isCompact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 12);
        FeedPanel.Padding = isCompact ? new Thickness(10) : new Thickness(12);
        PhaseTextBlock.FontSize = isCompact ? 11 : 12;
        VenueTextBlock.FontSize = isCompact ? 10 : 11;
        ScoreSeparatorTextBlock.Margin = isCompact ? new Thickness(4, 0, 4, 0) : new Thickness(6, 0, 6, 0);

        CompactViewToggleButton.Content = isCompact ? "⤢" : "⤡";
        CompactViewToggleButton.ToolTip = isCompact ? "Expand Match View" : "Compact View";
        AnimateCompactTransition(isCompact);
        UpdatePlaybackControls();

        if (resizeWindow)
        {
            ApplyCompactWindowSize(isCompact);
        }
    }

    private void AnimateCompactTransition(bool isCompact)
    {
        var duration = TimeSpan.FromMilliseconds(180);
        var targetScale = 1.0;
        ScoreboardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration));
        ScoreboardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration));
        LiveTrackerPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.92, 1.0, duration));
    }

    private void ApplyCompactWindowSize(bool isCompact)
    {
        var window = Window.GetWindow(this);
        if (window is null)
        {
            return;
        }

        if (!isCompact)
        {
            RestoreExpandedWindowSize();
            return;
        }

        if (!_hasStoredExpandedWindowSize)
        {
            _expandedWindowWidth = window.Width;
            _expandedWindowMinWidth = window.MinWidth;
            _expandedWindowState = window.WindowState;
            _hasStoredExpandedWindowSize = true;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.MinWidth = CompactLiveViewMinWidth;
        window.Width = CompactLiveViewWidth;
    }

    private void RestoreExpandedWindowSize()
    {
        var window = Window.GetWindow(this);
        if (window is null || !_hasStoredExpandedWindowSize)
        {
            return;
        }

        window.MinWidth = _expandedWindowMinWidth;
        window.Width = Math.Max(_expandedWindowWidth, _expandedWindowMinWidth);
        window.WindowState = _expandedWindowState;
        _hasStoredExpandedWindowSize = false;
    }

    private bool InitializeLiveMatchContext()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return false;
        }

        if (!_isSecondHalf)
        {
            if (_state.CurrentMatch is null || !IsCurrentMatchForFixture(_state.CurrentMatch, _state.CurrentFixture))
            {
                _state.CurrentMatch = _gameSessionService.CreateSelectedTeamLiveMatch(_state.League, _state.SelectedTeam);
            }
        }
        else if (_state.CurrentMatch is null)
        {
            return false;
        }

        _matchTeamColors = TeamColorService.GetMatchPalettes(_state.CurrentMatch!.HomeTeam, _state.CurrentMatch.AwayTeam);
        SetScoreboardTeams(_state.CurrentMatch.HomeTeam, _state.CurrentMatch.AwayTeam);
        SetScore(_state.CurrentMatch.HomeScore, _state.CurrentMatch.AwayScore);
        PhaseTextBlock.Text = CreatePhaseLabel(_state.CurrentMatch.CurrentMinute);
        UpdateVenueLabel(_state.CurrentMatch);
        UpdateLiveStatusVisuals(LiveMatchStatus.Neutral, attackingTeam: null, defendingTeam: null);
        InitializeDisplayedPitchStats();
        LoadPausedActionPanel();
        return true;
    }

    private static bool IsCurrentMatchForFixture(Match match, Fixture fixture)
    {
        return match.HomeTeam == fixture.HomeTeam &&
            match.AwayTeam == fixture.AwayTeam;
    }

    private void InitializeSpeedControls()
    {
        _speedLevel = GetSpeedLevelFromState(_state.CurrentMatchSpeed);
        UpdateSpeedControls();
    }

    private void DecreaseSpeedButton_Click(object sender, RoutedEventArgs e)
    {
        _speedLevel = Math.Max(MinSpeedLevel, _speedLevel - 1);
        SaveSpeedLevelToState();
        UpdateSpeedControls();
    }

    private void IncreaseSpeedButton_Click(object sender, RoutedEventArgs e)
    {
        _speedLevel = Math.Min(MaxSpeedLevel, _speedLevel + 1);
        SaveSpeedLevelToState();
        UpdateSpeedControls();
    }

    private void InitializeTacticalControls()
    {
        if (_state.SelectedTeam is not null)
        {
            ActionTacticalSettingsPanel.LoadTactics(_state.SelectedTeam.Tactics);
            TacticalSettingsPanel.LoadTactics(_state.SelectedTeam.Tactics);
        }
    }

    private void PrepareContinueButton()
    {
        ContinueButton.Content = _isSecondHalf ? "End" : "Continue";
        ContinueButton.Visibility = Visibility.Collapsed;
        ContinueButton.IsEnabled = false;
        CompactContinueButton.Content = ContinueButton.Content;
        CompactContinueButton.Visibility = Visibility.Collapsed;
        CompactContinueButton.IsEnabled = false;
    }

    private async Task ResumePlaybackAsync()
    {
        if (_state.CurrentMatch is null || _state.League is null || _state.CurrentFixture is null || _state.SelectedTeam is null)
        {
            return;
        }

        if (_state.CurrentMatch.CurrentMinute >= GetPhaseEndMinute() && _pendingPlaybackEvents.Count == 0)
        {
            await CompletePhaseAsync();
            return;
        }

        if (_isPlaybackPaused)
        {
            return;
        }

        _isPausedForSubstitution = false;
        _isPausedForTacticalAdjustment = false;
        UpdatePlaybackControls();

        _playbackCancellation = new CancellationTokenSource();
        var cancellationToken = _playbackCancellation.Token;

        try
        {
            while (_pendingPlaybackEvents.Count > 0 || _state.CurrentMatch.CurrentMinute < GetPhaseEndMinute())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_pendingPlaybackEvents.Count > 0)
                {
                    await PlayPendingEventsAsync(cancellationToken);
                    RefreshPlayerPanels();
                    continue;
                }

                var nextMinute = _state.CurrentMatch.CurrentMinute + 1;
                var includeFulltime = _isSecondHalf && nextMinute >= GetPhaseEndMinute();
                var existingEventCount = _state.CurrentMatch.Events.Count;

                _state.CurrentMatch = _gameSessionService.AdvanceSelectedTeamLiveMatch(
                    _state.League,
                    _state.CurrentFixture,
                    _state.CurrentMatch,
                    _state.SelectedTeam,
                    nextMinute,
                    nextMinute,
                    includeFulltime);

                PhaseTextBlock.Text = CreatePhaseLabel(_state.CurrentMatch.CurrentMinute);
                UpdateVenueLabel(_state.CurrentMatch);

                _pendingPlaybackEvents.AddRange(_state.CurrentMatch.Events
                    .Skip(existingEventCount)
                    .OrderBy(matchEvent => matchEvent.Minute)
                    .ToList());

                await PlayPendingEventsAsync(cancellationToken);
                RefreshPlayerPanels();
            }

            await CompletePhaseAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            UpdatePlaybackControls();
        }
    }

    private async Task PlayPendingEventsAsync(CancellationToken cancellationToken)
    {
        while (_pendingPlaybackEvents.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

            var matchEvent = _pendingPlaybackEvents[0];
            _pendingPlaybackEvents.RemoveAt(0);
            await PlayMatchEventAsync(matchEvent, cancellationToken);
            if (_isPlaybackPaused)
            {
                return;
            }
        }

        SyncLivePlayerRatings();
    }

    private async Task PlayMatchEventAsync(MatchEvent matchEvent, CancellationToken cancellationToken)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

                    var feedItem = CreateFeedItem(matchEvent, _state.CurrentMatch);
                    InsertFeedItemAtTop(feedItem);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        RecordDisplayedPitchStats(matchEvent);
        RecordDisplayedPitchRating(matchEvent);
        SyncLivePlayerStaminaForActivePlayers();
        _lastDisplayedEventType = matchEvent.EventType;
        if (ShouldRefreshPitchAfterEvent(matchEvent.EventType))
        {
            RefreshPlayerPanels();
        }

                    UpdateLiveStatusFromEvent(matchEvent);

                    if (matchEvent.HomeScore.HasValue && matchEvent.AwayScore.HasValue)
                    {
                        SetScore(matchEvent.HomeScore.Value, matchEvent.AwayScore.Value);
                    }

        await Task.Delay(GetRevealDelayMilliseconds(feedItem), cancellationToken);

                    if (feedItem.IsGoal)
                    {
                        await PlayGoalEffectAsync(feedItem.TeamName, cancellationToken);
                    }

        if (matchEvent.EventType == EventType.Injury && TryEnterMandatoryInjuryPause(matchEvent))
        {
            return;
        }

        if (IsSubstitutionStoppageEvent(matchEvent.EventType))
        {
            TryCommitPendingSubstitution();
        }

        await Task.Delay(GetDelayFor(feedItem), cancellationToken);
    }

    private async Task CompletePhaseAsync()
    {
        if (_state.CurrentMatch is null || _state.League is null || _state.CurrentFixture is null)
        {
            return;
        }

        if (_isSecondHalf && !_fixtureCompleted)
        {
            _gameSessionService.CompleteSelectedTeamLiveMatch(_state.League, _state.CurrentFixture, _state.CurrentMatch);
            if (_state.SelectedTeam is not null)
            {
                _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
                _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
                _transferMarketService.RunAiTransferActivity(
                    _state.TransferMarket,
                    _state.League,
                    _state.SelectedTeam,
                    _state.CurrentFixture.RoundNumber);
            }

            _fixtureCompleted = true;
        }

        ContinueButton.Visibility = Visibility.Visible;
        ContinueButton.IsEnabled = true;
        CompactContinueButton.Content = ContinueButton.Content;
        CompactContinueButton.Visibility = Visibility.Visible;
        CompactContinueButton.IsEnabled = true;
        UpdatePlaybackControls();
        await Task.CompletedTask;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hasNavigated)
        {
            return;
        }

        _hasNavigated = true;
        CancelPlayback();

        if (_isSecondHalf)
        {
            _navigate(new MatchResultView(_state, _navigate));
            return;
        }

        _navigate(new HalfTimeView(_state, _navigate));
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        CancelPlayback();
        _pendingPlaybackEvents.Clear();

        if (_state.CurrentMatch is not null &&
            _state.League is not null &&
            _state.CurrentFixture is not null &&
            _state.SelectedTeam is not null &&
            _state.CurrentMatch.CurrentMinute < GetPhaseEndMinute())
        {
            while (_state.CurrentMatch.CurrentMinute < GetPhaseEndMinute())
            {
                var nextMinute = _state.CurrentMatch.CurrentMinute + 1;
                var includeFulltime = _isSecondHalf && nextMinute >= GetPhaseEndMinute();
                _state.CurrentMatch = _gameSessionService.AdvanceSelectedTeamLiveMatch(
                    _state.League,
                    _state.CurrentFixture,
                    _state.CurrentMatch,
                    _state.SelectedTeam,
                    nextMinute,
                    nextMinute,
                    includeFulltime);
            }

            SetScore(_state.CurrentMatch.HomeScore, _state.CurrentMatch.AwayScore);
            InitializeDisplayedPitchStats();
            RefreshPlayerPanels();
            await CompletePhaseAsync();
        }

        ContinueButton_Click(sender, e);
    }

    private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentMatch is null || _hasNavigated)
        {
            return;
        }

        if (_isPlaybackPaused)
        {
            ResumeFromPausedActionPanel();
            return;
        }

        EnterPausedMode();
    }

    private void EnterPausedMode()
    {
        CancelPlayback();
        _isPlaybackPaused = true;
        LoadPausedActionPanel();
        UpdatePlaybackControls();
    }

    private void ResumeFromPausedActionPanel()
    {
        if (HasMandatoryInjurySubstitution() && !HasQueuedMandatoryInjurySubstitution())
        {
            PausedActionStatusTextBlock.Text = $"{_mandatoryInjurySubstitutionPlayer!.Name} cannot continue. Queue a substitute before resuming.";
            UpdatePlaybackControls();
            return;
        }

        if (!TryCommitPendingSubstitution())
        {
            UpdatePlaybackControls();
            return;
        }

        ApplyInlineTacticalSettings();
        _mandatoryInjurySubstitutionPlayer = null;
        _isPlaybackPaused = false;
        _isPausedForSubstitution = false;
        _isPausedForTacticalAdjustment = false;
        UpdatePlaybackControls();
        _ = ResumePlaybackAsync();
    }

    private void LoadPausedActionPanel()
    {
        var userTeam = GetUserTeam();
        ActionTacticalSettingsPanel.LoadTactics(userTeam.Tactics);
        RefreshPausedSubstitutionViews();
        if (HasMandatoryInjurySubstitution())
        {
            PausedActionStatusTextBlock.Text = $"{_mandatoryInjurySubstitutionPlayer!.Name} cannot continue. Select a substitute to replace him.";
            PausedActionStatusTextBlock.Visibility = Visibility.Visible;
        }
        else if (HasPendingSubstitution())
        {
            PausedActionStatusTextBlock.Text = "Queued substitutions will be made at the next stoppage.";
            PausedActionStatusTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            PausedActionStatusTextBlock.Text = string.Empty;
            PausedActionStatusTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private bool TryEnterMandatoryInjuryPause(MatchEvent matchEvent)
    {
        if (_state.CurrentMatch is null || _state.SelectedTeam is null || string.IsNullOrWhiteSpace(matchEvent.PrimaryPlayerName))
        {
            return false;
        }

        var injuredPlayer = GetActivePitchPlayers(_state.SelectedTeam)
            .FirstOrDefault(player => string.Equals(player.Name, matchEvent.PrimaryPlayerName, StringComparison.OrdinalIgnoreCase));
        if (injuredPlayer is null)
        {
            return false;
        }

        _mandatoryInjurySubstitutionPlayer = injuredPlayer;
        _selectedPitchPlayerKey = CreatePlayerId(_state.SelectedTeam, injuredPlayer);
        _isPlaybackPaused = true;
        _isPausedForSubstitution = false;
        _isPausedForTacticalAdjustment = false;
        LoadPausedActionPanel();
        RefreshPlayerPanels();
        UpdatePlaybackControls();
        return true;
    }

    private bool HasMandatoryInjurySubstitution()
    {
        return _mandatoryInjurySubstitutionPlayer is not null &&
            _mandatoryInjurySubstitutionPlayer.IsInjured &&
            _mandatoryInjurySubstitutionPlayer.IsOnPitch;
    }

    private bool HasQueuedMandatoryInjurySubstitution()
    {
        return !HasMandatoryInjurySubstitution() ||
            _pendingSubstitutions.Any(pending =>
                string.Equals(pending.PlayerOut.Name, _mandatoryInjurySubstitutionPlayer!.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyInlineTacticalSettings()
    {
        var userTeam = GetUserTeam();
        ActionTacticalSettingsPanel.ApplyTo(userTeam.Tactics);
        ApplyTacticalLiveModifiers(userTeam);
    }

    private void MakeSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentMatch is null || _state.SelectedTeam is null)
        {
            return;
        }

        CancelPlayback();
        _isPausedForSubstitution = true;
        _selectedStarterForSubstitution = null;
        _selectedBenchForSubstitution = null;

        var userTeam = GetUserTeam();

        LiveStarterListBox.ItemsSource = CreateSubstitutionPlayerCards(GetActivePitchPlayers(userTeam), showPendingState: false, userTeam);
        LiveBenchListBox.ItemsSource = CreateSubstitutionPlayerCards(userTeam.Substitutes.Where(IsAvailableSubstitute), showPendingState: false, userTeam);

        var usedSubstitutions = _squadSelectionService.CountTeamSubstitutions(_state.CurrentMatch, userTeam.Name);
        SubstitutionOverlayStatusTextBlock.Text =
            $"{MatchEngine.FormatDisplayMinute(_state.CurrentMatch, _state.CurrentMatch.CurrentMinute)} | {usedSubstitutions}/5 substitutions used. Choose one starter and one substitute.";

        ConfirmSubstitutionButton.IsEnabled = false;
        SubstitutionOverlay.Visibility = Visibility.Visible;
        UpdatePlaybackControls();
    }

    private void CancelSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        SubstitutionOverlay.Visibility = Visibility.Collapsed;
        _selectedStarterForSubstitution = null;
        _selectedBenchForSubstitution = null;
        ConfirmSubstitutionButton.IsEnabled = false;
        _isPausedForSubstitution = false;
        _isPlaybackPaused = true;
        UpdatePlaybackControls();
    }

    private void TacticalAdjustmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        CancelPlayback();
        _isPausedForTacticalAdjustment = true;

        var userTeam = GetUserTeam();
        TacticalOverlayStatusTextBlock.Text = $"{MatchEngine.FormatDisplayMinute(_state.CurrentMatch, _state.CurrentMatch.CurrentMinute)} | Adjust tactics for the next phase of play.";
        TacticalSettingsPanel.LoadTactics(userTeam.Tactics);

        TacticalAdjustmentOverlay.Visibility = Visibility.Visible;
        UpdatePlaybackControls();
    }

    private void CancelTacticalAdjustmentButton_Click(object sender, RoutedEventArgs e)
    {
        TacticalAdjustmentOverlay.Visibility = Visibility.Collapsed;
        _isPausedForTacticalAdjustment = false;
        _ = ResumePlaybackAsync();
    }

    private void ConfirmTacticalAdjustmentButton_Click(object sender, RoutedEventArgs e)
    {
        var userTeam = GetUserTeam();
        TacticalSettingsPanel.ApplyTo(userTeam.Tactics);
        ApplyTacticalLiveModifiers(userTeam);

        TacticalAdjustmentOverlay.Visibility = Visibility.Collapsed;
        _isPausedForTacticalAdjustment = false;
        RefreshPitchPlayers();
        UpdatePlaybackControls();
        _ = ResumePlaybackAsync();
    }

    private void LiveStarterListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedStarterForSubstitution = (LiveStarterListBox.SelectedItem as SubstitutionPlayerCard)?.Player;
        UpdateConfirmSubstitutionButton();
    }

    private void LiveBenchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedBenchForSubstitution = (LiveBenchListBox.SelectedItem as SubstitutionPlayerCard)?.Player;
        UpdateConfirmSubstitutionButton();
    }

    private void PausedBenchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isCancellingPendingSubstitution)
        {
            PausedBenchListBox.SelectedItem = null;
            return;
        }

        if (!_isPlaybackPaused || PausedBenchListBox.SelectedItem is not SubstitutionPlayerCard substituteCard)
        {
            return;
        }

        QueuePausedSubstitution(substituteCard.Player);
        PausedBenchListBox.SelectedItem = null;
    }

    private void BenchPlayerCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPlaybackPaused ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: SubstitutionPlayerCard substituteCard })
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, substituteCard.Player, DragDropEffects.Move);
    }

    private void CancelPendingSubstitutionButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isCancellingPendingSubstitution = true;
    }

    private void PitchPlayerIcon_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPlaybackPaused ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: LivePlayerIconViewModel pitchPlayer } ||
            !IsUserTeamPitchPlayer(pitchPlayer))
        {
            return;
        }

        _selectedPitchPlayerKey = pitchPlayer.PlayerKey;
        DragDrop.DoDragDrop((DependencyObject)sender, pitchPlayer, DragDropEffects.Move);
    }

    private void PitchPlayerIcon_Drop(object sender, DragEventArgs e)
    {
        if (!_isPlaybackPaused ||
            sender is not FrameworkElement { DataContext: LivePlayerIconViewModel selectedPlayer })
        {
            return;
        }

        if (e.Data.GetData(typeof(LivePlayerIconViewModel)) is LivePlayerIconViewModel draggedPitchPlayer)
        {
            SwapPausedPitchPlayers(draggedPitchPlayer, selectedPlayer);
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(Player)) is Player substitute)
        {
        _selectedPitchPlayerKey = selectedPlayer.PlayerKey;
        QueuePausedSubstitution(substitute);
        }

        e.Handled = true;
    }

    private void SwapPausedPitchPlayers(LivePlayerIconViewModel draggedPitchPlayer, LivePlayerIconViewModel targetPitchPlayer)
    {
        if (_state.CurrentMatch is null ||
            string.Equals(draggedPitchPlayer.PlayerKey, targetPitchPlayer.PlayerKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsUserTeamPitchPlayer(draggedPitchPlayer) || !IsUserTeamPitchPlayer(targetPitchPlayer))
        {
            PausedActionStatusTextBlock.Text = "Only your on-pitch players can be swapped while paused.";
            return;
        }

        var userTeam = GetUserTeam();
        var draggedPlayer = FindActivePlayer(userTeam, draggedPitchPlayer.Name);
        var targetPlayer = FindActivePlayer(userTeam, targetPitchPlayer.Name);
        if (draggedPlayer is null || targetPlayer is null)
        {
            PausedActionStatusTextBlock.Text = "That player swap is no longer available.";
            return;
        }

        var draggedSlot = PositionSuitabilityService.NormalizeExactPosition(draggedPitchPlayer.ExactPosition);
        var targetSlot = PositionSuitabilityService.NormalizeExactPosition(targetPitchPlayer.ExactPosition);
        if ((targetSlot == "GK" && !PositionSuitabilityService.IsGoalkeeperCapable(draggedPlayer)) ||
            (draggedSlot == "GK" && !PositionSuitabilityService.IsGoalkeeperCapable(targetPlayer)))
        {
            PausedActionStatusTextBlock.Text = "Only a goalkeeper-capable player can occupy the GK slot.";
            return;
        }

        var draggedIndex = userTeam.Players.IndexOf(draggedPlayer);
        var targetIndex = userTeam.Players.IndexOf(targetPlayer);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            PausedActionStatusTextBlock.Text = "That player swap is no longer available.";
            return;
        }

        (userTeam.Players[draggedIndex], userTeam.Players[targetIndex]) = (userTeam.Players[targetIndex], userTeam.Players[draggedIndex]);
        PositionSuitabilityService.EnsurePositionMetadata(draggedPlayer, targetSlot);
        PositionSuitabilityService.EnsurePositionMetadata(targetPlayer, draggedSlot);
        var draggedOutOfPosition = ApplyPositionSwapImpact(userTeam, draggedPlayer);
        var targetOutOfPosition = ApplyPositionSwapImpact(userTeam, targetPlayer);

        _selectedPitchPlayerKey = CreatePlayerId(userTeam, draggedPlayer);
        PausedActionStatusTextBlock.Text = CreatePositionSwapStatus(
            draggedPlayer,
            targetPlayer,
            targetSlot,
            draggedSlot,
            draggedOutOfPosition,
            targetOutOfPosition);
        RefreshPlayerPanels();
        UpdatePlaybackControls();
    }

    private bool IsUserTeamPitchPlayer(LivePlayerIconViewModel pitchPlayer)
    {
        return _state.SelectedTeam is not null &&
            string.Equals(pitchPlayer.TeamName, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static Player? FindActivePlayer(Team team, string playerName)
    {
        return GetActivePitchPlayers(team).FirstOrDefault(player =>
            string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreatePositionSwapStatus(
        Player draggedPlayer,
        Player targetPlayer,
        string targetSlot,
        string draggedSlot,
        bool draggedOutOfPosition,
        bool targetOutOfPosition)
    {
        var baseMessage = $"{draggedPlayer.Name} moved to {targetSlot}; {targetPlayer.Name} moved to {draggedSlot}.";
        var penalizedPlayers = new[]
            {
                draggedOutOfPosition ? draggedPlayer.Name : string.Empty,
                targetOutOfPosition ? targetPlayer.Name : string.Empty
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return penalizedPlayers.Count == 0
            ? baseMessage
            : $"{baseMessage} Warning: {string.Join(", ", penalizedPlayers)} out of position, OVR and performance reduced.";
    }

    private bool ApplyPositionSwapImpact(Team team, Player player)
    {
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(player);
        if (suitability >= 1.0)
        {
            player.LiveMatchModifier = Math.Max(player.LiveMatchModifier, 1.0);
            return false;
        }

        player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier * suitability, 0.75, 1.15);
        GetOrCreateLivePerformance(team, player).Rating -= 0.15;
        SyncLivePlayerRating(player.Name, team);
        return true;
    }

    private void ConfirmSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentMatch is null || _selectedStarterForSubstitution is null || _selectedBenchForSubstitution is null)
        {
            return;
        }

        _selectedPitchPlayerKey = CreatePlayerId(GetUserTeam(), _selectedStarterForSubstitution);
        if (!QueuePendingSubstitution(_selectedStarterForSubstitution, _selectedBenchForSubstitution))
        {
            return;
        }

        SubstitutionOverlay.Visibility = Visibility.Collapsed;
        _selectedStarterForSubstitution = null;
        _selectedBenchForSubstitution = null;
        ConfirmSubstitutionButton.IsEnabled = false;
        _isPausedForSubstitution = false;
        _isPlaybackPaused = true;
        PausedActionStatusTextBlock.Text = "Queued substitutions confirm on resume.";
        UpdatePlaybackControls();
    }

    private void CancelPendingSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        _isCancellingPendingSubstitution = true;
        if (sender is FrameworkElement { DataContext: PendingSubstitutionViewModel pendingSubstitution })
        {
            _pendingSubstitutions.Remove(pendingSubstitution);
        }
        else if (sender is FrameworkElement { DataContext: SubstitutionPlayerCard { PendingSubstitution: not null } substituteCard })
        {
            _pendingSubstitutions.Remove(substituteCard.PendingSubstitution);
        }
        else
        {
            ClearPendingSubstitutions();
        }

        PausedActionStatusTextBlock.Text = HasMandatoryInjurySubstitution() && !HasQueuedMandatoryInjurySubstitution()
            ? $"{_mandatoryInjurySubstitutionPlayer!.Name} cannot continue. Queue a substitute before resuming."
            : _pendingSubstitutions.Count == 0
            ? "No pending substitutions."
            : "Queued substitutions confirm on resume.";
        PausedBenchListBox.SelectedItem = null;
        RefreshPausedSubstitutionViews();
        _isCancellingPendingSubstitution = false;
        e.Handled = true;
        UpdatePlaybackControls();
    }

    private void QueuePausedSubstitution(Player substitute)
    {
        if (_state.CurrentMatch is null || _selectedPitchPlayerKey is null)
        {
            PausedActionStatusTextBlock.Text = "Select a starter on the pitch before choosing a substitute.";
            return;
        }

        var userTeam = GetUserTeam();
        var starter = GetActivePitchPlayers(userTeam).FirstOrDefault(player =>
            string.Equals(CreatePlayerId(userTeam, player), _selectedPitchPlayerKey, StringComparison.OrdinalIgnoreCase));

        if (starter is null)
        {
            PausedActionStatusTextBlock.Text = "The selected player is not in your current XI.";
            return;
        }

        QueuePendingSubstitution(starter, substitute);
    }

    private bool QueuePendingSubstitution(Player starter, Player substitute)
    {
        if (HasMandatoryInjurySubstitution() &&
            !string.Equals(starter.Name, _mandatoryInjurySubstitutionPlayer!.Name, StringComparison.OrdinalIgnoreCase))
        {
            PausedActionStatusTextBlock.Text = $"{_mandatoryInjurySubstitutionPlayer.Name} is injured and must be replaced first.";
            return false;
        }

        if (GetUserSubstitutionsLeft() <= 0)
        {
            PausedActionStatusTextBlock.Text = "No substitutions remaining.";
            return false;
        }

        if (_pendingSubstitutions.Any(pending =>
            string.Equals(pending.PlayerIn.Name, substitute.Name, StringComparison.OrdinalIgnoreCase)))
        {
            PausedActionStatusTextBlock.Text = $"{substitute.Name} is already queued to come on.";
            return false;
        }

        var existingStarterPending = _pendingSubstitutions.FirstOrDefault(pending =>
            string.Equals(pending.PlayerOut.Name, starter.Name, StringComparison.OrdinalIgnoreCase));
        if (existingStarterPending is not null)
        {
            _pendingSubstitutions.Remove(existingStarterPending);
        }

        if (_pendingSubstitutions.Count >= Math.Min(MaxPendingSubstitutions, GetUserSubstitutionsLeft()))
        {
            PausedActionStatusTextBlock.Text = $"Maximum {MaxPendingSubstitutions} pending substitutions at once.";
            return false;
        }

        _pendingSubstitutions.Add(new PendingSubstitutionViewModel(substitute, starter));
        PausedActionStatusTextBlock.Text = HasMandatoryInjurySubstitution()
            ? $"{starter.Name} injury replacement queued. Resume to confirm."
            : "Substitution queued. It will be made at the next stoppage.";
        RefreshPausedSubstitutionViews();
        UpdatePlaybackControls();
        return true;
    }

    private bool TryCommitPendingSubstitution()
    {
        if (!HasPendingSubstitution())
        {
            return true;
        }

        if (_state.CurrentMatch is null)
        {
            ClearPendingSubstitutions();
            return true;
        }

        var userTeam = GetUserTeam();
        var pendingSubstitutions = _pendingSubstitutions.ToList();
        if (!CanApplyPendingSubstitutionNow())
        {
            PausedActionStatusTextBlock.Text = "Substitution pending until the next stoppage.";
            RefreshPausedSubstitutionViews();
            return true;
        }

        if (pendingSubstitutions.Count > GetUserSubstitutionsLeft())
        {
            PausedActionStatusTextBlock.Text = "Not enough substitutions remaining for the queued changes.";
            return false;
        }

        var minute = Math.Max(1, _state.CurrentMatch.CurrentMinute);
        var validatedSubstitutions = new List<(Player Starter, Player Substitute)>();

        foreach (var pendingSubstitution in pendingSubstitutions)
        {
            var starter = GetActivePitchPlayers(userTeam).FirstOrDefault(player =>
                string.Equals(player.Name, pendingSubstitution.PlayerOut.Name, StringComparison.OrdinalIgnoreCase));
            var substitute = userTeam.Substitutes.FirstOrDefault(player =>
                IsAvailableSubstitute(player) &&
                string.Equals(player.Name, pendingSubstitution.PlayerIn.Name, StringComparison.OrdinalIgnoreCase));

            if (starter is null || substitute is null)
            {
                PausedActionStatusTextBlock.Text = "A queued substitution is no longer valid. Remove it and choose again.";
                return false;
            }

            validatedSubstitutions.Add((starter, substitute));
        }

        foreach (var (starter, substitute) in validatedSubstitutions)
        {
            PositionSuitabilityService.EnsurePositionMetadata(starter);
            var incomingAssignedPosition = starter.AssignedPosition;
            var swapResult = _squadSelectionService.SwapStarterWithSubstitute(
                userTeam,
                starter,
                substitute,
                _state.CurrentMatch,
                minute);

            if (!swapResult.Success)
            {
                PausedActionStatusTextBlock.Text = swapResult.Message;
                return false;
            }

            starter.AssignedPosition = starter.PreferredPosition;
            PositionSuitabilityService.EnsurePositionMetadata(substitute, incomingAssignedPosition);
            ApplyManualSubstitutionImpact(starter, substitute);
            _state.CurrentMatch.SuperSubBoosts[substitute.Name] = minute + 10;

            var substitutionEvent = CreateManualSubstitutionEvent(minute, userTeam, starter, substitute);
            _state.CurrentMatch.Events.Add(substitutionEvent);
            InsertFeedItemAtTop(CreateFeedItem(substitutionEvent, _state.CurrentMatch));
            UpdateLiveStatusFromEvent(substitutionEvent);

            _selectedPitchPlayerKey = CreatePlayerId(userTeam, substitute);
        }

        PausedBenchListBox.ItemsSource = CreateSubstitutionPlayerCards(userTeam.Substitutes.Where(IsAvailableSubstitute), showPendingState: true, userTeam);
        PausedActionStatusTextBlock.Text = $"{validatedSubstitutions.Count} substitution(s) confirmed.";
        ClearPendingSubstitutions();
        RefreshPlayerPanels();
        return true;
    }

    private MatchEvent CreateManualSubstitutionEvent(int minute, Team userTeam, Player starter, Player substitute)
    {
        return _lastDisplayedEventType.HasValue
            ? _matchEventFactory.CreateStoppageSubstitution(minute, userTeam, starter, substitute, _lastDisplayedEventType.Value)
            : _matchEventFactory.CreateSubstitution(minute, userTeam, starter, substitute);
    }

    private bool CanApplyPendingSubstitutionNow()
    {
        if (HasMandatoryInjurySubstitution())
        {
            return true;
        }

        return _state.CurrentMatch?.CurrentPhase == MatchPhase.Halftime ||
            IsSubstitutionStoppageEvent(_lastDisplayedEventType);
    }

    private bool HasPendingSubstitution()
    {
        return _pendingSubstitutions.Count > 0;
    }

    private static bool IsSubstitutionStoppageEvent(EventType? eventType)
    {
        return eventType is EventType.Halftime
            or EventType.Injury
            or EventType.Foul
            or EventType.PenaltyDecision
            or EventType.PenaltyTaker
            or EventType.Penalty
            or EventType.Offside
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Goal
            or EventType.WonderGoal
            or EventType.VarCheck
            or EventType.VarDecision
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.RefereeControversy
            or EventType.Confrontation;
    }

    private void ClearPendingSubstitutions()
    {
        _pendingSubstitutions.Clear();
    }

    private void RefreshPausedSubstitutionViews()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        var userTeam = GetUserTeam();
        var benchPlayers = GetFilteredPausedBenchPlayers(userTeam).ToList();
        PausedBenchListBox.ItemsSource = CreateSubstitutionPlayerCards(
            benchPlayers,
            showPendingState: true,
            userTeam);
        var shouldShowEmptyState = _selectedPitchPlayerKey is not null && benchPlayers.Count == 0;
        PausedBenchListBox.Visibility = shouldShowEmptyState ? Visibility.Collapsed : Visibility.Visible;
        NoAvailableSubstituteTextBlock.Visibility = shouldShowEmptyState ? Visibility.Visible : Visibility.Collapsed;
        RefreshPitchPlayers();
    }

    private IEnumerable<Player> GetFilteredPausedBenchPlayers(Team userTeam)
    {
        var availableSubstitutes = userTeam.Substitutes.Where(IsAvailableSubstitute).ToList();
        var selectedPlayer = GetSelectedActiveUserPlayer(userTeam);
        if (selectedPlayer is null)
        {
            return availableSubstitutes;
        }

        PositionSuitabilityService.EnsurePositionMetadata(selectedPlayer);
        var selectedPosition = PositionSuitabilityService.NormalizeExactPosition(selectedPlayer.AssignedPosition);
        if (string.IsNullOrWhiteSpace(selectedPosition))
        {
            selectedPosition = PositionSuitabilityService.NormalizeExactPosition(selectedPlayer.PreferredPosition);
        }

        if (string.IsNullOrWhiteSpace(selectedPosition))
        {
            return availableSubstitutes;
        }

        return availableSubstitutes
            .Where(substitute => CanPlayerCoverPosition(substitute, selectedPosition))
            .OrderByDescending(substitute => PositionCompatibilityService.GetCompatibilityScore(substitute, selectedPosition))
            .ThenByDescending(GetOverallRating)
            .ThenBy(substitute => substitute.SquadNumber <= 0 ? int.MaxValue : substitute.SquadNumber)
            .ThenBy(substitute => substitute.Name);
    }

    private Player? GetSelectedActiveUserPlayer(Team userTeam)
    {
        if (string.IsNullOrWhiteSpace(_selectedPitchPlayerKey))
        {
            return null;
        }

        return GetActivePitchPlayers(userTeam).FirstOrDefault(player =>
            string.Equals(CreatePlayerId(userTeam, player), _selectedPitchPlayerKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanPlayerCoverPosition(Player player, string exactPosition)
    {
        var normalizedPosition = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        if (string.IsNullOrWhiteSpace(normalizedPosition))
        {
            return false;
        }

        return PositionCompatibilityService.CanOccupySlot(player, normalizedPosition, allowOutOfPosition: true);
    }

    private bool IsAvailableSubstitute(Player player)
    {
        if (player.IsSentOff || player.IsSuspended || player.IsInjured)
        {
            return false;
        }

        return _state.CurrentMatch is null ||
            !_squadSelectionService.WasPlayerSubstitutedOff(_state.CurrentMatch, GetUserTeam().Name, player.Name);
    }

    private void ApplyManualSubstitutionImpact(Player playerOff, Player playerOn)
    {
        playerOff.LiveMatchModifier = 1.0;

        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(playerOn);
        if (suitability < 1.0)
        {
            playerOn.LiveMatchModifier = Math.Clamp(0.92 * suitability, 0.75, 1.15);
            GetOrCreateLivePerformance(GetUserTeam(), playerOn).Rating -= 0.15;
            SyncLivePlayerRating(playerOn.Name, GetUserTeam());
            return;
        }

        if (playerOn.Position == playerOff.Position)
        {
            playerOn.LiveMatchModifier = 1.15;
            return;
        }

        playerOn.LiveMatchModifier = 1.02;
    }

    private PlayerMatchPerformance GetOrCreateLivePerformance(Team team, Player player)
    {
        if (_state.CurrentMatch is null)
        {
            return new PlayerMatchPerformance { PlayerName = player.Name, TeamName = team.Name, Position = player.Position };
        }

        var performance = _state.CurrentMatch.PlayerPerformances.FirstOrDefault(existing =>
            string.Equals(existing.TeamName, team.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase));

        if (performance is not null)
        {
            return performance;
        }

        performance = new PlayerMatchPerformance
        {
            PlayerName = player.Name,
            TeamName = team.Name,
            Position = player.Position,
            FatigueAtStart = 100 - GetStaminaPercentage(player),
            FatigueAtEnd = 100 - GetStaminaPercentage(player)
        };
        _state.CurrentMatch.PlayerPerformances.Add(performance);
        return performance;
    }

    private static void ApplyTacticalLiveModifiers(Team team)
    {
        var attackingBonus = team.Tactics.Mentality switch
        {
            Mentality.AllOutAttack => 0.12,
            Mentality.Attacking => 0.07,
            Mentality.UltraDefensive => -0.04,
            Mentality.Defensive => -0.02,
            _ => 0.0
        };
        var defensiveBonus = team.Tactics.Mentality switch
        {
            Mentality.UltraDefensive => 0.08,
            Mentality.Defensive => 0.05,
            Mentality.Attacking => -0.03,
            Mentality.AllOutAttack => -0.08,
            _ => 0.0
        };
        var intensityPenalty = Math.Max(0, team.Tactics.PressingIntensity + team.Tactics.Tempo - 135) / 500.0;

        foreach (var player in GetActivePitchPlayers(team))
        {
            var fatiguePenalty = player.Stamina <= 30 ? (30 - player.Stamina) / 200.0 : 0.0;
            var positionBonus = player.Position switch
            {
                Position.Forward => attackingBonus,
                Position.Midfielder => attackingBonus * 0.6 + defensiveBonus * 0.4,
                Position.Defender => defensiveBonus,
                Position.Goalkeeper => defensiveBonus * 0.5,
                _ => 0.0
            };

            player.LiveMatchModifier = Math.Clamp(1.0 + positionBonus - intensityPenalty - fatiguePenalty, 0.82, 1.15);
        }
    }

    private void UpdateConfirmSubstitutionButton()
    {
        ConfirmSubstitutionButton.IsEnabled = _selectedStarterForSubstitution is not null && _selectedBenchForSubstitution is not null;
    }

    private void UpdatePlaybackControls()
    {
        var substitutionsLeft = GetUserSubstitutionsLeft();
        var isOverlayActive = _isPausedForSubstitution || _isPausedForTacticalAdjustment;
        var isMandatoryInjurySubPending = HasMandatoryInjurySubstitution() && !HasQueuedMandatoryInjurySubstitution();
        var isMatchActive = _state.CurrentMatch is not null &&
            _state.CurrentMatch.CurrentMinute < GetPhaseEndMinute() &&
            !_hasNavigated;

        PauseResumeButton.Content = _isPlaybackPaused ? "\u25B6" : "\u23F8";
        PauseResumeButton.IsEnabled = isMatchActive && !isOverlayActive && !isMandatoryInjurySubPending;
        DecreaseSpeedButton.IsEnabled = _speedLevel > MinSpeedLevel;
        IncreaseSpeedButton.IsEnabled = _speedLevel < MaxSpeedLevel;
        CompactPauseResumeButton.Content = PauseResumeButton.Content;
        CompactPauseResumeButton.IsEnabled = PauseResumeButton.IsEnabled;
        CompactDecreaseSpeedButton.IsEnabled = DecreaseSpeedButton.IsEnabled;
        CompactIncreaseSpeedButton.IsEnabled = IncreaseSpeedButton.IsEnabled;
        CompactContinueButton.Content = ContinueButton.Content;
        CompactContinueButton.Visibility = ContinueButton.Visibility;
        CompactContinueButton.IsEnabled = ContinueButton.IsEnabled;
        PausedActionPanel.Visibility = Visibility.Visible;
        PausedActionPanel.IsEnabled = _isPlaybackPaused;
        PausedActionPanel.Opacity = _isPlaybackPaused ? 1.0 : 0.42;
        PausedActionPanel.Effect = _isPlaybackPaused ? null : CreateDisabledPanelBlur();
        PausedSubstitutionPanelBorder.IsEnabled = _isPlaybackPaused;
        PausedSubstitutionPanelBorder.Opacity = _isPlaybackPaused ? 1.0 : 0.42;
        PausedSubstitutionPanelBorder.Effect = _isPlaybackPaused ? null : CreateDisabledPanelBlur();
        PausedBenchListBox.IsEnabled = _isPlaybackPaused && substitutionsLeft > 0;
        SubsLeftTextBlock.Text = $"Subs left: {Math.Max(0, substitutionsLeft)}";
        PausedSubstitutionPanelBorder.BorderBrush = ToBrush(isMandatoryInjurySubPending
            ? "#D92D20"
            : ThemeManager.GetBrushHex("AppBorderBrush", "#243247"));
        PausedSubstitutionPanelBorder.Background = ToBrush(isMandatoryInjurySubPending
            ? ThemeManager.GetBrushHex("FeedAttackBackground", "#3B1115")
            : GetLiveMatchCardBackground());
    }

    private static string GetLiveMatchCardBackground()
    {
        return ThemeManager.GetBrushHex("AppCardBackground", "#0F172A");
    }

    private static BlurEffect CreateDisabledPanelBlur()
    {
        return new BlurEffect
        {
            Radius = 1.35,
            KernelType = KernelType.Gaussian
        };
    }

    private void UpdateSpeedControls()
    {
        SpeedLevelTextBlock.Text = GetSpeedLevelText(_speedLevel);
        CompactSpeedLevelTextBlock.Text = SpeedLevelTextBlock.Text;
        DecreaseSpeedButton.IsEnabled = _speedLevel > MinSpeedLevel;
        IncreaseSpeedButton.IsEnabled = _speedLevel < MaxSpeedLevel;
        CompactDecreaseSpeedButton.IsEnabled = DecreaseSpeedButton.IsEnabled;
        CompactIncreaseSpeedButton.IsEnabled = IncreaseSpeedButton.IsEnabled;
    }

    private void SaveSpeedLevelToState()
    {
        _state.CurrentMatchSpeed = _speedLevel switch
        {
            0 => MatchSpeed.VerySlow,
            1 => MatchSpeed.Medium,
            2 => MatchSpeed.OnePointFive,
            3 => MatchSpeed.Fast,
            4 => MatchSpeed.VeryFast,
            _ => MatchSpeed.Medium
        };
    }

    private static int GetSpeedLevelFromState(MatchSpeed speed)
    {
        return speed switch
        {
            MatchSpeed.VerySlow or MatchSpeed.Slow => 0,
            MatchSpeed.Medium => 1,
            MatchSpeed.OnePointFive => 2,
            MatchSpeed.Fast => 3,
            MatchSpeed.VeryFast => 4,
            _ => DefaultSpeedLevel
        };
    }

    private static string GetSpeedLevelText(int speedLevel)
    {
        return speedLevel switch
        {
            0 => "0.5x",
            1 => "1x",
            2 => "1.5x",
            3 => "2x",
            4 => "4x",
            _ => "1x"
        };
    }

    private int GetUserSubstitutionsLeft()
    {
        if (_state.CurrentMatch is null)
        {
            return 0;
        }

        var userTeam = GetUserTeam();
        var usedSubstitutions = _squadSelectionService.CountTeamSubstitutions(_state.CurrentMatch, userTeam.Name);
        return MatchConstants.MaxSubstitutionsPerTeam - usedSubstitutions;
    }

    private void CancelPlayback()
    {
        if (_playbackCancellation is null)
        {
            return;
        }

        _playbackCancellation.Cancel();
        _playbackCancellation.Dispose();
        _playbackCancellation = null;
    }

    private void RefreshPlayerPanels()
    {
        RefreshPitchPlayers();
    }

    private void InitializeDisplayedPitchStats()
    {
        _displayedPitchStats.Clear();
        _livePlayerStatsById.Clear();
        if (_state.CurrentMatch is null)
        {
            return;
        }

        SyncLivePlayerRatings();
        SyncLivePlayerStaminaForActivePlayers();
        foreach (var matchEvent in _state.CurrentMatch.Events)
        {
            RecordDisplayedPitchStats(matchEvent);
        }
    }

    private void RecordDisplayedPitchStats(MatchEvent matchEvent)
    {
        switch (matchEvent.EventType)
        {
            case EventType.Goal:
                if (!matchEvent.Description.Contains("Own goal", StringComparison.OrdinalIgnoreCase))
                {
                    IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Goals++);
                    IncrementDisplayedPlayerStat(matchEvent.SecondaryPlayerName, stats => stats.Assists++);
                }
                break;

            case EventType.WonderGoal:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Goals++);
                break;

            case EventType.VarDecision:
            case EventType.Offside:
                if (IsGoalDisallowedEvent(matchEvent))
                {
                    IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Goals = Math.Max(0, stats.Goals - 1));
                    IncrementDisplayedPlayerStat(matchEvent.SecondaryPlayerName, stats => stats.Assists = Math.Max(0, stats.Assists - 1));
                }
                break;

            case EventType.Penalty:
                if (matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase))
                {
                    IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Goals++);
                }
                break;

            case EventType.Save:
                IncrementDisplayedPlayerStat(matchEvent.SecondaryPlayerName, stats => stats.Saves++);
                break;

            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
            case EventType.DefensiveStop:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.DefensiveContributions++);
                break;

            case EventType.DefensiveError:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.DefensiveErrors++);
                break;

            case EventType.YellowCard:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.YellowCards++);
                if (matchEvent.Description.Contains("Both players", StringComparison.OrdinalIgnoreCase))
                {
                    IncrementDisplayedPlayerStat(matchEvent.SecondaryPlayerName, stats => stats.YellowCards++);
                }
                break;

            case EventType.RedCard:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.RedCards++);
                break;

            case EventType.Injury:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Injuries++);
                break;
        }
    }

    private void IncrementDisplayedPlayerStat(string? playerName, Action<DisplayedPitchStats> update)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        var playerContext = ResolvePlayerContext(playerName);
        if (playerContext is null)
        {
            return;
        }

        var playerKey = CreatePlayerId(playerContext.Value.Team, playerContext.Value.Player);
        if (!_displayedPitchStats.TryGetValue(playerKey, out var stats))
        {
            stats = new DisplayedPitchStats();
            _displayedPitchStats[playerKey] = stats;
        }

        update(stats);
    }

    private void RecordDisplayedPitchRating(MatchEvent matchEvent)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        if (IsGoalDisallowedEvent(matchEvent))
        {
            SyncLivePlayerRating(matchEvent.PrimaryPlayerName);
            SyncLivePlayerRating(matchEvent.SecondaryPlayerName);
            return;
        }

        if (IsScoringEvent(matchEvent))
        {
            ApplyDisplayedScoringRating(matchEvent);
            return;
        }

        var eventTeam = ResolveTeam(FindTeamName(matchEvent, _state.CurrentMatch));
        var opposingTeam = GetOpposingTeam(eventTeam);

        switch (matchEvent.EventType)
        {
            case EventType.Save:
                SyncLivePlayerRating(matchEvent.PrimaryPlayerName, opposingTeam);
                SyncLivePlayerRating(matchEvent.SecondaryPlayerName, eventTeam);
                break;

            case EventType.Foul:
            case EventType.YellowCard:
            case EventType.RedCard:
            case EventType.DefensiveStop:
            case EventType.DefensiveError:
            case EventType.GoalkeeperHeroics:
            case EventType.GoalkeeperMistake:
            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
                SyncLivePlayerRating(matchEvent.PrimaryPlayerName, eventTeam);
                SyncLivePlayerRating(matchEvent.SecondaryPlayerName, opposingTeam);
                break;

            default:
                SyncLivePlayerRating(matchEvent.PrimaryPlayerName, eventTeam);
                SyncLivePlayerRating(matchEvent.SecondaryPlayerName, eventTeam);
                break;
        }
    }

    private void ApplyDisplayedScoringRating(MatchEvent matchEvent)
    {
        if (matchEvent.Description.Contains("Own goal", StringComparison.OrdinalIgnoreCase))
        {
            SyncLivePlayerRating(matchEvent.PrimaryPlayerName);
            return;
        }

        var scorerBoost = matchEvent.EventType switch
        {
            EventType.WonderGoal => 1.35,
            EventType.Penalty => 1.05,
            _ => 1.05
        };

        ApplyDisplayedRatingDelta(matchEvent.PrimaryPlayerName, scorerBoost);
        if (!string.IsNullOrWhiteSpace(matchEvent.SecondaryPlayerName))
        {
            ApplyDisplayedRatingDelta(matchEvent.SecondaryPlayerName, 0.70);
        }
    }

    private void ApplyDisplayedRatingDelta(string? playerName, double delta)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        var playerContext = ResolvePlayerContext(playerName);
        if (playerContext is null)
        {
            return;
        }

        var stats = GetOrCreateLivePlayerStats(playerContext.Value.Team, playerContext.Value.Player);
        stats.SetCurrentRating(stats.CurrentRating + delta);
    }

    private void SyncLivePlayerRating(string? playerName, Team? preferredTeam = null)
    {
        if (_state.CurrentMatch is null || string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        var playerContext = ResolvePlayerContext(playerName, preferredTeam);
        if (playerContext is null)
        {
            return;
        }

        var performance = _state.CurrentMatch.PlayerPerformances.FirstOrDefault(existing =>
            string.Equals(existing.TeamName, playerContext.Value.Team.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.PlayerName, playerName, StringComparison.OrdinalIgnoreCase));
        if (performance is null)
        {
            return;
        }

        GetOrCreateLivePlayerStats(playerContext.Value.Team, playerContext.Value.Player).SetCurrentRating(performance.Rating);
    }

    private void SyncLivePlayerRatings()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        foreach (var performance in _state.CurrentMatch.PlayerPerformances)
        {
            var team = ResolveTeam(performance.TeamName);
            var player = team?.Players.Concat(team.Substitutes)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, performance.PlayerName, StringComparison.OrdinalIgnoreCase));
            if (team is null || player is null)
            {
                continue;
            }

            GetOrCreateLivePlayerStats(team, player).SetCurrentRating(performance.Rating);
        }
    }

    private void SyncLivePlayerStaminaForActivePlayers()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        SyncTeamLivePlayerStamina(_state.CurrentMatch.HomeTeam);
        SyncTeamLivePlayerStamina(_state.CurrentMatch.AwayTeam);
        LogMinute24OpponentStaminaDebug();
    }

    private void SyncTeamLivePlayerStamina(Team team)
    {
        foreach (var player in team.Players.Where(player => player.IsOnPitch && !player.IsSentOff))
        {
            GetOrCreateLivePlayerStats(team, player, fallbackStamina: GetStaminaPercentage(player))
                .SetStaminaPercent(GetStaminaPercentage(player));
        }
    }

    private LivePlayerStats GetOrCreateLivePlayerStats(Team team, Player player, double fallbackRating = 6.0, int? fallbackStamina = null)
    {
        var playerId = CreatePlayerId(team, player);
        if (_livePlayerStatsById.TryGetValue(playerId, out var stats))
        {
            return stats;
        }

        stats = new LivePlayerStats
        {
            PlayerId = playerId,
            TeamName = team.Name,
            PlayerName = player.Name
        };
        stats.SetCurrentRating(fallbackRating);
        stats.SetStaminaPercent(fallbackStamina ?? GetStaminaPercentage(player));
        _livePlayerStatsById[playerId] = stats;
        return stats;
    }

    private void LogMinute24OpponentStaminaDebug()
    {
        if (_hasLoggedMinute24OpponentStamina ||
            _state.CurrentMatch is null ||
            _state.SelectedTeam is null ||
            _state.CurrentMatch.CurrentMinute != 24)
        {
            return;
        }

        var opponent = string.Equals(_state.CurrentMatch.HomeTeam.Name, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase)
            ? _state.CurrentMatch.AwayTeam
            : _state.CurrentMatch.HomeTeam;

        foreach (var player in opponent.Players.Where(player => player.IsOnPitch && !player.IsSentOff))
        {
            var stats = GetOrCreateLivePlayerStats(opponent, player, fallbackStamina: GetStaminaPercentage(player));
            System.Diagnostics.Debug.WriteLine(
                $"[StaminaDebug] Minute 24 | Opponent={opponent.Name} | Player={player.Name} | StaminaPercent={stats.StaminaPercent:0} | BarWidth={stats.StaminaBarWidth:0.0}");
        }

        _hasLoggedMinute24OpponentStamina = true;
    }

    private DisplayedPitchStats GetDisplayedPitchStats(string playerKey)
    {
        return _displayedPitchStats.TryGetValue(playerKey, out var stats)
            ? stats
            : DisplayedPitchStats.Empty;
    }

    private void RefreshPitchPlayers()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        _playerFormStatusService.UpdateLiveMatchPlayerFormStatuses(_state.CurrentMatch);

        var pitchWidth = _pitchWidth > 0 ? _pitchWidth : 860;
        var pitchHeight = _pitchHeight > 0 ? _pitchHeight : 560;
        var items = new List<LivePlayerIconViewModel>();

        items.AddRange(CreatePitchIcons(_state.CurrentMatch.HomeTeam, isHomeTeam: true, pitchWidth, pitchHeight));
        items.AddRange(CreatePitchIcons(_state.CurrentMatch.AwayTeam, isHomeTeam: false, pitchWidth, pitchHeight));

        _pitchPlayers.Clear();
        foreach (var item in items)
        {
            _pitchPlayers.Add(item);
        }

        UpdateSelectedPlayerPanel();
    }

    private IEnumerable<LivePlayerIconViewModel> CreatePitchIcons(Team team, bool isHomeTeam, double pitchWidth, double pitchHeight)
    {
        var formationPositions = _formationLayoutService.GetPositions(team.Formation);
        var players = team.Players
            .Where(player => player.IsOnPitch && !player.IsSentOff)
            .ToList();

        foreach (var assignment in CreatePitchSlotAssignments(players, formationPositions))
        {
            yield return CreatePitchIcon(team, assignment.Player, assignment.Position, isHomeTeam, pitchWidth, pitchHeight);
        }
    }

    private static List<PitchSlotAssignment> CreatePitchSlotAssignments(
        IReadOnlyList<Player> players,
        IReadOnlyList<PitchPosition> formationPositions)
    {
        var remainingPlayers = players.ToList();
        var assignments = new List<PitchSlotAssignment>();
        foreach (var position in formationPositions)
        {
            var selectedPlayer = SelectPlayerForSlot(remainingPlayers, position.ExactPosition);
            if (selectedPlayer is null)
            {
                continue;
            }

            assignments.Add(new PitchSlotAssignment(selectedPlayer, position));
            remainingPlayers.Remove(selectedPlayer);
        }

        return assignments;
    }

    private static Player? SelectPlayerForSlot(List<Player> remainingPlayers, string exactPosition)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        if (normalizedSlot == "GK")
        {
            return remainingPlayers.FirstOrDefault(PositionSuitabilityService.IsGoalkeeperCapable);
        }

        var selectedPlayer = remainingPlayers
            .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
            .Select(player => new
            {
                Player = player,
                Compatibility = PositionCompatibilityService.GetCompatibilityScore(player, normalizedSlot)
            })
            .Where(candidate => candidate.Compatibility > PositionCompatibilityService.Impossible)
            .OrderByDescending(candidate => candidate.Compatibility)
            .ThenByDescending(candidate => candidate.Player.OverallRating)
            .ThenBy(candidate => candidate.Player.SquadNumber <= 0 ? int.MaxValue : candidate.Player.SquadNumber)
            .Select(candidate => candidate.Player)
            .FirstOrDefault();

        if (selectedPlayer is null)
        {
            System.Diagnostics.Debug.WriteLine($"[LineupWarning] No compatible player available for {normalizedSlot} pitch slot.");
            selectedPlayer = remainingPlayers
                .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
                .OrderByDescending(player => player.OverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .FirstOrDefault();
        }

        return selectedPlayer;
    }

    private LivePlayerIconViewModel CreatePitchIcon(
        Team team,
        Player player,
        PitchPosition formationPosition,
        bool isHomeTeam,
        double pitchWidth,
        double pitchHeight)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player, formationPosition.ExactPosition);
        var performance = _state.CurrentMatch?.PlayerPerformances
            .FirstOrDefault(existing => existing.TeamName == team.Name && existing.PlayerName == player.Name);
        var currentStamina = GetStaminaPercentage(player);
        var (xRatio, yRatio) = GetLivePitchPosition(formationPosition, isHomeTeam);
        var x = Math.Clamp((pitchWidth * xRatio) - (PlayerIconSlotWidth / 2), 4, Math.Max(4, pitchWidth - PlayerIconSlotWidth - 4));
        var y = Math.Clamp((pitchHeight * yRatio) - (PlayerIconSlotHeight / 2), 4, Math.Max(4, pitchHeight - PlayerIconSlotHeight - 4));
        var playerId = CreatePlayerId(team, player);
        var playerKey = playerId;
        var liveStats = GetOrCreateLivePlayerStats(team, player, performance?.Rating ?? 6.0, currentStamina);
        var displayedStats = GetDisplayedPitchStats(playerKey);
        var stamina = liveStats.StaminaPercent;
        var status = GetPlayerStatus(stamina, displayedStats);
        var yellowCards = displayedStats.YellowCards;
        var redCards = displayedStats.RedCards;
        var isSelected = string.Equals(_selectedPitchPlayerKey, playerKey, StringComparison.OrdinalIgnoreCase);
        var teamColors = GetMatchPalette(team);
        var formBadge = PlayerFormBadgeHelper.Create(GetDisplayedFormStatus(liveStats.CurrentRating));
        var ratingBadgeColors = GetRatingBadgeColors(liveStats.CurrentRating, formBadge);
        var nationality = PlayerNationalityDisplayService.Resolve(player);

        return new LivePlayerIconViewModel
        {
            PlayerId = playerId,
            Name = player.Name,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            TeamName = team.Name,
            PlayerKey = playerKey,
            LiveStats = liveStats,
            ShirtNumberText = player.SquadNumber > 0 ? player.SquadNumber.ToString() : string.Empty,
            Initials = GetInitials(player.Name),
            PositionText = formationPosition.ExactPosition,
            ExactPosition = formationPosition.ExactPosition,
            TeamSide = isHomeTeam ? "Home" : "Away",
            X = x,
            Y = y,
            IconBrush = teamColors.PrimaryColor,
            IconForeground = teamColors.TextColor,
            BorderBrush = teamColors.BorderColor,
            SelectionBrush = isSelected ? teamColors.SelectedGlowColor : "Transparent",
            SelectionThickness = isSelected ? 4 : 0,
            RatingFormBrush = formBadge.Background,
            RatingBadgeBackground = ratingBadgeColors.Background,
            RatingBadgeForeground = ratingBadgeColors.Foreground,
            RatingBadgeBorderBrush = ratingBadgeColors.Border,
            Goals = displayedStats.Goals,
            Assists = displayedStats.Assists,
            DefensiveContributions = displayedStats.DefensiveContributions,
            Saves = displayedStats.Saves,
            YellowCards = yellowCards,
            RedCards = redCards,
            IsInjured = displayedStats.Injuries > 0,
            ContributionBadgesText = BuildContributionBadgesText(displayedStats),
            GoalBadgeText = displayedStats.Goals > 0 ? $"{SoccerBallIcon()}{displayedStats.Goals}" : string.Empty,
            AssistBadgeText = displayedStats.Assists > 0 ? $"{AssistIcon()}{displayedStats.Assists}" : string.Empty,
            DefensiveBadgeText = displayedStats.DefensiveContributions > 0 ? $"{ShieldIcon()}{displayedStats.DefensiveContributions}" : string.Empty,
            ErrorBadgeText = displayedStats.DefensiveErrors > 0 ? WarningIcon() : string.Empty,
            SaveBadgeText = displayedStats.Saves > 0 ? $"{GloveIcon()}{displayedStats.Saves}" : string.Empty,
            YellowBadgeText = yellowCards > 0 ? YellowCardIcon() : string.Empty,
            RedBadgeText = redCards > 0 ? RedCardIcon() : string.Empty,
            InjuryBadgeText = displayedStats.Injuries > 0 ? InjuryIcon() : string.Empty,
            PendingSubOutBadgeText = IsPendingSubOut(team, player) ? "↓" : string.Empty,
            DetailText = BuildPlayerDetailText(player, performance, liveStats.CurrentRating, stamina, displayedStats.DefensiveContributions),
            CardsText = yellowCards == 0 && redCards == 0 ? "None" : $"Y{yellowCards} R{redCards}",
            InjuryStatusText = displayedStats.Injuries > 0 ? "Injured" : "Fit",
            FormText = PlayerFormStatusService.ToDisplayText(player.FormStatus),
            StaminaText = $"{stamina:0}%",
            MatchStatusText = status.Text,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
            CardStatusBadges = PlayerCardStatusBadgeHelper.Create(yellowCards, redCards)
        };
    }

    private static (double X, double Y) GetLivePitchPosition(PitchPosition formationPosition, bool isHomeTeam)
    {
        var depthFromOwnGoal = Math.Clamp(1.0 - formationPosition.Y, 0.0, 1.0);
        var x = isHomeTeam
            ? 0.07 + depthFromOwnGoal * 0.40
            : 0.93 - depthFromOwnGoal * 0.40;
        var y = Math.Clamp(formationPosition.X, 0.08, 0.92);

        return (Math.Clamp(x, 0.06, 0.94), y);
    }

    private static (string Background, string Foreground, string Border) GetRatingBadgeColors(double rating, PlayerFormBadge formBadge)
    {
        if (rating >= 9.0)
        {
            return ("#10B981", "#FFFFFF", "#D1FAE5");
        }

        return ("#102033", formBadge.Background, formBadge.Background);
    }

    private static PlayerFormStatus GetDisplayedFormStatus(double rating)
    {
        return rating switch
        {
            >= 9.0 => PlayerFormStatus.Excellent,
            >= 7.5 => PlayerFormStatus.Good,
            >= 6.0 => PlayerFormStatus.Average,
            >= 5.0 => PlayerFormStatus.Poor,
            _ => PlayerFormStatus.VeryPoor
        };
    }

    private bool IsPendingSubOut(Team team, Player player)
    {
        return _state.SelectedTeam is not null &&
            string.Equals(team.Name, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase) &&
            _pendingSubstitutions.Any(pending =>
                string.Equals(pending.PlayerOut.Name, player.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void PitchPlayerIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LivePlayerIconViewModel selectedPlayer })
        {
            return;
        }

        _selectedPitchPlayerKey = selectedPlayer.PlayerKey;
        RefreshPitchPlayers();
        if (_isPlaybackPaused)
        {
            PausedActionStatusTextBlock.Text = $"Selected {selectedPlayer.Name}. Choose a substitute or drag him onto a valid position swap.";
            RefreshPausedSubstitutionViews();
        }

        e.Handled = true;
    }

    private void UpdateSelectedPlayerPanel()
    {
        var selectedPlayer = _pitchPlayers.FirstOrDefault(player =>
            string.Equals(player.PlayerKey, _selectedPitchPlayerKey, StringComparison.OrdinalIgnoreCase));

        if (selectedPlayer is null)
        {
            SelectedPlayerPlaceholderTextBlock.Visibility = Visibility.Visible;
            SelectedPlayerStatsPanel.Visibility = Visibility.Collapsed;
            SelectedPlayerStatsPanel.DataContext = null;
            return;
        }

        SelectedPlayerPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        SelectedPlayerStatsPanel.Visibility = Visibility.Visible;
        SelectedPlayerStatsPanel.DataContext = selectedPlayer;

        SelectedPlayerNameTextBlock.Text = selectedPlayer.Name;
        SelectedPlayerMetaTextBlock.Text = $"{selectedPlayer.TeamName} | {selectedPlayer.PositionText}";
        SelectedPlayerTraitItemsControl.ItemsSource = selectedPlayer.TraitBadges;
        SelectedPlayerTraitItemsControl.Visibility = selectedPlayer.TraitBadges.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SelectedPlayerCardStatusItemsControl.ItemsSource = selectedPlayer.CardStatusBadges;
        SelectedPlayerCardStatusItemsControl.Visibility = selectedPlayer.CardStatusBadges.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        var playerContext = FindSelectedPlayerContext(selectedPlayer);
        var player = playerContext?.Player;
        var performance = playerContext?.Performance;
        var team = playerContext?.Team;
        var rating = selectedPlayer.LiveStats.CurrentRating;
        var passAccuracy = GetEstimatedPassAccuracy(team, rating, selectedPlayer.Stamina);

        if (player?.Position == Position.Goalkeeper)
        {
            SetSelectedPlayerStatRows(
                [
                    new("Rating", selectedPlayer.LiveStats.RatingDisplay),
                    new("Saves", selectedPlayer.Saves.ToString()),
                    new("Clean Sheet", IsCleanSheet(team) ? "Yes" : "No"),
                    new("Punches", GetEstimatedPunches(performance).ToString()),
                    new("Claims", GetEstimatedClaims(performance).ToString()),
                    new("Pass Accuracy", $"{passAccuracy:0}%")
                ],
                [
                    new("Stamina", $"{selectedPlayer.Stamina:0}%"),
                    new("Goals Conceded", GetGoalsConceded(team).ToString()),
                    new("Fouls", (performance?.Fouls ?? 0).ToString()),
                    new("Cards", selectedPlayer.CardsText),
                    new("Status", selectedPlayer.MatchStatusText),
                    new("Injury", selectedPlayer.InjuryStatusText)
                ]);
        }
        else
        {
            SetSelectedPlayerStatRows(
                [
                    new("Rating", selectedPlayer.LiveStats.RatingDisplay),
                    new("Goals", selectedPlayer.Goals.ToString()),
                    new("Assists", selectedPlayer.Assists.ToString()),
                    new("Successful Tackles", (performance?.Tackles ?? 0).ToString()),
                    new("Key Passes", (performance?.KeyPasses ?? 0).ToString()),
                    new("Interceptions", (performance?.Interceptions ?? 0).ToString())
                ],
                [
                    new("Stamina", $"{selectedPlayer.Stamina:0}%"),
                    new("Shots", (performance?.Shots ?? 0).ToString()),
                    new("Pass Accuracy", $"{passAccuracy:0}%"),
                    new("Duels Won", GetDuelsWon(performance).ToString()),
                    new("Fouls", (performance?.Fouls ?? 0).ToString()),
                    new("Cards", selectedPlayer.CardsText)
                ]);
        }

        var formBadge = PlayerFormBadgeHelper.Create(player?.FormStatus ?? PlayerFormStatus.Average);
        SelectedPlayerFormBadgeBorder.Background = ToBrush(formBadge.Background);
        SelectedPlayerFormBadgeTextBlock.Foreground = ToBrush(formBadge.Foreground);
        SelectedPlayerFormBadgeTextBlock.Text = formBadge.Text;
    }

    private SelectedPlayerContext? FindSelectedPlayerContext(LivePlayerIconViewModel selectedPlayer)
    {
        if (_state.CurrentMatch is null)
        {
            return null;
        }

        var team = _state.CurrentMatch.HomeTeam.Name == selectedPlayer.TeamName
            ? _state.CurrentMatch.HomeTeam
            : _state.CurrentMatch.AwayTeam.Name == selectedPlayer.TeamName
                ? _state.CurrentMatch.AwayTeam
                : null;

        if (team is null)
        {
            return null;
        }

        var player = team.Players.Concat(team.Substitutes)
            .FirstOrDefault(candidate => string.Equals(CreatePlayerId(team, candidate), selectedPlayer.PlayerId, StringComparison.OrdinalIgnoreCase));
        var performance = _state.CurrentMatch.PlayerPerformances
            .FirstOrDefault(existing => existing.TeamName == selectedPlayer.TeamName &&
                player is not null &&
                existing.PlayerName == player.Name);

        return player is null ? null : new SelectedPlayerContext(team, player, performance);
    }

    private void SetSelectedPlayerStatRows(IReadOnlyList<PlayerStatRow> leftRows, IReadOnlyList<PlayerStatRow> rightRows)
    {
        SetPlayerStatRow(SelectedPlayerLeftStat1Label, SelectedPlayerLeftStat1Value, leftRows[0]);
        SetPlayerStatRow(SelectedPlayerLeftStat2Label, SelectedPlayerLeftStat2Value, leftRows[1]);
        SetPlayerStatRow(SelectedPlayerLeftStat3Label, SelectedPlayerLeftStat3Value, leftRows[2]);
        SetPlayerStatRow(SelectedPlayerLeftStat4Label, SelectedPlayerLeftStat4Value, leftRows[3]);
        SetPlayerStatRow(SelectedPlayerLeftStat5Label, SelectedPlayerLeftStat5Value, leftRows[4]);
        SetPlayerStatRow(SelectedPlayerLeftStat6Label, SelectedPlayerLeftStat6Value, leftRows[5]);

        SetPlayerStatRow(SelectedPlayerRightStat1Label, SelectedPlayerRightStat1Value, rightRows[0]);
        SetPlayerStatRow(SelectedPlayerRightStat2Label, SelectedPlayerRightStat2Value, rightRows[1]);
        SetPlayerStatRow(SelectedPlayerRightStat3Label, SelectedPlayerRightStat3Value, rightRows[2]);
        SetPlayerStatRow(SelectedPlayerRightStat4Label, SelectedPlayerRightStat4Value, rightRows[3]);
        SetPlayerStatRow(SelectedPlayerRightStat5Label, SelectedPlayerRightStat5Value, rightRows[4]);
        SetPlayerStatRow(SelectedPlayerRightStat6Label, SelectedPlayerRightStat6Value, rightRows[5]);
    }

    private static void SetPlayerStatRow(TextBlock label, TextBlock value, PlayerStatRow row)
    {
        label.Text = row.Label;
        value.Text = row.Value;
    }

    private double GetEstimatedPassAccuracy(Team? team, double rating, double stamina)
    {
        if (_state.CurrentMatch is null || team is null)
        {
            return Math.Clamp(68 + rating * 2.0 + stamina / 12.0, 55, 96);
        }

        var teamStats = team == _state.CurrentMatch.HomeTeam
            ? _state.CurrentMatch.HomeStats
            : _state.CurrentMatch.AwayStats;
        var baseline = teamStats.PassAccuracyPercentage > 0 ? teamStats.PassAccuracyPercentage : 78.0;

        return Math.Round(Math.Clamp(baseline + (rating - 6.5) * 1.8 + (stamina - 70) * 0.04, 55, 96), 1);
    }

    private bool IsCleanSheet(Team? team)
    {
        return GetGoalsConceded(team) == 0;
    }

    private int GetGoalsConceded(Team? team)
    {
        if (_state.CurrentMatch is null || team is null)
        {
            return 0;
        }

        return team == _state.CurrentMatch.HomeTeam
            ? _state.CurrentMatch.AwayScore
            : _state.CurrentMatch.HomeScore;
    }

    private static int GetEstimatedPunches(PlayerMatchPerformance? performance)
    {
        return performance is null ? 0 : performance.Saves / 3;
    }

    private static int GetEstimatedClaims(PlayerMatchPerformance? performance)
    {
        return performance is null ? 0 : Math.Max(0, performance.Saves / 2 + performance.Clearances);
    }

    private static int GetDuelsWon(PlayerMatchPerformance? performance)
    {
        return performance is null
            ? 0
            : performance.Tackles + performance.Blocks + performance.Clearances;
    }

    private static Brush ToBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private static string BuildEventIconsText(Player player, PlayerMatchPerformance? performance)
    {
        var icons = new List<string>();

        if (performance is not null)
        {
            if (performance.Goals > 0)
            {
                icons.Add($"{SoccerBallIcon()}{performance.Goals}");
            }

            if (performance.Assists > 0)
            {
                icons.Add($"{AssistIcon()}{performance.Assists}");
            }

            if (performance.Saves > 0)
            {
                icons.Add($"{GloveIcon()}{performance.Saves}");
            }

            var defensiveActions = performance.Tackles +
                performance.Interceptions +
                performance.Blocks +
                performance.Clearances;

            if (defensiveActions > 0)
            {
                icons.Add($"{ShieldIcon()}{defensiveActions}");
            }
        }

        if (player.YellowCards > 0 || performance?.YellowCards > 0)
        {
            icons.Add("🟨");
        }

        if (player.IsSentOff || performance?.RedCards > 0)
        {
            icons.Add("🟥");
        }

        if (player.IsInjured || performance?.Injuries > 0)
        {
            icons.Add("🩹");
        }

        return string.Join(" ", icons);
    }

    private static string BuildContributionBadgesText(Player player, PlayerMatchPerformance? performance)
    {
        var icons = new List<string>();

        if (performance is not null)
        {
            if (performance.Goals > 0)
            {
                icons.Add($"{SoccerBallIcon()}{performance.Goals}");
            }

            if (performance.Assists > 0)
            {
                icons.Add($"{AssistIcon()}{performance.Assists}");
            }

            if (performance.Saves > 0)
            {
                icons.Add($"{GloveIcon()}{performance.Saves}");
            }

            var defensiveActions = GetDefensiveContributions(performance);
            if (defensiveActions > 0)
            {
                icons.Add($"{ShieldIcon()}{defensiveActions}");
            }
        }

        if (player.YellowCards > 0 || performance?.YellowCards > 0)
        {
            icons.Add(YellowCardIcon());
        }

        if (player.IsSentOff || performance?.RedCards > 0)
        {
            icons.Add(RedCardIcon());
        }

        if (player.IsInjured || performance?.Injuries > 0)
        {
            icons.Add(InjuryIcon());
        }

        return string.Join(" ", icons);
    }

    private static string BuildContributionBadgesText(DisplayedPitchStats stats)
    {
        var icons = new List<string>();

        if (stats.Goals > 0)
        {
            icons.Add($"{SoccerBallIcon()}{stats.Goals}");
        }

        if (stats.Assists > 0)
        {
            icons.Add($"{AssistIcon()}{stats.Assists}");
        }

        if (stats.Saves > 0)
        {
            icons.Add($"{GloveIcon()}{stats.Saves}");
        }

        if (stats.DefensiveContributions > 0)
        {
            icons.Add($"{ShieldIcon()}{stats.DefensiveContributions}");
        }

        if (stats.DefensiveErrors > 0)
        {
            icons.Add(WarningIcon());
        }

        if (stats.YellowCards > 0)
        {
            icons.Add(YellowCardIcon());
        }

        if (stats.RedCards > 0)
        {
            icons.Add(RedCardIcon());
        }

        if (stats.Injuries > 0)
        {
            icons.Add(InjuryIcon());
        }

        return string.Join(" ", icons);
    }

    private static int GetPositionGroupOrder(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => 0,
            Position.Defender => 1,
            Position.Midfielder => 2,
            Position.Forward => 3,
            _ => 4
        };
    }

    private static string GetPositionGroupHeader(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "DEF",
            Position.Midfielder => "MID",
            Position.Forward => "FWD",
            _ => position.ToString()
        };
    }

    private static string SoccerBallIcon() => char.ConvertFromUtf32(0x26BD);

    private static string HatTrickIcon() => char.ConvertFromUtf32(0x1F3A9);

    private static string AssistIcon() => char.ConvertFromUtf32(0x1F3AF);

    private static string ShieldIcon() => char.ConvertFromUtf32(0x1F6E1);

    private static string GloveIcon() => char.ConvertFromUtf32(0x1F9E4);

    private static string YellowCardIcon() => char.ConvertFromUtf32(0x1F7E8);

    private static string RedCardIcon() => char.ConvertFromUtf32(0x1F7E5);

    private static string InjuryIcon()
    {
        return char.ConvertFromUtf32(0x1FA79);
    }

    private static string FlagIcon() => char.ConvertFromUtf32(0x1F6A9);

    private static string SwordsIcon() => char.ConvertFromUtf32(0x2694);

    private static string TargetIcon() => char.ConvertFromUtf32(0x1F3AF);

    private static string HeaderIcon() => $"{SoccerBallIcon()}↕";

    private static string StopIcon() => char.ConvertFromUtf32(0x1F6D1);

    private static string StarIcon() => char.ConvertFromUtf32(0x2B50);

    private static string GoalNetIcon() => char.ConvertFromUtf32(0x1F945);

    private static string WarningIcon() => char.ConvertFromUtf32(0x26A0);

    private static string FireIcon() => char.ConvertFromUtf32(0x1F525);

    private static string ThumbsUpIcon() => char.ConvertFromUtf32(0x1F44D);

    private static string NeutralFaceIcon() => char.ConvertFromUtf32(0x1F610);

    private static string SunIcon() => char.ConvertFromUtf32(0x2600);

    private static string RainCloudIcon() => char.ConvertFromUtf32(0x1F327);

    private static string StormIcon() => char.ConvertFromUtf32(0x26C8);

    private static string WindIcon() => char.ConvertFromUtf32(0x1F32C);

    private static string FogIcon() => char.ConvertFromUtf32(0x1F32B);

    private static string HeatIcon() => char.ConvertFromUtf32(0x1F305);

    private static string ColdIcon() => char.ConvertFromUtf32(0x1F321);

    private static string SnowflakeIcon() => char.ConvertFromUtf32(0x2744);

    private static string MegaphoneIcon() => char.ConvertFromUtf32(0x1F4E3);

    private static string BatteryIcon() => char.ConvertFromUtf32(0x1F50B);

    private static string RotateIcon() => char.ConvertFromUtf32(0x1F504);

    private static string ClockIcon() => char.ConvertFromUtf32(0x23F1);

    private static string PauseIcon() => char.ConvertFromUtf32(0x23F8);

    private static string CheckeredFlagIcon() => char.ConvertFromUtf32(0x1F3C1);

    private static string BulletIcon() => char.ConvertFromUtf32(0x2022);

    private static string BuildPlayerDetailText(
        Player player,
        PlayerMatchPerformance? performance,
        double currentRating,
        int stamina,
        int defensiveContributions)
    {
        var yellowCards = Math.Max(player.YellowCards, performance?.YellowCards ?? 0);
        var redCards = Math.Max(player.IsSentOff ? 1 : 0, performance?.RedCards ?? 0);
        var injuryText = player.IsInjured || performance?.Injuries > 0 ? "Injured" : "Fit";

        return $"{player.Name}\n" +
            $"Position: {GetPositionText(player.Position)}\n" +
            $"Rating: {RatingDisplayHelper.CreateRatingText(currentRating)}\n" +
            $"Stamina: {stamina}%\n" +
            $"Goals: {performance?.Goals ?? 0} | Assists: {performance?.Assists ?? 0}\n" +
            $"Defensive: {defensiveContributions} | Saves: {performance?.Saves ?? 0}\n" +
            $"Cards: Y{yellowCards} R{redCards} | {injuryText}";
    }

    private static int GetDefensiveContributions(PlayerMatchPerformance? performance)
    {
        return performance is null
            ? 0
            : performance.Tackles + performance.Interceptions + performance.Blocks + performance.Clearances;
    }

    private static IEnumerable<Player> OrderPlayersForPitch(IEnumerable<Player> players)
    {
        return players
            .OrderBy(player => player.Position switch
            {
                Position.Goalkeeper => 0,
                Position.Defender => 1,
                Position.Midfielder => 2,
                Position.Forward => 3,
                _ => 4
            })
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ThenBy(player => player.Name);
    }

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    private static string CreatePlayerId(Team team, Player player)
    {
        var squadNumber = player.SquadNumber > 0 ? player.SquadNumber.ToString() : "no-number";
        var position = string.IsNullOrWhiteSpace(player.PreferredPosition)
            ? player.Position.ToString()
            : player.PreferredPosition;

        return $"{team.Name}|{player.Name}|{squadNumber}|{position}";
    }

    private void LivePitchCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _pitchWidth = e.NewSize.Width;
        _pitchHeight = e.NewSize.Height;
        RefreshPitchPlayers();
    }

    private Team GetUserTeam()
    {
        if (_state.CurrentMatch is null || _state.SelectedTeam is null)
        {
            return _state.SelectedTeam ?? new Team();
        }

        return _state.CurrentMatch.HomeTeam.Name == _state.SelectedTeam.Name
            ? _state.CurrentMatch.HomeTeam
            : _state.CurrentMatch.AwayTeam;
    }

    private static IEnumerable<Player> GetActivePitchPlayers(Team team)
    {
        return team.Players.Where(player => player.IsOnPitch && !player.IsSentOff);
    }

    private Team GetOpponentTeam()
    {
        if (_state.CurrentMatch is null || _state.SelectedTeam is null)
        {
            return new Team();
        }

        return _state.CurrentMatch.HomeTeam.Name == _state.SelectedTeam.Name
            ? _state.CurrentMatch.AwayTeam
            : _state.CurrentMatch.HomeTeam;
    }

    private int GetPhaseEndMinute()
    {
        if (_state.CurrentMatch is null)
        {
            return _isSecondHalf ? FullTimeMinute : FirstHalfEndMinute;
        }

        return _isSecondHalf
            ? FullTimeMinute + _state.CurrentMatch.FirstHalfAddedMinutes + _state.CurrentMatch.SecondHalfAddedMinutes
            : FirstHalfEndMinute + _state.CurrentMatch.FirstHalfAddedMinutes;
    }

    private string CreatePhaseLabel(int currentMinute)
    {
        var phaseText = _isSecondHalf ? "Second Half" : "First Half";
        if (_state.CurrentMatch is null)
        {
            return $"{phaseText} - {currentMinute}'";
        }

        return $"{phaseText} - {MatchEngine.FormatDisplayMinute(_state.CurrentMatch, currentMinute)}";
    }

    private void UpdateVenueLabel(Match match)
    {
        var selectedTeam = _state.SelectedTeam;
        var isSelectedTeamHome = selectedTeam is not null &&
            string.Equals(match.HomeTeam.Name, selectedTeam.Name, StringComparison.OrdinalIgnoreCase);
        var homeAwayText = isSelectedTeamHome ? "Home" : "Away";
        var venue = TeamVenueService.GetDisplayVenue(match.HomeTeam);

        VenueTextBlock.Text = $"Venue: {venue} ({homeAwayText})";
        VenueTextBlock.ToolTip = isSelectedTeamHome
            ? $"{selectedTeam?.Name ?? match.HomeTeam.Name} are playing at home."
            : $"{selectedTeam?.Name ?? match.AwayTeam.Name} are away at {venue}.";
    }

    private void InsertFeedItemAtTop(MatchFeedItem feedItem)
    {
        var shouldKeepLatestEventVisible = IsViewingLatestEvents();

        _visibleEvents.Insert(0, feedItem);

        if (shouldKeepLatestEventVisible)
        {
            MatchEventsListBox.ScrollIntoView(feedItem);
        }
    }

    private void RefreshVisibleFeedTheme()
    {
        if (_state.CurrentMatch is null || _visibleEvents.Count == 0)
        {
            return;
        }

        for (var index = 0; index < _visibleEvents.Count; index++)
        {
            var sourceEvent = _visibleEvents[index].SourceEvent;
            if (sourceEvent is null)
            {
                continue;
            }

            _visibleEvents[index] = CreateFeedItem(sourceEvent, _state.CurrentMatch);
        }
    }

    private bool IsViewingLatestEvents()
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(MatchEventsListBox);
        return scrollViewer is null || scrollViewer.VerticalOffset <= 1;
    }

    private void SetScoreboardTeams(Team homeTeam, Team awayTeam)
    {
        HomeTeamNameTextBlock.Text = homeTeam.Name;
        AwayTeamNameTextBlock.Text = awayTeam.Name;
        ApplyScorePanelColors(HomeScorePanel, HomeTeamNameTextBlock, HomeScoreTextBlock, homeTeam, isPossessionTeam: false);
        ApplyScorePanelColors(AwayScorePanel, AwayTeamNameTextBlock, AwayScoreTextBlock, awayTeam, isPossessionTeam: false);
    }

    private void SetScore(int homeScore, int awayScore)
    {
        HomeScoreTextBlock.Text = homeScore.ToString();
        AwayScoreTextBlock.Text = awayScore.ToString();
    }

    private void ApplyScorePanelColors(Border panel, TextBlock teamNameTextBlock, TextBlock scoreTextBlock, Team team, bool isPossessionTeam)
    {
        var colors = GetMatchPalette(team);
        panel.Background = CreateBrush(colors.PrimaryColor);
        panel.BorderBrush = CreateBrush(isPossessionTeam ? colors.SelectedGlowColor : colors.BorderColor);
        panel.BorderThickness = isPossessionTeam ? new Thickness(3) : new Thickness(1);
        teamNameTextBlock.Foreground = CreateBrush(colors.TextColor);
        scoreTextBlock.Foreground = CreateBrush(colors.TextColor);
    }

    private TeamColorPalette GetMatchPalette(Team? team)
    {
        if (_state.CurrentMatch is null || team is null)
        {
            return TeamColorService.GetPalette(team);
        }

        _matchTeamColors ??= TeamColorService.GetMatchPalettes(_state.CurrentMatch.HomeTeam, _state.CurrentMatch.AwayTeam);
        return IsSameTeam(team, _state.CurrentMatch.HomeTeam)
            ? _matchTeamColors.Home
            : _matchTeamColors.Away;
    }

    private void UpdateLiveStatusFromEvent(MatchEvent matchEvent)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        var eventTeam = ResolveEventTeam(matchEvent);
        Team? attackingTeam = null;
        Team? defendingTeam = null;
        var status = LiveMatchStatus.Neutral;

        switch (matchEvent.EventType)
        {
            case EventType.Kickoff:
                _currentPossessionTeam = eventTeam ?? _state.CurrentMatch.HomeTeam;
                break;

            case EventType.Attack:
            case EventType.ChanceCreated:
            case EventType.Shot:
            case EventType.Woodwork:
            case EventType.Miss:
            case EventType.Goal:
            case EventType.WonderGoal:
            case EventType.CrowdMomentum:
            case EventType.LateDrama:
                attackingTeam = eventTeam ?? _currentPossessionTeam;
                defendingTeam = GetOpposingTeam(attackingTeam);
                _currentPossessionTeam = attackingTeam;
                status = LiveMatchStatus.Attacking;
                break;

            case EventType.Turnover:
                attackingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
                defendingTeam = GetOpposingTeam(attackingTeam);
                _currentPossessionTeam = attackingTeam;
                status = LiveMatchStatus.Turnover;
                break;

            case EventType.BadPass:
            case EventType.Miscontrol:
                attackingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName) ?? _currentPossessionTeam;
                defendingTeam = GetOpposingTeam(attackingTeam);
                status = LiveMatchStatus.Turnover;
                break;

            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
                defendingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
                attackingTeam = GetOpposingTeam(defendingTeam);
                status = LiveMatchStatus.Defending;
                break;

            case EventType.DefensiveStop:
            case EventType.Save:
            case EventType.GoalkeeperHeroics:
            case EventType.GoalkeeperMistake:
                defendingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
                attackingTeam = GetOpposingTeam(defendingTeam);
                _currentPossessionTeam = defendingTeam;
                status = LiveMatchStatus.Defending;
                break;

            case EventType.Foul:
                defendingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
                attackingTeam = GetOpposingTeam(defendingTeam);
                _currentPossessionTeam = attackingTeam;
                status = LiveMatchStatus.SetPiece;
                break;

            case EventType.Offside:
                defendingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
                attackingTeam = GetOpposingTeam(defendingTeam);
                _currentPossessionTeam = attackingTeam;
                status = LiveMatchStatus.SetPiece;
                break;

            case EventType.PenaltyDecision:
            case EventType.PenaltyTaker:
            case EventType.Penalty:
            case EventType.SetPieceDanger:
            case EventType.CornerKick:
                attackingTeam = ResolveSetPieceAttackingTeam(matchEvent, eventTeam);
                defendingTeam = GetOpposingTeam(attackingTeam);
                _currentPossessionTeam = attackingTeam;
                status = LiveMatchStatus.SetPiece;
                break;

            case EventType.DefensiveError:
                defendingTeam = eventTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
                attackingTeam = GetOpposingTeam(defendingTeam);
                _currentPossessionTeam = attackingTeam;
                status = LiveMatchStatus.Turnover;
                break;

            case EventType.Substitution:
            case EventType.YellowCard:
            case EventType.RedCard:
            case EventType.Injury:
            case EventType.Confrontation:
            case EventType.RefereeControversy:
            case EventType.VarCheck:
            case EventType.VarDecision:
            case EventType.RivalryAtmosphere:
            case EventType.Weather:
            case EventType.Exhaustion:
                attackingTeam = _currentPossessionTeam;
                defendingTeam = GetOpposingTeam(attackingTeam);
                status = attackingTeam is null ? LiveMatchStatus.Neutral : _currentLiveStatus;
                break;

            case EventType.Halftime:
            case EventType.Fulltime:
                _currentPossessionTeam = null;
                break;
        }

        _currentLiveStatus = status;
        UpdateLiveStatusVisuals(status, attackingTeam, defendingTeam);
    }

    private Team? ResolveEventTeam(MatchEvent matchEvent)
    {
        if (_state.CurrentMatch is null)
        {
            return null;
        }

        var teamName = FindTeamName(matchEvent, _state.CurrentMatch);
        if (!string.IsNullOrWhiteSpace(teamName))
        {
            return ResolveTeam(teamName);
        }

        var againstTeam = ResolveAgainstTeam(matchEvent.Description);
        if (againstTeam is not null)
        {
            return againstTeam;
        }

        return ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
    }

    private Team? ResolveSetPieceAttackingTeam(MatchEvent matchEvent, Team? eventTeam)
    {
        var againstTeam = ResolveAgainstTeam(matchEvent.Description);
        if (againstTeam is not null)
        {
            return GetOpposingTeam(againstTeam);
        }

        return eventTeam ?? _currentPossessionTeam ?? ResolvePlayerTeam(matchEvent.PrimaryPlayerName);
    }

    private Team? ResolveAgainstTeam(string description)
    {
        if (_state.CurrentMatch is null)
        {
            return null;
        }

        if (ContainsTeamPhrase(description, "against", _state.CurrentMatch.HomeTeam.Name))
        {
            return _state.CurrentMatch.HomeTeam;
        }

        if (ContainsTeamPhrase(description, "against", _state.CurrentMatch.AwayTeam.Name))
        {
            return _state.CurrentMatch.AwayTeam;
        }

        return null;
    }

    private Team? ResolveTeam(string teamName)
    {
        if (_state.CurrentMatch is null)
        {
            return null;
        }

        if (string.Equals(_state.CurrentMatch.HomeTeam.Name, teamName, StringComparison.OrdinalIgnoreCase))
        {
            return _state.CurrentMatch.HomeTeam;
        }

        if (string.Equals(_state.CurrentMatch.AwayTeam.Name, teamName, StringComparison.OrdinalIgnoreCase))
        {
            return _state.CurrentMatch.AwayTeam;
        }

        return null;
    }

    private Team? ResolvePlayerTeam(string? playerName)
    {
        if (_state.CurrentMatch is null || string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        if (_state.CurrentMatch.HomeTeam.Players.Concat(_state.CurrentMatch.HomeTeam.Substitutes)
            .Any(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return _state.CurrentMatch.HomeTeam;
        }

        if (_state.CurrentMatch.AwayTeam.Players.Concat(_state.CurrentMatch.AwayTeam.Substitutes)
            .Any(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return _state.CurrentMatch.AwayTeam;
        }

        return null;
    }

    private (Team Team, Player Player)? ResolvePlayerContext(string playerName, Team? preferredTeam = null)
    {
        if (_state.CurrentMatch is null)
        {
            return null;
        }

        if (preferredTeam is not null)
        {
            var preferredPlayer = preferredTeam.Players.Concat(preferredTeam.Substitutes)
                .FirstOrDefault(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (preferredPlayer is not null)
            {
                return (preferredTeam, preferredPlayer);
            }
        }

        var homePlayer = _state.CurrentMatch.HomeTeam.Players.Concat(_state.CurrentMatch.HomeTeam.Substitutes)
            .FirstOrDefault(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase));
        if (homePlayer is not null)
        {
            return (_state.CurrentMatch.HomeTeam, homePlayer);
        }

        var awayPlayer = _state.CurrentMatch.AwayTeam.Players.Concat(_state.CurrentMatch.AwayTeam.Substitutes)
            .FirstOrDefault(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase));
        if (awayPlayer is not null)
        {
            return (_state.CurrentMatch.AwayTeam, awayPlayer);
        }

        return null;
    }

    private Team? GetOpposingTeam(Team? team)
    {
        if (_state.CurrentMatch is null || team is null)
        {
            return null;
        }

        if (string.Equals(team.Name, _state.CurrentMatch.HomeTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return _state.CurrentMatch.AwayTeam;
        }

        if (string.Equals(team.Name, _state.CurrentMatch.AwayTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return _state.CurrentMatch.HomeTeam;
        }

        return null;
    }

    private void UpdateLiveStatusVisuals(LiveMatchStatus status, Team? attackingTeam, Team? defendingTeam)
    {
        var headerStatus = GetHeaderPerspectiveStatus(status, attackingTeam, defendingTeam);
        var (text, icon, foreground, background) = headerStatus switch
        {
            LiveMatchStatus.Attacking => ("Attacking...", SwordsIcon(), "#B42318", "#FFE7E3"),
            LiveMatchStatus.Defending => ("Defending...", ShieldIcon(), "#174A8B", "#E8F1FF"),
            LiveMatchStatus.Turnover => ("Turnover...", RotateIcon(), "#6D28D9", "#F0E7FF"),
            LiveMatchStatus.SetPiece => ("Set Piece...", TargetIcon(), "#8A4D00", "#FFF3D6"),
            _ => ("Neutral...", BulletIcon(), "#667085", "#EEF2F7")
        };

        LiveStatusTextBlock.Text = text;
        LiveStatusIconTextBlock.Text = icon;
        LiveStatusTextBlock.Foreground = CreateBrush(foreground);
        LiveStatusIconTextBlock.Foreground = CreateBrush(foreground);
        LiveStatusBadge.Background = CreateBrush(background);

        ApplyScoreboardPossessionColors(_currentPossessionTeam ?? attackingTeam);

        UpdatePitchStatusBadges(status, attackingTeam, defendingTeam);
    }

    private void ApplyScoreboardPossessionColors(Team? possessionTeam)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        ApplyScorePanelColors(
            HomeScorePanel,
            HomeTeamNameTextBlock,
            HomeScoreTextBlock,
            _state.CurrentMatch.HomeTeam,
            IsSameTeam(possessionTeam, _state.CurrentMatch.HomeTeam));
        ApplyScorePanelColors(
            AwayScorePanel,
            AwayTeamNameTextBlock,
            AwayScoreTextBlock,
            _state.CurrentMatch.AwayTeam,
            IsSameTeam(possessionTeam, _state.CurrentMatch.AwayTeam));
    }

    private static bool IsSameTeam(Team? first, Team? second)
    {
        return first is not null &&
            second is not null &&
            string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
    }

    private LiveMatchStatus GetHeaderPerspectiveStatus(LiveMatchStatus eventStatus, Team? attackingTeam, Team? defendingTeam)
    {
        if (_state.SelectedTeam is null || eventStatus == LiveMatchStatus.Neutral)
        {
            return eventStatus;
        }

        if (defendingTeam is not null &&
            string.Equals(defendingTeam.Name, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return LiveMatchStatus.Defending;
        }

        if (attackingTeam is not null &&
            string.Equals(attackingTeam.Name, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return eventStatus == LiveMatchStatus.SetPiece
                ? LiveMatchStatus.SetPiece
                : LiveMatchStatus.Attacking;
        }

        return eventStatus;
    }

    private void UpdatePitchStatusBadges(LiveMatchStatus status, Team? attackingTeam, Team? defendingTeam)
    {
        AttackPitchStatusBadge.Visibility = Visibility.Collapsed;
        DefensePitchStatusBadge.Visibility = Visibility.Collapsed;

        if (_state.CurrentMatch is null || status == LiveMatchStatus.Neutral)
        {
            return;
        }

        if (attackingTeam is not null)
        {
            PlacePitchStatusBadge(AttackPitchStatusBadge, attackingTeam);
            AttackPitchStatusBadge.Visibility = Visibility.Visible;
            AttackPitchStatusIconTextBlock.Text = SwordsIcon();
        }

        if (defendingTeam is not null)
        {
            PlacePitchStatusBadge(DefensePitchStatusBadge, defendingTeam);
            DefensePitchStatusBadge.Visibility = Visibility.Visible;
            DefensePitchStatusIconTextBlock.Text = ShieldIcon();
        }
    }

    private void PlacePitchStatusBadge(FrameworkElement badge, Team team)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        var isHomeSide = string.Equals(team.Name, _state.CurrentMatch.HomeTeam.Name, StringComparison.OrdinalIgnoreCase);
        Grid.SetColumn(badge, isHomeSide ? 0 : 1);
        badge.HorizontalAlignment = isHomeSide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        badge.Margin = isHomeSide
            ? new Thickness(0, 0, 8, 0)
            : new Thickness(8, 0, 0, 0);
    }

    private static Brush CreateBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private async Task PlayGoalEffectAsync(string scoringTeamName, CancellationToken cancellationToken)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        Border? scoringPanel = null;

        if (scoringTeamName == _state.CurrentMatch.HomeTeam.Name)
        {
            scoringPanel = HomeScorePanel;
        }
        else if (scoringTeamName == _state.CurrentMatch.AwayTeam.Name)
        {
            scoringPanel = AwayScorePanel;
        }

        if (scoringPanel is not null)
        {
            var scoringTeam = scoringTeamName == _state.CurrentMatch.HomeTeam.Name
                ? _state.CurrentMatch.HomeTeam
                : _state.CurrentMatch.AwayTeam;
            FlashScorePanel(scoringPanel, GetMatchPalette(scoringTeam).PrimaryColor);
        }

        PulseScoreboard();
        await Task.Delay(450, cancellationToken);
    }

    private static void FlashScorePanel(Border panel, string targetColor)
    {
        var brush = new SolidColorBrush(Color.FromRgb(31, 164, 90));
        panel.Background = brush;

        var animation = new ColorAnimation
        {
            From = Color.FromRgb(31, 164, 90),
            To = (Color)ColorConverter.ConvertFromString(targetColor),
            Duration = TimeSpan.FromMilliseconds(1100),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void PulseScoreboard()
    {
        var grow = new DoubleAnimation
        {
            From = 1,
            To = 1.1,
            Duration = TimeSpan.FromMilliseconds(220),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        ScoreboardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ScoreboardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
    }

    private static MatchFeedItem CreateFeedItem(MatchEvent matchEvent, Match match)
    {
        var teamName = FindTeamName(matchEvent, match);
        var displayTeamName = string.IsNullOrWhiteSpace(teamName) ? "Match" : teamName;
        var eventStyle = GetEventStyle(matchEvent);

        return new MatchFeedItem
        {
            Minute = matchEvent.Minute,
            DisplayMinuteText = string.IsNullOrWhiteSpace(matchEvent.DisplayMinuteText)
                ? MatchEngine.FormatDisplayMinute(match, matchEvent.Minute)
                : matchEvent.DisplayMinuteText,
            SourceEvent = matchEvent,
            Type = matchEvent.EventType.ToString(),
            Icon = eventStyle.Icon,
            TeamName = displayTeamName,
            EventLabel = eventStyle.Label,
            Title = CreateEventTitle(matchEvent, displayTeamName),
            Description = CreateEventDescription(matchEvent),
            ScoreText = CreateScoreText(matchEvent, match),
            TriggeredTrait = matchEvent.TriggeredTrait?.ToString() ?? string.Empty,
            TriggeredTraitIcon = matchEvent.TriggeredTrait.HasValue ? PlayerTraitDisplayService.GetIcon(matchEvent.TriggeredTrait.Value) : string.Empty,
            TriggeredTraitDescription = matchEvent.TriggeredTrait.HasValue
                ? $"{PlayerTraitDisplayService.GetLabel(matchEvent.TriggeredTrait.Value)}: {PlayerTraitDisplayService.GetEffectDescription(matchEvent.TriggeredTrait.Value)}"
                : string.Empty,
            RowBackground = eventStyle.RowBackground,
            RowBorderBrush = eventStyle.RowBorderBrush,
            IconBackground = eventStyle.IconBackground,
            IconForeground = eventStyle.IconForeground,
            LabelBackground = eventStyle.LabelBackground,
            LabelForeground = eventStyle.LabelForeground,
            MinuteForeground = eventStyle.MinuteForeground,
            TitleForeground = eventStyle.TitleForeground,
            DescriptionForeground = eventStyle.DescriptionForeground,
            TraitBadgeBackground = eventStyle.TraitBadgeBackground,
            TraitBadgeBorderBrush = eventStyle.TraitBadgeBorderBrush,
            IsGoal = IsScoringEvent(matchEvent),
            IsImportant = IsImportantEvent(matchEvent.EventType)
        };
    }

    private static string FindTeamName(MatchEvent matchEvent, Match match)
    {
        if (matchEvent.EventType is EventType.AddedTime or EventType.Halftime or EventType.Fulltime)
        {
            return string.Empty;
        }

        var primaryPlayerTeam = FindPlayerTeamName(matchEvent.PrimaryPlayerName, match);
        var secondaryPlayerTeam = FindPlayerTeamName(matchEvent.SecondaryPlayerName, match);

        switch (matchEvent.EventType)
        {
            case EventType.Turnover:
            case EventType.Attack:
            case EventType.ChanceCreated:
            case EventType.Shot:
            case EventType.Miss:
            case EventType.Goal:
            case EventType.WonderGoal:
            case EventType.Woodwork:
            case EventType.LateDrama:
            case EventType.CrowdMomentum:
            case EventType.TimeWasting:
            case EventType.Offside:
            case EventType.BadPass:
            case EventType.Miscontrol:
            case EventType.CornerKick:
            case EventType.SetPieceDanger:
            case EventType.Penalty:
            case EventType.PenaltyTaker:
                return primaryPlayerTeam ?? FindMentionedTeamName(matchEvent, match);

            case EventType.Save:
                return secondaryPlayerTeam ?? FindSavingTeamName(matchEvent, match) ?? FindMentionedTeamName(matchEvent, match);

            case EventType.Foul:
            case EventType.YellowCard:
            case EventType.RedCard:
            case EventType.DefensiveStop:
            case EventType.DefensiveError:
            case EventType.GoalkeeperMistake:
            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
            case EventType.RefereeControversy:
                return primaryPlayerTeam ?? FindMentionedTeamName(matchEvent, match);
        }

        return FindMentionedTeamName(matchEvent, match);
    }

    private static string FindMentionedTeamName(MatchEvent matchEvent, Match match)
    {
        if (ContainsTeamPhrase(matchEvent.Description, "for", match.HomeTeam.Name)
            || StartsWithTeamName(matchEvent.Description, match.HomeTeam.Name)
            || ContainsTeamPhrase(matchEvent.Description, "from", match.HomeTeam.Name)
            || ContainsTeamPhrase(matchEvent.Description, "by", match.HomeTeam.Name))
        {
            return match.HomeTeam.Name;
        }

        if (ContainsTeamPhrase(matchEvent.Description, "for", match.AwayTeam.Name)
            || StartsWithTeamName(matchEvent.Description, match.AwayTeam.Name)
            || ContainsTeamPhrase(matchEvent.Description, "from", match.AwayTeam.Name)
            || ContainsTeamPhrase(matchEvent.Description, "by", match.AwayTeam.Name))
        {
            return match.AwayTeam.Name;
        }

        return string.Empty;
    }

    private static string? FindSavingTeamName(MatchEvent matchEvent, Match match)
    {
        if (ContainsTeamPhrase(matchEvent.Description, "by", match.HomeTeam.Name) ||
            ContainsTeamPhrase(matchEvent.Description, "for", match.HomeTeam.Name) ||
            ContainsTeamPhrase(matchEvent.Description, "of", match.HomeTeam.Name))
        {
            return match.HomeTeam.Name;
        }

        if (ContainsTeamPhrase(matchEvent.Description, "by", match.AwayTeam.Name) ||
            ContainsTeamPhrase(matchEvent.Description, "for", match.AwayTeam.Name) ||
            ContainsTeamPhrase(matchEvent.Description, "of", match.AwayTeam.Name))
        {
            return match.AwayTeam.Name;
        }

        return null;
    }

    private static string? FindPlayerTeamName(string? playerName, Match match)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        if (match.HomeTeam.Players.Concat(match.HomeTeam.Substitutes)
            .Any(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return match.HomeTeam.Name;
        }

        if (match.AwayTeam.Players.Concat(match.AwayTeam.Substitutes)
            .Any(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return match.AwayTeam.Name;
        }

        return null;
    }

    private static bool ContainsTeamPhrase(string description, string phrase, string teamName)
    {
        return description.Contains($"{phrase} {teamName}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithTeamName(string description, string teamName)
    {
        return description.StartsWith($"{teamName} ", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateEventTitle(MatchEvent matchEvent, string teamName)
    {
        var headline = matchEvent.EventType switch
        {
            EventType.Kickoff => matchEvent.Description.Contains("second half", StringComparison.OrdinalIgnoreCase)
                ? "Second half underway"
                : "Kickoff underway",
            EventType.Attack => CreateAttackHeadline(matchEvent, teamName),
            EventType.ChanceCreated => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "creates chance", $"{teamName} create chance"),
            EventType.Shot => CreateShotHeadline(matchEvent, teamName),
            EventType.Save => CreateSaveHeadline(matchEvent),
            EventType.Goal => CreateGoalHeadline(matchEvent, teamName),
            EventType.Foul => CreateFoulHeadline(matchEvent),
            EventType.YellowCard => matchEvent.Description.Contains("Both players", StringComparison.OrdinalIgnoreCase)
                ? "Both players booked"
                : CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "booked", "Yellow card shown"),
            EventType.RedCard => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "sent off", "Red card shown"),
            EventType.Injury => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "goes down injured", "Injury concern"),
            EventType.PenaltyDecision => "Penalty awarded",
            EventType.PenaltyTaker => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "steps up", $"{teamName} penalty taker"),
            EventType.Penalty => CreatePenaltyHeadline(matchEvent, teamName),
            EventType.Offside => IsGoalDisallowedEvent(matchEvent)
                ? "Goal ruled out"
                : CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "caught offside", $"{teamName} offside"),
            EventType.BadPass => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misplaces pass", "Bad pass"),
            EventType.Miscontrol => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "miscontrols", "Miscontrol"),
            EventType.Tackle => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "wins tackle", "Tackle won"),
            EventType.Interception => CreateInterceptionHeadline(matchEvent),
            EventType.Pressure => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "forces pressure", "Pressure forces error"),
            EventType.BlockedPass => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "blocks pass", "Blocked pass"),
            EventType.Turnover => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "gets the ball", $"{teamName} regain possession"),
            EventType.DefensiveStop => CreateDefensiveHeadline(matchEvent),
            EventType.DefensiveError => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "makes defensive error", $"{teamName} make error"),
            EventType.Woodwork => CreateWoodworkHeadline(matchEvent, teamName),
            EventType.WonderGoal => CreateWonderGoalHeadline(matchEvent, teamName),
            EventType.GoalkeeperHeroics => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "keeps them alive", "Goalkeeper heroics"),
            EventType.GoalkeeperMistake => CreateGoalkeeperMistakeHeadline(matchEvent),
            EventType.CornerKick => $"{teamName} win corner",
            EventType.SetPieceDanger => $"{teamName} threaten from set piece",
            EventType.Confrontation => "Players clash",
            EventType.CrowdMomentum => $"{teamName} gain momentum",
            EventType.TimeWasting => $"{teamName} slow the game",
            EventType.LateDrama => $"{teamName} push late",
            EventType.RivalryAtmosphere => "Derby tension rises",
            EventType.Weather => "Weather affects play",
            EventType.AddedTime => "Added time announced",
            EventType.VarCheck => "VAR checking incident",
            EventType.VarDecision => IsGoalDisallowedEvent(matchEvent)
                ? "Goal ruled out"
                : "VAR decision",
            EventType.RefereeControversy => matchEvent.Description.Contains("warns", StringComparison.OrdinalIgnoreCase)
                ? "Referee warning"
                : "Referee controversy",
            EventType.Exhaustion => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "looks exhausted", "Stamina dropping"),
            EventType.Substitution => CreateSubstitutionHeadline(matchEvent),
            EventType.Halftime => "Halftime",
            EventType.Fulltime => "Full time",
            EventType.Miss => IsPenaltyResult(matchEvent)
                ? CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misses penalty", $"{teamName} miss penalty")
                : CreateMissHeadline(matchEvent, teamName),
            _ => "Match Event"
        };

        return LimitHeadlineWords(headline, 8);
    }

    private static string CreateEventDescription(MatchEvent matchEvent)
    {
        if (IsGoalDisallowedEvent(matchEvent))
        {
            return matchEvent.Description;
        }

        var suffix = matchEvent.EventType switch
        {
            EventType.Goal => "Crowd erupts.",
            EventType.ChanceCreated => "Opening created.",
            EventType.Save => IsPenaltyResult(matchEvent) ? string.Empty : "Huge stop.",
            EventType.Miss => IsPenaltyResult(matchEvent) ? string.Empty : "Shot Off Target.",
            EventType.Foul => matchEvent.IsPenaltyFoul || matchEvent.FoulLocation == FoulLocation.PenaltyBox
                ? string.Empty
                : "Play is stopped.",
            EventType.YellowCard => "He is on a booking.",
            EventType.RedCard => "They are down to ten.",
            EventType.Injury => "Medical staff are watching.",
            EventType.PenaltyDecision => "The referee points to the spot.",
            EventType.PenaltyTaker => "Pressure from the spot.",
            EventType.Penalty => "Pressure from the spot.",
            EventType.Offside => "The line holds.",
            EventType.Turnover => string.Empty,
            EventType.DefensiveStop => "Strong defending.",
            EventType.DefensiveError => "Pressure is on.",
            EventType.WonderGoal => "Top-class finish.",
            EventType.GoalkeeperHeroics => "Brilliant keeping.",
            EventType.CornerKick => "Corner delivery coming.",
            EventType.SetPieceDanger => "Danger from dead ball.",
            EventType.Confrontation => "Tempers rise.",
            EventType.CrowdMomentum => "Stadium noise lifts them.",
            EventType.TimeWasting => "The clock keeps ticking.",
            EventType.Exhaustion => "Fatigue is showing.",
            EventType.Substitution => "Fresh legs arrive.",
            EventType.AddedTime => "Minimum stoppage time confirmed.",
            _ => string.Empty
        };

        var description = string.IsNullOrWhiteSpace(suffix)
            ? matchEvent.Description
            : $"{matchEvent.Description} {suffix}";

        return description;
    }

    private static string CreateScoreText(MatchEvent matchEvent, Match match)
    {
        if (!IsScoringEvent(matchEvent))
        {
            return string.Empty;
        }

        var homeScore = matchEvent.HomeScore ?? match.HomeScore;
        var awayScore = matchEvent.AwayScore ?? match.AwayScore;
        return $"{homeScore} - {awayScore}";
    }

    private static bool IsScoringEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType == EventType.Goal
            || matchEvent.EventType == EventType.WonderGoal
            || (matchEvent.EventType == EventType.Penalty &&
                matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGoalDisallowedEvent(MatchEvent matchEvent)
    {
        return (matchEvent.EventType is EventType.VarDecision or EventType.Offside) &&
            (matchEvent.Description.Contains("goal ruled out", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal is ruled out", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal disallowed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGoalConfirmedByVarEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType == EventType.VarDecision &&
            (matchEvent.Description.Contains("confirms the goal", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal stands", StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateAttackHeadline(MatchEvent matchEvent, string teamName)
    {
        var description = matchEvent.Description;
        if (description.Contains("BIG CHANCE", StringComparison.OrdinalIgnoreCase))
        {
            return $"{teamName} big chance";
        }

        if (description.Contains("loose ball", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("keeper", StringComparison.OrdinalIgnoreCase))
        {
            return $"{teamName} rebound chance";
        }

        if (description.Contains("miscontrols", StringComparison.OrdinalIgnoreCase)
            || description.Contains("turns over", StringComparison.OrdinalIgnoreCase)
            || description.Contains("win it back", StringComparison.OrdinalIgnoreCase)
            || description.Contains("dispossessed", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "loses possession", $"{teamName} lose possession");
        }

        if (description.Contains("through ball", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.SecondaryPlayerName, "through on goal", $"{teamName} break through");
        }

        if (description.Contains("build", StringComparison.OrdinalIgnoreCase)
            || description.Contains("midfield", StringComparison.OrdinalIgnoreCase))
        {
            return $"{teamName} build quickly";
        }

        return $"{teamName} on the attack";
    }

    private static string CreateShotHeadline(MatchEvent matchEvent, string teamName)
    {
        var classifiedHeadline = matchEvent.ShotClassification switch
        {
            ShotClassification.Header => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "heads at goal", $"{teamName} header"),
            ShotClassification.Volley => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "volleys goalward", $"{teamName} volley"),
            ShotClassification.LongShot => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "shoots from distance", $"{teamName} long shot"),
            ShotClassification.FreeKick => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "takes free kick", $"{teamName} free kick"),
            ShotClassification.Penalty => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "takes penalty", $"{teamName} penalty"),
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(classifiedHeadline))
        {
            return classifiedHeadline;
        }

        if (matchEvent.Description.Contains("REBOUND SHOT", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "rebound shot", $"{teamName} rebound shot");
        }

        if (matchEvent.Description.Contains("defensive error", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains("mistake", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "punishes mistake", $"{teamName} punish mistake");
        }

        return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "shoots", $"{teamName} shoot");
    }

    private static string CreateMissHeadline(MatchEvent matchEvent, string teamName)
    {
        return matchEvent.ShotClassification switch
        {
            ShotClassification.Header => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "heads wide", $"{teamName} header wide"),
            ShotClassification.Volley => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "volleys wide", $"{teamName} volley wide"),
            ShotClassification.LongShot => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "shoots wide", $"{teamName} long shot wide"),
            ShotClassification.FreeKick => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "free kick wide", $"{teamName} free kick wide"),
            _ => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misses chance", $"{teamName} miss chance")
        };
    }

    private static string CreateDefensiveHeadline(MatchEvent matchEvent)
    {
        var description = matchEvent.Description;
        if (description.Contains("tackle", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "wins the tackle", "Big defensive tackle");
        }

        if (description.Contains("block", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "blocks the shot", "Shot blocked");
        }

        if (description.Contains("clearance", StringComparison.OrdinalIgnoreCase)
            || description.Contains("clears", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "clears danger", "Defender clears danger");
        }

        return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "intercepts the attack", "Defense stop the move");
    }

    private static string CreateInterceptionHeadline(MatchEvent matchEvent)
    {
        var defenderName = GetHeadlinePlayerName(matchEvent.PrimaryPlayerName);
        var attackerName = GetHeadlinePlayerName(matchEvent.SecondaryPlayerName);
        if (string.IsNullOrWhiteSpace(defenderName))
        {
            return "Interception";
        }

        return string.IsNullOrWhiteSpace(attackerName)
            ? $"{defenderName} intercepts"
            : $"{defenderName} intercepts {attackerName}'s pass";
    }

    private static string CreateSaveHeadline(MatchEvent matchEvent)
    {
        if (IsPenaltyResult(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.SecondaryPlayerName, "saves penalty", "Penalty saved");
        }

        if (!string.IsNullOrWhiteSpace(matchEvent.SecondaryPlayerName))
        {
            return CreatePlayerHeadline(matchEvent.SecondaryPlayerName, "makes the save", "Important save");
        }

        return "Important save";
    }

    private static string CreateWoodworkHeadline(MatchEvent matchEvent, string teamName)
    {
        var description = matchEvent.Description;
        if (description.Contains("crossbar", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("bar", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "rattles crossbar", $"{teamName} rattle crossbar");
        }

        if (description.Contains("post", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "hits the post", $"{teamName} hit the post");
        }

        if (description.Contains("denies", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "denied by woodwork", $"Woodwork denies {teamName}");
        }

        if (description.Contains("rebound", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("chaos", StringComparison.OrdinalIgnoreCase))
        {
            return $"{teamName} denied by woodwork";
        }

        return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "hits woodwork", $"{teamName} hit woodwork");
    }

    private static string CreateGoalkeeperMistakeHeadline(MatchEvent matchEvent)
    {
        var description = matchEvent.Description;
        if (description.Contains("spill", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "spills the ball", "Keeper spills the ball");
        }

        if (description.Contains("pass", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("miskick", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misplaces pass", "Keeper makes mistake");
        }

        return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "makes a mistake", "Keeper makes mistake");
    }

    private static string CreateGoalHeadline(MatchEvent matchEvent, string teamName)
    {
        if (IsHatTrickGoalEvent(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "completes hat-trick", $"{teamName} hat-trick");
        }

        if (IsExtraordinaryScoringEvent(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores again", $"{teamName} scoring display");
        }

        if (IsBraceGoalEvent(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores brace", $"{teamName} brace");
        }

        return IsPenaltyResult(matchEvent)
            ? CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores penalty", $"{teamName} score penalty")
            : CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores", $"GOAL for {teamName}");
    }

    private static string CreateWonderGoalHeadline(MatchEvent matchEvent, string teamName)
    {
        if (IsHatTrickGoalEvent(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "completes hat-trick", $"{teamName} hat-trick");
        }

        if (IsExtraordinaryScoringEvent(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores again", $"{teamName} scoring display");
        }

        if (IsBraceGoalEvent(matchEvent))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores brace", $"{teamName} brace");
        }

        return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores wonder goal", $"Wonder goal for {teamName}");
    }

    private static string CreatePenaltyHeadline(MatchEvent matchEvent, string teamName)
    {
        if (matchEvent.Description.Contains("is saved", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "penalty saved", $"{teamName} penalty saved");
        }

        if (matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores penalty", $"{teamName} score penalty");
        }

        return $"{teamName} win penalty";
    }

    private static string CreateSubstitutionHeadline(MatchEvent matchEvent)
    {
        var playerIn = GetHeadlinePlayerName(matchEvent.PrimaryPlayerName);
        var playerOut = GetHeadlinePlayerName(matchEvent.SecondaryPlayerName);
        if (!string.IsNullOrWhiteSpace(playerIn) && !string.IsNullOrWhiteSpace(playerOut))
        {
            return $"{playerIn} replaces {playerOut}";
        }

        return string.IsNullOrWhiteSpace(playerIn)
            ? "Substitution made"
            : $"{playerIn} comes on";
    }

    private static string CreatePlayerHeadline(string? playerName, string action, string fallback)
    {
        var shortName = GetHeadlinePlayerName(playerName);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return fallback;
        }

        return $"{shortName} {action}";
    }

    private static string CreateFoulHeadline(MatchEvent matchEvent)
    {
        if (matchEvent.IsPenaltyFoul || matchEvent.FoulLocation == FoulLocation.PenaltyBox)
        {
            var foulingPlayer = GetHeadlinePlayerName(string.IsNullOrWhiteSpace(matchEvent.FoulingPlayer)
                ? matchEvent.PrimaryPlayerName
                : matchEvent.FoulingPlayer);
            var fouledPlayer = GetHeadlinePlayerName(string.IsNullOrWhiteSpace(matchEvent.FouledPlayer)
                ? matchEvent.SecondaryPlayerName
                : matchEvent.FouledPlayer);

            if (!string.IsNullOrWhiteSpace(foulingPlayer) && !string.IsNullOrWhiteSpace(fouledPlayer))
            {
                return $"{foulingPlayer} fouls {fouledPlayer} inside the box";
            }
        }

        return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "commits foul", "Foul given");
    }

    private static string GetHeadlinePlayerName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : parts[0];
    }

    private static string LimitHeadlineWords(string headline, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(headline))
        {
            return headline;
        }

        var words = headline.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
        {
            return headline;
        }

        return string.Join(" ", words.Take(maxWords));
    }


    private static bool IsPenaltyResult(MatchEvent matchEvent)
    {
        return matchEvent.Description.Contains("penalty", StringComparison.OrdinalIgnoreCase)
            || matchEvent.Description.Contains("from the spot", StringComparison.OrdinalIgnoreCase)
            || matchEvent.Description.Contains("spot kick", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHatTrickGoalEvent(MatchEvent matchEvent)
    {
        return matchEvent.Description.Contains("HAT-TRICK", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains("hat-trick", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains("third goal of the match", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBraceGoalEvent(MatchEvent matchEvent)
    {
        return matchEvent.Description.Contains("brace", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExtraordinaryScoringEvent(MatchEvent matchEvent)
    {
        return matchEvent.Description.Contains("extraordinary scoring display", StringComparison.OrdinalIgnoreCase);
    }

    private static FeedEventStyle GetEventStyle(MatchEvent matchEvent)
    {
        if (matchEvent.EventType == EventType.Miss && IsPenaltyResult(matchEvent))
        {
            return MissStyle(WarningIcon(), "MISSED PENALTY");
        }

        if (IsGoalDisallowedEvent(matchEvent))
        {
            return DisallowedGoalStyle(FlagIcon(), "GOAL RULED OUT");
        }

        if (IsGoalConfirmedByVarEvent(matchEvent))
        {
            return GoalStyle(CheckeredFlagIcon(), "VAR DECISION");
        }

        if (IsHatTrickGoalEvent(matchEvent))
        {
            return GoalStyle(HatTrickIcon(), "HAT-TRICK");
        }

        if (IsExtraordinaryScoringEvent(matchEvent))
        {
            return GoalStyle(SoccerBallIcon(), "SCORING DISPLAY");
        }

        return matchEvent.EventType switch
        {
            EventType.Kickoff => MatchBoundaryStyle(FlagIcon(), "KICKOFF"),
            EventType.Attack => AttackStyle(SwordsIcon(), "ATTACK"),
            EventType.ChanceCreated => ChanceStyle(StarIcon(), "CHANCE CREATED"),
            EventType.Shot => ShotStyle(matchEvent),
            EventType.Save => SaveStyle(GloveIcon(), "SAVE"),
            EventType.Foul => FoulStyle(StopIcon(), "FOUL"),
            EventType.Miss => MissStyle(WarningIcon(), "MISS"),
            EventType.Woodwork => MissStyle(WarningIcon(), "WOODWORK"),
            EventType.Goal => GoalStyle(SoccerBallIcon(), "GOAL"),
            EventType.YellowCard => YellowCardStyle(YellowCardIcon(), "YELLOW CARD"),
            EventType.RedCard => RedCardStyle(RedCardIcon(), "RED CARD"),
            EventType.Injury => FoulStyle(InjuryIcon(), "INJURY"),
            EventType.PenaltyDecision => SolidOrangeStyle(GoalNetIcon(), "PENALTY DECISION"),
            EventType.PenaltyTaker => ChanceStyle(TargetIcon(), "PENALTY TAKER"),
            EventType.Penalty => ChanceStyle(GoalNetIcon(), "PENALTY"),
            EventType.Offside => MissStyle(FlagIcon(), "OFFSIDE"),
            EventType.BadPass => MissStyle(WarningIcon(), "BAD PASS"),
            EventType.Miscontrol => MissStyle(WarningIcon(), "MISCONTROL"),
            EventType.Tackle => DefensiveStyle(ShieldIcon(), "TACKLE"),
            EventType.Interception => DefensiveStyle(ShieldIcon(), "INTERCEPTION"),
            EventType.Pressure => DefensiveStyle(ShieldIcon(), "PRESSURE"),
            EventType.BlockedPass => DefensiveStyle(ShieldIcon(), "BLOCKED PASS"),
            EventType.Turnover => TurnoverStyle(RotateIcon(), "TURNOVER"),
            EventType.DefensiveStop => DefensiveStyle(ShieldIcon(), "DEFENSE"),
            EventType.DefensiveError => ChanceStyle(WarningIcon(), "DEFENSIVE ERROR"),
            EventType.WonderGoal => GoalStyle(StarIcon(), "WONDER GOAL"),
            EventType.GoalkeeperHeroics => SaveStyle(GloveIcon(), "KEEPER HEROICS"),
            EventType.CornerKick => ChanceStyle(FlagIcon(), "CORNER KICK"),
            EventType.SetPieceDanger => ChanceStyle(TargetIcon(), "SET PIECE"),
            EventType.Weather => WeatherStyle(matchEvent),
            EventType.AddedTime => MatchBoundaryStyle(ClockIcon(), "ADDED TIME"),
            EventType.TimeWasting => FoulStyle(ClockIcon(), "TIME WASTING"),
            EventType.VarCheck => VarStyle(TargetIcon(), "VAR CHECK"),
            EventType.VarDecision => VarStyle(CheckeredFlagIcon(), "VAR DECISION"),
            EventType.RefereeControversy => FoulStyle(StopIcon(), "CONTROVERSY"),
            EventType.GoalkeeperMistake => ChanceStyle(WarningIcon(), "KEEPER ERROR"),
            EventType.LateDrama => AttackStyle(FireIcon(), "LATE DRAMA"),
            EventType.RivalryAtmosphere => FoulStyle(FireIcon(), "RIVALRY"),
            EventType.Confrontation => RedCardStyle(FireIcon(), "CONFRONTATION"),
            EventType.CrowdMomentum => AttackStyle(MegaphoneIcon(), "CROWD MOMENTUM"),
            EventType.Exhaustion => MissStyle(BatteryIcon(), "EXHAUSTION"),
            EventType.Substitution => NeutralStyle(RotateIcon(), "SUBSTITUTION"),
            EventType.Halftime => NeutralStyle(PauseIcon(), "HALFTIME"),
            EventType.Fulltime => MatchBoundaryStyle(CheckeredFlagIcon(), "FULLTIME"),
            _ => NeutralStyle(BulletIcon(), "EVENT")
        };
    }

    private static FeedEventStyle AttackStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedAttack", "#FEF2F2", "#EF4444", "#FEE2E2", "#B91C1C");
    }

    private static FeedEventStyle DefensiveStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedDefensive", "#EFF6FF", "#3B82F6", "#DBEAFE", "#1D4ED8");
    }

    private static FeedEventStyle TurnoverStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedTurnover", "#F5F3FF", "#8B5CF6", "#EDE9FE", "#6D28D9");
    }

    private static FeedEventStyle ChanceStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedChance", "#FFF7ED", "#F97316", "#FFEDD5", "#C2410C");
    }

    private static FeedEventStyle VarStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#7C3AED", "#5B21B6", "#EDE9FE", "#5B21B6", "#FFFFFF", IconForeground: "#5B21B6", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#EDE9FE", TraitBadgeBackground: "#EDE9FE", TraitBadgeBorderBrush: "#C4B5FD");
    }

    private static FeedEventStyle SolidOrangeStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#F97316", "#C2410C", "#FFEDD5", "#C2410C", "#FFFFFF", IconForeground: "#C2410C", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#FFEDD5", TraitBadgeBackground: "#FFEDD5", TraitBadgeBorderBrush: "#FDBA74");
    }

    private static FeedEventStyle ShotStyle(MatchEvent matchEvent)
    {
        return matchEvent.ShotClassification switch
        {
            ShotClassification.Header => SolidOrangeStyle(HeaderIcon(), "HEADER"),
            ShotClassification.Volley => SolidOrangeStyle(SoccerBallIcon(), "VOLLEY"),
            ShotClassification.LongShot => SolidOrangeStyle(TargetIcon(), "LONG SHOT"),
            ShotClassification.FreeKick => SolidOrangeStyle(TargetIcon(), "FREE KICK"),
            ShotClassification.Penalty => SolidOrangeStyle(GoalNetIcon(), "PENALTY"),
            _ => SolidOrangeStyle(TargetIcon(), "SHOT")
        };
    }

    private static FeedEventStyle MatchBoundaryStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#111827", "#030712", "#F3F4F6", "#111827", "#FFFFFF", IconForeground: "#111827", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#E5E7EB", TraitBadgeBackground: "#F3F4F6", TraitBadgeBorderBrush: "#9CA3AF");
    }

    private static FeedEventStyle DisallowedGoalStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#4B5563", "#374151", "#E5E7EB", "#374151", "#FFFFFF", IconForeground: "#374151", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#F3F4F6", TraitBadgeBackground: "#E5E7EB", TraitBadgeBorderBrush: "#9CA3AF");
    }

    private static FeedEventStyle FoulStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedFoul", "#FEFCE8", "#EAB308", "#FEF9C3", "#854D0E");
    }

    private static FeedEventStyle MissStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedMiss", "#F3F4F6", "#6B7280", "#E5E7EB", "#374151");
    }

    private static FeedEventStyle SaveStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#1D4ED8", "#1E40AF", "#DBEAFE", "#1E40AF", "#FFFFFF", IconForeground: "#1D4ED8", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#DBEAFE", TraitBadgeBackground: "#DBEAFE", TraitBadgeBorderBrush: "#93C5FD");
    }

    private static FeedEventStyle GoalStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#16A34A", "#15803D", "#DCFCE7", "#15803D", "#FFFFFF", IconForeground: "#166534", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#DCFCE7", TraitBadgeBackground: "#DCFCE7", TraitBadgeBorderBrush: "#86EFAC");
    }

    private static FeedEventStyle RedCardStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#DC2626", "#991B1B", "#FEE2E2", "#991B1B", "#FFFFFF", IconForeground: "#991B1B", MinuteForeground: "#FFFFFF", TitleForeground: "#FFFFFF", DescriptionForeground: "#FEE2E2", TraitBadgeBackground: "#FEE2E2", TraitBadgeBorderBrush: "#FCA5A5");
    }

    private static FeedEventStyle YellowCardStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#FACC15", "#CA8A04", "#FEF9C3", "#FDE047", "#713F12", IconForeground: "#713F12", MinuteForeground: "#422006", TitleForeground: "#422006", DescriptionForeground: "#713F12", TraitBadgeBackground: "#FEF9C3", TraitBadgeBorderBrush: "#CA8A04");
    }

    private static FeedEventStyle NeutralStyle(string icon, string label)
    {
        return CreateThemedFeedStyle(icon, label, "FeedNeutral", "#FFFFFF", "#CBD5E1", "#F1F5F9", "#334155");
    }

    private static FeedEventStyle WeatherStyle(MatchEvent matchEvent)
    {
        var weather = matchEvent.WeatherCondition ?? InferWeatherFromDescription(matchEvent.Description);
        return weather switch
        {
            WeatherCondition.Clear => new FeedEventStyle(SunIcon(), "WEATHER", "#FFF8DB", "#F4D35E", "#FEF3C7", "#FACC15", "#713F12", IconForeground: "#B45309", TraitBadgeBackground: "#FEF3C7", TraitBadgeBorderBrush: "#F4D35E"),
            WeatherCondition.Rainy or WeatherCondition.HeavyRain => new FeedEventStyle(RainCloudIcon(), "WEATHER", "#E6F1FF", "#93C5FD", "#DBEAFE", "#BFDBFE", "#1E3A8A", IconForeground: "#1D4ED8", TraitBadgeBackground: "#DBEAFE", TraitBadgeBorderBrush: "#93C5FD"),
            WeatherCondition.Storm => new FeedEventStyle(StormIcon(), "WEATHER", "#EDE9FE", "#A78BFA", "#DDD6FE", "#C4B5FD", "#312E81", IconForeground: "#4C1D95", TraitBadgeBackground: "#DDD6FE", TraitBadgeBorderBrush: "#A78BFA"),
            WeatherCondition.Snow => new FeedEventStyle(SnowflakeIcon(), "WEATHER", "#EAF6FF", "#93C5FD", "#DBEAFE", "#BFDBFE", "#1E3A8A", IconForeground: "#2563EB", TraitBadgeBackground: "#DBEAFE", TraitBadgeBorderBrush: "#93C5FD"),
            WeatherCondition.Windy => new FeedEventStyle(WindIcon(), "WEATHER", "#F1F5F9", "#CBD5E1", "#E2E8F0", "#CBD5E1", "#334155", IconForeground: "#475569", TraitBadgeBackground: "#E2E8F0", TraitBadgeBorderBrush: "#CBD5E1"),
            WeatherCondition.Foggy => new FeedEventStyle(FogIcon(), "WEATHER", "#F3F4F6", "#D1D5DB", "#E5E7EB", "#D1D5DB", "#374151", IconForeground: "#6B7280", TraitBadgeBackground: "#E5E7EB", TraitBadgeBorderBrush: "#D1D5DB"),
            WeatherCondition.Hot => new FeedEventStyle(HeatIcon(), "WEATHER", "#FFF1E6", "#FDBA74", "#FFEDD5", "#FED7AA", "#9A3412", IconForeground: "#C2410C", TraitBadgeBackground: "#FFEDD5", TraitBadgeBorderBrush: "#FDBA74"),
            WeatherCondition.Cold => new FeedEventStyle(ColdIcon(), "WEATHER", "#ECFEFF", "#67E8F9", "#CFFAFE", "#A5F3FC", "#155E75", IconForeground: "#0E7490", TraitBadgeBackground: "#CFFAFE", TraitBadgeBorderBrush: "#67E8F9"),
            _ => NeutralStyle(FlagIcon(), "WEATHER")
        };
    }

    private static WeatherCondition InferWeatherFromDescription(string description)
    {
        if (description.Contains("thunder", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("storm", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Storm;
        }

        if (description.Contains("heavy rain", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.HeavyRain;
        }

        if (description.Contains("rain", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Rainy;
        }

        if (description.Contains("snow", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Snow;
        }

        if (description.Contains("wind", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Windy;
        }

        if (description.Contains("fog", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("mist", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Foggy;
        }

        if (description.Contains("hot", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("humid", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Hot;
        }

        if (description.Contains("cold", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("freezing", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherCondition.Cold;
        }

        return WeatherCondition.Clear;
    }

    private static FeedEventStyle CreateThemedFeedStyle(
        string icon,
        string label,
        string keyPrefix,
        string fallbackBackground,
        string fallbackBorder,
        string fallbackIconBackground,
        string fallbackAccent)
    {
        var background = ThemeManager.GetBrushHex($"{keyPrefix}Background", fallbackBackground);
        var border = ThemeManager.GetBrushHex($"{keyPrefix}Border", fallbackBorder);
        var iconBackground = ThemeManager.GetBrushHex($"{keyPrefix}IconBackground", fallbackIconBackground);
        var iconForeground = ThemeManager.GetBrushHex($"{keyPrefix}IconForeground", fallbackAccent);
        var labelBackground = ThemeManager.GetBrushHex($"{keyPrefix}LabelBackground", fallbackIconBackground);
        var labelForeground = ThemeManager.GetBrushHex($"{keyPrefix}LabelForeground", fallbackAccent);

        return new FeedEventStyle(
            icon,
            label,
            background,
            border,
            iconBackground,
            labelBackground,
            labelForeground,
            IconForeground: iconForeground,
            MinuteForeground: ThemeManager.GetBrushHex("FeedMinuteForeground", "#14233A"),
            TitleForeground: ThemeManager.GetBrushHex("FeedTitleForeground", "#071A2E"),
            DescriptionForeground: ThemeManager.GetBrushHex("FeedDescriptionForeground", "#34465C"),
            TraitBadgeBackground: iconBackground,
            TraitBadgeBorderBrush: border);
    }

    private static bool IsImportantEvent(EventType eventType)
    {
        return eventType is EventType.Goal
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.Miss
            or EventType.Injury
            or EventType.PenaltyDecision
            or EventType.PenaltyTaker
            or EventType.Penalty
            or EventType.BadPass
            or EventType.Miscontrol
            or EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass
            or EventType.Turnover
            or EventType.ChanceCreated
            or EventType.DefensiveStop
            or EventType.DefensiveError
            or EventType.WonderGoal
            or EventType.GoalkeeperHeroics
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Confrontation
            or EventType.CrowdMomentum
            or EventType.Exhaustion
            or EventType.AddedTime
            or EventType.TimeWasting
            or EventType.Halftime
            or EventType.Fulltime;
    }

    private static bool ShouldRefreshPitchAfterEvent(EventType eventType)
    {
        return eventType is EventType.Goal
            or EventType.WonderGoal
            or EventType.Penalty
            or EventType.Save
            or EventType.Miss
            or EventType.Foul
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.Injury
            or EventType.Offside
            or EventType.BadPass
            or EventType.Miscontrol
            or EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass
            or EventType.Turnover
            or EventType.ChanceCreated
            or EventType.DefensiveStop
            or EventType.DefensiveError
            or EventType.GoalkeeperHeroics
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Confrontation
            or EventType.Exhaustion
            or EventType.TimeWasting
            or EventType.Substitution
            or EventType.Halftime
            or EventType.Fulltime;
    }

    private int GetDelayFor(MatchFeedItem feedItem)
    {
        var baseDelay = GetBaseDelayMilliseconds(_speedLevel);
        var extraDelay = (int)Math.Round(GetEventExtraDelayMilliseconds(feedItem.Type) * GetEventDelayMultiplier(_speedLevel));
        return baseDelay + extraDelay;
    }

    private static int GetRevealDelayMilliseconds(MatchFeedItem feedItem)
    {
        return feedItem.IsImportant ? 350 : 180;
    }

    private static int GetBaseDelayMilliseconds(int speedLevel)
    {
        return speedLevel switch
        {
            0 => VerySlowBaseDelayMilliseconds,
            1 => MediumBaseDelayMilliseconds,
            2 => OnePointFiveBaseDelayMilliseconds,
            3 => FastBaseDelayMilliseconds,
            4 => 700,
            _ => MediumBaseDelayMilliseconds
        };
    }

    private static double GetEventDelayMultiplier(int speedLevel)
    {
        return speedLevel switch
        {
            0 => 1.0,
            1 => 0.75,
            2 => 0.60,
            3 => 0.45,
            4 => 0.15,
            _ => 0.75
        };
    }

    private static int GetEventExtraDelayMilliseconds(string eventType)
    {
        return eventType switch
        {
            nameof(EventType.Shot) or nameof(EventType.Save) => 500,
            nameof(EventType.Foul) or nameof(EventType.Offside) or nameof(EventType.Exhaustion) or nameof(EventType.DefensiveStop) or nameof(EventType.Tackle) or nameof(EventType.Interception) or nameof(EventType.Pressure) or nameof(EventType.BlockedPass) => 500,
            nameof(EventType.YellowCard) => 1000,
            nameof(EventType.RedCard) or nameof(EventType.Miss) or nameof(EventType.Injury) or nameof(EventType.BadPass) or nameof(EventType.Miscontrol) or nameof(EventType.DefensiveError) or nameof(EventType.SetPieceDanger) or nameof(EventType.CornerKick) or nameof(EventType.Confrontation) => 1500,
            nameof(EventType.Goal) or nameof(EventType.Penalty) or nameof(EventType.WonderGoal) or nameof(EventType.GoalkeeperHeroics) or nameof(EventType.CrowdMomentum) => 2000,
            nameof(EventType.AddedTime) or nameof(EventType.TimeWasting) or nameof(EventType.Halftime) or nameof(EventType.Fulltime) => 1500,
            _ => 0
        };
    }

    private static int GetStaminaPercentage(Player player)
    {
        return Math.Clamp((int)Math.Round(player.Stamina), 0, 100);
    }

    private static string GetStaminaBrush(int staminaPercentage)
    {
        return staminaPercentage switch
        {
            < 30 => "#EF4444",
            < 50 => "#FB923C",
            <= 70 => "#FACC15",
            _ => "#86EFAC"
        };
    }

    private List<SubstitutionPlayerCard> CreateSubstitutionPlayerCards(IEnumerable<Player> players, bool showPendingState, Team? team)
    {
        return players
            .Select(player => CreateSubstitutionPlayerCard(
                player,
                showPendingState
                    ? _pendingSubstitutions.FirstOrDefault(pending =>
                        string.Equals(pending.PlayerIn.Name, player.Name, StringComparison.OrdinalIgnoreCase))
                    : null,
                team))
            .ToList();
    }

    private SubstitutionPlayerCard CreateSubstitutionPlayerCard(Player player, PendingSubstitutionViewModel? pendingSubstitution, Team? team)
    {
        var stamina = GetStaminaPercentage(player);
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var exactPosition = PositionSuitabilityService.NormalizeExactPosition(player.AssignedPosition);
        var isPendingSubIn = pendingSubstitution is not null;
        var teamColors = TeamColorService.GetPalette(team);
        var displayedStats = team is null
            ? DisplayedPitchStats.Empty
            : GetDisplayedPitchStats(CreatePlayerId(team, player));
        var cardStatusBadges = team is null
            ? PlayerCardStatusBadgeHelper.Create(player)
            : PlayerCardStatusBadgeHelper.Create(displayedStats.YellowCards, displayedStats.RedCards);
        var nationality = PlayerNationalityDisplayService.Resolve(player);

        if (string.IsNullOrWhiteSpace(exactPosition))
        {
            exactPosition = PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition);
        }

        if (string.IsNullOrWhiteSpace(exactPosition))
        {
            exactPosition = GetPositionText(player.Position);
        }

        return new SubstitutionPlayerCard
        {
            Player = player,
            Name = player.Name,
            DisplayName = isPendingSubIn ? $"{player.Name} ↑" : player.Name,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            Position = exactPosition,
            OverallText = $"OVR {GetOverallRating(player)}",
            GrowthText = PlayerGrowthDisplayHelper.CreateGrowthText(player),
            Stamina = stamina,
            StaminaBrush = GetStaminaBrush(stamina),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
            CardBackground = isPendingSubIn ? GetThemedStatusBackground("positive") : teamColors.PrimaryColor,
            CardBorderBrush = isPendingSubIn ? "#34A853" : teamColors.BorderColor,
            NameForeground = isPendingSubIn ? GetThemedStatusForeground("positive") : teamColors.TextColor,
            TextForeground = isPendingSubIn ? GetThemedStatusForeground("positive") : teamColors.TextColor,
            PositionBackground = isPendingSubIn ? GetThemedStatusBackground("positive") : teamColors.SecondaryColor,
            PositionForeground = isPendingSubIn ? GetThemedStatusForeground("positive") : TeamColorService.GetReadableTextColor(teamColors.SecondaryColor),
            PendingPlayerOutName = pendingSubstitution?.PlayerOut.Name ?? string.Empty,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
            CardStatusBadges = cardStatusBadges,
            PendingSubstitution = pendingSubstitution
        };
    }

    private static int GetOverallRating(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
    }

    private static string GetPositionText(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "DEF",
            Position.Midfielder => "MID",
            Position.Forward => "FWD",
            _ => position.ToString()
        };
    }

    private static StatusBadge GetPlayerStatus(int staminaPercentage, DisplayedPitchStats displayedStats)
    {
        if (displayedStats.RedCards > 0)
        {
            return new StatusBadge("Red Card", GetThemedStatusBackground("danger"), GetThemedStatusForeground("danger"));
        }

        if (displayedStats.Injuries > 0)
        {
            return new StatusBadge("Injured", GetThemedStatusBackground("danger"), GetThemedStatusForeground("danger"));
        }

        if (displayedStats.YellowCards > 0)
        {
            return new StatusBadge("Carded", GetThemedStatusBackground("warning"), GetThemedStatusForeground("warning"));
        }

        return staminaPercentage switch
        {
            >= 75 => new StatusBadge("High Stamina", GetThemedStatusBackground("positive"), GetThemedStatusForeground("positive")),
            >= 50 => new StatusBadge("Moderate Stamina", GetThemedStatusBackground("warning"), GetThemedStatusForeground("warning")),
            >= 25 => new StatusBadge("Low Stamina", GetThemedStatusBackground("chance"), GetThemedStatusForeground("chance")),
            _ => new StatusBadge("Critical Stamina", GetThemedStatusBackground("danger"), GetThemedStatusForeground("danger"))
        };
    }

    private static string GetThemedStatusBackground(string statusType)
    {
        if (ThemeManager.CurrentTheme == AppTheme.Dark)
        {
            return statusType switch
            {
                "positive" => "#12351F",
                "warning" => "#332B10",
                "chance" => "#3A2411",
                _ => "#3B1115"
            };
        }

        return statusType switch
        {
            "positive" => "#D9F1E1",
            "warning" => "#FFF0A3",
            "chance" => "#FFE4BF",
            _ => "#FFD1D1"
        };
    }

    private static string GetThemedStatusForeground(string statusType)
    {
        if (ThemeManager.CurrentTheme == AppTheme.Dark)
        {
            return statusType switch
            {
                "positive" => "#86EFAC",
                "warning" => "#FDE68A",
                "chance" => "#FDBA74",
                _ => "#FCA5A5"
            };
        }

        return statusType switch
        {
            "positive" => "#236B39",
            "warning" => "#5F4500",
            "chance" => "#8A4E00",
            _ => "#8F1F1F"
        };
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is T typedChild)
            {
                return typedChild;
            }

            var match = FindVisualChild<T>(child);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private sealed record FeedEventStyle(
        string Icon,
        string Label,
        string RowBackground,
        string RowBorderBrush,
        string IconBackground,
        string LabelBackground,
        string LabelForeground,
        string IconForeground = "#14233A",
        string MinuteForeground = "#14233A",
        string TitleForeground = "#102033",
        string DescriptionForeground = "#34465C",
        string TraitBadgeBackground = "#FFFFFF",
        string TraitBadgeBorderBrush = "#DCE5F0");

    private sealed record StatusBadge(string Text, string Background, string Foreground);

    private sealed record PlayerStatRow(string Label, string Value);

    private sealed record SelectedPlayerContext(Team Team, Player Player, PlayerMatchPerformance? Performance);

    private sealed class DisplayedPitchStats
    {
        public static readonly DisplayedPitchStats Empty = new();

        public int Goals { get; set; }
        public int Assists { get; set; }
        public int Saves { get; set; }
        public int DefensiveContributions { get; set; }
        public int DefensiveErrors { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
        public int Injuries { get; set; }
    }

    private enum LiveMatchStatus
    {
        Neutral,
        Attacking,
        Defending,
        Turnover,
        SetPiece
    }

    private sealed class SubstitutionPlayerCard
    {
        public Player Player { get; init; } = new();
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string FlagImagePath { get; init; } = "/Assets/Flags/default.png";
        public string NationalityName { get; init; } = "Unknown nationality";
        public string ShirtNumberText { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string OverallText { get; init; } = string.Empty;
        public string GrowthText { get; init; } = string.Empty;
        public double Stamina { get; init; }
        public string StaminaBrush { get; init; } = "#7CFC9A";
        public string FormBadgeText { get; init; } = string.Empty;
        public string FormBadgeBackground { get; init; } = "#E1E5EA";
        public string FormBadgeForeground { get; init; } = "#465364";
        public string CardBackground { get; init; } = "White";
        public string CardBorderBrush { get; init; } = "#D6DFEA";
        public string NameForeground { get; init; } = "#102033";
        public string TextForeground { get; init; } = "#102033";
        public string PositionBackground { get; init; } = "#E7EEF8";
        public string PositionForeground { get; init; } = "#102033";
        public string PendingPlayerOutName { get; init; } = string.Empty;
        public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
        public IReadOnlyList<PlayerCardStatusBadge> CardStatusBadges { get; init; } = [];
        public PendingSubstitutionViewModel? PendingSubstitution { get; init; }
    }

    private sealed class PendingSubstitutionViewModel(Player playerIn, Player playerOut)
    {
        public Player PlayerIn { get; } = playerIn;
        public Player PlayerOut { get; } = playerOut;
        public string PlayerInName { get; set; } = GetShortPlayerName(playerIn.Name);
        public string PlayerOutName { get; set; } = GetShortPlayerName(playerOut.Name);

        private static string GetShortPlayerName(string name)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 1 ? name : parts[^1];
        }
    }
}
