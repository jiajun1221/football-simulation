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

        TitleTextBlock.Text = $"Round {_state.CurrentFixture.RoundNumber} Results";
        RoundResultsListBox.ItemsSource = _state.League.Fixtures
            .Where(fixture => fixture.RoundNumber == _state.CurrentFixture.RoundNumber)
            .Select(fixture => CreateRoundResultRow(fixture, _state.SelectedTeam))
            .ToList();
        LeagueTableDataGrid.ItemsSource = CreateLeagueTableRows(_state.League, _state.SelectedTeam);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        var isSeasonComplete = _seasonCompletionService.IsLeagueComplete(_state.League);
        _state.CurrentFixture = null;
        _state.CurrentMatch = null;
        _navigate(isSeasonComplete
            ? new EndSeasonResultView(_state, _navigate)
            : new DashboardView(_state, _navigate));
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
            ScoreText = fixture.Result is null
                ? "vs"
                : $"{fixture.Result.HomeScore} - {fixture.Result.AwayScore}",
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

    private string GetClubLogoPath(string clubName)
    {
        return ClubLogoService.GetClubLogoPath(clubName, _state.League?.LeagueId ?? _state.SelectedLeagueId);
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
}
