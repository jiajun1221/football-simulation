using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;
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
    private const double PlayerIconSlotSize = 74;
    private const double LiveIconFatigueBarWidth = 42;

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly bool _isSecondHalf;
    private readonly GameSessionService _gameSessionService = new();
    private readonly SquadSelectionService _squadSelectionService = new();
    private readonly MatchEventFactory _matchEventFactory = new();
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly ObservableCollection<MatchFeedItem> _visibleEvents = [];
    private readonly ObservableCollection<LivePlayerIconViewModel> _pitchPlayers = [];

    private CancellationTokenSource? _playbackCancellation;
    private int _speedLevel = DefaultSpeedLevel;
    private bool _hasNavigated;
    private bool _isPlaybackPaused;
    private bool _isPausedForSubstitution;
    private bool _isPausedForTacticalAdjustment;
    private bool _fixtureCompleted;
    private Player? _selectedStarterForSubstitution;
    private Player? _selectedBenchForSubstitution;
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

        if (_state.CurrentMatch.CurrentMinute >= GetPhaseEndMinute())
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
            while (_state.CurrentMatch.CurrentMinute < GetPhaseEndMinute())
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                var newEvents = _state.CurrentMatch.Events
                    .Skip(existingEventCount)
                    .OrderBy(matchEvent => matchEvent.Minute)
                    .ToList();

                foreach (var matchEvent in newEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var feedItem = CreateFeedItem(matchEvent, _state.CurrentMatch);
                    InsertFeedItemAtTop(feedItem);

                    await Task.Delay(GetRevealDelayMilliseconds(feedItem), cancellationToken);

                    if (matchEvent.HomeScore.HasValue && matchEvent.AwayScore.HasValue)
                    {
                        SetScore(matchEvent.HomeScore.Value, matchEvent.AwayScore.Value);
                    }

                    if (ShouldRefreshPitchAfterEvent(matchEvent.EventType))
                    {
                        RefreshPlayerPanels();
                    }

                    if (feedItem.IsGoal)
                    {
                        await PlayGoalEffectAsync(feedItem.TeamName, cancellationToken);
                    }

                    await Task.Delay(GetDelayFor(feedItem), cancellationToken);
                }

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
        ApplyInlineTacticalSettings();
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
        PausedBenchListBox.ItemsSource = userTeam.Substitutes.ToList();
        PausedActionStatusTextBlock.Text = _selectedPitchPlayerKey is null
            ? "Select a player on the pitch before choosing a substitute."
            : "Tactical and substitution changes will apply from the next played minute.";
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

        LiveStarterListBox.ItemsSource = userTeam.Players.ToList();
        LiveBenchListBox.ItemsSource = userTeam.Substitutes.ToList();

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
        _ = ResumePlaybackAsync();
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
        _selectedStarterForSubstitution = LiveStarterListBox.SelectedItem as Player;
        UpdateConfirmSubstitutionButton();
    }

    private void LiveBenchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedBenchForSubstitution = LiveBenchListBox.SelectedItem as Player;
        UpdateConfirmSubstitutionButton();
    }

    private void PausedBenchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPlaybackPaused || PausedBenchListBox.SelectedItem is not Player substitute)
        {
            return;
        }

        TryApplyPausedSubstitution(substitute);
        PausedBenchListBox.SelectedItem = null;
    }

    private void BenchPlayerCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPlaybackPaused ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: Player substitute })
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, substitute, DragDropEffects.Move);
    }

    private void PitchPlayerIcon_Drop(object sender, DragEventArgs e)
    {
        if (!_isPlaybackPaused ||
            sender is not FrameworkElement { DataContext: LivePlayerIconViewModel selectedPlayer } ||
            e.Data.GetData(typeof(Player)) is not Player substitute)
        {
            return;
        }

        _selectedPitchPlayerKey = selectedPlayer.PlayerKey;
        TryApplyPausedSubstitution(substitute);
        e.Handled = true;
    }

    private void ConfirmSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentMatch is null || _selectedStarterForSubstitution is null || _selectedBenchForSubstitution is null)
        {
            return;
        }

        var minute = Math.Max(1, _state.CurrentMatch.CurrentMinute);
        var userTeam = GetUserTeam();
        var swapResult = _squadSelectionService.SwapStarterWithSubstitute(
            userTeam,
            _selectedStarterForSubstitution,
            _selectedBenchForSubstitution,
            _state.CurrentMatch,
            minute);

        if (!swapResult.Success)
        {
            MessageBox.Show(swapResult.Message);
            return;
        }

        ApplyManualSubstitutionImpact(_selectedStarterForSubstitution, _selectedBenchForSubstitution);
        _state.CurrentMatch.SuperSubBoosts[_selectedBenchForSubstitution.Name] = minute + 10;

        var substitutionEvent = _matchEventFactory.CreateSubstitution(
            minute,
            userTeam,
            _selectedStarterForSubstitution,
            _selectedBenchForSubstitution);

        _state.CurrentMatch.Events.Add(substitutionEvent);
        InsertFeedItemAtTop(CreateFeedItem(substitutionEvent, _state.CurrentMatch));
        RefreshPlayerPanels();

        SubstitutionOverlay.Visibility = Visibility.Collapsed;
        _selectedStarterForSubstitution = null;
        _selectedBenchForSubstitution = null;
        ConfirmSubstitutionButton.IsEnabled = false;

        _ = ResumePlaybackAsync();
    }

    private void TryApplyPausedSubstitution(Player substitute)
    {
        if (_state.CurrentMatch is null || _selectedPitchPlayerKey is null)
        {
            PausedActionStatusTextBlock.Text = "Select a starter on the pitch before choosing a substitute.";
            return;
        }

        var userTeam = GetUserTeam();
        var starter = userTeam.Players.FirstOrDefault(player =>
            string.Equals(CreatePlayerKey(userTeam.Name, player.Name), _selectedPitchPlayerKey, StringComparison.OrdinalIgnoreCase));

        if (starter is null)
        {
            PausedActionStatusTextBlock.Text = "The selected player is not in your current XI.";
            return;
        }

        var minute = Math.Max(1, _state.CurrentMatch.CurrentMinute);
        var swapResult = _squadSelectionService.SwapStarterWithSubstitute(
            userTeam,
            starter,
            substitute,
            _state.CurrentMatch,
            minute);

        if (!swapResult.Success)
        {
            PausedActionStatusTextBlock.Text = swapResult.Message;
            return;
        }

        ApplyManualSubstitutionImpact(starter, substitute);
        _state.CurrentMatch.SuperSubBoosts[substitute.Name] = minute + 10;

        var substitutionEvent = _matchEventFactory.CreateSubstitution(minute, userTeam, starter, substitute);
        _state.CurrentMatch.Events.Add(substitutionEvent);
        InsertFeedItemAtTop(CreateFeedItem(substitutionEvent, _state.CurrentMatch));

        _selectedPitchPlayerKey = CreatePlayerKey(userTeam.Name, substitute.Name);
        PausedBenchListBox.ItemsSource = userTeam.Substitutes.ToList();
        PausedActionStatusTextBlock.Text = $"{substitute.Name} replaces {starter.Name}. Fresh legs get a short performance boost after resume.";
        RefreshPlayerPanels();
        UpdatePlaybackControls();
    }

    private void ApplyManualSubstitutionImpact(Player playerOff, Player playerOn)
    {
        playerOff.LiveMatchModifier = 1.0;

        if (playerOn.Position == playerOff.Position)
        {
            playerOn.LiveMatchModifier = 1.15;
            return;
        }

        playerOn.LiveMatchModifier = 1.02;
    }

    private static void ApplyTacticalLiveModifiers(Team team)
    {
        var attackingBonus = team.Tactics.Mentality == Mentality.Attacking ? 0.07 : 0.0;
        var defensiveBonus = team.Tactics.Mentality == Mentality.Defensive ? 0.05 : 0.0;
        var intensityPenalty = Math.Max(0, team.Tactics.PressingIntensity + team.Tactics.Tempo - 135) / 500.0;

        foreach (var player in team.Players)
        {
            var fatiguePenalty = player.Fatigue >= 70 ? (player.Fatigue - 70) / 200.0 : 0.0;
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
        var isMatchActive = _state.CurrentMatch is not null &&
            _state.CurrentMatch.CurrentMinute < GetPhaseEndMinute() &&
            !_hasNavigated;

        PauseResumeButton.Content = _isPlaybackPaused ? "\u25B6" : "\u23F8";
        PauseResumeButton.IsEnabled = isMatchActive && !isOverlayActive;
        DecreaseSpeedButton.IsEnabled = _speedLevel > MinSpeedLevel;
        IncreaseSpeedButton.IsEnabled = _speedLevel < MaxSpeedLevel;
        ActionLockedTextBlock.Visibility = _isPlaybackPaused ? Visibility.Collapsed : Visibility.Visible;
        PausedActionPanel.Visibility = Visibility.Visible;
        PausedActionPanel.IsEnabled = _isPlaybackPaused;
        PausedActionPanel.Opacity = _isPlaybackPaused ? 1.0 : 0.48;
        PausedBenchListBox.IsEnabled = _isPlaybackPaused && substitutionsLeft > 0;
        SubsLeftTextBlock.Text = $"Subs left: {Math.Max(0, substitutionsLeft)}";
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

    private void RefreshPitchPlayers()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

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
        var players = team.Players.ToList();

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
        var performance = _state.CurrentMatch?.PlayerPerformances
            .FirstOrDefault(existing => existing.TeamName == team.Name && existing.PlayerName == player.Name);
        var fatigue = GetFatiguePercentage(player);
        var (xRatio, yRatio) = GetLivePitchPosition(formationPosition, isHomeTeam);
        var x = Math.Clamp((pitchWidth * xRatio) - (PlayerIconSlotSize / 2), 4, Math.Max(4, pitchWidth - PlayerIconSlotSize - 4));
        var y = Math.Clamp((pitchHeight * yRatio) - (PlayerIconSlotSize / 2), 4, Math.Max(4, pitchHeight - PlayerIconSlotSize - 4));
        var defensiveContributions = GetDefensiveContributions(performance);
        var isUserTeam = _state.SelectedTeam is not null &&
            string.Equals(team.Name, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase);
        var playerKey = CreatePlayerKey(team.Name, player.Name);
        var status = GetPlayerStatus(player, fatigue);
        var yellowCards = Math.Max(player.YellowCards, performance?.YellowCards ?? 0);
        var redCards = Math.Max(player.IsSentOff ? 1 : 0, performance?.RedCards ?? 0);
        var isSelected = string.Equals(_selectedPitchPlayerKey, playerKey, StringComparison.OrdinalIgnoreCase);

        return new LivePlayerIconViewModel
        {
            Name = player.Name,
            TeamName = team.Name,
            PlayerKey = playerKey,
            ShirtNumberText = player.SquadNumber > 0 ? player.SquadNumber.ToString() : string.Empty,
            Initials = GetInitials(player.Name),
            PositionText = GetPositionText(player.Position),
            TeamSide = isHomeTeam ? "Home" : "Away",
            X = x,
            Y = y,
            IconBrush = isUserTeam ? "#246BFE" : "#EF3333",
            BorderBrush = isUserTeam ? "#DCEBFF" : "#FFE0E0",
            SelectionBrush = isSelected ? "#F7C948" : "Transparent",
            SelectionThickness = isSelected ? 4 : 0,
            RatingText = (performance?.Rating ?? 6.0).ToString("0.0"),
            FatiguePercent = fatigue,
            FatigueBarWidth = LiveIconFatigueBarWidth * (100 - Math.Clamp(fatigue, 0, 100)) / 100.0,
            FatigueBrush = GetFatigueBrush(fatigue),
            Goals = performance?.Goals ?? 0,
            Assists = performance?.Assists ?? 0,
            DefensiveContributions = defensiveContributions,
            Saves = performance?.Saves ?? 0,
            YellowCards = yellowCards,
            RedCards = redCards,
            IsInjured = player.IsInjured || performance?.Injuries > 0,
            ContributionBadgesText = BuildContributionBadgesText(player, performance),
            GoalBadgeText = performance?.Goals > 0 ? $"{SoccerBallIcon()}{performance.Goals}" : string.Empty,
            AssistBadgeText = performance?.Assists > 0 ? $"{AssistIcon()}{performance.Assists}" : string.Empty,
            DefensiveBadgeText = defensiveContributions > 0 ? $"{ShieldIcon()}{defensiveContributions}" : string.Empty,
            SaveBadgeText = performance?.Saves > 0 ? $"{GloveIcon()}{performance.Saves}" : string.Empty,
            YellowBadgeText = yellowCards > 0 ? YellowCardIcon() : string.Empty,
            RedBadgeText = redCards > 0 ? RedCardIcon() : string.Empty,
            InjuryBadgeText = player.IsInjured || performance?.Injuries > 0 ? InjuryIcon() : string.Empty,
            DetailText = BuildPlayerDetailText(player, performance, fatigue, defensiveContributions),
            CardsText = yellowCards == 0 && redCards == 0 ? "None" : $"Y{yellowCards} R{redCards}",
            InjuryStatusText = player.IsInjured || performance?.Injuries > 0 ? "Injured" : "Fit",
            FormText = string.IsNullOrWhiteSpace(player.Form) ? player.CurrentForm.ToString() : player.Form,
            StaminaText = $"{Math.Round(player.CurrentStamina):0}/{player.Stamina}",
            MatchStatusText = status.Text
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
            PausedActionStatusTextBlock.Text = $"Selected {selectedPlayer.Name}. Choose a substitute to replace him.";
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
            return;
        }

        SelectedPlayerPlaceholderTextBlock.Visibility = Visibility.Collapsed;
        SelectedPlayerStatsPanel.Visibility = Visibility.Visible;

        SelectedPlayerNameTextBlock.Text = selectedPlayer.Name;
        SelectedPlayerMetaTextBlock.Text = $"{selectedPlayer.TeamName} | {selectedPlayer.PositionText}";
        SelectedPlayerRatingTextBlock.Text = selectedPlayer.RatingText;
        SelectedPlayerFatigueTextBlock.Text = $"{selectedPlayer.FatiguePercent}%";
        SelectedPlayerStaminaBar.Width = Math.Max(8, 270 * (100 - Math.Clamp(selectedPlayer.FatiguePercent, 0, 100)) / 100.0);
        SelectedPlayerStaminaBar.Background = (Brush)new BrushConverter().ConvertFromString(selectedPlayer.FatigueBrush)!;
        SelectedPlayerGoalsTextBlock.Text = $"{SoccerBallIcon()} {selectedPlayer.Goals}";
        SelectedPlayerAssistsTextBlock.Text = $"{AssistIcon()} {selectedPlayer.Assists}";
        SelectedPlayerDefensiveTextBlock.Text = $"{ShieldIcon()} {selectedPlayer.DefensiveContributions} | {GloveIcon()} {selectedPlayer.Saves}";
        SelectedPlayerCardsTextBlock.Text = selectedPlayer.CardsText;
        SelectedPlayerStatusTextBlock.Text =
            $"Status: {selectedPlayer.MatchStatusText} | Injury: {selectedPlayer.InjuryStatusText}\n" +
            $"Form: {selectedPlayer.FormText} | Stamina: {selectedPlayer.StaminaText}";
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

    private static string MegaphoneIcon() => char.ConvertFromUtf32(0x1F4E3);

    private static string BatteryIcon() => char.ConvertFromUtf32(0x1F50B);

    private static string RotateIcon() => char.ConvertFromUtf32(0x1F504);

    private static string PauseIcon() => char.ConvertFromUtf32(0x23F8);

    private static string CheckeredFlagIcon() => char.ConvertFromUtf32(0x1F3C1);

    private static string BulletIcon() => char.ConvertFromUtf32(0x2022);

    private static string BuildPlayerDetailText(
        Player player,
        PlayerMatchPerformance? performance,
        int fatigue,
        int defensiveContributions)
    {
        var yellowCards = Math.Max(player.YellowCards, performance?.YellowCards ?? 0);
        var redCards = Math.Max(player.IsSentOff ? 1 : 0, performance?.RedCards ?? 0);
        var injuryText = player.IsInjured || performance?.Injuries > 0 ? "Injured" : "Fit";

        return $"{player.Name}\n" +
            $"Position: {GetPositionText(player.Position)}\n" +
            $"Rating: {(performance?.Rating ?? 6.0):0.0}\n" +
            $"Fatigue: {fatigue}%\n" +
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
    }

    private void SetScore(int homeScore, int awayScore)
    {
        HomeScoreTextBlock.Text = homeScore.ToString();
        AwayScoreTextBlock.Text = awayScore.ToString();
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
            FlashScorePanel(scoringPanel);
        }

        PulseScoreboard();
        await Task.Delay(450, cancellationToken);
    }

    private static void FlashScorePanel(Border panel)
    {
        var brush = new SolidColorBrush(Color.FromRgb(31, 164, 90));
        panel.Background = brush;

        var animation = new ColorAnimation
        {
            From = Color.FromRgb(31, 164, 90),
            To = Color.FromRgb(23, 42, 69),
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
        var eventStyle = GetCleanEventStyle(matchEvent.EventType);

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
            RowBackground = eventStyle.RowBackground,
            RowBorderBrush = eventStyle.RowBorderBrush,
            IconBackground = eventStyle.IconBackground,
            LabelBackground = eventStyle.LabelBackground,
            LabelForeground = eventStyle.LabelForeground,
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

        if (ContainsTeamPhrase(matchEvent.Description, "for", match.HomeTeam.Name)
            || StartsWithTeamName(matchEvent.Description, match.HomeTeam.Name)
            || ContainsTeamPhrase(matchEvent.Description, "by", match.HomeTeam.Name))
        {
            return match.HomeTeam.Name;
        }

        if (ContainsTeamPhrase(matchEvent.Description, "for", match.AwayTeam.Name)
            || StartsWithTeamName(matchEvent.Description, match.AwayTeam.Name)
            || ContainsTeamPhrase(matchEvent.Description, "by", match.AwayTeam.Name))
        {
            return match.AwayTeam.Name;
        }

        return string.Empty;
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
        return matchEvent.EventType switch
        {
            EventType.Kickoff => "The match is underway",
            EventType.Attack => $"{teamName} push forward",
            EventType.Shot => $"{teamName} create a shooting chance",
            EventType.Save => "Important save",
            EventType.Goal => $"GOAL for {teamName}!",
            EventType.Foul => "Foul stops the move",
            EventType.YellowCard => "Yellow card shown",
            EventType.RedCard => "Red card shown",
            EventType.Injury => "Injury concern",
            EventType.Penalty => $"{teamName} win a penalty",
            EventType.Offside => $"{teamName} caught offside",
            EventType.DefensiveError => $"{teamName} defensive error",
            EventType.WonderGoal => $"Wonder goal for {teamName}!",
            EventType.GoalkeeperHeroics => "Goalkeeper heroics",
            EventType.SetPieceDanger => $"{teamName} threaten from a set piece",
            EventType.Confrontation => "Players clash",
            EventType.CrowdMomentum => $"{teamName} ride the crowd momentum",
            EventType.Exhaustion => "Fatigue is showing",
            EventType.Substitution => "Substitution",
            EventType.Halftime => "Halftime",
            EventType.Fulltime => "Fulltime",
            EventType.Miss => $"{teamName}'s big chance goes begging",
            _ => "Match event"
        };
    }

    private static string CreateEventDescription(MatchEvent matchEvent)
    {
        return matchEvent.EventType switch
        {
            EventType.Goal => $"{matchEvent.Description} The crowd erupts as the scoreboard changes.",
            EventType.Save => $"{matchEvent.Description} The goalkeeper keeps the match alive.",
            EventType.Miss => $"{matchEvent.Description} That was a major opening.",
            EventType.Foul => $"{matchEvent.Description} The referee brings play back.",
            EventType.YellowCard => $"{matchEvent.Description} The player will need to be careful now.",
            EventType.RedCard => $"{matchEvent.Description} The team must continue with ten players.",
            EventType.Injury => $"{matchEvent.Description} This could change the shape of the match.",
            EventType.Penalty => $"{matchEvent.Description} A huge pressure moment in the box.",
            EventType.Offside => $"{matchEvent.Description} The defensive line survives the scare.",
            EventType.DefensiveError => $"{matchEvent.Description} The opponent will sense an opening now.",
            EventType.WonderGoal => $"{matchEvent.Description} A moment of individual quality changes the match.",
            EventType.GoalkeeperHeroics => $"{matchEvent.Description} The keeper has produced a massive intervention.",
            EventType.SetPieceDanger => $"{matchEvent.Description} The delivery causes real panic.",
            EventType.Confrontation => $"{matchEvent.Description} The temperature of the match rises.",
            EventType.CrowdMomentum => $"{matchEvent.Description} The home crowd is starting to influence the rhythm.",
            EventType.Exhaustion => $"{matchEvent.Description} Legs are heavy and mistakes become more likely.",
            EventType.Substitution => $"{matchEvent.Description} Fresh legs enter the match.",
            _ => matchEvent.Description
        };
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

    private static FeedEventStyle GetEventStyle(EventType eventType)
    {
        return eventType switch
        {
            EventType.Kickoff => new FeedEventStyle("🚩", "KICKOFF", "#FFFFFF", "#DCE5F0", "#EEF3FA", "#E9EEF5", "#14233A"),
            EventType.Attack => new FeedEventStyle("⚔️", "ATTACK", "#EDF5FF", "#BFD9FF", "#DDEBFF", "#D7E8FF", "#1E528F"),
            EventType.Shot => new FeedEventStyle("🎯", "SHOT", "#FFF4E7", "#F2C27C", "#FFE4BF", "#FFE0AF", "#8A4E00"),
            EventType.Save => new FeedEventStyle("🧤", "SAVE", "#EAF8EF", "#95D5AA", "#D9F1E1", "#D9F1E1", "#236B39"),
            EventType.Foul => new FeedEventStyle("🛑", "FOUL", "#F1F3F5", "#C9D0D8", "#E1E5EA", "#DDE2E8", "#465364"),
            EventType.Miss => new FeedEventStyle("⭐", "BIG CHANCE", "#E4F0FF", "#78AEEF", "#CFE3FF", "#BCD8FF", "#164B8F"),
            EventType.Goal => new FeedEventStyle("⚽", "GOAL", "#E4FAEC", "#1FA45A", "#C9F2D8", "#1FA45A", "#FFFFFF"),
            EventType.YellowCard => new FeedEventStyle("🟨", "YELLOW CARD", "#FFF7CD", "#E3BC26", "#FFF0A3", "#FFE36B", "#5F4500"),
            EventType.RedCard => new FeedEventStyle("🟥", "RED CARD", "#FFE6E6", "#D94343", "#FFD1D1", "#D94343", "#FFFFFF"),
            EventType.Injury => new FeedEventStyle("🩹", "INJURY", "#FFEFEF", "#E28585", "#FFDCDC", "#FFD1D1", "#8F1F1F"),
            EventType.Penalty => new FeedEventStyle("🥅", "PENALTY", "#E4FAEC", "#1FA45A", "#C9F2D8", "#1FA45A", "#FFFFFF"),
            EventType.Offside => new FeedEventStyle("🚩", "OFFSIDE", "#F1F3F5", "#C9D0D8", "#E1E5EA", "#DDE2E8", "#465364"),
            EventType.DefensiveError => new FeedEventStyle("⚠️", "DEFENSIVE ERROR", "#FFF4E7", "#F2A65A", "#FFE4BF", "#FFE0AF", "#8A4E00"),
            EventType.WonderGoal => new FeedEventStyle("🌟", "WONDER GOAL", "#E4FAEC", "#1FA45A", "#C9F2D8", "#1FA45A", "#FFFFFF"),
            EventType.GoalkeeperHeroics => new FeedEventStyle("🧤", "KEEPER HEROICS", "#EAF8EF", "#95D5AA", "#D9F1E1", "#D9F1E1", "#236B39"),
            EventType.SetPieceDanger => new FeedEventStyle("🎯", "SET PIECE", "#FFF4E7", "#F2C27C", "#FFE4BF", "#FFE0AF", "#8A4E00"),
            EventType.Confrontation => new FeedEventStyle("🔥", "CONFRONTATION", "#FFE6E6", "#D94343", "#FFD1D1", "#D94343", "#FFFFFF"),
            EventType.CrowdMomentum => new FeedEventStyle("📣", "CROWD MOMENTUM", "#EDF5FF", "#78AEEF", "#CFE3FF", "#BCD8FF", "#164B8F"),
            EventType.Exhaustion => new FeedEventStyle("🔋", "EXHAUSTION", "#F1F3F5", "#C9D0D8", "#E1E5EA", "#DDE2E8", "#465364"),
            EventType.Substitution => new FeedEventStyle("🔄", "SUBSTITUTION", "#F1F8FF", "#9CC9EE", "#DCEEFF", "#DCEEFF", "#225D86"),
            EventType.Halftime => new FeedEventStyle("⏸️", "HALFTIME", "#FFF7E7", "#EAB95C", "#FFEAC0", "#FFE2A1", "#694900"),
            EventType.Fulltime => new FeedEventStyle("🏁", "FULLTIME", "#F2F0FF", "#A69BE8", "#E4E0FF", "#DCD7FF", "#3F367A"),
            _ => new FeedEventStyle("•", "EVENT", "#FFFFFF", "#DCE5F0", "#EEF3FA", "#E9EEF5", "#14233A")
        };
    }

    private static FeedEventStyle GetCleanEventStyle(EventType eventType)
    {
        return eventType switch
        {
            EventType.Kickoff => new FeedEventStyle(FlagIcon(), "KICKOFF", "#FFFFFF", "#DCE5F0", "#EEF3FA", "#E9EEF5", "#14233A"),
            EventType.Attack => new FeedEventStyle(SwordsIcon(), "ATTACK", "#EDF5FF", "#BFD9FF", "#DDEBFF", "#D7E8FF", "#1E528F"),
            EventType.Shot => new FeedEventStyle(TargetIcon(), "SHOT", "#FFF4E7", "#F2C27C", "#FFE4BF", "#FFE0AF", "#8A4E00"),
            EventType.Save => new FeedEventStyle(GloveIcon(), "SAVE", "#EAF8EF", "#95D5AA", "#D9F1E1", "#D9F1E1", "#236B39"),
            EventType.Foul => new FeedEventStyle(StopIcon(), "FOUL", "#F1F3F5", "#C9D0D8", "#E1E5EA", "#DDE2E8", "#465364"),
            EventType.Miss => new FeedEventStyle(StarIcon(), "BIG CHANCE", "#E4F0FF", "#78AEEF", "#CFE3FF", "#BCD8FF", "#164B8F"),
            EventType.Goal => new FeedEventStyle(SoccerBallIcon(), "GOAL", "#E4FAEC", "#1FA45A", "#C9F2D8", "#1FA45A", "#FFFFFF"),
            EventType.YellowCard => new FeedEventStyle(YellowCardIcon(), "YELLOW CARD", "#FFF7CD", "#E3BC26", "#FFF0A3", "#FFE36B", "#5F4500"),
            EventType.RedCard => new FeedEventStyle(RedCardIcon(), "RED CARD", "#FFE6E6", "#D94343", "#FFD1D1", "#D94343", "#FFFFFF"),
            EventType.Injury => new FeedEventStyle(InjuryIcon(), "INJURY", "#FFEFEF", "#E28585", "#FFDCDC", "#FFD1D1", "#8F1F1F"),
            EventType.Penalty => new FeedEventStyle(GoalNetIcon(), "PENALTY", "#E4FAEC", "#1FA45A", "#C9F2D8", "#1FA45A", "#FFFFFF"),
            EventType.Offside => new FeedEventStyle(FlagIcon(), "OFFSIDE", "#F1F3F5", "#C9D0D8", "#E1E5EA", "#DDE2E8", "#465364"),
            EventType.DefensiveError => new FeedEventStyle(WarningIcon(), "DEFENSIVE ERROR", "#FFF4E7", "#F2A65A", "#FFE4BF", "#FFE0AF", "#8A4E00"),
            EventType.WonderGoal => new FeedEventStyle(StarIcon(), "WONDER GOAL", "#E4FAEC", "#1FA45A", "#C9F2D8", "#1FA45A", "#FFFFFF"),
            EventType.GoalkeeperHeroics => new FeedEventStyle(GloveIcon(), "KEEPER HEROICS", "#EAF8EF", "#95D5AA", "#D9F1E1", "#D9F1E1", "#236B39"),
            EventType.SetPieceDanger => new FeedEventStyle(TargetIcon(), "SET PIECE", "#FFF4E7", "#F2C27C", "#FFE4BF", "#FFE0AF", "#8A4E00"),
            EventType.Confrontation => new FeedEventStyle(FireIcon(), "CONFRONTATION", "#FFE6E6", "#D94343", "#FFD1D1", "#D94343", "#FFFFFF"),
            EventType.CrowdMomentum => new FeedEventStyle(MegaphoneIcon(), "CROWD MOMENTUM", "#EDF5FF", "#78AEEF", "#CFE3FF", "#BCD8FF", "#164B8F"),
            EventType.Exhaustion => new FeedEventStyle(BatteryIcon(), "EXHAUSTION", "#F1F3F5", "#C9D0D8", "#E1E5EA", "#DDE2E8", "#465364"),
            EventType.Substitution => new FeedEventStyle(RotateIcon(), "SUBSTITUTION", "#F1F8FF", "#9CC9EE", "#DCEEFF", "#DCEEFF", "#225D86"),
            EventType.Halftime => new FeedEventStyle(PauseIcon(), "HALFTIME", "#FFF7E7", "#EAB95C", "#FFEAC0", "#FFE2A1", "#694900"),
            EventType.Fulltime => new FeedEventStyle(CheckeredFlagIcon(), "FULLTIME", "#F2F0FF", "#A69BE8", "#E4E0FF", "#DCD7FF", "#3F367A"),
            _ => new FeedEventStyle(BulletIcon(), "EVENT", "#FFFFFF", "#DCE5F0", "#EEF3FA", "#E9EEF5", "#14233A")
        };
    }

    private static bool IsImportantEvent(EventType eventType)
    {
        return eventType is EventType.Goal
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.Miss
            or EventType.Injury
            or EventType.Penalty
            or EventType.DefensiveError
            or EventType.WonderGoal
            or EventType.GoalkeeperHeroics
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
            or EventType.DefensiveError
            or EventType.GoalkeeperHeroics
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
            nameof(EventType.Foul) or nameof(EventType.Offside) or nameof(EventType.Exhaustion) => 500,
            nameof(EventType.YellowCard) => 1000,
            nameof(EventType.RedCard) or nameof(EventType.Miss) or nameof(EventType.Injury) or nameof(EventType.DefensiveError) or nameof(EventType.SetPieceDanger) or nameof(EventType.Confrontation) => 1500,
            nameof(EventType.Goal) or nameof(EventType.Penalty) or nameof(EventType.WonderGoal) or nameof(EventType.GoalkeeperHeroics) or nameof(EventType.CrowdMomentum) => 2000,
            nameof(EventType.Halftime) or nameof(EventType.Fulltime) => 1500,
            _ => 0
        };
    }

    private static int GetFatiguePercentage(Player player)
    {
        if (player.Fatigue > 0)
        {
            return Math.Clamp(player.Fatigue, 0, 100);
        }

        if (player.Stamina <= 0)
        {
            return 100;
        }

        var staminaRatio = Math.Clamp(player.CurrentStamina / player.Stamina, 0.0, 1.0);
        return (int)Math.Round((1.0 - staminaRatio) * 100);
    }

    private static string GetFatigueBrush(int fatiguePercentage)
    {
        return fatiguePercentage switch
        {
            <= 40 => "#2FA84F",
            <= 70 => "#E3BC26",
            <= 85 => "#E8872E",
            _ => "#D94343"
        };
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

    private static StatusBadge GetPlayerStatus(Player player, int fatiguePercentage)
    {
        if (player.IsSentOff)
        {
            return new StatusBadge("Red Card", "#FFD1D1", "#8F1F1F");
        }

        if (player.IsInjured)
        {
            return new StatusBadge("Injured", "#FFE6E6", "#8F1F1F");
        }

        if (player.YellowCards > 0)
        {
            return new StatusBadge("Carded", "#FFF0A3", "#5F4500");
        }

        return fatiguePercentage switch
        {
            <= 40 => new StatusBadge("Fresh", "#D9F1E1", "#236B39"),
            <= 70 => new StatusBadge("Tired", "#FFF0A3", "#5F4500"),
            <= 85 => new StatusBadge("Very Tired", "#FFE4BF", "#8A4E00"),
            _ => new StatusBadge("Exhausted", "#FFD1D1", "#8F1F1F")
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
        string LabelForeground);

    private sealed record StatusBadge(string Text, string Background, string Foreground);
}
