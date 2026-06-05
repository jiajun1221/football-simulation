namespace FootballSimulation.Services;

public enum FormationCategory
{
    Attacking,
    Balanced,
    Defensive
}

public sealed record FormationOption(string Name, FormationCategory Category)
{
    public override string ToString() => Name;
}

public static class FormationCatalogService
{
    private static readonly IReadOnlyList<FormationOption> Formations =
    [
        new("4-3-3 Attack", FormationCategory.Attacking),
        new("4-2-4", FormationCategory.Attacking),
        new("3-4-3", FormationCategory.Attacking),
        new("4-1-2-1-2 Narrow", FormationCategory.Attacking),
        new("4-2-2-2", FormationCategory.Attacking),
        new("3-5-2 Attack", FormationCategory.Attacking),

        new("4-3-3 Holding", FormationCategory.Balanced),
        new("4-3-3 Flat", FormationCategory.Balanced),
        new("4-2-3-1 Wide", FormationCategory.Balanced),
        new("4-2-3-1 Narrow", FormationCategory.Balanced),
        new("4-4-2", FormationCategory.Balanced),
        new("4-1-4-1", FormationCategory.Balanced),
        new("3-5-2", FormationCategory.Balanced),
        new("5-2-3", FormationCategory.Balanced),

        new("4-5-1", FormationCategory.Defensive),
        new("5-3-2", FormationCategory.Defensive),
        new("5-4-1", FormationCategory.Defensive),
        new("5-2-2-1", FormationCategory.Defensive),
        new("4-1-4-1 Defensive", FormationCategory.Defensive),
        new("3-4-2-1", FormationCategory.Defensive)
    ];

    private static readonly Dictionary<string, string> FormationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4-3-3"] = "4-3-3 Attack",
        ["4-2-3-1"] = "4-2-3-1 Wide",
        ["433"] = "4-3-3 Attack",
        ["4231"] = "4-2-3-1 Wide",
        ["442"] = "4-4-2",
        ["352"] = "3-5-2"
    };

    public static IReadOnlyList<FormationOption> GetFormations()
    {
        return Formations;
    }

    public static IReadOnlyList<string> GetFormationNames()
    {
        return Formations.Select(formation => formation.Name).ToList();
    }

    public static string NormalizeFormationName(string? formation)
    {
        var normalized = string.Join(' ', (formation ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "4-3-3 Holding";
        }

        if (FormationAliases.TryGetValue(normalized, out var alias))
        {
            return alias;
        }

        return Formations.Any(option => option.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ? Formations.First(option => option.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Name
            : "4-3-3 Holding";
    }

    public static FormationCategory GetCategory(string? formation)
    {
        var normalized = NormalizeFormationName(formation);
        return Formations.First(option => option.Name == normalized).Category;
    }
}
