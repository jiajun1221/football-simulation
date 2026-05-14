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

    [Fact]
    public void PlayerOverallCalculator_UsesPositionSpecificAttributeWeights()
    {
        var striker = CreatePlayer("Striker", overall: 70, age: 22);
        striker.PreferredPosition = "ST";
        striker.Attack = 80;
        striker.Finishing = 90;
        striker.Passing = 65;
        striker.Defense = 35;

        var centreBack = CreatePlayer("Centre Back", overall: 70, age: 22);
        centreBack.Position = Position.Defender;
        centreBack.PreferredPosition = "CB";
        centreBack.Attack = 35;
        centreBack.Finishing = 30;
        centreBack.Passing = 68;
        centreBack.Defense = 88;

        Assert.True(PlayerOverallCalculator.CalculateOverall(striker) > PlayerOverallCalculator.CalculateOverall(centreBack));
        Assert.True(PlayerOverallCalculator.CalculateOverall(centreBack) >= 70);
    }

    [Fact]
    public void ApplyMatchGrowth_StrikerGrowthPrioritizesAttackAndFinishing()
    {
        var service = new PlayerGrowthService();
        var player = CreatePlayer("Striker", overall: 78, age: 20);
        player.PreferredPosition = "ST";
        player.Attack = 78;
        player.Defense = 78;
        player.Passing = 78;
        player.Finishing = 78;
        player.GrowthPoints = 95;
        var opponent = CreatePlayer("Opponent", overall: 78, age: 25);
        var team = CreateTeam("Chelsea", player);
        var opponentTeam = CreateTeam("Liverpool", opponent);

        service.ApplyMatchGrowth(CreateMatch(team, opponentTeam, player, rating: 9.0, goals: 2));

        Assert.Equal(79, player.OverallRating);
        Assert.True(player.Finishing > 78 || player.Attack > 78);
        Assert.Equal(78, player.Defense);
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
