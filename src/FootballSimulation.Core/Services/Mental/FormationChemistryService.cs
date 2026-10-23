using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed class FormationChemistryService
{
    public void ApplyMatchSnapshot(Team team)
    {
        foreach (var player in team.AllPlayers) player.MentalState.MatchChemistryModifier = 0;
        var links = GetAdjacentLinks(team.Players);
        var totals = new Dictionary<Player, List<double>>();
        foreach (var (first, second) in links)
        {
            var relationship = Find(team, first, second);
            var familiarity = Math.Min(1.0, (relationship?.MatchesPlayedTogether ?? 0) / 20.0);
            var relationshipScore = ((relationship?.Friendship ?? 0) + (relationship?.Respect ?? 0) - (relationship?.Rivalry ?? 0)) / 300.0;
            var language = first.Nationality.Equals(second.Nationality, StringComparison.OrdinalIgnoreCase) ? 0.008 : 0;
            var modifier = Math.Clamp(familiarity * 0.025 + relationshipScore * 0.02 + language,
                -MentalSystemConstants.MaximumChemistryModifier, MentalSystemConstants.MaximumChemistryModifier);
            Add(totals, first, modifier);
            Add(totals, second, modifier);
        }
        foreach (var item in totals)
            item.Key.MentalState.MatchChemistryModifier = Math.Clamp(item.Value.Average(),
                -MentalSystemConstants.MaximumChemistryModifier, MentalSystemConstants.MaximumChemistryModifier);
    }

    public IReadOnlyList<(Player First, Player Second)> GetAdjacentLinks(IReadOnlyList<Player> players)
    {
        var result = new List<(Player, Player)>();
        AddPairs(result, players, "CB", "CB");
        AddPairs(result, players, "LB", "LW", "LM");
        AddPairs(result, players, "RB", "RW", "RM");
        AddPairs(result, players, "LWB", "LW", "LM");
        AddPairs(result, players, "RWB", "RW", "RM");
        AddUnitPairs(result, players.Where(p => IsOneOf(Slot(p), "CDM", "CM", "CAM", "LM", "RM")).ToList());
        AddPairs(result, players, "ST", "ST", "CF");
        return result.Distinct().ToList();
    }

    private static PlayerRelationship? Find(Team team, Player first, Player second)
    {
        var firstId = RelationshipService.GetStableId(first, team);
        var secondId = RelationshipService.GetStableId(second, team);
        return team.DressingRoom.Relationships.FirstOrDefault(r =>
            r.FirstPlayerId == firstId && r.SecondPlayerId == secondId || r.FirstPlayerId == secondId && r.SecondPlayerId == firstId);
    }

    private static void Add(Dictionary<Player, List<double>> values, Player player, double value)
    {
        if (!values.TryGetValue(player, out var list)) values[player] = list = [];
        list.Add(value);
    }

    private static void AddPairs(List<(Player, Player)> result, IReadOnlyList<Player> players, string source, params string[] targets)
    {
        foreach (var first in players.Where(p => Slot(p) == source))
        foreach (var second in players.Where(p => !ReferenceEquals(p, first) && targets.Contains(Slot(p))))
            if (!result.Any(pair => pair.Item1 == second && pair.Item2 == first)) result.Add((first, second));
    }

    private static void AddUnitPairs(List<(Player, Player)> result, IReadOnlyList<Player> unit)
    {
        for (var i = 0; i < unit.Count; i++)
        for (var j = i + 1; j < unit.Count; j++) result.Add((unit[i], unit[j]));
    }

    private static string Slot(Player player) => (string.IsNullOrWhiteSpace(player.AssignedPosition) ? player.PreferredPosition : player.AssignedPosition).ToUpperInvariant();
    private static bool IsOneOf(string value, params string[] values) => values.Contains(value);
}
