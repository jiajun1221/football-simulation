using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class AiLineupSelectionServiceTests
{
    [Fact]
    public void BuildRealisticLineup_RotatesHighRiskStarterForFreshComparablePlayer()
    {
        var tiredMidfielder = CreatePlayer("Tired CM", "CM", Position.Midfielder, 82, isStarter: true);
        tiredMidfielder.Stamina = 48;
        tiredMidfielder.SeasonFatigue = 86;
        tiredMidfielder.MatchesPlayedRecently = 5;
        tiredMidfielder.ConsecutiveStarts = 9;

        var freshMidfielder = CreatePlayer("Fresh CM", "CM", Position.Midfielder, 79, isStarter: false);
        freshMidfielder.Stamina = 96;
        freshMidfielder.SeasonFatigue = 0;

        var team = new Team
        {
            Name = "Rotation FC",
            Formation = "4-3-3 Holding",
            Players =
            [
                CreatePlayer("GK", "GK", Position.Goalkeeper, 78, isStarter: true),
                CreatePlayer("LB", "LB", Position.Defender, 78, isStarter: true),
                CreatePlayer("CB 1", "CB", Position.Defender, 78, isStarter: true),
                CreatePlayer("CB 2", "CB", Position.Defender, 78, isStarter: true),
                CreatePlayer("RB", "RB", Position.Defender, 78, isStarter: true),
                CreatePlayer("CDM", "CDM", Position.Midfielder, 78, isStarter: true),
                tiredMidfielder,
                CreatePlayer("CM 2", "CM", Position.Midfielder, 78, isStarter: true),
                CreatePlayer("LW", "LW", Position.Forward, 78, isStarter: true),
                CreatePlayer("RW", "RW", Position.Forward, 78, isStarter: true),
                CreatePlayer("ST", "ST", Position.Forward, 78, isStarter: true)
            ],
            Substitutes = [freshMidfielder],
            Tactics = new TeamTactics()
        };

        AiLineupSelectionService.BuildRealisticLineup(team);

        Assert.Contains(team.Players, player => player.Name == freshMidfielder.Name);
        Assert.Contains(team.Substitutes, player => player.Name == tiredMidfielder.Name);
    }

    [Fact]
    public void BuildRealisticLineup_KeepsCurrentFormationWhenAssignmentCannotBeBuilt()
    {
        var team = new Team
        {
            Name = "Thin Squad FC",
            Formation = "5-3-2",
            Players =
            [
                CreatePlayer("Starter GK", "GK", Position.Goalkeeper, 78, isStarter: true),
                CreatePlayer("Left Wing Back", "LWB", Position.Defender, 78, isStarter: true),
                CreatePlayer("Center Back", "CB", Position.Defender, 78, isStarter: true),
                CreatePlayer("Right Wing Back", "RWB", Position.Defender, 78, isStarter: true),
                CreatePlayer("Central Midfielder", "CM", Position.Midfielder, 78, isStarter: true),
                CreatePlayer("Right Winger 1", "RW", Position.Forward, 78, isStarter: true),
                CreatePlayer("Right Winger 2", "RW", Position.Forward, 78, isStarter: true),
                CreatePlayer("Right Winger 3", "RW", Position.Forward, 78, isStarter: true),
                CreatePlayer("Right Winger 4", "RW", Position.Forward, 78, isStarter: true),
                CreatePlayer("Right Winger 5", "RW", Position.Forward, 78, isStarter: true),
                CreatePlayer("Backup GK", "GK", Position.Goalkeeper, 76, isStarter: true)
            ],
            Substitutes = [],
            Tactics = new TeamTactics { Mentality = Mentality.Defensive }
        };

        team.Players[2].IsInjured = true;

        AiLineupSelectionService.BuildRealisticLineup(team);

        Assert.Equal("5-3-2", team.Formation);
        Assert.Equal(11, team.Players.Count);
    }

    private static Player CreatePlayer(string name, string preferredPosition, Position position, int overall, bool isStarter)
    {
        return new Player
        {
            Name = name,
            SquadNumber = Math.Abs(name.GetHashCode()) % 99 + 1,
            PreferredPosition = preferredPosition,
            AssignedPosition = preferredPosition,
            Position = position,
            OverallRating = overall,
            BaseOverallRating = overall,
            Stamina = 90,
            CurrentStamina = 90,
            Physical = 75,
            Attack = overall,
            Defense = overall,
            Passing = overall,
            Finishing = overall,
            IsStarter = isStarter,
            IsOnPitch = isStarter,
            Role = isStarter ? PlayerRole.Starter : PlayerRole.Rotation
        };
    }
}
