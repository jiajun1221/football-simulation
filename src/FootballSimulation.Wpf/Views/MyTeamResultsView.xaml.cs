using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class MyTeamResultsView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly PostMatchAnalysisService _postMatchAnalysisService = new();

    public MyTeamResultsView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();
        _state = state;
        _navigate = navigate;
        LoadResults();
    }

    private void LoadResults()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        var rows = _state.League.Fixtures
            .Where(fixture => fixture.IsPlayed && fixture.Result is not null && IsTeamInFixture(fixture, _state.SelectedTeam))
            .OrderByDescending(GetFixtureCalendarRound)
            .ThenByDescending(fixture => fixture.Competition)
            .Select(fixture => CreateResultRow(fixture, _state.SelectedTeam))
            .ToList();

        TitleTextBlock.Text = $"{_state.SelectedTeam.Name} Match History";
        SubtitleTextBlock.Text = $"{_state.League.Name} | Season {FormatSeasonLabel(_state.League.Season)} | {rows.Count} completed matches";
        ResultsDataGrid.ItemsSource = rows;
        ResultsDataGrid.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTextBlock.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadRecordSummary(rows);
    }

    private void LoadRecordSummary(IReadOnlyCollection<ResultRow> rows)
    {
        var wins = rows.Count(row => row.ResultText == "W");
        var draws = rows.Count(row => row.ResultText == "D");
        var losses = rows.Count(row => row.ResultText == "L");
        var goalsFor = rows.Sum(row => row.SelectedGoals);
        var goalsAgainst = rows.Sum(row => row.OpponentGoals);

        RecordTextBlock.Text = $"Record: {wins}W {draws}D {losses}L";
        GoalsTextBlock.Text = $"Goals: {goalsFor} for, {goalsAgainst} against, GD {FormatGoalDifference(goalsFor - goalsAgainst)}";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(new DashboardView(_state, _navigate));
    }

    private ResultRow CreateResultRow(Fixture fixture, Team selectedTeam)
    {
        var result = fixture.Result!;
        var isHome = fixture.HomeTeam.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase);
        var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;
        var selectedGoals = isHome ? result.HomeScore : result.AwayScore;
        var opponentGoals = isHome ? result.AwayScore : result.HomeScore;
        var resultText = selectedGoals > opponentGoals ? "W" : selectedGoals < opponentGoals ? "L" : "D";
        var playerOfTheMatch = GetPlayerOfTheMatch(result);
        var leagueId = _state.League?.LeagueId ?? _state.SelectedLeagueId;

        return new ResultRow
        {
            RoundText = GetFixtureRoundText(fixture),
            CompetitionText = CompetitionDisplayService.GetShortName(fixture.Competition),
            ResultText = resultText,
            ResultBrush = GetResultBrush(resultText),
            ScoreText = CreateScoreText(fixture, isHome),
            OpponentName = opponent.Name,
            OpponentLogoPath = ClubLogoService.GetClubLogoPath(opponent.Name, leagueId),
            HomeAwayText = isHome ? "Home match" : "Away match",
            VenueText = isHome ? "Home" : "Away",
            PlayerOfTheMatchName = playerOfTheMatch?.PlayerName ?? "-",
            PlayerOfTheMatchLogoPath = ClubLogoService.GetClubLogoPath(playerOfTheMatch?.TeamName ?? string.Empty, leagueId),
            NotesText = CreateNotesText(fixture, resultText),
            SelectedGoals = selectedGoals,
            OpponentGoals = opponentGoals,
            RowBackground = resultText switch
            {
                "W" => "#DCFCE7",
                "L" => ThemeManager.GetBrushHex("FeedAttackBackground", "#FEE2E2"),
                _ => ThemeManager.GetBrushHex("TableRowBackground", "#F8FAFC")
            }
        };
    }

    private PlayerMatchPerformance? GetPlayerOfTheMatch(Match match)
    {
        if (match.PlayerPerformances.Count == 0)
        {
            return null;
        }

        return _postMatchAnalysisService.CreateSummary(match).ManOfTheMatch;
    }

    private static string CreateScoreText(Fixture fixture, bool selectedTeamIsHome)
    {
        var result = fixture.Result!;
        var selectedGoals = selectedTeamIsHome ? result.HomeScore : result.AwayScore;
        var opponentGoals = selectedTeamIsHome ? result.AwayScore : result.HomeScore;
        var score = $"{selectedGoals} - {opponentGoals}";

        if (fixture.PenaltyHomeScore.HasValue && fixture.PenaltyAwayScore.HasValue)
        {
            var selectedPens = selectedTeamIsHome ? fixture.PenaltyHomeScore.Value : fixture.PenaltyAwayScore.Value;
            var opponentPens = selectedTeamIsHome ? fixture.PenaltyAwayScore.Value : fixture.PenaltyHomeScore.Value;
            return $"{score} ({selectedPens}-{opponentPens} pens)";
        }

        if (fixture.ExtraTimeHomeScore.HasValue && fixture.ExtraTimeAwayScore.HasValue)
        {
            var selectedExtra = selectedTeamIsHome ? fixture.ExtraTimeHomeScore.Value : fixture.ExtraTimeAwayScore.Value;
            var opponentExtra = selectedTeamIsHome ? fixture.ExtraTimeAwayScore.Value : fixture.ExtraTimeHomeScore.Value;
            if (selectedExtra != selectedGoals || opponentExtra != opponentGoals)
            {
                return $"{selectedExtra} - {opponentExtra} AET";
            }
        }

        return score;
    }

    private static string CreateNotesText(Fixture fixture, string resultText)
    {
        if (fixture.IsKnockout && !string.IsNullOrWhiteSpace(fixture.WinningTeamName))
        {
            return $"{fixture.WinningTeamName} advanced";
        }

        return resultText switch
        {
            "W" => "Win",
            "L" => "Defeat",
            _ => "Draw"
        };
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static string GetFixtureRoundText(Fixture fixture)
    {
        return string.IsNullOrWhiteSpace(fixture.RoundName)
            ? $"Round {fixture.RoundNumber}"
            : fixture.RoundName;
    }

    private static string GetResultBrush(string resultText)
    {
        return resultText switch
        {
            "W" => "#16A34A",
            "L" => "#DC2626",
            _ => "#64748B"
        };
    }

    private static string FormatGoalDifference(int goalDifference)
    {
        return goalDifference > 0 ? $"+{goalDifference}" : goalDifference.ToString();
    }

    private static string FormatSeasonLabel(string season)
    {
        return string.IsNullOrWhiteSpace(season)
            ? "-"
            : season.Trim().Replace('-', '/');
    }

    private sealed class ResultRow
    {
        public string RoundText { get; init; } = string.Empty;
        public string CompetitionText { get; init; } = string.Empty;
        public string ResultText { get; init; } = string.Empty;
        public string ResultBrush { get; init; } = "#64748B";
        public string ScoreText { get; init; } = string.Empty;
        public string OpponentName { get; init; } = string.Empty;
        public string OpponentLogoPath { get; init; } = string.Empty;
        public string HomeAwayText { get; init; } = string.Empty;
        public string VenueText { get; init; } = string.Empty;
        public string PlayerOfTheMatchName { get; init; } = string.Empty;
        public string PlayerOfTheMatchLogoPath { get; init; } = string.Empty;
        public string NotesText { get; init; } = string.Empty;
        public int SelectedGoals { get; init; }
        public int OpponentGoals { get; init; }
        public string RowBackground { get; init; } = "#F8FAFC";
    }
}
