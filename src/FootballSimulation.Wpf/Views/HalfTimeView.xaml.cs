using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class HalfTimeView : UserControl
{
    private static readonly string[] SupportedFormations = ["4-3-3", "4-2-3-1", "4-4-2", "3-5-2"];

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly TacticalInsightService _tacticalInsightService = new();
    private readonly SquadSelectionService _squadSelectionService = new();
    private const double PitchCardWidth = 128;
    private const double PitchCardHeight = 70;

    private Player? _selectedStarter;
    private List<Player> _pitchSlots = [];
    private readonly List<PendingHalftimeSubstitution> _pendingHalftimeSubstitutions = [];
    private Point _dragStartPoint;
    private bool _isDraggingPlayer;
    private bool _isLoadingSetup;

    private sealed record PitchSlotAssignment(Player Player, PitchPosition Position);
    private sealed record PendingHalftimeSubstitution(Player Starter, Player Substitute, string AssignedPosition);
    private sealed record PendingSubstitutionRow(PendingHalftimeSubstitution Substitution, string DisplayText);

    public HalfTimeView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadHalfTime();
    }

    private void LoadHalfTime()
    {
        if (_state.CurrentMatch is not null)
        {
            ScoreTextBlock.Text = $"{_state.CurrentMatch.HomeTeam.Name} {_state.CurrentMatch.HomeScore} - {_state.CurrentMatch.AwayScore} {_state.CurrentMatch.AwayTeam.Name}";
        }

        if (_state.SelectedTeam is null)
        {
            return;
        }

        _isLoadingSetup = true;

        LoadFormationSelector(_state.SelectedTeam);
        LoadTactics(_state.SelectedTeam.Tactics);
        InitializePitchSlots();
        RefreshSubstitutes();
        RefreshPendingSubstitutions();
        RenderPitch();

        _isLoadingSetup = false;
        RefreshTacticalInsight();
    }

    private void InitializePitchSlots()
    {
        if (_state.SelectedTeam is null)
        {
            _pitchSlots = [];
            return;
        }

        _pitchSlots = OrderPlayersForPitch(GetActivePitchPlayers(_state.SelectedTeam), _state.SelectedTeam.Formation).ToList();
        if (_selectedStarter is not null && !IsActivePitchPlayer(_selectedStarter))
        {
            _selectedStarter = null;
            UpdateSelectedPlayerDetails();
        }

        AssignFormationPositions();
    }

    private void LoadFormationSelector(Team team)
    {
        FormationComboBox.ItemsSource = SupportedFormations;
        FormationComboBox.SelectedItem = SupportedFormations.Contains(team.Formation)
            ? team.Formation
            : "4-3-3";
    }

    private void LoadTactics(TeamTactics tactics)
    {
        TacticalSettingsPanel.LoadTactics(tactics);
    }

    private void RenderPitch()
    {
        if (_state.SelectedTeam is null || PitchCanvas.ActualWidth <= 0 || PitchCanvas.ActualHeight <= 0)
        {
            return;
        }

        if (_pitchSlots.Count != GetActivePitchPlayers(_state.SelectedTeam).Count())
        {
            InitializePitchSlots();
        }

        PitchCanvas.Children.Clear();

        var formation = FormationComboBox.SelectedItem as string ?? _state.SelectedTeam.Formation;
        var positions = _formationLayoutService.GetPositions(formation);
        AssignFormationPositions(positions);

        foreach (var assignment in CreatePitchSlotAssignments(_pitchSlots, positions))
        {
            var player = assignment.Player;
            var position = assignment.Position;
            var button = CreatePlayerButton(player, position.ExactPosition);

            Canvas.SetLeft(button, GetClampedCanvasPosition(PitchCanvas.ActualWidth, position.X, PitchCardWidth));
            Canvas.SetTop(button, GetClampedCanvasPosition(PitchCanvas.ActualHeight, position.Y, PitchCardHeight));
            PitchCanvas.Children.Add(button);
        }
    }

    private void AssignFormationPositions(IReadOnlyList<PitchPosition>? positions = null)
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        positions ??= _formationLayoutService.GetPositions(FormationComboBox.SelectedItem as string ?? _state.SelectedTeam.Formation);

        for (var index = 0; index < _pitchSlots.Count && index < positions.Count; index++)
        {
            if (CanPlayerOccupySlot(_pitchSlots[index], positions[index].ExactPosition))
            {
                PositionSuitabilityService.EnsurePositionMetadata(_pitchSlots[index], positions[index].ExactPosition);
            }
        }
    }

    private static List<PitchSlotAssignment> CreatePitchSlotAssignments(
        IReadOnlyList<Player> players,
        IReadOnlyList<PitchPosition> positions)
    {
        var remainingPlayers = players.Where(IsActivePitchPlayer).ToList();
        var assignments = new List<PitchSlotAssignment>();
        foreach (var position in positions)
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

        return remainingPlayers.FirstOrDefault(player => !PositionSuitabilityService.IsGoalkeeperCapable(player)) ??
            remainingPlayers.FirstOrDefault();
    }

    private static double GetClampedCanvasPosition(double canvasSize, double normalizedPosition, double elementSize)
    {
        var rawPosition = (canvasSize * normalizedPosition) - (elementSize / 2);
        return Math.Clamp(rawPosition, 4, Math.Max(4, canvasSize - elementSize - 4));
    }

    private Button CreatePlayerButton(Player player, string displayedPosition)
    {
        var card = CreatePitchPlayerCard(player, displayedPosition);
        var button = new Button
        {
            Width = PitchCardWidth,
            MinHeight = PitchCardHeight,
            Tag = player,
            DataContext = card,
            ToolTip = "Drag this player or drop another player here.",
            Content = card,
            ContentTemplate = (DataTemplate)FindResource("PitchPlayerCardTemplate"),
            Style = (Style)FindResource("PitchPlayerButtonStyle"),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AllowDrop = true
        };

        button.Click += PlayerButton_Click;
        button.PreviewMouseLeftButtonDown += StarterButton_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += StarterButton_MouseMove;
        button.MouseMove += StarterButton_MouseMove;
        button.PreviewDragEnter += PlayerCard_DragEnter;
        button.PreviewDragLeave += PlayerCard_DragLeave;
        button.AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(PlayerCard_DragOver), true);
        button.AddHandler(DragDrop.DragOverEvent, new DragEventHandler(PlayerCard_DragOver), true);
        button.AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(PlayerCard_Drop), true);
        button.AddHandler(DragDrop.DropEvent, new DragEventHandler(PlayerCard_Drop), true);
        return button;
    }

    private PitchPlayerCard CreatePitchPlayerCard(Player player, string displayedPosition)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var isOutOfPosition = PositionSuitabilityService.IsOutOfPosition(player);
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(player);
        var ratingVisual = GetRatingVisual(player, suitability);
        var teamColors = TeamColorService.GetPalette(_state.SelectedTeam);
        var textForeground = teamColors.TextColor;
        var positionBackground = teamColors.SecondaryColor;

        return new PitchPlayerCard
        {
            Player = player,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            ShirtNumberValue = player.SquadNumber > 0 ? player.SquadNumber.ToString() : string.Empty,
            PlayerImagePath = GetPlayerImagePath(player),
            PlayerName = player.Name,
            PositionText = displayedPosition,
            OverallText = $"OVR {ratingVisual.Rating}",
            OverallForeground = textForeground,
            TextForeground = textForeground,
            MutedForeground = textForeground,
            PositionBackground = positionBackground,
            PositionForeground = TeamColorService.GetReadableTextColor(positionBackground),
            GrowthText = PlayerGrowthDisplayHelper.CreateGrowthText(player),
            Stamina = GetStaminaPercentage(player),
            StaminaBrush = GetStaminaBrush(player),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
            CardStatusBadges = CreateCardStatusBadges(player, pendingIn: false),
            CardBackground = teamColors.PrimaryColor,
            CardBorderBrush = player == _selectedStarter
                ? teamColors.SelectedGlowColor
                : isOutOfPosition
                    ? ratingVisual.Foreground
                    : teamColors.BorderColor,
            CardBorderThickness = player == _selectedStarter ? new Thickness(3) : new Thickness(1)
        };
    }

    private void PlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Player player })
        {
            _selectedStarter = player;
            UpdateSelectedPlayerDetails();
            RenderPitch();
        }
    }

    private void FormationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_state.SelectedTeam is not null && FormationComboBox.SelectedItem is string formation)
        {
            _state.SelectedTeam.Formation = formation;
            if (!_isLoadingSetup)
            {
                _pitchSlots = OrderPlayersForPitch(GetActivePitchPlayers(_state.SelectedTeam), formation).ToList();
                SyncActivePitchSlotsIntoTeamPlayers();
            }
        }

        RenderPitch();
        RefreshTacticalInsight();
    }

    private void TacticalSettingsPanel_TacticsChanged(object? sender, EventArgs e)
    {
        RefreshTacticalInsight();
    }

    private void ExecuteHalftimeSwap(Player starter, Player substitute)
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        if (!starter.IsOnPitch || starter.IsSentOff)
        {
            MessageBox.Show("Sent-off players cannot be replaced.");
            return;
        }

        var usedSubstitutions = GetUsedSubstitutions();
        var existingStarterPlan = _pendingHalftimeSubstitutions.FirstOrDefault(substitution => substitution.Starter == starter);
        var pendingCountAfterChange = existingStarterPlan is null
            ? _pendingHalftimeSubstitutions.Count + 1
            : _pendingHalftimeSubstitutions.Count;
        if (usedSubstitutions + pendingCountAfterChange > MatchConstants.MaxSubstitutionsPerTeam)
        {
            MessageBox.Show($"Maximum substitutions reached ({MatchConstants.MaxSubstitutionsPerTeam}/5).");
            return;
        }

        PositionSuitabilityService.EnsurePositionMetadata(starter);
        PositionSuitabilityService.EnsurePositionMetadata(substitute);
        var incomingAssignedPosition = starter.AssignedPosition;
        if (incomingAssignedPosition == "GK" && !PositionSuitabilityService.IsGoalkeeperCapable(substitute))
        {
            MessageBox.Show("Goalkeeper substitutions require a goalkeeper-capable replacement.");
            return;
        }

        if (_state.CurrentMatch is not null &&
            _squadSelectionService.WasPlayerSubstitutedOff(_state.CurrentMatch, _state.SelectedTeam.Name, substitute.Name))
        {
            MessageBox.Show("Players substituted off cannot return in the same match.");
            return;
        }

        if (existingStarterPlan is not null)
        {
            _pendingHalftimeSubstitutions.Remove(existingStarterPlan);
        }

        if (_pendingHalftimeSubstitutions.Any(substitution => substitution.Substitute == substitute))
        {
            MessageBox.Show($"{substitute.Name} is already queued to come on.");
            return;
        }

        _pendingHalftimeSubstitutions.Add(new PendingHalftimeSubstitution(starter, substitute, incomingAssignedPosition));
        Debug.WriteLine($"[HalfTimeDrag] Queued halftime sub: {substitute.Name} -> {starter.Name} ({incomingAssignedPosition})");

        RefreshSubstitutes();
        UpdateSelectedPlayerDetails();
        RefreshPendingSubstitutions();
        RenderPitch();
        RefreshTacticalInsight();
    }

    private void RefreshSubstitutes()
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        var benchCards = _state.SelectedTeam.Substitutes
            .Where(IsAvailableSubstitute)
            .Select(CreateBenchPlayerCard)
            .ToList();

        SubstituteListBox.ItemsSource = null;
        SubstituteListBox.ItemsSource = benchCards;
        SubstituteListBox.IsEnabled = benchCards.Count > 0 && GetUsedSubstitutions() + _pendingHalftimeSubstitutions.Count < MatchConstants.MaxSubstitutionsPerTeam;
        SubstitutionStatusTextBlock.Text = _pendingHalftimeSubstitutions.Count == 0
            ? $"{GetUsedSubstitutions()}/5 used"
            : $"{GetUsedSubstitutions()}/5 used · {_pendingHalftimeSubstitutions.Count} queued";
    }

    private bool IsAvailableSubstitute(Player player)
    {
        if (player.IsSentOff || player.IsSuspended || player.IsInjured)
        {
            return false;
        }

        return _state.CurrentMatch is null ||
            !_squadSelectionService.WasPlayerSubstitutedOff(_state.CurrentMatch, _state.SelectedTeam?.Name ?? string.Empty, player.Name);
    }

    private void RefreshPendingSubstitutions()
    {
        if (_pendingHalftimeSubstitutions.Count == 0)
        {
            PendingSubstitutionPanel.Visibility = Visibility.Collapsed;
            PendingSubstitutionsItemsControl.ItemsSource = null;
            return;
        }

        PendingSubstitutionPanel.Visibility = Visibility.Visible;
        PendingSubstitutionHeaderTextBlock.Text = $"{_pendingHalftimeSubstitutions.Count} queued";
        PendingSubstitutionsItemsControl.ItemsSource = _pendingHalftimeSubstitutions
            .Select(substitution => new PendingSubstitutionRow(
                substitution,
                $"{substitution.Substitute.Name} ↑  {substitution.Starter.Name} ↓"))
            .ToList();
    }

    private void CancelPendingSubstitutionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PendingSubstitutionRow row })
        {
            return;
        }

        _pendingHalftimeSubstitutions.Remove(row.Substitution);
        RefreshSubstitutes();
        RefreshPendingSubstitutions();
        UpdateSelectedPlayerDetails();
        RenderPitch();
    }

    private BenchPlayerCard CreateBenchPlayerCard(Player player)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var teamColors = TeamColorService.GetPalette(_state.SelectedTeam);

        return new BenchPlayerCard
        {
            Player = player,
            Name = player.Name,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            PlayerImagePath = GetPlayerImagePath(player),
            Position = player.PreferredPosition,
            OverallText = $"OVR {GetOverallRating(player)}",
            OverallRating = GetOverallRating(player),
            GrowthText = PlayerGrowthDisplayHelper.CreateGrowthText(player),
            Stamina = GetStaminaPercentage(player),
            StaminaBrush = GetStaminaBrush(player),
            BenchFormBadgeText = form.Text,
            BenchFormBadgeBackground = form.Background,
            BenchFormBadgeForeground = form.Foreground,
            CardBackground = teamColors.PrimaryColor,
            CardBorderBrush = teamColors.BorderColor,
            TextForeground = teamColors.TextColor,
            PositionBackground = teamColors.SecondaryColor,
            PositionForeground = TeamColorService.GetReadableTextColor(teamColors.SecondaryColor),
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
            CardStatusBadges = CreateCardStatusBadges(player, pendingIn: true)
        };
    }

    private IReadOnlyList<PlayerCardStatusBadge> CreateCardStatusBadges(Player player, bool pendingIn)
    {
        var badges = PlayerCardStatusBadgeHelper.Create(player, FindSelectedPlayerPerformance(player, _state.SelectedTeam)).ToList();
        if (_pendingHalftimeSubstitutions.Any(substitution => substitution.Starter == player))
        {
            badges.Add(new PlayerCardStatusBadge
            {
                Text = "↓",
                TooltipText = "Pending halftime substitution off.",
                Background = "#DC2626",
                Foreground = "#FFFFFF",
                BorderBrush = "#FCA5A5"
            });
        }

        if (pendingIn && _pendingHalftimeSubstitutions.Any(substitution => substitution.Substitute == player))
        {
            badges.Add(new PlayerCardStatusBadge
            {
                Text = "↑",
                TooltipText = "Pending halftime substitution on.",
                Background = "#16A34A",
                Foreground = "#FFFFFF",
                BorderBrush = "#86EFAC"
            });
        }

        return badges;
    }

    private int GetUsedSubstitutions()
    {
        return _state.CurrentMatch is null || _state.SelectedTeam is null
            ? 0
            : _squadSelectionService.CountTeamSubstitutions(_state.CurrentMatch, _state.SelectedTeam.Name);
    }

    private void UpdateSelectedPlayerDetails()
    {
        if (_selectedStarter is null)
        {
            SelectedPlayerEmptyTextBlock.Visibility = Visibility.Visible;
            SelectedPlayerCard.Visibility = Visibility.Collapsed;
            SelectedPlayerCard.DataContext = null;
            return;
        }

        PositionSuitabilityService.EnsurePositionMetadata(_selectedStarter);
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(_selectedStarter);
        var team = FindSelectedPlayerTeam(_selectedStarter);
        var performance = FindSelectedPlayerPerformance(_selectedStarter, team);
        var rating = performance?.Rating ?? 6.0;
        var passAccuracy = GetEstimatedPassAccuracy(team, rating, _selectedStarter.Stamina);
        var formBadge = PlayerFormBadgeHelper.Create(_selectedStarter.FormStatus);
        var ratingVisual = GetRatingVisual(_selectedStarter, suitability);

        SelectedPlayerEmptyTextBlock.Visibility = Visibility.Collapsed;
        SelectedPlayerCard.Visibility = Visibility.Visible;
        SelectedPlayerCard.DataContext = _selectedStarter;

        SelectedPlayerNameTextBlock.Text = _selectedStarter.Name;
        SelectedPlayerMetaTextBlock.Text = $"{team?.Name ?? _state.SelectedTeam?.Name ?? "Team"} | {_selectedStarter.AssignedPosition}";
        var selectedTraitBadges = PlayerTraitBadgeHelper.Create(_selectedStarter.Traits);
        SelectedPlayerTraitItemsControl.ItemsSource = selectedTraitBadges;
        SelectedPlayerTraitItemsControl.Visibility = selectedTraitBadges.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        var selectedCardBadges = PlayerCardStatusBadgeHelper.Create(_selectedStarter, performance);
        SelectedPlayerCardStatusItemsControl.ItemsSource = selectedCardBadges;
        SelectedPlayerCardStatusItemsControl.Visibility = selectedCardBadges.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SelectedPlayerFormBadgeBorder.Background = ToBrush(formBadge.Background);
        SelectedPlayerFormBadgeTextBlock.Foreground = ToBrush(formBadge.Foreground);
        SelectedPlayerFormBadgeTextBlock.Text = formBadge.Text;
        SelectedPlayerOvrBadgeTextBlock.Text = $"OVR {ratingVisual.Rating}";
        SelectedPlayerOvrBadgeBorder.Background = ToBrush(ratingVisual.Background);
        SelectedPlayerCard.ToolTip = PlayerGrowthDisplayHelper.CreateGrowthText(_selectedStarter);

        if (_selectedStarter.Position == Position.Goalkeeper)
        {
            SetSelectedPlayerStatRows(
                [
                    new("Rating", rating.ToString("0.0")),
                    new("Saves", (performance?.Saves ?? 0).ToString()),
                    new("Clean Sheet", IsCleanSheet(team) ? "Yes" : "No"),
                    new("Claims", GetEstimatedClaims(performance).ToString()),
                    new("Punches", GetEstimatedPunches(performance).ToString()),
                    new("Pass Accuracy", $"{passAccuracy:0}%")
                ],
                [
                    new("Stamina", $"{GetStaminaPercentage(_selectedStarter)}%"),
                    new("Goals Conceded", GetGoalsConceded(team).ToString()),
                    new("Fouls", (performance?.Fouls ?? 0).ToString()),
                    new("Cards", GetCardsText(_selectedStarter, performance)),
                    new("Status", GetMatchStatusText(_selectedStarter)),
                    new("Injury", _selectedStarter.IsInjured || performance?.Injuries > 0 ? "Injured" : "None")
                ]);
            return;
        }

        SetSelectedPlayerStatRows(
            [
                new("Rating", rating.ToString("0.0")),
                new("Goals", (performance?.Goals ?? 0).ToString()),
                new("Assists", (performance?.Assists ?? 0).ToString()),
                new("Shots", (performance?.Shots ?? 0).ToString()),
                new("Successful Tackles", (performance?.Tackles ?? 0).ToString()),
                new("Interceptions", (performance?.Interceptions ?? 0).ToString())
            ],
            [
                new("Stamina", $"{GetStaminaPercentage(_selectedStarter)}%"),
                new("Pass Accuracy", $"{passAccuracy:0}%"),
                new("Duels Won", GetDuelsWon(performance).ToString()),
                new("Fouls", (performance?.Fouls ?? 0).ToString()),
                new("Cards", GetCardsText(_selectedStarter, performance)),
                new("Status", GetMatchStatusText(_selectedStarter))
            ]);
    }

    private Team? FindSelectedPlayerTeam(Player player)
    {
        if (_state.CurrentMatch is null)
        {
            return _state.SelectedTeam;
        }

        if (_state.CurrentMatch.HomeTeam.Players.Concat(_state.CurrentMatch.HomeTeam.Substitutes).Contains(player))
        {
            return _state.CurrentMatch.HomeTeam;
        }

        if (_state.CurrentMatch.AwayTeam.Players.Concat(_state.CurrentMatch.AwayTeam.Substitutes).Contains(player))
        {
            return _state.CurrentMatch.AwayTeam;
        }

        return _state.SelectedTeam;
    }

    private PlayerMatchPerformance? FindSelectedPlayerPerformance(Player player, Team? team)
    {
        return _state.CurrentMatch?.PlayerPerformances.FirstOrDefault(performance =>
            string.Equals(performance.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(performance.TeamName, team?.Name, StringComparison.OrdinalIgnoreCase));
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

    private void RefreshTacticalInsight()
    {
        if (_isLoadingSetup || _state.SelectedTeam is null || _state.CurrentFixture is null || TacticalInsightInfoIcon is null)
        {
            return;
        }

        SaveSetup(_state.SelectedTeam);

        var opponent = _state.CurrentFixture.HomeTeam == _state.SelectedTeam
            ? _state.CurrentFixture.AwayTeam
            : _state.CurrentFixture.HomeTeam;
        var insight = _tacticalInsightService.GenerateInsight(_state.SelectedTeam, opponent);

        TacticalInsightInfoIcon.ToolTip =
            $"Tactical Insight{Environment.NewLine}{Environment.NewLine}" +
            $"Opponent threats{Environment.NewLine}{FormatInsightItems(insight.OpponentThreats)}{Environment.NewLine}{Environment.NewLine}" +
            $"Likely tactics{Environment.NewLine}{FormatInsightItems(insight.LikelyTactics)}{Environment.NewLine}{Environment.NewLine}" +
            $"Warnings{Environment.NewLine}{FormatInsightItems(insight.Warnings)}{Environment.NewLine}{Environment.NewLine}" +
            $"Recommendations{Environment.NewLine}{FormatInsightItems(insight.Recommendations)}";
    }

    private static string FormatInsightItems(IEnumerable<string> items)
    {
        var itemList = items.ToList();
        return itemList.Count == 0
            ? "- No major issue identified."
            : string.Join(Environment.NewLine, itemList.Select(item => $"- {item}"));
    }

    private static int GetOverallRating(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
    }

    private static int GetStaminaPercentage(Player player)
    {
        return Math.Clamp((int)Math.Round(player.Stamina), 0, 100);
    }

    private static string GetStaminaBrush(Player player)
    {
        return GetStaminaPercentage(player) switch
        {
            >= 75 => "#2FA84F",
            >= 50 => "#E3BC26",
            >= 25 => "#E8872E",
            _ => "#D94343"
        };
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

    private static string GetCardsText(Player player, PlayerMatchPerformance? performance)
    {
        var yellowCards = Math.Max(player.YellowCards, performance?.YellowCards ?? 0);
        var redCards = Math.Max(player.IsSentOff ? 1 : 0, performance?.RedCards ?? 0);
        var cards = new List<string>();

        if (yellowCards > 0)
        {
            cards.Add($"{YellowCardIcon()} {yellowCards}");
        }

        if (redCards > 0)
        {
            cards.Add($"{RedCardIcon()} {redCards}");
        }

        return cards.Count == 0 ? "None" : string.Join(" ", cards);
    }

    private static string GetMatchStatusText(Player player)
    {
        if (player.IsSentOff)
        {
            return "Sent off";
        }

        if (player.IsInjured)
        {
            return "Injured";
        }

        return player.IsOnPitch ? "On pitch" : "Bench";
    }

    private static string YellowCardIcon()
    {
        return char.ConvertFromUtf32(0x1F7E8);
    }

    private static string RedCardIcon()
    {
        return char.ConvertFromUtf32(0x1F7E5);
    }

    private static string GetPlayerImagePath(Player player)
    {
        var playerImage = $"pack://application:,,,/Assets/Players/{CreatePlayerImageSlug(player.Name)}.png";
        if (ResourceExists(playerImage))
        {
            return playerImage;
        }

        const string defaultImage = "pack://application:,,,/Assets/Players/default.png";
        return ResourceExists(defaultImage) ? defaultImage : string.Empty;
    }

    private static string CreatePlayerImageSlug(string playerName)
    {
        var slugCharacters = playerName
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        return string.Join("-", new string(slugCharacters)
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ResourceExists(string packUri)
    {
        try
        {
            return Application.GetResourceStream(new Uri(packUri, UriKind.Absolute)) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static ImageSource? CreateImageSource(string imagePath)
    {
        return string.IsNullOrWhiteSpace(imagePath)
            ? null
            : new BitmapImage(new Uri(imagePath, UriKind.Absolute));
    }

    private static Brush ToBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private static RatingVisual GetRatingVisual(Player player, double suitability)
    {
        if (suitability >= 1.0)
        {
            return new RatingVisual(GetOverallRating(player), "#071A2E", "#102033");
        }

        if (suitability >= 0.90)
        {
            return new RatingVisual(PositionSuitabilityService.GetEffectiveOverall(player), "#C96A00", "#C96A00");
        }

        return new RatingVisual(PositionSuitabilityService.GetEffectiveOverall(player), "#B42318", "#B42318");
    }

    private static string GetPositionGroupText(Player player)
    {
        var secondaryPositions = player.SecondaryPositions.Count == 0
            ? string.Empty
            : $" | Also {string.Join("/", player.SecondaryPositions)}";

        return $"Pref {player.PreferredPosition}{secondaryPositions}";
    }

    private void PitchCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderPitch();
    }

    private void StarterButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void StarterButton_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button { Tag: Player player } button)
        {
            return;
        }

        if (!HasMovedEnoughToDrag(e.GetPosition(this)))
        {
            return;
        }

        e.Handled = true;
        StartPlayerDrag(button, player, DragSource.StartingXi, _pitchSlots.IndexOf(player));
    }

    private void SubstituteCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void SubstituteCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: BenchPlayerCard benchCard })
        {
            return;
        }

        if (!HasMovedEnoughToDrag(e.GetPosition(this)))
        {
            return;
        }

        e.Handled = true;
        StartPlayerDrag(
            (DependencyObject)sender,
            benchCard.Player,
            DragSource.Substitute,
            _state.SelectedTeam?.Substitutes.IndexOf(benchCard.Player) ?? -1);
    }

    private bool HasMovedEnoughToDrag(Point currentPosition)
    {
        return Math.Abs(currentPosition.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void StartPlayerDrag(DependencyObject source, Player player, DragSource dragSource, int sourceIndex)
    {
        if (_isDraggingPlayer)
        {
            return;
        }

        _isDraggingPlayer = true;
        Debug.WriteLine($"[HalfTimeDrag] Start: {player.Name}; source={dragSource}; slot/index={sourceIndex}");

        var dataObject = new DataObject();
        dataObject.SetData(typeof(DraggedPlayerInfo), new DraggedPlayerInfo(player, dragSource, sourceIndex));
        dataObject.SetData(typeof(Player), player);

        try
        {
            DragDrop.DoDragDrop(source, dataObject, DragDropEffects.Move);
        }
        finally
        {
            _isDraggingPlayer = false;
        }
    }

    private void PlayerCard_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyDropTargetStyle(button, GetDraggedPlayer(e) is not null);
        }
    }

    private void PlayerCard_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = GetDraggedPlayer(e) is not null;
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlayerCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            ResetDropTargetStyle(button);
        }
    }

    private void PlayerCard_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        ResetDropTargetStyle(button);

        var draggedPlayer = GetDraggedPlayer(e);
        var targetPlayer = GetTargetPlayer(button);
        if (draggedPlayer is null || targetPlayer is null)
        {
            return;
        }

        Debug.WriteLine(
            $"[HalfTimeDrag] Drop: dragged={draggedPlayer.Player.Name}; source={draggedPlayer.Source}; source slot/index={draggedPlayer.SourceIndex}; " +
            $"target={targetPlayer.Name}; target slot={_pitchSlots.IndexOf(targetPlayer)}");

        if (draggedPlayer.Source == DragSource.StartingXi)
        {
            SwapPitchPlayers(draggedPlayer.Player, targetPlayer);
        }
        else
        {
            ExecuteHalftimeSwap(targetPlayer, draggedPlayer.Player);
        }

        e.Handled = true;
    }

    private void SubstituteCard_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            ApplySubstituteDropTargetStyle(border, CanDropOnSubstituteCard(e));
        }
    }

    private void SubstituteCard_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = CanDropOnSubstituteCard(e);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void SubstituteCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            ResetSubstituteDropTargetStyle(border);
        }
    }

    private void SubstituteCard_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BenchPlayerCard benchCard } element)
        {
            return;
        }

        if (element is Border border)
        {
            ResetSubstituteDropTargetStyle(border);
        }

        var draggedPlayer = GetDraggedPlayer(e);
        if (draggedPlayer is null)
        {
            return;
        }

        Debug.WriteLine(
            $"[HalfTimeDrag] Drop: dragged={draggedPlayer.Player.Name}; source={draggedPlayer.Source}; source slot/index={draggedPlayer.SourceIndex}; " +
            $"target={benchCard.Player.Name}; target substitute index={_state.SelectedTeam?.Substitutes.IndexOf(benchCard.Player) ?? -1}");

        if (draggedPlayer.Source == DragSource.StartingXi)
        {
            ExecuteHalftimeSwap(draggedPlayer.Player, benchCard.Player);
        }
        else
        {
            SwapSubstitutes(draggedPlayer.Player, benchCard.Player);
        }

        e.Handled = true;
    }

    private void SubstituteListBox_DragEnter(object sender, DragEventArgs e)
    {
        ApplySubstituteListDropTargetStyle(GetDraggedPlayer(e)?.Source == DragSource.StartingXi);
    }

    private void SubstituteListBox_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = GetDraggedPlayer(e)?.Source == DragSource.StartingXi;
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void SubstituteListBox_DragLeave(object sender, DragEventArgs e)
    {
        ResetSubstituteListDropTargetStyle();
    }

    private void SubstituteListBox_Drop(object sender, DragEventArgs e)
    {
        ResetSubstituteListDropTargetStyle();

        var draggedPlayer = GetDraggedPlayer(e);
        var firstSubstitute = _state.SelectedTeam?.Substitutes.FirstOrDefault(IsAvailableSubstitute);
        if (draggedPlayer is null || draggedPlayer.Source != DragSource.StartingXi || firstSubstitute is null)
        {
            return;
        }

        ExecuteHalftimeSwap(draggedPlayer.Player, firstSubstitute);
        e.Handled = true;
    }

    private static bool CanDropOnSubstituteCard(DragEventArgs e)
    {
        return GetDraggedPlayer(e) is not null;
    }

    private static DraggedPlayerInfo? GetDraggedPlayer(DragEventArgs e)
    {
        return e.Data.GetDataPresent(typeof(DraggedPlayerInfo))
            ? e.Data.GetData(typeof(DraggedPlayerInfo)) as DraggedPlayerInfo
            : null;
    }

    private static Player? GetTargetPlayer(FrameworkElement element)
    {
        return element.DataContext is PitchPlayerCard pitchCard
            ? pitchCard.Player
            : element.Tag as Player;
    }

    private void SwapPitchPlayers(Player draggedPlayer, Player targetPlayer)
    {
        if (_state.SelectedTeam is null || draggedPlayer == targetPlayer)
        {
            return;
        }

        var draggedIndex = _pitchSlots.IndexOf(draggedPlayer);
        var targetIndex = _pitchSlots.IndexOf(targetPlayer);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            Debug.WriteLine(
                $"[HalfTimeDrag] Swap failed: {draggedPlayer.Name} index={draggedIndex}; {targetPlayer.Name} index={targetIndex}");
            return;
        }

        var positions = _formationLayoutService.GetPositions(FormationComboBox.SelectedItem as string ?? _state.SelectedTeam.Formation);
        var draggedTargetSlot = targetIndex < positions.Count ? positions[targetIndex].ExactPosition : string.Empty;
        var targetTargetSlot = draggedIndex < positions.Count ? positions[draggedIndex].ExactPosition : string.Empty;
        if (!CanPlayerOccupySlot(draggedPlayer, draggedTargetSlot) || !CanPlayerOccupySlot(targetPlayer, targetTargetSlot))
        {
            MessageBox.Show("Only a goalkeeper-capable player can occupy the GK slot.");
            return;
        }

        (_pitchSlots[draggedIndex], _pitchSlots[targetIndex]) = (_pitchSlots[targetIndex], _pitchSlots[draggedIndex]);
        SyncActivePitchSlotsIntoTeamPlayers();
        AssignFormationPositions();

        Debug.WriteLine(
            $"[HalfTimeDrag] Swapped StartingXI: {draggedPlayer.Name} -> slot {targetIndex} ({draggedPlayer.AssignedPosition}); " +
            $"{targetPlayer.Name} -> slot {draggedIndex} ({targetPlayer.AssignedPosition})");

        _selectedStarter = draggedPlayer;
        UpdateSelectedPlayerDetails();
        RenderPitch();
        RefreshTacticalInsight();
    }

    private void SwapSubstitutes(Player draggedPlayer, Player targetPlayer)
    {
        if (_state.SelectedTeam is null || draggedPlayer == targetPlayer)
        {
            return;
        }

        var draggedIndex = _state.SelectedTeam.Substitutes.IndexOf(draggedPlayer);
        var targetIndex = _state.SelectedTeam.Substitutes.IndexOf(targetPlayer);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            return;
        }

        (_state.SelectedTeam.Substitutes[draggedIndex], _state.SelectedTeam.Substitutes[targetIndex]) =
            (_state.SelectedTeam.Substitutes[targetIndex], _state.SelectedTeam.Substitutes[draggedIndex]);

        Debug.WriteLine(
            $"[HalfTimeDrag] Swapped substitutes: {draggedPlayer.Name} -> index {targetIndex}; {targetPlayer.Name} -> index {draggedIndex}");

        RefreshSubstitutes();
    }

    private static void ApplyDropTargetStyle(Button button, bool canDrop)
    {
        if (!canDrop)
        {
            return;
        }

        button.BorderBrush = ToBrush(ThemeManager.GetBrushHex("AppHighlightBrush", "#6B4A16"));
        button.BorderThickness = new Thickness(3);
        button.Background = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255));
    }

    private static void ResetDropTargetStyle(Button button)
    {
        button.BorderBrush = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Background = Brushes.Transparent;
    }

    private static void ApplySubstituteDropTargetStyle(Border border, bool canDrop)
    {
        if (!canDrop)
        {
            return;
        }

        border.BorderBrush = ToBrush(ThemeManager.GetBrushHex("AppHighlightBrush", "#6B4A16"));
        border.BorderThickness = new Thickness(3);
        border.Background = ToBrush(ThemeManager.GetBrushHex("TableCurrentClubBackground", "#5A3D12"));
    }

    private static void ResetSubstituteDropTargetStyle(Border border)
    {
        border.ClearValue(Border.BorderBrushProperty);
        border.ClearValue(Border.BorderThicknessProperty);
        border.ClearValue(Border.BackgroundProperty);
    }

    private void ApplySubstituteListDropTargetStyle(bool canDrop)
    {
        if (canDrop)
        {
            SubstituteListBox.Background = new SolidColorBrush(Color.FromArgb(48, 255, 224, 102));
        }
    }

    private void ResetSubstituteListDropTargetStyle()
    {
        SubstituteListBox.Background = Brushes.Transparent;
    }

    private void StartSecondHalfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is not null)
        {
            if (!ApplyPendingHalftimeSubstitutions())
            {
                return;
            }

            SaveSetup(_state.SelectedTeam);
        }

        _navigate(new MatchLiveView(_state, _navigate, isSecondHalf: true));
    }

    private bool ApplyPendingHalftimeSubstitutions()
    {
        if (_state.SelectedTeam is null || _pendingHalftimeSubstitutions.Count == 0)
        {
            return true;
        }

        SyncActivePitchSlotsIntoTeamPlayers();
        foreach (var pendingSubstitution in _pendingHalftimeSubstitutions.ToList())
        {
            var result = _squadSelectionService.SwapStarterWithSubstitute(
                _state.SelectedTeam,
                pendingSubstitution.Starter,
                pendingSubstitution.Substitute,
                _state.CurrentMatch,
                MatchConstants.HalftimeMinute);
            if (!result.Success)
            {
                MessageBox.Show(result.Message);
                RefreshSubstitutes();
                RefreshPendingSubstitutions();
                RenderPitch();
                return false;
            }

            pendingSubstitution.Starter.AssignedPosition = pendingSubstitution.Starter.PreferredPosition;
            PositionSuitabilityService.EnsurePositionMetadata(pendingSubstitution.Substitute, pendingSubstitution.AssignedPosition);
        }

        _pendingHalftimeSubstitutions.Clear();
        _pitchSlots = GetActivePitchPlayers(_state.SelectedTeam).ToList();
        RefreshSubstitutes();
        RefreshPendingSubstitutions();
        RenderPitch();
        return true;
    }

    private void SaveSetup(Team team)
    {
        if (FormationComboBox.SelectedItem is string formation)
        {
            team.Formation = formation;
        }

        TacticalSettingsPanel.ApplyTo(team.Tactics);
    }

    private void SyncActivePitchSlotsIntoTeamPlayers()
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        var activeSlots = new Queue<Player>(_pitchSlots.Where(IsActivePitchPlayer));
        for (var index = 0; index < _state.SelectedTeam.Players.Count && activeSlots.Count > 0; index++)
        {
            var currentPlayer = _state.SelectedTeam.Players[index];
            if (!IsActivePitchPlayer(currentPlayer))
            {
                continue;
            }

            _state.SelectedTeam.Players[index] = activeSlots.Dequeue();
        }
    }

    private static IEnumerable<Player> GetActivePitchPlayers(Team team)
    {
        return team.Players.Where(IsActivePitchPlayer);
    }

    private static bool IsActivePitchPlayer(Player player)
    {
        return player.IsOnPitch && !player.IsSentOff;
    }

    private IEnumerable<Player> OrderPlayersForPitch(IEnumerable<Player> players, string formation)
    {
        var remainingPlayers = players
            .Where(IsActivePitchPlayer)
            .ToList();
        var orderedPlayers = new List<Player>();
        var formationSlots = _formationLayoutService.GetPositions(formation);

        foreach (var slot in formationSlots)
        {
            var selectedPlayer = remainingPlayers
                .Where(player => CanPlayerOccupySlot(player, slot.ExactPosition))
                .OrderByDescending(player => GetSlotFitScore(player, slot.ExactPosition))
                .ThenByDescending(GetOverallRating)
                .ThenBy(player => player.SquadNumber)
                .ThenBy(player => player.Name)
                .FirstOrDefault();

            if (selectedPlayer is null)
            {
                continue;
            }

            orderedPlayers.Add(selectedPlayer);
            remainingPlayers.Remove(selectedPlayer);
        }

        orderedPlayers.AddRange(remainingPlayers);
        return orderedPlayers;
    }

    private static int GetSlotFitScore(Player player, string exactPosition)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);

        if (player.PreferredPosition == normalizedSlot)
        {
            return 1000;
        }

        if (player.SecondaryPositions.Contains(normalizedSlot))
        {
            return 1000;
        }

        return player.Position == GetGenericPositionForSlot(normalizedSlot) ? 600 : 100;
    }

    private static bool CanPlayerOccupySlot(Player player, string exactPosition)
    {
        return PositionSuitabilityService.NormalizeExactPosition(exactPosition) != "GK" ||
            PositionSuitabilityService.IsGoalkeeperCapable(player);
    }

    private static Position GetGenericPositionForSlot(string exactPosition)
    {
        return exactPosition switch
        {
            "GK" => Position.Goalkeeper,
            "LB" or "CB" or "RB" => Position.Defender,
            "CDM" or "CM" or "CAM" => Position.Midfielder,
            "LW" or "RW" or "ST" => Position.Forward,
            _ => Position.Midfielder
        };
    }

    private sealed record RatingVisual(int Rating, string Foreground, string Background);

    private sealed record PlayerStatRow(string Label, string Value);

    private enum DragSource
    {
        StartingXi,
        Substitute
    }

    private sealed record DraggedPlayerInfo(Player Player, DragSource Source, int SourceIndex);

    private sealed class BenchPlayerCard
    {
        public Player Player { get; init; } = new();
        public string Name { get; init; } = string.Empty;
        public string ShirtNumberText { get; init; } = string.Empty;
        public string PlayerImagePath { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string OverallText { get; init; } = string.Empty;
        public int OverallRating { get; init; }
        public string GrowthText { get; init; } = string.Empty;
        public double Stamina { get; init; }
        public string StaminaBrush { get; init; } = "#2FA84F";
        public string BenchFormBadgeText { get; init; } = string.Empty;
        public string BenchFormBadgeBackground { get; init; } = "#E1E5EA";
        public string BenchFormBadgeForeground { get; init; } = "#465364";
        public string CardBackground { get; init; } = "White";
        public string CardBorderBrush { get; init; } = "#D6DFEA";
        public string TextForeground { get; init; } = "#102033";
        public string PositionBackground { get; init; } = "#E7EEF8";
        public string PositionForeground { get; init; } = "#102033";
        public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
        public IReadOnlyList<PlayerCardStatusBadge> CardStatusBadges { get; init; } = [];
    }
}
