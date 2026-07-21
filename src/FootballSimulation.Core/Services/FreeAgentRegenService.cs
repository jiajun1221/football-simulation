using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FreeAgentRegenService
{
    private const string FreeAgentClubId = "free-agents";

    public FreeAgentRegenResult ProcessSeasonRollover(TransferMarketState state, string season)
    {
        ArgumentNullException.ThrowIfNull(state);

        var retiredPlayers = new List<Player>();
        var regens = new List<Player>();
        var existingIds = state.FreeAgents
            .Select(player => player.PlayerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var player in state.FreeAgents.ToList())
        {
            if (!ShouldRetire(player, season))
            {
                continue;
            }

            state.FreeAgents.Remove(player);
            existingIds.Remove(player.PlayerId);
            var regen = CreateRegen(player, season, existingIds);
            existingIds.Add(regen.PlayerId);
            state.FreeAgents.Add(regen);
            retiredPlayers.Add(player);
            regens.Add(regen);
        }

        if (retiredPlayers.Count > 0)
        {
            state.Inbox.Add(new TransferNotification
            {
                Type = TransferNotificationType.Info,
                Message = $"{retiredPlayers.Count} free agent{(retiredPlayers.Count == 1 ? "" : "s")} retired; {regens.Count} young regen{(regens.Count == 1 ? "" : "s")} entered the market.",
                CreatedRound = 0,
                IsRead = false
            });
        }

        return new FreeAgentRegenResult(retiredPlayers, regens);
    }

    public bool ShouldRetire(Player player, string season)
    {
        var age = player.Age ?? 0;
        if (age < 34)
        {
            return false;
        }

        if (age >= 40)
        {
            return true;
        }

        var retirementChance = age switch
        {
            34 => 0.15,
            35 => 0.28,
            36 => 0.45,
            37 => 0.62,
            38 => 0.78,
            _ => 0.90
        };

        return CreateRandom(player, season, "retire").NextDouble() < retirementChance;
    }

    private static Player CreateRegen(Player retiredPlayer, string season, ISet<string> existingIds)
    {
        var random = CreateRandom(retiredPlayer, season, "regen");
        var potential = GetRegenPotential(retiredPlayer, random);
        var maxCurrentOverall = Math.Min(69, potential - 6);
        var minCurrentOverall = Math.Min(55, maxCurrentOverall);
        var currentOverall = random.Next(minCurrentOverall, maxCurrentOverall + 1);
        var age = random.Next(16, 20);
        var preferredPosition = string.IsNullOrWhiteSpace(retiredPlayer.PreferredPosition)
            ? GetDefaultPositionCode(retiredPlayer.Position)
            : retiredPlayer.PreferredPosition.Trim();
        var retiredTraits = retiredPlayer.Traits ?? [];
        var traits = retiredTraits
            .OrderBy(_ => random.Next())
            .Take(random.Next(0, Math.Min(2, retiredTraits.Count) + 1))
            .ToList();
        var secondaryPositions = retiredPlayer.SecondaryPositions ?? [];

        var player = new Player
        {
            PlayerId = CreateUniqueRegenId(retiredPlayer, season, existingIds),
            Name = CreateRegenName(retiredPlayer, random),
            Position = retiredPlayer.Position,
            PreferredPosition = preferredPosition,
            AssignedPosition = preferredPosition,
            SecondaryPositions = secondaryPositions.Count > 0
                ? secondaryPositions.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : GetDefaultSecondaryPositions(preferredPosition),
            Nationality = retiredPlayer.Nationality,
            NationalityCode = retiredPlayer.NationalityCode,
            NationalityName = retiredPlayer.NationalityName,
            FlagEmoji = retiredPlayer.FlagEmoji,
            FlagImagePath = retiredPlayer.FlagImagePath,
            OverallRating = currentOverall,
            BaseOverallRating = currentOverall,
            Age = age,
            PotentialOverall = potential,
            Role = PlayerRole.Prospect,
            Form = "Average",
            CurrentForm = 50,
            Morale = 55,
            Stamina = 88,
            Traits = traits,
            PreferredFoot = string.IsNullOrWhiteSpace(retiredPlayer.PreferredFoot)
                ? (preferredPosition is "LB" or "LW" ? "Left" : "Right")
                : retiredPlayer.PreferredFoot,
            ContractEndYear = GetSeasonEndYear(season) - 1,
            ContractStatus = PlayerContractStatus.FreeAgent,
            TransferStatus = PlayerTransferStatus.None,
            ClubId = FreeAgentClubId,
            PreviousClubId = string.Empty,
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

        return player;
    }

    private static int GetRegenPotential(Player retiredPlayer, Random random)
    {
        var sourceCeiling = Math.Max(
            retiredPlayer.PotentialOverall ?? 0,
            Math.Max(retiredPlayer.OverallRating, retiredPlayer.BaseOverallRating));
        var variedPotential = sourceCeiling + random.Next(-2, 5);
        return Math.Clamp(Math.Max(80, variedPotential), 80, 96);
    }

    private static string CreateUniqueRegenId(Player retiredPlayer, string season, ISet<string> existingIds)
    {
        var sourceKey = string.IsNullOrWhiteSpace(retiredPlayer.PlayerId)
            ? retiredPlayer.Name
            : retiredPlayer.PlayerId;
        var baseId = $"regen-free-agent-{NormalizeId(season)}-{NormalizeId(sourceKey)}-{StableHash($"{sourceKey}|{season}") & 0xfffffff:x7}";
        var candidate = baseId;
        var suffix = 2;
        while (existingIds.Contains(candidate))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        return candidate;
    }

    private static string CreateRegenName(Player retiredPlayer, Random random)
    {
        var parts = retiredPlayer.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            var firstNames = GetFirstNamePool(retiredPlayer.NationalityName, retiredPlayer.Nationality);
            return $"{firstNames[random.Next(firstNames.Length)]} {parts[^1]}";
        }

        return $"{GetFallbackFirstName(random)} {GetFallbackLastName(random)}";
    }

    private static string[] GetFirstNamePool(string nationalityName, string nationality)
    {
        var normalized = string.IsNullOrWhiteSpace(nationalityName) ? nationality : nationalityName;
        return normalized switch
        {
            var value when value.Contains("England", StringComparison.OrdinalIgnoreCase) => ["Alfie", "Archie", "Ethan", "Leo", "Oscar", "Theo"],
            var value when value.Contains("Spain", StringComparison.OrdinalIgnoreCase) => ["Diego", "Hugo", "Iker", "Mateo", "Nico", "Pablo"],
            var value when value.Contains("France", StringComparison.OrdinalIgnoreCase) => ["Enzo", "Hugo", "Lucas", "Mathis", "Noah", "Theo"],
            var value when value.Contains("Germany", StringComparison.OrdinalIgnoreCase) => ["Ben", "Emil", "Finn", "Jonas", "Leon", "Lukas"],
            var value when value.Contains("Brazil", StringComparison.OrdinalIgnoreCase) => ["Bruno", "Caio", "Felipe", "Joao", "Lucas", "Rafael"],
            var value when value.Contains("Netherlands", StringComparison.OrdinalIgnoreCase) => ["Daan", "Finn", "Jens", "Lars", "Milan", "Sem"],
            _ => ["Alex", "Daniel", "Elias", "Luca", "Milan", "Noah"]
        };
    }

    private static string GetFallbackFirstName(Random random)
    {
        string[] names = ["Alex", "Daniel", "Elias", "Luca", "Milan", "Noah"];
        return names[random.Next(names.Length)];
    }

    private static string GetFallbackLastName(Random random)
    {
        string[] names = ["Bennett", "Costa", "Fischer", "Garcia", "Moreau", "Silva"];
        return names[random.Next(names.Length)];
    }

    private static string GetDefaultPositionCode(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "CB",
            Position.Midfielder => "CM",
            Position.Forward => "ST",
            _ => "CM"
        };
    }

    private static List<string> GetDefaultSecondaryPositions(string exactPosition)
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

    private static Random CreateRandom(Player player, string season, string salt)
    {
        var key = $"{salt}|{season}|{player.PlayerId}|{player.Name}|{player.Age}|{player.PreferredPosition}";
        return new Random(unchecked((int)(StableHash(key) & 0x7fffffff)));
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
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    private static int GetSeasonEndYear(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return PlayerContractService.DefaultSeasonEndYear;
        }

        var normalized = season.Trim().Replace('/', '-');
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var startYear))
        {
            if (int.TryParse(parts[1], out var endPart))
            {
                return endPart < 100 ? (startYear / 100) * 100 + endPart : endPart;
            }
        }

        return int.TryParse(normalized, out var year) ? year : PlayerContractService.DefaultSeasonEndYear;
    }
}

public sealed record FreeAgentRegenResult(
    IReadOnlyList<Player> RetiredPlayers,
    IReadOnlyList<Player> Regens);
