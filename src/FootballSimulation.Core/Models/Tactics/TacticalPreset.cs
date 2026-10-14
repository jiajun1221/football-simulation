namespace FootballSimulation.Models;

public sealed record TacticalPreset(
    string Key,
    string Label,
    string Description,
    TeamTactics Tactics);
