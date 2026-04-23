using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class MatchLiveView : UserControl
{
    private const int VeryFastBaseDelayMilliseconds = 100;
    private const int FastBaseDelayMilliseconds = 3000;
    private const int MediumBaseDelayMilliseconds = 6000;
    private const int SlowBaseDelayMilliseconds = 8000;
    private const int VerySlowBaseDelayMilliseconds = 10000;

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly bool _isSecondHalf;
    private readonly GameSessionService _gameSessionService = new();
    private readonly ObservableCollection<MatchFeedItem> _visibleEvents = [];
    private readonly CancellationTokenSource _playbackCancellation = new();
    private bool _hasNavigated;
    private readonly List<MatchSpeedOption> _speedOptions =
    [
        new(MatchSpeed.VeryFast, "Very Fast (0.1 sec)"),
        new(MatchSpeed.Fast, "Fast (3 sec)"),
        new(MatchSpeed.Medium, "Medium (6 sec)"),
        new(MatchSpeed.Slow, "Slow (8 sec)"),
        new(MatchSpeed.VerySlow, "Very Slow (10 sec)")
    ];

    public MatchLiveView(GameFlowState state, Action<UserControl> navigate, bool isSecondHalf)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        _isSecondHalf = isSecondHalf;
        MatchEventsListBox.ItemsSource = _visibleEvents;
        InitializeSpeedSelector();
        PrepareContinueButton();

        Loaded += MatchLiveView_Loaded;
        Unloaded += MatchLiveView_Unloaded;
    }

    private async void MatchLiveView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MatchLiveView_Loaded;

        if (_isSecondHalf)
        {
            await RunSecondHalfAsync();
            return;
        }

        await RunFirstHalfAsync();
    }

    private void MatchLiveView_Unloaded(object sender, RoutedEventArgs e)
    {
        _playbackCancellation.Cancel();
    }

    private void InitializeSpeedSelector()
    {
        SpeedComboBox.ItemsSource = _speedOptions;
        SpeedComboBox.DisplayMemberPath = nameof(MatchSpeedOption.Label);
        SpeedComboBox.SelectedItem = _speedOptions.First(option => option.Speed == _state.CurrentMatchSpeed);
    }

    private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedComboBox.SelectedItem is MatchSpeedOption selectedOption)
        {
            _state.CurrentMatchSpeed = selectedOption.Speed;
        }
    }

    private void PrepareContinueButton()
    {
        ContinueButton.Content = _isSecondHalf
            ? "View Result"
            : "Continue";
        ContinueButton.Visibility = Visibility.Collapsed;
        ContinueButton.IsEnabled = false;
    }

    private void ShowContinueButton()
    {
        ContinueButton.Visibility = Visibility.Visible;
        ContinueButton.IsEnabled = true;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToNextPhase();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToNextPhase();
    }

    private void NavigateToNextPhase()
    {
        if (_hasNavigated)
        {
            return;
        }

        _hasNavigated = true;
        _playbackCancellation.Cancel();

        if (_isSecondHalf)
        {
            _navigate(new MatchResultView(_state, _navigate));
            return;
        }

        _navigate(new HalfTimeView(_state, _navigate));
    }

    private async Task RunFirstHalfAsync()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return;
        }

        PhaseTextBlock.Text = "First Half";
        SetScoreboardTeams(_state.CurrentFixture.HomeTeam, _state.CurrentFixture.AwayTeam);
        SetScore(0, 0);

        _state.CurrentMatch = _gameSessionService.SimulateSelectedTeamFirstHalf(_state.League, _state.SelectedTeam);
        RefreshFatiguePanel(_state.CurrentMatch);

        var completed = await ShowEventsAsync(_state.CurrentMatch.Events, _state.CurrentMatch, _playbackCancellation.Token);
        if (completed && !_hasNavigated)
        {
            ShowContinueButton();
        }
    }

    private async Task RunSecondHalfAsync()
    {
        if (_state.League is null || _state.CurrentFixture is null || _state.CurrentMatch is null)
        {
            return;
        }

        PhaseTextBlock.Text = "Second Half";
        SetScoreboardTeams(_state.CurrentMatch.HomeTeam, _state.CurrentMatch.AwayTeam);
        SetScore(_state.CurrentMatch.HomeScore, _state.CurrentMatch.AwayScore);

        var existingEventCount = _state.CurrentMatch.Events.Count;
        _state.CurrentMatch = _gameSessionService.SimulateSelectedTeamSecondHalf(
            _state.League,
            _state.CurrentFixture,
            _state.CurrentMatch,
            _state.SelectedTeam);
        RefreshFatiguePanel(_state.CurrentMatch);

        var completed = await ShowEventsAsync(_state.CurrentMatch.Events.Skip(existingEventCount), _state.CurrentMatch, _playbackCancellation.Token);
        if (completed && !_hasNavigated)
        {
            ShowContinueButton();
        }
    }

    private async Task<bool> ShowEventsAsync(IEnumerable<MatchEvent> eventsToShow, Match match, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var matchEvent in eventsToShow.OrderBy(matchEvent => matchEvent.Minute))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var feedItem = CreateFeedItem(matchEvent, match);
                InsertFeedItemAtTop(feedItem);

                if (matchEvent.HomeScore.HasValue && matchEvent.AwayScore.HasValue)
                {
                    SetScore(matchEvent.HomeScore.Value, matchEvent.AwayScore.Value);
                }

                RefreshFatiguePanel(match);

                if (feedItem.IsGoal)
                {
                    await PlayGoalEffectAsync(feedItem.TeamName, cancellationToken);
                }

                await Task.Delay(GetDelayFor(feedItem), cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
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

    private void RefreshFatiguePanel(Match match)
    {
        var tiredPlayers = match.HomeTeam.Players
            .Concat(match.AwayTeam.Players)
            .Select(player => new
            {
                TeamName = match.HomeTeam.Players.Contains(player) ? match.HomeTeam.Name : match.AwayTeam.Name,
                Player = player,
                Fatigue = GetFatiguePercentage(player)
            })
            .Where(item => item.Fatigue >= 55)
            .OrderByDescending(item => item.Fatigue)
            .Take(5)
            .ToList();

        var substitutions = match.Substitutions
            .OrderByDescending(substitution => substitution.Minute)
            .Take(3)
            .Select(substitution => $"{substitution.Minute}' {substitution.PlayerOnName} on for {substitution.PlayerOffName}");

        var fatigueText = tiredPlayers.Count == 0
            ? "Fatigue watch: no major tired players yet."
            : "Fatigue watch: " + string.Join(" | ", tiredPlayers.Select(item =>
                $"{item.Player.Name} ({item.TeamName}) {item.Fatigue}%"));

        var substitutionText = match.Substitutions.Count == 0
            ? "AI substitutions: none yet."
            : "Recent substitutions: " + string.Join(" | ", substitutions);

        FatigueStatusTextBlock.Text = $"{fatigueText}\n{substitutionText}";
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
        var eventStyle = GetEventStyle(matchEvent.EventType);

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
            || (matchEvent.EventType == EventType.Penalty
                && matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase));
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
            EventType.Injury => new FeedEventStyle("🚑", "INJURY", "#FFEFEF", "#E28585", "#FFDCDC", "#FFD1D1", "#8F1F1F"),
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

    private int GetDelayFor(MatchFeedItem feedItem)
    {
        var baseDelay = GetBaseDelayMilliseconds(_state.CurrentMatchSpeed);
        var extraDelay = GetEventExtraDelayMilliseconds(feedItem.Type);

        return baseDelay + extraDelay;
    }

    private static int GetBaseDelayMilliseconds(MatchSpeed matchSpeed)
    {
        return matchSpeed switch
        {
            MatchSpeed.VeryFast => VeryFastBaseDelayMilliseconds,
            MatchSpeed.Fast => FastBaseDelayMilliseconds,
            MatchSpeed.Medium => MediumBaseDelayMilliseconds,
            MatchSpeed.Slow => SlowBaseDelayMilliseconds,
            MatchSpeed.VerySlow => VerySlowBaseDelayMilliseconds,
            _ => VerySlowBaseDelayMilliseconds
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

    private sealed record MatchSpeedOption(MatchSpeed Speed, string Label);
}
