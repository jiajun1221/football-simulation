using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FootballSimulation.Models;

public class Player : INotifyPropertyChanged
{
    private double _stamina = 100;
    private PlayerFormStatus _formStatus = PlayerFormStatus.Average;
    private int _suspendedMatches;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SquadNumber { get; set; }
    public Position Position { get; set; }
    public string PreferredPosition { get; set; } = string.Empty;
    public List<string> SecondaryPositions { get; set; } = [];
    public string AssignedPosition { get; set; } = string.Empty;
    public string PreferredFoot { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string NationalityCode { get; set; } = string.Empty;
    public string NationalityName { get; set; } = string.Empty;
    public string FlagEmoji { get; set; } = string.Empty;
    public string FlagImagePath { get; set; } = string.Empty;
    public int DisciplineRating { get; set; } = 50;
    public int OverallRating { get; set; }
    public int BaseOverallRating { get; set; }
    public int GrowthPoints { get; set; }
    public int LastMatchGrowthPoints { get; set; }
    public int LastMatchOverallIncrease { get; set; }
    public int? Age { get; set; }
    public int? PotentialOverall { get; set; }
    public PlayerTransferStatus TransferStatus { get; set; } = PlayerTransferStatus.None;
    public int? ContractEndYear { get; set; }
    public decimal? WeeklyWage { get; set; }
    public decimal? ReleaseClause { get; set; }
    public PlayerContractStatus ContractStatus { get; set; } = PlayerContractStatus.Active;
    [JsonIgnore]
    public bool IsListedForSale
    {
        get => TransferStatus == PlayerTransferStatus.Listed;
        set
        {
            if (value)
            {
                TransferStatus = PlayerTransferStatus.Listed;
                return;
            }

            if (TransferStatus == PlayerTransferStatus.Listed)
            {
                TransferStatus = PlayerTransferStatus.None;
            }
        }
    }
    public PlayerRole Role { get; set; } = PlayerRole.Rotation;
    public bool RejectTransferOffers { get; set; }
    public string Form { get; set; } = "Average";
    public bool IsStarter { get; set; }
    public int CurrentForm { get; set; } = 50;
    public PlayerFormStatus FormStatus
    {
        get => _formStatus;
        set
        {
            if (_formStatus == value)
            {
                return;
            }

            _formStatus = value;
            OnPropertyChanged();
        }
    }

    public int Morale { get; set; } = 50;
    public List<PlayerTrait> Traits { get; set; } = [];
    public int Pace { get; set; }
    public int Shooting { get; set; }
    public int Dribbling { get; set; }
    public int Defending { get; set; }
    public int Physical { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Passing { get; set; }
    public double Stamina
    {
        get => _stamina;
        set => SetStamina(value);
    }

    public double CurrentStamina
    {
        get => Stamina;
        set => SetStamina(value);
    }

    [JsonIgnore]
    public int Fatigue
    {
        get => 100 - (int)Math.Round(Stamina);
        set => SetStamina(100 - value);
    }
    public double LiveMatchModifier { get; set; } = 1.0;
    public bool IsInjured { get; set; }
    public string InjuryType { get; set; } = string.Empty;
    public InjurySeverity? InjurySeverity { get; set; }
    public int InjuryRecoveryMatches { get; set; }
    public bool IsSeasonEndingInjury { get; set; }
    public bool NewlyInjuredThisMatch { get; set; }
    public int SuspendedMatches
    {
        get => _suspendedMatches;
        set
        {
            var normalizedValue = Math.Max(0, value);
            if (_suspendedMatches == normalizedValue)
            {
                return;
            }

            _suspendedMatches = normalizedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSuspended));
        }
    }

    [JsonIgnore]
    public bool NewlySuspendedThisMatch { get; set; }

    [JsonIgnore]
    public bool IsSuspended
    {
        get => SuspendedMatches > 0;
        set => SuspendedMatches = value ? Math.Max(1, SuspendedMatches) : 0;
    }
    public int MatchesPlayedRecently { get; set; }
    public int Finishing { get; set; }
    public int YellowCards { get; set; }
    public bool IsSentOff { get; set; }
    public int? RedCardMinute { get; set; }
    public bool IsOnPitch { get; set; } = true;

    private void SetStamina(double value)
    {
        var normalizedValue = Math.Clamp(value, 0, 100);
        if (Math.Abs(_stamina - normalizedValue) < 0.01)
        {
            return;
        }

        _stamina = normalizedValue;
        OnPropertyChanged(nameof(Stamina));
        OnPropertyChanged(nameof(CurrentStamina));
        OnPropertyChanged(nameof(Fatigue));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
