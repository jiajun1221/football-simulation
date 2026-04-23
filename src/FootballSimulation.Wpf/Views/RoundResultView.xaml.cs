using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class RoundResultView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;

    public RoundResultView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadRoundResult();
    }

    private void LoadRoundResult()
    {
        if (_state.League is null || _state.CurrentFixture is null)
        {
            return;
        }

        TitleTextBlock.Text = $"Round {_state.CurrentFixture.RoundNumber} Results";
        RoundResultsListBox.ItemsSource = _state.League.Fixtures
            .Where(fixture => fixture.RoundNumber == _state.CurrentFixture.RoundNumber)
            .Select(FormatRoundResult)
            .ToList();
        LeagueTableDataGrid.ItemsSource = _state.League.Table;
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _state.CurrentFixture = null;
        _state.CurrentMatch = null;
        _navigate(new DashboardView(_state, _navigate));
    }

    private static string FormatRoundResult(Fixture fixture)
    {
        if (fixture.Result is null)
        {
            return $"{fixture.HomeTeam.Name} vs {fixture.AwayTeam.Name}";
        }

        return $"{fixture.HomeTeam.Name} {fixture.Result.HomeScore} - {fixture.Result.AwayScore} {fixture.AwayTeam.Name}";
    }
}
