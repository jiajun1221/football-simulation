using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LeagueDataServiceTests
{
    [Fact]
    public void LoadTeams_GivesYoungPlayersConservativeGrowthPotential()
    {
        var youngPlayers = new LeagueDataService()
            .LoadTeams("premier-league")
            .SelectMany(team => team.AllPlayers)
            .Where(player => player.Age <= 21)
            .ToList();

        Assert.NotEmpty(youngPlayers);
        Assert.All(youngPlayers, player => Assert.InRange(player.PotentialOverall!.Value, player.OverallRating, 94));
    }

    [Fact]
    public void LoadTeams_CreatesIndependentPlayersWithFreshConditionForEveryNewGame()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var firstGameTeams = dataService.LoadTeams(definition);
        var firstGamePlayer = firstGameTeams.SelectMany(team => team.AllPlayers).First();

        firstGamePlayer.Stamina = 32;
        firstGamePlayer.SeasonFatigue = 91;
        firstGamePlayer.MatchesPlayedRecently = 6;
        firstGamePlayer.RecentMatchMinutes.AddRange([90, 90, 90]);
        firstGamePlayer.IsInjured = true;
        firstGamePlayer.InjuryRecoveryMatches = 8;

        var secondGameTeams = dataService.LoadTeams(definition);
        var secondGamePlayer = secondGameTeams
            .SelectMany(team => team.AllPlayers)
            .Single(player => player.PlayerId == firstGamePlayer.PlayerId);

        Assert.NotSame(firstGamePlayer, secondGamePlayer);
        Assert.Equal(100, secondGamePlayer.Stamina);
        Assert.Equal(0, secondGamePlayer.Fatigue);
        Assert.Equal(0, secondGamePlayer.SeasonFatigue);
        Assert.Equal(0, secondGamePlayer.MatchesPlayedRecently);
        Assert.Empty(secondGamePlayer.RecentMatchMinutes);
        Assert.False(secondGamePlayer.IsInjured);
        Assert.Equal(0, secondGamePlayer.InjuryRecoveryMatches);
        Assert.Equal(0, secondGamePlayer.SuspendedMatches);
    }

    [Fact]
    public void LoadTeams_UsesStablePlayerIdsAcrossTheFullChelseaSquad()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");

        var teams = dataService.LoadTeams(definition);
        var chelsea = teams.Single(team => team.Name == "Chelsea");
        var squad = chelsea.AllPlayers.ToList();

        Assert.True(squad.Count >= 23);
        Assert.All(squad, player => Assert.False(string.IsNullOrWhiteSpace(player.PlayerId)));
        Assert.Contains(squad, player => player.PlayerId.StartsWith("ea:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(squad.Count, squad.Select(player => player.PlayerId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ChelseaLoanedOutPlayers_UseVerifiedIdentityAndRatingData()
    {
        var chelsea = new LeagueDataService().LoadTeams("premier-league")
            .Single(team => team.Name == "Chelsea");

        var mudryk = chelsea.LoanedOutPlayers.Single(player => player.Name == "Mykhaylo Mudryk");
        Assert.Equal(75, mudryk.OverallRating);
        Assert.Equal("Ukraine", mudryk.NationalityName);
        Assert.Equal("ea:246340", mudryk.PlayerId);

        var jorgensen = chelsea.LoanedOutPlayers.Single(player => player.Name == "Filip Jorgensen");
        Assert.Equal(Position.Goalkeeper, jorgensen.Position);
        Assert.Equal(77, jorgensen.OverallRating);
        Assert.Equal("Denmark", jorgensen.NationalityName);

        AssertPlayer("Harrison Murray-Campbell", "CB", "England", 64);
        AssertPlayer("Ishe Samuels-Smith", "LB", "England", 68);
        AssertPlayer("Kaiden Wilson", "CB", "England", 59);
        AssertPlayer("Ollie Harrison", "CDM", "England", 60);
        AssertPlayer("Caleb Wiley", "LB", "United States", 68);
        AssertPlayer("Dastan Satpayev", "ST", "Kazakhstan", 63);
        AssertPlayer("Dujuan Richards", "ST", "Jamaica", 64);
        AssertPlayer("Reggie Walsh", "CAM", "England", 62);
        AssertPlayer("Ryan Kavuma-McQueen", "LW", "England", 62);

        var denner = chelsea.AllPlayers.Single(player => player.Name == "Denner");
        Assert.Equal("LB", denner.PreferredPosition);
        Assert.Equal("Brazil", denner.NationalityName);
        Assert.Equal(66, denner.OverallRating);

        var sharmanLowe = chelsea.AllPlayers.Single(player => player.Name == "Teddy Sharman-Lowe");
        Assert.Equal(Position.Goalkeeper, sharmanLowe.Position);
        Assert.Equal("England", sharmanLowe.NationalityName);
        Assert.Equal(64, sharmanLowe.OverallRating);

        void AssertPlayer(string name, string position, string nation, int overall)
        {
            var player = chelsea.LoanedOutPlayers.Single(candidate => candidate.Name == name);
            Assert.Equal(position, player.PreferredPosition);
            Assert.Equal(nation, player.NationalityName);
            Assert.Equal(overall, player.OverallRating);
        }
    }

    private static readonly string[] EnabledLeagueIds =
    [
        "premier-league",
        "la-liga",
        "serie-a",
        "bundesliga",
        "ligue-1"
    ];

    [Theory]
    [InlineData("premier-league", "Arsenal|AFC Bournemouth|Aston Villa|Brentford|Brighton & Hove Albion|Chelsea|Coventry City|Crystal Palace|Everton|Fulham|Hull City|Ipswich Town|Leeds United|Liverpool|Manchester City|Manchester United|Newcastle United|Nottingham Forest|Sunderland|Tottenham Hotspur")]
    [InlineData("la-liga", "Athletic Club|Atletico Madrid|Osasuna|Celta Vigo|Deportivo Alaves|Elche|Barcelona|Getafe|Levante|Malaga|Racing Santander|Rayo Vallecano|Deportivo La Coruna|Espanyol|Real Betis|Real Madrid|Real Sociedad|Sevilla|Valencia|Villarreal")]
    [InlineData("bundesliga", "Augsburg|Union Berlin|Werder Bremen|Borussia Dortmund|Elversberg|Eintracht Frankfurt|Freiburg|Hamburg|Hoffenheim|FC Koln|RB Leipzig|Bayer Leverkusen|Mainz 05|Borussia Monchengladbach|Bayern Munich|Paderborn|Schalke 04|Stuttgart")]
    [InlineData("serie-a", "Fiorentina|Frosinone|AC Milan|Monza|Parma|Sassuolo|Torino|Udinese|Venezia|Atalanta|Bologna|Cagliari|Como|Genoa|Inter Milan|Juventus|Napoli|Lazio|Roma|Lecce")]
    [InlineData("ligue-1", "Angers|Auxerre|Brest|Le Havre|Lens|Lille|Lorient|Lyon|Le Mans|Marseille|Monaco|Nice|Paris FC|Paris Saint-Germain|Rennes|Strasbourg|Toulouse|Troyes")]
    public void ActiveLeagueMembership_MatchesThe2026_27Snapshot(string leagueId, string expectedNames)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition(leagueId);
        var actual = dataService.LoadTeams(definition).Select(team => team.Name).Order().ToList();
        var expected = expectedNames.Split('|').Order().ToList();

        Assert.Equal("2026-27", definition.Season);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ActiveSquadSources_HaveGloballyUniquePlayerOwnership()
    {
        var dataService = new LeagueDataService();
        var ownership = dataService.LoadSquadSourceDefinitions()
            .SelectMany(definition => dataService.LoadTeams(definition))
            .SelectMany(team => team.AllPlayers.Select(player => (team.Name, player.PlayerId)))
            .ToList();

        Assert.DoesNotContain(ownership.GroupBy(item => item.PlayerId, StringComparer.OrdinalIgnoreCase), group =>
            group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
    }

    [Fact]
    public void AttachedDeadlineMoves_AreAppliedWithLoanOwnership()
    {
        var dataService = new LeagueDataService();
        var premierLeague = dataService.LoadTeams("premier-league");
        var serieA = dataService.LoadTeams("serie-a");

        Assert.Contains(premierLeague.Single(team => team.Name == "Arsenal").AllPlayers,
            player => player.Name == "Bruno Guimarães");
        Assert.DoesNotContain(premierLeague.Single(team => team.Name == "Newcastle United").AllPlayers,
            player => player.Name == "Bruno Guimarães");

        var woltemade = serieA.Single(team => team.Name == "Juventus").AllPlayers
            .Single(player => player.Name == "Nick Woltemade");
        Assert.True(woltemade.IsOnLoan);
        Assert.Equal("Newcastle United", woltemade.ParentClubName);

        var sanchez = serieA.Single(team => team.Name == "Como").AllPlayers
            .Single(player => player.Name == "Robert Sánchez");
        Assert.True(sanchez.IsOnLoan);
        Assert.Equal("Chelsea", sanchez.ParentClubName);
    }

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
            Assert.All(team.AllPlayers, AssertHasNormalizedPlayerData);
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
            var squadNumbers = team.AllPlayers
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
        Assert.Equal(teams.Count * (teams.Count - 1), league.Fixtures.Count(fixture => fixture.Competition == CompetitionType.PremierLeague));
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.FACup);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.LeagueCup);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague);
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

    [Fact]
    public void LoadSquadSourceDefinitions_IncludesNonPlayableChampionsLeagueSquads()
    {
        var dataService = new LeagueDataService();

        var definition = dataService.LoadSquadSourceDefinitions()
            .Single(league => league.LeagueId == "champions-league");
        var teams = dataService.LoadTeams(definition);

        Assert.False(definition.IsAvailable);
        Assert.Equal(15, teams.Count);
        Assert.Contains(teams, team => team.Name == "Sporting CP");
        Assert.Contains(teams, team => team.Name == "Slovan Bratislava");
        Assert.All(teams, team =>
        {
            Assert.Equal(11, team.Players.Count);
            Assert.InRange(team.Substitutes.Count, 7, 12);
            Assert.DoesNotContain(team.AllPlayers, player =>
                player.Name.Contains(" Player ", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(team.Players, player => player.Position == Position.Goalkeeper);
            Assert.Contains(team.Substitutes, player => player.Position == Position.Goalkeeper);
        });
    }

    [Theory]
    [MemberData(nameof(EnabledLeagues))]
    public void LoadTeams_PreservesFc27PreferredPositions(string leagueId)
    {
        var players = new LeagueDataService()
            .LoadTeams(leagueId)
            .SelectMany(team => team.AllPlayers)
            .ToList();

        Assert.All(players, player => Assert.Contains(player.PreferredPosition,
            new[] { "GK", "CB", "LB", "RB", "CDM", "CM", "CAM", "LM", "RM", "LW", "RW", "CF", "ST" }));
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
        Assert.NotNull(player.Age);
        Assert.InRange(player.Age!.Value, 15, 45);
        Assert.False(string.IsNullOrWhiteSpace(player.NationalityCode));
        Assert.False(string.IsNullOrWhiteSpace(player.NationalityName));
        Assert.False(string.IsNullOrWhiteSpace(player.FlagImagePath));
        Assert.NotNull(player.ContractEndYear);
        Assert.True(player.ContractEndYear >= PlayerContractService.DefaultSeasonEndYear);
        Assert.NotNull(player.WeeklyWage);
        Assert.True(player.WeeklyWage > 0);
    }
}
