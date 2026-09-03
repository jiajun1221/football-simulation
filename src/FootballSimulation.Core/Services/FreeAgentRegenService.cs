using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FreeAgentRegenService
{
    private const string FreeAgentClubId = "free-agents";

    public FreeAgentRegenResult ProcessSeasonRollover(
        TransferMarketState state,
        string season,
        IEnumerable<Team>? activeLeagueTeams = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var retiredPlayers = new List<Player>();
        var regens = new List<Player>();
        var teams = (activeLeagueTeams ?? [])
            .Concat(state.Leagues.SelectMany(league => league.Teams))
            .Distinct()
            .ToList();
        var allExistingPlayers = teams
            .SelectMany(team => team.AllPlayers)
            .Concat(state.FreeAgents)
            .ToList();
        var existingIds = allExistingPlayers
            .Select(player => player.PlayerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedNames = GeneratedPlayerNameService.CreateUsedNameSet(
            allExistingPlayers.Select(player => player.Name));

        foreach (var player in state.FreeAgents.ToList())
        {
            if (!ShouldRetire(player, season))
            {
                continue;
            }

            state.FreeAgents.Remove(player);
            existingIds.Remove(player.PlayerId);
            var regen = CreateRegen(player, season, existingIds, usedNames, "free-agent");
            existingIds.Add(regen.PlayerId);
            state.FreeAgents.Add(regen);
            retiredPlayers.Add(player);
            regens.Add(regen);
        }

        var retiredFreeAgentCount = retiredPlayers.Count;
        var processedTeamPlayerIds = retiredPlayers
            .Select(player => player.PlayerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processedTeamPlayerReferences = new HashSet<Player>();
        foreach (var player in teams.SelectMany(team => team.AllPlayers).ToList())
        {
            if (!processedTeamPlayerReferences.Add(player) ||
                (!string.IsNullOrWhiteSpace(player.PlayerId) && !processedTeamPlayerIds.Add(player.PlayerId)) ||
                !ShouldRetire(player, season))
            {
                continue;
            }

            foreach (var team in teams)
            {
                RemovePlayerFromTeam(team, player);
            }

            retiredPlayers.Add(player);
            existingIds.Remove(player.PlayerId);
            if (!IsStarPlayer(player))
            {
                continue;
            }

            var regen = CreateRegen(player, season, existingIds, usedNames, "star");
            existingIds.Add(regen.PlayerId);
            state.FreeAgents.Add(regen);
            regens.Add(regen);
        }

        if (retiredFreeAgentCount > 0)
        {
            var freeAgentRegenCount = regens.Count(player =>
                player.PlayerId.StartsWith("regen-free-agent-", StringComparison.OrdinalIgnoreCase));
            state.Inbox.Add(new TransferNotification
            {
                Type = TransferNotificationType.Info,
                Message = $"{retiredFreeAgentCount} free agent{(retiredFreeAgentCount == 1 ? "" : "s")} retired; {freeAgentRegenCount} young regen{(freeAgentRegenCount == 1 ? "" : "s")} entered the market.",
                CreatedRound = 0,
                IsRead = false
            });
        }

        var retiredSeniorCount = retiredPlayers.Count - retiredFreeAgentCount;
        if (retiredSeniorCount > 0)
        {
            var starRegenCount = regens.Count(player =>
                player.PlayerId.StartsWith("regen-star-", StringComparison.OrdinalIgnoreCase));
            state.Inbox.Add(new TransferNotification
            {
                Type = TransferNotificationType.Info,
                Message = $"{retiredSeniorCount} senior player{(retiredSeniorCount == 1 ? "" : "s")} retired; {starRegenCount} star regen{(starRegenCount == 1 ? "" : "s")} entered the free-agent market.",
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

    private static Player CreateRegen(
        Player retiredPlayer,
        string season,
        ISet<string> existingIds,
        ISet<string> usedNames,
        string sourceType)
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
            PlayerId = CreateUniqueRegenId(retiredPlayer, season, existingIds, sourceType),
            Name = CreateRegenName(retiredPlayer, random, usedNames),
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
        var minimumPotential = IsStarPlayer(retiredPlayer) ? 88 : 80;
        return Math.Clamp(Math.Max(minimumPotential, variedPotential), minimumPotential, 96);
    }

    private static string CreateUniqueRegenId(
        Player retiredPlayer,
        string season,
        ISet<string> existingIds,
        string sourceType)
    {
        var sourceKey = string.IsNullOrWhiteSpace(retiredPlayer.PlayerId)
            ? retiredPlayer.Name
            : retiredPlayer.PlayerId;
        var baseId = $"regen-{sourceType}-{NormalizeId(season)}-{NormalizeId(sourceKey)}-{StableHash($"{sourceKey}|{season}") & 0xfffffff:x7}";
        var candidate = baseId;
        var suffix = 2;
        while (existingIds.Contains(candidate))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        return candidate;
    }

    private static string CreateRegenName(Player retiredPlayer, Random random, ISet<string> usedNames)
    {
        var parts = retiredPlayer.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nationality = string.IsNullOrWhiteSpace(retiredPlayer.NationalityName)
            ? retiredPlayer.Nationality
            : retiredPlayer.NationalityName;
        return GeneratedPlayerNameService.CreateUniqueName(
            nationality,
            random,
            usedNames,
            parts.Length >= 2 ? parts[^1] : null);
    }

    private static bool IsStarPlayer(Player player)
    {
        var peakRating = Math.Max(
            player.PotentialOverall ?? 0,
            Math.Max(player.OverallRating, player.BaseOverallRating));
        return peakRating >= 85 ||
            (player.Role == PlayerRole.KeyPlayer && peakRating >= 82);
    }

    private static void RemovePlayerFromTeam(Team team, Player retiredPlayer)
    {
        var matchesPlayer = (Player candidate) => ReferenceEquals(candidate, retiredPlayer) ||
            (!string.IsNullOrWhiteSpace(retiredPlayer.PlayerId) &&
                candidate.PlayerId.Equals(retiredPlayer.PlayerId, StringComparison.OrdinalIgnoreCase));
        var removedStarter = team.Players.RemoveAll(player => matchesPlayer(player)) > 0;
        team.Substitutes.RemoveAll(player => matchesPlayer(player));
        team.Reserves.RemoveAll(player => matchesPlayer(player));
        if (!removedStarter)
        {
            return;
        }

        while (team.Players.Count < 11 && team.Substitutes.Count > 0)
        {
            var replacement = team.Substitutes
                .OrderByDescending(player => player.OverallRating)
                .First();
            team.Substitutes.Remove(replacement);
            replacement.IsStarter = true;
            replacement.IsOnPitch = true;
            team.Players.Add(replacement);
        }

        if (retiredPlayer.IsCaptain &&
            !team.AllPlayers.Any(player => player.IsCaptain))
        {
            var newCaptain = team.Players
                .OrderByDescending(player => player.Role == PlayerRole.KeyPlayer)
                .ThenByDescending(player => player.OverallRating)
                .FirstOrDefault();
            if (newCaptain is not null)
            {
                newCaptain.IsCaptain = true;
            }
        }
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
