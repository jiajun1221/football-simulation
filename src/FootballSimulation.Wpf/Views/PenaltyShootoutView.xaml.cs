using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class PenaltyShootoutView : UserControl
{
    private const int RequiredTakers = 5;

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly bool _isPracticeMode;
    private readonly Func<UserControl>? _practiceReturnFactory;
    private readonly Match? _previousMatch;
    private readonly LiveMatchSegment _previousSegment;
    private readonly GameSessionService _gameSessionService = new();
    private readonly TransferMarketService _transferMarketService = new();
    private readonly Random _random = new();
    private readonly ObservableCollection<TakerOption> _homePlayerOptions = [];
    private readonly ObservableCollection<TakerOption> _awayPlayerOptions = [];
    private readonly ObservableCollection<PenaltyIndicatorViewModel> _homeIndicators = [];
    private readonly ObservableCollection<PenaltyIndicatorViewModel> _awayIndicators = [];
    private readonly ObservableCollection<OrderSlotViewModel> _homeOrderSlots = [];
    private readonly ObservableCollection<OrderSlotViewModel> _awayOrderSlots = [];
    private readonly ObservableCollection<FeedItemViewModel> _feedItems = [];
    private readonly List<TakerOption> _selectedUserTakers = [];
    private readonly List<Player> _homeOrder = [];
    private readonly List<Player> _awayOrder = [];
    private readonly List<PenaltyAttempt> _homeAttempts = [];
    private readonly List<PenaltyAttempt> _awayAttempts = [];

    private Team? _homeTeam;
    private Team? _awayTeam;
    private Team? _userTeam;
    private TeamColorPalette? _homePalette;
    private TeamColorPalette? _awayPalette;
    private bool _isUserHome;
    private bool _isSelectionUpdating;
    private bool _shootoutStarted;
    private bool _shootoutCompleted;
    private bool _isAutoRunning;
    private bool _homeToKick = true;

    public PenaltyShootoutView(GameFlowState state, Action<UserControl> navigate)
        : this(state, navigate, isPracticeMode: false)
    {
    }

    public PenaltyShootoutView(
        GameFlowState state,
        Action<UserControl> navigate,
        bool isPracticeMode,
        Func<UserControl>? practiceReturnFactory = null,
        Match? previousMatch = null,
        LiveMatchSegment previousSegment = LiveMatchSegment.FirstHalf)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        _isPracticeMode = isPracticeMode;
        _practiceReturnFactory = practiceReturnFactory;
        _previousMatch = previousMatch;
        _previousSegment = previousSegment;

        LoadShootout();
    }

    private void LoadShootout()
    {
        var fixture = _state.CurrentFixture;
        var match = _state.CurrentMatch;
        if (fixture is null || match is null)
        {
            StatusTextBlock.Text = "Penalty shootout unavailable.";
            PrimaryActionButton.Content = "Back to Result";
            return;
        }

        _homeTeam = match.HomeTeam;
        _awayTeam = match.AwayTeam;
        _userTeam = ResolveUserTeam(match);
        _isUserHome = string.Equals(_userTeam.Name, _homeTeam.Name, StringComparison.OrdinalIgnoreCase);
        var matchPalettes = TeamColorService.GetMatchPalettes(_homeTeam, _awayTeam);
        _homePalette = matchPalettes.Home;
        _awayPalette = matchPalettes.Away;

        ExtraTimeScoreTextBlock.Text = "Penalty Shootout";
        FixtureTextBlock.Text = $"{_homeTeam.Name} {match.HomeScore} - {match.AwayScore} {_awayTeam.Name}";
        HomeTeamTextBlock.Text = _homeTeam.Name;
        AwayTeamTextBlock.Text = _awayTeam.Name;
        HomeLogoImage.Source = ClubLogoService.LoadClubLogo(_homeTeam.Name, _state.League?.LeagueId ?? _state.SelectedLeagueId);
        AwayLogoImage.Source = ClubLogoService.LoadClubLogo(_awayTeam.Name, _state.League?.LeagueId ?? _state.SelectedLeagueId);
        HomeBreakdownTitleTextBlock.Text = $"{_homeTeam.Name} Penalty Breakdown";
        AwayBreakdownTitleTextBlock.Text = $"{_awayTeam.Name} Penalty Breakdown";
        ApplyTeamTheme(HomeTeamPanelBorder, HomeTeamTextBlock, HomeAutoPickButton, _homePalette);
        ApplyTeamTheme(AwayTeamPanelBorder, AwayTeamTextBlock, AwayAutoPickButton, _awayPalette);

        BindCollections();
        LoadPlayers();
        ResetIndicators();
        UpdateOrderSlots();
        UpdateSelectionState();
    }

    private static void ApplyTeamTheme(Border panel, TextBlock teamNameTextBlock, Button autoPickButton, TeamColorPalette? palette)
    {
        if (palette is null)
        {
            return;
        }

        panel.BorderBrush = ToBrush(palette.IsLight ? palette.BorderColor : palette.PrimaryColor);
        panel.Background = ToBrush(palette.SubtleBackgroundColor);
        teamNameTextBlock.Foreground = ToBrush(palette.IsLight ? "#0F172A" : palette.PrimaryColor);
        autoPickButton.Background = ToBrush(palette.PrimaryColor);
        autoPickButton.Foreground = ToBrush(palette.TextColor);
        autoPickButton.BorderBrush = ToBrush(palette.BorderColor);
    }

    private void BindCollections()
    {
        HomePlayersListBox.ItemsSource = _homePlayerOptions;
        AwayPlayersListBox.ItemsSource = _awayPlayerOptions;
        HomeIndicatorsItemsControl.ItemsSource = _homeIndicators;
        AwayIndicatorsItemsControl.ItemsSource = _awayIndicators;
        HomeOrderItemsControl.ItemsSource = _homeOrderSlots;
        AwayOrderItemsControl.ItemsSource = _awayOrderSlots;
        LiveFeedListBox.ItemsSource = _feedItems;
    }

    private void LoadPlayers()
    {
        if (_homeTeam is null || _awayTeam is null)
        {
            return;
        }

        _homePlayerOptions.Clear();
        _awayPlayerOptions.Clear();
        foreach (var player in CreateSortedTakerOptions(_homeTeam, _isUserHome, _homePalette))
        {
            _homePlayerOptions.Add(player);
        }

        foreach (var player in CreateSortedTakerOptions(_awayTeam, !_isUserHome, _awayPalette))
        {
            _awayPlayerOptions.Add(player);
        }

        HomePlayersListBox.IsEnabled = _isUserHome && !_shootoutStarted;
        AwayPlayersListBox.IsEnabled = !_isUserHome && !_shootoutStarted;
        HomeAutoPickButton.Visibility = _isUserHome ? Visibility.Visible : Visibility.Collapsed;
        AwayAutoPickButton.Visibility = !_isUserHome ? Visibility.Visible : Visibility.Collapsed;
        HomeSelectionHintTextBlock.Text = _isUserHome
            ? "Select 5 on-pitch penalty takers."
            : "Opponent order is selected automatically.";
        AwaySelectionHintTextBlock.Text = !_isUserHome
            ? "Select 5 on-pitch penalty takers."
            : "Opponent order is selected automatically.";
    }

    private IEnumerable<TakerOption> CreateSortedTakerOptions(Team team, bool canSelect, TeamColorPalette? palette)
    {
        return GetEligiblePlayers(team)
            .OrderByDescending(CalculatePenaltyAttribute)
            .ThenByDescending(CalculateComposure)
            .ThenByDescending(GetOverall)
            .Select(player => CreateTakerOption(player, canSelect, palette));
    }

    private TakerOption CreateTakerOption(Player player, bool canSelect, TeamColorPalette? palette)
    {
        var penalty = CalculatePenaltyAttribute(player);
        var primary = ToBrush(palette?.PrimaryColor ?? "#2563EB");
        var border = ToBrush(palette?.BorderColor ?? "#34435C");
        var selectedBackground = ToBrush(CreateTransparentColor(palette?.PrimaryColor ?? "#2563EB", 52));
        return new TakerOption(
            player,
            player.Name,
            GetDisplayPosition(player),
            $"OVR {GetOverall(player)}",
            $"Pens: {penalty}",
            penalty,
            canSelect,
            Brushes.Transparent,
            border,
            1,
            ToBrush("#0F172A"),
            ToBrush("#475569"),
            primary,
            new SolidColorBrush(GetPenaltyBadgeColor(penalty)),
            border,
            primary,
            selectedBackground);
    }

    private void ResetIndicators()
    {
        _homeIndicators.Clear();
        _awayIndicators.Clear();
        for (var index = 1; index <= RequiredTakers; index++)
        {
            _homeIndicators.Add(PenaltyIndicatorViewModel.Pending(index));
            _awayIndicators.Add(PenaltyIndicatorViewModel.Pending(index));
        }
    }

    private void PlayerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectionUpdating || _shootoutStarted || sender is not ListBox listBox || !listBox.IsEnabled)
        {
            return;
        }

        foreach (var removed in e.RemovedItems.OfType<TakerOption>())
        {
            _selectedUserTakers.Remove(removed);
            removed.IsSelected = false;
            removed.CardBackground = Brushes.Transparent;
            removed.BorderBrush = removed.DefaultBorderBrush;
            removed.BorderThickness = 1;
        }

        foreach (var added in e.AddedItems.OfType<TakerOption>())
        {
            if (_selectedUserTakers.Count >= RequiredTakers)
            {
                _isSelectionUpdating = true;
                listBox.SelectedItems.Remove(added);
                _isSelectionUpdating = false;
                StatusTextBlock.Text = "Only 5 penalty takers can be selected.";
                continue;
            }

            if (!_selectedUserTakers.Contains(added))
            {
                _selectedUserTakers.Add(added);
                added.IsSelected = true;
                added.CardBackground = added.SelectedBackground;
                added.BorderBrush = added.SelectionBorderBrush;
                added.BorderThickness = 2;
            }
        }

        RefreshPlayerLists();
        UpdateOrderSlots();
        UpdateSelectionState();
    }

    private void AutoPickBestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shootoutStarted)
        {
            return;
        }

        var listBox = _isUserHome ? HomePlayersListBox : AwayPlayersListBox;
        var options = _isUserHome ? _homePlayerOptions : _awayPlayerOptions;
        _isSelectionUpdating = true;
        listBox.SelectedItems.Clear();
        _selectedUserTakers.Clear();

        foreach (var option in options)
        {
            option.IsSelected = false;
            option.CardBackground = Brushes.Transparent;
            option.BorderBrush = option.DefaultBorderBrush;
            option.BorderThickness = 1;
        }

        foreach (var option in options.Take(RequiredTakers))
        {
            _selectedUserTakers.Add(option);
            option.IsSelected = true;
            option.CardBackground = option.SelectedBackground;
            option.BorderBrush = option.SelectionBorderBrush;
            option.BorderThickness = 2;
            listBox.SelectedItems.Add(option);
        }

        _isSelectionUpdating = false;
        RefreshPlayerLists();
        UpdateOrderSlots();
        UpdateSelectionState();
    }

    private void RefreshPlayerLists()
    {
        HomePlayersListBox.Items.Refresh();
        AwayPlayersListBox.Items.Refresh();
    }

    private void UpdateSelectionState()
    {
        if (_shootoutStarted)
        {
            return;
        }

        var selectedCount = _selectedUserTakers.Count;
        PrimaryActionButton.IsEnabled = selectedCount == RequiredTakers;
        PrimaryActionButton.Content = "Ready";
        StatusTextBlock.Text = selectedCount == RequiredTakers
            ? "Penalty order ready. Lock it in to begin."
            : "Select 5 on-pitch penalty takers.";
        RoundTextBlock.Text = "Select Penalty Takers";
        CurrentResultTextBlock.Text = "READY?";
        KickDuelTextBlock.Text = $"{selectedCount}/5 selected for {_userTeam?.Name ?? "your team"}.";
    }

    private void UpdateOrderSlots()
    {
        if (_homeTeam is null || _awayTeam is null)
        {
            return;
        }

        var homeOrder = _isUserHome
            ? _selectedUserTakers.Select(option => option.Player).ToList()
            : CreateAiPenaltyOrder(_homeTeam).Take(RequiredTakers).ToList();
        var awayOrder = !_isUserHome
            ? _selectedUserTakers.Select(option => option.Player).ToList()
            : CreateAiPenaltyOrder(_awayTeam).Take(RequiredTakers).ToList();

        ReplaceOrderSlots(_homeOrderSlots, homeOrder, _homePalette, "#2563EB");
        ReplaceOrderSlots(_awayOrderSlots, awayOrder, _awayPalette, "#DC2626");
    }

    private static void ReplaceOrderSlots(
        ObservableCollection<OrderSlotViewModel> slots,
        IReadOnlyList<Player> order,
        TeamColorPalette? palette,
        string fallbackColor)
    {
        var isLight = palette?.IsLight == true;
        var background = ToBrush(isLight ? "#F8FAFC" : palette?.PrimaryColor ?? fallbackColor);
        var borderBrush = ToBrush(isLight ? palette?.BorderColor ?? "#94A3B8" : "#FFFFFF");
        var numberForeground = ToBrush(isLight ? "#0F172A" : "#FFFFFF");
        var playerForeground = ToBrush(isLight ? "#475569" : "#EAF6FF");

        slots.Clear();
        for (var index = 0; index < RequiredTakers; index++)
        {
            var player = index < order.Count ? order[index] : null;
            slots.Add(new OrderSlotViewModel(
                index + 1,
                player is null ? string.Empty : GetShortName(player.Name),
                background,
                borderBrush,
                numberForeground,
                playerForeground));
        }
    }

    private async void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shootoutCompleted)
        {
            if (_isPracticeMode)
            {
                RestorePracticeState();
                _navigate(_practiceReturnFactory?.Invoke() ?? new PreMatchView(_state, _navigate));
                return;
            }

            _navigate(new MatchResultView(_state, _navigate));
            return;
        }

        if (!_shootoutStarted)
        {
            await StartShootoutAsync();
            return;
        }
    }

    private async Task StartShootoutAsync()
    {
        if (_state.League is null || _state.CurrentFixture is null || _state.CurrentMatch is null ||
            _homeTeam is null || _awayTeam is null || _userTeam is null)
        {
            _navigate(new MatchResultView(_state, _navigate));
            return;
        }

        if (_selectedUserTakers.Count < RequiredTakers)
        {
            StatusTextBlock.Text = "Select 5 penalty takers.";
            return;
        }

        if (_isAutoRunning)
        {
            return;
        }

        _homeOrder.Clear();
        _awayOrder.Clear();
        _homeOrder.AddRange(_isUserHome
            ? _selectedUserTakers.Select(option => option.Player)
            : CreateAiPenaltyOrder(_homeTeam));
        _awayOrder.AddRange(!_isUserHome
            ? _selectedUserTakers.Select(option => option.Player)
            : CreateAiPenaltyOrder(_awayTeam));

        HomePlayersListBox.IsEnabled = false;
        AwayPlayersListBox.IsEnabled = false;
        HomeAutoPickButton.IsEnabled = false;
        AwayAutoPickButton.IsEnabled = false;
        _shootoutStarted = true;
        _homeToKick = true;
        _isAutoRunning = true;
        ReadyPanel.Visibility = Visibility.Collapsed;
        LiveFeedListBox.Visibility = Visibility.Visible;
        PrimaryActionButton.Visibility = Visibility.Collapsed;
        StatusTextBlock.Text = string.Empty;
        RoundTextBlock.Text = "Penalty 1";
        CurrentResultTextBlock.Text = "READY";
        _feedItems.Clear();
        AddFeed("Penalty Shootout Starts", FeedTone.Start);
        UpdateScoreline();
        ResetShotVisuals();
        await RunAutomaticShootoutAsync();
    }

    private async Task RunAutomaticShootoutAsync()
    {
        while (!_shootoutCompleted)
        {
            await TakeNextPenaltyAsync();
            if (!_shootoutCompleted)
            {
                await ResetBallForNextKickAsync();
            }
        }
    }

    private async Task TakeNextPenaltyAsync()
    {
        if (_homeTeam is null || _awayTeam is null)
        {
            return;
        }

        var shootingTeam = _homeToKick ? _homeTeam : _awayTeam;
        var defendingTeam = _homeToKick ? _awayTeam : _homeTeam;
        var attempts = _homeToKick ? _homeAttempts : _awayAttempts;
        var order = _homeToKick ? _homeOrder : _awayOrder;
        var round = attempts.Count + 1;
        var taker = GetTakerForAttempt(order, attempts, shootingTeam, round);
        var goalkeeper = GetGoalkeeper(defendingTeam);
        var shot = SimulatePenalty(taker, goalkeeper, round);

        UpdateNextDuel();
        StatusTextBlock.Text = $"{taker.Name} steps up.";
        AddFeed($"{FormatPlayerWithNumber(taker)} steps up.", FeedTone.Step);
        ResetShotVisuals();
        await Task.Delay(2400);

        StatusTextBlock.Text = $"{taker.Name} shoots {DescribeTarget(shot.Target)}.";
        AddFeed($"{FormatPlayerWithNumber(taker)} shoots {DescribeTarget(shot.Target)}.", FeedTone.Shot);
        AnimateShotAndKeeper(shot);
        await Task.Delay(1900);

        attempts.Add(new PenaltyAttempt(taker, shot.Outcome));

        UpdateBallResult(shot.Outcome);
        await Task.Delay(800);
        UpdateKickDisplay(round, taker, goalkeeper, shot.Outcome);
        UpdateIndicators();
        UpdateScoreline();
        AddResultFeed(taker, goalkeeper, shot);
        await Task.Delay(2200);

        var winner = TryGetShootoutWinner();
        if (winner is not null)
        {
            CompleteShootout(winner);
            return;
        }

        _homeToKick = !_homeToKick;
        UpdateNextDuel();
    }

    private Player GetTakerForAttempt(IReadOnlyList<Player> preferredOrder, IReadOnlyList<PenaltyAttempt> attempts, Team team, int round)
    {
        if (round <= preferredOrder.Count)
        {
            return preferredOrder[round - 1];
        }

        var usedPlayerIds = attempts.Select(attempt => CreatePlayerKey(attempt.Player)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unused = CreateAiPenaltyOrder(team)
            .FirstOrDefault(player => !usedPlayerIds.Contains(CreatePlayerKey(player)));
        if (unused is not null)
        {
            return unused;
        }

        var fallback = preferredOrder.Count > 0
            ? preferredOrder[(round - 1) % preferredOrder.Count]
            : GetEligiblePlayers(team).FirstOrDefault() ?? team.Players.FirstOrDefault();
        return fallback ?? new Player { Name = "Penalty Taker", OverallRating = 70, Finishing = 70 };
    }

    private PenaltyShot SimulatePenalty(Player taker, Player? goalkeeper, int round)
    {
        var takerScore = CalculatePenaltyAttribute(taker);
        var pressure = round <= RequiredTakers ? 0 : 5;
        var staminaPenalty = Math.Max(0, 55 - taker.Stamina) * 0.16;
        var formBonus = GetFormModifier(taker);
        var keeperScore = goalkeeper is null ? 68 : CalculateGoalkeeperPenaltyScore(goalkeeper);
        var chance = Math.Clamp(0.75 + (takerScore - keeperScore) / 240.0 + formBonus - staminaPenalty / 100.0 - pressure / 100.0, 0.55, 0.91);
        var roll = _random.NextDouble();
        var target = PickShotTarget();
        var dive = PickKeeperDive(target, keeperScore, takerScore);

        if (roll < chance)
        {
            return new PenaltyShot(PenaltyOutcome.Goal, target, dive);
        }

        var saveChance = goalkeeper is null
            ? 0.45
            : Math.Clamp(0.48 + (keeperScore - takerScore) / 300.0, 0.34, 0.68);
        var outcome = _random.NextDouble() < saveChance ? PenaltyOutcome.Saved : PenaltyOutcome.Missed;
        return new PenaltyShot(outcome, target, dive);
    }

    private ShotTarget PickShotTarget()
    {
        var values = Enum.GetValues<ShotTarget>();
        return values[_random.Next(values.Length)];
    }

    private KeeperDive PickKeeperDive(ShotTarget target, int keeperScore, int takerScore)
    {
        var readsShotChance = Math.Clamp(0.38 + (keeperScore - takerScore) / 360.0, 0.22, 0.58);
        if (_random.NextDouble() < readsShotChance)
        {
            return target switch
            {
                ShotTarget.TopLeft or ShotTarget.BottomLeft => KeeperDive.Left,
                ShotTarget.TopRight or ShotTarget.BottomRight => KeeperDive.Right,
                _ => KeeperDive.Center
            };
        }

        return _random.Next(3) switch
        {
            0 => KeeperDive.Left,
            1 => KeeperDive.Right,
            _ => KeeperDive.Center
        };
    }

    private void AnimateShotAndKeeper(PenaltyShot shot)
    {
        var (targetX, targetY) = GetBallTargetPosition(shot.Target, shot.Outcome, shot.Dive);
        BallMarker.Opacity = 1;
        BallMarker.Foreground = Brushes.White;
        BallMarker.Effect = CreateBallGlow(Colors.White, 0.35);
        Canvas.SetLeft(BallMarker, 308);
        Canvas.SetTop(BallMarker, 286);
        AnimateCanvasPosition(BallMarker, 308, 286, targetX, targetY, milliseconds: 1350);

        var keeperX = shot.Dive switch
        {
            KeeperDive.Left => 228,
            KeeperDive.Right => 362,
            _ => 295
        };
        AnimateCanvasPosition(GoalkeeperMarker, Canvas.GetLeft(GoalkeeperMarker), Canvas.GetTop(GoalkeeperMarker), keeperX, 108, milliseconds: 520);
    }

    private void ResetShotVisuals()
    {
        BallMarker.BeginAnimation(Canvas.LeftProperty, null);
        BallMarker.BeginAnimation(Canvas.TopProperty, null);
        GoalkeeperMarker.BeginAnimation(Canvas.LeftProperty, null);
        GoalkeeperMarker.BeginAnimation(Canvas.TopProperty, null);
        BallMarker.Opacity = 1;
        BallMarker.Foreground = Brushes.White;
        BallMarker.Effect = null;
        Canvas.SetLeft(BallMarker, 308);
        Canvas.SetTop(BallMarker, 286);
        Canvas.SetLeft(GoalkeeperMarker, 295);
        Canvas.SetTop(GoalkeeperMarker, 108);
    }

    private void UpdateBallResult(PenaltyOutcome outcome)
    {
        var color = outcome == PenaltyOutcome.Goal
            ? Color.FromRgb(34, 197, 94)
            : Color.FromRgb(239, 68, 68);
        BallMarker.Foreground = new SolidColorBrush(color);
        BallMarker.Effect = CreateBallGlow(color, 0.85);
        PulseBall();
    }

    private async Task ResetBallForNextKickAsync()
    {
        await Task.Delay(1400);
        BallMarker.BeginAnimation(Canvas.LeftProperty, null);
        BallMarker.BeginAnimation(Canvas.TopProperty, null);
        BallMarker.Foreground = Brushes.White;
        BallMarker.Effect = null;
        AnimateCanvasPosition(BallMarker, Canvas.GetLeft(BallMarker), Canvas.GetTop(BallMarker), 308, 286, milliseconds: 450);
        AnimateCanvasPosition(GoalkeeperMarker, Canvas.GetLeft(GoalkeeperMarker), Canvas.GetTop(GoalkeeperMarker), 295, 108, milliseconds: 450);
        await Task.Delay(900);
    }

    private static DropShadowEffect CreateBallGlow(Color color, double opacity)
    {
        return new DropShadowEffect
        {
            Color = color,
            BlurRadius = 22,
            ShadowDepth = 0,
            Opacity = opacity
        };
    }

    private void PulseBall()
    {
        if (BallMarker.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var animation = new DoubleAnimation(1.0, 1.24, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static void AnimateCanvasPosition(FrameworkElement element, double fromX, double fromY, double toX, double toY, int milliseconds)
    {
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(fromX, toX, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
        element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
    }

    private static (double X, double Y) GetBallTargetPosition(ShotTarget target, PenaltyOutcome outcome, KeeperDive dive)
    {
        if (outcome == PenaltyOutcome.Saved)
        {
            return dive switch
            {
                KeeperDive.Left => (236, 114),
                KeeperDive.Right => (374, 114),
                _ => (314, 118)
            };
        }

        if (outcome == PenaltyOutcome.Missed)
        {
            return target switch
            {
                ShotTarget.TopLeft => (132, 28),
                ShotTarget.TopRight => (490, 28),
                ShotTarget.BottomLeft => (132, 146),
                ShotTarget.BottomRight => (490, 146),
                ShotTarget.HighCenter => (309, 14),
                _ => (309, 174)
            };
        }

        return target switch
        {
            ShotTarget.TopLeft => (188, 50),
            ShotTarget.TopRight => (438, 50),
            ShotTarget.BottomLeft => (198, 128),
            ShotTarget.BottomRight => (428, 128),
            ShotTarget.HighCenter => (309, 48),
            _ => (309, 104)
        };
    }

    private static string DescribeTarget(ShotTarget target)
    {
        return target switch
        {
            ShotTarget.TopLeft => "toward the top left corner",
            ShotTarget.TopRight => "toward the top right corner",
            ShotTarget.BottomLeft => "low to the bottom left",
            ShotTarget.BottomRight => "low to the bottom right",
            ShotTarget.HighCenter => "high down the middle",
            _ => "down the middle"
        };
    }

    private void AddResultFeed(Player taker, Player? goalkeeper, PenaltyShot shot)
    {
        switch (shot.Outcome)
        {
            case PenaltyOutcome.Goal:
                AddFeed($"GOAL! {CreateGoalDetailText(taker, goalkeeper, shot)} {CreateScoreFeedText()}", FeedTone.Goal);
                break;
            case PenaltyOutcome.Saved:
                AddFeed($"SAVED! {goalkeeper?.Name ?? "The goalkeeper"} gets a hand to {FormatPlayerWithNumber(taker)}'s penalty. {CreateScoreFeedText()}", FeedTone.Miss);
                break;
            default:
                AddFeed($"MISSED! {FormatPlayerWithNumber(taker)} watches it flash wide. {CreateScoreFeedText()}", FeedTone.Miss);
                break;
        }
    }

    private static string CreateGoalDetailText(Player taker, Player? goalkeeper, PenaltyShot shot)
    {
        var goalkeeperName = goalkeeper?.Name ?? "the keeper";
        return KeeperWentRightWay(shot)
            ? $"{goalkeeperName} goes the right way, but {FormatPlayerWithNumber(taker)} finds the finish."
            : $"{FormatPlayerWithNumber(taker)} sends {goalkeeperName} the wrong way.";
    }

    private static bool KeeperWentRightWay(PenaltyShot shot)
    {
        return shot.Target switch
        {
            ShotTarget.TopLeft or ShotTarget.BottomLeft => shot.Dive == KeeperDive.Left,
            ShotTarget.TopRight or ShotTarget.BottomRight => shot.Dive == KeeperDive.Right,
            _ => shot.Dive == KeeperDive.Center
        };
    }

    private static string FormatPlayerWithNumber(Player player)
    {
        return player.SquadNumber > 0 ? $"#{player.SquadNumber} {player.Name}" : player.Name;
    }

    private void AddFeed(string text, FeedTone tone)
    {
        _feedItems.Add(FeedItemViewModel.Create(text, tone));
        LiveFeedListBox.ScrollIntoView(_feedItems[^1]);
    }

    private string CreateScoreFeedText()
    {
        return _homeTeam is null || _awayTeam is null
            ? $"Pens: {_homeAttempts.Count(IsGoal)} - {_awayAttempts.Count(IsGoal)}"
            : $"Pens: {_homeTeam.Name} {_homeAttempts.Count(IsGoal)} - {_awayAttempts.Count(IsGoal)} {_awayTeam.Name}";
    }

    private void UpdateKickDisplay(int round, Player taker, Player? goalkeeper, PenaltyOutcome outcome)
    {
        RoundTextBlock.Text = round <= RequiredTakers ? $"Penalty {round}" : $"Sudden Death - Penalty {round}";
        CurrentResultTextBlock.Text = outcome switch
        {
            PenaltyOutcome.Goal => "GOAL",
            PenaltyOutcome.Saved => "SAVED",
            _ => "MISSED"
        };
        CurrentResultTextBlock.Foreground = outcome == PenaltyOutcome.Goal
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(255, 226, 226));
        KickDuelTextBlock.Text = $"{taker.Name}\nvs\n{goalkeeper?.Name ?? "Goalkeeper"}";
        StatusTextBlock.Text = outcome == PenaltyOutcome.Goal
            ? $"{taker.Name} scores."
            : outcome == PenaltyOutcome.Saved
                ? $"{goalkeeper?.Name ?? "The goalkeeper"} saves from {taker.Name}."
                : $"{taker.Name} misses.";
    }

    private void UpdateNextDuel()
    {
        if (_homeTeam is null || _awayTeam is null || _shootoutCompleted)
        {
            return;
        }

        var attempts = _homeToKick ? _homeAttempts : _awayAttempts;
        var order = _homeToKick ? _homeOrder : _awayOrder;
        var team = _homeToKick ? _homeTeam : _awayTeam;
        var keeperTeam = _homeToKick ? _awayTeam : _homeTeam;
        var round = attempts.Count + 1;
        var taker = GetTakerForAttempt(order, attempts, team, round);
        var goalkeeper = GetGoalkeeper(keeperTeam);
        ApplyKickMarkerTheme(team, keeperTeam);
        RoundTextBlock.Text = round <= RequiredTakers ? $"Penalty {round}" : $"Sudden Death - Penalty {round}";
        KickDuelTextBlock.Text = $"{taker.Name}\nvs\n{goalkeeper?.Name ?? "Goalkeeper"}";
        TakerNumberTextBlock.Text = taker.SquadNumber > 0 ? taker.SquadNumber.ToString() : "?";
    }

    private void ApplyKickMarkerTheme(Team shootingTeam, Team defendingTeam)
    {
        var shootingPalette = GetPaletteForTeam(shootingTeam);
        var defendingPalette = GetPaletteForTeam(defendingTeam);
        if (shootingPalette is not null)
        {
            var takerFill = GetMarkerFillColor(shootingPalette);
            TakerMarker.Background = ToBrush(takerFill);
            TakerMarker.BorderBrush = ToBrush(shootingPalette.BorderColor);
            TakerNumberTextBlock.Foreground = ToBrush(TeamColorService.GetReadableTextColor(takerFill));
        }

        if (defendingPalette is not null)
        {
            var goalkeeperFill = GetMarkerFillColor(defendingPalette);
            GoalkeeperMarker.Background = ToBrush(goalkeeperFill);
            GoalkeeperMarker.BorderBrush = ToBrush(defendingPalette.BorderColor);
            GoalkeeperTextBlock.Foreground = ToBrush(TeamColorService.GetReadableTextColor(goalkeeperFill));
        }
    }

    private static string GetMarkerFillColor(TeamColorPalette palette)
    {
        return palette.IsLight ? palette.BorderColor : palette.PrimaryColor;
    }

    private TeamColorPalette? GetPaletteForTeam(Team team)
    {
        if (_homeTeam is not null && string.Equals(team.Name, _homeTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return _homePalette;
        }

        if (_awayTeam is not null && string.Equals(team.Name, _awayTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return _awayPalette;
        }

        return TeamColorService.GetPalette(team);
    }

    private void UpdateIndicators()
    {
        ReplaceIndicators(_homeIndicators, _homeAttempts);
        ReplaceIndicators(_awayIndicators, _awayAttempts);
    }

    private static void ReplaceIndicators(ObservableCollection<PenaltyIndicatorViewModel> indicators, IReadOnlyList<PenaltyAttempt> attempts)
    {
        var count = Math.Max(RequiredTakers, attempts.Count);
        indicators.Clear();
        for (var index = 0; index < count; index++)
        {
            indicators.Add(index < attempts.Count
                ? PenaltyIndicatorViewModel.FromAttempt(index + 1, attempts[index])
                : PenaltyIndicatorViewModel.Pending(index + 1));
        }
    }

    private void UpdateScoreline()
    {
        ScorelineTextBlock.Text = $"Pens: {_homeAttempts.Count(IsGoal)} - {_awayAttempts.Count(IsGoal)}";
    }

    private Team? TryGetShootoutWinner()
    {
        if (_homeTeam is null || _awayTeam is null)
        {
            return null;
        }

        var homeScore = _homeAttempts.Count(IsGoal);
        var awayScore = _awayAttempts.Count(IsGoal);
        var homeTaken = _homeAttempts.Count;
        var awayTaken = _awayAttempts.Count;

        if (homeTaken < RequiredTakers || awayTaken < RequiredTakers)
        {
            var homeRemaining = Math.Max(0, RequiredTakers - homeTaken);
            var awayRemaining = Math.Max(0, RequiredTakers - awayTaken);
            if (homeScore > awayScore + awayRemaining)
            {
                return _homeTeam;
            }

            if (awayScore > homeScore + homeRemaining)
            {
                return _awayTeam;
            }
        }

        if (homeTaken >= RequiredTakers &&
            awayTaken >= RequiredTakers &&
            homeTaken == awayTaken &&
            homeScore != awayScore)
        {
            return homeScore > awayScore ? _homeTeam : _awayTeam;
        }

        return null;
    }

    private void CompleteShootout(Team winner)
    {
        if (_state.League is null || _state.CurrentFixture is null || _state.CurrentMatch is null || _homeTeam is null || _awayTeam is null)
        {
            return;
        }

        var fixture = _state.CurrentFixture;
        var match = _state.CurrentMatch;
        var homePenalties = _homeAttempts.Count(IsGoal);
        var awayPenalties = _awayAttempts.Count(IsGoal);
        var loser = string.Equals(winner.Name, _homeTeam.Name, StringComparison.OrdinalIgnoreCase) ? _awayTeam : _homeTeam;

        if (!_isPracticeMode)
        {
            fixture.ExtraTimeHomeScore = match.HomeScore;
            fixture.ExtraTimeAwayScore = match.AwayScore;
            fixture.PenaltyHomeScore = homePenalties;
            fixture.PenaltyAwayScore = awayPenalties;
            fixture.WinningTeamName = winner.Name;
            fixture.LosingTeamName = loser.Name;
        }
        match.CurrentPhase = MatchPhase.PenaltyShootout;

        if (!_isPracticeMode)
        {
            _gameSessionService.CompleteSelectedTeamLiveMatch(_state.League, fixture, match);
            RunPostMatchTransferActivity();
        }

        _shootoutCompleted = true;
        _isAutoRunning = false;
        CenterHeaderPanel.Visibility = Visibility.Collapsed;
        PitchViewbox.Visibility = Visibility.Collapsed;
        BottomFeedPanel.Visibility = Visibility.Collapsed;
        FinalResultPanel.Visibility = Visibility.Visible;
        var winnerPalette = string.Equals(winner.Name, _homeTeam.Name, StringComparison.OrdinalIgnoreCase) ? _homePalette : _awayPalette;
        if (winnerPalette is not null)
        {
            FinalResultPanel.Background = ToBrush(winnerPalette.PrimaryColor);
            FinalWinnerTextBlock.Foreground = ToBrush(winnerPalette.TextColor);
            FinalPenaltyScoreTextBlock.Foreground = ToBrush(winnerPalette.TextColor);
            FinalContinueButton.Background = ToBrush(winnerPalette.SecondaryColor);
            FinalContinueButton.Foreground = ToBrush(TeamColorService.GetReadableTextColor(winnerPalette.SecondaryColor));
        }

        FinalWinnerLogoImage.Source = ClubLogoService.LoadClubLogo(winner.Name, _state.League?.LeagueId ?? _state.SelectedLeagueId);
        FinalWinnerTextBlock.Text = $"{winner.Name.ToUpperInvariant()} WIN ON PENALTIES";
        FinalContinueButton.IsEnabled = true;
        CurrentResultTextBlock.Text = $"{winner.Name} WIN";
        RoundTextBlock.Text = "Penalty Shootout Result";
        var winnerPenalties = string.Equals(winner.Name, _homeTeam.Name, StringComparison.OrdinalIgnoreCase) ? homePenalties : awayPenalties;
        var loserPenalties = string.Equals(winner.Name, _homeTeam.Name, StringComparison.OrdinalIgnoreCase) ? awayPenalties : homePenalties;
        FinalPenaltyScoreTextBlock.Text = $"{winnerPenalties} - {loserPenalties}";
        KickDuelTextBlock.Text = $"{winner.Name} {winnerPenalties} - {loserPenalties} {loser.Name} (Pens)";
        StatusTextBlock.Text = _isPracticeMode
            ? $"{winner.Name} win {homePenalties} - {awayPenalties} on penalties. Practice only, fixture not completed."
            : $"{winner.Name} win {homePenalties} - {awayPenalties} on penalties.";
        AddFeed($"{winner.Name.ToUpperInvariant()} WIN ON PENALTIES {winnerPenalties} - {loserPenalties}.", FeedTone.Final);
        ResultBreakdownBorder.Visibility = Visibility.Collapsed;
        HomeBreakdownItemsControl.ItemsSource = CreateBreakdown(_homeAttempts);
        AwayBreakdownItemsControl.ItemsSource = CreateBreakdown(_awayAttempts);
    }

    private List<BreakdownItemViewModel> CreateBreakdown(IEnumerable<PenaltyAttempt> attempts)
    {
        return attempts
            .Select(attempt => new BreakdownItemViewModel(
                $"{(attempt.Outcome == PenaltyOutcome.Goal ? "✓" : "✗")} {attempt.Player.Name}",
                attempt.Outcome == PenaltyOutcome.Goal ? Brushes.LightGreen : Brushes.LightCoral))
            .ToList();
    }

    private List<Player> CreateAiPenaltyOrder(Team team)
    {
        return GetEligiblePlayers(team)
            .OrderByDescending(CalculatePenaltyAttribute)
            .ThenByDescending(CalculateComposure)
            .ThenByDescending(player => player.Finishing)
            .ThenByDescending(GetOverall)
            .ToList();
    }

    private IEnumerable<Player> GetEligiblePlayers(Team team)
    {
        return team.Players
            .Where(player => player.IsOnPitch)
            .Where(player => !player.IsInjured)
            .Where(player => !player.IsSentOff);
    }

    private Player? GetGoalkeeper(Team team)
    {
        return GetEligiblePlayers(team)
            .FirstOrDefault(player => string.Equals(GetDisplayPosition(player), "GK", StringComparison.OrdinalIgnoreCase) ||
                player.Position == Position.Goalkeeper)
            ?? team.Players.FirstOrDefault(player => player.Position == Position.Goalkeeper);
    }

    private Team ResolveUserTeam(Match match)
    {
        if (_state.SelectedTeam is null)
        {
            return match.HomeTeam;
        }

        return string.Equals(match.AwayTeam.Name, _state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase)
            ? match.AwayTeam
            : match.HomeTeam;
    }

    private static int CalculatePenaltyAttribute(Player player)
    {
        var traitBonus = player.Traits.Contains(PlayerTrait.PenaltySpecialist) ? 6 : 0;
        var bigMatchBonus = player.Traits.Contains(PlayerTrait.BigMatchPlayer) ? 3 : 0;
        return Math.Clamp((int)Math.Round(
            player.Finishing * 0.38 +
            player.Shooting * 0.22 +
            player.Attack * 0.16 +
            player.Passing * 0.08 +
            CalculateComposure(player) * 0.12 +
            GetOverall(player) * 0.04 +
            traitBonus +
            bigMatchBonus), 40, 99);
    }

    private static int CalculateComposure(Player player)
    {
        return Math.Clamp((int)Math.Round(
            player.Morale * 0.34 +
            player.CurrentForm * 0.28 +
            GetOverall(player) * 0.24 +
            player.DisciplineRating * 0.14), 35, 99);
    }

    private static int CalculateGoalkeeperPenaltyScore(Player goalkeeper)
    {
        var oneOnOneBonus = goalkeeper.Traits.Contains(PlayerTrait.OneOnOnes) ? 6 : 0;
        return Math.Clamp((int)Math.Round(
            GetOverall(goalkeeper) * 0.45 +
            goalkeeper.Defense * 0.20 +
            goalkeeper.Physical * 0.14 +
            goalkeeper.CurrentForm * 0.12 +
            goalkeeper.Stamina * 0.09 +
            oneOnOneBonus), 45, 99);
    }

    private static double GetFormModifier(Player player)
    {
        return player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 0.04,
            PlayerFormStatus.Good => 0.02,
            PlayerFormStatus.Poor => -0.025,
            PlayerFormStatus.VeryPoor => -0.045,
            _ => 0.0
        };
    }

    private void RunPostMatchTransferActivity()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        _transferMarketService.RunAiTransferActivity(
            _state.TransferMarket,
            _state.League,
            _state.SelectedTeam,
            GetFixtureCalendarRound(_state.CurrentFixture));
    }

    private void RestorePracticeState()
    {
        _state.CurrentMatch = _previousMatch;
        _state.CurrentLiveMatchSegment = _previousSegment;
    }

    private static bool IsGoal(PenaltyAttempt attempt)
    {
        return attempt.Outcome == PenaltyOutcome.Goal;
    }

    private static string GetDisplayPosition(Player player)
    {
        return string.IsNullOrWhiteSpace(player.AssignedPosition)
            ? string.IsNullOrWhiteSpace(player.PreferredPosition) ? player.Position.ToString() : player.PreferredPosition
            : player.AssignedPosition;
    }

    private static int GetOverall(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
    }

    private static Color GetPenaltyBadgeColor(int penalty)
    {
        return penalty >= 85 ? Color.FromRgb(22, 163, 74) :
            penalty >= 70 ? Color.FromRgb(37, 99, 235) :
            penalty >= 55 ? Color.FromRgb(217, 119, 6) :
            Color.FromRgb(220, 38, 38);
    }

    private static string GetShortName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? name : parts[^1];
    }

    private static string CreatePlayerKey(Player player)
    {
        return string.IsNullOrWhiteSpace(player.PlayerId) ? $"{player.Name}|{player.SquadNumber}" : player.PlayerId;
    }

    private static Brush ToBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private static string CreateTransparentColor(string color, byte alpha)
    {
        var normalized = color.Trim().TrimStart('#');
        if (normalized.Length != 6)
        {
            normalized = "2563EB";
        }

        return $"#{alpha:X2}{normalized}";
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private sealed class TakerOption
    {
        public TakerOption(
            Player player,
            string name,
            string position,
            string overallText,
            string penaltyText,
            int penaltyAttribute,
            bool canSelect,
            Brush cardBackground,
            Brush borderBrush,
            double borderThickness,
            Brush nameForeground,
            Brush metaForeground,
            Brush positionBackground,
            Brush penaltyBackground,
            Brush defaultBorderBrush,
            Brush selectionBorderBrush,
            Brush selectedBackground)
        {
            Player = player;
            Name = name;
            Position = position;
            OverallText = overallText;
            PenaltyText = penaltyText;
            PenaltyAttribute = penaltyAttribute;
            CanSelect = canSelect;
            CardBackground = cardBackground;
            BorderBrush = borderBrush;
            BorderThickness = borderThickness;
            NameForeground = nameForeground;
            MetaForeground = metaForeground;
            PositionBackground = positionBackground;
            PenaltyBackground = penaltyBackground;
            DefaultBorderBrush = defaultBorderBrush;
            SelectionBorderBrush = selectionBorderBrush;
            SelectedBackground = selectedBackground;
        }

        public Player Player { get; }
        public string Name { get; }
        public string Position { get; }
        public string OverallText { get; }
        public string PenaltyText { get; }
        public int PenaltyAttribute { get; }
        public bool CanSelect { get; }
        public bool IsSelected { get; set; }
        public Brush CardBackground { get; set; }
        public Brush BorderBrush { get; set; }
        public double BorderThickness { get; set; }
        public Brush NameForeground { get; }
        public Brush MetaForeground { get; }
        public Brush PositionBackground { get; }
        public Brush PenaltyBackground { get; }
        public Brush DefaultBorderBrush { get; }
        public Brush SelectionBorderBrush { get; }
        public Brush SelectedBackground { get; }
    }

    private sealed record PenaltyIndicatorViewModel(Brush Fill, Brush Stroke, string Tooltip)
    {
        public static PenaltyIndicatorViewModel Pending(int number)
        {
            return new PenaltyIndicatorViewModel(
                new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Brushes.White,
                $"Penalty {number}: not taken");
        }

        public static PenaltyIndicatorViewModel FromAttempt(int number, PenaltyAttempt attempt)
        {
            var isGoal = attempt.Outcome == PenaltyOutcome.Goal;
            return new PenaltyIndicatorViewModel(
                isGoal ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                Brushes.White,
                $"Penalty {number}: {attempt.Player.Name} - {attempt.Outcome}");
        }
    }

    private sealed record OrderSlotViewModel(
        int Number,
        string PlayerShortName,
        Brush Background,
        Brush BorderBrush,
        Brush NumberForeground,
        Brush PlayerForeground);
    private sealed record BreakdownItemViewModel(string Text, Brush Foreground);
    private sealed record FeedItemViewModel(
        string Icon,
        string MinuteText,
        string EventLabel,
        string Title,
        string Description,
        Brush RowBackground,
        Brush RowBorderBrush,
        Brush IconBackground,
        Brush IconForeground,
        Brush MinuteForeground,
        Brush LabelBackground,
        Brush LabelForeground,
        Brush TitleForeground,
        Brush DescriptionForeground)
    {
        public static FeedItemViewModel Create(string text, FeedTone tone)
        {
            var (title, description) = SplitFeedText(text);
            var style = tone switch
            {
                FeedTone.Start => new FeedStyle("●", "START", "#0F172A", "#020617", "#1E293B", "#FFFFFF", "#FFFFFF", "#020617", "#FFFFFF", "#FFFFFF", "#CBD5E1"),
                FeedTone.Step => new FeedStyle("⚪", "STEP UP", "#FFFFFF", "#CBD5E1", "#F8FAFC", "#0F172A", "#0F172A", "#E2E8F0", "#0F172A", "#0F172A", "#334155"),
                FeedTone.Shot => new FeedStyle("🎯", "SHOT", "#F97316", "#C2410C", "#FFEDD5", "#C2410C", "#FFFFFF", "#C2410C", "#FFFFFF", "#FFFFFF", "#FFEDD5"),
                FeedTone.Goal => new FeedStyle("⚽", "GOAL", "#16A34A", "#15803D", "#DCFCE7", "#166534", "#FFFFFF", "#15803D", "#FFFFFF", "#FFFFFF", "#DCFCE7"),
                FeedTone.Miss => new FeedStyle("🧤", "SAVED / MISS", "#DC2626", "#991B1B", "#FEE2E2", "#991B1B", "#FFFFFF", "#991B1B", "#FFFFFF", "#FFFFFF", "#FEE2E2"),
                FeedTone.Final => new FeedStyle("★", "RESULT", "#F97316", "#C2410C", "#FFEDD5", "#C2410C", "#FFFFFF", "#C2410C", "#FFFFFF", "#FFFFFF", "#FFEDD5"),
                _ => new FeedStyle("⚽", "PENALTY", "#FFFFFF", "#CBD5E1", "#F8FAFC", "#0F172A", "#0F172A", "#E2E8F0", "#0F172A", "#0F172A", "#334155")
            };

            return new FeedItemViewModel(
                style.Icon,
                "Pens",
                style.Label,
                title,
                description,
                ToBrush(style.RowBackground),
                ToBrush(style.RowBorder),
                ToBrush(style.IconBackground),
                ToBrush(style.IconForeground),
                ToBrush(style.MinuteForeground),
                ToBrush(style.LabelBackground),
                ToBrush(style.LabelForeground),
                ToBrush(style.TitleForeground),
                ToBrush(style.DescriptionForeground));
        }

        private static (string Title, string Description) SplitFeedText(string text)
        {
            var trimmed = text.Trim();
            var scoreIndex = trimmed.IndexOf(" Pens:", StringComparison.OrdinalIgnoreCase);
            if (scoreIndex > 0)
            {
                return (trimmed[..scoreIndex].Trim(), trimmed[(scoreIndex + 1)..].Trim());
            }

            var sentenceIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceIndex > 0 && sentenceIndex + 2 < trimmed.Length)
            {
                return (trimmed[..(sentenceIndex + 1)].Trim(), trimmed[(sentenceIndex + 2)..].Trim());
            }

            return (trimmed, string.Empty);
        }
    }

    private sealed record FeedStyle(
        string Icon,
        string Label,
        string RowBackground,
        string RowBorder,
        string IconBackground,
        string IconForeground,
        string MinuteForeground,
        string LabelBackground,
        string LabelForeground,
        string TitleForeground = "#FFFFFF",
        string DescriptionForeground = "#E5E7EB");

    private sealed record PenaltyShot(PenaltyOutcome Outcome, ShotTarget Target, KeeperDive Dive);
    private sealed record PenaltyAttempt(Player Player, PenaltyOutcome Outcome);

    private enum FeedTone
    {
        Neutral,
        Start,
        Step,
        Shot,
        Goal,
        Miss,
        Final
    }

    private enum ShotTarget
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Center,
        HighCenter
    }

    private enum KeeperDive
    {
        Left,
        Right,
        Center
    }

    private enum PenaltyOutcome
    {
        Goal,
        Saved,
        Missed
    }
}
