using FootballSimulation.Models;

namespace FootballSimulation.Services;

public enum PersonalityTraitKey
{
    Professionalism,
    Temperament,
    Leadership,
    Resilience,
    BigMatchTemperament,
    Ambition,
    Loyalty,
    Sportsmanship,
    Ego,
    Adaptability,
    Sociability
}

public sealed record TraitRequirement(PersonalityTraitKey Trait, int? Minimum = null, int? Maximum = null);

public sealed record ArchetypeEffects(
    double PositiveLateOrRivalryResponse = 1.0,
    double NegativeEventResponse = 1.0,
    double RivalryNegativeResponse = 1.0,
    double GeneralEventResponse = 1.0,
    double SubstitutionNegativeResponse = 1.0,
    double RecoveryRate = 1.0,
    double ConfidenceRecoveryRate = 1.0,
    double RelationshipGrowth = 1.0,
    double RelationshipCompatibilityBonus = 0.0,
    double EgoConflictMultiplier = 1.0,
    double FocusBaselineAdjustment = 0.0,
    double AnxietyBaselineAdjustment = 0.0,
    double BenchedFrustrationAdjustment = 0.0,
    double FoulRiskAdjustment = 0.0,
    double FrustrationResponse = 1.0,
    double JoyResponse = 1.0);

public sealed record PersonalityArchetypeDefinition(
    string Name,
    IReadOnlyList<TraitRequirement> Requirements,
    ArchetypeEffects Effects);

public static class PersonalityArchetypeConfig
{
    // Ordered table: first match wins. Thresholds and small additive/multiplicative effects are tuned here.
    public static IReadOnlyList<PersonalityArchetypeDefinition> Definitions { get; } =
    [
        Define("Captain Material", [Min(PersonalityTraitKey.Leadership, 16), Min(PersonalityTraitKey.Resilience, 13)]),
        Define("Hothead", [Max(PersonalityTraitKey.Temperament, 6), Max(PersonalityTraitKey.Sportsmanship, 9)]),
        Define("Mercenary", [Min(PersonalityTraitKey.Ambition, 16), Max(PersonalityTraitKey.Loyalty, 7)]),
        Define("Diva", [Min(PersonalityTraitKey.Ego, 16), Max(PersonalityTraitKey.Sociability, 9)]),
        Define("Club Man", [Min(PersonalityTraitKey.Loyalty, 16), Min(PersonalityTraitKey.Professionalism, 12)]),
        Define("Quiet Professional", [Min(PersonalityTraitKey.Professionalism, 15), Min(PersonalityTraitKey.Temperament, 13)]),

        Define("Big-Game Player", [Min(PersonalityTraitKey.BigMatchTemperament, 15), Min(PersonalityTraitKey.Resilience, 11)],
            new(PositiveLateOrRivalryResponse: 1.08, AnxietyBaselineAdjustment: -2)),
        Define("Fragile Talent", [Max(PersonalityTraitKey.Resilience, 8), Max(PersonalityTraitKey.BigMatchTemperament, 9)],
            new(NegativeEventResponse: 1.08, RivalryNegativeResponse: 1.05, AnxietyBaselineAdjustment: 3)),
        Define("Sensitive Soul", [Min(PersonalityTraitKey.Ego, 14), Max(PersonalityTraitKey.Resilience, 10)],
            new(NegativeEventResponse: 1.06, SubstitutionNegativeResponse: 1.10, ConfidenceRecoveryRate: 0.90)),
        Define("Enforcer", [Max(PersonalityTraitKey.Sportsmanship, 8), Max(PersonalityTraitKey.Temperament, 11)],
            new(FoulRiskAdjustment: 0.015)),
        Define("Social Glue", [Min(PersonalityTraitKey.Sociability, 14), Min(PersonalityTraitKey.Adaptability, 12)],
            new(RelationshipGrowth: 1.15, RelationshipCompatibilityBonus: 1.5)),
        Define("Lone Wolf", [Max(PersonalityTraitKey.Sociability, 8), Max(PersonalityTraitKey.Ego, 13)],
            new(RelationshipGrowth: 0.80, EgoConflictMultiplier: 0.0)),
        Define("Driven Professional", [Min(PersonalityTraitKey.Ambition, 14), Min(PersonalityTraitKey.Professionalism, 12)],
            new(SubstitutionNegativeResponse: 1.08, FocusBaselineAdjustment: 2, BenchedFrustrationAdjustment: 1.5)),
        Define("Humble Servant", [Min(PersonalityTraitKey.Loyalty, 13), Max(PersonalityTraitKey.Ego, 8)],
            new(SubstitutionNegativeResponse: 0.80, RelationshipCompatibilityBonus: 1.5, EgoConflictMultiplier: 0.5)),
        Define("Free Spirit", [Max(PersonalityTraitKey.Professionalism, 8), Min(PersonalityTraitKey.Sociability, 12)],
            new(RelationshipGrowth: 1.10, FocusBaselineAdjustment: -3, JoyResponse: 1.12)),
        Define("Passenger", [Max(PersonalityTraitKey.Professionalism, 8), Max(PersonalityTraitKey.Ambition, 9)],
            new(GeneralEventResponse: 0.85, RecoveryRate: 0.85, FocusBaselineAdjustment: -3)),
        Define("Gentleman", [Min(PersonalityTraitKey.Sportsmanship, 16), Min(PersonalityTraitKey.Temperament, 11)],
            new(FoulRiskAdjustment: -0.015, FrustrationResponse: 0.80))
    ];

    public static int GetValue(PersonalityProfile profile, PersonalityTraitKey trait) => trait switch
    {
        PersonalityTraitKey.Professionalism => profile.Professionalism,
        PersonalityTraitKey.Temperament => profile.Temperament,
        PersonalityTraitKey.Leadership => profile.Leadership,
        PersonalityTraitKey.Resilience => profile.Resilience,
        PersonalityTraitKey.BigMatchTemperament => profile.BigMatchTemperament,
        PersonalityTraitKey.Ambition => profile.Ambition,
        PersonalityTraitKey.Loyalty => profile.Loyalty,
        PersonalityTraitKey.Sportsmanship => profile.Sportsmanship,
        PersonalityTraitKey.Ego => profile.Ego,
        PersonalityTraitKey.Adaptability => profile.Adaptability,
        PersonalityTraitKey.Sociability => profile.Sociability,
        _ => 10
    };

    private static PersonalityArchetypeDefinition Define(string name, IReadOnlyList<TraitRequirement> requirements, ArchetypeEffects? effects = null) =>
        new(name, requirements, effects ?? new ArchetypeEffects());

    private static TraitRequirement Min(PersonalityTraitKey trait, int value) => new(trait, Minimum: value);
    private static TraitRequirement Max(PersonalityTraitKey trait, int value) => new(trait, Maximum: value);
}
