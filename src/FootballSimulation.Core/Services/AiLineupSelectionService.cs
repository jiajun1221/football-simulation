using System.Diagnostics;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class AiLineupSelectionService
{
    public static void BuildRealisticLineup(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        team.Formation = SelectPreferredFormation(team);
        var slots = FormationSlotService.GetSlots(team.Formation);
        var allPlayers = team.Players
            .Concat(team.Substitutes)
            .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var availablePlayers = allPlayers.Where(IsAvailableForSelection).ToList();
        var assignment = NaturalPositionAssignmentService.Assign(
            availablePlayers,
            slots,
            allowEmergencyAssignments: true,
            candidateScoreAdjustment: CalculateFatigueRotationScoreAdjustment);
        if (!assignment.Success)
        {
            Debug.WriteLine($"[AiLineup] {team.Name}: {string.Join("; ", assignment.Warnings)}");
            return;
        }

        var selectedPlayers = assignment.Assignments
            .Select(item =>
            {
                item.Player.IsStarter = true;
                item.Player.IsOnPitch = true;
                PositionSuitabilityService.EnsurePositionMetadata(item.Player, item.Slot);
                return item.Player;
            })
            .ToList();
        var usedPlayerIds = selectedPlayers
            .Select(CreatePlayerKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var player in allPlayers.Where(player => !usedPlayerIds.Contains(CreatePlayerKey(player))))
        {
            player.IsStarter = false;
            player.IsOnPitch = false;
        }

        team.Players = selectedPlayers;
        team.Substitutes = allPlayers
            .Where(player => !usedPlayerIds.Contains(CreatePlayerKey(player)))
            .OrderByDescending(IsAvailableForSelection)
            .ThenByDescending(GetFreshnessScore)
            .ThenByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ToList();
    }

    private static bool IsAvailableForSelection(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }

    private static int CalculateFatigueRotationScoreAdjustment(Player player, string slot)
    {
        var staminaPenalty = player.Stamina switch
        {
            < 45 => 520,
            < 55 => 380,
            < 70 => (int)Math.Round((70 - player.Stamina) * 10),
            _ => 0
        };
        var seasonFatiguePenalty = player.SeasonFatigue switch
        {
            >= 85 => 360,
            >= 70 => 240,
            >= 55 => 130,
            >= 40 => 60,
            _ => 0
        };
        var loadPenalty = Math.Min(7, player.MatchesPlayedRecently) * 35;
        var consecutiveStartPenalty = player.ConsecutiveStarts >= 8
            ? 220
            : player.ConsecutiveStarts >= 5
                ? 110
                : 0;

        return -(staminaPenalty + seasonFatiguePenalty + loadPenalty + consecutiveStartPenalty);
    }

    private static double GetFreshnessScore(Player player)
    {
        return player.Stamina - player.SeasonFatigue * 0.35 - player.MatchesPlayedRecently * 3.0;
    }

    private static string CreatePlayerKey(Player player)
    {
        return !string.IsNullOrWhiteSpace(player.PlayerId)
            ? player.PlayerId
            : $"{player.Name}|{player.SquadNumber}";
    }

    public static string SelectPreferredFormation(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        if (team.Tactics.Mentality == Mentality.AllOutAttack)
        {
            return PickByTeamName(team, "4-2-4", "3-4-3");
        }

        if (team.Tactics.Mentality is Mentality.Defensive or Mentality.UltraDefensive)
        {
            return PickByTeamName(team, "5-4-1", "5-3-2");
        }

        var averageOverall = team.Players
            .Concat(team.Substitutes)
            .DefaultIfEmpty()
            .Average(player => player?.OverallRating ?? 72);
        if (averageOverall >= 83)
        {
            return PickByTeamName(team, "4-3-3 Holding", "4-2-3-1 Wide", "4-3-3 Attack");
        }

        if (averageOverall <= 74 || team.Tactics.Tempo >= 70 && team.Tactics.DefensiveLine <= 45)
        {
            return PickByTeamName(team, "4-4-2", "5-3-2", "5-4-1");
        }

        return FormationCatalogService.NormalizeFormationName(team.Formation);
    }

    private static string PickByTeamName(Team team, params string[] formations)
    {
        return formations[(team.Name.GetHashCode() & int.MaxValue) % formations.Length];
    }
}
