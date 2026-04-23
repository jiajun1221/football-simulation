namespace FootballSimulation.Models;

public class ClubNewsItem
{
    public ClubNewsType Type { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
