using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private const int MediumBaseDelayMilliseconds = 6000;
    private const int VerySlowBaseDelayMilliseconds = 10000;
    private const int MinSpeedLevel = 0;
    private const int DefaultSpeedLevel = 1;
    private const int MaxSpeedLevel = 3;
    private const int FirstHalfEndMinute = 45;
    private const int FullTimeMinute = 90;
    private const double PlayerIconSlotWidth = 72;
    private const double PlayerIconSlotHeight = 76;
    private const int MaxPendingSubstitutions = 3;

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly bool _isSecondHalf;
    private readonly GameSessionService _gameSessionService = new();
    private readonly SquadSelectionService _squadSelectionService = new();
    private readonly MatchEventFactory _matchEventFactory = new();
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly PlayerFormStatusService _playerFormStatusService = new();
    private readonly ObservableCollection<MatchFeedItem> _visibleEvents = [];
    private readonly ObservableCollection<LivePlayerIconViewModel> _pitchPlayers = [];
    private readonly ObservableCollection<PendingSubstitutionViewModel> _pendingSubstitutions = [];
    private readonly Dictionary<string, DisplayedPitchStats> _displayedPitchStats = [];
    private readonly List<MatchEvent> _pendingPlaybackEvents = [];

    private CancellationTokenSource? _playbackCancellation;
    private int _speedLevel = DefaultSpeedLevel;
    private bool _hasNavigated;
    private bool _isPlaybackPaused;
    private bool _isPausedForSubstitution;
    private bool _isPausedForTacticalAdjustment;
    private bool _fixtureCompleted;
    private bool _isCancellingPendingSubstitution;
    private Player? _selectedStarterForSubstitution;
    private Player? _selectedBenchForSubstitution;
    private Player? _mandatoryInjurySubstitutionPlayer;
    private Team? _currentPossessionTeam;
    private LiveMatchStatus _currentLiveStatus = LiveMatchStatus.Neutral;
    private string? _selectedPitchPlayerKey;
    private double _pitchWidth;
    private double _pitchHeight;

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
        await ResumePlaybackAsync();
    }

    private void MatchLiveView_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelPlayback();
    }

    private bool InitializeLiveMatchContext()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return false;
        }

        if (!_isSecondHalf)
        {
            _state.CurrentMatch ??= _gameSessionService.CreateSelectedTeamLiveMatch(_state.League, _state.SelectedTeam);
        }
        else if (_state.CurrentMatch is null)
        {
            return false;
        }

        SetScoreboardTeams(_state.CurrentMatch!.HomeTeam, _state.CurrentMatch.AwayTeam);
        SetScore(_state.CurrentMatch.HomeScore, _state.CurrentMatch.AwayScore);
        PhaseTextBlock.Text = CreatePhaseLabel(_state.CurrentMatch.CurrentMinute);
        UpdateLiveStatusVisuals(LiveMatchStatus.Neutral, attackingTeam: null, defendingTeam: null);
        InitializeDisplayedPitchStats();
        LoadPausedActionPanel();
        return true;
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
        TacticalMentalityComboBox.ItemsSource = Enum.GetValues<Mentality>();
        ActionMentalityComboBox.ItemsSource = Enum.GetValues<Mentality>();
    }

    private void PrepareContinueButton()
    {
        ContinueButton.Content = _isSecondHalf ? "View Result" : "Continue";
        ContinueButton.Visibility = Visibility.Collapsed;
        ContinueButton.IsEnabled = false;
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
                var includeFulltime = _isSecondHalf && nextMinute == FullTimeMinute;
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
    }

    private async Task PlayMatchEventAsync(MatchEvent matchEvent, CancellationToken cancellationToken)
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        var feedItem = CreateFeedItem(matchEvent, _state.CurrentMatch);
        InsertFeedItemAtTop(feedItem);
        RecordDisplayedPitchStats(matchEvent);
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
            _fixtureCompleted = true;
        }

        ContinueButton.Visibility = Visibility.Visible;
        ContinueButton.IsEnabled = true;
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
                var includeFulltime = _isSecondHalf && nextMinute == FullTimeMinute;
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
        ActionMentalityComboBox.SelectedItem = userTeam.Tactics.Mentality;
        ActionPressingSlider.Value = userTeam.Tactics.PressingIntensity;
        ActionWidthSlider.Value = userTeam.Tactics.Width;
        ActionTempoSlider.Value = userTeam.Tactics.Tempo;
        ActionDefensiveLineSlider.Value = userTeam.Tactics.DefensiveLine;
        RefreshPausedSubstitutionViews();
        PausedActionStatusTextBlock.Text = HasMandatoryInjurySubstitution()
            ? $"{_mandatoryInjurySubstitutionPlayer!.Name} cannot continue. Select a substitute to replace him."
            : HasPendingSubstitution()
            ? "Queued substitutions confirm on resume."
            : _selectedPitchPlayerKey is null
                ? "Select a player on the pitch before choosing a substitute."
                : "Tactical and substitution changes will apply from the next played minute.";
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
        _selectedPitchPlayerKey = CreatePlayerKey(_state.SelectedTeam.Name, injuredPlayer.Name);
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

        if (ActionMentalityComboBox.SelectedItem is Mentality mentality)
        {
            userTeam.Tactics.Mentality = mentality;
        }

        userTeam.Tactics.PressingIntensity = (int)Math.Round(ActionPressingSlider.Value);
        userTeam.Tactics.Width = (int)Math.Round(ActionWidthSlider.Value);
        userTeam.Tactics.Tempo = (int)Math.Round(ActionTempoSlider.Value);
        userTeam.Tactics.DefensiveLine = (int)Math.Round(ActionDefensiveLineSlider.Value);
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
            $"{_state.CurrentMatch.CurrentMinute}' | {usedSubstitutions}/5 substitutions used. Choose one starter and one substitute.";

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
        TacticalOverlayStatusTextBlock.Text = $"{_state.CurrentMatch.CurrentMinute}' | Adjust tactics for the next phase of play.";
        TacticalMentalityComboBox.SelectedItem = userTeam.Tactics.Mentality;
        TacticalPressingSlider.Value = userTeam.Tactics.PressingIntensity;
        TacticalWidthSlider.Value = userTeam.Tactics.Width;
        TacticalTempoSlider.Value = userTeam.Tactics.Tempo;
        TacticalDefensiveLineSlider.Value = userTeam.Tactics.DefensiveLine;

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

        if (TacticalMentalityComboBox.SelectedItem is Mentality mentality)
        {
            userTeam.Tactics.Mentality = mentality;
        }

        userTeam.Tactics.PressingIntensity = (int)Math.Round(TacticalPressingSlider.Value);
        userTeam.Tactics.Width = (int)Math.Round(TacticalWidthSlider.Value);
        userTeam.Tactics.Tempo = (int)Math.Round(TacticalTempoSlider.Value);
        userTeam.Tactics.DefensiveLine = (int)Math.Round(TacticalDefensiveLineSlider.Value);

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

        _selectedPitchPlayerKey = CreatePlayerKey(userTeam.Name, draggedPlayer.Name);
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
        return true;
    }

    private void ConfirmSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentMatch is null || _selectedStarterForSubstitution is null || _selectedBenchForSubstitution is null)
        {
            return;
        }

        _selectedPitchPlayerKey = CreatePlayerKey(GetUserTeam().Name, _selectedStarterForSubstitution.Name);
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
            string.Equals(CreatePlayerKey(userTeam.Name, player.Name), _selectedPitchPlayerKey, StringComparison.OrdinalIgnoreCase));

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
            : "Queued substitutions confirm on resume.";
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

            var substitutionEvent = _matchEventFactory.CreateSubstitution(
                minute,
                userTeam,
                starter,
                substitute);
            _state.CurrentMatch.Events.Add(substitutionEvent);
            InsertFeedItemAtTop(CreateFeedItem(substitutionEvent, _state.CurrentMatch));
            UpdateLiveStatusFromEvent(substitutionEvent);

            _selectedPitchPlayerKey = CreatePlayerKey(userTeam.Name, substitute.Name);
        }

        PausedBenchListBox.ItemsSource = CreateSubstitutionPlayerCards(userTeam.Substitutes.Where(IsAvailableSubstitute), showPendingState: true, userTeam);
        PausedActionStatusTextBlock.Text = $"{validatedSubstitutions.Count} substitution(s) confirmed.";
        ClearPendingSubstitutions();
        RefreshPlayerPanels();
        return true;
    }

    private bool HasPendingSubstitution()
    {
        return _pendingSubstitutions.Count > 0;
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

        return availableSubstitutes.Where(substitute => CanPlayerCoverPosition(substitute, selectedPosition));
    }

    private Player? GetSelectedActiveUserPlayer(Team userTeam)
    {
        if (string.IsNullOrWhiteSpace(_selectedPitchPlayerKey))
        {
            return null;
        }

        return GetActivePitchPlayers(userTeam).FirstOrDefault(player =>
            string.Equals(CreatePlayerKey(userTeam.Name, player.Name), _selectedPitchPlayerKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanPlayerCoverPosition(Player player, string exactPosition)
    {
        var normalizedPosition = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        if (string.IsNullOrWhiteSpace(normalizedPosition))
        {
            return false;
        }

        PositionSuitabilityService.EnsurePositionMetadata(player);
        return string.Equals(player.PreferredPosition, normalizedPosition, StringComparison.OrdinalIgnoreCase) ||
            player.SecondaryPositions.Any(position =>
                string.Equals(PositionSuitabilityService.NormalizeExactPosition(position), normalizedPosition, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAvailableSubstitute(Player player)
    {
        return !player.IsSentOff && !player.IsSuspended && !player.IsInjured;
    }

    private void ApplyManualSubstitutionImpact(Player playerOff, Player playerOn)
    {
        playerOff.LiveMatchModifier = 1.0;

        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(playerOn);
        if (suitability < 1.0)
        {
            playerOn.LiveMatchModifier = Math.Clamp(0.92 * suitability, 0.75, 1.15);
            GetOrCreateLivePerformance(GetUserTeam(), playerOn).Rating -= 0.15;
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
        var attackingBonus = team.Tactics.Mentality == Mentality.Attacking ? 0.07 : 0.0;
        var defensiveBonus = team.Tactics.Mentality == Mentality.Defensive ? 0.05 : 0.0;
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
        ActionLockedTextBlock.Visibility = _isPlaybackPaused ? Visibility.Collapsed : Visibility.Visible;
        PausedActionPanel.Visibility = Visibility.Visible;
        PausedActionPanel.IsEnabled = _isPlaybackPaused;
        PausedActionPanel.Opacity = _isPlaybackPaused ? 1.0 : 0.48;
        PausedBenchListBox.IsEnabled = _isPlaybackPaused && substitutionsLeft > 0;
        SubsLeftTextBlock.Text = $"Subs left: {Math.Max(0, substitutionsLeft)}";
        PausedSubstitutionPanelBorder.BorderBrush = ToBrush(isMandatoryInjurySubPending ? "#D92D20" : "Transparent");
        PausedSubstitutionPanelBorder.Background = ToBrush(isMandatoryInjurySubPending ? "#FFF5F5" : "White");
    }

    private void UpdateSpeedControls()
    {
        SpeedLevelTextBlock.Text = GetSpeedLevelText(_speedLevel);
        DecreaseSpeedButton.IsEnabled = _speedLevel > MinSpeedLevel;
        IncreaseSpeedButton.IsEnabled = _speedLevel < MaxSpeedLevel;
    }

    private void SaveSpeedLevelToState()
    {
        _state.CurrentMatchSpeed = _speedLevel switch
        {
            0 => MatchSpeed.VerySlow,
            1 => MatchSpeed.Medium,
            2 => MatchSpeed.Fast,
            3 => MatchSpeed.VeryFast,
            _ => MatchSpeed.Medium
        };
    }

    private static int GetSpeedLevelFromState(MatchSpeed speed)
    {
        return speed switch
        {
            MatchSpeed.VerySlow or MatchSpeed.Slow => 0,
            MatchSpeed.Medium => 1,
            MatchSpeed.Fast => 2,
            MatchSpeed.VeryFast => 3,
            _ => DefaultSpeedLevel
        };
    }

    private static string GetSpeedLevelText(int speedLevel)
    {
        return speedLevel switch
        {
            0 => "0.5x",
            1 => "1x",
            2 => "2x",
            3 => "3x",
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
        if (_state.CurrentMatch is null)
        {
            return;
        }

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

            case EventType.Penalty:
                if (matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase))
                {
                    IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Goals++);
                }
                break;

            case EventType.Save:
                IncrementDisplayedPlayerStat(matchEvent.SecondaryPlayerName, stats => stats.Saves++);
                break;

            case EventType.GoalkeeperHeroics:
                IncrementDisplayedPlayerStat(matchEvent.PrimaryPlayerName, stats => stats.Saves++);
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

        var team = ResolvePlayerTeam(playerName);
        if (team is null)
        {
            return;
        }

        var playerKey = CreatePlayerKey(team.Name, playerName);
        if (!_displayedPitchStats.TryGetValue(playerKey, out var stats))
        {
            stats = new DisplayedPitchStats();
            _displayedPitchStats[playerKey] = stats;
        }

        update(stats);
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

        for (var index = 0; index < players.Count && index < formationPositions.Count; index++)
        {
            yield return CreatePitchIcon(team, players[index], formationPositions[index], isHomeTeam, pitchWidth, pitchHeight);
        }
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
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(player);
        var displayRating = (performance?.Rating ?? 6.0) * suitability;
        var stamina = GetStaminaPercentage(player);
        var (xRatio, yRatio) = GetLivePitchPosition(formationPosition, isHomeTeam);
        var x = Math.Clamp((pitchWidth * xRatio) - (PlayerIconSlotWidth / 2), 4, Math.Max(4, pitchWidth - PlayerIconSlotWidth - 4));
        var y = Math.Clamp((pitchHeight * yRatio) - (PlayerIconSlotHeight / 2), 4, Math.Max(4, pitchHeight - PlayerIconSlotHeight - 4));
        var playerKey = CreatePlayerKey(team.Name, player.Name);
        var displayedStats = GetDisplayedPitchStats(playerKey);
        var status = GetPlayerStatus(stamina, displayedStats);
        var yellowCards = displayedStats.YellowCards;
        var redCards = displayedStats.RedCards;
        var isSelected = string.Equals(_selectedPitchPlayerKey, playerKey, StringComparison.OrdinalIgnoreCase);
        var teamColors = TeamColorService.GetPalette(team);

        return new LivePlayerIconViewModel
        {
            Name = player.Name,
            TeamName = team.Name,
            PlayerKey = playerKey,
            ShirtNumberText = player.SquadNumber > 0 ? player.SquadNumber.ToString() : string.Empty,
            Initials = GetInitials(player.Name),
            PositionText = player.AssignedPosition,
            ExactPosition = formationPosition.ExactPosition,
            TeamSide = isHomeTeam ? "Home" : "Away",
            X = x,
            Y = y,
            IconBrush = teamColors.PrimaryColor,
            IconForeground = teamColors.TextColor,
            BorderBrush = teamColors.BorderColor,
            SelectionBrush = isSelected ? teamColors.SelectedGlowColor : "Transparent",
            SelectionThickness = isSelected ? 4 : 0,
            RatingText = displayRating.ToString("0.0"),
            Stamina = stamina,
            StaminaBrush = GetStaminaBrush(stamina),
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
            DetailText = BuildPlayerDetailText(player, performance, stamina, displayedStats.DefensiveContributions),
            CardsText = yellowCards == 0 && redCards == 0 ? "None" : $"Y{yellowCards} R{redCards}",
            InjuryStatusText = displayedStats.Injuries > 0 ? "Injured" : "Fit",
            FormText = PlayerFormStatusService.ToDisplayText(player.FormStatus),
            StaminaText = $"{Math.Round(player.Stamina):0}%",
            MatchStatusText = status.Text,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits)
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

        var playerContext = FindSelectedPlayerContext(selectedPlayer);
        var player = playerContext?.Player;
        var performance = playerContext?.Performance;
        var team = playerContext?.Team;
        var rating = GetSelectedPlayerRating(selectedPlayer, performance);
        var passAccuracy = GetEstimatedPassAccuracy(team, rating, selectedPlayer.Stamina);

        if (player?.Position == Position.Goalkeeper)
        {
            SetSelectedPlayerStatRows(
                [
                    new("Rating", selectedPlayer.RatingText),
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
                    new("Rating", selectedPlayer.RatingText),
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
            .FirstOrDefault(candidate => string.Equals(candidate.Name, selectedPlayer.Name, StringComparison.OrdinalIgnoreCase));
        var performance = _state.CurrentMatch.PlayerPerformances
            .FirstOrDefault(existing => existing.TeamName == selectedPlayer.TeamName &&
                existing.PlayerName == selectedPlayer.Name);

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

    private double GetSelectedPlayerRating(LivePlayerIconViewModel selectedPlayer, PlayerMatchPerformance? performance)
    {
        if (performance is not null)
        {
            return performance.Rating;
        }

        return double.TryParse(selectedPlayer.RatingText, out var rating) ? rating : 6.0;
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

    private static string StopIcon() => char.ConvertFromUtf32(0x1F6D1);

    private static string StarIcon() => char.ConvertFromUtf32(0x2B50);

    private static string GoalNetIcon() => char.ConvertFromUtf32(0x1F945);

    private static string WarningIcon() => char.ConvertFromUtf32(0x26A0);

    private static string FireIcon() => char.ConvertFromUtf32(0x1F525);

    private static string ThumbsUpIcon() => char.ConvertFromUtf32(0x1F44D);

    private static string NeutralFaceIcon() => char.ConvertFromUtf32(0x1F610);

    private static string SnowflakeIcon() => char.ConvertFromUtf32(0x2744);

    private static string MegaphoneIcon() => char.ConvertFromUtf32(0x1F4E3);

    private static string BatteryIcon() => char.ConvertFromUtf32(0x1F50B);

    private static string RotateIcon() => char.ConvertFromUtf32(0x1F504);

    private static string PauseIcon() => char.ConvertFromUtf32(0x23F8);

    private static string CheckeredFlagIcon() => char.ConvertFromUtf32(0x1F3C1);

    private static string BulletIcon() => char.ConvertFromUtf32(0x2022);

    private static string BuildPlayerDetailText(
        Player player,
        PlayerMatchPerformance? performance,
        int stamina,
        int defensiveContributions)
    {
        var yellowCards = Math.Max(player.YellowCards, performance?.YellowCards ?? 0);
        var redCards = Math.Max(player.IsSentOff ? 1 : 0, performance?.RedCards ?? 0);
        var injuryText = player.IsInjured || performance?.Injuries > 0 ? "Injured" : "Fit";

        return $"{player.Name}\n" +
            $"Position: {GetPositionText(player.Position)}\n" +
            $"Rating: {(performance?.Rating ?? 6.0):0.0}\n" +
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

    private static string CreatePlayerKey(string teamName, string playerName)
    {
        return $"{teamName}|{playerName}";
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
        return _isSecondHalf ? FullTimeMinute : FirstHalfEndMinute;
    }

    private string CreatePhaseLabel(int currentMinute)
    {
        var phaseText = _isSecondHalf ? "Second Half" : "First Half";
        return $"{phaseText} - {currentMinute}'";
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
        var colors = TeamColorService.GetPalette(team);
        panel.Background = CreateBrush(colors.PrimaryColor);
        panel.BorderBrush = CreateBrush(isPossessionTeam ? colors.SelectedGlowColor : colors.BorderColor);
        panel.BorderThickness = isPossessionTeam ? new Thickness(3) : new Thickness(1);
        teamNameTextBlock.Foreground = CreateBrush(colors.TextColor);
        scoreTextBlock.Foreground = CreateBrush(colors.TextColor);
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
            case EventType.Shot:
            case EventType.Miss:
            case EventType.Goal:
            case EventType.WonderGoal:
            case EventType.CrowdMomentum:
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
            FlashScorePanel(scoringPanel, TeamColorService.GetPalette(scoringTeam).PrimaryColor);
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
        if (matchEvent.EventType is EventType.Halftime or EventType.Fulltime)
        {
            return string.Empty;
        }

        var primaryPlayerTeam = FindPlayerTeamName(matchEvent.PrimaryPlayerName, match);
        var secondaryPlayerTeam = FindPlayerTeamName(matchEvent.SecondaryPlayerName, match);

        switch (matchEvent.EventType)
        {
            case EventType.Turnover:
            case EventType.Attack:
            case EventType.Shot:
            case EventType.Miss:
            case EventType.Goal:
            case EventType.WonderGoal:
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
            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
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
            EventType.Kickoff => "Kickoff underway",
            EventType.Attack => CreateAttackHeadline(matchEvent, teamName),
            EventType.Shot => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "creates chance", $"{teamName} create chance"),
            EventType.Save => CreateSaveHeadline(matchEvent),
            EventType.Goal => IsPenaltyResult(matchEvent)
                ? CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores penalty", $"{teamName} score penalty")
                : CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores", $"GOAL for {teamName}"),
            EventType.Foul => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "commits foul", "Foul given"),
            EventType.YellowCard => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "booked", "Yellow card shown"),
            EventType.RedCard => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "sent off", "Red card shown"),
            EventType.Injury => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "goes down injured", "Injury concern"),
            EventType.PenaltyDecision => "Penalty awarded",
            EventType.PenaltyTaker => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "steps up", $"{teamName} penalty taker"),
            EventType.Penalty => CreatePenaltyHeadline(matchEvent, teamName),
            EventType.Offside => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "caught offside", $"{teamName} offside"),
            EventType.BadPass => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misplaces pass", "Bad pass"),
            EventType.Miscontrol => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "miscontrols", "Miscontrol"),
            EventType.Tackle => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "wins tackle", "Tackle won"),
            EventType.Interception => CreateInterceptionHeadline(matchEvent),
            EventType.Pressure => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "forces pressure", "Pressure forces error"),
            EventType.BlockedPass => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "blocks pass", "Blocked pass"),
            EventType.Turnover => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "gets the ball", $"{teamName} regain possession"),
            EventType.DefensiveStop => CreateDefensiveHeadline(matchEvent),
            EventType.DefensiveError => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "makes defensive error", $"{teamName} make error"),
            EventType.WonderGoal => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "scores wonder goal", $"Wonder goal for {teamName}"),
            EventType.GoalkeeperHeroics => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "keeps them alive", "Goalkeeper heroics"),
            EventType.CornerKick => $"{teamName} win corner",
            EventType.SetPieceDanger => $"{teamName} threaten from set piece",
            EventType.Confrontation => "Players clash",
            EventType.CrowdMomentum => $"{teamName} gain momentum",
            EventType.Exhaustion => CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "looks exhausted", "Stamina dropping"),
            EventType.Substitution => "Substitution",
            EventType.Halftime => "Halftime",
            EventType.Fulltime => "Fulltime",
            EventType.Miss => IsPenaltyResult(matchEvent)
                ? CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misses penalty", $"{teamName} miss penalty")
                : CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "misses chance", $"{teamName} miss chance"),
            _ => "Match event"
        };

        return LimitHeadlineWords(headline, 8);
    }

    private static string CreateEventDescription(MatchEvent matchEvent)
    {
        var suffix = matchEvent.EventType switch
        {
            EventType.Goal => "Crowd erupts.",
            EventType.Save => IsPenaltyResult(matchEvent) ? string.Empty : "Huge stop.",
            EventType.Miss => IsPenaltyResult(matchEvent) ? string.Empty : "Shot Off Target.",
            EventType.Foul => "Play is stopped.",
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
            EventType.Exhaustion => "Fatigue is showing.",
            EventType.Substitution => "Fresh legs arrive.",
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

    private static string CreateAttackHeadline(MatchEvent matchEvent, string teamName)
    {
        var description = matchEvent.Description;
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

    private static string CreateDefensiveHeadline(MatchEvent matchEvent)
    {
        var description = matchEvent.Description;
        if (description.Contains("tackle", StringComparison.OrdinalIgnoreCase))
        {
            return CreatePlayerHeadline(matchEvent.PrimaryPlayerName, "wins the tackle", "Big defensive tackle");
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

    private static string CreatePlayerHeadline(string? playerName, string action, string fallback)
    {
        var shortName = GetHeadlinePlayerName(playerName);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return fallback;
        }

        return $"{shortName} {action}";
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

    private static FeedEventStyle GetEventStyle(MatchEvent matchEvent)
    {
        if (matchEvent.EventType == EventType.Miss && IsPenaltyResult(matchEvent))
        {
            return MissStyle(WarningIcon(), "MISSED PENALTY");
        }

        return matchEvent.EventType switch
        {
            EventType.Kickoff => NeutralStyle(FlagIcon(), "KICKOFF"),
            EventType.Attack => AttackStyle(SwordsIcon(), "ATTACK"),
            EventType.Shot => ChanceStyle(TargetIcon(), "SHOT"),
            EventType.Save => SaveStyle(GloveIcon(), "SAVE"),
            EventType.Foul => FoulStyle(StopIcon(), "FOUL"),
            EventType.Miss => MissStyle(WarningIcon(), "MISS"),
            EventType.Goal => GoalStyle(SoccerBallIcon(), "GOAL"),
            EventType.YellowCard => YellowCardStyle(YellowCardIcon(), "YELLOW CARD"),
            EventType.RedCard => RedCardStyle(RedCardIcon(), "RED CARD"),
            EventType.Injury => FoulStyle(InjuryIcon(), "INJURY"),
            EventType.PenaltyDecision => ChanceStyle(GoalNetIcon(), "PENALTY DECISION"),
            EventType.PenaltyTaker => ChanceStyle(TargetIcon(), "PENALTY TAKER"),
            EventType.Penalty => ChanceStyle(GoalNetIcon(), "PENALTY"),
            EventType.Offside => TurnoverStyle(FlagIcon(), "OFFSIDE"),
            EventType.BadPass => TurnoverStyle(WarningIcon(), "BAD PASS"),
            EventType.Miscontrol => TurnoverStyle(WarningIcon(), "MISCONTROL"),
            EventType.Tackle => DefensiveStyle(ShieldIcon(), "TACKLE"),
            EventType.Interception => DefensiveStyle(ShieldIcon(), "INTERCEPTION"),
            EventType.Pressure => DefensiveStyle(ShieldIcon(), "PRESSURE"),
            EventType.BlockedPass => DefensiveStyle(ShieldIcon(), "BLOCKED PASS"),
            EventType.Turnover => TurnoverStyle(RotateIcon(), "TURNOVER"),
            EventType.DefensiveStop => DefensiveStyle(ShieldIcon(), "DEFENSE"),
            EventType.DefensiveError => DefensiveStyle(WarningIcon(), "DEFENSIVE ERROR"),
            EventType.WonderGoal => GoalStyle(StarIcon(), "WONDER GOAL"),
            EventType.GoalkeeperHeroics => SaveStyle(GloveIcon(), "KEEPER HEROICS"),
            EventType.CornerKick => ChanceStyle(FlagIcon(), "CORNER KICK"),
            EventType.SetPieceDanger => ChanceStyle(TargetIcon(), "SET PIECE"),
            EventType.Confrontation => RedCardStyle(FireIcon(), "CONFRONTATION"),
            EventType.CrowdMomentum => AttackStyle(MegaphoneIcon(), "CROWD MOMENTUM"),
            EventType.Exhaustion => MissStyle(BatteryIcon(), "EXHAUSTION"),
            EventType.Substitution => NeutralStyle(RotateIcon(), "SUBSTITUTION"),
            EventType.Halftime => NeutralStyle(PauseIcon(), "HALFTIME"),
            EventType.Fulltime => NeutralStyle(CheckeredFlagIcon(), "FULLTIME"),
            _ => NeutralStyle(BulletIcon(), "EVENT")
        };
    }

    private static FeedEventStyle AttackStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#FEF2F2", "#EF4444", "#FEE2E2", "#FEE2E2", "#B91C1C", IconForeground: "#B91C1C", TitleForeground: "#071A2E", DescriptionForeground: "#34465C");
    }

    private static FeedEventStyle DefensiveStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#EFF6FF", "#3B82F6", "#DBEAFE", "#DBEAFE", "#1D4ED8", IconForeground: "#1D4ED8", TitleForeground: "#071A2E", DescriptionForeground: "#34465C");
    }

    private static FeedEventStyle TurnoverStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#F5F3FF", "#8B5CF6", "#EDE9FE", "#EDE9FE", "#6D28D9", IconForeground: "#6D28D9", TitleForeground: "#071A2E", DescriptionForeground: "#34465C");
    }

    private static FeedEventStyle ChanceStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#FFF7ED", "#F97316", "#FFEDD5", "#FFEDD5", "#C2410C", IconForeground: "#C2410C", TitleForeground: "#071A2E", DescriptionForeground: "#34465C");
    }

    private static FeedEventStyle FoulStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#FEFCE8", "#EAB308", "#FEF9C3", "#FEF9C3", "#854D0E", IconForeground: "#854D0E", TitleForeground: "#071A2E", DescriptionForeground: "#34465C");
    }

    private static FeedEventStyle MissStyle(string icon, string label)
    {
        return new FeedEventStyle(icon, label, "#F3F4F6", "#6B7280", "#E5E7EB", "#E5E7EB", "#374151", IconForeground: "#374151", TitleForeground: "#071A2E", DescriptionForeground: "#34465C");
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
        return new FeedEventStyle(icon, label, "#FFFFFF", "#CBD5E1", "#F1F5F9", "#E2E8F0", "#334155", IconForeground: "#334155", TitleForeground: "#102033", DescriptionForeground: "#34465C");
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
            or EventType.DefensiveStop
            or EventType.DefensiveError
            or EventType.WonderGoal
            or EventType.GoalkeeperHeroics
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Confrontation
            or EventType.CrowdMomentum
            or EventType.Exhaustion
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
            or EventType.DefensiveStop
            or EventType.DefensiveError
            or EventType.GoalkeeperHeroics
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Confrontation
            or EventType.Exhaustion
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
            2 => FastBaseDelayMilliseconds,
            3 => 700,
            _ => MediumBaseDelayMilliseconds
        };
    }

    private static double GetEventDelayMultiplier(int speedLevel)
    {
        return speedLevel switch
        {
            0 => 1.0,
            1 => 0.75,
            2 => 0.45,
            3 => 0.15,
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
            nameof(EventType.Halftime) or nameof(EventType.Fulltime) => 1500,
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
            < 20 => "#EF4444",
            < 35 => "#FB923C",
            < 60 => "#FACC15",
            _ => "#7CFC9A"
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

    private static SubstitutionPlayerCard CreateSubstitutionPlayerCard(Player player, PendingSubstitutionViewModel? pendingSubstitution, Team? team)
    {
        var stamina = GetStaminaPercentage(player);
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var exactPosition = PositionSuitabilityService.NormalizeExactPosition(player.AssignedPosition);
        var isPendingSubIn = pendingSubstitution is not null;
        var teamColors = TeamColorService.GetPalette(team);

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
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            Position = exactPosition,
            OverallText = $"OVR {GetOverallRating(player)}",
            GrowthText = PlayerGrowthDisplayHelper.CreateGrowthText(player),
            Stamina = stamina,
            StaminaBrush = GetStaminaBrush(stamina),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
            CardBackground = isPendingSubIn ? "#ECFDF3" : teamColors.PrimaryColor,
            CardBorderBrush = isPendingSubIn ? "#34A853" : teamColors.BorderColor,
            NameForeground = isPendingSubIn ? "#137333" : teamColors.TextColor,
            TextForeground = isPendingSubIn ? "#137333" : teamColors.TextColor,
            PositionBackground = isPendingSubIn ? "#D9F1E1" : teamColors.SecondaryColor,
            PositionForeground = TeamColorService.GetReadableTextColor(isPendingSubIn ? "#D9F1E1" : teamColors.SecondaryColor),
            PendingPlayerOutName = pendingSubstitution?.PlayerOut.Name ?? string.Empty,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
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
            return new StatusBadge("Red Card", "#FFD1D1", "#8F1F1F");
        }

        if (displayedStats.Injuries > 0)
        {
            return new StatusBadge("Injured", "#FFE6E6", "#8F1F1F");
        }

        if (displayedStats.YellowCards > 0)
        {
            return new StatusBadge("Carded", "#FFF0A3", "#5F4500");
        }

        return staminaPercentage switch
        {
            >= 75 => new StatusBadge("High Stamina", "#D9F1E1", "#236B39"),
            >= 50 => new StatusBadge("Moderate Stamina", "#FFF0A3", "#5F4500"),
            >= 25 => new StatusBadge("Low Stamina", "#FFE4BF", "#8A4E00"),
            _ => new StatusBadge("Critical Stamina", "#FFD1D1", "#8F1F1F")
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
