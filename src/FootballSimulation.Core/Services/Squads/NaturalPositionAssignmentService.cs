using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed record NaturalPositionAssignment(Player Player, string Slot, int CompatibilityScore, int OverallPenalty)
{
    public bool IsEmergency => CompatibilityScore <= PositionCompatibilityService.Emergency;
}

public sealed record NaturalPositionAssignmentResult(
    bool Success,
    IReadOnlyList<NaturalPositionAssignment> Assignments,
    int TotalScore,
    int EmergencyAssignments,
    IReadOnlyList<string> Warnings);

public static class NaturalPositionAssignmentService
{
    private const int CandidateLimitPerSlot = 12;
    private const int MaximumSearchNodes = 50_000;

    public static NaturalPositionAssignmentResult Assign(
        IReadOnlyList<Player> players,
        IReadOnlyList<string> slots,
        bool allowEmergencyAssignments = true,
        Func<Player, string, int>? candidateScoreAdjustment = null)
    {
        var normalizedSlots = slots
            .Select(PositionSuitabilityService.NormalizeExactPosition)
            .ToList();
        if (normalizedSlots.Count == 0)
        {
            return Failed("No formation slots are available.");
        }

        var invalidSlotIndex = normalizedSlots.FindIndex(string.IsNullOrWhiteSpace);
        if (invalidSlotIndex >= 0)
        {
            return Failed($"Unknown formation slot {slots[invalidSlotIndex]}.");
        }

        var candidatesBySlot = normalizedSlots
            .Select((slot, index) => new SlotCandidateSet(index, slot, CreateCandidates(players, slot, allowEmergencyAssignments, candidateScoreAdjustment)))
            .ToList();
        if (candidatesBySlot.Any(set => set.Candidates.Count == 0))
        {
            var failedSlot = candidatesBySlot.First(set => set.Candidates.Count == 0).Slot;
            return Failed($"No valid player can cover {failedSlot}.");
        }

        var searchOrder = candidatesBySlot
            .OrderBy(set => set.Candidates.Count)
            .ThenBy(set => GetSlotPriority(set.Slot))
            .ToList();
        var selected = new Dictionary<int, AssignmentCandidate>();
        var usedPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var best = new SearchState([], int.MinValue);
        var searchNodes = 0;
        Search(searchOrder, selected, usedPlayerIds, depth: 0, score: 0, ref best, ref searchNodes);

        if (best.Assignments.Count != normalizedSlots.Count)
        {
            var fallback = CreateGreedyFallback(searchOrder);
            if (fallback.Count != normalizedSlots.Count)
            {
                return Failed("Could not build a realistic role assignment.");
            }

            best = new SearchState(fallback, fallback.Sum(item => item.Score));
        }

        var orderedAssignments = best.Assignments
            .OrderBy(item => item.SlotIndex)
            .Select(item => new NaturalPositionAssignment(
                item.Player,
                normalizedSlots[item.SlotIndex],
                item.Score,
                PositionSuitabilityService.GetOutOfPositionPenalty(item.Player, normalizedSlots[item.SlotIndex])))
            .ToList();
        var warnings = orderedAssignments
            .Where(item => item.IsEmergency)
            .Select(item => $"{item.Player.Name} is an emergency option at {item.Slot}.")
            .ToList();

        return new NaturalPositionAssignmentResult(
            true,
            orderedAssignments,
            best.Score,
            orderedAssignments.Count(item => item.IsEmergency),
            warnings);
    }

    private static List<AssignmentCandidate> CreateCandidates(
        IReadOnlyList<Player> players,
        string slot,
        bool allowEmergencyAssignments,
        Func<Player, string, int>? candidateScoreAdjustment)
    {
        var candidates = players
            .Select(player =>
            {
                var compatibility = PositionCompatibilityService.GetCompatibilityScore(player, slot);
                return new AssignmentCandidate(player, SlotIndex: -1, compatibility, CalculateCandidateScore(player, slot, compatibility, candidateScoreAdjustment));
            })
            .Where(candidate => candidate.Compatibility > PositionCompatibilityService.Impossible)
            .Where(candidate => allowEmergencyAssignments || candidate.Compatibility > PositionCompatibilityService.Emergency)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Player.OverallRating)
            .ThenBy(candidate => candidate.Player.SquadNumber <= 0 ? int.MaxValue : candidate.Player.SquadNumber)
            .Take(CandidateLimitPerSlot)
            .ToList();

        if (candidates.Count > 0 || !allowEmergencyAssignments)
        {
            return candidates;
        }

        return CreateLastResortCandidates(players, slot, candidateScoreAdjustment);
    }

    private static List<AssignmentCandidate> CreateLastResortCandidates(
        IReadOnlyList<Player> players,
        string slot,
        Func<Player, string, int>? candidateScoreAdjustment)
    {
        if (slot == "GK")
        {
            return players
                .Where(PositionSuitabilityService.IsGoalkeeperCapable)
                .Select(player => new AssignmentCandidate(player, -1, PositionCompatibilityService.Emergency, CalculateCandidateScore(player, slot, PositionCompatibilityService.Emergency, candidateScoreAdjustment)))
                .ToList();
        }

        return players
            .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
            .Where(player => !IsAbsurdOutfieldAssignment(player, slot))
            .Select(player => new AssignmentCandidate(player, -1, PositionCompatibilityService.Emergency, CalculateCandidateScore(player, slot, PositionCompatibilityService.Emergency, candidateScoreAdjustment)))
            .OrderByDescending(candidate => candidate.Score)
            .Take(CandidateLimitPerSlot)
            .ToList();
    }

    private static bool IsAbsurdOutfieldAssignment(Player player, string slot)
    {
        var primary = PositionSuitabilityService.GetNaturalExactPositions(player).FirstOrDefault() ?? string.Empty;
        return slot switch
        {
            "CB" => primary is "ST" or "CF" or "LW" or "RW" or "CAM",
            "ST" or "CF" => primary is "CB" or "LB" or "RB" or "LWB" or "RWB" or "CDM",
            "CDM" => primary is "ST" or "CF" or "LW" or "RW",
            _ => false
        };
    }

    private static int CalculateCandidateScore(
        Player player,
        string slot,
        int compatibility,
        Func<Player, string, int>? candidateScoreAdjustment)
    {
        if (compatibility <= PositionCompatibilityService.Impossible)
        {
            return int.MinValue;
        }

        var naturalRankBonus = GetNaturalRankBonus(player, slot);
        var penalty = PositionSuitabilityService.GetOutOfPositionPenalty(player, slot);
        var adjustment = candidateScoreAdjustment?.Invoke(player, slot) ?? 0;
        return compatibility * 100 + PlayerOverallCalculator.CalculateOverall(player) * 3 + naturalRankBonus - penalty * 30 + adjustment;
    }

    private static int GetNaturalRankBonus(Player player, string slot)
    {
        var naturalPositions = PositionSuitabilityService.GetNaturalExactPositions(player);
        if (naturalPositions.Count > 0 &&
            naturalPositions[0].Equals(slot, StringComparison.OrdinalIgnoreCase))
        {
            return 500;
        }

        return naturalPositions.Skip(1).Contains(slot, StringComparer.OrdinalIgnoreCase) ? 250 : 0;
    }

    private static int GetSlotPriority(string slot)
    {
        return slot switch
        {
            "GK" => 0,
            "CB" => 1,
            "LB" or "RB" or "LWB" or "RWB" => 2,
            "CDM" or "CM" or "CAM" => 3,
            "LW" or "RW" or "LM" or "RM" => 4,
            "ST" or "CF" => 5,
            _ => 6
        };
    }

    private static void Search(
        IReadOnlyList<SlotCandidateSet> searchOrder,
        Dictionary<int, AssignmentCandidate> selected,
        HashSet<string> usedPlayerIds,
        int depth,
        int score,
        ref SearchState best,
        ref int searchNodes)
    {
        if (++searchNodes > MaximumSearchNodes)
        {
            return;
        }

        if (depth >= searchOrder.Count)
        {
            if (score > best.Score)
            {
                best = new SearchState(selected.Select(pair => pair.Value with { SlotIndex = pair.Key }).ToList(), score);
            }

            return;
        }

        var set = searchOrder[depth];
        foreach (var candidate in set.Candidates)
        {
            var playerKey = CreatePlayerKey(candidate.Player);
            if (!usedPlayerIds.Add(playerKey))
            {
                continue;
            }

            selected[set.SlotIndex] = candidate;
            Search(searchOrder, selected, usedPlayerIds, depth + 1, score + candidate.Score, ref best, ref searchNodes);
            selected.Remove(set.SlotIndex);
            usedPlayerIds.Remove(playerKey);
        }
    }

    private static List<AssignmentCandidate> CreateGreedyFallback(IReadOnlyList<SlotCandidateSet> searchOrder)
    {
        var selected = new List<AssignmentCandidate>();
        var usedPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in searchOrder)
        {
            var candidate = set.Candidates
                .Where(candidate => !usedPlayerIds.Contains(CreatePlayerKey(candidate.Player)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Player.OverallRating)
                .FirstOrDefault();
            if (candidate is null)
            {
                return [];
            }

            usedPlayerIds.Add(CreatePlayerKey(candidate.Player));
            selected.Add(candidate with { SlotIndex = set.SlotIndex });
        }

        return selected;
    }

    private static string CreatePlayerKey(Player player)
    {
        return !string.IsNullOrWhiteSpace(player.PlayerId)
            ? player.PlayerId
            : $"{player.Name}|{player.SquadNumber}";
    }

    private static NaturalPositionAssignmentResult Failed(string warning)
    {
        return new NaturalPositionAssignmentResult(false, [], 0, int.MaxValue, [warning]);
    }

    private sealed record SlotCandidateSet(int SlotIndex, string Slot, List<AssignmentCandidate> Candidates);

    private sealed record AssignmentCandidate(Player Player, int SlotIndex, int Compatibility, int Score);

    private sealed record SearchState(List<AssignmentCandidate> Assignments, int Score);
}
