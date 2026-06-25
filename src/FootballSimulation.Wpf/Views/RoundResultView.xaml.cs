using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class RoundResultView : UserControl
{
    private const string ClubsAssetPath = "Assets/Clubs";
    private const string DefaultLogoPath = "pack://application:,,,/Assets/Clubs/default.png";

    private static readonly Dictionary<string, string> ImportedLogoFileNames = new()
    {
        ["AFC Bournemouth"] = "AFC Bournemouth.png",
        ["Arsenal"] = "Arsenal FC.png",
        ["Aston Villa"] = "Aston Villa.png",
        ["Brentford"] = "Brentford FC.png",
        ["Brighton & Hove Albion"] = "Brighton Hove Albion.png",
        ["Burnley"] = "Burnley FC.png",
        ["Chelsea"] = "Chelsea FC.png",
        ["Crystal Palace"] = "Crystal Palace.png",
        ["Everton"] = "Everton FC.png",
        ["Fulham"] = "Fulham FC.png",
        ["Leeds United"] = "Leeds United.png",
        ["Liverpool"] = "Liverpool FC.png",
        ["Manchester City"] = "Manchester City.png",
        ["Manchester United"] = "Manchester United.png",
        ["Newcastle United"] = "Newcastle United.png",
        ["Nottingham Forest"] = "Nottingham Forest.png",
        ["Sunderland"] = "Sunderland AFC.png",
        ["Tottenham Hotspur"] = "Tottenham Hotspur.png",
        ["West Ham United"] = "West Ham United.png",
        ["Wolverhampton Wanderers"] = "Wolverhampton Wanderers.png"
    };

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly SeasonCompletionService _seasonCompletionService = new();

    public RoundResultView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadRoundResult();
    }

    private void LoadRoundResult()
    {
        if (_state.League is null || _state.CurrentFixture is null)
        {
            return;
        }

        var currentSlot = GetFixtureCalendarRound(_state.CurrentFixture);
        TitleTextBlock.Text = $"{CompetitionDisplayService.GetName(_state.CurrentFixture.Competition)} - {GetFixtureRoundText(_state.CurrentFixture)} Results";
        RoundResultsListBox.ItemsSource = _state.League.Fixtures
            .Where(fixture => GetFixtureCalendarRound(fixture) == currentSlot)
            .OrderBy(fixture => fixture.Competition)
            .Select(fixture => CreateRoundResultRow(fixture, _state.SelectedTeam))
            .ToList();
        if (ShouldShowMatchBracket(_state.CurrentFixture))
        {
            RightPanelTitleTextBlock.Text = "Match Bracket";
            LeagueTableDataGrid.Visibility = Visibility.Collapsed;
            BracketScrollViewer.Visibility = Visibility.Visible;
            BracketItemsControl.ItemsSource = CreateBracketRoundGroups(_state.League, _state.CurrentFixture, _state.SelectedTeam);
            return;
        }

        RightPanelTitleTextBlock.Text = "Updated League Table";
        BracketScrollViewer.Visibility = Visibility.Collapsed;
        LeagueTableDataGrid.Visibility = Visibility.Visible;
        LeagueTableDataGrid.ItemsSource = CreateLeagueTableRows(_state.League, _state.SelectedTeam);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        var isSeasonComplete = _seasonCompletionService.IsSelectedTeamSeasonComplete(_state.League, _state.SelectedTeam);
        if (isSeasonComplete && _state.League is not null)
        {
            _state.League.IsCompleted = true;
        }

        _state.CurrentFixture = null;
        _state.CurrentMatch = null;
        _navigate(new DashboardView(_state, _navigate));
    }

    private RoundResultRow CreateRoundResultRow(Fixture fixture, Team? selectedTeam)
    {
        var isUserMatch = selectedTeam is not null &&
            (string.Equals(fixture.HomeTeam.Name, selectedTeam.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fixture.AwayTeam.Name, selectedTeam.Name, StringComparison.OrdinalIgnoreCase));

        return new RoundResultRow
        {
            HomeTeamName = fixture.HomeTeam.Name,
            AwayTeamName = fixture.AwayTeam.Name,
            HomeLogoPath = GetClubLogoPath(fixture.HomeTeam.Name),
            AwayLogoPath = GetClubLogoPath(fixture.AwayTeam.Name),
            ScoreText = CreateScoreText(fixture),
            RowBackground = isUserMatch
                ? ThemeManager.GetBrushHex("TableCurrentClubBackground", "#5A3D12")
                : ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#111827"),
            BorderBrush = isUserMatch
                ? ThemeManager.GetBrushHex("AppHighlightBrush", "#6B4A16")
                : ThemeManager.GetBrushHex("AppBorderBrush", "#243247"),
            BorderThickness = isUserMatch ? new Thickness(2) : new Thickness(1)
        };
    }

    private List<LeagueTableRow> CreateLeagueTableRows(League league, Team? selectedTeam)
    {
        return league.Table
            .Select(entry => new LeagueTableRow
            {
                Club = entry.TeamName,
                LogoPath = GetClubLogoPath(entry.TeamName),
                Played = entry.Played,
                Wins = entry.Wins,
                Draws = entry.Draws,
                Losses = entry.Losses,
                GoalsFor = entry.GoalsFor,
                GoalsAgainst = entry.GoalsAgainst,
                GoalDifference = entry.GoalDifference,
                Points = entry.Points,
                IsSelectedTeam = selectedTeam is not null &&
                    string.Equals(entry.TeamName, selectedTeam.Name, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    private List<BracketRoundGroup> CreateBracketRoundGroups(League league, Fixture currentFixture, Team? selectedTeam)
    {
        var roundOrder = GetCompetitionRoundOrder(league, currentFixture.Competition);
        return league.Fixtures
            .Where(fixture => fixture.Competition == currentFixture.Competition && fixture.IsKnockout)
            .Where(fixture => currentFixture.Competition != CompetitionType.ChampionsLeague ||
                !fixture.KnockoutRoundKey.Equals("League Phase", StringComparison.OrdinalIgnoreCase))
            .GroupBy(GetBracketRoundName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var fixtures = group
                    .OrderBy(GetFixtureCalendarRound)
                    .ThenBy(fixture => fixture.RoundNumber)
                    .ThenBy(fixture => fixture.HomeTeam.Name)
                    .Select(fixture => CreateBracketMatchRow(fixture, selectedTeam))
                    .ToList();
                return new BracketRoundGroup
                {
                    RoundName = group.Key,
                    SummaryText = CreateBracketRoundSummary(fixtures),
                    SortOrder = GetRoundSortOrder(roundOrder, group.Key, group.Min(GetFixtureCalendarRound)),
                    Matches = fixtures
                };
            })
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.RoundName)
            .ToList();
    }

    private BracketMatchRow CreateBracketMatchRow(Fixture fixture, Team? selectedTeam)
    {
        var winner = fixture.WinningTeamName;
        var homeWon = !string.IsNullOrWhiteSpace(winner) &&
            winner.Equals(fixture.HomeTeam.Name, StringComparison.OrdinalIgnoreCase);
        var awayWon = !string.IsNullOrWhiteSpace(winner) &&
            winner.Equals(fixture.AwayTeam.Name, StringComparison.OrdinalIgnoreCase);
        var isSelectedTeamMatch = selectedTeam is not null &&
            (fixture.HomeTeam.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase) ||
                fixture.AwayTeam.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase));

        return new BracketMatchRow
        {
            HomeTeamName = fixture.HomeTeam.Name,
            AwayTeamName = fixture.AwayTeam.Name,
            HomeLogoPath = GetClubLogoPath(fixture.HomeTeam.Name),
            AwayLogoPath = GetClubLogoPath(fixture.AwayTeam.Name),
            HomeSeedText = homeWon ? "Advanced" : "Home",
            AwaySeedText = awayWon ? "Advanced" : "Away",
            ScoreText = CreateBracketScoreText(fixture),
            HomeFontWeight = homeWon ? "Black" : "SemiBold",
            AwayFontWeight = awayWon ? "Black" : "SemiBold",
            RowBackground = isSelectedTeamMatch
                ? ThemeManager.GetBrushHex("TableCurrentClubBackground", "#FEF3C7")
                : ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#F8FAFC"),
            BorderBrush = isSelectedTeamMatch
                ? ThemeManager.GetBrushHex("AppHighlightBrush", "#FACC15")
                : ThemeManager.GetBrushHex("AppBorderBrush", "#D8E0EA"),
            BorderThickness = isSelectedTeamMatch ? new Thickness(2) : new Thickness(1)
        };
    }

    private static string CreateBracketRoundSummary(IReadOnlyCollection<BracketMatchRow> matches)
    {
        var completed = matches.Count(match => match.ScoreText != "vs");
        return completed == matches.Count
            ? $"{matches.Count} matches completed"
            : $"{completed}/{matches.Count} matches completed";
    }

    private static string CreateBracketScoreText(Fixture fixture)
    {
        if (fixture.Result is null)
        {
            return "vs";
        }

        var score = $"{fixture.Result.HomeScore}-{fixture.Result.AwayScore}";
        if (fixture.PenaltyHomeScore.HasValue && fixture.PenaltyAwayScore.HasValue)
        {
            return $"{score}\n{fixture.PenaltyHomeScore}-{fixture.PenaltyAwayScore} pens";
        }

        if (fixture.ExtraTimeHomeScore.HasValue && fixture.ExtraTimeAwayScore.HasValue &&
            (fixture.ExtraTimeHomeScore != fixture.Result.HomeScore || fixture.ExtraTimeAwayScore != fixture.Result.AwayScore))
        {
            return $"{fixture.ExtraTimeHomeScore}-{fixture.ExtraTimeAwayScore}\nAET";
        }

        return score;
    }

    private static List<string> GetCompetitionRoundOrder(League league, CompetitionType competition)
    {
        return league.CompetitionStates
            .FirstOrDefault(state => state.Competition == competition)
            ?.RoundOrder ?? [];
    }

    private static int GetRoundSortOrder(IReadOnlyList<string> roundOrder, string roundName, int fallbackCalendarRound)
    {
        var orderIndex = roundOrder
            .Select((name, index) => new { name, index })
            .FirstOrDefault(item => item.name.Equals(roundName, StringComparison.OrdinalIgnoreCase))
            ?.index;
        return orderIndex.HasValue ? orderIndex.Value * 1000 : 10_000 + fallbackCalendarRound;
    }

    private static string GetBracketRoundName(Fixture fixture)
    {
        if (!string.IsNullOrWhiteSpace(fixture.KnockoutRoundKey) &&
            !fixture.KnockoutRoundKey.Equals("League Phase", StringComparison.OrdinalIgnoreCase))
        {
            return fixture.KnockoutRoundKey;
        }

        return GetFixtureRoundText(fixture);
    }

    private static bool ShouldShowMatchBracket(Fixture fixture)
    {
        return fixture.IsKnockout &&
            fixture.Competition is CompetitionType.FACup or CompetitionType.LeagueCup or CompetitionType.ChampionsLeague;
    }

    private string GetClubLogoPath(string clubName)
    {
        return ClubLogoService.GetClubLogoPath(clubName, _state.League?.LeagueId ?? _state.SelectedLeagueId);
    }

    private static string CreateScoreText(Fixture fixture)
    {
        if (fixture.Result is null)
        {
            return "vs";
        }

        var score = $"{fixture.Result.HomeScore} - {fixture.Result.AwayScore}";
        if (!fixture.IsKnockout || string.IsNullOrWhiteSpace(fixture.WinningTeamName))
        {
            return score;
        }

        if (fixture.PenaltyHomeScore.HasValue && fixture.PenaltyAwayScore.HasValue)
        {
            return $"{score} ({fixture.PenaltyHomeScore}-{fixture.PenaltyAwayScore} pens, {fixture.WinningTeamName} advance)";
        }

        if (fixture.ExtraTimeHomeScore.HasValue && fixture.ExtraTimeAwayScore.HasValue &&
            (fixture.ExtraTimeHomeScore != fixture.Result.HomeScore || fixture.ExtraTimeAwayScore != fixture.Result.AwayScore))
        {
            return $"{fixture.ExtraTimeHomeScore} - {fixture.ExtraTimeAwayScore} AET";
        }

        return $"{score} ({fixture.WinningTeamName} advance)";
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

    private static IEnumerable<string> GetLogoCandidatePaths(string clubName)
    {
        yield return TeamSelectionView.GetClubLogoPath(clubName);

        if (ImportedLogoFileNames.TryGetValue(clubName, out var importedFileName))
        {
            yield return CreatePackPath(importedFileName);
        }

        yield return DefaultLogoPath;
    }

    private static string CreatePackPath(string fileName)
    {
        var escapedFileName = Uri.EscapeDataString(fileName);
        return $"pack://application:,,,/{ClubsAssetPath}/{escapedFileName}";
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

    private sealed class RoundResultRow
    {
        public string HomeTeamName { get; init; } = string.Empty;
        public string AwayTeamName { get; init; } = string.Empty;
        public string HomeLogoPath { get; init; } = string.Empty;
        public string AwayLogoPath { get; init; } = string.Empty;
        public string ScoreText { get; init; } = string.Empty;
        public string RowBackground { get; init; } = "#FFFFFF";
        public string BorderBrush { get; init; } = "#DCE5F0";
        public Thickness BorderThickness { get; init; } = new(1);
    }

    private sealed class LeagueTableRow
    {
        public string Club { get; init; } = string.Empty;
        public string LogoPath { get; init; } = string.Empty;
        public int Played { get; init; }
        public int Wins { get; init; }
        public int Draws { get; init; }
        public int Losses { get; init; }
        public int GoalsFor { get; init; }
        public int GoalsAgainst { get; init; }
        public int GoalDifference { get; init; }
        public int Points { get; init; }
        public bool IsSelectedTeam { get; init; }
    }

    private sealed class BracketRoundGroup
    {
        public string RoundName { get; init; } = string.Empty;
        public string SummaryText { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public IReadOnlyList<BracketMatchRow> Matches { get; init; } = [];
    }

    private sealed class BracketMatchRow
    {
        public string HomeTeamName { get; init; } = string.Empty;
        public string AwayTeamName { get; init; } = string.Empty;
        public string HomeLogoPath { get; init; } = string.Empty;
        public string AwayLogoPath { get; init; } = string.Empty;
        public string HomeSeedText { get; init; } = string.Empty;
        public string AwaySeedText { get; init; } = string.Empty;
        public string ScoreText { get; init; } = string.Empty;
        public string HomeFontWeight { get; init; } = "SemiBold";
        public string AwayFontWeight { get; init; } = "SemiBold";
        public string RowBackground { get; init; } = "#F8FAFC";
        public string BorderBrush { get; init; } = "#D8E0EA";
        public Thickness BorderThickness { get; init; } = new(1);
    }
}
