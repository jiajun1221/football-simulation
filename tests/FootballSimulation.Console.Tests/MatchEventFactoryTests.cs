using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class MatchEventFactoryTests
{
    [Theory]
    [InlineData("free-kick header")]
    [InlineData("corner header")]
    public void CreateShot_ClassifiesHeaderAttemptsWithoutChangingShotEventType(string chanceType)
    {
        var team = new Team { Name = "Chelsea" };
        var taker = new Player { Name = "Enzo Fernandez", Position = Position.Midfielder, PreferredPosition = "CM" };
        var target = new Player { Name = "Liam Delap", Position = Position.Forward, PreferredPosition = "ST" };

        var matchEvent = new MatchEventFactory().CreateShot(2, team, target, taker, chanceType, new Random(4));

        Assert.Equal(EventType.Shot, matchEvent.EventType);
        Assert.Equal(ShotClassification.Header, matchEvent.ShotClassification);
        Assert.Equal(target.Name, matchEvent.PrimaryPlayerName);
        Assert.DoesNotContain("takes a shot", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateMiss_KeepsHeaderContextForOutcomeTextOnly()
    {
        var team = new Team { Name = "Chelsea" };
        var target = new Player { Name = "Liam Delap", Position = Position.Forward, PreferredPosition = "ST" };

        var matchEvent = new MatchEventFactory().CreateMiss(2, team, target, "free-kick header", new Random(2));

        Assert.Equal(EventType.Miss, matchEvent.EventType);
        Assert.Equal(ShotClassification.Header, matchEvent.ShotClassification);
    }

    [Fact]
    public void CreateLateDrama_PreservesCatalystAndTriggeredTrait()
    {
        var team = new Team { Name = "Chelsea" };
        var opponent = new Team { Name = "Arsenal" };
        var catalyst = new Player { Name = "Cole Palmer", Position = Position.Midfielder, PreferredPosition = "CAM" };
        var match = new Match { HomeTeam = team, AwayTeam = opponent, HomeScore = 1, AwayScore = 2 };

        var matchEvent = new MatchEventFactory().CreateLateDrama(88, team, opponent, match, catalyst, PlayerTrait.BigMatchPlayer);

        Assert.Equal(EventType.LateDrama, matchEvent.EventType);
        Assert.Equal(catalyst.Name, matchEvent.PrimaryPlayerName);
        Assert.Equal(PlayerTrait.BigMatchPlayer, matchEvent.TriggeredTrait);
    }

    [Fact]
    public void CreateTimeWasting_PreservesPlayerAndLeadershipTrait()
    {
        var team = new Team { Name = "Chelsea" };
        var leader = new Player { Name = "Reece James", Position = Position.Defender, PreferredPosition = "RB" };

        var matchEvent = new MatchEventFactory().CreateTimeWasting(84, team, new Random(1), leader, PlayerTrait.Leadership);

        Assert.Equal(EventType.TimeWasting, matchEvent.EventType);
        Assert.Equal(leader.Name, matchEvent.PrimaryPlayerName);
        Assert.Equal(PlayerTrait.Leadership, matchEvent.TriggeredTrait);
    }

    [Fact]
    public void CreateGoalkeeperMistake_UsesSlipperyConditionContext()
    {
        var team = new Team { Name = "Chelsea" };
        var goalkeeper = new Player { Name = "Robert Sanchez", Position = Position.Goalkeeper, PreferredPosition = "GK" };
        var attacker = new Player { Name = "Bukayo Saka", Position = Position.Forward, PreferredPosition = "RW" };

        var matchEvent = new MatchEventFactory().CreateGoalkeeperMistake(63, team, goalkeeper, attacker, new Random(3), "slippery conditions");

        Assert.Equal(EventType.GoalkeeperMistake, matchEvent.EventType);
        Assert.Equal(goalkeeper.Name, matchEvent.PrimaryPlayerName);
        Assert.Equal(attacker.Name, matchEvent.SecondaryPlayerName);
        Assert.Contains("ball", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }
}
