using System.Text.Json;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class TeamRosterServiceTests
{
    [Fact]
    public void SelectMatchdayBench_UsesFullRosterAndBuildsBalancedEight()
    {
        var team = new Team
        {
            Players = Enumerable.Range(1, 11).Select(index => CreatePlayer($"starter-{index}", Position.Midfielder, 70)).ToList(),
            Substitutes = [],
            Reserves =
            [
                CreatePlayer("gk", Position.Goalkeeper, 70),
                .. Enumerable.Range(1, 4).Select(index => CreatePlayer($"def-{index}", Position.Defender, 70 + index)),
                .. Enumerable.Range(1, 4).Select(index => CreatePlayer($"mid-{index}", Position.Midfielder, 70 + index)),
                .. Enumerable.Range(1, 3).Select(index => CreatePlayer($"fwd-{index}", Position.Forward, 70 + index)),
                CreatePlayer("extra", Position.Forward, 60)
            ]
        };

        TeamRosterService.SelectMatchdayBench(team);

        Assert.Equal(8, team.Substitutes.Count);
        Assert.Contains(team.Substitutes, player => player.Position == Position.Goalkeeper);
        Assert.True(team.Substitutes.Count(player => player.Position == Position.Defender) >= 2);
        Assert.True(team.Substitutes.Count(player => player.Position == Position.Midfielder) >= 2);
        Assert.True(team.Substitutes.Count(player => player.Position == Position.Forward) >= 2);
        Assert.Equal(5, team.Reserves.Count);
    }

    [Fact]
    public void LegacyTeamJson_WithoutReserves_LoadsAnEmptyReserveList()
    {
        var team = JsonSerializer.Deserialize<Team>("{\"Name\":\"Legacy FC\",\"Players\":[],\"Substitutes\":[]}");

        Assert.NotNull(team);
        Assert.Empty(team.Reserves);
    }

    [Fact]
    public void SelectMatchdayBench_KeepsInjuredPlayersInReserves()
    {
        var injuredStar = CreatePlayer("injured-star", Position.Midfielder, 99);
        injuredStar.IsInjured = true;
        var team = new Team
        {
            Players = Enumerable.Range(1, 11)
                .Select(index => CreatePlayer($"starter-{index}", Position.Midfielder, 70))
                .ToList(),
            Substitutes = [injuredStar],
            Reserves =
            [
                CreatePlayer("gk", Position.Goalkeeper, 70),
                .. Enumerable.Range(1, 3).Select(index => CreatePlayer($"def-{index}", Position.Defender, 70)),
                .. Enumerable.Range(1, 3).Select(index => CreatePlayer($"mid-{index}", Position.Midfielder, 70)),
                .. Enumerable.Range(1, 2).Select(index => CreatePlayer($"fwd-{index}", Position.Forward, 70))
            ]
        };

        TeamRosterService.SelectMatchdayBench(team);

        Assert.Equal(8, team.Substitutes.Count);
        Assert.DoesNotContain(injuredStar, team.Substitutes);
        Assert.Contains(injuredStar, team.Reserves);
    }

    private static Player CreatePlayer(string id, Position position, int overall) => new()
    {
        PlayerId = id,
        Name = id,
        Position = position,
        OverallRating = overall,
        Stamina = 100
    };
}
