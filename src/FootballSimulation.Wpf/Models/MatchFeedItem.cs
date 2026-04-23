namespace FootballSimulation.Wpf.Models;

public class MatchFeedItem
{
    public int Minute { get; set; }
    public string MinuteText => $"{Minute}'";
    public string Type { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string EventLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public string RowBackground { get; set; } = "#FFFFFF";
    public string RowBorderBrush { get; set; } = "#DCE5F0";
    public string IconBackground { get; set; } = "#EEF3FA";
    public string LabelBackground { get; set; } = "#E9EEF5";
    public string LabelForeground { get; set; } = "#14233A";
    public bool IsGoal { get; set; }
    public bool IsImportant { get; set; }
}
