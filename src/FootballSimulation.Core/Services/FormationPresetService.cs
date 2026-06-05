using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FormationPresetService
{
    public const int MaxPresets = 5;
    public const int MaxPresetNameLength = 24;
    public static readonly IReadOnlyList<string> SuggestedPresetNames = ["Default", "Attacking", "Defensive", "Counter Attack", "Custom"];

    public FormationPresetOperationResult SaveNewPreset(Team team, string name)
    {
        ArgumentNullException.ThrowIfNull(team);
        if (team.FormationPresets.Count >= MaxPresets)
        {
            return FormationPresetOperationResult.Failed("You can save up to 5 formation presets. Overwrite or delete one first.");
        }

        var validation = ValidatePresetName(name);
        if (!validation.Success)
        {
            return validation;
        }

        var preset = Capture(team, validation.Preset!.Name);
        team.FormationPresets.Add(preset);
        return FormationPresetOperationResult.Succeeded($"Saved {preset.Name}.", preset);
    }

    public FormationPresetOperationResult OverwritePreset(Team team, string presetId)
    {
        ArgumentNullException.ThrowIfNull(team);
        var preset = FindPreset(team, presetId);
        if (preset is null)
        {
            return FormationPresetOperationResult.Failed("The selected preset could not be found.");
        }

        var updatedPreset = Capture(team, preset.Name, preset.Id);
        preset.Formation = updatedPreset.Formation;
        preset.StartingXI = updatedPreset.StartingXI;
        preset.Bench = updatedPreset.Bench;
        preset.Tactics = updatedPreset.Tactics;
        preset.UpdatedAt = DateTime.Now;
        return FormationPresetOperationResult.Succeeded($"Updated {preset.Name}.", preset);
    }

    public FormationPresetOperationResult RenamePreset(Team team, string presetId, string name)
    {
        ArgumentNullException.ThrowIfNull(team);
        var preset = FindPreset(team, presetId);
        if (preset is null)
        {
            return FormationPresetOperationResult.Failed("The selected preset could not be found.");
        }

        var validation = ValidatePresetName(name);
        if (!validation.Success)
        {
            return validation;
        }

        preset.Name = validation.Preset!.Name;
        preset.UpdatedAt = DateTime.Now;
        return FormationPresetOperationResult.Succeeded($"Renamed preset to {preset.Name}.", preset);
    }

    public FormationPresetOperationResult DeletePreset(Team team, string presetId)
    {
        ArgumentNullException.ThrowIfNull(team);
        var preset = FindPreset(team, presetId);
        if (preset is null)
        {
            return FormationPresetOperationResult.Failed("The selected preset could not be found.");
        }

        team.FormationPresets.Remove(preset);
        return FormationPresetOperationResult.Succeeded($"Deleted {preset.Name}.");
    }

    public FormationPresetApplyResult ApplyPreset(Team team, FormationPreset preset)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(preset);

        if (preset.StartingXI.Count == 0)
        {
            return FormationPresetApplyResult.Failed("This preset does not contain a starting XI.");
        }

        var allPlayers = team.Players.Concat(team.Substitutes)
            .Distinct()
            .ToList();
        var playersById = allPlayers
            .Where(player => !string.IsNullOrWhiteSpace(player.PlayerId))
            .GroupBy(player => player.PlayerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var playersByName = allPlayers
            .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var usedPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var starters = new List<Player>();
        var warnings = new List<string>();

        team.Formation = string.IsNullOrWhiteSpace(preset.Formation)
            ? FormationCatalogService.NormalizeFormationName(team.Formation)
            : FormationCatalogService.NormalizeFormationName(preset.Formation);
        TacticalProfileService.CopyTo(preset.Tactics, team.Tactics);

        foreach (var slot in preset.StartingXI)
        {
            var player = ResolvePlayer(slot.PlayerId, slot.PlayerName, playersById, playersByName);
            if (player is null)
            {
                warnings.Add($"{slot.PlayerName} is no longer at the club.");
                player = ChooseReplacement(allPlayers, usedPlayerIds, slot.Slot);
            }
            else if (!IsAvailableForSelection(player))
            {
                warnings.Add($"{player.Name} is unavailable and was replaced.");
                player = ChooseReplacement(allPlayers, usedPlayerIds, slot.Slot);
            }
            else if (!TryMarkUsed(player, usedPlayerIds))
            {
                warnings.Add($"{player.Name} was duplicated in the preset.");
                player = ChooseReplacement(allPlayers, usedPlayerIds, slot.Slot);
            }

            if (player is null)
            {
                continue;
            }

            ApplyStarterSlot(player, slot.Slot);
            starters.Add(player);
        }

        var formationSlots = FormationSlotService.GetSlots(team.Formation);
        while (starters.Count < 11)
        {
            var slot = starters.Count < formationSlots.Count ? formationSlots[starters.Count] : string.Empty;
            var replacement = ChooseReplacement(allPlayers, usedPlayerIds, slot);
            if (replacement is null)
            {
                break;
            }

            ApplyStarterSlot(replacement, slot);
            starters.Add(replacement);
        }

        if (starters.Count < 11)
        {
            return FormationPresetApplyResult.Failed("Not enough available players to load this preset.", warnings);
        }

        var bench = new List<Player>();
        foreach (var benchRef in preset.Bench)
        {
            var player = ResolvePlayer(benchRef.PlayerId, benchRef.PlayerName, playersById, playersByName);
            if (player is null ||
                !IsAvailableForSelection(player) ||
                IsUsed(player, usedPlayerIds) ||
                bench.Contains(player))
            {
                continue;
            }

            ApplyBenchStatus(player);
            bench.Add(player);
            _ = TryMarkUsed(player, usedPlayerIds);
        }

        foreach (var player in allPlayers)
        {
            if (IsUsed(player, usedPlayerIds) || bench.Contains(player))
            {
                continue;
            }

            ApplyBenchStatus(player);
            bench.Add(player);
        }

        team.Players = starters;
        team.Substitutes = bench;
        var goalkeeperResult = LineupValidationService.RepairGoalkeeperSlot(team);
        if (!goalkeeperResult.IsValid)
        {
            return FormationPresetApplyResult.Failed(goalkeeperResult.Message ?? LineupValidationService.NoAvailableGoalkeeperMessage, warnings);
        }

        return FormationPresetApplyResult.Succeeded(warnings);
    }

    public FormationPreset Capture(Team team, string name, string? presetId = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        var slots = FormationSlotService.GetSlots(team.Formation);
        return new FormationPreset
        {
            Id = string.IsNullOrWhiteSpace(presetId) ? Guid.NewGuid().ToString("N") : presetId,
            Name = NormalizePresetName(name),
            Formation = FormationCatalogService.NormalizeFormationName(team.Formation),
            StartingXI = team.Players
                .Take(11)
                .Select((player, index) => new FormationPresetSlot
                {
                    Slot = index < slots.Count ? slots[index] : PositionSuitabilityService.NormalizeExactPosition(player.AssignedPosition),
                    PlayerId = player.PlayerId,
                    PlayerName = player.Name
                })
                .ToList(),
            Bench = team.Substitutes
                .Select(player => new FormationPresetPlayerRef
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.Name
                })
                .ToList(),
            Tactics = TacticalProfileService.Clone(team.Tactics),
            UpdatedAt = DateTime.Now
        };
    }

    public FormationPreset? FindPreset(Team team, string presetId)
    {
        ArgumentNullException.ThrowIfNull(team);
        return team.FormationPresets.FirstOrDefault(preset =>
            preset.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
    }

    private static FormationPresetOperationResult ValidatePresetName(string name)
    {
        var normalizedName = NormalizePresetName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return FormationPresetOperationResult.Failed("Preset name cannot be empty.");
        }

        return FormationPresetOperationResult.Succeeded(string.Empty, new FormationPreset { Name = normalizedName });
    }

    private static string NormalizePresetName(string name)
    {
        var normalizedName = string.Join(' ', (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalizedName.Length <= MaxPresetNameLength
            ? normalizedName
            : normalizedName[..MaxPresetNameLength].Trim();
    }

    private static Player? ResolvePlayer(
        string playerId,
        string playerName,
        IReadOnlyDictionary<string, Player> playersById,
        IReadOnlyDictionary<string, Player> playersByName)
    {
        if (!string.IsNullOrWhiteSpace(playerId) && playersById.TryGetValue(playerId, out var byId))
        {
            return byId;
        }

        return !string.IsNullOrWhiteSpace(playerName) && playersByName.TryGetValue(playerName, out var byName)
            ? byName
            : null;
    }

    private static Player? ChooseReplacement(IEnumerable<Player> players, ISet<string> usedPlayerIds, string slot)
    {
        var normalizedSlot = PositionSuitabilityService.NormalizeExactPosition(slot);
        return players
            .Where(IsAvailableForSelection)
            .Where(player => !IsUsed(player, usedPlayerIds))
            .Where(player => string.IsNullOrWhiteSpace(normalizedSlot) || PositionCompatibilityService.IsReasonableFit(player, normalizedSlot))
            .OrderByDescending(player => string.IsNullOrWhiteSpace(normalizedSlot)
                ? player.OverallRating
                : PositionCompatibilityService.GetCompatibilityScore(player, normalizedSlot))
            .ThenByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .FirstOrDefault(player => TryMarkUsed(player, usedPlayerIds));
    }

    private static bool TryMarkUsed(Player player, ISet<string> usedPlayerIds)
    {
        var key = GetPlayerKey(player);
        return !string.IsNullOrWhiteSpace(key) && usedPlayerIds.Add(key);
    }

    private static bool IsUsed(Player player, ISet<string> usedPlayerIds)
    {
        var key = GetPlayerKey(player);
        return !string.IsNullOrWhiteSpace(key) && usedPlayerIds.Contains(key);
    }

    private static string GetPlayerKey(Player player)
    {
        return string.IsNullOrWhiteSpace(player.PlayerId) ? player.Name : player.PlayerId;
    }

    private static void ApplyStarterSlot(Player player, string slot)
    {
        player.IsStarter = true;
        player.IsOnPitch = true;
        PositionSuitabilityService.EnsurePositionMetadata(player, slot);
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
}
