using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LeagueDataServiceTests
{
    private static readonly string[] EnabledLeagueIds =
    [
        "premier-league",
        "la-liga",
        "serie-a",
        "bundesliga",
        "ligue-1"
    ];

    [Theory]
    [MemberData(nameof(EnabledLeagues))]
    public void LoadTeams_ReturnsPlayableTeamsForEachEnabledLeague(string leagueId)
    {
        var dataService = new LeagueDataService();

        var teams = dataService.LoadTeams(leagueId);

        Assert.InRange(teams.Count, 18, 20);
        Assert.All(teams, team =>
        {
            Assert.False(string.IsNullOrWhiteSpace(team.Name));
            Assert.False(string.IsNullOrWhiteSpace(team.Formation));
            Assert.False(string.IsNullOrWhiteSpace(team.Venue));
            Assert.Equal(11, team.Players.Count);
            Assert.InRange(team.Substitutes.Count, 7, 12);
            Assert.Contains(team.Players, player => player.Position == Position.Goalkeeper);
            Assert.Contains(team.Substitutes, player => player.Position == Position.Goalkeeper);
            Assert.All(team.Players.Concat(team.Substitutes), AssertHasNormalizedPlayerData);
        });
    }

    [Theory]
    [MemberData(nameof(EnabledLeagues))]
    public void LoadTeams_AssignsVisibleUniqueShirtNumbers(string leagueId)
    {
        var dataService = new LeagueDataService();

        var teams = dataService.LoadTeams(leagueId);

        Assert.All(teams, team =>
        {
            var squadNumbers = team.Players
                .Concat(team.Substitutes)
                .Select(player => player.SquadNumber)
                .ToList();

            Assert.All(squadNumbers, squadNumber => Assert.InRange(squadNumber, 1, 99));
            Assert.Equal(squadNumbers.Count, squadNumbers.Distinct().Count());
        });
    }

    [Theory]
    [MemberData(nameof(EnabledLeagues))]
    public void CreateLeague_UsesSelectedLeagueMetadata(string leagueId)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition(leagueId);
        var teams = dataService.LoadTeams(definition);

        var league = new GameSessionService().CreateLeague(definition, teams);

        Assert.Equal(definition.LeagueId, league.LeagueId);
        Assert.Equal(definition.Name, league.Name);
        Assert.Equal(definition.Season, league.Season);
        Assert.Equal(teams.Count, league.Table.Count);
        Assert.Equal(teams.Count * (teams.Count - 1) / 2, league.Fixtures.Count);
    }

    [Theory]
    [MemberData(nameof(EnabledLeagues))]
    public void SaveData_RestoresSelectedLeagueIdentity(string leagueId)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition(leagueId);
        var teams = dataService.LoadTeams(definition);
        var league = new GameSessionService().CreateLeague(definition, teams);
        var selectedTeam = league.Teams[0];

        var saveData = SaveGameService.CreateSaveData(league, selectedTeam);
        var restoredLeague = SaveGameService.CreateLeague(saveData);

        Assert.Equal(definition.LeagueId, saveData.LeagueId);
        Assert.Equal(definition.LeagueId, saveData.LeagueState.LeagueId);
        Assert.Equal(definition.LeagueId, restoredLeague.LeagueId);
        Assert.Equal(definition.Name, restoredLeague.Name);
        Assert.Equal(selectedTeam.Name, saveData.SelectedClubName);
        Assert.Equal(league.Teams.Count, restoredLeague.Teams.Count);
        Assert.Equal(league.Fixtures.Count, restoredLeague.Fixtures.Count);
        Assert.Equal(league.Table.Count, restoredLeague.Table.Count);
    }

    public static IEnumerable<object[]> EnabledLeagues()
    {
        return EnabledLeagueIds.Select(leagueId => new object[] { leagueId });
    }

    private static void AssertHasNormalizedPlayerData(Player player)
    {
        Assert.False(string.IsNullOrWhiteSpace(player.Name));
        Assert.InRange(player.OverallRating, 1, 100);
        Assert.InRange(player.Stamina, 1, 100);
        Assert.InRange(player.CurrentStamina, 0, player.Stamina);
        Assert.False(string.IsNullOrWhiteSpace(player.PreferredPosition));
        Assert.InRange(player.SquadNumber, 1, 99);
        Assert.InRange(player.DisciplineRating, 1, 100);
    }
}
