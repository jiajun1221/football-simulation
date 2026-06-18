using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class PreMatchView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly TacticalInsightService _tacticalInsightService = new();
    private readonly SaveGameService _saveGameService = new();
    private readonly FormationPresetService _formationPresetService = new();
    private const double PitchCardWidth = 128;
    private const double PitchCardHeight = 70;

    private Player? _selectedStarter;
    private string? _selectedPositionFilter;
    private List<Player> _pitchSlots = [];
    private Point _dragStartPoint;
    private bool _isDraggingPlayer;
    private bool _isLoadingSetup;

    private sealed record PitchSlotAssignment(Player Player, PitchPosition Position);

    public PreMatchView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadPreMatch();
    }

    private void LoadPreMatch()
    {
        if (_state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return;
        }

        _isLoadingSetup = true;

        FixtureTextBlock.Text = $"{CompetitionDisplayService.GetName(_state.CurrentFixture.Competition)} - {GetFixtureRoundText(_state.CurrentFixture)} - {_state.CurrentFixture.HomeTeam.Name} vs {_state.CurrentFixture.AwayTeam.Name}";
        var goalkeeperValidation = ReconcileUnavailablePlayers(_state.SelectedTeam);
        ShowGoalkeeperWarningIfNeeded(goalkeeperValidation);
        ValidateLineupAssignments("PrepareMatchLoaded");
        LoadFormationSelector(_state.SelectedTeam);
        LoadTactics(_state.SelectedTeam.Tactics);
        LoadSavedFormationSelector(_state.SelectedTeam);
        InitializePitchSlots();
        RefreshSubstitutes();
        RenderPitch();

        _isLoadingSetup = false;
        RefreshTacticalInsight();
    }

    private void InitializePitchSlots()
    {
        var team = _state.SelectedTeam;
        if (team is null)
        {
            _pitchSlots = [];
            return;
        }

        ValidateLineupAssignments("InitializePitchSlots");
        var roster = GetDistinctRoster(team);
        if (team.Players.Count == 11)
        {
            _pitchSlots = team.Players
                .DistinctBy(CreateRosterKey)
                .ToList();

            if (_pitchSlots.Count < 11)
            {
                var pitchKeys = _pitchSlots
                    .Select(CreateRosterKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _pitchSlots.AddRange(OrderPlayersForPitch(
                    roster.Where(player => !pitchKeys.Contains(CreateRosterKey(player))),
                    team.Formation).Take(11 - _pitchSlots.Count));
            }
        }
        else
        {
            _pitchSlots = OrderPlayersForPitch(roster, team.Formation)
                .Take(11)
                .ToList();
        }

        ApplyPitchSlotsToTeam();
        AssignFormationPositions();
        ValidateLineupAssignments("InitializePitchSlotsApplied");
    }

    private static string GetFixtureRoundText(Fixture fixture)
    {
        return string.IsNullOrWhiteSpace(fixture.RoundName)
            ? $"Round {fixture.RoundNumber}"
            : fixture.RoundName;
    }

    private LineupValidationResult ReconcileUnavailablePlayers(Team team)
    {
        var formationSlots = _formationLayoutService.GetPositions(team.Formation);
        var wasRepaired = false;

        for (var index = 0; index < team.Players.Count; index++)
        {
            var player = team.Players[index];
            if (IsAvailableForSelection(player))
            {
                continue;
            }

            var slot = index < formationSlots.Count
                ? formationSlots[index].ExactPosition
                : player.AssignedPosition;
            var replacement = ChooseBenchReplacementForSlot(team, slot);
            if (replacement is null)
            {
                team.Players.RemoveAt(index);
                index--;
            }
            else
            {
                team.Substitutes.Remove(replacement);
                team.Players[index] = replacement;
                replacement.IsStarter = true;
                replacement.IsOnPitch = true;
                PositionSuitabilityService.EnsurePositionMetadata(replacement, slot);
            }

            if (!team.Substitutes.Contains(player))
            {
                team.Substitutes.Add(player);
            }

            player.IsStarter = false;
            player.IsOnPitch = false;
            wasRepaired = true;
        }

        while (team.Players.Count < 11)
        {
            var slot = team.Players.Count < formationSlots.Count
                ? formationSlots[team.Players.Count].ExactPosition
                : string.Empty;
            var replacement = ChooseBenchReplacementForSlot(team, slot);
            if (replacement is null)
            {
                break;
            }

            team.Substitutes.Remove(replacement);
            PositionSuitabilityService.EnsurePositionMetadata(replacement, slot);
            team.Players.Add(replacement);
            replacement.IsStarter = true;
            replacement.IsOnPitch = true;
            wasRepaired = true;
        }

        var goalkeeperResult = LineupValidationService.RepairGoalkeeperSlot(team);
        if (!goalkeeperResult.IsValid)
        {
            return goalkeeperResult;
        }

        return wasRepaired || goalkeeperResult.WasRepaired
            ? LineupValidationResult.Repaired()
            : LineupValidationResult.Valid();
    }

    private void LoadFormationSelector(Team team)
    {
        FormationComboBox.DisplayMemberPath = nameof(FormationOption.Name);
        FormationComboBox.SelectedValuePath = nameof(FormationOption.Name);
        FormationComboBox.ItemsSource = CreateFormationOptionsView();
        FormationComboBox.SelectedValue = FormationCatalogService.NormalizeFormationName(team.Formation);
    }

    private static ICollectionView CreateFormationOptionsView()
    {
        var view = CollectionViewSource.GetDefaultView(FormationCatalogService.GetFormations().ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FormationOption.Category)));
        return view;
    }

    private string GetSelectedFormation(Team team)
    {
        return FormationComboBox.SelectedValue as string
            ?? FormationCatalogService.NormalizeFormationName(team.Formation);
    }

    private void LoadTactics(TeamTactics tactics)
    {
        TacticalSettingsPanel.LoadTactics(tactics);
    }

    private void LoadSavedFormationSelector(Team team)
    {
        SavedFormationComboBox.ItemsSource = team.FormationPresets
            .OrderByDescending(preset => preset.UpdatedAt)
            .ToList();
        SavedFormationComboBox.SelectedIndex = team.FormationPresets.Count == 0 ? -1 : 0;
        var hasPresets = team.FormationPresets.Count > 0;
        SavedFormationComboBox.IsEnabled = hasPresets;
        LoadSavedFormationButton.IsEnabled = hasPresets;
        LoadSavedFormationButton.ToolTip = hasPresets
            ? "Load the selected formation preset"
            : "Save formation presets from My Squad.";
    }

    private void ToggleSavedFormationsButton_Click(object sender, RoutedEventArgs e)
    {
        var isExpanded = SavedFormationsPanel.Visibility == Visibility.Visible;
        SavedFormationsPanel.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        ToggleSavedFormationsButton.Content = isExpanded ? "Formation ▼" : "Formation ▲";
    }

    private void RenderPitch()
    {
        if (_state.SelectedTeam is null || PitchCanvas.ActualWidth <= 0 || PitchCanvas.ActualHeight <= 0)
        {
            return;
        }

        PitchCanvas.Children.Clear();

        var formation = GetSelectedFormation(_state.SelectedTeam);
        var positions = _formationLayoutService.GetPositions(formation);
        var assignments = CreatePitchSlotAssignments(_pitchSlots, positions);

        foreach (var assignment in assignments)
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

        positions ??= _formationLayoutService.GetPositions(GetSelectedFormation(_state.SelectedTeam));

        for (var index = 0; index < _pitchSlots.Count && index < positions.Count; index++)
        {
            if (CanPlayerOccupySlot(_pitchSlots[index], positions[index].ExactPosition))
            {
                PositionSuitabilityService.EnsurePositionMetadata(_pitchSlots[index], positions[index].ExactPosition);
            }
        }
    }

    private void ValidateLineupAssignments(string reason)
    {
        var team = _state.SelectedTeam;
        if (team is null)
        {
            return;
        }

        var positions = _formationLayoutService.GetPositions(FormationCatalogService.NormalizeFormationName(team.Formation));
        var roster = GetDistinctRoster(team)
            .Where(IsAvailableForSelection)
            .ToList();
        var originalPitchKeys = team.Players
            .Select(CreateRosterKey)
            .ToList();
        var assignedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = new Player?[positions.Count];

        for (var index = 0; index < positions.Count && index < team.Players.Count; index++)
        {
            var player = team.Players[index];
            var playerKey = CreateRosterKey(player);
            if (!IsAvailableForSelection(player) ||
                assignedKeys.Contains(playerKey) ||
                !CanAssignPlayerToSlot(player, positions[index].ExactPosition))
            {
                continue;
            }

            assignments[index] = player;
            assignedKeys.Add(playerKey);
        }

        var remainingPlayers = roster
            .Where(player => !assignedKeys.Contains(CreateRosterKey(player)))
            .ToList();

        for (var index = 0; index < positions.Count; index++)
        {
            if (assignments[index] is not null)
            {
                continue;
            }

            var slot = positions[index].ExactPosition;
            var selectedPlayer = SelectPlayerForFormationSlot(remainingPlayers, slot);
            if (selectedPlayer is null)
            {
                Debug.WriteLine($"[LINEUP WARNING] Reason={reason}; Method={nameof(ValidateLineupAssignments)}; No valid player for slot {slot}.");
                continue;
            }

            assignments[index] = selectedPlayer;
            remainingPlayers.Remove(selectedPlayer);
            assignedKeys.Add(CreateRosterKey(selectedPlayer));
        }

        var validatedAssignments = assignments
            .Select((player, index) => new { Player = player, Position = positions[index], Index = index })
            .Where(assignment => assignment.Player is not null)
            .ToList();

        foreach (var assignment in validatedAssignments)
        {
            var player = assignment.Player!;
            var oldIndex = originalPitchKeys.FindIndex(key => string.Equals(key, CreateRosterKey(player), StringComparison.OrdinalIgnoreCase));
            if (oldIndex != assignment.Index)
            {
                LogLineupChange(
                    reason,
                    nameof(ValidateLineupAssignments),
                    player,
                    oldIndex >= 0 && oldIndex < positions.Count ? positions[oldIndex].ExactPosition : "Bench",
                    assignment.Position.ExactPosition);
            }

            PositionSuitabilityService.EnsurePositionMetadata(player, assignment.Position.ExactPosition);
        }

        var validatedPitch = validatedAssignments
            .Select(assignment => assignment.Player!)
            .ToList();

        var pitchKeys = validatedPitch
            .Select(CreateRosterKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bench = GetDistinctRoster(team)
            .Where(player => !pitchKeys.Contains(CreateRosterKey(player)))
            .ToList();

        foreach (var player in validatedPitch)
        {
            player.IsStarter = true;
            player.IsOnPitch = true;
        }

        foreach (var player in bench)
        {
            player.IsStarter = false;
            player.IsOnPitch = false;
        }

        team.Players = validatedPitch;
        team.Substitutes = bench;
        _pitchSlots = validatedPitch;
    }

    private static void LogLineupChange(string reason, string method, Player player, string oldSlot, string newSlot)
    {
        Debug.WriteLine(
            $"[LINEUP CHANGE] Reason={reason}; Method={method}; Player={player.Name}; Old Slot={oldSlot}; New Slot={newSlot}");
    }

    private static List<PitchSlotAssignment> CreatePitchSlotAssignments(
        IReadOnlyList<Player> players,
        IReadOnlyList<PitchPosition> positions)
    {
        return positions
            .Select((position, index) => index < players.Count
                ? new PitchSlotAssignment(players[index], position)
                : null)
            .Where(assignment => assignment is not null)
            .Cast<PitchSlotAssignment>()
            .ToList();
    }

    private void ApplyPitchSlotsToTeam()
    {
        var team = _state.SelectedTeam;
        if (team is null)
        {
            return;
        }

        var pitchPlayers = _pitchSlots
            .Where(player => player is not null)
            .DistinctBy(CreateRosterKey)
            .Take(11)
            .ToList();
        var pitchKeys = pitchPlayers
            .Select(CreateRosterKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingPlayers = team.Players
            .Concat(team.Substitutes)
            .Where(player => !pitchKeys.Contains(CreateRosterKey(player)))
            .DistinctBy(CreateRosterKey)
            .ToList();

        foreach (var player in pitchPlayers)
        {
            player.IsStarter = true;
            player.IsOnPitch = true;
        }

        foreach (var player in remainingPlayers)
        {
            player.IsStarter = false;
            player.IsOnPitch = false;
        }

        _pitchSlots = pitchPlayers;
        team.Players = pitchPlayers;
        team.Substitutes = remainingPlayers;
    }

    private static List<Player> GetDistinctRoster(Team team)
    {
        return team.Players
            .Concat(team.Substitutes)
            .DistinctBy(CreateRosterKey)
            .ToList();
    }

    private static string CreateRosterKey(Player player)
    {
        return PlayerRosterKeyService.CreateKey(player);
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
            AllowDrop = IsAvailableForSelection(player),
            IsEnabled = IsAvailableForSelection(player),
            Opacity = IsAvailableForSelection(player) ? 1.0 : 0.55
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
        if (CanPlayerOccupySlot(player, displayedPosition))
        {
            PositionSuitabilityService.EnsurePositionMetadata(player, displayedPosition);
        }
        else
        {
            PositionSuitabilityService.EnsurePositionMetadata(player);
            Debug.WriteLine(
                $"[LINEUP WARNING] Reason=RenderPitch; Method={nameof(CreatePitchPlayerCard)}; Player={player.Name}; Invalid Slot={displayedPosition}");
        }

        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var isOutOfPosition = PositionSuitabilityService.IsOutOfPosition(player);
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(player);
        var ratingVisual = GetRatingVisual(player, suitability);
        var teamColors = TeamColorService.GetPalette(_state.SelectedTeam);
        var cardBackground = player.IsInjured ? "#FFE4E4" : teamColors.PrimaryColor;
        var cardBorder = player.IsInjured
            ? "#D92D20"
            : player == _selectedStarter
                ? teamColors.SelectedGlowColor
                : isOutOfPosition
                    ? ratingVisual.Foreground
                    : teamColors.BorderColor;
        var textForeground = player.IsInjured ? "#8F1F1F" : teamColors.TextColor;
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        var fatigueBadge = CreateFatigueBadge(player);

        return new PitchPlayerCard
        {
            Player = player,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            ShirtNumberValue = player.SquadNumber > 0 ? player.SquadNumber.ToString() : string.Empty,
            PlayerImagePath = GetPlayerImagePath(player),
            PlayerName = player.Name,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            PositionText = displayedPosition,
            OverallText = $"OVR {ratingVisual.Rating}",
            OverallForeground = player.IsInjured ? ratingVisual.Foreground : textForeground,
            TextForeground = textForeground,
            MutedForeground = textForeground,
            PositionBackground = player.IsInjured ? "#FFD1D1" : teamColors.SecondaryColor,
            PositionForeground = TeamColorService.GetReadableTextColor(player.IsInjured ? "#FFD1D1" : teamColors.SecondaryColor),
            GrowthText = PlayerGrowthDisplayHelper.CreateGrowthText(player),
            Stamina = GetStaminaPercentage(player),
            StaminaBrush = GetStaminaBrush(player),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
            FatigueWarningText = fatigueBadge.Text,
            FatigueWarningTooltip = fatigueBadge.Tooltip,
            FatigueWarningBadgeBackground = fatigueBadge.Background,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
            CardBackground = cardBackground,
            CardBorderBrush = cardBorder,
            CardBorderThickness = player == _selectedStarter ? new Thickness(3) : new Thickness(1)
        };
    }

    private void PlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Player player } && IsAvailableForSelection(player))
        {
            _selectedStarter = player;
            _selectedPositionFilter = (sender as FrameworkElement)?.DataContext is PitchPlayerCard card
                ? PositionSuitabilityService.NormalizeExactPosition(card.PositionText)
                : null;
            UpdateSelectedPlayerDetails();
            RefreshSubstitutes();
            RenderPitch();
        }
    }

    private void PitchCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinPitchPlayerButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ClearPitchSelection();
    }

    private void ClearSubstituteFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ClearPitchSelection();
    }

    private void ClearPitchSelection()
    {
        if (_selectedStarter is null && string.IsNullOrWhiteSpace(_selectedPositionFilter))
        {
            return;
        }

        _selectedStarter = null;
        _selectedPositionFilter = null;
        UpdateSelectedPlayerDetails();
        RefreshSubstitutes();
        RenderPitch();
    }

    private void FormationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSetup)
        {
            return;
        }

        if (_state.SelectedTeam is not null && FormationComboBox.SelectedValue is string formation)
        {
            _state.SelectedTeam.Formation = formation;
            _pitchSlots = OrderPlayersForPitch(GetDistinctRoster(_state.SelectedTeam), formation)
                .Take(11)
                .ToList();
            ApplyPitchSlotsToTeam();
            AssignFormationPositions();
            ValidateLineupAssignments("FormationChanged");
            SaveSetup(_state.SelectedTeam);
        }

        RenderPitch();
        RefreshTacticalInsight();
    }

    private void TacticalSettingsPanel_TacticsChanged(object? sender, EventArgs e)
    {
        RefreshTacticalInsight();
        if (!_isLoadingSetup && _state.SelectedTeam is not null)
        {
            SaveSetup(_state.SelectedTeam);
        }
    }

    private void LoadSavedFormationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is null || SavedFormationComboBox.SelectedItem is not FormationPreset preset)
        {
            return;
        }

        var result = _formationPresetService.ApplyPreset(_state.SelectedTeam, preset);
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Saved Formations", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isLoadingSetup = true;
        LoadFormationSelector(_state.SelectedTeam);
        LoadTactics(_state.SelectedTeam.Tactics);
        InitializePitchSlots();
        RefreshSubstitutes();
        RenderPitch();
        _isLoadingSetup = false;
        RefreshTacticalInsight();
        SaveSetup(_state.SelectedTeam);

        if (result.Warnings.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, result.Warnings), "Preset Loaded With Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExecuteSwap(Player starter, Player substitute)
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        if (!IsAvailableForSelection(starter) || !IsAvailableForSelection(substitute))
        {
            MessageBox.Show("Injured or suspended players cannot be selected.");
            return;
        }

        var starterIndex = _pitchSlots.IndexOf(starter);
        if (starterIndex < 0)
        {
            return;
        }

        var incomingAssignedPosition = GetDisplayedSlotForStarter(starter);
        if (IsHardBlockedManualSubstituteSlot(substitute, incomingAssignedPosition))
        {
            MessageBox.Show($"{substitute.Name} cannot cover {incomingAssignedPosition}. Choose a player who can play {incomingAssignedPosition}.");
            return;
        }

        _pitchSlots[starterIndex] = substitute;
        starter.AssignedPosition = starter.PreferredPosition;
        PositionSuitabilityService.EnsurePositionMetadata(substitute, incomingAssignedPosition);
        ApplyPitchSlotsToTeam();

        LogLineupChange("SubstituteSwap", nameof(ExecuteSwap), substitute, "Bench", incomingAssignedPosition);
        LogLineupChange("SubstituteSwap", nameof(ExecuteSwap), starter, incomingAssignedPosition, "Bench");
        Debug.WriteLine(
            $"[PreMatchDrag] Swapped Sub<->StartingXI: {substitute.Name} -> slot {_pitchSlots.IndexOf(substitute)} ({substitute.AssignedPosition}); " +
            $"{starter.Name} -> substitute index {_state.SelectedTeam.Substitutes.IndexOf(starter)}");
        _selectedStarter = substitute;
        RefreshSubstitutes();
        UpdateSelectedPlayerDetails();
        RenderPitch();
        RefreshTacticalInsight();
        SaveSetup(_state.SelectedTeam);
    }

    private string GetDisplayedSlotForStarter(Player starter)
    {
        if (_state.SelectedTeam is null)
        {
            PositionSuitabilityService.EnsurePositionMetadata(starter);
            return starter.AssignedPosition;
        }

        var starterIndex = _pitchSlots.IndexOf(starter);
        var positions = _formationLayoutService.GetPositions(GetSelectedFormation(_state.SelectedTeam));
        if (starterIndex >= 0 && starterIndex < positions.Count)
        {
            return positions[starterIndex].ExactPosition;
        }

        PositionSuitabilityService.EnsurePositionMetadata(starter);
        return starter.AssignedPosition;
    }

    private static bool IsHardBlockedManualSubstituteSlot(Player substitute, string exactPosition)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        return normalizedSlot == "GK"
            ? !PositionSuitabilityService.IsGoalkeeperCapable(substitute)
            : PositionSuitabilityService.IsGoalkeeperCapable(substitute);
    }

    private void RefreshSubstitutes()
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        var normalizedFilter = PositionSuitabilityService.NormalizeExactPosition(_selectedPositionFilter);
        var pitchKeys = _pitchSlots
            .Select(CreateRosterKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var substitutes = _state.SelectedTeam.Substitutes
            .Where(player => !pitchKeys.Contains(CreateRosterKey(player)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(normalizedFilter))
        {
            substitutes = substitutes
                .OrderByDescending(player => PositionCompatibilityService.IsReasonableFit(player, normalizedFilter))
                .ThenByDescending(player => PositionCompatibilityService.GetCompatibilityScore(player, normalizedFilter))
                .ThenBy(player => IsAvailableForSelection(player) ? 0 : 1)
                .ThenByDescending(GetOverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .ThenBy(player => player.Name)
                .ToList();
        }

        var benchCards = substitutes
            .Select(CreateBenchPlayerCard)
            .ToList();

        SubstituteListBox.ItemsSource = null;
        SubstituteListBox.ItemsSource = benchCards;
        SubstituteListBox.IsEnabled = benchCards.Count > 0;
        UpdateSubstituteFilterLabel(normalizedFilter);
    }

    private void UpdateSubstituteFilterLabel(string normalizedFilter)
    {
        if (string.IsNullOrWhiteSpace(normalizedFilter))
        {
            SubstituteFilterTextBlock.Text = "All players";
            ClearSubstituteFilterButton.Visibility = Visibility.Collapsed;
            return;
        }

        SubstituteFilterTextBlock.Text = $"Filter: {normalizedFilter}";
        ClearSubstituteFilterButton.Visibility = Visibility.Visible;
    }

    private BenchPlayerCard CreateBenchPlayerCard(Player player)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var teamColors = TeamColorService.GetPalette(_state.SelectedTeam);
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        var isAvailable = IsAvailableForSelection(player);
        var fatigueBadge = CreateFatigueBadge(player);

        return new BenchPlayerCard
        {
            Player = player,
            Name = player.Name,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
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
            CardBorderBrush = isAvailable ? teamColors.BorderColor : "#7F1D1D",
            CardBorderThickness = isAvailable ? new Thickness(1) : new Thickness(2),
            CardOpacity = isAvailable ? 1.0 : 0.68,
            CardCursor = isAvailable ? Cursors.Hand : Cursors.No,
            CanInteract = isAvailable,
            TextForeground = teamColors.TextColor,
            PositionBackground = teamColors.SecondaryColor,
            PositionForeground = TeamColorService.GetReadableTextColor(teamColors.SecondaryColor),
            StatusText = CreateUnavailableStatusText(player),
            StatusBadgeBackground = player.IsSuspended || player.IsSentOff ? "#7F1D1D" : "#B91C1C",
            Tooltip = CreateUnavailableTooltip(player),
            FatigueWarningText = fatigueBadge.Text,
            FatigueWarningTooltip = fatigueBadge.Tooltip,
            FatigueWarningBadgeBackground = fatigueBadge.Background,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits)
        };
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
        var form = PlayerFormBadgeHelper.Create(_selectedStarter.FormStatus);
        var ratingVisual = GetRatingVisual(_selectedStarter, suitability);

        SelectedPlayerEmptyTextBlock.Visibility = Visibility.Collapsed;
        SelectedPlayerCard.Visibility = Visibility.Visible;
        SelectedPlayerCard.DataContext = _selectedStarter;

        SelectedPlayerImage.Source = CreateImageSource(GetPlayerImagePath(_selectedStarter));
        var selectedNationality = PlayerNationalityDisplayService.Resolve(_selectedStarter);
        SelectedPlayerFlagImage.FlagSource = selectedNationality.FlagImagePath;
        SelectedPlayerFlagImage.ToolTip = selectedNationality.Name;
        SelectedPlayerNumberTextBlock.Text = _selectedStarter.SquadNumber > 0 ? $"#{_selectedStarter.SquadNumber}" : "No squad number";
        SelectedPlayerNameTextBlock.Text = _selectedStarter.Name;
        SelectedPlayerPositionChip.Text = GetPlayablePositionsText(_selectedStarter);
        SelectedPlayerPreferredChip.Text = _state.SelectedTeam?.Name ?? GetPositionGroupText(_selectedStarter);
        SelectedPlayerOvrBadgeTextBlock.Text = ratingVisual.Rating.ToString();
        SelectedPlayerOvrBadgeBorder.Background = ToBrush(ratingVisual.Background);
        SelectedPlayerOvrBadgeBorder.ToolTip = PlayerGrowthDisplayHelper.CreateGrowthText(_selectedStarter);
        SelectedPlayerFormChip.Text = form.Text;
        SelectedPlayerFormChip.Foreground = ToBrush(form.Foreground);
        SelectedPlayerFormChipBorder.Background = ToBrush(form.Background);
        var selectedTraitBadges = PlayerTraitBadgeHelper.Create(_selectedStarter.Traits);
        SelectedPlayerTraitItemsControl.ItemsSource = selectedTraitBadges;
        SelectedPlayerTraitItemsControl.Visibility = selectedTraitBadges.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SelectedPlayerInjuryChip.Text = _selectedStarter.IsInjured
            ? $"{_selectedStarter.InjuryType} | {(_selectedStarter.IsSeasonEndingInjury ? "Season" : $"{Math.Max(1, _selectedStarter.InjuryRecoveryMatches)} matches")}"
            : "Available";
        var useDarkInjuryChip = ThemeManager.CurrentTheme == AppTheme.Dark;
        SelectedPlayerInjuryChip.Foreground = ToBrush(_selectedStarter.IsInjured
            ? (useDarkInjuryChip ? "#FCA5A5" : "#8F1F1F")
            : (useDarkInjuryChip ? "#86EFAC" : "#236B39"));
        SelectedPlayerInjuryChipBorder.Background = ToBrush(_selectedStarter.IsInjured
            ? (useDarkInjuryChip ? ThemeManager.GetBrushHex("FeedAttackBackground", "#3B1115") : "#FFD1D1")
            : (useDarkInjuryChip ? ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#111827") : "#D9F1E1"));
        var staminaText = $"{GetStaminaPercentage(_selectedStarter)}% stamina";
        SelectedPlayerStaminaTextBlock.Text = staminaText;
        SelectedPlayerStaminaPercentTextBlock.Text = $"{GetStaminaPercentage(_selectedStarter)}%";
        SelectedPlayerStaminaFill.Foreground = ToBrush(GetStaminaBrush(_selectedStarter));

        SelectedPlayerAttackTextBlock.Text = _selectedStarter.Attack.ToString();
        SelectedPlayerDefenseTextBlock.Text = _selectedStarter.Defense.ToString();
        SelectedPlayerPassingTextBlock.Text = _selectedStarter.Passing.ToString();
        SelectedPlayerFinishingTextBlock.Text = _selectedStarter.Finishing.ToString();
        SelectedPlayerCard.ToolTip =
            $"{_selectedStarter.Name}{Environment.NewLine}" +
            $"{_selectedStarter.AssignedPosition} | OVR {ratingVisual.Rating} | {staminaText}{Environment.NewLine}" +
            $"Attack {_selectedStarter.Attack}  Defense {_selectedStarter.Defense}{Environment.NewLine}" +
            $"Passing {_selectedStarter.Passing}  Finishing {_selectedStarter.Finishing}";
    }

    private string GetRecentPlayerFormText(Player player)
    {
        if (_state.League is null)
        {
            return "No recent match data yet";
        }

        var ratings = _state.League.Fixtures
            .Where(fixture => fixture.IsPlayed && fixture.Result is not null)
            .OrderByDescending(fixture => fixture.RoundNumber)
            .SelectMany(fixture => fixture.Result!.PlayerPerformances)
            .Where(performance => performance.PlayerName == player.Name)
            .Take(5)
            .Select(performance => performance.Rating.ToString("0.0"))
            .ToList();

        return ratings.Count == 0
            ? "No recent match data yet"
            : string.Join(" / ", ratings);
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
        var insight = _tacticalInsightService.GenerateInsight(_state.SelectedTeam, opponent, GetFixtureRestGapDays());

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
        return PlayerOverallCalculator.CalculateOverall(player);
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
            : new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute));
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

    private static string GetPlayablePositionsText(Player player)
    {
        var positions = new List<string>();
        if (!string.IsNullOrWhiteSpace(player.PreferredPosition))
        {
            positions.Add(player.PreferredPosition);
        }

        positions.AddRange(player.SecondaryPositions.Where(position => !string.IsNullOrWhiteSpace(position)));

        return positions.Count == 0
            ? player.Position.ToString()
            : string.Join("/", positions.Distinct(StringComparer.OrdinalIgnoreCase));
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

        if (!IsAvailableForSelection(player))
        {
            return;
        }

        _isDraggingPlayer = true;
        Debug.WriteLine($"[PreMatchDrag] Start: {player.Name}; source={dragSource}; slot/index={sourceIndex}");

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
            $"[PreMatchDrag] Drop: dragged={draggedPlayer.Player.Name}; source={draggedPlayer.Source}; source slot/index={draggedPlayer.SourceIndex}; " +
            $"target={targetPlayer.Name}; target slot={_pitchSlots.IndexOf(targetPlayer)}");

        if (draggedPlayer.Source == DragSource.StartingXi)
        {
            SwapPitchPlayers(draggedPlayer.Player, targetPlayer);
        }
        else
        {
            ExecuteSwap(targetPlayer, draggedPlayer.Player);
        }

        e.Handled = true;
    }

    private void SubstituteCard_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            ApplySubstituteDropTargetStyle(border, CanDropOnSubstituteCard(sender, e));
        }
    }

    private void SubstituteCard_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = CanDropOnSubstituteCard(sender, e);
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
        if (draggedPlayer is null || !benchCard.CanInteract)
        {
            return;
        }

        Debug.WriteLine(
            $"[PreMatchDrag] Drop: dragged={draggedPlayer.Player.Name}; source={draggedPlayer.Source}; source slot/index={draggedPlayer.SourceIndex}; " +
            $"target={benchCard.Player.Name}; target substitute index={_state.SelectedTeam?.Substitutes.IndexOf(benchCard.Player) ?? -1}");

        if (draggedPlayer.Source == DragSource.StartingXi)
        {
            ExecuteSwap(draggedPlayer.Player, benchCard.Player);
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
        var firstSubstitute = _state.SelectedTeam?.Substitutes.FirstOrDefault(IsAvailableForSelection);
        if (draggedPlayer is null || draggedPlayer.Source != DragSource.StartingXi || firstSubstitute is null)
        {
            return;
        }

        ExecuteSwap(draggedPlayer.Player, firstSubstitute);
        e.Handled = true;
    }

    private static bool CanDropOnSubstituteCard(object sender, DragEventArgs e)
    {
        return GetDraggedPlayer(e) is not null &&
            sender is FrameworkElement { DataContext: BenchPlayerCard { CanInteract: true } };
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

    private static bool IsWithinPitchPlayerButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button { DataContext: PitchPlayerCard })
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void SwapPitchPlayers(Player draggedPlayer, Player targetPlayer)
    {
        if (_state.SelectedTeam is null || draggedPlayer == targetPlayer)
        {
            return;
        }

        if (!IsAvailableForSelection(draggedPlayer) || !IsAvailableForSelection(targetPlayer))
        {
            return;
        }

        var draggedIndex = _pitchSlots.IndexOf(draggedPlayer);
        var targetIndex = _pitchSlots.IndexOf(targetPlayer);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            Debug.WriteLine(
                $"[PreMatchDrag] Swap failed: {draggedPlayer.Name} index={draggedIndex}; {targetPlayer.Name} index={targetIndex}");
            return;
        }

        var positions = _formationLayoutService.GetPositions(GetSelectedFormation(_state.SelectedTeam));
        var draggedTargetSlot = targetIndex < positions.Count ? positions[targetIndex].ExactPosition : string.Empty;
        var targetTargetSlot = draggedIndex < positions.Count ? positions[draggedIndex].ExactPosition : string.Empty;
        var hardBlockMessage = CreateHardSwapBlockMessage(draggedPlayer, draggedTargetSlot, targetPlayer, targetTargetSlot);
        if (!string.IsNullOrWhiteSpace(hardBlockMessage))
        {
            MessageBox.Show(hardBlockMessage);
            return;
        }
        (_pitchSlots[draggedIndex], _pitchSlots[targetIndex]) = (_pitchSlots[targetIndex], _pitchSlots[draggedIndex]);
        ApplyPitchSlotsToTeam();
        AssignFormationPositions();

        Debug.WriteLine(
            $"[PreMatchDrag] Swapped StartingXI: {draggedPlayer.Name} -> slot {targetIndex} ({draggedPlayer.AssignedPosition}); " +
            $"{targetPlayer.Name} -> slot {draggedIndex} ({targetPlayer.AssignedPosition})");

        _selectedStarter = draggedPlayer;
        UpdateSelectedPlayerDetails();
        RenderPitch();
        RefreshTacticalInsight();
        SaveSetup(_state.SelectedTeam);
    }

    private void SwapSubstitutes(Player draggedPlayer, Player targetPlayer)
    {
        if (_state.SelectedTeam is null || draggedPlayer == targetPlayer)
        {
            return;
        }

        if (!IsAvailableForSelection(draggedPlayer) || !IsAvailableForSelection(targetPlayer))
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
            $"[PreMatchDrag] Swapped substitutes: {draggedPlayer.Name} -> index {targetIndex}; {targetPlayer.Name} -> index {draggedIndex}");

        RefreshSubstitutes();
        SaveSetup(_state.SelectedTeam);
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

    private void StartFirstHalfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is null)
        {
            MessageBox.Show("No team has been selected.");
            return;
        }

        var goalkeeperValidation = ReconcileUnavailablePlayers(_state.SelectedTeam);
        if (!goalkeeperValidation.IsValid)
        {
            MessageBox.Show(goalkeeperValidation.Message ?? LineupValidationService.NoAvailableGoalkeeperMessage);
            return;
        }

        ApplyPitchSlotsToTeam();
        SaveSetup(_state.SelectedTeam);
        _navigate(new MatchLiveView(_state, _navigate, isSecondHalf: false));
    }

    private void TestPenaltyShootoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            MessageBox.Show("No fixture is available for penalty shootout practice.");
            return;
        }

        var goalkeeperValidation = ReconcileUnavailablePlayers(_state.SelectedTeam);
        if (!goalkeeperValidation.IsValid)
        {
            MessageBox.Show(goalkeeperValidation.Message ?? LineupValidationService.NoAvailableGoalkeeperMessage);
            return;
        }

        ApplyPitchSlotsToTeam();
        SaveSetup(_state.SelectedTeam);
        var previousMatch = _state.CurrentMatch;
        var previousSegment = _state.CurrentLiveMatchSegment;
        var fixture = _state.CurrentFixture;
        _state.CurrentMatch = new Match
        {
            HomeTeam = fixture.HomeTeam,
            AwayTeam = fixture.AwayTeam,
            HomeScore = 2,
            AwayScore = 2,
            CurrentMinute = 120,
            CurrentPhase = MatchPhase.PenaltyShootout
        };
        _state.CurrentLiveMatchSegment = LiveMatchSegment.ExtraTimeSecondHalf;

        _navigate(new PenaltyShootoutView(
            _state,
            _navigate,
            isPracticeMode: true,
            practiceReturnFactory: () => new PreMatchView(_state, _navigate),
            previousMatch,
            previousSegment));
    }

    private void BackToDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is not null)
        {
            SaveSetup(_state.SelectedTeam);
        }

        _navigate(new DashboardView(_state, _navigate));
    }

    private void SaveSetup(Team team)
    {
        if (FormationComboBox.SelectedValue is string formation)
        {
            team.Formation = formation;
        }

        TacticalSettingsPanel.ApplyTo(team.Tactics);
        PersistCurrentSaveSlot();
    }

    private void PersistCurrentSaveSlot()
    {
        if (_state.CurrentSaveSlotNumber is not int slotNumber ||
            _state.League is null ||
            _state.SelectedTeam is null)
        {
            return;
        }

        try
        {
            var saveData = SaveGameService.CreateSaveData(_state.League, _state.SelectedTeam, _state.TransferMarket);
            _saveGameService.SaveGame(slotNumber, saveData);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Debug.WriteLine($"[PreMatchSave] Could not persist lineup setup: {ex.Message}");
        }
    }

    private static Player? ChooseBenchReplacementForSlot(Team team, string exactPosition)
    {
        return team.Substitutes
            .Where(IsAvailableForSelection)
            .Where(player => CanPlayerOccupySlot(player, exactPosition))
            .Where(player => GetSlotFitScore(player, exactPosition) > PositionCompatibilityService.Impossible)
            .OrderByDescending(player => GetSlotFitScore(player, exactPosition))
            .ThenByDescending(GetOverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ThenBy(player => player.Name)
            .FirstOrDefault();
    }

    private static Player? SelectPlayerForFormationSlot(List<Player> remainingPlayers, string exactPosition)
    {
        var selectedPlayer = remainingPlayers
            .Where(player => CanPlayerOccupySlot(player, exactPosition))
            .OrderByDescending(player => GetSlotFitScore(player, exactPosition))
            .ThenByDescending(GetOverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ThenBy(player => player.Name)
            .FirstOrDefault();

        if (selectedPlayer is not null)
        {
            return selectedPlayer;
        }

        return remainingPlayers
            .Where(player => CanAssignPlayerToSlot(player, exactPosition))
            .OrderByDescending(player => GetFallbackSlotFitScore(player, exactPosition))
            .ThenByDescending(GetOverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ThenBy(player => player.Name)
            .FirstOrDefault();
    }

    private IEnumerable<Player> OrderPlayersForPitch(IEnumerable<Player> players, string formation)
    {
        var remainingPlayers = players.ToList();
        var orderedPlayers = new List<Player>();
        var formationSlots = _formationLayoutService.GetPositions(formation);

        foreach (var slot in formationSlots)
        {
            var selectedPlayer = remainingPlayers
                .Where(player => CanPlayerOccupySlot(player, slot.ExactPosition))
                .Where(player => GetSlotFitScore(player, slot.ExactPosition) > PositionCompatibilityService.Impossible)
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
        return PositionCompatibilityService.GetCompatibilityScore(player, exactPosition);
    }

    private static int GetFallbackSlotFitScore(Player player, string exactPosition)
    {
        var score = GetSlotFitScore(player, exactPosition);
        return score == PositionCompatibilityService.Impossible ? int.MinValue : score;
    }

    private static bool CanPlayerOccupySlot(Player player, string exactPosition)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        if (normalizedSlot == "GK")
        {
            return PositionSuitabilityService.IsGoalkeeperCapable(player);
        }

        return !PositionSuitabilityService.IsGoalkeeperCapable(player) &&
            PositionCompatibilityService.GetCompatibilityScore(player, normalizedSlot) > PositionCompatibilityService.Impossible;
    }

    private static bool CanAssignPlayerToSlot(Player player, string exactPosition)
    {
        return PositionCompatibilityService.CanOccupySlot(player, exactPosition, allowOutOfPosition: true);
    }

    private static string? CreateHardSwapBlockMessage(Player firstPlayer, string firstTargetSlot, Player secondPlayer, string secondTargetSlot)
    {
        var firstFailure = CreateHardPositionFailure(firstPlayer, firstTargetSlot);
        var secondFailure = CreateHardPositionFailure(secondPlayer, secondTargetSlot);
        var failures = new[] { firstFailure, secondFailure }
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => string.Join(Environment.NewLine, failures)
        };
    }

    private static string? CreateHardPositionFailure(Player player, string targetSlot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(targetSlot);
        if (string.IsNullOrWhiteSpace(normalizedSlot) ||
            PositionCompatibilityService.CanOccupySlot(player, normalizedSlot, allowOutOfPosition: true))
        {
            return null;
        }

        return $"{player.Name} cannot cover {normalizedSlot}. Choose a player who can play {normalizedSlot}.";
    }

    private static void ShowGoalkeeperWarningIfNeeded(LineupValidationResult result)
    {
        if (!result.IsValid)
        {
            MessageBox.Show(result.Message ?? LineupValidationService.NoAvailableGoalkeeperMessage);
        }
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

    private static bool IsAvailableForSelection(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }

    private static string CreateUnavailableStatusText(Player player)
    {
        if (player.IsSuspended || player.IsSentOff)
        {
            return "Banned";
        }

        return player.IsInjured ? "Injured" : string.Empty;
    }

    private FatigueBadgeResult CreateFatigueBadge(Player player)
    {
        return FatigueBadgeService.Evaluate(player, GetFixtureRestGapDays());
    }

    private int? GetFixtureRestGapDays()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return null;
        }

        var currentRound = GetFixtureCalendarRound(_state.CurrentFixture);
        var previousFixture = _state.League.Fixtures
            .Where(fixture => fixture.IsPlayed && IsTeamInFixture(fixture, _state.SelectedTeam))
            .Where(fixture => GetFixtureCalendarRound(fixture) < currentRound)
            .OrderByDescending(GetFixtureCalendarRound)
            .FirstOrDefault();

        return previousFixture is null
            ? null
            : Math.Max(1, currentRound - GetFixtureCalendarRound(previousFixture));
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateUnavailableTooltip(Player player)
    {
        if (player.IsSuspended || player.IsSentOff)
        {
            var matches = Math.Max(1, player.SuspendedMatches);
            var matchesText = matches == 1 ? "1 match" : $"{matches} matches";
            return string.Join(Environment.NewLine, "Banned", "Suspended", $"Recovery: {matchesText}");
        }

        if (!player.IsInjured)
        {
            return string.Empty;
        }

        var injuryType = string.IsNullOrWhiteSpace(player.InjuryType)
            ? "Injury"
            : player.InjuryType;
        var severity = player.InjurySeverity?.ToString() ?? "Unknown";
        var recovery = player.IsSeasonEndingInjury
            ? "season"
            : Math.Max(1, player.InjuryRecoveryMatches) == 1 ? "1 match" : $"{Math.Max(1, player.InjuryRecoveryMatches)} matches";

        return string.Join(
            Environment.NewLine,
            "Injured",
            injuryType,
            $"Severity: {severity}",
            $"Recovery: {recovery}");
    }

    private sealed record RatingVisual(int Rating, string Foreground, string Background);

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
        public string FlagImagePath { get; init; } = "/Assets/Flags/default.png";
        public string NationalityName { get; init; } = "Unknown nationality";
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
        public Thickness CardBorderThickness { get; init; } = new(1);
        public double CardOpacity { get; init; } = 1.0;
        public Cursor CardCursor { get; init; } = Cursors.Hand;
        public bool CanInteract { get; init; } = true;
        public string TextForeground { get; init; } = "#102033";
        public string PositionBackground { get; init; } = "#E7EEF8";
        public string PositionForeground { get; init; } = "#102033";
        public string StatusText { get; init; } = string.Empty;
        public string StatusBadgeBackground { get; init; } = "#B91C1C";
        public string Tooltip { get; init; } = string.Empty;
        public string FatigueWarningText { get; init; } = string.Empty;
        public string FatigueWarningTooltip { get; init; } = string.Empty;
        public string FatigueWarningBadgeBackground { get; init; } = "#F59E0B";
        public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
    }
}
