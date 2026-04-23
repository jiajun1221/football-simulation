using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class DisciplinaryServiceTests
{
    [Fact]
    public void ApplyYellowCard_TwoYellowsEscalateToRed()
    {
        var disciplinaryService = new DisciplinaryService();
        var player = new Player
        {
            Name = "Test Defender",
            Position = Position.Defender,
            Attack = 30,
            Defense = 70,
            Passing = 40,
            Stamina = 70,
            CurrentStamina = 70,
            Finishing = 20
        };
        var teamStats = new MatchTeamStats();

        var firstYellowSentOff = disciplinaryService.ApplyYellowCard(player, teamStats);
        var secondYellowSentOff = disciplinaryService.ApplyYellowCard(player, teamStats);

        Assert.False(firstYellowSentOff);
        Assert.True(secondYellowSentOff);
        Assert.Equal(2, player.YellowCards);
        Assert.True(player.IsSentOff);
        Assert.Equal(2, teamStats.YellowCards);
        Assert.Equal(1, teamStats.RedCards);
    }
}
