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
}
