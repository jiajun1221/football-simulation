using System.Text.Json.Serialization;

namespace FootballSimulation.Data.JsonModels;

public class SquadPlayerRecord
{
    public string? PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SquadNumber { get; set; }
    public string Position { get; set; } = string.Empty;
    public string? PreferredPosition { get; set; }
    public List<string> SecondaryPositions { get; set; } = [];
    public string? PreferredFoot { get; set; }
    public string? Nationality { get; set; }
    public string? NationalityCode { get; set; }
    public string? NationalityName { get; set; }
    public string? FlagEmoji { get; set; }
    public string? FlagImagePath { get; set; }
    public int? DisciplineRating { get; set; }
    public int OverallRating { get; set; }
    public int? Age { get; set; }
    public int? PotentialOverall { get; set; }
    public int? Pace { get; set; }
    public int? Shooting { get; set; }
    [JsonPropertyName("passing")]
    public int? PassingAttribute { get; set; }
    public int? Dribbling { get; set; }
    public int? Defending { get; set; }
    public int? Physical { get; set; }
    public string? TransferStatus { get; set; }
    public string? Role { get; set; }
    public bool? RejectTransferOffers { get; set; }
    public int? ContractEndYear { get; set; }
    public decimal? WeeklyWage { get; set; }
    public decimal? ReleaseClause { get; set; }
    public string? ContractStatus { get; set; }
    public int? Stamina { get; set; }
    public int Fatigue { get; set; }
    public string Form { get; set; } = "Average";
    public string? FormStatus { get; set; }
    public bool IsStarter { get; set; }
    public List<string> Traits { get; set; } = [];
    public int? Morale { get; set; }
    public bool? IsInjured { get; set; }
    public string? InjuryType { get; set; }
    public string? InjurySeverity { get; set; }
    public int? InjuryRecoveryMatches { get; set; }
    public bool? IsSeasonEndingInjury { get; set; }
    public bool? IsSuspended { get; set; }
    public int? SuspendedMatches { get; set; }
    public int? MatchesPlayedRecently { get; set; }
}
