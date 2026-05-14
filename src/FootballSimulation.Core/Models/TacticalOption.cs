namespace FootballSimulation.Models;

public sealed record TacticalOption(
    TacticalDimension Dimension,
    string Key,
    string Label,
    string Icon,
    string Description,
    int Value,
    Mentality? MentalityValue = null);
