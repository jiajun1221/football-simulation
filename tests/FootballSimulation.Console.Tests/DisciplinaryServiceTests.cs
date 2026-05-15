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
        Assert.False(player.IsOnPitch);
        Assert.True(player.IsSuspended);
        Assert.Equal(1, player.SuspendedMatches);
        Assert.True(player.NewlySuspendedThisMatch);
        Assert.Equal(2, teamStats.YellowCards);
        Assert.Equal(1, teamStats.RedCards);
    }

    [Fact]
    public void AdvanceRecoveryAfterCompletedRound_ServesSuspensionAfterNextFixture()
    {
        var player = new Player
        {
            Name = "Suspended Player",
            Position = Position.Defender,
            SuspendedMatches = 1,
            NewlySuspendedThisMatch = true
        };
        var team = new Team { Name = "Test FC", Players = [player] };
        var recoveryService = new InjuryRecoveryService();

        recoveryService.AdvanceRecoveryAfterCompletedRound([team]);
        Assert.True(player.IsSuspended);
        Assert.Equal(1, player.SuspendedMatches);
        Assert.False(player.NewlySuspendedThisMatch);

        recoveryService.AdvanceRecoveryAfterCompletedRound([team]);
        Assert.False(player.IsSuspended);
        Assert.Equal(0, player.SuspendedMatches);
    }
}
