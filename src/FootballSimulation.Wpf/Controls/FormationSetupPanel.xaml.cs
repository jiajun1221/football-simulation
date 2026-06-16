using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;

namespace FootballSimulation.Wpf.Controls;

public partial class FormationSetupPanel : UserControl
{
    private const double PitchCardWidth = 116;
    private const double PitchCardHeight = 66;

    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly SquadSelectionService _squadSelectionService = new();
    private readonly FormationPresetService _formationPresetService = new();
    private Team? _team;
    private List<Player> _pitchSlots = [];
    private Player? _selectedStarter;
    private string? _selectedPositionFilter;
    private Point _dragStartPoint;
    private bool _isLoadingSetup;
    private bool _isRefreshingPresetControls;

    public FormationSetupPanel()
    {
        InitializeComponent();
        FormationComboBox.DisplayMemberPath = nameof(FormationOption.Name);
        FormationComboBox.SelectedValuePath = nameof(FormationOption.Name);
        FormationComboBox.ItemsSource = CreateFormationOptionsView();
    }

    public event EventHandler? SetupChanged;

    public bool ShowUnavailablePlayers { get; set; } = true;

    public void LoadTeam(Team team)
    {
        _team = team;
        _isLoadingSetup = true;
        FormationComboBox.SelectedValue = FormationCatalogService.NormalizeFormationName(team.Formation);
        TacticalSettingsPanel.LoadTactics(team.Tactics);
        _pitchSlots = team.Players.Count == 11 ? team.Players.ToList() : OrderPlayersForPitch(team.Players, team.Formation).ToList();
        ApplyPitchSlotsToTeam();
        AssignFormationPositions();
        RefreshAll();
        RefreshPresetControls();
        _isLoadingSetup = false;
    }

    public void ApplyCurrentSetup()
    {
        if (_team is null) { return; }
        if (FormationComboBox.SelectedValue is string formation) { _team.Formation = formation; }
        ApplyPitchSlotsToTeam();
        TacticalSettingsPanel.ApplyTo(_team.Tactics);
    }

    private void ApplyPitchSlotsToTeam()
    {
        if (_team is null)
        {
            return;
        }

        var pitchPlayers = _pitchSlots
            .Where(player => player is not null)
            .DistinctBy(CreateRosterKey)
            .ToList();
        var pitchKeys = pitchPlayers
            .Select(CreateRosterKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remainingPlayers = _team.Players
            .Concat(_team.Substitutes)
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
        _team.Players = pitchPlayers;
        _team.Substitutes = remainingPlayers;
    }

    private static string CreateRosterKey(Player player)
    {
        return !string.IsNullOrWhiteSpace(player.PlayerId)
            ? player.PlayerId
            : player.Name;
    }

    private static ICollectionView CreateFormationOptionsView()
    {
        var view = CollectionViewSource.GetDefaultView(FormationCatalogService.GetFormations().ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FormationOption.Category)));
        return view;
    }

    private string GetSelectedFormation()
    {
        return FormationComboBox.SelectedValue as string
            ?? FormationCatalogService.NormalizeFormationName(_team?.Formation);
    }

    public void RefreshEditor()
    {
        if (_team is not null) { LoadTeam(_team); }
    }

    private void RefreshAll()
    {
        RenderPitch();
        RefreshSubstitutes();
        RefreshUnavailablePlayers();
        RefreshPresetControls();
    }

    private void NotifyChanged()
    {
        if (_isLoadingSetup || _team is null) { return; }
        ApplyCurrentSetup();
        RefreshPresetControls();
        SetupChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPresetControls(string? preferredPresetId = null)
    {
        if (_team is null || PresetComboBox is null)
        {
            return;
        }

        var selectedId = preferredPresetId
            ?? (PresetComboBox.SelectedItem is FormationPreset selectedPreset ? selectedPreset.Id : string.Empty);
        var presets = _team.FormationPresets
            .OrderByDescending(preset => preset.UpdatedAt)
            .ToList();

        _isRefreshingPresetControls = true;
        try
        {
            PresetComboBox.ItemsSource = presets;
            PresetComboBox.IsEnabled = presets.Count > 0;
            PresetComboBox.SelectedItem = string.IsNullOrWhiteSpace(selectedId)
                ? null
                : presets.FirstOrDefault(preset => preset.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isRefreshingPresetControls = false;
        }

        if (PresetComboBox.SelectedItem is FormationPreset preset)
        {
            PresetNameTextBox.Text = preset.Name;
        }
        else if (string.IsNullOrWhiteSpace(PresetNameTextBox.Text) ||
            PresetNameTextBox.Text.Equals("Match Plan", StringComparison.OrdinalIgnoreCase))
        {
            PresetNameTextBox.Text = GetNextSuggestedPresetName();
        }

        UpdatePresetButtonStates(presets.Count);
    }

    private void UpdatePresetButtonStates(int? presetCount = null)
    {
        if (_team is null || SavePresetButton is null)
        {
            return;
        }

        var count = presetCount ?? _team.FormationPresets.Count;
        var hasSelectedPreset = PresetComboBox.SelectedItem is FormationPreset;
        var hasPresetName = !string.IsNullOrWhiteSpace(PresetNameTextBox.Text);
        var atLimit = count >= FormationPresetService.MaxPresets;

        SavePresetButton.IsEnabled = !atLimit && hasPresetName;
        OverwritePresetButton.IsEnabled = hasSelectedPreset;
        RenamePresetButton.IsEnabled = hasSelectedPreset && hasPresetName;
        DeletePresetButton.IsEnabled = hasSelectedPreset;
        PresetSlotUsageTextBlock.Text = atLimit
            ? "5/5 setup slots used. Overwrite or delete one to save a new setup."
            : $"{count}/5 setup slots used. Suggested slots: Default, Attacking, Defensive, Counter Attack, Custom.";
    }

    private string GetNextSuggestedPresetName()
    {
        if (_team is null)
        {
            return FormationPresetService.SuggestedPresetNames[0];
        }

        return FormationPresetService.SuggestedPresetNames
            .FirstOrDefault(name => !_team.FormationPresets.Any(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            ?? FormationPresetService.SuggestedPresetNames[^1];
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is not FormationPreset preset)
        {
            UpdatePresetButtonStates();
            return;
        }

        PresetNameTextBox.Text = preset.Name;
        UpdatePresetButtonStates();

        if (_isRefreshingPresetControls || _isLoadingSetup || _team is null)
        {
            return;
        }

        ApplySelectedPreset(preset);
    }

    private void PresetNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePresetButtonStates();

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_team is null)
        {
            return;
        }

        ApplyCurrentSetup();
        var result = _formationPresetService.SaveNewPreset(_team, PresetNameTextBox.Text);
        ShowPresetOperationResult(result);
    }

    private void ApplySelectedPreset(FormationPreset preset)
    {
        if (_team is null)
        {
            return;
        }

        var result = _formationPresetService.ApplyPreset(_team, preset);
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Saved Setups", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadTeam(_team);
        SetupChanged?.Invoke(this, EventArgs.Empty);
        if (result.Warnings.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, result.Warnings), "Setup Loaded With Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OverwritePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_team is null || PresetComboBox.SelectedItem is not FormationPreset preset)
        {
            return;
        }

        ApplyCurrentSetup();
        ShowPresetOperationResult(_formationPresetService.OverwritePreset(_team, preset.Id));
    }

    private void RenamePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_team is null || PresetComboBox.SelectedItem is not FormationPreset preset)
        {
            return;
        }

        ShowPresetOperationResult(_formationPresetService.RenamePreset(_team, preset.Id, PresetNameTextBox.Text));
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_team is null || PresetComboBox.SelectedItem is not FormationPreset preset)
        {
            return;
        }

        if (MessageBox.Show($"Delete \"{preset.Name}\"?", "Delete Saved Setup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ShowPresetOperationResult(_formationPresetService.DeletePreset(_team, preset.Id));
    }

    private void ShowPresetOperationResult(FormationPresetOperationResult result)
    {
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Saved Setups", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshPresetControls(result.Preset?.Id);
        SetupChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderPitch()
    {
        if (_team is null || PitchCanvas.ActualWidth <= 0 || PitchCanvas.ActualHeight <= 0) { return; }
        PitchCanvas.Children.Clear();
        var formation = GetSelectedFormation();
        var positions = _formationLayoutService.GetPositions(formation);
        AssignFormationPositions(positions);

        for (var index = 0; index < _pitchSlots.Count && index < positions.Count; index++)
        {
            var player = _pitchSlots[index];
            if (!IsAvailable(player)) { continue; }
            var position = positions[index];
            var button = CreatePlayerButton(player, position.ExactPosition);
            Canvas.SetLeft(button, ClampCanvas(PitchCanvas.ActualWidth, position.X, PitchCardWidth));
            Canvas.SetTop(button, ClampCanvas(PitchCanvas.ActualHeight, position.Y, PitchCardHeight));
            PitchCanvas.Children.Add(button);
        }
    }

    private void AssignFormationPositions(IReadOnlyList<PitchPosition>? positions = null)
    {
        if (_team is null) { return; }
        positions ??= _formationLayoutService.GetPositions(GetSelectedFormation());
        for (var index = 0; index < _pitchSlots.Count && index < positions.Count; index++)
        {
            if (CanPlayerOccupySlot(_pitchSlots[index], positions[index].ExactPosition))
            {
                PositionSuitabilityService.EnsurePositionMetadata(_pitchSlots[index], positions[index].ExactPosition);
            }
        }
    }

    private Button CreatePlayerButton(Player player, string slot)
    {
        var card = CreatePitchPlayerCard(player, slot);
        var button = new Button
        {
            Width = PitchCardWidth,
            MinHeight = PitchCardHeight,
            Tag = player,
            Content = card,
            DataContext = card,
            ContentTemplate = (DataTemplate)FindResource("PitchPlayerCardTemplate"),
            Style = (Style)FindResource("PitchPlayerButtonStyle"),
            AllowDrop = true
        };
        button.Click += PlayerButton_Click;
        button.PreviewMouseLeftButtonDown += (_, e) => _dragStartPoint = e.GetPosition(this);
        button.PreviewMouseMove += StarterButton_MouseMove;
        button.PreviewDragOver += PlayerCard_DragOver;
        button.PreviewDrop += PlayerCard_Drop;
        return button;
    }

    private PitchPlayerCard CreatePitchPlayerCard(Player player, string slot)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player, slot);
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var colors = TeamColorService.GetPalette(_team);
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        return new PitchPlayerCard
        {
            Player = player,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            PlayerName = player.Name,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            PositionText = slot,
            OverallText = $"OVR {PlayerOverallCalculator.CalculateOverall(player)}",
            OverallForeground = colors.TextColor,
            TextForeground = colors.TextColor,
            MutedForeground = colors.TextColor,
            PositionBackground = colors.SecondaryColor,
            PositionForeground = TeamColorService.GetReadableTextColor(colors.SecondaryColor),
            Stamina = Math.Clamp((int)Math.Round(player.Stamina), 0, 100),
            StaminaBrush = GetStaminaBrush(player),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits),
            CardBackground = colors.PrimaryColor,
            CardBorderBrush = player == _selectedStarter ? colors.SelectedGlowColor : colors.BorderColor,
            CardBorderThickness = player == _selectedStarter ? new Thickness(3) : new Thickness(1)
        };
    }

    private void RefreshSubstitutes()
    {
        if (_team is null) { return; }
        var filter = PositionSuitabilityService.NormalizeExactPosition(_selectedPositionFilter);
        var players = _team.Substitutes.Where(IsAvailable).ToList();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            players = players
                .Where(player => PositionCompatibilityService.IsReasonableFit(player, filter))
                .OrderByDescending(player => PositionCompatibilityService.GetCompatibilityScore(player, filter))
                .ThenByDescending(PlayerOverallCalculator.CalculateOverall)
                .ToList();
        }

        SubstituteListBox.ItemsSource = players.Select(CreateBenchCard).ToList();
        FilterTextBlock.Text = string.IsNullOrWhiteSpace(filter) ? "All players" : $"Filter: {filter}";
        ClearFilterButton.Visibility = string.IsNullOrWhiteSpace(filter) ? Visibility.Collapsed : Visibility.Visible;
    }

    private BenchPlayerCard CreateBenchCard(Player player)
    {
        var form = PlayerFormBadgeHelper.Create(player.FormStatus);
        var colors = TeamColorService.GetPalette(_team);
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        return new BenchPlayerCard
        {
            Player = player,
            Name = player.Name,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            Position = player.PreferredPosition,
            OverallText = $"OVR {PlayerOverallCalculator.CalculateOverall(player)}",
            OverallRating = PlayerOverallCalculator.CalculateOverall(player),
            GrowthText = PlayerGrowthDisplayHelper.CreateGrowthText(player),
            Stamina = Math.Clamp((int)Math.Round(player.Stamina), 0, 100),
            StaminaBrush = GetStaminaBrush(player),
            BenchFormBadgeText = form.Text,
            BenchFormBadgeBackground = form.Background,
            BenchFormBadgeForeground = form.Foreground,
            CardBackground = colors.PrimaryColor,
            CardBorderBrush = colors.BorderColor,
            TextForeground = colors.TextColor,
            PositionBackground = colors.SecondaryColor,
            PositionForeground = TeamColorService.GetReadableTextColor(colors.SecondaryColor),
            TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits)
        };
    }

    private void RefreshUnavailablePlayers()
    {
        if (_team is null) { return; }
        if (!ShowUnavailablePlayers)
        {
            UnavailableListBox.ItemsSource = null;
            UnavailableTitleTextBlock.Visibility = Visibility.Collapsed;
            UnavailableListBox.Visibility = Visibility.Collapsed;
            return;
        }

        var unavailable = _team.Players.Concat(_team.Substitutes)
            .Where(player => player.IsInjured || player.IsSuspended || player.IsSentOff)
            .Distinct()
            .Select(player => UnavailablePlayerCard.Create(player, _team))
            .ToList();
        UnavailableListBox.ItemsSource = unavailable;
        UnavailableTitleTextBlock.Visibility = unavailable.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UnavailableListBox.Visibility = unavailable.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FormationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_team is not null && FormationComboBox.SelectedValue is string formation)
        {
            _team.Formation = formation;
            if (!_isLoadingSetup)
            {
                _pitchSlots = OrderPlayersForPitch(_pitchSlots, formation).Take(11).ToList();
                ApplyPitchSlotsToTeam();
            }
        }
        RefreshAll();
        NotifyChanged();
    }

    private void TacticalSettingsPanel_TacticsChanged(object? sender, EventArgs e) => NotifyChanged();
    private void PitchCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderPitch();
    private void ClearFilterButton_Click(object sender, RoutedEventArgs e) { _selectedStarter = null; _selectedPositionFilter = null; RefreshAll(); }

    private void PlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Player player, DataContext: PitchPlayerCard card }) { return; }
        _selectedStarter = player;
        _selectedPositionFilter = card.PositionText;
        RefreshAll();
    }

    private void PitchCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinPitchCard(e.OriginalSource as DependencyObject)) { return; }
        _selectedStarter = null;
        _selectedPositionFilter = null;
        RefreshAll();
    }

    private void StarterButton_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button { Tag: Player player } button || !MovedEnough(e.GetPosition(this))) { return; }
        StartDrag(button, player, DragSource.StartingXi);
    }

    private void SubstituteCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(this);

    private void SubstituteCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: BenchPlayerCard card } || !MovedEnough(e.GetPosition(this))) { return; }
        StartDrag((DependencyObject)sender, card.Player, DragSource.Substitute);
    }

    private void PlayerCard_DragOver(object sender, DragEventArgs e) { e.Effects = GetDrag(e) is null ? DragDropEffects.None : DragDropEffects.Move; e.Handled = true; }
    private void SubstituteCard_DragOver(object sender, DragEventArgs e) { e.Effects = GetDrag(e) is null ? DragDropEffects.None : DragDropEffects.Move; e.Handled = true; }
    private void SubstituteListBox_DragOver(object sender, DragEventArgs e) { e.Effects = GetDrag(e)?.Source == DragSource.StartingXi ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }

    private void PlayerCard_Drop(object sender, DragEventArgs e)
    {
        var drag = GetDrag(e);
        var target = sender is Button { Tag: Player player } ? player : null;
        if (drag is null || target is null) { return; }
        if (drag.Source == DragSource.StartingXi) { SwapPitchPlayers(drag.Player, target); }
        else { SwapStarterWithSub(target, drag.Player); }
        e.Handled = true;
    }

    private void SubstituteCard_Drop(object sender, DragEventArgs e)
    {
        var drag = GetDrag(e);
        var target = sender is FrameworkElement { DataContext: BenchPlayerCard card } ? card.Player : null;
        if (drag is null || target is null) { return; }
        if (drag.Source == DragSource.StartingXi) { SwapStarterWithSub(drag.Player, target); }
        else { SwapBenchPlayers(drag.Player, target); }
        e.Handled = true;
    }

    private void SubstituteListBox_Drop(object sender, DragEventArgs e)
    {
        var drag = GetDrag(e);
        var first = _team?.Substitutes.FirstOrDefault();
        if (drag?.Source == DragSource.StartingXi && first is not null) { SwapStarterWithSub(drag.Player, first); }
    }

    private void SwapStarterWithSub(Player starter, Player substitute)
    {
        if (_team is null) { return; }
        ApplyPitchSlotsToTeam();
        var slot = starter.AssignedPosition;
        var result = _squadSelectionService.SwapStarterWithSubstitute(_team, starter, substitute);
        if (!result.Success) { MessageBox.Show(result.Message); return; }
        PositionSuitabilityService.EnsurePositionMetadata(substitute, slot);
        starter.AssignedPosition = starter.PreferredPosition;
        _pitchSlots = _team.Players.ToList();
        _selectedStarter = substitute;
        RefreshAll();
        NotifyChanged();
    }

    private void SwapPitchPlayers(Player first, Player second)
    {
        if (_team is null || first == second) { return; }
        var firstIndex = _pitchSlots.IndexOf(first);
        var secondIndex = _pitchSlots.IndexOf(second);
        if (firstIndex < 0 || secondIndex < 0) { return; }
        var positions = _formationLayoutService.GetPositions(GetSelectedFormation());
        var firstSlot = secondIndex < positions.Count ? positions[secondIndex].ExactPosition : string.Empty;
        var secondSlot = firstIndex < positions.Count ? positions[firstIndex].ExactPosition : string.Empty;
        var hardBlockMessage = CreateHardSwapBlockMessage(first, firstSlot, second, secondSlot);
        if (!string.IsNullOrWhiteSpace(hardBlockMessage))
        {
            MessageBox.Show(hardBlockMessage);
            return;
        }
        var outOfPositionWarning = CreateOutOfPositionSwapWarning(first, firstSlot, second, secondSlot);
        (_pitchSlots[firstIndex], _pitchSlots[secondIndex]) = (_pitchSlots[secondIndex], _pitchSlots[firstIndex]);
        ApplyPitchSlotsToTeam();
        AssignFormationPositions();
        RefreshAll();
        NotifyChanged();
        if (!string.IsNullOrWhiteSpace(outOfPositionWarning))
        {
            MessageBox.Show(outOfPositionWarning, "Out Of Position", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SwapBenchPlayers(Player first, Player second)
    {
        if (_team is null || first == second) { return; }
        var firstIndex = _team.Substitutes.IndexOf(first);
        var secondIndex = _team.Substitutes.IndexOf(second);
        if (firstIndex < 0 || secondIndex < 0) { return; }
        (_team.Substitutes[firstIndex], _team.Substitutes[secondIndex]) = (_team.Substitutes[secondIndex], _team.Substitutes[firstIndex]);
        RefreshSubstitutes();
        NotifyChanged();
    }

    private IEnumerable<Player> OrderPlayersForPitch(IEnumerable<Player> players, string formation)
    {
        var remaining = players.Where(IsAvailable).ToList();
        var ordered = new List<Player>();
        foreach (var slot in _formationLayoutService.GetPositions(formation))
        {
            var selected = remaining
                .Where(player => CanPlayerOccupySlot(player, slot.ExactPosition))
                .OrderByDescending(player => PositionCompatibilityService.GetCompatibilityScore(player, slot.ExactPosition))
                .ThenByDescending(PlayerOverallCalculator.CalculateOverall)
                .FirstOrDefault();
            if (selected is null) { continue; }
            ordered.Add(selected);
            remaining.Remove(selected);
        }
        ordered.AddRange(remaining);
        return ordered;
    }

    private void StartDrag(DependencyObject source, Player player, DragSource dragSource)
    {
        if (!IsAvailable(player)) { return; }
        var data = new DataObject();
        data.SetData(typeof(DraggedPlayerInfo), new DraggedPlayerInfo(player, dragSource));
        DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
    }

    private static DraggedPlayerInfo? GetDrag(DragEventArgs e) => e.Data.GetData(typeof(DraggedPlayerInfo)) as DraggedPlayerInfo;
    private bool MovedEnough(Point current) => Math.Abs(current.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(current.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    private static double ClampCanvas(double size, double position, double elementSize) => Math.Clamp((size * position) - (elementSize / 2), 4, Math.Max(4, size - elementSize - 4));
    private static bool IsAvailable(Player player) => !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    private static string GetStaminaBrush(Player player) => player.Stamina < 35 ? "#EF4444" : player.Stamina < 60 ? "#F59E0B" : "#22C55E";
    private static bool CanPlayerOccupySlot(Player player, string slot) => PositionSuitabilityService.NormalizeExactPosition(slot) == "GK" ? PositionSuitabilityService.IsGoalkeeperCapable(player) : !PositionSuitabilityService.IsGoalkeeperCapable(player) && PositionCompatibilityService.GetCompatibilityScore(player, slot) > PositionCompatibilityService.Impossible;

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

    private static string? CreateOutOfPositionSwapWarning(Player firstPlayer, string firstTargetSlot, Player secondPlayer, string secondTargetSlot)
    {
        var firstWarning = CreateOutOfPositionWarningPart(firstPlayer, firstTargetSlot);
        var secondWarning = CreateOutOfPositionWarningPart(secondPlayer, secondTargetSlot);
        if (firstWarning is null && secondWarning is null)
        {
            return null;
        }

        var naturalFitText = CreateNaturalFitText(firstPlayer, firstTargetSlot, secondPlayer, secondTargetSlot);
        var warningParts = new[] { firstWarning, secondWarning }
            .Where(message => !string.IsNullOrWhiteSpace(message));
        return $"{naturalFitText}{string.Join(" ", warningParts)} OVR will be reduced for out-of-position players.";
    }

    private static string CreateNaturalFitText(Player firstPlayer, string firstTargetSlot, Player secondPlayer, string secondTargetSlot)
    {
        var naturalFits = new[]
            {
                CreateNaturalFitPart(firstPlayer, firstTargetSlot),
                CreateNaturalFitPart(secondPlayer, secondTargetSlot)
            }
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();

        return naturalFits.Count == 0 ? string.Empty : $"{string.Join(", ", naturalFits)}. ";
    }

    private static string? CreateNaturalFitPart(Player player, string targetSlot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(targetSlot);
        return !string.IsNullOrWhiteSpace(normalizedSlot) &&
            PositionCompatibilityService.CanPlayPosition(player, normalizedSlot)
                ? $"{player.Name} can play {normalizedSlot}"
                : null;
    }

    private static string? CreateOutOfPositionWarningPart(Player player, string targetSlot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(targetSlot);
        if (string.IsNullOrWhiteSpace(normalizedSlot) ||
            normalizedSlot == "GK" ||
            PositionCompatibilityService.CanPlayPosition(player, normalizedSlot))
        {
            return null;
        }

        return $"{player.Name} cannot naturally cover {normalizedSlot}.";
    }

    private static bool IsWithinPitchCard(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button { DataContext: PitchPlayerCard }) { return true; }
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private enum DragSource { StartingXi, Substitute }
    private sealed record DraggedPlayerInfo(Player Player, DragSource Source);

    private sealed class BenchPlayerCard
    {
        public Player Player { get; init; } = new();
        public string Name { get; init; } = string.Empty;
        public string FlagImagePath { get; init; } = "/Assets/Flags/default.png";
        public string NationalityName { get; init; } = "Unknown nationality";
        public string ShirtNumberText { get; init; } = string.Empty;
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
    }

    private sealed record UnavailablePlayerCard
    {
        public string Name { get; init; } = string.Empty;
        public string FlagImagePath { get; init; } = "/Assets/Flags/default.png";
        public string NationalityName { get; init; } = "Unknown nationality";
        public string ShirtNumberText { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string OverallText { get; init; } = string.Empty;
        public double Stamina { get; init; }
        public string StaminaBrush { get; init; } = "#2FA84F";
        public string BenchFormBadgeText { get; init; } = string.Empty;
        public string BenchFormBadgeBackground { get; init; } = "#E1E5EA";
        public string BenchFormBadgeForeground { get; init; } = "#465364";
        public string CardBackground { get; init; } = "White";
        public string CardBorderBrush { get; init; } = "#B91C1C";
        public string TextForeground { get; init; } = "#102033";
        public string PositionBackground { get; init; } = "#E7EEF8";
        public string PositionForeground { get; init; } = "#102033";
        public string StatusText { get; init; } = "Unavailable";
        public string StatusBadgeBackground { get; init; } = "#B91C1C";
        public string InjuryType { get; init; } = string.Empty;
        public string RecoveryText { get; init; } = string.Empty;
        public string UnavailableInfoText => string.IsNullOrWhiteSpace(RecoveryText)
            ? InjuryType
            : $"{InjuryType} | {RecoveryText}";
        public string Tooltip { get; init; } = string.Empty;
        public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
        public static UnavailablePlayerCard Create(Player player, Team team)
        {
            PositionSuitabilityService.EnsurePositionMetadata(player);
            var form = PlayerFormBadgeHelper.Create(player.FormStatus);
            var colors = TeamColorService.GetPalette(team);
            var nationality = PlayerNationalityDisplayService.Resolve(player);
            var baseCard = new UnavailablePlayerCard
            {
                Name = player.Name,
                FlagImagePath = nationality.FlagImagePath,
                NationalityName = nationality.Name,
                ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
                Position = player.PreferredPosition,
                OverallText = $"OVR {PlayerOverallCalculator.CalculateOverall(player)}",
                Stamina = Math.Clamp((int)Math.Round(player.Stamina), 0, 100),
                StaminaBrush = GetStaminaBrush(player),
                BenchFormBadgeText = form.Text,
                BenchFormBadgeBackground = form.Background,
                BenchFormBadgeForeground = form.Foreground,
                CardBackground = colors.PrimaryColor,
                CardBorderBrush = "#B91C1C",
                TextForeground = colors.TextColor,
                PositionBackground = colors.SecondaryColor,
                PositionForeground = TeamColorService.GetReadableTextColor(colors.SecondaryColor),
                TraitBadges = PlayerTraitBadgeHelper.Create(player.Traits)
            };

            if (player.IsSuspended || player.IsSentOff)
            {
                var matches = Math.Max(1, player.SuspendedMatches);
                var recovery = matches == 1 ? "Suspended: 1 match" : $"Suspended: {matches} matches";
                return baseCard with
                {
                    StatusText = "Banned",
                    StatusBadgeBackground = "#7F1D1D",
                    InjuryType = "Suspended",
                    RecoveryText = recovery,
                    Tooltip = recovery
                };
            }

            var severity = player.InjurySeverity?.ToString() ?? "Injury";
            var injuryType = string.IsNullOrWhiteSpace(player.InjuryType)
                ? severity
                : $"{player.InjuryType} | {severity}";
            var recoveryText = player.IsSeasonEndingInjury
                ? "Recovery: Season"
                : $"Recovery: {Math.Max(1, player.InjuryRecoveryMatches)} Matches";
            return baseCard with
            {
                StatusText = "Injured",
                StatusBadgeBackground = "#DC2626",
                InjuryType = injuryType,
                RecoveryText = recoveryText,
                Tooltip = recoveryText
            };
        }
    }
}
