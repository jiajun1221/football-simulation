using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class GeneratedPlayerNameServiceTests
{
    [Fact]
    public void GenerateSeasonalIntake_DoesNotRepeatYouthNamesWithinAcademy()
    {
        var academy = new YouthAcademy
        {
            ClubId = "pl:test-club",
            ClubName = "Test Club",
            AcademyLevel = AcademyLevel.Elite,
            Reputation = 90
        };
        var team = new Team { Name = academy.ClubName };

        var players = new YouthPlayerGeneratorService()
            .GenerateSeasonalIntake(academy, team, "2029-30", count: 30);

        Assert.Equal(30, players.Count);
        Assert.Equal(30, players.Select(player => player.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
    }

    [Fact]
    public void CreateUniqueName_ProducesLargeNonRepeatingCountryPool()
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var random = new Random(42);

        var names = Enumerable.Range(0, 100)
            .Select(_ => GeneratedPlayerNameService.CreateUniqueName("England", random, usedNames))
            .ToList();

        Assert.Equal(100, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(names, name => name.EndsWith(" 2", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateUniqueName_AvoidsNamesAlreadyUsedByExistingPlayers()
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Vinicius Silva",
            "Gabriel Silva",
            "Pedro Silva"
        };

        var generated = GeneratedPlayerNameService.CreateUniqueName(
            "Brazil",
            new Random(7),
            usedNames,
            preferredLastName: "Silva");

        Assert.DoesNotContain(generated, new[] { "Vinicius Silva", "Gabriel Silva", "Pedro Silva" });
        Assert.EndsWith(" Silva", generated);
        Assert.Equal(4, usedNames.Count);
    }
}
