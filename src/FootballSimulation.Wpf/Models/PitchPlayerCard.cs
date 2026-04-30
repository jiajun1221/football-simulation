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
    public string RatingText { get; init; } = string.Empty;
    public string MatchStatsText { get; init; } = string.Empty;
    public string FatigueText { get; init; } = string.Empty;
    public double FatigueBarWidth { get; init; }
    public string FatigueBrush { get; init; } = "#2FA84F";
    public string FormBadgeText { get; init; } = "Average";
    public string FormBadgeBackground { get; init; } = "#FFE36B";
    public string FormBadgeForeground { get; init; } = "#5F4500";
    public string CardBackground { get; init; } = "#FFFFFF";
    public string CardBorderBrush { get; init; } = "#102033";
    public Thickness CardBorderThickness { get; init; } = new(1);
}
