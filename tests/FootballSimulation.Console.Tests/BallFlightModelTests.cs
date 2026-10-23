using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Console.Tests;

public class BallFlightModelTests
{
    [Fact]
    public void SampleShot_WithSameSeed_IsReproducibleAndUsesFootballUnits()
    {
        var (team, _) = new SeedDataService().CreateDemoTeams();
        var shooter = team.Players.First(player => player.Position == Position.Forward);

        var first = BallFlightModel.SampleShot(shooter, "long-range attempt", WeatherCondition.Windy, 0.5, new Random(42));
        var second = BallFlightModel.SampleShot(shooter, "long-range attempt", WeatherCondition.Windy, 0.5, new Random(42));

        Assert.Equal(first, second);
        Assert.InRange(first.DistanceMeters, 4.0, 35.0);
        Assert.InRange(first.InitialSpeedMetersPerSecond, 18.0, 36.0);
        Assert.True(first.ArrivalSpeedMetersPerSecond < first.InitialSpeedMetersPerSecond);
        Assert.NotEqual(0.0, first.LateralDriftMeters);
    }

    [Fact]
    public void HeavyRain_SlowsAveragePassMoreThanClearWeather()
    {
        var (team, _) = new SeedDataService().CreateDemoTeams();

        var clear = BallFlightModel.EstimateAveragePass(team, WeatherCondition.Clear);
        var heavyRain = BallFlightModel.EstimateAveragePass(team, WeatherCondition.HeavyRain);

        Assert.Equal(clear.InitialSpeedMetersPerSecond, heavyRain.InitialSpeedMetersPerSecond);
        Assert.True(heavyRain.ArrivalSpeedMetersPerSecond < clear.ArrivalSpeedMetersPerSecond);
    }

    [Fact]
    public void SimulateMatch_StoresSeedAndPhysicalTelemetry()
    {
        var (homeTeam, awayTeam) = new SeedDataService().CreateDemoTeams();

        var match = new MatchEngine().SimulateMatch(homeTeam, awayTeam, seed: 31415);

        Assert.Equal(31415, match.SimulationSeed);
        Assert.True(match.HomeStats.ShotPhysicsSamples + match.AwayStats.ShotPhysicsSamples > 0);
        Assert.InRange(match.HomeStats.AveragePassLengthMeters, 12.0, 26.0);
        Assert.InRange(match.HomeStats.AveragePassSpeedMetersPerSecond, 15.0, 25.0);
        var measuredShot = match.Events.First(matchEvent => matchEvent.EventType == EventType.Shot && matchEvent.ExpectedGoals.HasValue);
        Assert.InRange(measuredShot.ShotDistanceMeters!.Value, 4.0, 35.0);
        Assert.InRange(measuredShot.BallSpeedMetersPerSecond!.Value, 18.0, 36.0);
    }
}
