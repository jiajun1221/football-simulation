using System.ComponentModel;
using System.Runtime.CompilerServices;
using FootballSimulation.Wpf.Helpers;

namespace FootballSimulation.Wpf.Models;

public sealed class LivePlayerStats : INotifyPropertyChanged
{
    private const double PitchStaminaBarWidth = 42;

    private double _currentRating = 6.0;
    private int _staminaPercent = 100;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PlayerId { get; init; } = string.Empty;
    public string TeamName { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;

    public double CurrentRating
    {
        get => _currentRating;
        private set
        {
            var normalizedRating = Math.Round(Math.Clamp(value, 1.0, 10.0), 1);
            if (Math.Abs(_currentRating - normalizedRating) < 0.01)
            {
                return;
            }

            _currentRating = normalizedRating;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RatingDisplay));
            OnPropertyChanged(nameof(RatingBrush));
            OnPropertyChanged(nameof(RatingForeground));
        }
    }

    public string RatingDisplay => RatingDisplayHelper.CreateRatingText(CurrentRating);
    public string RatingBrush => RatingDisplayHelper.GetRatingBrush(CurrentRating);
    public string RatingForeground => RatingDisplayHelper.GetRatingForeground(CurrentRating);
    public int StaminaPercent
    {
        get => _staminaPercent;
        private set
        {
            var normalizedStamina = Math.Clamp(value, 0, 100);
            if (_staminaPercent == normalizedStamina)
            {
                return;
            }

            _staminaPercent = normalizedStamina;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StaminaBarWidth));
            OnPropertyChanged(nameof(StaminaBrush));
        }
    }

    public double StaminaBarWidth => PitchStaminaBarWidth;
    public string StaminaBrush => StaminaPercent switch
    {
        < 30 => "#EF4444",
        < 50 => "#FB923C",
        <= 70 => "#FACC15",
        _ => "#86EFAC"
    };

    public void SetCurrentRating(double rating)
    {
        CurrentRating = rating;
    }

    public void SetStaminaPercent(int staminaPercent)
    {
        StaminaPercent = staminaPercent;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
