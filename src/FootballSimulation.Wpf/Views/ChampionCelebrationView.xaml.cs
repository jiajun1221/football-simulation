using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class ChampionCelebrationView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly Func<UserControl> _nextViewFactory;
    private readonly TrophyCelebrationEvent _celebrationEvent;

    public ChampionCelebrationView(GameFlowState state, Action<UserControl> navigate)
        : this(state, navigate, () => new EndSeasonResultView(state, navigate))
    {
    }

    public ChampionCelebrationView(GameFlowState state, Action<UserControl> navigate, Func<UserControl> nextViewFactory)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        _nextViewFactory = nextViewFactory;
        _celebrationEvent = _state.TrophyCelebrationQueue.Count > 0
            ? _state.TrophyCelebrationQueue.Dequeue()
            : CreateFallbackEvent();

        LoadCelebration();
    }

    private void LoadCelebration()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        var accentBrush = CreateBrush(_celebrationEvent.AccentColor, "#FACC15");
        var themeColor = CreateColor(_celebrationEvent.ThemeColor, "#07110B");

        CelebrationCardBorder.Background = new SolidColorBrush(Color.FromArgb(214, themeColor.R, themeColor.G, themeColor.B));
        CelebrationCardBorder.BorderBrush = accentBrush;
        AccentGradientStop.Color = CreateColor(_celebrationEvent.AccentColor, "#FACC15", 0x66);
        InfoBorder.Background = new SolidColorBrush(CreateColor(_celebrationEvent.AccentColor, "#FACC15", 0x33));
        InfoBorder.BorderBrush = accentBrush;
        NextButton.Background = accentBrush;
        NextButton.BorderBrush = accentBrush;

        CelebrationTitleTextBlock.Text = _celebrationEvent.CelebrationTitle;
        CelebrationTitleTextBlock.Foreground = accentBrush;
        ClubNameTextBlock.Text = _celebrationEvent.ClubName;
        SeasonTextBlock.Text = $"Season {_celebrationEvent.Season}";
        SeasonTextBlock.Foreground = accentBrush;
        CelebrationTextBlock.Text = _celebrationEvent.Message;
        CelebrationSubtitleTextBlock.Text = _celebrationEvent.CelebrationSubtitle;
        BackgroundImageBrush.ImageSource = CreateImageSource(
            _celebrationEvent.BackgroundImagePath,
            "pack://application:,,,/Assets/Backgrounds/main-menu-stadium.png");
        TrophyImage.Source = CreateImageSource(
            TrophyCelebrationService.ResolveTrophyImagePath(
                _celebrationEvent.TrophyImagePath,
                _celebrationEvent.CompetitionName),
            TrophyCelebrationService.DefaultTrophyImagePath);
        ClubLogoImage.Source = CreateImageSource(ClubLogoService.GetClubLogoPath(_celebrationEvent.ClubName, _celebrationEvent.LeagueId));
        if (_celebrationEvent.CompetitionId.StartsWith("league-", StringComparison.OrdinalIgnoreCase))
        {
            _state.League.HasShownLeagueTrophyCelebration = true;
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(_state.TrophyCelebrationQueue.Count > 0
            ? new ChampionCelebrationView(_state, _navigate, _nextViewFactory)
            : _nextViewFactory());
    }

    private static BitmapImage? CreateImageSource(string path, string? fallbackPath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.IsNullOrWhiteSpace(fallbackPath) ? null : CreateImageSource(fallbackPath);
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return string.IsNullOrWhiteSpace(fallbackPath) || fallbackPath.Equals(path, StringComparison.OrdinalIgnoreCase)
                ? null
                : CreateImageSource(fallbackPath);
        }
    }

    private TrophyCelebrationEvent CreateFallbackEvent()
    {
        var league = _state.League;
        var selectedTeam = _state.SelectedTeam;
        var leagueId = league?.LeagueId ?? _state.SelectedLeagueId;
        var clubName = selectedTeam?.Name ?? "Your Club";
        var season = league?.Season ?? string.Empty;
        return new TrophyCelebrationEvent(
            $"{season}:fallback",
            $"league-{leagueId}",
            league?.Name ?? "League",
            CompetitionType.PremierLeague,
            leagueId,
            clubName,
            season,
            TrophyCelebrationService.DefaultTrophyImagePath,
            "pack://application:,,,/Assets/Backgrounds/main-menu-stadium.png",
            "#07110B",
            "#FACC15",
            $"{league?.Name ?? "League"} Champions",
            "Enjoy the lap of honour before reviewing the full season.",
            $"{clubName} lifted the {league?.Name ?? "league"} trophy!",
            TrophyCelebrationNextRoute.SeasonOverview);
    }

    private static Brush CreateBrush(string color, string fallbackColor)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(color)!;
        }
        catch
        {
            return (Brush)new BrushConverter().ConvertFromString(fallbackColor)!;
        }
    }

    private static Color CreateColor(string color, string fallbackColor, byte? alpha = null)
    {
        Color parsed;
        try
        {
            parsed = (Color)ColorConverter.ConvertFromString(color);
        }
        catch
        {
            parsed = (Color)ColorConverter.ConvertFromString(fallbackColor);
        }

        return alpha.HasValue ? Color.FromArgb(alpha.Value, parsed.R, parsed.G, parsed.B) : parsed;
    }
}
