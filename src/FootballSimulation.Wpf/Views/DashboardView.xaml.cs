using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class DashboardView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly GameSessionService _gameSessionService = new();
    private readonly RecentResultService _recentResultService = new();

    public DashboardView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadDashboard();
    }

    private void LoadDashboard()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        SelectedTeamTextBlock.Text = _state.SelectedTeam.Name;
        LeagueTableDataGrid.ItemsSource = CreateLeagueTableRows(_state.League, _state.SelectedTeam);

        var nextFixture = _gameSessionService.FindNextFixtureForTeam(_state.League, _state.SelectedTeam);
        _state.CurrentFixture = nextFixture;
        LoadUpcomingMatch(nextFixture, _state.SelectedTeam);
        UpcomingFixturesListBox.ItemsSource = CreateUpcomingFixtureRows(_state.League, _state.SelectedTeam);
    }

    private void PrepareMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentFixture is null)
        {
            MessageBox.Show("No upcoming fixture was found.");
            return;
        }

        if (_state.League is not null && _state.SelectedTeam is not null)
        {
            _state.CurrentMatch ??= _gameSessionService.CreateSelectedTeamLiveMatch(_state.League, _state.SelectedTeam);
        }

        _navigate(new PreMatchView(_state, _navigate));
    }

    private List<LeagueTableRow> CreateLeagueTableRows(League league, Team selectedTeam)
    {
        return league.Table
            .Select((entry, index) =>
            {
                var position = index + 1;
                var team = league.Teams.FirstOrDefault(candidate => candidate.Name == entry.TeamName);
                var recentResults = team is null
                    ? new List<ResultBadge>()
                    : _recentResultService.GetRecentResults(league, team)
                        .OrderBy(result => result.RoundNumber)
                        .Select(CreateResultBadge)
                        .ToList();

                return new LeagueTableRow
                {
                    Position = position,
                    Club = entry.TeamName,
                    Played = entry.Played,
                    Wins = entry.Wins,
                    Draws = entry.Draws,
                    Losses = entry.Losses,
                    GoalsFor = entry.GoalsFor,
                    GoalsAgainst = entry.GoalsAgainst,
                    GoalDifference = entry.GoalDifference,
                    Points = entry.Points,
                    LastFive = recentResults,
                    IsSelectedTeam = entry.TeamName == selectedTeam.Name,
                    ZoneBrush = GetZoneBrush(position, league.Table.Count),
                    RowBackground = GetRowBackground(position, league.Table.Count)
                };
            })
            .ToList();
    }

    private void LoadUpcomingMatch(Fixture fixture, Team selectedTeam)
    {
        var isHome = fixture.HomeTeam == selectedTeam;
        var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;
        var venue = isHome ? GetVenueName(selectedTeam) : GetVenueName(opponent);

        UpcomingFixtureTextBlock.Text = $"Round {fixture.RoundNumber}: {selectedTeam.Name} vs {opponent.Name}";
        VenueTextBlock.Text = $"Venue: {venue}";
        HomeAwayBadgeTextBlock.Text = isHome ? "HOME" : "AWAY";
        HomeAwayBadge.Background = new SolidColorBrush(isHome
            ? Color.FromRgb(47, 168, 79)
            : Color.FromRgb(249, 115, 22));
    }

    private static List<string> CreateUpcomingFixtureRows(League league, Team selectedTeam)
    {
        var fixtures = league.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .Take(5)
            .Select(fixture =>
            {
                var isHome = fixture.HomeTeam == selectedTeam;
                var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;
                var badge = isHome ? "HOME" : "AWAY";
                return $"R{fixture.RoundNumber}: {badge} vs {opponent.Name}";
            })
            .ToList();

        return fixtures.Count == 0 ? ["No upcoming fixtures."] : fixtures;
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam == team || fixture.AwayTeam == team;
    }

    private static string GetVenueName(Team homeTeam)
    {
        return $"{homeTeam.Name} Stadium";
    }

    private static ResultBadge CreateResultBadge(RecentMatchResult result)
    {
        return new ResultBadge
        {
            ResultType = result.ResultType,
            BadgeBrush = result.ResultType switch
            {
                "W" => "#2FA84F",
                "L" => "#D94343",
                _ => "#9AA3AF"
            }
        };
    }

    private static string GetZoneBrush(int position, int tableSize)
    {
        if (position <= 4)
        {
            return "#3B82F6";
        }

        if (position == 5)
        {
            return "#F97316";
        }

        if (position == 6)
        {
            return "#22C55E";
        }

        return position > tableSize - 3 ? "#EF4444" : "Transparent";
    }

    private static string GetRowBackground(int position, int tableSize)
    {
        if (position <= 4)
        {
            return "#F7FAFF";
        }

        if (position == 5)
        {
            return "#FFF8F0";
        }

        if (position == 6)
        {
            return "#F4FFF7";
        }

        return position > tableSize - 3 ? "#FFF6F6" : "#FFFFFF";
    }

    private sealed class LeagueTableRow
    {
        public int Position { get; init; }
        public string Club { get; init; } = string.Empty;
        public int Played { get; init; }
        public int Wins { get; init; }
        public int Draws { get; init; }
        public int Losses { get; init; }
        public int GoalsFor { get; init; }
        public int GoalsAgainst { get; init; }
        public int GoalDifference { get; init; }
        public int Points { get; init; }
        public List<ResultBadge> LastFive { get; init; } = [];
        public bool IsSelectedTeam { get; init; }
        public string ZoneBrush { get; init; } = "Transparent";
        public string RowBackground { get; init; } = "#FFFFFF";
    }

    private sealed class ResultBadge
    {
        public string ResultType { get; init; } = string.Empty;
        public string BadgeBrush { get; init; } = "#9AA3AF";
    }
}
