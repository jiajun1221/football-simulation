using System.Security.Cryptography;
using System.Text;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PersonalityService
{
    public static void EnsureInitialized(Player player, string teamName)
    {
        player.MentalState ??= new PlayerMentalState();
        player.MentalState.Personality ??= new PersonalityProfile();
        player.MentalState.Emotions ??= new EmotionState();
        player.MentalState.RecentChanges ??= [];

        if (player.MentalState.Personality.InitializationVersion > 0 &&
            !IsNeutralDefault(player.MentalState.Personality))
        {
            Clamp(player.MentalState.Personality);
            return;
        }

        var key = $"mental-v1|{teamName}|{player.PlayerId}|{player.Name}|{player.Age}|{player.Nationality}";
        var values = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var profile = player.MentalState.Personality;
        profile.Professionalism = Scale(values[0]);
        profile.Temperament = Scale(values[1]);
        profile.Leadership = Scale(values[2]);
        profile.Resilience = Scale(values[3]);
        profile.BigMatchTemperament = Scale(values[4]);
        profile.Ambition = Scale(values[5]);
        profile.Loyalty = Scale(values[6]);
        profile.Sportsmanship = Scale(values[7]);
        profile.Ego = Scale(values[8]);
        profile.Adaptability = Scale(values[9]);
        profile.Sociability = Scale(values[10]);

        Bias(profile, player);
        profile.InitializationVersion = 1;
        SetEmotionBaselines(player);
    }

    public static string GetArchetype(Player player)
    {
        return GetArchetype(player.MentalState.Personality);
    }

    public static string GetArchetype(PersonalityProfile profile) =>
        GetDefinition(profile)?.Name ?? GetFallbackArchetype(profile);

    public static PersonalityArchetypeDefinition? GetDefinition(PersonalityProfile profile) =>
        PersonalityArchetypeConfig.Definitions.FirstOrDefault(definition => definition.Requirements.All(requirement =>
        {
            var value = PersonalityArchetypeConfig.GetValue(profile, requirement.Trait);
            return (!requirement.Minimum.HasValue || value >= requirement.Minimum.Value) &&
                (!requirement.Maximum.HasValue || value <= requirement.Maximum.Value);
        }));

    public static ArchetypeEffects GetArchetypeEffects(Player player) =>
        GetDefinition(player.MentalState.Personality)?.Effects ?? new ArchetypeEffects();

    public static string GetSecondaryTag(Player player)
    {
        var profile = player.MentalState.Personality;
        var definition = GetDefinition(profile);
        var excluded = definition?.Requirements.Select(requirement => requirement.Trait).ToHashSet() ?? [];
        if (definition is null)
        {
            var primaryExtreme = GetExtremeTrait(profile, new HashSet<PersonalityTraitKey>());
            if (primaryExtreme is not null) excluded.Add(primaryExtreme.Value.Trait);
        }
        var extreme = GetExtremeTrait(profile, excluded);
        return extreme is null ? string.Empty : FormatTraitLabel(extreme.Value.Trait, extreme.Value.Value < extreme.Value.Average, includeTypeSuffix: false);
    }

    private static bool IsNeutralDefault(PersonalityProfile p) =>
        p.Professionalism == 10 && p.Temperament == 10 && p.Leadership == 10 &&
        p.Resilience == 10 && p.BigMatchTemperament == 10 && p.Ambition == 10 &&
        p.Loyalty == 10 && p.Sportsmanship == 10 && p.Ego == 10 &&
        p.Adaptability == 10 && p.Sociability == 10;

    private static int Scale(byte value) => 4 + value % 14;

    private static void Bias(PersonalityProfile p, Player player)
    {
        if (player.Traits.Contains(PlayerTrait.Leadership)) p.Leadership += 4;
        if (player.Traits.Contains(PlayerTrait.BigMatchPlayer)) p.BigMatchTemperament += 4;
        if (player.Traits.Contains(PlayerTrait.Composed)) p.Temperament += 3;
        if (player.Traits.Contains(PlayerTrait.TeamPlayer))
        {
            p.Sociability += 3;
            p.Sportsmanship += 2;
        }
        if (player.Traits.Contains(PlayerTrait.DivesIntoTackles))
        {
            p.Temperament -= 2;
            p.Sportsmanship -= 2;
        }
        Clamp(p);
    }

    private static void Clamp(PersonalityProfile p)
    {
        p.Professionalism = Math.Clamp(p.Professionalism, 1, 20);
        p.Temperament = Math.Clamp(p.Temperament, 1, 20);
        p.Leadership = Math.Clamp(p.Leadership, 1, 20);
        p.Resilience = Math.Clamp(p.Resilience, 1, 20);
        p.BigMatchTemperament = Math.Clamp(p.BigMatchTemperament, 1, 20);
        p.Ambition = Math.Clamp(p.Ambition, 1, 20);
        p.Loyalty = Math.Clamp(p.Loyalty, 1, 20);
        p.Sportsmanship = Math.Clamp(p.Sportsmanship, 1, 20);
        p.Ego = Math.Clamp(p.Ego, 1, 20);
        p.Adaptability = Math.Clamp(p.Adaptability, 1, 20);
        p.Sociability = Math.Clamp(p.Sociability, 1, 20);
    }

    private static void SetEmotionBaselines(Player player)
    {
        var p = player.MentalState.Personality;
        var e = player.MentalState.Emotions;
        var effects = GetArchetypeEffects(player);
        e.Confidence = Math.Clamp(player.Morale, 35, 65);
        e.Frustration = Math.Clamp(30 - p.Temperament * 0.8, 10, 35);
        e.Anxiety = Math.Clamp(32 - p.Resilience * 0.8 + effects.AnxietyBaselineAdjustment, 10, 35);
        e.Joy = Math.Clamp(30 + player.Morale * 0.2, 30, 55);
        e.Focus = Math.Clamp(45 + p.Professionalism * 0.8 + effects.FocusBaselineAdjustment, 42, 67);
    }

    private static string GetFallbackArchetype(PersonalityProfile profile)
    {
        var extreme = GetExtremeTrait(profile, new HashSet<PersonalityTraitKey>());
        if (extreme is null || extreme.Value.Distance <= 3) return "Balanced Character";
        return FormatTraitLabel(extreme.Value.Trait, extreme.Value.Value < extreme.Value.Average, includeTypeSuffix: true);
    }

    private static (PersonalityTraitKey Trait, int Value, double Average, double Distance)? GetExtremeTrait(
        PersonalityProfile profile,
        IReadOnlySet<PersonalityTraitKey> excluded)
    {
        var traits = Enum.GetValues<PersonalityTraitKey>()
            .Select(trait => (Trait: trait, Value: PersonalityArchetypeConfig.GetValue(profile, trait)))
            .ToList();
        var average = traits.Average(item => item.Value);
        var candidates = traits.Where(item => !excluded.Contains(item.Trait))
            .Select(item => (item.Trait, item.Value, Average: average, Distance: Math.Abs(item.Value - average)))
            .OrderByDescending(item => item.Distance)
            .ThenBy(item => item.Trait)
            .ToList();
        return candidates.Count == 0 ? null : candidates[0];
    }

    private static string FormatTraitLabel(PersonalityTraitKey trait, bool isLow, bool includeTypeSuffix)
    {
        var adjective = trait switch
        {
            PersonalityTraitKey.Professionalism => "Professional",
            PersonalityTraitKey.Temperament => "Temperamental",
            PersonalityTraitKey.Leadership => "Leader",
            PersonalityTraitKey.Resilience => "Resilient",
            PersonalityTraitKey.BigMatchTemperament => "Big-Match",
            PersonalityTraitKey.Ambition => "Ambitious",
            PersonalityTraitKey.Loyalty => "Loyal",
            PersonalityTraitKey.Sportsmanship => "Sporting",
            PersonalityTraitKey.Ego => "Ego",
            PersonalityTraitKey.Adaptability => "Adaptable",
            PersonalityTraitKey.Sociability => "Social",
            _ => trait.ToString()
        };
        var label = isLow ? $"Low {adjective}" : adjective;
        return includeTypeSuffix ? $"{label} Type" : label;
    }
}
