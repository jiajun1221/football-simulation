using System.Windows;
using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Models;

public class PitchPlayerCard
{
    public required Player Player { get; init; }
    public string ShirtNumberText { get; init; } = string.Empty;
    public string ShirtNumberValue { get; init; } = string.Empty;
    public string PlayerImagePath { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public string PositionText { get; init; } = string.Empty;
    public string OverallText { get; init; } = string.Empty;
    public string OverallForeground { get; init; } = "#071A2E";
    public string TextForeground { get; init; } = "#102033";
    public string MutedForeground { get; init; } = "#143052";
    public string PositionBackground { get; init; } = "#E7EEF8";
    public string PositionForeground { get; init; } = "#071A2E";
    public string GrowthText { get; init; } = string.Empty;
    public string RatingText { get; init; } = string.Empty;
    public string MatchStatsText { get; init; } = string.Empty;
    public double Stamina { get; init; }
    public string StaminaBrush { get; init; } = "#2FA84F";
    public string FormBadgeText { get; init; } = string.Empty;
    public string FormBadgeBackground { get; init; } = "#E1E5EA";
    public string FormBadgeForeground { get; init; } = "#465364";
    public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
    public IReadOnlyList<PlayerCardStatusBadge> CardStatusBadges { get; init; } = [];
    public string CardBackground { get; init; } = "#FFFFFF";
    public string CardBorderBrush { get; init; } = "#102033";
    public Thickness CardBorderThickness { get; init; } = new(1);
}
