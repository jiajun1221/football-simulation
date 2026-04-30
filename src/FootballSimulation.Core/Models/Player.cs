using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FootballSimulation.Models;

public class Player : INotifyPropertyChanged
{
    private double _stamina = 100;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public int Fatigue
    {
        get => 100 - (int)Math.Round(Stamina);
        set => SetStamina(100 - value);
    }
    public double LiveMatchModifier { get; set; } = 1.0;
    public bool IsInjured { get; set; }
    public bool IsSuspended { get; set; }
    public int MatchesPlayedRecently { get; set; }
    public int Finishing { get; set; }
    public int YellowCards { get; set; }
    public bool IsSentOff { get; set; }

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
