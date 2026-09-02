using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FutureStarMarketService
{
    public const double DefaultGenerationChance = 0.32;
    private const string FreeAgentClubId = "free-agents";

    private static readonly (string Name, string Code, string FlagPath)[] Nationalities =
    [
        ("England", "GB-ENG", "Assets/Flags/england.png"),
        ("Brazil", "BR", "Assets/Flags/brazil.png"),
        ("Argentina", "AR", "Assets/Flags/argentina.png"),
        ("France", "FR", "Assets/Flags/france.png"),
        ("Spain", "ES", "Assets/Flags/spain.png"),
        ("Germany", "DE", "Assets/Flags/germany.png"),
        ("Portugal", "PT", "Assets/Flags/portugal.png"),
        ("Netherlands", "NL", "Assets/Flags/netherlands.png"),
        ("Belgium", "BE", "Assets/Flags/belgium.png"),
        ("Italy", "IT", "Assets/Flags/italy.png"),
        ("Croatia", "HR", "Assets/Flags/croatia.png"),
        ("Uruguay", "UY", "Assets/Flags/uruguay.png"),
        ("Colombia", "CO", "Assets/Flags/colombia.png"),
        ("Norway", "NO", "Assets/Flags/norway.png"),
        ("Denmark", "DK", "Assets/Flags/denmark.png"),
        ("Japan", "JP", "Assets/Flags/japan.png"),
        ("South Korea", "KR", "Assets/Flags/south-korea.png")
    ];

    private static readonly (string ExactPosition, Position Position)[] Positions =
    [
        ("GK", Position.Goalkeeper),
        ("CB", Position.Defender),
        ("LB", Position.Defender),
        ("RB", Position.Defender),
        ("CDM", Position.Midfielder),
        ("CM", Position.Midfielder),
        ("CAM", Position.Midfielder),
        ("LW", Position.Forward),
        ("RW", Position.Forward),
        ("ST", Position.Forward)
    ];

    public Player? TryGenerateSeasonalFutureStar(
        TransferMarketState state,
        string season,
        double generationChance = DefaultGenerationChance)
    {
        ArgumentNullException.ThrowIfNull(state);

        var seasonKey = NormalizeId(season);
        var id = $"future-star-{seasonKey}-{StableHash($"future-star|{season}") & 0xfffffff:x7}";
        var allPlayers = state.Leagues
            .SelectMany(league => league.Teams)
            .SelectMany(team => team.Players.Concat(team.Substitutes))
            .Concat(state.FreeAgents)
            .ToList();
        if (allPlayers.Any(player => player.PlayerId.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var random = new Random(unchecked((int)(StableHash($"market-future-star|{season}") & 0x7fffffff)));
        if (random.NextDouble() >= Math.Clamp(generationChance, 0, 1))
        {
            return null;
        }

        var nationality = Nationalities[random.Next(Nationalities.Length)];
        var position = Positions[random.Next(Positions.Length)];
        var potential = random.Next(89, 97);
        var overall = Math.Clamp(random.Next(62, 74), 62, potential - 12);
        var usedNames = GeneratedPlayerNameService.CreateUsedNameSet(allPlayers.Select(player => player.Name));
        var traits = PickTraits(position.ExactPosition, random);
        var player = new Player
        {
            PlayerId = id,
            Name = GeneratedPlayerNameService.CreateUniqueName(nationality.Name, random, usedNames),
            Position = position.Position,
            PreferredPosition = position.ExactPosition,
            AssignedPosition = position.ExactPosition,
            SecondaryPositions = GetSecondaryPositions(position.ExactPosition),
            Nationality = nationality.Name,
            NationalityName = nationality.Name,
            NationalityCode = nationality.Code,
            FlagImagePath = nationality.FlagPath,
            OverallRating = overall,
            BaseOverallRating = overall,
            PotentialOverall = potential,
            Age = random.Next(16, 20),
            PreferredFoot = position.ExactPosition is "LB" or "LW" || random.NextDouble() < 0.24 ? "Left" : "Right",
            Role = PlayerRole.Prospect,
            Form = "Average",
            CurrentForm = 50,
            Morale = 55,
            Stamina = 92,
            Traits = traits,
            ContractEndYear = GetSeasonEndYear(season) - 1,
            ContractStatus = PlayerContractStatus.FreeAgent,
            TransferStatus = PlayerTransferStatus.None,
            ClubId = FreeAgentClubId,
            IsStarter = false,
            IsOnPitch = false
        };

        player.WeeklyWage = PlayerContractService.EstimateWeeklyWage(player, FreeAgentClubId);
        var attributes = PlayerAttributeService.DeriveAttributes(
            player.Position,
            player.PreferredPosition,
            player.OverallRating,
            player.Traits,
            (int)Math.Round(player.Stamina));
        player.Pace = attributes.Pace;
        player.Shooting = attributes.Shooting;
        player.Passing = attributes.Passing;
        player.Dribbling = attributes.Dribbling;
        player.Defending = attributes.Defending;
        player.Physical = attributes.Physical;
        YouthAcademyService.RepairSeniorOverallAttributes(player, player.OverallRating);

        state.FreeAgents.Add(player);
        state.Inbox.Add(new TransferNotification
        {
            Type = TransferNotificationType.Info,
            Message = $"Scouts have identified future star {player.Name}, a {player.Age}-year-old {player.PreferredPosition}, in the free-agent market.",
            CreatedRound = 0,
            IsRead = false
        });
        return player;
    }

    private static List<PlayerTrait> PickTraits(string position, Random random)
    {
        var candidates = position switch
        {
            "GK" => new[] { PlayerTrait.OneOnOnes, PlayerTrait.ShotStopper, PlayerTrait.CrossClaimer, PlayerTrait.RushesOutOfGoal },
            "CB" => new[] { PlayerTrait.Interceptor, PlayerTrait.Strong, PlayerTrait.AerialThreat, PlayerTrait.BallWinner },
            "LB" or "RB" => new[] { PlayerTrait.Engine, PlayerTrait.CrossingSpecialist, PlayerTrait.RecoveryPace, PlayerTrait.Rapid },
            "CDM" or "CM" => new[] { PlayerTrait.Playmaker, PlayerTrait.LongPasser, PlayerTrait.BoxToBox, PlayerTrait.PressResistant },
            "CAM" => new[] { PlayerTrait.Playmaker, PlayerTrait.FirstTouch, PlayerTrait.Flair, PlayerTrait.TechnicalDribbler },
            "LW" or "RW" => new[] { PlayerTrait.Rapid, PlayerTrait.Flair, PlayerTrait.CrossingSpecialist, PlayerTrait.TechnicalDribbler },
            _ => new[] { PlayerTrait.ClinicalFinisher, PlayerTrait.Poacher, PlayerTrait.Acrobatics, PlayerTrait.BigMatchPlayer }
        };

        return candidates
            .OrderBy(_ => random.Next())
            .Take(2)
            .ToList();
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

    private static int GetSeasonEndYear(string season)
    {
        var normalized = season.Trim().Replace('/', '-');
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var startYear) && int.TryParse(parts[1], out var endPart))
        {
            return endPart < 100 ? (startYear / 100) * 100 + endPart : endPart;
        }

        return int.TryParse(normalized, out var year) ? year : PlayerContractService.DefaultSeasonEndYear;
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash;
        }
    }

    private static string NormalizeId(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
    }
}
