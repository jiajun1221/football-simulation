using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class ClubMatchSetupService
{
    public static ClubMatchSetup Capture(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        return new ClubMatchSetup
        {
            ClubName = team.Name,
            Formation = team.Formation,
            StartingXI = team.Players
                .Select(player => new LineupSlotAssignment
                {
                    Slot = PositionSuitabilityService.NormalizeExactPosition(player.AssignedPosition),
                    PlayerId = player.PlayerId,
                    PlayerName = player.Name
                })
                .ToList(),
            Bench = team.Substitutes.Select(player => player.Name).ToList(),
            BenchPlayers = team.Substitutes
                .Select(player => new LineupPlayerRef
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.Name
                })
                .ToList(),
            Tactics = CloneTactics(team.Tactics)
        };
    }

    public static void Apply(Team team, ClubMatchSetup? setup)
    {
        ArgumentNullException.ThrowIfNull(team);
        if (setup is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(setup.Formation))
        {
            team.Formation = setup.Formation;
        }

        TacticalProfileService.CopyTo(setup.Tactics, team.Tactics);

        var originalPlayers = team.Players.Concat(team.Substitutes)
            .GroupBy(CreatePlayerKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var playersById = originalPlayers
            .Where(player => !string.IsNullOrWhiteSpace(player.PlayerId))
            .GroupBy(player => player.PlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var playersByName = originalPlayers
            .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var usedPlayerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var starters = new List<Player>();

        foreach (var assignment in setup.StartingXI ?? [])
        {
            var player = ResolvePlayer(assignment.PlayerId, assignment.PlayerName, playersById, playersByName);
            if (player is null ||
                !IsAvailableForSelection(player) ||
                usedPlayerKeys.Contains(CreatePlayerKey(player)))
            {
                player = ChooseReplacement(originalPlayers, usedPlayerKeys, assignment.Slot);
            }

            if (player is null || !usedPlayerKeys.Add(CreatePlayerKey(player)))
            {
                continue;
            }

            ApplyStarterSlot(player, assignment.Slot);
            starters.Add(player);
        }

        while (starters.Count < 11)
        {
            var replacement = originalPlayers
                .Where(player => IsAvailableForSelection(player) && !usedPlayerKeys.Contains(CreatePlayerKey(player)))
                .OrderByDescending(player => player.OverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .FirstOrDefault();
            if (replacement is null || !usedPlayerKeys.Add(CreatePlayerKey(replacement)))
            {
                break;
            }

            replacement.IsStarter = true;
            replacement.IsOnPitch = true;
            starters.Add(replacement);
        }

        var bench = new List<Player>();
        var savedBench = setup.BenchPlayers is { Count: > 0 }
            ? setup.BenchPlayers
            : (setup.Bench ?? []).Select(playerName => new LineupPlayerRef { PlayerName = playerName }).ToList();
        foreach (var playerRef in savedBench)
        {
            var player = ResolvePlayer(playerRef.PlayerId, playerRef.PlayerName, playersById, playersByName);
            if (player is null ||
                usedPlayerKeys.Contains(CreatePlayerKey(player)) ||
                bench.Any(existing => string.Equals(CreatePlayerKey(existing), CreatePlayerKey(player), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            ApplyBenchStatus(player);
            bench.Add(player);
        }

        foreach (var player in originalPlayers)
        {
            if (usedPlayerKeys.Contains(CreatePlayerKey(player)) ||
                bench.Any(existing => string.Equals(CreatePlayerKey(existing), CreatePlayerKey(player), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            ApplyBenchStatus(player);
            bench.Add(player);
        }

        team.Players = starters;
        team.Substitutes = bench;
    }

    private static Player? ResolvePlayer(
        string playerId,
        string playerName,
        IReadOnlyDictionary<string, Player> playersById,
        IReadOnlyDictionary<string, Player> playersByName)
    {
        if (!string.IsNullOrWhiteSpace(playerId) &&
            playersById.TryGetValue(playerId, out var playerById))
        {
            return playerById;
        }

        return !string.IsNullOrWhiteSpace(playerName) &&
            playersByName.TryGetValue(playerName, out var playerByName)
                ? playerByName
                : null;
    }

    private static Player? ChooseReplacement(IEnumerable<Player> players, ISet<string> usedPlayerKeys, string slot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(slot);
        return players
            .Where(player => IsAvailableForSelection(player) && !usedPlayerKeys.Contains(CreatePlayerKey(player)))
            .Where(player => PositionCompatibilityService.IsReasonableFit(player, normalizedSlot))
            .OrderByDescending(player => GetSlotFitScore(player, normalizedSlot))
            .ThenByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .FirstOrDefault();
    }

    private static int GetSlotFitScore(Player player, string slot)
    {
        return PositionCompatibilityService.GetCompatibilityScore(player, slot);
    }

    private static void ApplyStarterSlot(Player player, string slot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(slot);
        player.IsStarter = true;
        player.IsOnPitch = true;
        if (string.IsNullOrWhiteSpace(normalizedSlot))
        {
            PositionSuitabilityService.EnsurePositionMetadata(player);
            return;
        }

        PositionSuitabilityService.EnsurePositionMetadata(player, normalizedSlot);
    }

    private static void ApplyBenchStatus(Player player)
    {
        player.IsStarter = false;
        player.IsOnPitch = false;
    }

    private static bool IsAvailableForSelection(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }

    private static string CreatePlayerKey(Player player)
    {
        return !string.IsNullOrWhiteSpace(player.PlayerId)
            ? player.PlayerId
            : $"{player.Name}|{player.SquadNumber}|{PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition)}";
    }

    private static TeamTactics CloneTactics(TeamTactics source)
    {
        return new TeamTactics
        {
            Mentality = source.Mentality,
            PressingIntensity = source.PressingIntensity,
            Width = source.Width,
            Tempo = source.Tempo,
            DefensiveLine = source.DefensiveLine
        };
    }
}
