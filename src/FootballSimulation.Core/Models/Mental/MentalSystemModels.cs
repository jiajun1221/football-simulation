namespace FootballSimulation.Models;

public sealed class PersonalityProfile
{
    public int Professionalism { get; set; } = 10;
    public int Temperament { get; set; } = 10;
    public int Leadership { get; set; } = 10;
    public int Resilience { get; set; } = 10;
    public int BigMatchTemperament { get; set; } = 10;
    public int Ambition { get; set; } = 10;
    public int Loyalty { get; set; } = 10;
    public int Sportsmanship { get; set; } = 10;
    public int Ego { get; set; } = 10;
    public int Adaptability { get; set; } = 10;
    public int Sociability { get; set; } = 10;
    public int InitializationVersion { get; set; } = 1;
}

public sealed class EmotionState
{
    public double Confidence { get; set; } = 50;
    public double Frustration { get; set; } = 20;
    public double Anxiety { get; set; } = 20;
    public double Joy { get; set; } = 40;
    public double Focus { get; set; } = 60;
    public int LastUpdatedMinute { get; set; }
    public int LastUpdatedCalendarRound { get; set; }
}

public sealed class PlayerMentalState
{
    public PersonalityProfile Personality { get; set; } = new();
    public EmotionState Emotions { get; set; } = new();
    public string MentorPlayerId { get; set; } = string.Empty;
    public List<MentalChangeRecord> RecentChanges { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public double MatchChemistryModifier { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public double MatchEmotionModifier { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool MatchEffectsEnabled { get; set; }
}

public sealed class MentalChangeRecord
{
    public int CalendarRound { get; set; }
    public int? MatchMinute { get; set; }
    public string Category { get; set; } = string.Empty;
    public double Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PlayerRelationship
{
    public string FirstPlayerId { get; set; } = string.Empty;
    public string SecondPlayerId { get; set; } = string.Empty;
    public double Friendship { get; set; }
    public double Respect { get; set; }
    public double Rivalry { get; set; }
    public int MatchesPlayedTogether { get; set; }
    public int LastUpdatedCalendarRound { get; set; }
    public List<MentalChangeRecord> RecentChanges { get; set; } = [];
}

public sealed class DressingRoomState
{
    public double Harmony { get; set; } = 50;
    public double SquadHappiness { get; set; } = 50;
    public string ViceCaptainPlayerId { get; set; } = string.Empty;
    public List<string> LeaderPlayerIds { get; set; } = [];
    public List<PlayerRelationship> Relationships { get; set; } = [];
    public List<DressingRoomClique> Cliques { get; set; } = [];
    public List<DressingRoomEvent> RecentEvents { get; set; } = [];
    public int LastProcessedCalendarRound { get; set; }
}

public sealed class DressingRoomClique
{
    public string Name { get; set; } = string.Empty;
    public List<string> PlayerIds { get; set; } = [];
}

public sealed class DressingRoomEvent
{
    public int CalendarRound { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> PlayerIds { get; set; } = [];
}
