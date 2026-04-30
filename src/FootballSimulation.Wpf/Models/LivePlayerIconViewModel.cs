namespace FootballSimulation.Wpf.Models;

public class LivePlayerIconViewModel
{
    public string Name { get; init; } = string.Empty;
    public string TeamName { get; init; } = string.Empty;
    public string PlayerKey { get; init; } = string.Empty;
    public string ShirtNumberText { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public string PositionText { get; init; } = string.Empty;
    public string TeamSide { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public string IconBrush { get; init; } = "#246BFE";
    public string BorderBrush { get; init; } = "#DCEBFF";
    public string SelectionBrush { get; init; } = "Transparent";
    public double SelectionThickness { get; init; }
    public string RatingText { get; init; } = "6.0";
    public int FatiguePercent { get; init; }
    public double FatigueBarWidth { get; init; }
    public string FatigueBrush { get; init; } = "#2FA84F";
    public int Goals { get; init; }
    public int Assists { get; init; }
    public int DefensiveContributions { get; init; }
    public int Saves { get; init; }
    public int YellowCards { get; init; }
    public int RedCards { get; init; }
    public bool IsInjured { get; init; }
    public string ContributionBadgesText { get; init; } = string.Empty;
    public string GoalBadgeText { get; init; } = string.Empty;
    public string AssistBadgeText { get; init; } = string.Empty;
    public string DefensiveBadgeText { get; init; } = string.Empty;
    public string SaveBadgeText { get; init; } = string.Empty;
    public string YellowBadgeText { get; init; } = string.Empty;
    public string RedBadgeText { get; init; } = string.Empty;
    public string InjuryBadgeText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public string CardsText { get; init; } = "None";
    public string InjuryStatusText { get; init; } = "Fit";
    public string FormText { get; init; } = "Average";
    public string StaminaText { get; init; } = "0/0";
    public string MatchStatusText { get; init; } = "Fresh";
}
