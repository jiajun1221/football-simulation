using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FootballSimulation.Wpf.Views;

public partial class MainMenuView : UserControl
{
    public static readonly DependencyProperty SelectedLeagueProperty = DependencyProperty.Register(
        nameof(SelectedLeague),
        typeof(LeagueOption),
        typeof(MainMenuView),
        new PropertyMetadata(null));

    public IReadOnlyList<LeagueOption> LeagueOptions { get; } =
    [
        new("Premier League", "/Assets/Leagues/premier-league.png", "Available", "#22C55E", true),
        new("LaLiga", "/Assets/Leagues/laliga.png", "Coming soon", "#F97316", false),
        new("Serie A", "/Assets/Leagues/serie-a.png", "Coming soon", "#F97316", false),
        new("Bundesliga", "/Assets/Leagues/bundesliga.png", "Coming soon", "#F97316", false),
        new("Ligue 1", "/Assets/Leagues/ligue-1.png", "Coming soon", "#F97316", false)
    ];

    public LeagueOption SelectedLeague
    {
        get => (LeagueOption)GetValue(SelectedLeagueProperty);
        private set => SetValue(SelectedLeagueProperty, value);
    }

    private readonly Action _startNewGame;
    private readonly Action _showLoadGame;
    private readonly Action _exit;
    private int _selectedLeagueIndex;

    public MainMenuView(Action startNewGame, Action showLoadGame, Action exit)
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

        _startNewGame();
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
            string name,
            string logoPath,
            string statusText,
            string statusBrush,
            bool isAvailable)
        {
            Name = name;
            LogoPath = logoPath;
            StatusText = statusText;
            StatusBrush = ToBrush(statusBrush);
            IsAvailable = isAvailable;
            CardOpacity = isAvailable ? 1.0 : 0.62;
        }

        public string Name { get; }
        public string LogoPath { get; }
        public string StatusText { get; }
        public Brush StatusBrush { get; }
        public bool IsAvailable { get; }
        public double CardOpacity { get; }

        private static Brush ToBrush(string color)
        {
            return (Brush)new BrushConverter().ConvertFromString(color)!;
        }
    }
}
