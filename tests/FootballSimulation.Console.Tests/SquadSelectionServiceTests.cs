using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SquadSelectionServiceTests
{
    [Fact]
    public void SwapStarterWithSubstitute_PreservesElevenStarters()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];

        var result = service.SwapStarterWithSubstitute(team, starter, substitute);

        Assert.True(result.Success);
        Assert.Equal(11, team.Players.Count);
        Assert.Contains(substitute, team.Players);
        Assert.Contains(starter, team.Substitutes);
        Assert.True(substitute.IsStarter);
        Assert.False(starter.IsStarter);
    }

    [Fact]
    public void SwapStarterWithSubstitute_RecordsHalftimeSubstitution()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var match = new Match { HomeTeam = team, AwayTeam = CreateTeam("Opponent") };
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];

        var result = service.SwapStarterWithSubstitute(team, starter, substitute, match, 45);

        Assert.True(result.Success);
        var substitution = Assert.Single(match.Substitutions);
        Assert.Equal(45, substitution.Minute);
        Assert.Equal(starter.Name, substitution.PlayerOffName);
        Assert.Equal(substitute.Name, substitution.PlayerOnName);
    }

    [Fact]
    public void SwapStarterWithSubstitute_RejectsMoreThanFiveMatchSubstitutions()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam(substituteCount: 7);
        var match = new Match { HomeTeam = team, AwayTeam = CreateTeam("Opponent") };

        for (var index = 0; index < 5; index++)
        {
            var result = service.SwapStarterWithSubstitute(team, team.Players[index + 1], team.Substitutes[0], match, 45);
            Assert.True(result.Success);
        }

        var rejected = service.SwapStarterWithSubstitute(team, team.Players[6], team.Substitutes[0], match, 45);

        Assert.False(rejected.Success);
        Assert.Equal(5, service.CountTeamSubstitutions(match, team.Name));
    }

    [Fact]
    public void SubstituteKeepsFresherStaminaThanTiredStarter()
    {
        var team = CreateTeam();
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];
        starter.CurrentStamina = 35;
        substitute.CurrentStamina = substitute.Stamina;

        new SquadSelectionService().SwapStarterWithSubstitute(team, starter, substitute);

        Assert.True(substitute.CurrentStamina > starter.CurrentStamina);
    }

    private static Team CreateTeam(string name = "Test FC", int substituteCount = 7)
    {
        var players = new List<Player>
        {
            CreatePlayer("Starter GK", Position.Goalkeeper, isStarter: true)
        };

        for (var index = 1; index < 11; index++)
        {
            players.Add(CreatePlayer($"Starter {index}", Position.Defender, isStarter: true));
        }

        var substitutes = new List<Player>
        {
            CreatePlayer("Sub GK", Position.Goalkeeper, isStarter: false)
        };

        for (var index = 1; index < substituteCount; index++)
        {
            substitutes.Add(CreatePlayer($"Sub {index}", Position.Midfielder, isStarter: false));
        }

        return new Team
        {
            Name = name,
            Formation = "4-3-3",
            Players = players,
            Substitutes = substitutes
        };
    }

    private static Player CreatePlayer(string name, Position position, bool isStarter)
    {
        return new Player
        {
            Name = name,
            Position = position,
            OverallRating = 75,
            Form = "Average",
            IsStarter = isStarter,
            Attack = 75,
            Defense = 75,
            Passing = 75,
            Stamina = 75,
            CurrentStamina = 75,
            Finishing = 75
        };
    }
}
