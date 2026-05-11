using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Models;

public class MatchFeedItem
{
    public int Minute { get; set; }
    public MatchEvent? SourceEvent { get; set; }
    public string MinuteText => $"{Minute}'";
    public string Type { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string EventLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public string TriggeredTrait { get; set; } = string.Empty;
    public string TriggeredTraitIcon { get; set; } = string.Empty;
    public string TriggeredTraitDescription { get; set; } = string.Empty;
    public bool HasTriggeredTrait => !string.IsNullOrWhiteSpace(TriggeredTraitIcon);
    public string RowBackground { get; set; } = "#FFFFFF";
    public string RowBorderBrush { get; set; } = "#DCE5F0";
    public string IconBackground { get; set; } = "#EEF3FA";
    public string IconForeground { get; set; } = "#14233A";
    public string LabelBackground { get; set; } = "#E9EEF5";
    public string LabelForeground { get; set; } = "#14233A";
    public string MinuteForeground { get; set; } = "#14233A";
    public string TitleForeground { get; set; } = "#102033";
    public string DescriptionForeground { get; set; } = "#34465C";
    public string TraitBadgeBackground { get; set; } = "#FFFFFF";
    public string TraitBadgeBorderBrush { get; set; } = "#DCE5F0";
    public bool IsGoal { get; set; }
    public bool IsImportant { get; set; }
}
