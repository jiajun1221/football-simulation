using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class ChampionCelebrationView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;

    public ChampionCelebrationView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadCelebration();
    }

    private void LoadCelebration()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        ClubNameTextBlock.Text = _state.SelectedTeam.Name;
        SeasonTextBlock.Text = $"Season {_state.League.Season}";
        CelebrationTextBlock.Text = $"{_state.SelectedTeam.Name} lifted the Premier League trophy!";
        ClubLogoImage.Source = CreateImageSource(ClubLogoService.GetClubLogoPath(_state.SelectedTeam.Name, _state.League.LeagueId));
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(new EndSeasonResultView(_state, _navigate));
    }

    private static BitmapImage? CreateImageSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new BitmapImage(new Uri(path, UriKind.Absolute));
    }
}
