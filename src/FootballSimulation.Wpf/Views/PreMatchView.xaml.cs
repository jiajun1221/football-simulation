using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class PreMatchView : UserControl
{
    private static readonly string[] SupportedFormations = ["4-3-3", "4-2-3-1", "4-4-2", "3-5-2"];

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly FormationLayoutService _formationLayoutService = new();
    private readonly TacticalInsightService _tacticalInsightService = new();
    private readonly SquadSelectionService _squadSelectionService = new();

    private Player? _selectedStarter;
    private Player? _selectedSubstitute;
    private bool _isLoadingSetup;
    private bool _areTacticsEventsWired;

    public PreMatchView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadPreMatch();
    }

    private void LoadPreMatch()
    {
        if (_state.SelectedTeam is null || _state.CurrentFixture is null)
        {
            return;
        }

        _isLoadingSetup = true;

        FixtureTextBlock.Text = $"{_state.CurrentFixture.HomeTeam.Name} vs {_state.CurrentFixture.AwayTeam.Name}";
        LoadFormationSelector(_state.SelectedTeam);
        LoadTactics(_state.SelectedTeam.Tactics);
        WireTacticsEvents();
        RefreshSubstitutes();
        RenderPitch();

        _isLoadingSetup = false;
        RefreshTacticalInsight();
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

    private void WireTacticsEvents()
    {
        if (_areTacticsEventsWired)
        {
            return;
        }

        PressingSlider.ValueChanged += TacticsSlider_Changed;
        WidthSlider.ValueChanged += TacticsSlider_Changed;
        TempoSlider.ValueChanged += TacticsSlider_Changed;
        DefensiveLineSlider.ValueChanged += TacticsSlider_Changed;
        _areTacticsEventsWired = true;
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

            Canvas.SetLeft(button, (PitchCanvas.ActualWidth * position.X) - 68);
            Canvas.SetTop(button, (PitchCanvas.ActualHeight * position.Y) - 44);
            PitchCanvas.Children.Add(button);
        }
    }

    private Button CreatePlayerButton(Player player)
    {
        var button = new Button
        {
            Width = 136,
            Height = 88,
            Tag = player,
            Content = CreatePitchPlayerCard(player),
            ContentTemplate = (DataTemplate)FindResource("PitchPlayerCardTemplate"),
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
        var form = GetFormBadge(player.CurrentForm);

        return new PitchPlayerCard
        {
            Player = player,
            ShirtNumberText = player.SquadNumber > 0 ? $"#{player.SquadNumber}" : string.Empty,
            PlayerName = player.Name,
            PositionText = GetPositionText(player.Position),
            OverallText = $"OVR {GetOverallRating(player)}",
            FatigueText = $"{fatigue}% fatigue",
            FatigueBarWidth = GetFatigueBarWidth(fatigue),
            FatigueBrush = GetFatigueBrush(fatigue),
            FormBadgeText = form.Text,
            FormBadgeBackground = form.Background,
            FormBadgeForeground = form.Foreground,
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
        RefreshTacticalInsight();
    }

    private void TacticsControl_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshTacticalInsight();
    }

    private void TacticsSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshTacticalInsight();
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
            _selectedSubstitute);

        if (!swapResult.Success)
        {
            MessageBox.Show(swapResult.Message);
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
        SubstituteStatusTextBlock.Text = _state.SelectedTeam.Substitutes.Count == 0
            ? "No substitutes are available yet. Add JSON players with isStarter set to false to enable swaps."
            : "Select a substitute, then choose a starter on the pitch and click Swap.";
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
            $"Overall: {GetOverallRating(_selectedStarter)}\n" +
            $"Form: {_selectedStarter.CurrentForm} | Recent: {GetRecentPlayerFormText(_selectedStarter)}\n" +
            $"Fatigue: {GetFatiguePercentage(_selectedStarter)}% | Injury: {(_selectedStarter.IsInjured ? "Injured" : "Fit")}\n" +
            $"Attack: {_selectedStarter.Attack} | Defense: {_selectedStarter.Defense}\n" +
            $"Passing: {_selectedStarter.Passing} | Finishing: {_selectedStarter.Finishing}\n" +
            $"Stamina: {_selectedStarter.CurrentStamina:0}/{_selectedStarter.Stamina}";
    }

    private string GetRecentPlayerFormText(Player player)
    {
        if (_state.League is null)
        {
            return "No recent match data yet";
        }

        var ratings = _state.League.Fixtures
            .Where(fixture => fixture.IsPlayed && fixture.Result is not null)
            .OrderByDescending(fixture => fixture.RoundNumber)
            .SelectMany(fixture => fixture.Result!.PlayerPerformances)
            .Where(performance => performance.PlayerName == player.Name)
            .Take(5)
            .Select(performance => performance.Rating.ToString("0.0"))
            .ToList();

        return ratings.Count == 0
            ? "No recent match data yet"
            : string.Join(" / ", ratings);
    }

    private void RefreshTacticalInsight()
    {
        if (_isLoadingSetup || _state.SelectedTeam is null || _state.CurrentFixture is null || TacticalInsightTextBlock is null)
        {
            return;
        }

        SaveSetup(_state.SelectedTeam);

        var opponent = _state.CurrentFixture.HomeTeam == _state.SelectedTeam
            ? _state.CurrentFixture.AwayTeam
            : _state.CurrentFixture.HomeTeam;
        var insight = _tacticalInsightService.GenerateInsight(_state.SelectedTeam, opponent);

        TacticalInsightTextBlock.Text =
            $"Opponent threats:\n{FormatInsightItems(insight.OpponentThreats)}\n\n" +
            $"Likely tactics:\n{FormatInsightItems(insight.LikelyTactics)}\n\n" +
            $"Warnings:\n{FormatInsightItems(insight.Warnings)}\n\n" +
            $"Recommendations:\n{FormatInsightItems(insight.Recommendations)}";
    }

    private static string FormatInsightItems(IEnumerable<string> items)
    {
        var itemList = items.ToList();
        return itemList.Count == 0
            ? "- No major issue identified."
            : string.Join(Environment.NewLine, itemList.Select(item => $"- {item}"));
    }

    private static int GetOverallRating(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
    }

    private static string GetPositionText(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "DEF",
            Position.Midfielder => "MID",
            Position.Forward => "FWD",
            _ => position.ToString()
        };
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

    private static double GetFatigueBarWidth(int fatiguePercentage)
    {
        const double fullBarWidth = 118;
        var freshnessPercentage = 100 - Math.Clamp(fatiguePercentage, 0, 100);

        return fullBarWidth * freshnessPercentage / 100.0;
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

    private static FormBadge GetFormBadge(int currentForm)
    {
        return currentForm switch
        {
            >= 80 => new FormBadge("Hot", "#D9F1E1", "#236B39"),
            >= 65 => new FormBadge("Good", "#E7F7EA", "#2F7D42"),
            >= 40 => new FormBadge("Average", "#FFF0A3", "#5F4500"),
            > 0 => new FormBadge("Poor", "#FFD1D1", "#8F1F1F"),
            _ => new FormBadge("Inactive", "#E1E5EA", "#465364")
        };
    }

    private void UpdateSwapButton()
    {
        SwapButton.IsEnabled = _selectedStarter is not null && _selectedSubstitute is not null;
    }

    private void PitchCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderPitch();
    }

    private void StartFirstHalfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is null)
        {
            MessageBox.Show("No team has been selected.");
            return;
        }

        SaveSetup(_state.SelectedTeam);
        _navigate(new MatchLiveView(_state, _navigate, isSecondHalf: false));
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

    private sealed record FormBadge(string Text, string Background, string Foreground);
}
