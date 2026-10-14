using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class YouthMarketValueCalculator
{
    public static decimal CalculateMarketValue(YouthPlayer player, YouthAcademy academy)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(academy);

        var potentialScore = (player.PotentialMin + player.PotentialMax + player.HiddenTruePotential) / 3.0;
        var baseValue = player.CurrentOVR switch
        {
            >= 66 => 1_800_000m,
            >= 62 => 1_150_000m,
            >= 58 => 700_000m,
            >= 54 => 425_000m,
            >= 50 => 250_000m,
            _ => 120_000m
        };

        var potentialMultiplier = potentialScore switch
        {
            >= 94 => 8.5m,
            >= 90 => 5.2m,
            >= 86 => 3.2m,
            >= 81 => 2.0m,
            >= 75 => 1.35m,
            _ => 0.9m
        };
        var ageMultiplier = player.Age switch
        {
            <= 15 => 1.18m,
            16 => 1.12m,
            17 => 1.04m,
            _ => 0.96m
        };
        var developmentMultiplier = player.DevelopmentRate switch
        {
            YouthDevelopmentRate.Explosive => 1.35m,
            YouthDevelopmentRate.Fast => 1.18m,
            YouthDevelopmentRate.Slow => 0.84m,
            _ => 1.0m
        };
        var rarityMultiplier = player.PotentialTier switch
        {
            YouthPotentialTier.GenerationalTalent => 1.55m,
            YouthPotentialTier.EliteProspect => 1.30m,
            YouthPotentialTier.ExcitingProspect => 1.12m,
            YouthPotentialTier.CommonProspect => 0.90m,
            _ => 1.0m
        };
        var academyMultiplier = academy.AcademyLevel switch
        {
            AcademyLevel.Elite => 1.20m,
            AcademyLevel.Gold => 1.10m,
            AcademyLevel.Bronze => 0.88m,
            _ => 1.0m
        };
        var positionMultiplier = player.Position switch
        {
            Position.Forward => 1.10m,
            Position.Midfielder => 1.05m,
            Position.Goalkeeper => 0.84m,
            _ => 0.96m
        };

        return RoundToYouthFigure(baseValue *
            potentialMultiplier *
            ageMultiplier *
            developmentMultiplier *
            rarityMultiplier *
            academyMultiplier *
            positionMultiplier);
    }

    public static decimal CalculateAskingPrice(YouthPlayer player, YouthAcademy academy)
    {
        var marketValue = CalculateMarketValue(player, academy);
        var protectionMultiplier = player.PotentialTier switch
        {
            YouthPotentialTier.GenerationalTalent => 2.4m,
            YouthPotentialTier.EliteProspect => 1.85m,
            YouthPotentialTier.ExcitingProspect => 1.45m,
            YouthPotentialTier.CommonProspect => 1.05m,
            _ => 1.20m
        };
        protectionMultiplier += academy.AcademyLevel == AcademyLevel.Elite ? 0.25m : 0m;
        return RoundToYouthFigure(marketValue * protectionMultiplier);
    }

    private static decimal RoundToYouthFigure(decimal value)
    {
        if (value >= 10_000_000m)
        {
            return Math.Round(value / 500_000m, MidpointRounding.AwayFromZero) * 500_000m;
        }

        if (value >= 1_000_000m)
        {
            return Math.Round(value / 100_000m, MidpointRounding.AwayFromZero) * 100_000m;
        }

        return Math.Round(value / 25_000m, MidpointRounding.AwayFromZero) * 25_000m;
    }
}

public static class YouthScoutReportService
{
    public static string CreateScoutReport(YouthPlayer player)
    {
        if (player.PotentialTier == YouthPotentialTier.GenerationalTalent)
        {
            return "Potential superstar if developed properly.";
        }

        if (player.PotentialTier == YouthPotentialTier.EliteProspect)
        {
            return "Has potential to be a special player.";
        }

        if (player.Traits.Contains(PlayerTrait.Rapid))
        {
            return "Elite pace for his age.";
        }

        if (player.PreferredPosition is "CM" or "CAM" || player.Traits.Contains(PlayerTrait.Playmaker))
        {
            return "Technically gifted midfielder.";
        }

        if (player.DevelopmentRate == YouthDevelopmentRate.Fast)
        {
            return "Could become a first-team regular.";
        }

        if (player.Position is Position.Defender or Position.Forward && player.CurrentOVR < 58)
        {
            return "Raw but physically impressive.";
        }

        return "Could become a first-team regular.";
    }
}
