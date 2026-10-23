using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed class RelationshipService
{
    public PlayerRelationship GetOrCreate(Team team, Player first, Player second)
    {
        var (firstId, secondId) = Order(GetStableId(first, team), GetStableId(second, team));
        var existing = team.DressingRoom.Relationships.FirstOrDefault(item =>
            item.FirstPlayerId == firstId && item.SecondPlayerId == secondId);
        if (existing is not null) return existing;

        var relationship = new PlayerRelationship
        {
            FirstPlayerId = firstId,
            SecondPlayerId = secondId,
            Friendship = GetCompatibility(first, second),
            Respect = (first.MentalState.Personality.Professionalism + second.MentalState.Personality.Professionalism - 20) * 0.5
        };
        team.DressingRoom.Relationships.Add(relationship);
        return relationship;
    }

    public void ApplySharedMatch(Team team, Match match, int calendarRound)
    {
        var participants = team.Players.Where(player => player.IsOnPitch ||
            match.PlayerPerformances.Any(p => p.TeamName == team.Name && p.PlayerName == player.Name)).ToList();
        for (var i = 0; i < participants.Count; i++)
        for (var j = i + 1; j < participants.Count; j++)
        {
            if (!AreRelevant(participants[i], participants[j])) continue;
            var relationship = GetOrCreate(team, participants[i], participants[j]);
            var firstEffects = PersonalityService.GetArchetypeEffects(participants[i]);
            var secondEffects = PersonalityService.GetArchetypeEffects(participants[j]);
            var growth = (firstEffects.RelationshipGrowth + secondEffects.RelationshipGrowth) / 2.0;
            relationship.MatchesPlayedTogether++;
            relationship.Respect = Math.Clamp(relationship.Respect + 0.15 * growth, -100, 100);
            relationship.Friendship = Math.Clamp(relationship.Friendship + 0.08 * growth, -100, 100);
            relationship.LastUpdatedCalendarRound = calendarRound;
            relationship.RecentChanges.Add(new MentalChangeRecord
            {
                CalendarRound = calendarRound,
                Category = "Relationship",
                Delta = 0.15,
                Reason = "Played together"
            });
            if (relationship.RecentChanges.Count > MentalSystemConstants.MaximumRecentChanges)
                relationship.RecentChanges.RemoveRange(0, relationship.RecentChanges.Count - MentalSystemConstants.MaximumRecentChanges);
        }
        Prune(team);
    }

    public static string GetStableId(Player player, Team team) => !string.IsNullOrWhiteSpace(player.PlayerId)
        ? player.PlayerId
        : $"legacy:{team.Name}:{player.Name}:{player.SquadNumber}";

    private static bool AreRelevant(Player first, Player second) =>
        first.Nationality.Equals(second.Nationality, StringComparison.OrdinalIgnoreCase) ||
        Math.Abs((first.Age ?? 25) - (second.Age ?? 25)) <= 3 ||
        first.Position == second.Position || first.IsCaptain || second.IsCaptain ||
        first.Traits.Contains(PlayerTrait.Leadership) || second.Traits.Contains(PlayerTrait.Leadership);

    private static double GetCompatibility(Player first, Player second)
    {
        var nationality = first.Nationality.Equals(second.Nationality, StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        var age = Math.Max(0, 5 - Math.Abs((first.Age ?? 25) - (second.Age ?? 25)));
        var sociability = (first.MentalState.Personality.Sociability + second.MentalState.Personality.Sociability - 20) * 0.35;
        var adaptability = (first.MentalState.Personality.Adaptability + second.MentalState.Personality.Adaptability - 20) * 0.15;
        var loyalty = (first.MentalState.Personality.Loyalty + second.MentalState.Personality.Loyalty - 20) * 0.10;
        var firstEffects = PersonalityService.GetArchetypeEffects(first);
        var secondEffects = PersonalityService.GetArchetypeEffects(second);
        var conflictMultiplier = Math.Min(firstEffects.EgoConflictMultiplier, secondEffects.EgoConflictMultiplier);
        var egoConflict = Math.Max(0, first.MentalState.Personality.Ego + second.MentalState.Personality.Ego - 30) * 0.4 * conflictMultiplier;
        var archetypeCompatibility = firstEffects.RelationshipCompatibilityBonus + secondEffects.RelationshipCompatibilityBonus;
        return Math.Clamp(nationality + age + sociability + adaptability + loyalty + archetypeCompatibility - egoConflict, -20, 20);
    }

    private static (string, string) Order(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0 ? (first, second) : (second, first);

    private static void Prune(Team team)
    {
        team.DressingRoom.Relationships.RemoveAll(item => item.MatchesPlayedTogether == 0 &&
            Math.Abs(item.Friendship) < 1 && Math.Abs(item.Respect) < 1 && Math.Abs(item.Rivalry) < 1);
    }
}
