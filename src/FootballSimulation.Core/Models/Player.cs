namespace FootballSimulation.Models;

public class Player
{
    public string Name { get; set; } = string.Empty;
    public int SquadNumber { get; set; }
    public Position Position { get; set; }
    public string PreferredPosition { get; set; } = string.Empty;
    public List<string> NaturalPositions { get; set; } = [];
    public List<string> SecondaryPositions { get; set; } = [];
    public string AssignedPosition { get; set; } = string.Empty;
    public int OverallRating { get; set; }
    public string Form { get; set; } = "Average";
    public bool IsStarter { get; set; }
    public int CurrentForm { get; set; } = 50;
    public int Morale { get; set; } = 50;
    public List<PlayerTrait> Traits { get; set; } = [];
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Passing { get; set; }
    public int Stamina { get; set; }
    public double CurrentStamina { get; set; }
    public int Fatigue { get; set; }
    public double LiveMatchModifier { get; set; } = 1.0;
    public bool IsInjured { get; set; }
    public bool IsSuspended { get; set; }
    public int MatchesPlayedRecently { get; set; }
    public int Finishing { get; set; }
    public int YellowCards { get; set; }
    public bool IsSentOff { get; set; }
}
