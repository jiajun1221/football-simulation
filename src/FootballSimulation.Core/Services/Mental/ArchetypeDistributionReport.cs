using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed record ArchetypeDistributionResult(
    IReadOnlyDictionary<string, double> CurrentPoolPercentages,
    IReadOnlyDictionary<string, double> RandomSamplePercentages);

public static class ArchetypeDistributionReport
{
    public static ArchetypeDistributionResult Generate(
        IEnumerable<Player> currentPlayers,
        int sampleSize = 10_000,
        int seed = 20261023,
        TextWriter? output = null)
    {
        if (sampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSize));

        var current = Calculate(currentPlayers.Select(PersonalityService.GetArchetype));
        var random = new Random(seed);
        var sample = Enumerable.Range(0, sampleSize)
            .Select(_ => PersonalityService.GetArchetype(CreateRandomProfile(random)));
        var sampled = Calculate(sample);

        WriteSection(output, "Current squad pool", current);
        WriteSection(output, $"Random sample ({sampleSize:N0})", sampled);
        return new ArchetypeDistributionResult(current, sampled);
    }

    private static IReadOnlyDictionary<string, double> Calculate(IEnumerable<string> archetypes)
    {
        var values = archetypes.ToList();
        if (values.Count == 0) return new Dictionary<string, double>();
        return values.GroupBy(value => value)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count() * 100.0 / values.Count);
    }

    private static PersonalityProfile CreateRandomProfile(Random random) => new()
    {
        Professionalism = random.Next(1, 21),
        Temperament = random.Next(1, 21),
        Leadership = random.Next(1, 21),
        Resilience = random.Next(1, 21),
        BigMatchTemperament = random.Next(1, 21),
        Ambition = random.Next(1, 21),
        Loyalty = random.Next(1, 21),
        Sportsmanship = random.Next(1, 21),
        Ego = random.Next(1, 21),
        Adaptability = random.Next(1, 21),
        Sociability = random.Next(1, 21),
        InitializationVersion = 1
    };

    private static void WriteSection(TextWriter? output, string title, IReadOnlyDictionary<string, double> values)
    {
        if (output is null) return;
        output.WriteLine(title);
        foreach (var item in values.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
            output.WriteLine($"{item.Key}: {item.Value:0.00}%");
    }
}
