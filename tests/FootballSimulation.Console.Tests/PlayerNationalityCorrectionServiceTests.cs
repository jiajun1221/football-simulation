using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PlayerNationalityCorrectionServiceTests
{
    [Fact]
    public void ApplyKnownCorrections_UsesNetherlandsFlagForGenericEnglandIdentity()
    {
        var player = new Player
        {
            Name = "Jorrel Hato",
            Nationality = "Netherlands",
            NationalityName = "Netherlands",
            NationalityCode = "GB-ENG",
            FlagImagePath = "/Assets/Flags/england.png"
        };

        PlayerNationalityCorrectionService.ApplyKnownCorrections(player);

        Assert.Equal("NL", player.NationalityCode);
        Assert.Equal("/Assets/Flags/netherlands.png", player.FlagImagePath);
    }

    [Fact]
    public void ApplyKnownCorrections_PreservesExplicitAlternateNationalTeam()
    {
        var player = new Player
        {
            Name = "Kevin Diks",
            Nationality = "Netherlands",
            NationalityName = "Netherlands",
            NationalityCode = "ID",
            FlagImagePath = "/Assets/Flags/indonesia.png"
        };

        PlayerNationalityCorrectionService.ApplyKnownCorrections(player);

        Assert.Equal("ID", player.NationalityCode);
        Assert.Equal("/Assets/Flags/indonesia.png", player.FlagImagePath);
    }
}
