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
                    PlayerName = player.Name
                })
                .ToList(),
            Bench = team.Substitutes.Select(player => player.Name).ToList(),
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
            .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var playersByName = originalPlayers.ToDictionary(player => player.Name, StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var starters = new List<Player>();

        foreach (var assignment in setup.StartingXI)
        {
            var player = playersByName.TryGetValue(assignment.PlayerName, out var savedPlayer) &&
                IsAvailableForSelection(savedPlayer)
                    ? savedPlayer
                    : ChooseReplacement(originalPlayers, usedNames, assignment.Slot);
            if (player is null || !usedNames.Add(player.Name))
            {
                continue;
            }

            ApplyStarterSlot(player, assignment.Slot);
            starters.Add(player);
        }

        while (starters.Count < 11)
        {
            var replacement = originalPlayers
                .Where(player => IsAvailableForSelection(player) && !usedNames.Contains(player.Name))
                .OrderByDescending(player => player.OverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .FirstOrDefault();
            if (replacement is null || !usedNames.Add(replacement.Name))
            {
                break;
            }

            replacement.IsStarter = true;
            replacement.IsOnPitch = true;
            starters.Add(replacement);
        }

        var bench = new List<Player>();
        foreach (var playerName in setup.Bench)
        {
            if (!playersByName.TryGetValue(playerName, out var player) ||
                usedNames.Contains(player.Name) ||
                bench.Any(existing => string.Equals(existing.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            ApplyBenchStatus(player);
            bench.Add(player);
        }

        foreach (var player in originalPlayers)
        {
            if (usedNames.Contains(player.Name) ||
                bench.Any(existing => string.Equals(existing.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            ApplyBenchStatus(player);
            bench.Add(player);
        }

        team.Players = starters;
        team.Substitutes = bench;
    }

    private static Player? ChooseReplacement(IEnumerable<Player> players, ISet<string> usedNames, string slot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(slot);
        return players
            .Where(player => IsAvailableForSelection(player) && !usedNames.Contains(player.Name))
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
