using System.Text.Json;
using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LeagueTraitDataTests
{
    private static readonly string[] EnabledLeagueIds =
    [
        "premier-league",
        "la-liga",
        "serie-a",
        "bundesliga",
        "ligue-1"
    ];

    [Fact]
    public void EnabledLeagueTraits_DoNotOveruseLeadership()
    {
        var players = LoadAllEnabledLeaguePlayers().ToList();
        var leadershipPlayers = players
            .Where(player => player.Traits.Contains(PlayerTrait.Leadership))
            .ToList();

        Assert.NotEmpty(leadershipPlayers);
        Assert.True(
            leadershipPlayers.Count < players.Count * 0.02,
            $"Leadership should be rare. Found {leadershipPlayers.Count} of {players.Count} players.");
        Assert.Contains(leadershipPlayers, player => player.Name == "Antonio Rüdiger");
        Assert.Contains(leadershipPlayers, player => player.Name == "Thibaut Courtois");
        Assert.DoesNotContain(players, player =>
            player.Name == "Kylian Mbappé" &&
            player.Traits.Contains(PlayerTrait.Leadership));
    }

    [Fact]
    public void EnabledLeagueTraits_KeepGoalkeepersOnGoalkeeperTraits()
    {
        var allowedGoalkeeperTraits = new HashSet<PlayerTrait>
        {
            PlayerTrait.OneOnOnes,
            PlayerTrait.RushesOutOfGoal,
            PlayerTrait.Puncher,
            PlayerTrait.LongThrower,
            PlayerTrait.Leadership
        };

        var goalkeepers = LoadAllEnabledLeaguePlayers()
            .Where(player => player.Position == Position.Goalkeeper)
            .ToList();

        Assert.NotEmpty(goalkeepers);
        Assert.All(goalkeepers, goalkeeper =>
        {
            Assert.All(goalkeeper.Traits, trait =>
                Assert.Contains(trait, allowedGoalkeeperTraits));
        });
    }

    [Fact]
    public void LaLigaTraits_UseRequestedRealMadridProfiles()
    {
        var realMadrid = LoadTeamsFromSourceData("la-liga")
            .Single(team => team.Name == "Real Madrid");
        var realMadridPlayers = realMadrid.Players
            .Concat(realMadrid.Substitutes)
            .ToDictionary(player => player.Name, StringComparer.OrdinalIgnoreCase);

        AssertTraits(realMadridPlayers["Kylian Mbappé"], PlayerTrait.Rapid, PlayerTrait.ClinicalFinisher, PlayerTrait.SpeedDribbler, PlayerTrait.Flair, PlayerTrait.OutsideFootShot);
        AssertTraits(realMadridPlayers["Vinicius Junior"], PlayerTrait.Rapid, PlayerTrait.Flair, PlayerTrait.SpeedDribbler, PlayerTrait.TechnicalDribbler, PlayerTrait.OutsideFootShot);
        AssertTraits(realMadridPlayers["Jude Bellingham"], PlayerTrait.BoxToBox, PlayerTrait.Engine, PlayerTrait.Playmaker, PlayerTrait.PressResistant, PlayerTrait.BigMatchPlayer);
        AssertTraits(realMadridPlayers["Rodrygo"], PlayerTrait.Flair, PlayerTrait.TechnicalDribbler, PlayerTrait.FinesseShot, PlayerTrait.OutsideFootShot);
        AssertTraits(realMadridPlayers["Federico Valverde"], PlayerTrait.Engine, PlayerTrait.BoxToBox, PlayerTrait.LongShotTaker, PlayerTrait.TeamPlayer);
        AssertTraits(realMadridPlayers["Eduardo Camavinga"], PlayerTrait.PressResistant, PlayerTrait.TechnicalDribbler, PlayerTrait.Engine);
        AssertTraits(realMadridPlayers["Trent Alexander-Arnold"], PlayerTrait.LongPasser, PlayerTrait.EarlyCrosser, PlayerTrait.Playmaker, PlayerTrait.DeadBallSpecialist);
        AssertTraits(realMadridPlayers["Antonio Rüdiger"], PlayerTrait.DivesIntoTackles, PlayerTrait.AerialThreat, PlayerTrait.Leadership);
        AssertTraits(realMadridPlayers["Éder Militão"], PlayerTrait.AerialThreat, PlayerTrait.Rapid, PlayerTrait.Interceptor);
        AssertTraits(realMadridPlayers["Thibaut Courtois"], PlayerTrait.OneOnOnes, PlayerTrait.LongThrower, PlayerTrait.Leadership);
    }

    [Fact]
    public void EnabledLeagueTraits_RespectTraitCountBands()
    {
        var players = LoadAllEnabledLeaguePlayers().ToList();

        Assert.All(players.Where(player => player.OverallRating < 78), player =>
            Assert.InRange(player.Traits.Count, 1, 3));
        Assert.All(players.Where(player => player.OverallRating is >= 78 and < 82), player =>
            Assert.InRange(player.Traits.Count, 1, 4));
        Assert.All(players.Where(player => player.OverallRating is >= 82 and < 86), player =>
            Assert.InRange(player.Traits.Count, 1, 4));
        Assert.All(players.Where(player => player.OverallRating is >= 86 and < 90), player =>
            Assert.InRange(player.Traits.Count, 1, 5));
        Assert.All(players.Where(player => player.OverallRating >= 90), player =>
            Assert.InRange(player.Traits.Count, 1, 6));
    }

    [Fact]
    public void EnabledLeagueTraits_GiveEveryPlayerAtLeastOneTrait()
    {
        var players = LoadAllEnabledLeaguePlayers().ToList();

        Assert.All(players, player => Assert.NotEmpty(player.Traits));
    }

    [Fact]
    public void PremierLeagueTraits_GiveEstevaoWingerTraits()
    {
        var chelsea = LoadTeamsFromSourceData("premier-league")
            .Single(team => team.Name == "Chelsea");
        var estevao = chelsea.Players
            .Concat(chelsea.Substitutes)
            .Single(player =>
                player.Name.Equals("Estevao", StringComparison.OrdinalIgnoreCase) ||
                player.Name.Equals("Estêvão", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(PlayerTrait.Rapid, estevao.Traits);
        Assert.Contains(PlayerTrait.TechnicalDribbler, estevao.Traits);
    }

    private static IEnumerable<Player> LoadAllEnabledLeaguePlayers()
    {
        return EnabledLeagueIds
            .SelectMany(LoadTeamsFromSourceData)
            .SelectMany(team => team.Players.Concat(team.Substitutes));
    }

    private static List<Team> LoadTeamsFromSourceData(string leagueId)
    {
        var dataFolder = GetSourceDataFolder();
        var index = ReadJsonFile<LeagueDataIndex>(Path.Combine(dataFolder, "leagues-index.json"));
        var definition = index.Leagues.Single(league => league.LeagueId == leagueId);
        var squadsFile = ReadJsonFile<LeagueSquadsFile>(Path.Combine(dataFolder, definition.SquadFile));
        var mappingService = new PlayerStatMappingService();

        return squadsFile.Teams
            .Select(team => new Team
            {
                Name = team.Name,
                Players = team.StartingXI
                    .Select(player => mappingService.MapToPlayer(player, isStarter: true))
                    .ToList(),
                Substitutes = team.Substitutes
                    .Select(player => mappingService.MapToPlayer(player, isStarter: false))
                    .ToList()
            })
            .ToList();
    }

    private static string GetSourceDataFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "FootballSimulation.Core", "Data", "Json");
            if (File.Exists(Path.Combine(candidate, "leagues-index.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate source league JSON data folder.");
    }

    private static T ReadJsonFile<T>(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not deserialize {filePath}.");
    }

    private static void AssertTraits(Player player, params PlayerTrait[] expectedTraits)
    {
        Assert.Equal(expectedTraits, player.Traits);
    }
}
