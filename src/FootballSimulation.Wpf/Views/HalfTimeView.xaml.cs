using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class HalfTimeView : UserControl
{
    private static readonly string[] SupportedFormations = ["4-3-3", "4-2-3-1", "4-4-2", "3-5-2"];

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly SquadSelectionService _squadSelectionService = new();

    private Player? _selectedStarter;
    private Player? _selectedSubstitute;

    public HalfTimeView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadHalfTime();
    }

    private void LoadHalfTime()
    {
        if (_state.CurrentMatch is not null)
        {
            ScoreTextBlock.Text = $"{_state.CurrentMatch.HomeTeam.Name} {_state.CurrentMatch.HomeScore} - {_state.CurrentMatch.AwayScore} {_state.CurrentMatch.AwayTeam.Name}";
        }

        if (_state.SelectedTeam is null)
        {
            return;
        }

        LoadFormationSelector(_state.SelectedTeam);
        LoadTactics(_state.SelectedTeam.Tactics);
        RefreshSubstitutes();
        RenderPitch();
    }

    private void LoadFormationSelector(Team team)
    {
        FormationComboBox.ItemsSource = SupportedFormations;
        FormationComboBox.SelectedItem = SupportedFormations.Contains(team.Formation)
            ? team.Formation
            : "4-3-3";
    }

    private void LoadTactics(TeamTactics tactics)
    {
        MentalityComboBox.ItemsSource = Enum.GetValues<Mentality>();
        MentalityComboBox.SelectedItem = tactics.Mentality;
        PressingSlider.Value = tactics.PressingIntensity;
        WidthSlider.Value = tactics.Width;
        TempoSlider.Value = tactics.Tempo;
        DefensiveLineSlider.Value = tactics.DefensiveLine;
    }

    private void RenderPitch()
    {
        if (_state.SelectedTeam is null || PitchCanvas.ActualWidth <= 0 || PitchCanvas.ActualHeight <= 0)
        {
            return;
        }

        PitchCanvas.Children.Clear();

        var formation = FormationComboBox.SelectedItem as string ?? _state.SelectedTeam.Formation;
        var positions = _formationLayoutService.GetPositions(formation);
        var players = OrderPlayersForPitch(_state.SelectedTeam.Players).ToList();

        for (var index = 0; index < players.Count && index < positions.Count; index++)
        {
            var player = players[index];
            var position = positions[index];
            var button = CreatePlayerButton(player);

            Canvas.SetLeft(button, (PitchCanvas.ActualWidth * position.X) - 64);
            Canvas.SetTop(button, (PitchCanvas.ActualHeight * position.Y) - 38);
            PitchCanvas.Children.Add(button);
        }
    }

    private Button CreatePlayerButton(Player player)
    {
        var button = new Button
        {
            Width = 128,
            Height = 76,
            Tag = player,
            Content = CreatePitchPlayerCard(player),
            ContentTemplate = (DataTemplate)FindResource("HalfTimePlayerCardTemplate"),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };

        button.Click += PlayerButton_Click;
        return button;
    }

    private PitchPlayerCard CreatePitchPlayerCard(Player player)
    {
        var fatigue = GetFatiguePercentage(player);
        var performance = GetPerformance(player);

        return new PitchPlayerCard
        {
            Player = player,
            PlayerName = player.SquadNumber > 0 ? $"{player.SquadNumber}. {player.Name}" : player.Name,
            RatingText = performance is null ? "6.0" : performance.Rating.ToString("0.0"),
            MatchStatsText = performance is null
                ? $"{player.Position}"
                : $"G {performance.Goals} A {performance.Assists} Sh {performance.Shots}",
            FatigueText = $"{fatigue}%",
            FatigueBarWidth = 86 * (100 - fatigue) / 100.0,
            FatigueBrush = GetFatigueBrush(fatigue),
            CardBackground = player == _selectedStarter ? "#FFF2B8" : "#FFFFFF",
            CardBorderBrush = player == _selectedStarter ? "#F6C343" : "#102033",
            CardBorderThickness = player == _selectedStarter ? new Thickness(3) : new Thickness(1)
        };
    }

    private void PlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Player player })
        {
            _selectedStarter = player;
            UpdateSelectedPlayerDetails();
            UpdateSwapButton();
            RenderPitch();
        }
    }

    private void FormationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_state.SelectedTeam is not null && FormationComboBox.SelectedItem is string formation)
        {
            _state.SelectedTeam.Formation = formation;
        }

        RenderPitch();
    }

    private void SubstituteListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedSubstitute = SubstituteListBox.SelectedItem as Player;
        UpdateSwapButton();
    }

    private void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is null || _selectedStarter is null || _selectedSubstitute is null)
        {
            return;
        }

        var starterIndex = _state.SelectedTeam.Players.IndexOf(_selectedStarter);
        if (starterIndex < 0)
        {
            return;
        }

        var swapResult = _squadSelectionService.SwapStarterWithSubstitute(
            _state.SelectedTeam,
            _selectedStarter,
            _selectedSubstitute,
            _state.CurrentMatch,
            substitutionMinute: 45);

        if (!swapResult.Success)
        {
            MessageBox.Show(swapResult.Message);
            RefreshSubstitutes();
            UpdateSwapButton();
            return;
        }

        _selectedStarter = _selectedSubstitute;
        _selectedSubstitute = null;
        RefreshSubstitutes();
        UpdateSelectedPlayerDetails();
        UpdateSwapButton();
        RenderPitch();
    }

    private void RefreshSubstitutes()
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        SubstituteListBox.ItemsSource = null;
        SubstituteListBox.ItemsSource = _state.SelectedTeam.Substitutes;
        SubstituteListBox.IsEnabled = _state.SelectedTeam.Substitutes.Count > 0;
        var usedSubstitutions = _state.CurrentMatch is null
            ? 0
            : _squadSelectionService.CountTeamSubstitutions(_state.CurrentMatch, _state.SelectedTeam.Name);

        SubstituteStatusTextBlock.Text = _state.SelectedTeam.Substitutes.Count == 0
            ? "No substitutes are available yet. Add JSON players with isStarter set to false to enable swaps."
            : $"{usedSubstitutions}/5 substitutions used. Select a substitute, then choose a starter and click Swap.";
    }

    private void UpdateSelectedPlayerDetails()
    {
        if (_selectedStarter is null)
        {
            SelectedPlayerTextBlock.Text = "Select a player on the pitch.";
            return;
        }

        SelectedPlayerTextBlock.Text =
            $"{_selectedStarter.Name}\n" +
            $"Position: {_selectedStarter.Position}\n" +
            $"Overall: {GetOverallRating(_selectedStarter)} | Match Rating: {GetPerformance(_selectedStarter)?.Rating.ToString("0.0") ?? "6.0"}\n" +
            $"Fatigue: {GetFatiguePercentage(_selectedStarter)}% | Injured: {(_selectedStarter.IsInjured ? "Yes" : "No")}\n" +
            $"Attack: {_selectedStarter.Attack} | Defense: {_selectedStarter.Defense}\n" +
            $"Passing: {_selectedStarter.Passing} | Finishing: {_selectedStarter.Finishing}\n" +
            $"Stamina: {_selectedStarter.CurrentStamina:0}/{_selectedStarter.Stamina}\n" +
            $"{GetHalftimeAdvice(_selectedStarter)}";
    }

    private static int GetOverallRating(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
    }

    private PlayerMatchPerformance? GetPerformance(Player player)
    {
        return _state.CurrentMatch?.PlayerPerformances
            .FirstOrDefault(performance => performance.PlayerName == player.Name);
    }

    private static int GetFatiguePercentage(Player player)
    {
        if (player.Fatigue > 0)
        {
            return Math.Clamp(player.Fatigue, 0, 100);
        }

        if (player.Stamina <= 0)
        {
            return 100;
        }

        var staminaRatio = Math.Clamp(player.CurrentStamina / player.Stamina, 0.0, 1.0);
        return (int)Math.Round((1.0 - staminaRatio) * 100);
    }

    private static string GetFatigueBrush(int fatiguePercentage)
    {
        return fatiguePercentage switch
        {
            <= 25 => "#2FA84F",
            <= 50 => "#E3BC26",
            <= 75 => "#E8872E",
            _ => "#D94343"
        };
    }

    private string GetHalftimeAdvice(Player player)
    {
        var performance = GetPerformance(player);
        var fatigue = GetFatiguePercentage(player);

        if (fatigue >= 75)
        {
            return "Advice: exhausted. Consider substituting or lowering pressing.";
        }

        if (performance is not null && performance.Rating < 5.8)
        {
            return "Advice: struggling. Consider a role or formation adjustment.";
        }

        if (performance is not null && performance.Rating >= 7.2)
        {
            return "Advice: playing well. Keep them involved.";
        }

        return "Advice: stable option for the second half.";
    }

    private void UpdateSwapButton()
    {
        var usedSubstitutions = _state.CurrentMatch is null || _state.SelectedTeam is null
            ? 0
            : _squadSelectionService.CountTeamSubstitutions(_state.CurrentMatch, _state.SelectedTeam.Name);

        SwapButton.IsEnabled = _selectedStarter is not null &&
            _selectedSubstitute is not null &&
            usedSubstitutions < 5;
    }

    private void PitchCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderPitch();
    }

    private void StartSecondHalfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is not null)
        {
            SaveSetup(_state.SelectedTeam);
        }

        _navigate(new MatchLiveView(_state, _navigate, isSecondHalf: true));
    }

    private void SaveSetup(Team team)
    {
        if (FormationComboBox.SelectedItem is string formation)
        {
            team.Formation = formation;
        }

        if (MentalityComboBox.SelectedItem is Mentality mentality)
        {
            team.Tactics.Mentality = mentality;
        }

        team.Tactics.PressingIntensity = (int)Math.Round(PressingSlider.Value);
        team.Tactics.Width = (int)Math.Round(WidthSlider.Value);
        team.Tactics.Tempo = (int)Math.Round(TempoSlider.Value);
        team.Tactics.DefensiveLine = (int)Math.Round(DefensiveLineSlider.Value);
    }

    private static IEnumerable<Player> OrderPlayersForPitch(IEnumerable<Player> players)
    {
        return players
            .OrderBy(player => player.Position switch
            {
                Position.Goalkeeper => 0,
                Position.Defender => 1,
                Position.Midfielder => 2,
                Position.Forward => 3,
                _ => 4
            })
            .ThenBy(player => player.SquadNumber)
            .ThenBy(player => player.Name);
    }
}
