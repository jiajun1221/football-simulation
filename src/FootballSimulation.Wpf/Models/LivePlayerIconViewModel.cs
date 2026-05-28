namespace FootballSimulation.Wpf.Models;

public class LivePlayerIconViewModel
{
    public string PlayerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FlagImagePath { get; init; } = "/Assets/Flags/default.png";
    public string NationalityName { get; init; } = "Unknown nationality";
    public string TeamName { get; init; } = string.Empty;
    public string PlayerKey { get; init; } = string.Empty;
    public LivePlayerStats LiveStats { get; init; } = new();
    public string ShirtNumberText { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public string PositionText { get; init; } = string.Empty;
    public string ExactPosition { get; init; } = string.Empty;
    public string TeamSide { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public string IconBrush { get; init; } = "#246BFE";
    public string IconForeground { get; init; } = "#FFFFFF";
    public string BorderBrush { get; init; } = "#DCEBFF";
    public string SelectionBrush { get; init; } = "Transparent";
    public double SelectionThickness { get; init; }
    public string RatingText => LiveStats.RatingDisplay;
    public string RatingBrush => LiveStats.RatingBrush;
    public string RatingForeground => LiveStats.RatingForeground;
    public string RatingFormBrush { get; init; } = "#FACC15";
    public string RatingBadgeBackground { get; init; } = "#102033";
    public string RatingBadgeForeground { get; init; } = "#FACC15";
    public string RatingBadgeBorderBrush { get; init; } = "#FACC15";
    public double Stamina => LiveStats.StaminaPercent;
    public string StaminaBrush => LiveStats.StaminaBrush;
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
    public string ErrorBadgeText { get; init; } = string.Empty;
    public string SaveBadgeText { get; init; } = string.Empty;
    public string YellowBadgeText { get; init; } = string.Empty;
    public string RedBadgeText { get; init; } = string.Empty;
    public string InjuryBadgeText { get; init; } = string.Empty;
    public string PendingSubOutBadgeText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public string CardsText { get; init; } = "None";
    public string InjuryStatusText { get; init; } = string.Empty;
    public string FormText { get; init; } = string.Empty;
    public string StaminaText { get; init; } = "0/0";
    public string MatchStatusText { get; init; } = "Fresh";
    public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
    public IReadOnlyList<PlayerCardStatusBadge> CardStatusBadges { get; init; } = [];
}
