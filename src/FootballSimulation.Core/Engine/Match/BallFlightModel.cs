using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

/// <summary>
/// Lightweight analytic ball physics for the event-based match engine. Distances are metres,
/// speeds are metres per second, angles are degrees, and spin is revolutions per second.
/// It deliberately avoids frame-by-frame simulation while making physical context observable.
/// </summary>
public static class BallFlightModel
{
    private const double GoalHalfWidthMeters = 3.66;
    private const double AirDragPerSecond = 0.055;
    private const double MagnusDriftScale = 0.0018;

    public static ShotFlight SampleShot(
        Player shooter,
        string chanceType,
        WeatherCondition weather,
        double pressure,
        Random random)
    {
        var attributes = PlayerAttributeService.GetAttributes(shooter);
        var (meanDistance, distanceSpread, meanAngle) = chanceType switch
        {
            "long-range attempt" => (25.0, 4.0, 24.0),
            "through ball attempt" => (13.0, 3.0, 35.0),
            "dribble run" => (12.0, 3.5, 38.0),
            "cross into box" => (11.5, 3.0, 30.0),
            "one-on-one" => (10.0, 2.5, 42.0),
            "rebound shot" => (8.5, 2.5, 46.0),
            _ => (16.0, 4.5, 30.0)
        };

        var distance = Math.Clamp(meanDistance + NextGaussian(random) * distanceSpread, 4.0, 35.0);
        var angle = Math.Clamp(meanAngle + NextGaussian(random) * 8.0, 8.0, 60.0);
        var initialSpeed = Math.Clamp(
            20.0 + (attributes.Shooting - 50) * 0.22 + NextGaussian(random) * 1.8,
            18.0,
            36.0);
        var flightTime = distance / Math.Max(12.0, initialSpeed * 0.88);
        var dragModifier = weather is WeatherCondition.HeavyRain or WeatherCondition.Storm ? 1.12 : 1.0;
        var arrivalSpeed = initialSpeed * Math.Exp(-AirDragPerSecond * dragModifier * flightTime);
        var spin = Math.Clamp(3.0 + (attributes.Dribbling - 50) * 0.08 + NextGaussian(random) * 1.2, 0.0, 10.0);
        var windSpeed = GetWindSpeed(weather);
        var lateralDrift = (MagnusDriftScale * spin * initialSpeed + windSpeed * 0.012) * flightTime * flightTime;

        var fatiguePenalty = Math.Max(0.0, 65.0 - shooter.Stamina) / 65.0;
        var technique = Math.Clamp((attributes.Shooting + shooter.Finishing) / 198.0, 0.35, 1.0);
        var errorSigma = (4.0 + distance * 0.17 + pressure * 5.0 + fatiguePenalty * 3.0) / technique;
        errorSigma *= GetControlErrorModifier(weather);
        var aimError = NextGaussian(random) * errorSigma + lateralDrift * 0.65;
        var goalHalfAngle = Math.Atan(GoalHalfWidthMeters / distance) * 180.0 / Math.PI;
        var onTarget = Math.Abs(aimError) <= goalHalfAngle;

        // Calibrates the old categorical xG bands to spatial shot quality without replacing them.
        var angleQuality = Math.Clamp(angle / 42.0, 0.35, 1.0);
        var distanceQuality = Math.Clamp(1.08 - Math.Max(0.0, distance - 10.0) * 0.018, 0.58, 1.08);
        var goalModifier = Math.Clamp(0.60 * angleQuality * distanceQuality / GetControlErrorModifier(weather), 0.36, 0.70);

        return new ShotFlight(distance, angle, initialSpeed, arrivalSpeed, spin, lateralDrift, aimError, onTarget, goalModifier);
    }

    public static PassFlight EstimateAveragePass(Team team, WeatherCondition weather)
    {
        var averagePassing = team.Players.Count == 0 ? 50.0 : team.Players.Average(player => player.Passing);
        var distance = Math.Clamp(14.0 + team.Tactics.Width * 0.07 + team.Tactics.Tempo * 0.035, 12.0, 26.0);
        var initialSpeed = Math.Clamp(14.0 + averagePassing * 0.11 + team.Tactics.Tempo * 0.018, 15.0, 25.0);
        var surfaceModifier = GetRollingSurfaceModifier(weather);
        var travelTime = distance / Math.Max(8.0, initialSpeed * 0.78);
        var arrivalSpeed = initialSpeed * Math.Exp(-0.075 * travelTime) * surfaceModifier;
        return new PassFlight(distance, initialSpeed, arrivalSpeed);
    }

    public static double GetWoodworkProbability(ShotFlight flight, WeatherCondition weather)
    {
        var nearPostError = Math.Clamp(1.0 - Math.Abs(flight.AimErrorDegrees) / 12.0, 0.0, 1.0);
        var weatherBonus = weather is WeatherCondition.Storm or WeatherCondition.Windy ? 0.003 : 0.0;
        return Math.Clamp(0.014 + nearPostError * 0.010 + weatherBonus, 0.012, 0.030);
    }

    public static double GetBounceRetention(WeatherCondition weather) => weather switch
    {
        WeatherCondition.HeavyRain or WeatherCondition.Storm => 0.48,
        WeatherCondition.Rainy => 0.55,
        WeatherCondition.Snow => 0.42,
        _ => 0.62
    };

    private static double GetWindSpeed(WeatherCondition weather) => weather switch
    {
        WeatherCondition.Storm => 12.0,
        WeatherCondition.Windy => 8.0,
        _ => 1.5
    };

    private static double GetControlErrorModifier(WeatherCondition weather) => weather switch
    {
        WeatherCondition.Storm => 1.22,
        WeatherCondition.HeavyRain => 1.16,
        WeatherCondition.Windy or WeatherCondition.Snow => 1.10,
        WeatherCondition.Rainy => 1.06,
        _ => 1.0
    };

    private static double GetRollingSurfaceModifier(WeatherCondition weather) => weather switch
    {
        WeatherCondition.HeavyRain or WeatherCondition.Storm => 0.88,
        WeatherCondition.Snow => 0.80,
        WeatherCondition.Rainy => 0.95,
        _ => 1.0
    };

    private static double NextGaussian(Random random)
    {
        var first = Math.Max(double.Epsilon, random.NextDouble());
        var second = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(first)) * Math.Cos(2.0 * Math.PI * second);
    }
}

public readonly record struct ShotFlight(
    double DistanceMeters,
    double AngleDegrees,
    double InitialSpeedMetersPerSecond,
    double ArrivalSpeedMetersPerSecond,
    double SpinRevolutionsPerSecond,
    double LateralDriftMeters,
    double AimErrorDegrees,
    bool IsOnTarget,
    double GoalProbabilityModifier);

public readonly record struct PassFlight(
    double DistanceMeters,
    double InitialSpeedMetersPerSecond,
    double ArrivalSpeedMetersPerSecond);
