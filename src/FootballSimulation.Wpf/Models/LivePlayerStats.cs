using System.ComponentModel;
using System.Runtime.CompilerServices;
using FootballSimulation.Wpf.Helpers;

namespace FootballSimulation.Wpf.Models;

public sealed class LivePlayerStats : INotifyPropertyChanged
{
    private double _currentRating = 6.0;

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

    public void SetCurrentRating(double rating)
    {
        CurrentRating = rating;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
