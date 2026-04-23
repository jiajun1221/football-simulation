using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class MatchResultView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly PostMatchAnalysisService _postMatchAnalysisService = new();

    public MatchResultView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadResult();
    }

    private void LoadResult()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        var match = _state.CurrentMatch;
        var summary = _postMatchAnalysisService.CreateSummary(match);

        HomeTeamTextBlock.Text = match.HomeTeam.Name;
        AwayTeamTextBlock.Text = match.AwayTeam.Name;
        ScoreTextBlock.Text = $"{match.HomeScore} - {match.AwayScore}";
        HomePlayersTitleTextBlock.Text = $"{match.HomeTeam.Name} Players";
        AwayPlayersTitleTextBlock.Text = $"{match.AwayTeam.Name} Players";

        HomeScorersItemsControl.ItemsSource = CreateGoalSummaryRows(match, match.HomeTeam);
        AwayScorersItemsControl.ItemsSource = CreateGoalSummaryRows(match, match.AwayTeam);
        HomePlayersItemsControl.ItemsSource = CreatePlayerRows(summary.PlayerPerformances, match.HomeTeam, summary.ManOfTheMatch);
        AwayPlayersItemsControl.ItemsSource = CreatePlayerRows(summary.PlayerPerformances, match.AwayTeam, summary.ManOfTheMatch);
        StatsComparisonItemsControl.ItemsSource = CreateStatComparisonRows(match);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(new RoundResultView(_state, _navigate));
    }

    private static List<string> CreateGoalSummaryRows(Match match, Team team)
    {
        var scoringEvents = match.Events
            .Where(IsScoringEvent)
            .Where(matchEvent => FindScoringTeam(matchEvent, match) == team.Name)
            .OrderBy(matchEvent => matchEvent.Minute)
            .Select(matchEvent => $"{matchEvent.Minute}' {matchEvent.PrimaryPlayerName ?? "Unknown scorer"}")
            .ToList();

        return scoringEvents.Count == 0 ? [] : scoringEvents;
    }

    private static bool IsScoringEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType == EventType.Goal
            || matchEvent.EventType == EventType.WonderGoal
            || (matchEvent.EventType == EventType.Penalty
                && matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindScoringTeam(MatchEvent matchEvent, Match match)
    {
        if (matchEvent.Description.Contains($"for {match.HomeTeam.Name}", StringComparison.OrdinalIgnoreCase))
        {
            return match.HomeTeam.Name;
        }

        if (matchEvent.Description.Contains($"for {match.AwayTeam.Name}", StringComparison.OrdinalIgnoreCase))
        {
            return match.AwayTeam.Name;
        }

        return string.Empty;
    }

    private static List<PlayerPerformanceRow> CreatePlayerRows(
        List<PlayerMatchPerformance> performances,
        Team team,
        PlayerMatchPerformance? manOfTheMatch)
    {
        return performances
            .Where(performance => performance.TeamName == team.Name)
            .OrderByDescending(performance => performance.Rating)
            .ThenByDescending(performance => performance.Goals + performance.Assists)
            .ThenByDescending(performance => performance.Saves)
            .ThenBy(performance => performance.PlayerName)
            .Select(performance => CreatePlayerRow(performance, manOfTheMatch))
            .ToList();
    }

    private static PlayerPerformanceRow CreatePlayerRow(
        PlayerMatchPerformance performance,
        PlayerMatchPerformance? manOfTheMatch)
    {
        var isMotm = manOfTheMatch is not null
            && manOfTheMatch.PlayerName == performance.PlayerName
            && manOfTheMatch.TeamName == performance.TeamName;

        return new PlayerPerformanceRow
        {
            PlayerName = performance.PlayerName,
            PositionText = GetPositionText(performance.Position),
            RatingText = performance.Rating.ToString("0.0"),
            StatusText = GetSubStatus(performance),
            GoalText = performance.Goals > 1 ? $"⚽ {performance.Goals}" : "⚽",
            AssistText = performance.Assists > 1 ? $"A {performance.Assists}" : "A",
            YellowText = performance.YellowCards > 1 ? $"Y {performance.YellowCards}" : "Y",
            RedText = performance.RedCards > 1 ? $"R {performance.RedCards}" : "R",
            GoalVisibility = performance.Goals > 0 ? Visibility.Visible : Visibility.Collapsed,
            AssistVisibility = performance.Assists > 0 ? Visibility.Visible : Visibility.Collapsed,
            YellowVisibility = performance.YellowCards > 0 ? Visibility.Visible : Visibility.Collapsed,
            RedVisibility = performance.RedCards > 0 ? Visibility.Visible : Visibility.Collapsed,
            MotmIcon = "★",
            MotmVisibility = isMotm ? Visibility.Visible : Visibility.Collapsed,
            RowBackground = isMotm ? "#FFF8D6" : "#F8FAFC",
            BorderBrush = isMotm ? "#E3A500" : "#E2E8F0",
            BorderThickness = isMotm ? new Thickness(2) : new Thickness(1)
        };
    }

    private static List<StatComparisonRow> CreateStatComparisonRows(Match match)
    {
        return
        [
            new("Possession", $"{match.HomeStats.PossessionPercentage:0.0}%", $"{match.AwayStats.PossessionPercentage:0.0}%"),
            new("Shots", match.HomeStats.TotalShots.ToString(), match.AwayStats.TotalShots.ToString()),
            new("Shots on Target", match.HomeStats.ShotsOnTarget.ToString(), match.AwayStats.ShotsOnTarget.ToString()),
            new("Passes", match.HomeStats.Passes.ToString(), match.AwayStats.Passes.ToString()),
            new("Pass Accuracy", $"{match.HomeStats.PassAccuracyPercentage:0.0}%", $"{match.AwayStats.PassAccuracyPercentage:0.0}%"),
            new("xG", match.HomeStats.ExpectedGoals.ToString("0.0"), match.AwayStats.ExpectedGoals.ToString("0.0")),
            new("Fouls", match.HomeStats.Fouls.ToString(), match.AwayStats.Fouls.ToString()),
            new("Yellow Cards", match.HomeStats.YellowCards.ToString(), match.AwayStats.YellowCards.ToString()),
            new("Red Cards", match.HomeStats.RedCards.ToString(), match.AwayStats.RedCards.ToString()),
            new("Offsides", match.HomeStats.Offsides.ToString(), match.AwayStats.Offsides.ToString()),
            new("Corners", match.HomeStats.Corners.ToString(), match.AwayStats.Corners.ToString())
        ];
    }

    private static string GetSubStatus(PlayerMatchPerformance performance)
    {
        if (performance.WasSubbedOn)
        {
            return $"Sub on {performance.SubstitutionMinute}'";
        }

        if (performance.WasSubbedOff)
        {
            return $"Sub off {performance.SubstitutionMinute}'";
        }

        return performance.WasSubstitute ? "Bench" : "Starter";
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

    private sealed class PlayerPerformanceRow
    {
        public string PlayerName { get; init; } = string.Empty;
        public string PositionText { get; init; } = string.Empty;
        public string RatingText { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public string GoalText { get; init; } = string.Empty;
        public string AssistText { get; init; } = string.Empty;
        public string YellowText { get; init; } = string.Empty;
        public string RedText { get; init; } = string.Empty;
        public string MotmIcon { get; init; } = string.Empty;
        public Visibility GoalVisibility { get; init; }
        public Visibility AssistVisibility { get; init; }
        public Visibility YellowVisibility { get; init; }
        public Visibility RedVisibility { get; init; }
        public Visibility MotmVisibility { get; init; }
        public string RowBackground { get; init; } = "#F8FAFC";
        public string BorderBrush { get; init; } = "#E2E8F0";
        public Thickness BorderThickness { get; init; } = new(1);
    }

    private sealed record StatComparisonRow(string Label, string HomeValue, string AwayValue);
}
