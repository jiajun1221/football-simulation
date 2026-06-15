using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class PenaltyShootoutView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly GameSessionService _gameSessionService = new();
    private readonly TransferMarketService _transferMarketService = new();
    private readonly Random _random = new();
    private bool _shootoutCompleted;

    public PenaltyShootoutView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadShootout();
    }

    private void LoadShootout()
    {
        var fixture = _state.CurrentFixture;
        var match = _state.CurrentMatch;
        if (fixture is null || match is null)
        {
            ShootoutSummaryTextBlock.Text = "Penalty shootout unavailable.";
            ShootoutDetailTextBlock.Text = "The match state could not be loaded.";
            PrimaryActionButton.Content = "Back to Result";
            return;
        }

        FixtureTextBlock.Text = $"{CompetitionDisplayService.GetName(fixture.Competition)} - {GetFixtureRoundText(fixture)}";
        ShootoutSummaryTextBlock.Text = $"{match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name} after extra time";
        ShootoutDetailTextBlock.Text = "The tie is level after 120 minutes. The penalty shootout will decide who advances.";
    }

    private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shootoutCompleted)
        {
            _navigate(new MatchResultView(_state, _navigate));
            return;
        }

        if (_state.League is null || _state.CurrentFixture is null || _state.CurrentMatch is null)
        {
            _navigate(new MatchResultView(_state, _navigate));
            return;
        }

        var fixture = _state.CurrentFixture;
        var match = _state.CurrentMatch;
        var result = SimulatePenaltyShootout(match.HomeTeam, match.AwayTeam);
        fixture.ExtraTimeHomeScore = match.HomeScore;
        fixture.ExtraTimeAwayScore = match.AwayScore;
        fixture.PenaltyHomeScore = result.HomePenalties;
        fixture.PenaltyAwayScore = result.AwayPenalties;
        fixture.WinningTeamName = result.HomePenalties > result.AwayPenalties ? match.HomeTeam.Name : match.AwayTeam.Name;
        fixture.LosingTeamName = result.HomePenalties > result.AwayPenalties ? match.AwayTeam.Name : match.HomeTeam.Name;
        match.CurrentPhase = MatchPhase.PenaltyShootout;

        _gameSessionService.CompleteSelectedTeamLiveMatch(_state.League, fixture, match);
        RunPostMatchTransferActivity();

        ShootoutSummaryTextBlock.Text =
            $"{fixture.WinningTeamName} win {fixture.PenaltyHomeScore} - {fixture.PenaltyAwayScore} on penalties";
        ShootoutDetailTextBlock.Text =
            $"{match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name} after extra time.";
        PrimaryActionButton.Content = "View Match Result";
        _shootoutCompleted = true;
    }

    private PenaltyResult SimulatePenaltyShootout(Team homeTeam, Team awayTeam)
    {
        var homeStrength = GetPenaltyStrength(homeTeam);
        var awayStrength = GetPenaltyStrength(awayTeam);
        var homePenalties = 0;
        var awayPenalties = 0;

        for (var kick = 1; kick <= 5; kick++)
        {
            if (_random.NextDouble() < GetConversionChance(homeStrength, awayStrength))
            {
                homePenalties++;
            }

            if (_random.NextDouble() < GetConversionChance(awayStrength, homeStrength))
            {
                awayPenalties++;
            }
        }

        while (homePenalties == awayPenalties)
        {
            var homeScores = _random.NextDouble() < GetConversionChance(homeStrength, awayStrength) - 0.03;
            var awayScores = _random.NextDouble() < GetConversionChance(awayStrength, homeStrength) - 0.03;
            if (homeScores)
            {
                homePenalties++;
            }

            if (awayScores)
            {
                awayPenalties++;
            }
        }

        return new PenaltyResult(homePenalties, awayPenalties);
    }

    private static double GetPenaltyStrength(Team team)
    {
        var activePlayers = team.Players.Where(player => player.IsOnPitch && !player.IsSentOff).ToList();
        if (activePlayers.Count == 0)
        {
            activePlayers = team.Players.Take(11).ToList();
        }

        return activePlayers.Count == 0
            ? 70
            : activePlayers.Average(player => player.OverallRating * 0.72 + player.Attack * 0.18 + player.Passing * 0.10);
    }

    private static double GetConversionChance(double takerStrength, double opponentStrength)
    {
        return Math.Clamp(0.74 + (takerStrength - opponentStrength) / 650.0, 0.62, 0.86);
    }

    private void RunPostMatchTransferActivity()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        _transferMarketService.RunAiTransferActivity(
            _state.TransferMarket,
            _state.League,
            _state.SelectedTeam,
            GetFixtureCalendarRound(_state.CurrentFixture));
    }

    private static string GetFixtureRoundText(Fixture fixture)
    {
        return string.IsNullOrWhiteSpace(fixture.RoundName)
            ? $"Round {fixture.RoundNumber}"
            : fixture.RoundName;
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private sealed record PenaltyResult(int HomePenalties, int AwayPenalties);
}
