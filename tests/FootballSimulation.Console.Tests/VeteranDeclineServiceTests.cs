using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class VeteranDeclineServiceTests
{
    [Fact]
    public void ApplySeasonDecline_PoorFormVeteranDeclinesFasterWithAge()
    {
        var age32 = CreatePlayer(32, PlayerFormStatus.Poor, 40);
        var age37 = CreatePlayer(37, PlayerFormStatus.Poor, 40);

        var youngerDecline = VeteranDeclineService.ApplySeasonDecline(age32);
        var olderDecline = VeteranDeclineService.ApplySeasonDecline(age37);

        Assert.True(olderDecline > youngerDecline);
        Assert.Equal(90 - youngerDecline, age32.OverallRating);
        Assert.Equal(90 - olderDecline, age37.OverallRating);
    }

    [Fact]
    public void ApplySeasonDecline_ExcellentFormSlowsButDoesNotEraseLateCareerDecline()
    {
        var excellentVeteran = CreatePlayer(38, PlayerFormStatus.Excellent, 80);
        var poorVeteran = CreatePlayer(38, PlayerFormStatus.VeryPoor, 20);

        var excellentDecline = VeteranDeclineService.ApplySeasonDecline(excellentVeteran);
        var poorDecline = VeteranDeclineService.ApplySeasonDecline(poorVeteran);

        Assert.InRange(excellentDecline, 1, 3);
        Assert.True(poorDecline > excellentDecline);
    }

    [Fact]
    public void ApplySeasonDecline_GoalkeeperDeclinesLaterThanOutfieldPlayer()
    {
        var outfieldPlayer = CreatePlayer(32, PlayerFormStatus.Average, 50);
        var goalkeeper = CreatePlayer(32, PlayerFormStatus.Average, 50, Position.Goalkeeper, "GK");

        Assert.Equal(1, VeteranDeclineService.ApplySeasonDecline(outfieldPlayer));
        Assert.Equal(0, VeteranDeclineService.ApplySeasonDecline(goalkeeper));
    }

    [Fact]
    public void ApplySeasonDecline_ReducesAttributesWithOverall()
    {
        var player = CreatePlayer(36, PlayerFormStatus.Poor, 35);

        var decline = VeteranDeclineService.ApplySeasonDecline(player);

        Assert.True(decline >= 4);
        Assert.Equal(90 - decline, player.OverallRating);
        Assert.Equal(90 - decline, player.Shooting);
        Assert.True(player.Pace < player.Shooting);
        Assert.Equal(player.OverallRating + 2, player.PotentialOverall);
    }

    private static Player CreatePlayer(
        int age,
        PlayerFormStatus form,
        int currentForm,
        Position position = Position.Forward,
        string preferredPosition = "ST")
    {
        return new Player
        {
            Name = "Veteran",
            Age = age,
            Position = position,
            PreferredPosition = preferredPosition,
            OverallRating = 90,
            BaseOverallRating = 90,
            PotentialOverall = 92,
            FormStatus = form,
            CurrentForm = currentForm,
            Attack = 90,
            Defense = 90,
            Passing = 90,
            Finishing = 90,
            Pace = 90,
            Shooting = 90,
            Dribbling = 90,
            Defending = 90,
            Physical = 90,
            Stamina = 100
        };
    }
}
