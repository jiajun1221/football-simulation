using System.Security.Cryptography;
using System.Text;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed class DressingRoomService
{
    private readonly RelationshipService _relationships = new();

    public void AdvanceCalendarSlot(League league, int calendarRound, IReadOnlyCollection<Match> matches)
    {
        foreach (var team in league.Teams)
        {
            EnsureTeam(team);
            if (team.DressingRoom.LastProcessedCalendarRound >= calendarRound) continue;
            foreach (var player in team.AllPlayers) EmotionService.DecayAfterCalendarSlot(player, calendarRound);
            foreach (var match in matches.Where(m => m.HomeTeam == team || m.AwayTeam == team))
            {
                _relationships.ApplySharedMatch(team, match, calendarRound);
                var participants = match.PlayerPerformances
                    .Where(performance => performance.TeamName == team.Name)
                    .Select(performance => performance.PlayerName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var benchedPlayer in team.AllPlayers.Where(player => !participants.Contains(player.Name)))
                    EmotionService.ApplyBenchedReaction(benchedPlayer, calendarRound);
            }
            UpdateHierarchy(team);
            UpdateCliques(team);
            UpdateHarmony(team);
            TryCreateEvent(team, league.Season, calendarRound);
            team.DressingRoom.LastProcessedCalendarRound = calendarRound;
        }
    }

    public static void EnsureTeam(Team team)
    {
        team.DressingRoom ??= new DressingRoomState();
        team.DressingRoom.LeaderPlayerIds ??= [];
        team.DressingRoom.Relationships ??= [];
        team.DressingRoom.Cliques ??= [];
        team.DressingRoom.RecentEvents ??= [];
        foreach (var player in team.AllPlayers.Concat(team.LoanedOutPlayers).Distinct()) PersonalityService.EnsureInitialized(player, team.Name);
    }

    private static void UpdateHierarchy(Team team)
    {
        var ranked = team.AllPlayers.OrderByDescending(p =>
            p.MentalState.Personality.Leadership * 3 + p.MentalState.Personality.Resilience +
            (p.IsCaptain ? 100 : 0) + (p.Traits.Contains(PlayerTrait.Leadership) ? 20 : 0)).ToList();
        team.DressingRoom.LeaderPlayerIds = ranked.Take(4).Select(p => RelationshipService.GetStableId(p, team)).ToList();
        team.DressingRoom.ViceCaptainPlayerId = ranked.Skip(1).Select(p => RelationshipService.GetStableId(p, team)).FirstOrDefault() ?? string.Empty;
    }

    private static void UpdateHarmony(Team team)
    {
        var relationships = team.DressingRoom.Relationships;
        var relationshipScore = relationships.Count == 0 ? 0 : relationships.Average(r => r.Friendship + r.Respect - r.Rivalry) / 4.0;
        var morale = team.AllPlayers.Select(p => p.Morale).DefaultIfEmpty(50).Average();
        team.DressingRoom.Harmony = Math.Clamp(45 + relationshipScore + (morale - 50) * 0.35, 0, 100);
        team.DressingRoom.SquadHappiness = Math.Clamp(morale * 0.65 + team.DressingRoom.Harmony * 0.35, 0, 100);
    }

    private static void UpdateCliques(Team team)
    {
        team.DressingRoom.Cliques = team.AllPlayers
            .Where(player => !string.IsNullOrWhiteSpace(player.Nationality))
            .GroupBy(player => player.Nationality, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .Select(group => new DressingRoomClique
            {
                Name = $"{group.Key} group",
                PlayerIds = group.Select(player => RelationshipService.GetStableId(player, team)).ToList()
            })
            .Take(4)
            .ToList();
    }

    private static void TryCreateEvent(Team team, string season, int round)
    {
        var players = team.AllPlayers.ToList();
        if (players.Count < 2) return;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"dressing-v1|{season}|{round}|{team.Name}"));
        var roll = BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
        if (roll > 0.18) return;
        var first = players[hash[4] % players.Count];
        var second = players[hash[5] % players.Count];
        if (ReferenceEquals(first, second)) second = players[(players.IndexOf(first) + 1) % players.Count];
        var positive = team.DressingRoom.Harmony >= 50 || hash[6] % 2 == 0;
        var description = positive
            ? $"{first.Name} helped {second.Name} settle into the group."
            : $"Tension surfaced between {first.Name} and {second.Name} after training.";
        team.DressingRoom.RecentEvents.Add(new DressingRoomEvent
        {
            CalendarRound = round,
            EventType = positive ? "Leader intervention" : "Argument",
            Description = description,
            PlayerIds = [RelationshipService.GetStableId(first, team), RelationshipService.GetStableId(second, team)]
        });
        team.DressingRoom.Harmony = Math.Clamp(team.DressingRoom.Harmony + (positive ? 2 : -2), 0, 100);
        var relationship = new RelationshipService().GetOrCreate(team, first, second);
        relationship.Friendship = Math.Clamp(relationship.Friendship + (positive ? 2 : -3), -100, 100);
        relationship.Rivalry = Math.Clamp(relationship.Rivalry + (positive ? -1 : 3), -100, 100);
        relationship.RecentChanges.Add(new MentalChangeRecord
        {
            CalendarRound = round,
            Category = "Relationship",
            Delta = positive ? 2 : -3,
            Reason = positive ? "Leader intervention" : "Training argument"
        });
        if (relationship.RecentChanges.Count > MentalSystemConstants.MaximumRecentChanges)
            relationship.RecentChanges.RemoveRange(0, relationship.RecentChanges.Count - MentalSystemConstants.MaximumRecentChanges);
        if (team.DressingRoom.RecentEvents.Count > MentalSystemConstants.MaximumRecentDressingRoomEvents)
            team.DressingRoom.RecentEvents.RemoveRange(0, team.DressingRoom.RecentEvents.Count - MentalSystemConstants.MaximumRecentDressingRoomEvents);
    }
}
