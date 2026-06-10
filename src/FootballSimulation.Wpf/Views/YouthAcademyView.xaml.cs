using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class YouthAcademyView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly YouthAcademyService _academyService = new();
    private readonly YouthScoutService _scoutService = new();
    private readonly TransferMarketService _transferMarketService = new();
    private readonly SaveGameService _saveGameService = new();
    private YouthPlayerRow? _selectedYouthRow;
    private List<YouthScoutCountry> _scoutCountries = [];
    private List<ScoutFocusOption> _scoutFocusOptions = [];

    public YouthAcademyView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();
        _state = state;
        _navigate = navigate;

        _scoutCountries = _scoutService.GetAvailableCountries().ToList();
        _scoutFocusOptions = _scoutService.GetAvailablePositionFocuses()
            .Select(focus => new ScoutFocusOption(focus, FormatScoutFocus(focus)))
            .ToList();
        YouthTabControl.SelectedIndex = 0;
        AcademySquadTabButton.IsChecked = true;
        LoadAcademy();
    }

    private void LoadAcademy()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        EnsureState();
        var academy = _academyService.GetAcademy(_state.League, _state.SelectedTeam.Name);
        AcademySummaryTextBlock.Text = $"{academy.ClubName} | {academy.AcademyLevel} Academy | Reputation {academy.Reputation} | {academy.YouthPlayers.Count} youth players";
        LoadAcademySquad();
        LoadYouthScout();
    }

    private void EnsureState()
    {
        if (_state.League is null)
        {
            return;
        }

        _academyService.EnsureAcademies(_state.League);
        _scoutService.EnsureScoutNetwork(_state.League);
        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
    }

    private void LoadAcademySquad()
    {
        var academy = GetSelectedAcademy();
        var selectedPlayerId = _selectedYouthRow?.Player.PlayerId;
        var rows = academy.YouthPlayers
            .Where(player => !player.IsPromoted)
            .OrderByDescending(player => player.PotentialMax)
            .ThenByDescending(player => player.CurrentOVR)
            .Select((player, index) => YouthPlayerRow.From(player, academy, index + 1))
            .ToList();
        AcademyPlayersDataGrid.ItemsSource = rows;
        _selectedYouthRow = rows.FirstOrDefault(row => row.Player.PlayerId == selectedPlayerId) ?? rows.FirstOrDefault();
        AcademyPlayersDataGrid.SelectedItem = _selectedYouthRow;
        ShowSelectedYouth();
    }

    private void LoadYouthScout()
    {
        var academy = GetSelectedAcademy();
        _scoutService.EnsureScoutNetwork(academy);
        ScoutAssignmentsItemsControl.ItemsSource = academy.ScoutAssignments
            .OrderBy(assignment => assignment.ScoutId)
            .Select(assignment => ScoutAssignmentRow.From(assignment, _scoutCountries, _scoutFocusOptions))
            .ToList();

        var prospectRows = academy.ScoutReports
            .OrderByDescending(report => report.CreatedRound)
            .ThenByDescending(report => report.CreatedAt)
            .SelectMany(report => report.Prospects
                .OrderByDescending(prospect => prospect.PotentialMax)
                .Select(prospect => ScoutProspectRow.From(report, prospect)))
            .ToList();
        ScoutProspectsDataGrid.ItemsSource = prospectRows;
        YouthScoutStatusTextBlock.Text = prospectRows.Count == 0
            ? "No completed reports yet. Scouts generate reports after 3 completed club matches."
            : $"{prospectRows.Count} scouted prospects available across completed reports.";
    }

    private YouthAcademy GetSelectedAcademy()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            throw new InvalidOperationException("No selected academy is available.");
        }

        return _academyService.GetAcademy(_state.League, _state.SelectedTeam.Name);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        PersistCurrentSaveSlot();
        _navigate(new DashboardView(_state, _navigate));
    }

    private void YouthTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var tabIndex))
        {
            YouthTabControl.SelectedIndex = tabIndex;
        }
    }

    private void YouthTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AcademySquadTabButton.IsChecked = YouthTabControl.SelectedIndex == 0;
        YouthScoutTabButton.IsChecked = YouthTabControl.SelectedIndex == 1;
    }

    private void AcademyPlayersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedYouthRow = AcademyPlayersDataGrid.SelectedItem as YouthPlayerRow;
        ShowSelectedYouth();
    }

    private void ShowSelectedYouth()
    {
        if (_selectedYouthRow is null)
        {
            SelectedYouthEmptyPanel.Visibility = Visibility.Visible;
            SelectedYouthDetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var row = _selectedYouthRow;
        SelectedYouthEmptyPanel.Visibility = Visibility.Collapsed;
        SelectedYouthDetailPanel.Visibility = Visibility.Visible;
        SelectedYouthNameTextBlock.Text = row.Name;
        SelectedYouthFlagImage.Source = CreateImageSource(row.NationalityFlagImagePath);
        SelectedYouthNationalityTextBlock.Text = row.NationalityName;
        SelectedYouthAgeTextBlock.Text = row.Age.ToString(CultureInfo.InvariantCulture);
        SelectedYouthPositionTextBlock.Text = row.Position;
        SelectedYouthOverallTextBlock.Text = row.OverallText;
        SelectedYouthPotentialTextBlock.Text = row.Potential;
        SelectedYouthDevelopmentTextBlock.Text = row.Development;
        SelectedYouthValueTextBlock.Text = row.Value;
        SelectedYouthTraitsTextBlock.Text = row.Traits;
        SelectedYouthScoutReportTextBlock.Text = row.ScoutReport;
    }

    private void PromoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null ||
            AcademyPlayersDataGrid.SelectedItem is not YouthPlayerRow row)
        {
            return;
        }

        var result = _academyService.PromoteYouthPlayer(_state.League, _state.SelectedTeam, row.Player.PlayerId);
        AcademyStatusTextBlock.Text = result.Message;
        LoadAcademy();
        PersistCurrentSaveSlot();
    }

    private void ReleasePlayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null ||
            AcademyPlayersDataGrid.SelectedItem is not YouthPlayerRow row)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            $"Release {row.Name} from the youth academy? This cannot be undone.",
            "Release Youth Player",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _academyService.ReleaseYouthPlayer(_state.League, _state.SelectedTeam, row.Player.PlayerId);
        AcademyStatusTextBlock.Text = result.Message;
        LoadAcademy();
        PersistCurrentSaveSlot();
    }

    private void AssignScoutCountryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ScoutAssignmentRow row })
        {
            return;
        }

        var academy = GetSelectedAcademy();
        var countryName = row.SelectedCountry?.Name ?? row.AssignedCountry;
        var primaryFocus = row.SelectedPrimaryFocus?.Value ?? YouthScoutPositionFocus.AnyPosition;
        var result = _scoutService.AssignScoutingPlan(academy, row.ScoutId, countryName, primaryFocus);
        YouthScoutStatusTextBlock.Text = result.Message;
        LoadYouthScout();
        PersistCurrentSaveSlot();
    }

    private void SignScoutProspectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.TransferMarket is null ||
            ScoutProspectsDataGrid.SelectedItem is not ScoutProspectRow row)
        {
            return;
        }

        var result = _scoutService.SignProspect(
            _state.League,
            _state.TransferMarket,
            _state.SelectedTeam,
            row.ReportId,
            row.ProspectId,
            GetCurrentRound());
        YouthScoutStatusTextBlock.Text = result.Message;
        LoadAcademySquad();
        LoadYouthScout();
        PersistCurrentSaveSlot();
    }

    private int GetCurrentRound()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return 1;
        }

        return _state.League.Fixtures
            .Where(fixture => !fixture.IsPlayed &&
                (fixture.HomeTeam.Name.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase) ||
                    fixture.AwayTeam.Name.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(fixture => fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber)
            .Select(fixture => fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber)
            .FirstOrDefault(1);
    }

    private void PersistCurrentSaveSlot()
    {
        if (!_state.CurrentSaveSlotNumber.HasValue || _state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        var saveData = SaveGameService.CreateSaveData(_state.League, _state.SelectedTeam, _state.TransferMarket);
        _saveGameService.SaveGame(_state.CurrentSaveSlotNumber.Value, saveData);
    }

    private static string FormatMoney(decimal value)
    {
        return value >= 1_000_000m
            ? $"€{value / 1_000_000m:0.#}M"
            : $"€{value / 1_000m:0}K";
    }

    private static string FormatScoutFocus(YouthScoutPositionFocus focus)
    {
        return focus == YouthScoutPositionFocus.AnyPosition ? "None" : focus.ToString();
    }

    private static BitmapImage? CreateImageSource(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var uri = imagePath.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(imagePath, UriKind.Absolute)
            : new Uri($"pack://application:,,,/{imagePath.TrimStart('/')}", UriKind.Absolute);
        return new BitmapImage(uri);
    }

    private sealed class YouthPlayerRow
    {
        public required YouthPlayer Player { get; init; }
        public string Number { get; init; } = string.Empty;
        public string Name => Player.Name;
        public string NationalityFlagImagePath { get; private init; } = string.Empty;
        public string NationalityName { get; private init; } = string.Empty;
        public int Age => Player.Age;
        public string Position => Player.PreferredPosition;
        public int Overall => Player.CurrentOVR;
        public string OverallText => Player.CurrentOVR.ToString(CultureInfo.InvariantCulture);
        public string Potential => $"{Player.PotentialMin}-{Player.PotentialMax}";
        public string Development => Player.DevelopmentRate.ToString();
        public string Personality => Player.Personality.ToString();
        public string Traits => Player.Traits.Count == 0 ? "-" : string.Join(", ", Player.Traits.Take(2));
        public string Value { get; private init; } = string.Empty;
        public string ScoutReport => Player.ScoutReport;
        public string PotentialBadgeText { get; private init; } = string.Empty;
        public string PotentialBadgeBackground { get; private init; } = "#E5E7EB";
        public string PotentialBadgeForeground { get; private init; } = "#374151";
        public string DevelopmentBadgeBackground { get; private init; } = "#E5E7EB";
        public string DevelopmentBadgeForeground { get; private init; } = "#374151";
        public string Status { get; private init; } = "Academy";
        public string StatusTooltip { get; private init; } = "Academy player";
        public string StatusBrush { get; private init; } = "#64748B";
        public string StatusForeground { get; private init; } = "#FFFFFF";

        public static YouthPlayerRow From(YouthPlayer player, YouthAcademy academy, int number)
        {
            var nationality = PlayerNationalityDisplayService.Resolve(player);
            var potentialBadge = CreatePotentialBadge(player);
            var developmentBadge = CreateDevelopmentBadge(player.DevelopmentRate);
            var status = CreateStatus(player);
            return new YouthPlayerRow
            {
                Player = player,
                Number = number.ToString(CultureInfo.InvariantCulture),
                NationalityFlagImagePath = nationality.FlagImagePath,
                NationalityName = nationality.Name,
                Value = FormatMoney(YouthMarketValueCalculator.CalculateMarketValue(player, academy)),
                PotentialBadgeText = potentialBadge.Text,
                PotentialBadgeBackground = potentialBadge.Background,
                PotentialBadgeForeground = potentialBadge.Foreground,
                DevelopmentBadgeBackground = developmentBadge.Background,
                DevelopmentBadgeForeground = developmentBadge.Foreground,
                Status = status.Text,
                StatusTooltip = status.Tooltip,
                StatusBrush = status.Background,
                StatusForeground = status.Foreground
            };
        }

        private static BadgeDisplay CreatePotentialBadge(YouthPlayer player)
        {
            var max = player.PotentialMax;
            return max >= 90
                ? new BadgeDisplay("Elite", "#7C3AED", "#FFFFFF")
                : max >= 85
                    ? new BadgeDisplay("Exciting", "#10B981", "#FFFFFF")
                    : max >= 75
                        ? new BadgeDisplay("Good", "#3B82F6", "#FFFFFF")
                        : new BadgeDisplay("Normal", "#E5E7EB", "#374151");
        }

        private static BadgeDisplay CreateDevelopmentBadge(YouthDevelopmentRate rate)
        {
            return rate switch
            {
                YouthDevelopmentRate.Explosive => new BadgeDisplay(rate.ToString(), "#7C3AED", "#FFFFFF"),
                YouthDevelopmentRate.Fast => new BadgeDisplay(rate.ToString(), "#10B981", "#FFFFFF"),
                YouthDevelopmentRate.Slow => new BadgeDisplay(rate.ToString(), "#F97316", "#FFFFFF"),
                _ => new BadgeDisplay(rate.ToString(), "#E2E8F0", "#334155")
            };
        }

        private static StatusDisplay CreateStatus(YouthPlayer player)
        {
            if (player.IsPromoted)
            {
                return new StatusDisplay("Promoted", "Senior squad player", "#3B82F6", "#FFFFFF");
            }

            if (player.Age >= YouthAcademyService.MinimumPromotionAge &&
                player.CurrentOVR >= YouthAcademyService.MinimumPromotionOverall)
            {
                return new StatusDisplay("Ready", "Ready for senior promotion", "#10B981", "#FFFFFF");
            }

            return new StatusDisplay("Academy", "Developing in the academy", "#64748B", "#FFFFFF");
        }
    }

    private sealed record BadgeDisplay(string Text, string Background, string Foreground);

    private sealed record StatusDisplay(string Text, string Tooltip, string Background, string Foreground);

    private sealed record ProspectStatusDisplay(
        string Text,
        string Tooltip,
        string Background,
        string BorderBrush,
        string Foreground);

    private sealed record ScoutFocusOption(YouthScoutPositionFocus Value, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed class ScoutAssignmentRow
    {
        public string ScoutId { get; init; } = string.Empty;
        public string ScoutName { get; init; } = string.Empty;
        public string Rating { get; init; } = string.Empty;
        public string AssignedCountry { get; init; } = string.Empty;
        public string CountryFlagImagePath { get; init; } = string.Empty;
        public string FocusText { get; init; } = string.Empty;
        public string ScoutSummaryText => $"{Rating} | {FocusText}";
        public int ProgressMatches { get; init; }
        public int RequiredMatches { get; init; }
        public string ProgressText => ProgressMatches >= RequiredMatches
            ? $"Progress: {ProgressMatches} / {RequiredMatches} Matches ✓"
            : $"Progress: {ProgressMatches} / {RequiredMatches} Matches";
        public bool CanAssign => ProgressMatches == 0 || ProgressMatches >= RequiredMatches;
        public string AssignButtonText => "Save Assignment";
        public List<YouthScoutCountry> AvailableCountries { get; init; } = [];
        public YouthScoutCountry? SelectedCountry { get; set; }
        public List<ScoutFocusOption> AvailableFocuses { get; init; } = [];
        public ScoutFocusOption? SelectedPrimaryFocus { get; set; }

        public static ScoutAssignmentRow From(
            YouthScoutAssignment assignment,
            IReadOnlyList<YouthScoutCountry> countries,
            IReadOnlyList<ScoutFocusOption> focusOptions)
        {
            var countryList = countries.ToList();
            var focusList = focusOptions.ToList();
            return new ScoutAssignmentRow
            {
                ScoutId = assignment.ScoutId,
                ScoutName = assignment.ScoutName,
                Rating = FormatScoutRating(assignment.Rating),
                AssignedCountry = assignment.AssignedCountry,
                CountryFlagImagePath = assignment.FlagImagePath,
                FocusText = FormatFocusText(assignment.PrimaryFocus, assignment.SecondaryFocus),
                ProgressMatches = assignment.ProgressMatches,
                RequiredMatches = assignment.RequiredMatches,
                AvailableCountries = countryList,
                SelectedCountry = countryList.FirstOrDefault(country =>
                    country.Name.Equals(assignment.AssignedCountry, StringComparison.OrdinalIgnoreCase)),
                AvailableFocuses = focusList,
                SelectedPrimaryFocus = focusList.FirstOrDefault(option => option.Value == assignment.PrimaryFocus)
            };
        }

        private static string FormatScoutRating(YouthScoutRating rating)
        {
            return rating switch
            {
                YouthScoutRating.EliteScout => "Elite Scout",
                YouthScoutRating.SeniorScout => "Senior Scout",
                YouthScoutRating.RegionalScout => "Regional Scout",
                _ => "Junior Scout"
            };
        }

        private static string FormatFocusText(YouthScoutPositionFocus primaryFocus, YouthScoutPositionFocus secondaryFocus)
        {
            return $"Focus: {FormatScoutFocus(primaryFocus)}";
        }
    }

    private sealed class ScoutProspectRow
    {
        public string ReportId { get; init; } = string.Empty;
        public string ProspectId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ScoutName { get; init; } = string.Empty;
        public string NationalityFlagImagePath { get; init; } = string.Empty;
        public string NationalityName { get; init; } = string.Empty;
        public int Age { get; init; }
        public string Position { get; init; } = string.Empty;
        public int Overall { get; init; }
        public string Potential { get; init; } = string.Empty;
        public string PotentialBadgeText { get; init; } = string.Empty;
        public string PotentialBadgeBackground { get; init; } = "#E5E7EB";
        public string PotentialBadgeForeground { get; init; } = "#374151";
        public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
        public string SigningCost { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string StatusTooltip { get; init; } = string.Empty;
        public string StatusBrush { get; init; } = "#E2E8F0";
        public string StatusBorderBrush { get; init; } = "#CBD5E1";
        public string StatusForeground { get; init; } = "#334155";

        public static ScoutProspectRow From(YouthScoutReport report, YouthScoutProspect prospect)
        {
            var status = CreateStatus(prospect);
            var potentialBadge = CreatePotentialBadge(prospect.PotentialMax);
            return new ScoutProspectRow
            {
                ReportId = report.ReportId,
                ProspectId = prospect.ProspectId,
                Name = prospect.Name,
                ScoutName = report.ScoutName,
                NationalityFlagImagePath = prospect.FlagImagePath,
                NationalityName = prospect.NationalityName,
                Age = prospect.Age,
                Position = prospect.PreferredPosition,
                Overall = prospect.CurrentOVR,
                Potential = $"{prospect.PotentialMin}-{prospect.PotentialMax}",
                PotentialBadgeText = potentialBadge.Text,
                PotentialBadgeBackground = potentialBadge.Background,
                PotentialBadgeForeground = potentialBadge.Foreground,
                TraitBadges = PlayerTraitBadgeHelper.Create(prospect.Traits, maxVisibleTraits: 4),
                SigningCost = FormatMoney(prospect.SigningCost),
                Status = status.Text,
                StatusTooltip = status.Tooltip,
                StatusBrush = status.Background,
                StatusBorderBrush = status.BorderBrush,
                StatusForeground = status.Foreground
            };
        }

        private static ProspectStatusDisplay CreateStatus(YouthScoutProspect prospect)
        {
            return prospect.IsSigned
                ? new ProspectStatusDisplay("Signed", "This prospect has already been signed.", "#E0F2FE", "#7DD3FC", "#075985")
                : new ProspectStatusDisplay("Available", "This prospect is available to sign.", "#DCFCE7", "#86EFAC", "#166534");
        }

        private static BadgeDisplay CreatePotentialBadge(int potentialMax)
        {
            return potentialMax >= 90
                ? new BadgeDisplay("Elite", "#7C3AED", "#FFFFFF")
                : potentialMax >= 85
                    ? new BadgeDisplay("Exciting", "#10B981", "#FFFFFF")
                    : potentialMax >= 75
                        ? new BadgeDisplay("Good", "#3B82F6", "#FFFFFF")
                        : new BadgeDisplay("Normal", "#E5E7EB", "#374151");
        }
    }

}
