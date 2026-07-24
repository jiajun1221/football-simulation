using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LiveMatchRosterRepairTests
{
    [Fact]
    public void AdvanceLiveFixture_RepairsMalformedAiLineupBeforePlayback()
    {
        var homeTeam = PlaceholderTeamFactory.Create("Chelsea", 82);
        var awayTeam = PlaceholderTeamFactory.Create("Aston Villa", 80);
        var fixture = new Fixture
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            RoundNumber = 1
        };
        var league = new League
        {
            Name = "Premier League",
            Teams = [homeTeam, awayTeam],
            Fixtures = [fixture]
        };
        var options = new MatchSimulationOptions
        {
            HumanControlledTeamName = homeTeam.Name,
            PreserveMatchStartStamina = true
        };
        var engine = new LeagueEngine();
        var match = engine.CreateLiveFixtureMatch(league, fixture, options);
        var missingStarter = awayTeam.Players[^1];
        awayTeam.Players.Remove(missingStarter);
        missingStarter.IsStarter = false;
        missingStarter.IsOnPitch = false;
        awayTeam.Substitutes.Add(missingStarter);

        engine.AdvanceLiveFixture(
            league,
            fixture,
            match,
            startMinute: 1,
            endMinute: 1,
            includeFulltime: false,
            options: options);

        Assert.Equal(11, match.AwayTeam.Players.Count);
        Assert.Equal(1, match.CurrentMinute);
    }
}
