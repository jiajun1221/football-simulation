using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PlayerGrowthServiceTests
{
    [Fact]
    public void ApplyMatchGrowth_StrongPerformerAcrossMatches_GainsOverall()
    {
        var service = new PlayerGrowthService();
        var player = CreatePlayer("Estevao", overall: 78, age: 18);
        var opponent = CreatePlayer("Opponent", overall: 78, age: 25);
        var team = CreateTeam("Chelsea", player);
        var opponentTeam = CreateTeam("Liverpool", opponent);

        service.ApplyMatchGrowth(CreateMatch(team, opponentTeam, player, rating: 8.6));
        service.ApplyMatchGrowth(CreateMatch(team, opponentTeam, player, rating: 8.6));
        service.ApplyMatchGrowth(CreateMatch(team, opponentTeam, player, rating: 8.6));

        Assert.Equal(79, player.OverallRating);
        Assert.True(player.GrowthPoints > 0);
        Assert.True(player.Attack > 78);
    }

    [Fact]
    public void ApplyMatchGrowth_PoorPerformanceAddsNoGrowth()
    {
        var service = new PlayerGrowthService();
        var player = CreatePlayer("Quiet Player", overall: 78, age: 22);
        var opponent = CreatePlayer("Opponent", overall: 78, age: 25);
        var team = CreateTeam("Chelsea", player);
        var opponentTeam = CreateTeam("Liverpool", opponent);

        service.ApplyMatchGrowth(CreateMatch(team, opponentTeam, player, rating: 5.8));

        Assert.Equal(78, player.OverallRating);
        Assert.Equal(0, player.GrowthPoints);
        Assert.Equal(0, player.LastMatchOverallIncrease);
    }

    [Fact]
    public void ApplyMatchGrowth_MaxOneOverallIncreasePerMatch()
    {
        var service = new PlayerGrowthService();
        var player = CreatePlayer("Breakout Player", overall: 78, age: 18);
        player.GrowthPoints = 95;
        var opponent = CreatePlayer("Opponent", overall: 78, age: 25);
        var team = CreateTeam("Chelsea", player);
        var opponentTeam = CreateTeam("Liverpool", opponent);

        service.ApplyMatchGrowth(CreateMatch(team, opponentTeam, player, rating: 10.0, goals: 3, assists: 2));

        Assert.Equal(79, player.OverallRating);
        Assert.Equal(1, player.LastMatchOverallIncrease);
        Assert.True(player.GrowthPoints >= 50);
    }

    private static Match CreateMatch(Team team, Team opponentTeam, Player player, double rating, int goals = 0, int assists = 0)
    {
        return new Match
        {
            HomeTeam = team,
            AwayTeam = opponentTeam,
            CurrentMinute = 90,
            HomeScore = goals,
            AwayScore = 0,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    PlayerName = player.Name,
                    TeamName = team.Name,
                    Position = player.Position,
                    Rating = rating,
                    Goals = goals,
                    Assists = assists
                }
            ]
        };
    }

    private static Team CreateTeam(string name, Player player)
    {
        return new Team
        {
            Name = name,
            Formation = "4-3-3",
            Players = [player]
        };
    }

    private static Player CreatePlayer(string name, int overall, int age)
    {
        return new Player
        {
            Name = name,
            Position = Position.Forward,
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = age,
            Attack = overall,
            Defense = overall,
            Passing = overall,
            Stamina = overall,
            Finishing = overall,
            FormStatus = PlayerFormStatus.Average
        };
    }
}
