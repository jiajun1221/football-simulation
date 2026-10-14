using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Services;

namespace FootballSimulation.Wpf.Views;

public partial class MainMenuView : UserControl
{
    public static readonly DependencyProperty SelectedLeagueProperty = DependencyProperty.Register(
        nameof(SelectedLeague),
        typeof(LeagueOption),
        typeof(MainMenuView),
        new PropertyMetadata(null));

    public IReadOnlyList<LeagueOption> LeagueOptions { get; } =
        new LeagueDataService().LoadLeagueDefinitions()
            .Select(LeagueOption.FromDefinition)
            .ToList();

    public LeagueOption SelectedLeague
    {
        get => (LeagueOption)GetValue(SelectedLeagueProperty);
        private set => SetValue(SelectedLeagueProperty, value);
    }

    private readonly Action<string> _startNewGame;
    private readonly Action _showLoadGame;
    private readonly Action _exit;
    private int _selectedLeagueIndex;

    public MainMenuView(Action<string> startNewGame, Action showLoadGame, Action exit)
    {
        InitializeComponent();
        _startNewGame = startNewGame;
        _showLoadGame = showLoadGame;
        _exit = exit;
        SelectLeague(0);
    }

    private void NewGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SelectedLeague.IsAvailable)
        {
            MessageBox.Show(
                $"{SelectedLeague.Name} is coming soon.",
                "Coming Soon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _startNewGame(SelectedLeague.LeagueId);
    }

    private void LoadGameButton_Click(object sender, RoutedEventArgs e)
    {
        _showLoadGame();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _exit();
    }

    private void PreviousLeagueButton_Click(object sender, RoutedEventArgs e)
    {
        SelectLeague(Math.Max(0, _selectedLeagueIndex - 1));
    }

    private void NextLeagueButton_Click(object sender, RoutedEventArgs e)
    {
        SelectLeague(Math.Min(LeagueOptions.Count - 1, _selectedLeagueIndex + 1));
    }

    private void SelectLeague(int selectedIndex)
    {
        _selectedLeagueIndex = selectedIndex;
        SelectedLeague = LeagueOptions[selectedIndex];
    }

    public sealed class LeagueOption
    {
        public LeagueOption(
            string leagueId,
            string name,
            string logoPath,
            string statusText,
            string statusBrush,
            bool isAvailable)
        {
            LeagueId = leagueId;
            Name = name;
            LogoPath = logoPath;
            StatusText = statusText;
            StatusBrush = ToBrush(statusBrush);
            IsAvailable = isAvailable;
            CardOpacity = isAvailable ? 1.0 : 0.62;
        }

        public string LeagueId { get; }
        public string Name { get; }
        public string LogoPath { get; }
        public string StatusText { get; }
        public Brush StatusBrush { get; }
        public bool IsAvailable { get; }
        public double CardOpacity { get; }

        public static LeagueOption FromDefinition(FootballSimulation.Models.LeagueDefinition definition)
        {
            return new LeagueOption(
                definition.LeagueId,
                definition.Name,
                definition.LogoPath,
                definition.IsAvailable ? "Available" : "Coming soon",
                definition.IsAvailable ? "#22C55E" : "#F97316",
                definition.IsAvailable);
        }

        private static Brush ToBrush(string color)
        {
            return (Brush)new BrushConverter().ConvertFromString(color)!;
        }
    }
}
