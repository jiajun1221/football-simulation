using System.Diagnostics;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class AiLineupSelectionService
{
    public static void BuildRealisticLineup(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var slots = FormationSlotService.GetSlots(team.Formation);
        var allPlayers = team.Players
            .Concat(team.Substitutes)
            .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var availablePlayers = allPlayers.Where(IsAvailableForSelection).ToList();
        var selectedPlayers = new List<Player>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentSlotByPlayerName = team.Players
            .Select((player, index) => new
            {
                player.Name,
                Slot = index < slots.Count ? slots[index] : string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Slot))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Slot, StringComparer.OrdinalIgnoreCase);

        foreach (var slot in slots)
        {
            var selectedPlayer = ChooseForSlot(availablePlayers, usedNames, currentSlotByPlayerName, slot, allowEmergency: false) ??
                ChooseForSlot(availablePlayers, usedNames, currentSlotByPlayerName, slot, allowEmergency: true) ??
                ChooseFallbackForSlot(availablePlayers, usedNames, slot);
            if (selectedPlayer is null)
            {
                Debug.WriteLine($"[AiLineup] No natural {slot} available for {team.Name}.");
                continue;
            }

            usedNames.Add(selectedPlayer.Name);
            selectedPlayer.IsStarter = true;
            selectedPlayer.IsOnPitch = true;
            PositionSuitabilityService.EnsurePositionMetadata(selectedPlayer, slot);
            selectedPlayers.Add(selectedPlayer);
        }

        foreach (var player in allPlayers.Where(player => !usedNames.Contains(player.Name)))
        {
            player.IsStarter = false;
            player.IsOnPitch = false;
        }

        team.Players = selectedPlayers;
        team.Substitutes = allPlayers
            .Where(player => !usedNames.Contains(player.Name))
            .OrderByDescending(IsAvailableForSelection)
            .ThenByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ToList();
    }

    private static Player? ChooseForSlot(
        IEnumerable<Player> players,
        ISet<string> usedNames,
        IReadOnlyDictionary<string, string> currentSlotByPlayerName,
        string slot,
        bool allowEmergency)
    {
        var candidates = players
            .Where(player => !usedNames.Contains(player.Name))
            .Select(player => new
            {
                Player = player,
                Compatibility = PositionCompatibilityService.GetCompatibilityScore(player, slot)
            })
            .Where(candidate => candidate.Compatibility > PositionCompatibilityService.Impossible)
            .Where(candidate => allowEmergency || candidate.Compatibility > PositionCompatibilityService.Emergency)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(candidate => candidate.Compatibility)
            .ThenByDescending(candidate =>
                currentSlotByPlayerName.TryGetValue(candidate.Player.Name, out var currentSlot) &&
                string.Equals(currentSlot, slot, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.Player.OverallRating)
            .ThenBy(candidate => candidate.Player.SquadNumber <= 0 ? int.MaxValue : candidate.Player.SquadNumber)
            .Select(candidate => candidate.Player)
            .FirstOrDefault();
    }

    private static Player? ChooseFallbackForSlot(
        IEnumerable<Player> players,
        ISet<string> usedNames,
        string slot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(slot);
        if (normalizedSlot == "GK")
        {
            return null;
        }

        var fallback = players
            .Where(player => !usedNames.Contains(player.Name))
            .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
            .OrderByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .FirstOrDefault();

        if (fallback is not null)
        {
            Debug.WriteLine($"[AiLineup] Fallback selected {fallback.Name} for {slot}.");
        }

        return fallback;
    }

    private static bool IsAvailableForSelection(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }
}
