using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class TeamVenueService
{
    private static readonly Dictionary<string, TeamVenue> PremierLeagueVenues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AFC Bournemouth"] = new("Vitality Stadium"),
        ["Arsenal"] = new("Emirates Stadium"),
        ["Aston Villa"] = new("Villa Park"),
        ["Brentford"] = new("Gtech Community Stadium"),
        ["Brighton & Hove Albion"] = new("American Express Stadium", "Amex Stadium"),
        ["Burnley"] = new("Turf Moor"),
        ["Chelsea"] = new("Stamford Bridge"),
        ["Crystal Palace"] = new("Selhurst Park"),
        ["Everton"] = new("Hill Dickinson Stadium"),
        ["Fulham"] = new("Craven Cottage"),
        ["Leeds United"] = new("Elland Road"),
        ["Liverpool"] = new("Anfield"),
        ["Manchester City"] = new("Etihad Stadium"),
        ["Manchester United"] = new("Old Trafford"),
        ["Newcastle United"] = new("St James' Park"),
        ["Nottingham Forest"] = new("The City Ground"),
        ["Sunderland"] = new("Stadium of Light"),
        ["Tottenham Hotspur"] = new("Tottenham Hotspur Stadium"),
        ["West Ham United"] = new("London Stadium"),
        ["Wolverhampton Wanderers"] = new("Molineux Stadium")
    };

    public static void ApplyVenue(Team team, string? venue = null, string? stadiumName = null)
    {
        var teamVenue = GetVenue(team.Name, venue, stadiumName);
        team.Venue = teamVenue.Venue;
        team.StadiumName = teamVenue.StadiumName;
    }

    public static string GetDisplayVenue(Team team)
    {
        if (!string.IsNullOrWhiteSpace(team.Venue))
        {
            return team.Venue;
        }

        return GetVenue(team.Name).Venue;
    }

    public static TeamVenue GetVenue(string teamName, string? venue = null, string? stadiumName = null)
    {
        var mappedVenue = PremierLeagueVenues.TryGetValue(teamName, out var configuredVenue)
            ? configuredVenue
            : new TeamVenue($"{teamName} Stadium");

        var displayVenue = string.IsNullOrWhiteSpace(venue)
            ? mappedVenue.Venue
            : venue.Trim();
        var displayStadiumName = string.IsNullOrWhiteSpace(stadiumName)
            ? mappedVenue.StadiumName
            : stadiumName.Trim();

        return new TeamVenue(displayVenue, displayStadiumName);
    }
}

public sealed record TeamVenue(string Venue, string StadiumName)
{
    public TeamVenue(string venue)
        : this(venue, venue)
    {
    }
}
