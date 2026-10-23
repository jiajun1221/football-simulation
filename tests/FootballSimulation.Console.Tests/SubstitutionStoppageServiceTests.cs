using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SubstitutionStoppageServiceTests
{
    [Theory]
    [InlineData(EventType.ThrowIn)]
    [InlineData(EventType.Miss)]
    [InlineData(EventType.Goal)]
    [InlineData(EventType.Injury)]
    [InlineData(EventType.Halftime)]
    [InlineData(EventType.VarCheck)]
    [InlineData(EventType.VarDecision)]
    [InlineData(EventType.Foul)]
    public void CanMakeSubstitution_AllowsRecognizedStoppages(EventType eventType)
    {
        Assert.True(SubstitutionStoppageService.CanMakeSubstitution(eventType));
    }

    [Theory]
    [InlineData(EventType.Attack)]
    [InlineData(EventType.AttackProgression)]
    [InlineData(EventType.ChanceCreated)]
    [InlineData(EventType.Tackle)]
    [InlineData(EventType.Turnover)]
    public void CanMakeSubstitution_BlocksActivePlay(EventType eventType)
    {
        Assert.False(SubstitutionStoppageService.CanMakeSubstitution(eventType));
    }
}
