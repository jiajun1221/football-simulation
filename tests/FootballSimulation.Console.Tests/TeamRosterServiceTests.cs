using System.Text.Json;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class TeamRosterServiceTests
{
    [Fact]
    public void SelectMatchdayBench_CoversEveryRequiredOutfieldRoleWithoutGoalkeeper()
    {
        var team = new Team
        {
            Players = Enumerable.Range(1, 11).Select(index => CreatePlayer($"starter-{index}", Position.Midfielder, 70)).ToList(),
            Substitutes = [],
            Reserves =
            [
                CreatePlayer("gk", Position.Goalkeeper, 70),
                CreatePlayer("st", Position.Forward, 78, "ST"),
                CreatePlayer("lw", Position.Forward, 77, "LW"),
                CreatePlayer("rw", Position.Forward, 76, "RW"),
                CreatePlayer("cam", Position.Midfielder, 75, "CAM"),
                CreatePlayer("cb", Position.Defender, 74, "CB"),
                CreatePlayer("lb", Position.Defender, 73, "LB"),
                CreatePlayer("rb", Position.Defender, 72, "RB"),
                CreatePlayer("cdm", Position.Midfielder, 71, "CDM"),
                CreatePlayer("extra", Position.Forward, 60)
            ]
        };

        TeamRosterService.SelectMatchdayBench(team);

        Assert.Equal(8, team.Substitutes.Count);
        Assert.DoesNotContain(team.Substitutes, player => player.Position == Position.Goalkeeper);
        Assert.All(new[] { "ST", "LW", "RW", "CAM", "CB", "LB", "RB", "CDM" }, role =>
            Assert.Contains(team.Substitutes, player => player.PreferredPosition == role));
        Assert.Contains(team.Reserves, player => player.Position == Position.Goalkeeper);
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

    [Fact]
    public void SelectMatchdayBench_MovesPlayersBeyondStartingElevenIntoBenchOrReserves()
    {
        var fullRoster = Enumerable.Range(1, 23)
            .Select(index => CreatePlayer($"player-{index}", Position.Midfielder, 90 - index, "CM"))
            .ToList();
        var team = new Team
        {
            Players = fullRoster.Take(13).ToList(),
            Substitutes = fullRoster.Skip(13).Take(6).ToList(),
            Reserves = fullRoster.Skip(19).ToList()
        };

        TeamRosterService.SelectMatchdayBench(team);

        var selectedRoster = TeamRosterService.GetDistinctPlayers(team);
        Assert.Equal(11, team.Players.Count);
        Assert.Equal(8, team.Substitutes.Count);
        Assert.Equal(4, team.Reserves.Count);
        Assert.Equal(fullRoster.Select(player => player.PlayerId).Order(), selectedRoster.Select(player => player.PlayerId).Order());
    }

    [Fact]
    public void PromoteReserveGoalkeeperForInjury_ReplacesLowestRatedOutfieldSubstitute()
    {
        var goalkeeper = CreatePlayer("reserve-gk", Position.Goalkeeper, 75, "GK");
        var weakestSubstitute = CreatePlayer("weakest", Position.Forward, 60, "ST");
        var team = new Team
        {
            Substitutes =
            [
                weakestSubstitute,
                CreatePlayer("lw", Position.Forward, 70, "LW"),
                CreatePlayer("rw", Position.Forward, 70, "RW"),
                CreatePlayer("cam", Position.Midfielder, 70, "CAM"),
                CreatePlayer("cb", Position.Defender, 70, "CB"),
                CreatePlayer("lb", Position.Defender, 70, "LB"),
                CreatePlayer("rb", Position.Defender, 70, "RB"),
                CreatePlayer("cm", Position.Midfielder, 70, "CM")
            ],
            Reserves = [goalkeeper]
        };

        var promoted = TeamRosterService.PromoteReserveGoalkeeperForInjury(team);

        Assert.Same(goalkeeper, promoted);
        Assert.Contains(goalkeeper, team.Substitutes);
        Assert.Contains(weakestSubstitute, team.Reserves);
        Assert.Equal(8, team.Substitutes.Count);
    }

    private static Player CreatePlayer(string id, Position position, int overall, string? preferredPosition = null) => new()
    {
        PlayerId = id,
        Name = id,
        Position = position,
        PreferredPosition = preferredPosition ?? string.Empty,
        OverallRating = overall,
        Stamina = 100
    };
}
