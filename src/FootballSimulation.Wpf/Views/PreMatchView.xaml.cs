using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class PreMatchView : UserControl
{
    private static readonly string[] SupportedFormations = ["4-3-3", "4-2-3-1", "4-4-2", "3-5-2"];

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly TacticalInsightService _tacticalInsightService = new();
    private readonly SquadSelectionService _squadSelectionService = new();
    private const double PitchCardWidth = 112;
    private const double PitchCardHeight = 72;

    private Player? _selectedStarter;
    private List<Player> _pitchSlots = [];
    private Point _dragStartPoint;
    private bool _isDraggingPlayer;
    private bool _isLoadingSetup;
    private bool _areTacticsEventsWired;

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

        FixtureTextBlock.Text = $"{_state.CurrentFixture.HomeTeam.Name} vs {_state.CurrentFixture.AwayTeam.Name}";
        LoadFormationSelector(_state.SelectedTeam);
        LoadTactics(_state.SelectedTeam.Tactics);
        WireTacticsEvents();
        InitializePitchSlots();
        RefreshSubstitutes();
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

        _pitchSlots = OrderPlayersForPitch(_state.SelectedTeam.Players, _state.SelectedTeam.Formation).ToList();
        _state.SelectedTeam.Players = _pitchSlots.ToList();
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
        MentalityComboBox.ItemsSource = Enum.GetValues<Mentality>();
        MentalityComboBox.SelectedItem = tactics.Mentality;
        PressingSlider.Value = tactics.PressingIntensity;
        WidthSlider.Value = tactics.Width;
        TempoSlider.Value = tactics.Tempo;
        DefensiveLineSlider.Value = tactics.DefensiveLine;
    }

    private void WireTacticsEvents()
    {
        if (_areTacticsEventsWired)
        {
            return;
        }

        PressingSlider.ValueChanged += TacticsSlider_Changed;
        WidthSlider.ValueChanged += TacticsSlider_Changed;
        TempoSlider.ValueChanged += TacticsSlider_Changed;
        DefensiveLineSlider.ValueChanged += TacticsSlider_Changed;
        _areTacticsEventsWired = true;
    }

    private void RenderPitch()
    {
        if (_state.SelectedTeam is null || PitchCanvas.ActualWidth <= 0 || PitchCanvas.ActualHeight <= 0)
        {
            return;
        }

        if (_pitchSlots.Count != _state.SelectedTeam.Players.Count)
        {
            InitializePitchSlots();
        }

        PitchCanvas.Children.Clear();

        var formation = FormationComboBox.SelectedItem as string ?? _state.SelectedTeam.Formation;
        var positions = _formationLayoutService.GetPositions(formation);
        AssignFormationPositions(positions);

        for (var index = 0; index < _pitchSlots.Count && index < positions.Count; index++)
        {
            var player = _pitchSlots[index];
            var position = positions[index];
            var button = CreatePlayerButton(player);

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
            PositionSuitabilityService.EnsurePositionMetadata(_pitchSlots[index], positions[index].ExactPosition);
        }
    }

    private static double GetClampedCanvasPosition(double canvasSize, double normalizedPosition, double elementSize)
    {
        var rawPosition = (canvasSize * normalizedPosition) - (elementSize / 2);
        return Math.Clamp(rawPosition, 4, Math.Max(4, canvasSize - elementSize - 4));
    }

    private Button CreatePlayerButton(Player player)
    {
        var card = CreatePitchPlayerCard(player);
        var button = new Button
        {
            Width = PitchCardWidth,
            Height = PitchCardHeight,
            Tag = player,
            DataContext = card,
            ToolTip = "Drag this player or drop another player here.",
            Content = card,
            ContentTemplate = (DataTemplate)FindResource("PitchPlayerCardTemplate"),
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

    private PitchPlayerCard CreatePitchPlayerCard(Player player)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var form = GetFormBadge(player.CurrentForm);
        var isOutOfPosition = PositionSuitabilityService.IsOutOfPosition(player);
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(player);
        var ratingVisual = GetRatingVisual(player, suitability);

        return new PitchPlayerCard
        {
            Player = player,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            ShirtNumberValue = player.SquadNumber > 0 ? player.SquadNumber.ToString() : string.Empty,
            PlayerImagePath = GetPlayerImagePath(player),
            PlayerName = player.Name,
            PositionText = player.AssignedPosition,
            OverallText = $"OVR {ratingVisual.Rating}",
            OverallForeground = ratingVisual.Foreground,
            Stamina = GetStaminaPercentage(player),
            StaminaBrush = GetStaminaBrush(player),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
            CardBackground = player == _selectedStarter ? "#FFF2B8" : isOutOfPosition ? "#FFF7EC" : "#FFFFFF",
            CardBorderBrush = player == _selectedStarter ? "#F6C343" : isOutOfPosition ? ratingVisual.Foreground : "#102033",
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
                _pitchSlots = OrderPlayersForPitch(_state.SelectedTeam.Players, formation).ToList();
                _state.SelectedTeam.Players = _pitchSlots.ToList();
            }
        }

        RenderPitch();
        RefreshTacticalInsight();
    }

    private void TacticsControl_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshTacticalInsight();
    }

    private void TacticsSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshTacticalInsight();
    }

    private void ExecuteSwap(Player starter, Player substitute)
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        _state.SelectedTeam.Players = _pitchSlots.ToList();
        PositionSuitabilityService.EnsurePositionMetadata(starter);
        PositionSuitabilityService.EnsurePositionMetadata(substitute);
        var incomingAssignedPosition = starter.AssignedPosition;

        var swapResult = _squadSelectionService.SwapStarterWithSubstitute(
            _state.SelectedTeam,
            starter,
            substitute);

        if (!swapResult.Success)
        {
            MessageBox.Show(swapResult.Message);
            return;
        }

        starter.AssignedPosition = starter.PreferredPosition;
        PositionSuitabilityService.EnsurePositionMetadata(substitute, incomingAssignedPosition);

        _pitchSlots = _state.SelectedTeam.Players.ToList();
        Debug.WriteLine(
            $"[PreMatchDrag] Swapped Sub<->StartingXI: {substitute.Name} -> slot {_pitchSlots.IndexOf(substitute)} ({substitute.AssignedPosition}); " +
            $"{starter.Name} -> substitute index {_state.SelectedTeam.Substitutes.IndexOf(starter)}");
        _selectedStarter = substitute;
        RefreshSubstitutes();
        UpdateSelectedPlayerDetails();
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
            .Select(CreateBenchPlayerCard)
            .ToList();

        SubstituteListBox.ItemsSource = null;
        SubstituteListBox.ItemsSource = benchCards;
        SubstituteListBox.IsEnabled = benchCards.Count > 0;
    }

    private static BenchPlayerCard CreateBenchPlayerCard(Player player)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var form = GetFormBadge(player.CurrentForm);

        return new BenchPlayerCard
        {
            Player = player,
            Name = player.Name,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            PlayerImagePath = GetPlayerImagePath(player),
            Position = player.PreferredPosition,
            OverallText = $"OVR {GetOverallRating(player)}",
            OverallRating = GetOverallRating(player),
            Stamina = GetStaminaPercentage(player),
            StaminaBrush = GetStaminaBrush(player),
            BenchFormBadgeText = form.Text,
            BenchFormBadgeBackground = form.Background,
            BenchFormBadgeForeground = form.Foreground
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
        var form = GetFormBadge(_selectedStarter.CurrentForm);
        var ratingVisual = GetRatingVisual(_selectedStarter, suitability);

        SelectedPlayerEmptyTextBlock.Visibility = Visibility.Collapsed;
        SelectedPlayerCard.Visibility = Visibility.Visible;
        SelectedPlayerCard.DataContext = _selectedStarter;

        SelectedPlayerImage.Source = CreateImageSource(GetPlayerImagePath(_selectedStarter));
        SelectedPlayerNumberTextBlock.Text = _selectedStarter.SquadNumber > 0 ? $"#{_selectedStarter.SquadNumber}" : "No squad number";
        SelectedPlayerNameTextBlock.Text = _selectedStarter.Name;
        SelectedPlayerPositionChip.Text = $"Playing {_selectedStarter.AssignedPosition}";
        SelectedPlayerPreferredChip.Text = GetPositionGroupText(_selectedStarter);
        SelectedPlayerOvrBadgeTextBlock.Text = ratingVisual.Rating.ToString();
        SelectedPlayerOvrBadgeBorder.Background = ToBrush(ratingVisual.Background);
        SelectedPlayerFormChip.Text = form.Text;
        SelectedPlayerFormChip.Foreground = ToBrush(form.Foreground);
        SelectedPlayerFormChipBorder.Background = ToBrush(form.Background);
        SelectedPlayerInjuryChip.Text = _selectedStarter.IsInjured ? "Injured" : "Fit";
        SelectedPlayerInjuryChip.Foreground = ToBrush(_selectedStarter.IsInjured ? "#8F1F1F" : "#236B39");
        SelectedPlayerInjuryChipBorder.Background = ToBrush(_selectedStarter.IsInjured ? "#FFD1D1" : "#D9F1E1");
        SelectedPlayerStaminaTextBlock.Text = $"{GetStaminaPercentage(_selectedStarter)}% stamina";
        SelectedPlayerStaminaFill.Foreground = ToBrush(GetStaminaBrush(_selectedStarter));

        SelectedPlayerAttackTextBlock.Text = $"Attack {_selectedStarter.Attack}";
        SelectedPlayerDefenseTextBlock.Text = $"Defense {_selectedStarter.Defense}";
        SelectedPlayerPassingTextBlock.Text = $"Passing {_selectedStarter.Passing}";
        SelectedPlayerFinishingTextBlock.Text = $"Finishing {_selectedStarter.Finishing}";
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
        var naturalPositions = player.NaturalPositions.Count == 0
            ? string.Empty
            : $"/{string.Join("/", player.NaturalPositions)}";
        var secondaryPositions = player.SecondaryPositions.Count == 0
            ? string.Empty
            : $" | Sec {string.Join("/", player.SecondaryPositions)}";

        return $"Pref {player.PreferredPosition}{naturalPositions}{secondaryPositions}";
    }

    private static FormBadge GetFormBadge(int currentForm)
    {
        return currentForm switch
        {
            >= 80 => new FormBadge("Hot", "#D9F1E1", "#236B39"),
            >= 65 => new FormBadge("Good", "#E7F7EA", "#2F7D42"),
            >= 40 => new FormBadge("Average", "#FFF0A3", "#5F4500"),
            > 0 => new FormBadge("Poor", "#FFD1D1", "#8F1F1F"),
            _ => new FormBadge("Inactive", "#E1E5EA", "#465364")
        };
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
        var firstSubstitute = _state.SelectedTeam?.Substitutes.FirstOrDefault();
        if (draggedPlayer is null || draggedPlayer.Source != DragSource.StartingXi || firstSubstitute is null)
        {
            return;
        }

        ExecuteSwap(draggedPlayer.Player, firstSubstitute);
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
                $"[PreMatchDrag] Swap failed: {draggedPlayer.Name} index={draggedIndex}; {targetPlayer.Name} index={targetIndex}");
            return;
        }

        (_pitchSlots[draggedIndex], _pitchSlots[targetIndex]) = (_pitchSlots[targetIndex], _pitchSlots[draggedIndex]);
        _state.SelectedTeam.Players = _pitchSlots.ToList();
        AssignFormationPositions();

        Debug.WriteLine(
            $"[PreMatchDrag] Swapped StartingXI: {draggedPlayer.Name} -> slot {targetIndex} ({draggedPlayer.AssignedPosition}); " +
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
            $"[PreMatchDrag] Swapped substitutes: {draggedPlayer.Name} -> index {targetIndex}; {targetPlayer.Name} -> index {draggedIndex}");

        RefreshSubstitutes();
    }

    private static void ApplyDropTargetStyle(Button button, bool canDrop)
    {
        if (!canDrop)
        {
            return;
        }

        button.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 224, 102));
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

        border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 224, 102));
        border.BorderThickness = new Thickness(3);
        border.Background = new SolidColorBrush(Color.FromRgb(255, 248, 210));
    }

    private static void ResetSubstituteDropTargetStyle(Border border)
    {
        border.BorderBrush = new SolidColorBrush(Color.FromRgb(214, 223, 234));
        border.BorderThickness = new Thickness(1);
        border.Background = Brushes.White;
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

        SaveSetup(_state.SelectedTeam);
        _navigate(new MatchLiveView(_state, _navigate, isSecondHalf: false));
    }

    private void SaveSetup(Team team)
    {
        if (FormationComboBox.SelectedItem is string formation)
        {
            team.Formation = formation;
        }

        if (MentalityComboBox.SelectedItem is Mentality mentality)
        {
            team.Tactics.Mentality = mentality;
        }

        team.Tactics.PressingIntensity = (int)Math.Round(PressingSlider.Value);
        team.Tactics.Width = (int)Math.Round(WidthSlider.Value);
        team.Tactics.Tempo = (int)Math.Round(TempoSlider.Value);
        team.Tactics.DefensiveLine = (int)Math.Round(DefensiveLineSlider.Value);
    }

    private IEnumerable<Player> OrderPlayersForPitch(IEnumerable<Player> players, string formation)
    {
        var remainingPlayers = players.ToList();
        var orderedPlayers = new List<Player>();
        var formationSlots = _formationLayoutService.GetPositions(formation);

        foreach (var slot in formationSlots)
        {
            var selectedPlayer = remainingPlayers
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

        if (player.NaturalPositions.Contains(normalizedSlot))
        {
            return 1000;
        }

        if (player.SecondaryPositions.Contains(normalizedSlot))
        {
            return 900;
        }

        return player.Position == GetGenericPositionForSlot(normalizedSlot) ? 600 : 100;
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

    private sealed record FormBadge(string Text, string Background, string Foreground);

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
        public string ShirtNumberText { get; init; } = string.Empty;
        public string PlayerImagePath { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string OverallText { get; init; } = string.Empty;
        public int OverallRating { get; init; }
        public double Stamina { get; init; }
        public string StaminaBrush { get; init; } = "#2FA84F";
        public string BenchFormBadgeText { get; init; } = string.Empty;
        public string BenchFormBadgeBackground { get; init; } = "#E1E5EA";
        public string BenchFormBadgeForeground { get; init; } = "#465364";
    }
}
