using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed record FormationAssignmentResult(
    bool Success,
    string Formation,
    int TotalScore,
    int EmergencyAssignments,
    IReadOnlyList<string> Warnings);

public class InMatchFormationService
{
    private const int MaximumEmergencyAssignments = 2;

    public FormationAssignmentResult ApplyFormation(Team team, string formation)
    {
        ArgumentNullException.ThrowIfNull(team);

        var normalizedFormation = FormationCatalogService.NormalizeFormationName(formation);
        var activePlayers = team.Players
            .Where(player => player.IsOnPitch && !player.IsSentOff)
            .ToList();
        var inactiveStarters = team.Players
            .Where(player => !activePlayers.Contains(player))
            .ToList();
        var assignment = CreateAssignment(activePlayers, normalizedFormation);
        if (!assignment.Success)
        {
            return assignment.ToResult();
        }

        team.Formation = normalizedFormation;
        team.Players = assignment.Assignments
            .Select(item =>
            {
                PositionSuitabilityService.EnsurePositionMetadata(item.Player, item.Slot);
                item.Player.IsStarter = true;
                item.Player.IsOnPitch = !item.Player.IsSentOff;
                return item.Player;
            })
            .Concat(inactiveStarters)
            .ToList();

        return assignment.ToResult();
    }

    public FormationAssignmentResult EvaluateFormation(Team team, string formation)
    {
        ArgumentNullException.ThrowIfNull(team);

        var activePlayers = team.Players
            .Where(player => player.IsOnPitch && !player.IsSentOff)
            .ToList();
        return CreateAssignment(activePlayers, FormationCatalogService.NormalizeFormationName(formation)).ToResult();
    }

    public string? ChooseBestFormation(Team team, IEnumerable<string> candidateFormations)
    {
        return candidateFormations
            .Select(formation => EvaluateFormation(team, formation))
            .Where(result => result.Success)
            .OrderBy(result => result.EmergencyAssignments)
            .ThenByDescending(result => result.TotalScore)
            .Select(result => result.Formation)
            .FirstOrDefault();
    }

    private static FormationAssignment CreateAssignment(IReadOnlyList<Player> activePlayers, string formation)
    {
        if (activePlayers.Count == 0)
        {
            return FormationAssignment.Failed(formation, "No on-field players are available.");
        }

        var slots = SelectSlotsForPlayerCount(FormationSlotService.GetSlots(formation), activePlayers.Count);
        var assignment = NaturalPositionAssignmentService.Assign(activePlayers, slots, allowEmergencyAssignments: true);
        if (!assignment.Success)
        {
            return FormationAssignment.Failed(formation, assignment.Warnings.FirstOrDefault() ?? "Could not build a realistic role assignment.");
        }

        if (assignment.EmergencyAssignments > MaximumEmergencyAssignments)
        {
            return FormationAssignment.Failed(formation, $"Too many emergency role assignments for {formation}.");
        }

        var assignments = assignment.Assignments
            .Select(item => new FormationSlotAssignment(item.Player, item.Slot))
            .ToList();
        return FormationAssignment.Succeeded(
            formation,
            assignments,
            assignment.TotalScore,
            assignment.EmergencyAssignments,
            assignment.Warnings.ToList());
    }

    private static List<string> SelectSlotsForPlayerCount(IReadOnlyList<string> sourceSlots, int playerCount)
    {
        var slots = sourceSlots.ToList();
        while (slots.Count > playerCount)
        {
            var removableIndex = FindLeastEssentialSlotIndex(slots);
            if (removableIndex < 0)
            {
                break;
            }

            slots.RemoveAt(removableIndex);
        }

        return slots;
    }

    private static int FindLeastEssentialSlotIndex(IReadOnlyList<string> slots)
    {
        var attackingSlots = slots.Count(IsAttackingSlot);
        var defensiveSlots = slots.Count(IsDefensiveSlot);
        var removalOrder = new[] { "CAM", "CF", "LW", "RW", "LM", "RM", "CM", "CDM", "LWB", "RWB", "LB", "RB", "ST", "CB" };

        foreach (var slot in removalOrder)
        {
            var index = slots
                .Select((value, itemIndex) => new { Slot = value, Index = itemIndex })
                .LastOrDefault(item =>
                    string.Equals(item.Slot, slot, StringComparison.OrdinalIgnoreCase) &&
                    CanRemoveSlot(item.Slot, attackingSlots, defensiveSlots))
                ?.Index;

            if (index.HasValue)
            {
                return index.Value;
            }
        }

        return -1;
    }

    private static bool CanRemoveSlot(string slot, int attackingSlots, int defensiveSlots)
    {
        if (slot == "GK")
        {
            return false;
        }

        if (IsAttackingSlot(slot))
        {
            return attackingSlots > 1;
        }

        if (IsDefensiveSlot(slot))
        {
            return defensiveSlots > 3;
        }

        return true;
    }

    private static bool IsAttackingSlot(string slot)
    {
        return slot is "ST" or "CF" or "LW" or "RW";
    }

    private static bool IsDefensiveSlot(string slot)
    {
        return slot is "CB" or "LB" or "RB" or "LWB" or "RWB";
    }

    private sealed record FormationSlotAssignment(Player Player, string Slot);

    private sealed class FormationAssignment
    {
        private FormationAssignment(
            bool success,
            string formation,
            List<FormationSlotAssignment> assignments,
            int totalScore,
            int emergencyAssignments,
            List<string> warnings)
        {
            Success = success;
            Formation = formation;
            Assignments = assignments;
            TotalScore = totalScore;
            EmergencyAssignments = emergencyAssignments;
            Warnings = warnings;
        }

        public bool Success { get; }
        public string Formation { get; }
        public List<FormationSlotAssignment> Assignments { get; }
        public int TotalScore { get; }
        public int EmergencyAssignments { get; }
        public List<string> Warnings { get; }

        public static FormationAssignment Succeeded(
            string formation,
            List<FormationSlotAssignment> assignments,
            int totalScore,
            int emergencyAssignments,
            List<string> warnings)
        {
            return new FormationAssignment(true, formation, assignments, totalScore, emergencyAssignments, warnings);
        }

        public static FormationAssignment Failed(string formation, string warning)
        {
            return new FormationAssignment(false, formation, [], 0, int.MaxValue, [warning]);
        }

        public FormationAssignmentResult ToResult()
        {
            return new FormationAssignmentResult(Success, Formation, TotalScore, EmergencyAssignments, Warnings);
        }
    }
}
