using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class TeamSelectionView : UserControl
{
    private const string ClubsAssetPath = "Assets/Clubs";
    private const string DefaultLogoPath = "pack://application:,,,/Assets/Clubs/default.png";

    private static readonly Dictionary<string, string> ImportedLogoFileNames = new()
    {
        ["AFC Bournemouth"] = "AFC Bournemouth.png",
        ["Arsenal"] = "Arsenal FC.png",
        ["Aston Villa"] = "Aston Villa.png",
        ["Brentford"] = "Brentford FC.png",
        ["Brighton & Hove Albion"] = "Brighton Hove Albion.png",
        ["Burnley"] = "Burnley FC.png",
        ["Chelsea"] = "Chelsea FC.png",
        ["Crystal Palace"] = "Crystal Palace.png",
        ["Everton"] = "Everton FC.png",
        ["Fulham"] = "Fulham FC.png",
        ["Leeds United"] = "Leeds United.png",
        ["Liverpool"] = "Liverpool FC.png",
        ["Manchester City"] = "Manchester City.png",
        ["Manchester United"] = "Manchester United.png",
        ["Newcastle United"] = "Newcastle United.png",
        ["Nottingham Forest"] = "Nottingham Forest.png",
        ["Sunderland"] = "Sunderland AFC.png",
        ["Tottenham Hotspur"] = "Tottenham Hotspur.png",
        ["West Ham United"] = "West Ham United.png",
        ["Wolverhampton Wanderers"] = "Wolverhampton Wanderers.png"
    };

    private static readonly Dictionary<string, string> ClubCities = new()
    {
        ["AFC Bournemouth"] = "Bournemouth, Dorset",
        ["Arsenal"] = "London",
        ["Aston Villa"] = "Birmingham",
        ["Brentford"] = "London",
        ["Brighton & Hove Albion"] = "Brighton and Hove",
        ["Burnley"] = "Burnley, Lancashire",
        ["Chelsea"] = "London",
        ["Crystal Palace"] = "London",
        ["Everton"] = "Liverpool",
        ["Fulham"] = "London",
        ["Leeds United"] = "Leeds",
        ["Liverpool"] = "Liverpool",
        ["Manchester City"] = "Manchester",
        ["Manchester United"] = "Manchester",
        ["Newcastle United"] = "Newcastle upon Tyne",
        ["Nottingham Forest"] = "Nottingham",
        ["Sunderland"] = "Sunderland",
        ["Tottenham Hotspur"] = "London",
        ["West Ham United"] = "London",
        ["Wolverhampton Wanderers"] = "Wolverhampton"
    };

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly LeagueDataService _dataService = new();
    private readonly GameSessionService _gameSessionService = new();
    private readonly TransferMarketService _transferMarketService = new();
    private readonly List<ClubSelectionItem> _clubItems = [];
    private readonly LeagueDefinition _leagueDefinition;

    public TeamSelectionView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        _leagueDefinition = _dataService.GetLeagueDefinition(_state.SelectedLeagueId);
        _state.SelectedLeagueDefinition = _leagueDefinition;
        _state.SelectedLeagueId = _leagueDefinition.LeagueId;

        LeagueClubsTitleTextBlock.Text = $"{_leagueDefinition.Name} Clubs";
        _state.Teams = _dataService.LoadTeams(_leagueDefinition);
        _state.League = _gameSessionService.CreateLeague(_leagueDefinition, _state.Teams);
        _state.TransferMarket = _transferMarketService.CreateInitialState(_state.League);

        _clubItems.AddRange(_state.Teams.Select(CreateClubSelectionItem));
        ClubListBox.ItemsSource = _clubItems;

        if (_clubItems.Count > 0)
        {
            ClubListBox.SelectedIndex = 0;
        }
    }

    private void ClubListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClubListBox.SelectedItem is ClubSelectionItem selectedClub)
        {
            UpdateClubPreview(selectedClub);
            StartCareerButton.IsEnabled = true;
        }
    }

    private void StartCareerButton_Click(object sender, RoutedEventArgs e)
    {
        if (ClubListBox.SelectedItem is not ClubSelectionItem selectedClub)
        {
            MessageBox.Show("Please choose a club.");
            return;
        }

        _state.SelectedTeam = selectedClub.Team;
        _navigate(new DashboardView(_state, _navigate));
    }

    private void UpdateClubPreview(ClubSelectionItem selectedClub)
    {
        ClubNameTextBlock.Text = selectedClub.Name;
        StadiumTextBlock.Text = selectedClub.Stadium;
        CityTextBlock.Text = selectedClub.City;
        ClubDescriptionTextBlock.Text = selectedClub.Description;
        SquadRatingTextBlock.Text = selectedClub.SquadRating;
        SelectedClubLogoImage.Source = selectedClub.LogoSource;
    }

    private ClubSelectionItem CreateClubSelectionItem(Team team)
    {
        return new ClubSelectionItem
        {
            Team = team,
            Name = team.Name,
            Stadium = TeamVenueService.GetDisplayVenue(team),
            City = GetClubCity(team.Name),
            SquadRating = CalculateSquadRating(team),
            Description = CreateClubDescription(team),
            LogoSource = ClubLogoService.LoadClubLogo(team.Name, _leagueDefinition.LeagueId)
        };
    }

    public static string GetClubLogoPath(string clubName)
    {
        return ClubLogoService.GetClubLogoPath(clubName);
    }

    public static string CreateClubSlug(string clubName)
    {
        return ClubLogoService.CreateClubSlug(clubName);
    }

    private static BitmapImage? LoadClubLogo(string clubName)
    {
        return ClubLogoService.LoadClubLogo(clubName);
    }

    private static IEnumerable<string> GetLogoCandidatePaths(string clubName)
    {
        yield return GetClubLogoPath(clubName);

        if (ImportedLogoFileNames.TryGetValue(clubName, out string? importedFileName))
        {
            yield return CreatePackPath(importedFileName);
        }

        yield return DefaultLogoPath;
    }

    private static string CreatePackPath(string fileName)
    {
        string escapedFileName = Uri.EscapeDataString(fileName);
        return $"pack://application:,,,/{ClubsAssetPath}/{escapedFileName}";
    }

    private static bool ResourceExists(string packUri)
    {
        try
        {
            return Application.GetResourceStream(new Uri(packUri, UriKind.Absolute)) is not null;
        }
        catch
        {
            return false;
        }
    }

    private string CreateClubDescription(Team team)
    {
        var formation = string.IsNullOrWhiteSpace(team.Formation) ? "flexible" : team.Formation;
        var squadSize = team.Players.Count + team.Substitutes.Count;
        var city = GetClubCity(team.Name);

        return $"Shape a {formation} system with a {squadSize}-player squad. Build your {_leagueDefinition.Name} career from {city}.";
    }

    private string GetClubCity(string clubName)
    {
        return ClubCities.TryGetValue(clubName, out var city) ? city : _leagueDefinition.Country;
    }

    private static string CalculateSquadRating(Team team)
    {
        List<Player> players = team.Players.Count > 0 ? team.Players : team.Substitutes;

        if (players.Count == 0)
        {
            return "N/A";
        }

        double averageRating = players.Average(GetPlayerRating);
        return Math.Round(averageRating).ToString(CultureInfo.InvariantCulture);
    }

    private static double GetPlayerRating(Player player)
    {
        if (player.OverallRating > 0)
        {
            return player.OverallRating;
        }

        return (player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0;
    }

    public sealed class ClubSelectionItem
    {
        public required Team Team { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Stadium { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string SquadRating { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public BitmapImage? LogoSource { get; init; }
    }
}
