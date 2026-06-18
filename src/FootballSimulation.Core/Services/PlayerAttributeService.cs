using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerAttributeService
{
    public static PlayerAttributeRatings GetAttributes(Player player)
    {
        if (player.Pace > 0 &&
            player.Shooting > 0 &&
            player.Passing > 0 &&
            player.Dribbling > 0 &&
            player.Defending > 0 &&
            player.Physical > 0)
        {
            return new PlayerAttributeRatings(
                player.Pace,
                player.Shooting,
                player.Passing,
                player.Dribbling,
                player.Defending,
                player.Physical);
        }

        return DeriveAttributes(
            player.Position,
            player.PreferredPosition,
            player.OverallRating,
            player.Traits,
            (int)Math.Round(player.Stamina));
    }

    public static void ApplyMissingAttributes(Player player)
    {
        PlayerTraitAssignmentService.EnsureMinimumTraits(player);
        var attributes = GetAttributes(player);
        player.Pace = player.Pace <= 0 ? attributes.Pace : player.Pace;
        player.Shooting = player.Shooting <= 0 ? attributes.Shooting : player.Shooting;
        player.Passing = player.Passing <= 0 ? attributes.Passing : player.Passing;
        player.Dribbling = player.Dribbling <= 0 ? attributes.Dribbling : player.Dribbling;
        player.Defending = player.Defending <= 0 ? attributes.Defending : player.Defending;
        player.Physical = player.Physical <= 0 ? attributes.Physical : player.Physical;
    }

    public static PlayerAttributeRatings DeriveAttributes(
        Position position,
        string exactPosition,
        int overall,
        IReadOnlyCollection<PlayerTrait>? traits = null,
        int? stamina = null)
    {
        var normalized = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        var attributes = normalized switch
        {
            "GK" => new PlayerAttributeRatings(overall - 44, overall - 48, overall - 12, overall - 42, overall + 3, overall - 4),
            "CB" => new PlayerAttributeRatings(overall - 8, overall - 22, overall - 1, overall - 7, overall + 8, overall + 7),
            "LB" or "RB" => new PlayerAttributeRatings(overall + 3, overall - 16, overall + 3, overall + 1, overall + 4, overall + 1),
            "CDM" => new PlayerAttributeRatings(overall - 1, overall - 14, overall + 4, overall, overall + 5, overall + 4),
            "CM" => new PlayerAttributeRatings(overall, overall - 8, overall + 6, overall + 3, overall - 1, overall + 1),
            "CAM" => new PlayerAttributeRatings(overall + 2, overall, overall + 5, overall + 5, overall - 16, overall - 4),
            "LW" or "RW" => new PlayerAttributeRatings(overall + 7, overall - 1, overall + 1, overall + 7, overall - 18, overall - 3),
            "ST" => new PlayerAttributeRatings(overall + 2, overall + 6, overall - 3, overall + 1, overall - 22, overall + 3),
            _ => DeriveFromGenericPosition(position, overall)
        };

        attributes = attributes with
        {
            Physical = stamina is null ? attributes.Physical : (int)Math.Round(attributes.Physical * 0.65 + stamina.Value * 0.35)
        };

        if (traits is not null)
        {
            attributes = ApplyTraitBonuses(attributes, traits);
        }

        return Clamp(attributes);
    }

    private static PlayerAttributeRatings DeriveFromGenericPosition(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => new PlayerAttributeRatings(overall - 44, overall - 48, overall - 12, overall - 42, overall + 3, overall - 4),
            Position.Defender => new PlayerAttributeRatings(overall - 3, overall - 18, overall + 1, overall - 3, overall + 6, overall + 5),
            Position.Midfielder => new PlayerAttributeRatings(overall, overall - 8, overall + 5, overall + 2, overall, overall + 1),
            Position.Forward => new PlayerAttributeRatings(overall + 4, overall + 4, overall - 1, overall + 4, overall - 20, overall + 1),
            _ => new PlayerAttributeRatings(overall, overall, overall, overall, overall, overall)
        };
    }

    private static PlayerAttributeRatings ApplyTraitBonuses(PlayerAttributeRatings attributes, IReadOnlyCollection<PlayerTrait> traits)
    {
        return attributes with
        {
            Pace = attributes.Pace +
                (traits.Contains(PlayerTrait.Rapid) ? 4 : 0) +
                (traits.Contains(PlayerTrait.TriesToBeatOffsideTrap) ? 2 : 0),
            Shooting = attributes.Shooting +
                (traits.Contains(PlayerTrait.ClinicalFinisher) ? 4 : 0) +
                (traits.Contains(PlayerTrait.FinesseShot) ? 2 : 0) +
                (traits.Contains(PlayerTrait.LongShotTaker) ? 2 : 0),
            Passing = attributes.Passing +
                (traits.Contains(PlayerTrait.Playmaker) ? 4 : 0) +
                (traits.Contains(PlayerTrait.LongPasser) ? 3 : 0) +
                (traits.Contains(PlayerTrait.EarlyCrosser) ? 2 : 0) +
                (traits.Contains(PlayerTrait.DeadBallSpecialist) ? 2 : 0),
            Dribbling = attributes.Dribbling +
                (traits.Contains(PlayerTrait.SpeedDribbler) ? 3 : 0) +
                (traits.Contains(PlayerTrait.TechnicalDribbler) ? 4 : 0) +
                (traits.Contains(PlayerTrait.Flair) ? 2 : 0) +
                (traits.Contains(PlayerTrait.PressResistant) ? 2 : 0),
            Defending = attributes.Defending +
                (traits.Contains(PlayerTrait.Interceptor) ? 4 : 0) +
                (traits.Contains(PlayerTrait.DivesIntoTackles) ? 3 : 0),
            Physical = attributes.Physical +
                (traits.Contains(PlayerTrait.AerialThreat) ? 4 : 0) +
                (traits.Contains(PlayerTrait.PowerHeader) ? 3 : 0) +
                (traits.Contains(PlayerTrait.Engine) ? 2 : 0)
        };
    }

    private static PlayerAttributeRatings Clamp(PlayerAttributeRatings attributes)
    {
        return new PlayerAttributeRatings(
            ClampStat(attributes.Pace),
            ClampStat(attributes.Shooting),
            ClampStat(attributes.Passing),
            ClampStat(attributes.Dribbling),
            ClampStat(attributes.Defending),
            ClampStat(attributes.Physical));
    }

    private static int ClampStat(int value)
    {
        return Math.Clamp(value, 1, 99);
    }
}

public sealed record PlayerAttributeRatings(
    int Pace,
    int Shooting,
    int Passing,
    int Dribbling,
    int Defending,
    int Physical);
