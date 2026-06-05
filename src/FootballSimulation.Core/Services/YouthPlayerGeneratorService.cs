using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class YouthPlayerGeneratorService
{
    private static readonly string[] EliteAcademies =
    [
        "Barcelona",
        "Real Madrid",
        "Chelsea",
        "Manchester City",
        "Man City",
        "Arsenal",
        "PSG",
        "Paris Saint-Germain",
        "Bayern",
        "Bayern Munich",
        "Dortmund",
        "Borussia Dortmund",
        "Ajax"
    ];

    private static readonly (string Name, string Code, string FlagPath, string[] FirstNames, string[] LastNames)[] NationalityPools =
    [
        ("England", "GB-ENG", "Assets/Flags/england.png", ["Alfie", "Archie", "Ethan", "Leo", "Oscar", "Theo"], ["Bennett", "Cole", "Foster", "Hughes", "Parker", "Ward"]),
        ("Spain", "ES", "Assets/Flags/spain.png", ["Diego", "Hugo", "Iker", "Mateo", "Nico", "Pablo"], ["Alonso", "Garcia", "Ramos", "Santos", "Vega", "Torres"]),
        ("France", "FR", "Assets/Flags/france.png", ["Enzo", "Hugo", "Lucas", "Mathis", "Noah", "Theo"], ["Bernard", "Dubois", "Moreau", "Roux", "Lefevre", "Girard"]),
        ("Germany", "DE", "Assets/Flags/germany.png", ["Ben", "Emil", "Finn", "Jonas", "Leon", "Lukas"], ["Bauer", "Fischer", "Klein", "Meyer", "Schulz", "Weber"]),
        ("Brazil", "BR", "Assets/Flags/brazil.png", ["Bruno", "Caio", "Felipe", "Joao", "Lucas", "Rafael"], ["Alves", "Costa", "Lima", "Mendes", "Pereira", "Silva"]),
        ("Netherlands", "NL", "Assets/Flags/netherlands.png", ["Daan", "Finn", "Jens", "Lars", "Milan", "Sem"], ["Bakker", "Jansen", "Meijer", "Smit", "Visser", "Vos"])
    ];

    private static readonly (string Slot, Position Position, int Weight)[] PositionWeights =
    [
        ("GK", Position.Goalkeeper, 8),
        ("CB", Position.Defender, 15),
        ("LB", Position.Defender, 8),
        ("RB", Position.Defender, 8),
        ("CDM", Position.Midfielder, 9),
        ("CM", Position.Midfielder, 15),
        ("CAM", Position.Midfielder, 9),
        ("LW", Position.Forward, 9),
        ("RW", Position.Forward, 9),
        ("ST", Position.Forward, 10)
    ];

    public YouthAcademy CreateAcademy(Team team, string leagueId = "", string season = "")
    {
        ArgumentNullException.ThrowIfNull(team);

        var level = GetDefaultAcademyLevel(team.Name);
        return new YouthAcademy
        {
            ClubId = CreateClubId(leagueId, team.Name),
            ClubName = team.Name,
            AcademyLevel = level,
            Reputation = GetDefaultReputation(team.Name, level),
            ScoutFocus = YouthScoutFocus.Balanced,
            TrainingFocus = YouthTrainingFocus.Balanced
        };
    }

    public List<YouthPlayer> GenerateSeasonalIntake(YouthAcademy academy, Team team, string season, int count)
    {
        ArgumentNullException.ThrowIfNull(academy);
        ArgumentNullException.ThrowIfNull(team);

        var seed = Math.Abs(HashCode.Combine(academy.ClubId, academy.ClubName, season, academy.IntakeHistory.Count));
        var random = new Random(seed);
        var players = new List<YouthPlayer>();
        for (var index = 0; index < count; index++)
        {
            players.Add(GeneratePlayer(academy, team, season, random, index));
        }

        return players;
    }

    public YouthPlayer GenerateScoutDiscovery(YouthAcademy academy, Team team, string season, int currentRound)
    {
        var seed = Math.Abs(HashCode.Combine(academy.ClubId, season, currentRound, academy.YouthPlayers.Count));
        return GeneratePlayer(academy, team, season, new Random(seed), academy.YouthPlayers.Count, discoveryBoost: true);
    }

    public static string CreateClubId(string leagueId, string clubName)
    {
        var normalizedLeague = string.IsNullOrWhiteSpace(leagueId) ? "league" : NormalizeId(leagueId);
        return $"{normalizedLeague}:{NormalizeId(clubName)}";
    }

    private static YouthPlayer GeneratePlayer(
        YouthAcademy academy,
        Team team,
        string season,
        Random random,
        int index,
        bool discoveryBoost = false)
    {
        var tier = PickTier(academy, random, discoveryBoost);
        var position = PickPosition(academy, team, random);
        var potentialRange = GetPotentialRange(tier, academy, random);
        var truePotential = random.Next(potentialRange.Min, potentialRange.Max + 1);
        var currentOverall = Math.Clamp(random.Next(45, 63) + GetCurrentOverallBonus(academy, tier, random), 45, Math.Min(68, truePotential - 4));
        var nationality = PickNationality(team, random);
        var traits = PickTraits(position.Slot, academy.ScoutFocus, tier, random);
        var player = new YouthPlayer
        {
            PlayerId = $"youth-{NormalizeId(academy.ClubName)}-{NormalizeId(season)}-{Guid.NewGuid():N}",
            Name = CreateName(nationality, random),
            Nationality = nationality.Name,
            NationalityName = nationality.Name,
            NationalityCode = nationality.Code,
            FlagImagePath = nationality.FlagPath,
            Age = random.Next(15, 19),
            Position = position.Position,
            PreferredPosition = position.Slot,
            SecondaryPositions = GetSecondaryPositions(position.Slot),
            CurrentOVR = currentOverall,
            PotentialMin = potentialRange.Min,
            PotentialMax = potentialRange.Max,
            HiddenTruePotential = truePotential,
            PotentialTier = tier,
            Traits = traits,
            Personality = PickPersonality(random),
            DevelopmentRate = PickDevelopmentRate(tier, random),
            ClubId = academy.ClubId,
            ClubName = academy.ClubName,
            IntakeSeason = GetSeasonStartYear(season)
        };

        player.MarketValue = YouthMarketValueCalculator.CalculateMarketValue(player, academy);
        player.ScoutReport = YouthScoutReportService.CreateScoutReport(player);
        return player;
    }

    private static YouthPotentialTier PickTier(YouthAcademy academy, Random random, bool discoveryBoost)
    {
        var eliteBoost = academy.AcademyLevel switch
        {
            AcademyLevel.Elite => 5.0,
            AcademyLevel.Gold => 2.5,
            AcademyLevel.Silver => 0.5,
            _ => -1.5
        };
        eliteBoost += Math.Max(0, academy.Reputation - 60) / 20.0;
        if (discoveryBoost)
        {
            eliteBoost += 1.5;
        }

        var common = 55.0 - eliteBoost;
        var good = 25.0 - eliteBoost * 0.35;
        var exciting = 13.0 + eliteBoost * 0.35;
        var elite = 5.0 + eliteBoost * 0.55;
        var generational = 2.0 + eliteBoost * 0.45;
        var roll = random.NextDouble() * (common + good + exciting + elite + generational);

        if ((roll -= common) < 0) return YouthPotentialTier.CommonProspect;
        if ((roll -= good) < 0) return YouthPotentialTier.GoodProspect;
        if ((roll -= exciting) < 0) return YouthPotentialTier.ExcitingProspect;
        if ((roll -= elite) < 0) return YouthPotentialTier.EliteProspect;
        return YouthPotentialTier.GenerationalTalent;
    }

    private static (string Slot, Position Position) PickPosition(YouthAcademy academy, Team team, Random random)
    {
        var seniorCounts = team.Players.Concat(team.Substitutes)
            .Select(player => PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition))
            .GroupBy(position => position)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var weighted = PositionWeights
            .Select(item => new
            {
                item.Slot,
                item.Position,
                Weight = item.Weight + GetScoutFocusWeight(item.Slot, academy.ScoutFocus) + Math.Max(0, 3 - seniorCounts.GetValueOrDefault(item.Slot))
            })
            .ToList();
        var total = weighted.Sum(item => item.Weight);
        var roll = random.Next(total);
        foreach (var item in weighted)
        {
            if ((roll -= item.Weight) < 0)
            {
                return (item.Slot, item.Position);
            }
        }

        return ("CM", Position.Midfielder);
    }

    private static int GetScoutFocusWeight(string slot, YouthScoutFocus focus)
    {
        return focus switch
        {
            YouthScoutFocus.Goalkeeper when slot == "GK" => 22,
            YouthScoutFocus.Defender when slot is "CB" or "LB" or "RB" => 12,
            YouthScoutFocus.Midfielder when slot is "CDM" or "CM" or "CAM" => 12,
            YouthScoutFocus.Winger when slot is "LW" or "RW" => 18,
            YouthScoutFocus.Striker when slot == "ST" => 22,
            YouthScoutFocus.Physical when slot is "CB" or "ST" or "CDM" => 8,
            YouthScoutFocus.Technical when slot is "CM" or "CAM" or "LW" or "RW" => 8,
            YouthScoutFocus.Pace when slot is "LB" or "RB" or "LW" or "RW" => 9,
            YouthScoutFocus.Playmaker when slot is "CM" or "CAM" => 14,
            _ => 0
        };
    }

    private static (int Min, int Max) GetPotentialRange(YouthPotentialTier tier, YouthAcademy academy, Random random)
    {
        var baseRange = tier switch
        {
            YouthPotentialTier.CommonProspect => (60, 72),
            YouthPotentialTier.GoodProspect => (73, 80),
            YouthPotentialTier.ExcitingProspect => (81, 86),
            YouthPotentialTier.EliteProspect => (87, 91),
            YouthPotentialTier.GenerationalTalent => (92, 99),
            _ => (70, 78)
        };
        var floorBonus = academy.AcademyLevel is AcademyLevel.Gold or AcademyLevel.Elite && tier >= YouthPotentialTier.ExcitingProspect ? 1 : 0;
        var min = Math.Clamp(baseRange.Item1 + floorBonus + random.Next(0, 2), 60, 99);
        var max = Math.Clamp(baseRange.Item2 + (academy.AcademyLevel == AcademyLevel.Elite ? 1 : 0), min, 99);
        return (min, max);
    }

    private static int GetCurrentOverallBonus(YouthAcademy academy, YouthPotentialTier tier, Random random)
    {
        return academy.AcademyLevel switch
        {
            AcademyLevel.Elite => random.Next(5, 10),
            AcademyLevel.Gold => random.Next(3, 8),
            AcademyLevel.Silver => random.Next(1, 6),
            _ => random.Next(0, 5)
        } + (tier >= YouthPotentialTier.EliteProspect ? 2 : 0);
    }

    private static List<PlayerTrait> PickTraits(string slot, YouthScoutFocus focus, YouthPotentialTier tier, Random random)
    {
        var candidates = slot switch
        {
            "GK" => new[] { PlayerTrait.OneOnOnes, PlayerTrait.RushesOutOfGoal, PlayerTrait.Puncher },
            "CB" => new[] { PlayerTrait.Interceptor, PlayerTrait.AerialThreat, PlayerTrait.DivesIntoTackles },
            "LB" or "RB" => new[] { PlayerTrait.Engine, PlayerTrait.EarlyCrosser, PlayerTrait.Rapid },
            "CDM" => new[] { PlayerTrait.TeamPlayer, PlayerTrait.DivesIntoTackles, PlayerTrait.LongPasser },
            "CM" => new[] { PlayerTrait.BoxToBox, PlayerTrait.Playmaker, PlayerTrait.PressResistant },
            "CAM" => new[] { PlayerTrait.Playmaker, PlayerTrait.Flair, PlayerTrait.TechnicalDribbler },
            "LW" or "RW" => new[] { PlayerTrait.Rapid, PlayerTrait.TechnicalDribbler, PlayerTrait.Flair },
            "ST" => new[] { PlayerTrait.ClinicalFinisher, PlayerTrait.PowerHeader, PlayerTrait.TriesToBeatOffsideTrap },
            _ => Array.Empty<PlayerTrait>()
        };
        var traitChance = tier switch
        {
            YouthPotentialTier.GenerationalTalent => 0.75,
            YouthPotentialTier.EliteProspect => 0.55,
            YouthPotentialTier.ExcitingProspect => 0.38,
            _ => 0.20
        };

        var traits = candidates.Where(_ => random.NextDouble() < traitChance).Take(2).ToList();
        if (focus == YouthScoutFocus.Pace && !traits.Contains(PlayerTrait.Rapid) && random.NextDouble() < 0.45)
        {
            traits.Add(PlayerTrait.Rapid);
        }
        if (focus == YouthScoutFocus.Playmaker && !traits.Contains(PlayerTrait.Playmaker) && random.NextDouble() < 0.45)
        {
            traits.Add(PlayerTrait.Playmaker);
        }

        return traits.Distinct().Take(2).ToList();
    }

    private static YouthDevelopmentRate PickDevelopmentRate(YouthPotentialTier tier, Random random)
    {
        var roll = random.NextDouble();
        return tier switch
        {
            YouthPotentialTier.GenerationalTalent => roll < 0.42 ? YouthDevelopmentRate.Explosive : YouthDevelopmentRate.Fast,
            YouthPotentialTier.EliteProspect => roll < 0.18 ? YouthDevelopmentRate.Explosive : roll < 0.72 ? YouthDevelopmentRate.Fast : YouthDevelopmentRate.Normal,
            YouthPotentialTier.ExcitingProspect => roll < 0.45 ? YouthDevelopmentRate.Fast : YouthDevelopmentRate.Normal,
            YouthPotentialTier.CommonProspect => roll < 0.22 ? YouthDevelopmentRate.Slow : YouthDevelopmentRate.Normal,
            _ => roll < 0.18 ? YouthDevelopmentRate.Fast : YouthDevelopmentRate.Normal
        };
    }

    private static YouthPersonality PickPersonality(Random random)
    {
        var values = Enum.GetValues<YouthPersonality>();
        return values[random.Next(values.Length)];
    }

    private static (string Name, string Code, string FlagPath, string[] FirstNames, string[] LastNames) PickNationality(Team team, Random random)
    {
        if (team.Players.Concat(team.Substitutes).Any(player => player.NationalityName.Equals("England", StringComparison.OrdinalIgnoreCase)) &&
            random.NextDouble() < 0.62)
        {
            return NationalityPools[0];
        }

        return NationalityPools[random.Next(NationalityPools.Length)];
    }

    private static string CreateName((string Name, string Code, string FlagPath, string[] FirstNames, string[] LastNames) nationality, Random random)
    {
        return $"{nationality.FirstNames[random.Next(nationality.FirstNames.Length)]} {nationality.LastNames[random.Next(nationality.LastNames.Length)]}";
    }

    private static List<string> GetSecondaryPositions(string exactPosition)
    {
        return exactPosition switch
        {
            "RB" => ["RWB", "LB"],
            "LB" => ["LWB", "RB"],
            "CB" => ["CDM"],
            "CDM" => ["CM", "CB"],
            "CM" => ["CDM", "CAM"],
            "CAM" => ["CM", "LW", "RW"],
            "RW" => ["LW", "RM", "ST"],
            "LW" => ["RW", "LM", "ST"],
            "ST" => ["CF"],
            _ => []
        };
    }

    private static AcademyLevel GetDefaultAcademyLevel(string clubName)
    {
        if (EliteAcademies.Any(name => clubName.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            return AcademyLevel.Elite;
        }

        return clubName.Contains("United", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("City", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Liverpool", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Tottenham", StringComparison.OrdinalIgnoreCase)
            ? AcademyLevel.Gold
            : AcademyLevel.Silver;
    }

    private static int GetDefaultReputation(string clubName, AcademyLevel level)
    {
        var baseReputation = level switch
        {
            AcademyLevel.Elite => 88,
            AcademyLevel.Gold => 76,
            AcademyLevel.Silver => 58,
            _ => 42
        };

        return Math.Clamp(baseReputation + Math.Abs(clubName.GetHashCode()) % 8, 35, 98);
    }

    private static int GetSeasonStartYear(string season)
    {
        var normalized = season.Replace('/', '-');
        var first = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(first, out var value) ? value : DateTime.Now.Year;
    }

    private static string NormalizeId(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }
}
