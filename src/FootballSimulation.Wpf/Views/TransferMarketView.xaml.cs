using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class TransferMarketView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly TransferMarketService _transferMarketService = new();
    private TransferPlayerListing? _selectedListing;
    private TransferOffer? _selectedOffer;
    private TransferHistoryItem? _selectedHistoryItem;
    private TransferPlayerDetailPanel? _activeDetailPanel;

    public TransferMarketView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();
        _state = state;
        _navigate = navigate;
        EnsureTransferState();
        InitializeFilterControls();
        LoadMarket();
        TransferTabControl.SelectedIndex = 0;
        SyncTransferTabButtons();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(new DashboardView(_state, _navigate));
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMarketSearch();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchNameTextBox.Text = string.Empty;
        SearchClubTextBox.Text = string.Empty;
        SearchLeagueComboBox.SelectedIndex = 0;
        SearchPositionComboBox.SelectedIndex = 0;
        SearchMinOvrComboBox.SelectedIndex = 0;
        SearchMaxPriceComboBox.SelectedIndex = 0;
        SearchMinAgeComboBox.SelectedIndex = 0;
        SearchMaxAgeComboBox.SelectedIndex = 0;
        SearchFormComboBox.SelectedIndex = 0;
        SearchTraitTextBox.Text = string.Empty;
        RefreshMarketSearch();
    }

    private void MoreFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        var showAdvanced = AdvancedFiltersPanel.Visibility != Visibility.Visible;
        AdvancedFiltersPanel.Visibility = showAdvanced ? Visibility.Visible : Visibility.Collapsed;
        MoreFiltersButton.Content = showAdvanced ? "Less Filters" : "More Filters";
    }

    private void TransferTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabIndex))
        {
            TransferTabControl.SelectedIndex = tabIndex;
        }
    }

    private void TransferTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender != TransferTabControl)
        {
            return;
        }

        SyncTransferTabButtons();
    }

    private void SyncTransferTabButtons()
    {
        var buttons = new[]
        {
            SquadSegmentButton,
            MarketSegmentButton,
            RecommendedSegmentButton,
            OffersSegmentButton,
            HistorySegmentButton
        };

        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].IsChecked = index == TransferTabControl.SelectedIndex;
        }
    }

    private void MarketDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MarketDataGrid.SelectedItem is not TransferPlayerRow row)
        {
            return;
        }

        _selectedListing = row.Listing;
        ShowPlayerDetails(row.Listing, TransferDetailMode.Market, panel: MarketDetailPanel);
    }

    private void RecommendedDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecommendedDataGrid.SelectedItem is not TransferPlayerRow row)
        {
            RecommendedDetailPanel.ShowEmpty();
            return;
        }

        _selectedListing = row.Listing;
        ShowPlayerDetails(row.Listing, TransferDetailMode.Scout, row.Reason, panel: RecommendedDetailPanel);
    }

    private void ShortlistedDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShortlistedDataGrid.SelectedItem is not TransferPlayerRow row)
        {
            RecommendedDetailPanel.ShowEmpty();
            return;
        }

        _selectedListing = row.Listing;
        ShowPlayerDetails(row.Listing, TransferDetailMode.Scout, panel: RecommendedDetailPanel);
    }

    private void SquadDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SquadDataGrid.SelectedItem is not TransferPlayerRow row)
        {
            SquadDetailPanel.ShowEmpty();
            return;
        }

        _selectedListing = row.Listing;
        ShowPlayerDetails(row.Listing, TransferDetailMode.Squad, panel: SquadDetailPanel);
    }

    private void OffersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OffersDataGrid.SelectedItem is not TransferOfferRow { Listing: not null } row)
        {
            OffersDetailPanel.ShowEmpty();
            return;
        }

        _selectedListing = row.Listing;
        _selectedOffer = row.Offer;
        ShowPlayerDetails(row.Listing, TransferDetailMode.Offers, offer: row.Offer, panel: OffersDetailPanel);
    }

    private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is not TransferHistoryRow { Listing: not null } row)
        {
            HistoryDetailPanel.ShowEmpty();
            return;
        }

        _selectedListing = row.Listing;
        _selectedHistoryItem = row.HistoryItem;
        ShowPlayerDetails(row.Listing, TransferDetailMode.History, historyItem: row.HistoryItem, panel: HistoryDetailPanel);
    }

    private void DetailPanel_MakeOfferRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _state.League is null || _state.SelectedTeam is null || _selectedListing is null)
        {
            return;
        }

        var offerFeeText = (sender as TransferPlayerDetailPanel)?.OfferFeeText ?? _activeDetailPanel?.OfferFeeText ?? string.Empty;
        if (!TryParseMillionAmount(offerFeeText, out var fee))
        {
            MessageBox.Show("Enter a valid transfer fee in millions.", "Transfer Market", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var offer = _transferMarketService.MakeUserOffer(
            _state.TransferMarket,
            _state.League,
            _state.SelectedTeam,
            _selectedListing.Player.PlayerId,
            fee,
            GetCurrentRound());

        MessageBox.Show(offer.Message, "Transfer Market", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshAll();
    }

    private void DetailPanel_ShortlistToggled(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _selectedListing is null)
        {
            return;
        }

        var playerId = _selectedListing.Player.PlayerId;
        var existingIndex = _state.TransferMarket.ShortlistedPlayerIds.FindIndex(item =>
            item.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _state.TransferMarket.ShortlistedPlayerIds.RemoveAt(existingIndex);
        }
        else
        {
            _state.TransferMarket.ShortlistedPlayerIds.Add(playerId);
        }

        RefreshRecommendations();
        UpdateActionAvailability();
    }

    private void DetailPanel_TransferListToggled(object? sender, EventArgs e)
    {
        if (_selectedListing is null)
        {
            return;
        }

        _selectedListing.Player.IsListedForSale = !_selectedListing.Player.IsListedForSale;
        RefreshAll();
        UpdateActionAvailability();
    }

    private void DetailPanel_AcceptOfferRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _state.League is null || _selectedOffer is null)
        {
            return;
        }

        _transferMarketService.AcceptOffer(_state.TransferMarket, _selectedOffer.OfferId, _state.League, GetCurrentRound());
        RefreshAll();
    }

    private void DetailPanel_RejectOfferRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _selectedOffer is null)
        {
            return;
        }

        _transferMarketService.RejectOffer(_state.TransferMarket, _selectedOffer.OfferId);
        RefreshAll();
    }

    private void DetailPanel_CounterOfferRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _state.League is null || _selectedOffer is null)
        {
            return;
        }

        var counterFeeText = (sender as TransferPlayerDetailPanel)?.CounterFeeText ?? _activeDetailPanel?.CounterFeeText ?? string.Empty;
        if (!TryParseMillionAmount(counterFeeText, out var fee))
        {
            MessageBox.Show("Enter a valid counter fee in millions.", "Transfer Market", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _transferMarketService.CounterOffer(_state.TransferMarket, _selectedOffer.OfferId, fee, _state.League, GetCurrentRound());
        RefreshAll();
    }

    private void LoadMarket()
    {
        RefreshHeader();
        RefreshAll();
        ShowEmptyPlayerDetails();
    }

    private void RefreshAll()
    {
        RefreshHeader();
        RefreshMarketSearch();
        RefreshRecommendations();
        RefreshSquad();
        RefreshOffers();
        RefreshHistory();
        UpdateActionAvailability();
    }

    private void RefreshHeader()
    {
        if (_state.League is null || _state.SelectedTeam is null || _state.TransferMarket is null)
        {
            return;
        }

        var round = GetCurrentRound();
        var windowInfo = _transferMarketService.GetWindowInfo(_state.League, round);
        var finance = _transferMarketService.GetFinance(_state.TransferMarket, _state.League.LeagueId, _state.SelectedTeam);

        SubtitleTextBlock.Text = _state.League.Name;
        ClubTextBlock.Text = _state.SelectedTeam.Name;
        BudgetTextBlock.Text = TransferMarketService.FormatMoney(finance.AvailableTransferBudget);
        WindowTextBlock.Text = windowInfo.StatusText;
        DeadlineTextBlock.Text = windowInfo.IsOpen
            ? $"R{round} · {windowInfo.RoundsRemaining} rounds left"
            : $"R{round}";
    }

    private void RefreshMarketSearch()
    {
        if (_state.TransferMarket is null || _state.League is null)
        {
            return;
        }

        var criteria = new TransferSearchCriteria
        {
            PlayerName = SearchNameTextBox.Text,
            ClubName = SearchClubTextBox.Text,
            LeagueId = GetSelectedComboTag(SearchLeagueComboBox),
            Position = GetSelectedComboTag(SearchPositionComboBox),
            Trait = SearchTraitTextBox.Text,
            MinimumOverall = TryParseInt(GetSelectedComboTag(SearchMinOvrComboBox)),
            MaximumPrice = TryParseMillionAmount(GetSelectedComboTag(SearchMaxPriceComboBox), out var maxPrice) ? maxPrice : null,
            MinimumAge = TryParseInt(GetSelectedComboTag(SearchMinAgeComboBox)),
            MaximumAge = TryParseInt(GetSelectedComboTag(SearchMaxAgeComboBox)),
            FormStatus = TryParseFormStatus(GetSelectedComboTag(SearchFormComboBox))
        };

        MarketDataGrid.ItemsSource = _transferMarketService.SearchPlayers(_state.TransferMarket, criteria, _state.League.PlayerStats)
            .Take(250)
            .Select(CreatePlayerRow)
            .ToList();
    }

    private void RefreshRecommendations()
    {
        if (_state.TransferMarket is null || _state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        var allListings = _transferMarketService.GetAllPlayerListings(_state.TransferMarket, _state.League.PlayerStats).ToList();
        var shortlistedIds = new HashSet<string>(_state.TransferMarket.ShortlistedPlayerIds, StringComparer.OrdinalIgnoreCase);
        var shortlistedRows = allListings
            .Where(listing => shortlistedIds.Contains(listing.Player.PlayerId))
            .Select(CreatePlayerRow)
            .ToList();

        ShortlistedDataGrid.ItemsSource = shortlistedRows;
        ShortlistedEmptyState.Visibility = shortlistedRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RecommendedDataGrid.ItemsSource = _transferMarketService
            .GetRecommendedPlayers(_state.TransferMarket, _state.League, _state.SelectedTeam)
            .Select(recommendation => CreatePlayerRow(recommendation.Listing, recommendation.Reason))
            .ToList();
    }

    private void RefreshSquad()
    {
        if (_state.TransferMarket is null || _state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        SquadDataGrid.ItemsSource = _transferMarketService
            .GetClubListings(_state.TransferMarket, _state.League.LeagueId, _state.SelectedTeam, _state.League.PlayerStats)
            .Select(CreatePlayerRow)
            .ToList();
    }

    private void RefreshOffers()
    {
        if (_state.TransferMarket is null)
        {
            return;
        }

        var listings = _transferMarketService.GetAllPlayerListings(_state.TransferMarket, _state.League?.PlayerStats).ToList();
        OffersDataGrid.ItemsSource = _state.TransferMarket.Offers
            .OrderByDescending(offer => offer.CreatedRound)
            .Select(offer =>
            {
                var listing = listings.FirstOrDefault(item => item.Player.PlayerId == offer.PlayerId);
                var nationality = ResolveNationality(listing?.Player, offer.PlayerName);
                return new TransferOfferRow(
                    offer,
                    listing,
                    string.IsNullOrWhiteSpace(offer.Message) ? $"{offer.FromClubName} → {offer.ToClubName}" : offer.Message,
                    offer.PlayerName,
                    nationality.FlagImagePath,
                    nationality.Name,
                    offer.ToClubName,
                    ClubLogoService.GetClubLogoPath(offer.ToClubName, offer.ToLeagueId),
                    listing is null ? "-" : listing.Player.OverallRating.ToString(CultureInfo.InvariantCulture),
                    TransferMarketService.FormatMoney(offer.CounterFee ?? offer.Fee),
                    listing is null ? "-" : TransferMarketService.FormatMoney(listing.MarketValue),
                    offer.Status.ToString(),
                    GetOfferStatusBrush(offer.Status));
            })
            .ToList();
    }

    private void RefreshHistory()
    {
        if (_state.TransferMarket is null)
        {
            return;
        }

        var listings = _transferMarketService.GetAllPlayerListings(_state.TransferMarket, _state.League?.PlayerStats).ToList();
        HistoryDataGrid.ItemsSource = _state.TransferMarket.TransferHistory
            .OrderByDescending(item => item.RoundNumber)
            .Select(item =>
            {
                var listing = listings.FirstOrDefault(candidate => candidate.Player.PlayerId == item.PlayerId);
                var nationality = ResolveNationality(listing?.Player, item.PlayerName);
                return new TransferHistoryRow(
                    item,
                    listing,
                    item.RoundNumber,
                    item.PlayerName,
                    nationality.FlagImagePath,
                    nationality.Name,
                    listing is null ? "-" : listing.Player.OverallRating.ToString(CultureInfo.InvariantCulture),
                    item.FromClubName,
                    ClubLogoService.GetClubLogoPath(item.FromClubName, item.FromLeagueId),
                    item.ToClubName,
                    ClubLogoService.GetClubLogoPath(item.ToClubName, item.ToLeagueId),
                    TransferMarketService.FormatMoney(item.Fee),
                    item.Type);
            })
            .ToList();
    }

    private void ShowPlayerDetails(
        TransferPlayerListing listing,
        TransferDetailMode mode,
        string recommendationReason = "",
        TransferOffer? offer = null,
        TransferHistoryItem? historyItem = null,
        TransferPlayerDetailPanel? panel = null)
    {
        var player = listing.Player;
        var stat = _state.League?.PlayerStats.FirstOrDefault(item => item.PlayerName == player.Name);
        var isOwnPlayer = _state.SelectedTeam is not null &&
            listing.Team.Name.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase);
        var windowInfo = _state.League is null
            ? new TransferWindowInfo(false, "Window Closed", 0, "No active league.")
            : _transferMarketService.GetWindowInfo(_state.League, GetCurrentRound());
        var isShortlisted = _state.TransferMarket?.ShortlistedPlayerIds.Contains(player.PlayerId, StringComparer.OrdinalIgnoreCase) == true;

        _selectedListing = listing;
        _selectedOffer = offer;
        _selectedHistoryItem = historyItem;
        _activeDetailPanel = panel ?? GetDetailPanel(mode);
        _activeDetailPanel.ShowPlayer(new TransferPlayerDetailContext(
            listing,
            mode,
            stat,
            isOwnPlayer,
            windowInfo.IsOpen,
            windowInfo.Tooltip,
            isShortlisted,
            player.IsListedForSale,
            mode is not TransferDetailMode.History,
            recommendationReason,
            offer,
            historyItem));
    }

    private void UpdateActionAvailability()
    {
        if (_selectedListing is null || _activeDetailPanel is null)
        {
            return;
        }

        var mode = _activeDetailPanel == RecommendedDetailPanel
            ? TransferDetailMode.Scout
            : _activeDetailPanel == SquadDetailPanel
                ? TransferDetailMode.Squad
                : _activeDetailPanel == OffersDetailPanel
                    ? TransferDetailMode.Offers
                    : _activeDetailPanel == HistoryDetailPanel
                        ? TransferDetailMode.History
                        : TransferDetailMode.Market;
        var selectedRecommendation = RecommendedDataGrid.SelectedItem as TransferPlayerRow;
        var recommendationReason = mode is TransferDetailMode.Scout &&
            selectedRecommendation?.PlayerId == _selectedListing.Player.PlayerId
                ? selectedRecommendation.Reason
                : string.Empty;
        ShowPlayerDetails(_selectedListing, mode, recommendationReason, _selectedOffer, _selectedHistoryItem, _activeDetailPanel);
    }

    private void EnsureTransferState()
    {
        if (_state.League is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
    }

    private void InitializeFilterControls()
    {
        SearchLeagueComboBox.Items.Clear();
        SearchLeagueComboBox.Items.Add(CreateComboItem("All Leagues", string.Empty));
        if (_state.TransferMarket is not null)
        {
            foreach (var league in _state.TransferMarket.Leagues.OrderBy(league => league.LeagueName))
            {
                SearchLeagueComboBox.Items.Add(CreateComboItem(league.LeagueName, league.LeagueId));
            }
        }

        SearchLeagueComboBox.SelectedIndex = 0;

        SearchPositionComboBox.Items.Clear();
        foreach (var item in new[] { ("Any", ""), ("GK", "GK"), ("CB", "CB"), ("LB", "LB"), ("RB", "RB"), ("CDM", "CDM"), ("CM", "CM"), ("CAM", "CAM"), ("LW", "LW"), ("RW", "RW"), ("ST", "ST") })
        {
            SearchPositionComboBox.Items.Add(CreateComboItem(item.Item1, item.Item2));
        }

        SearchPositionComboBox.SelectedIndex = 0;

        SearchMaxPriceComboBox.Items.Clear();
        foreach (var item in new[] { ("€ Any", ""), ("€25M", "25"), ("€50M", "50"), ("€100M", "100"), ("€150M", "150"), ("€250M", "250") })
        {
            SearchMaxPriceComboBox.Items.Add(CreateComboItem(item.Item1, item.Item2));
        }

        SearchMaxPriceComboBox.SelectedIndex = 0;

        SearchMinOvrComboBox.Items.Clear();
        foreach (var item in new[] { ("Any", ""), ("70+", "70"), ("75+", "75"), ("80+", "80"), ("85+", "85"), ("90+", "90") })
        {
            SearchMinOvrComboBox.Items.Add(CreateComboItem(item.Item1, item.Item2));
        }

        SearchMinOvrComboBox.SelectedIndex = 0;

        SearchMinAgeComboBox.Items.Clear();
        foreach (var item in new[] { ("Any", ""), ("18+", "18"), ("21+", "21"), ("24+", "24"), ("28+", "28") })
        {
            SearchMinAgeComboBox.Items.Add(CreateComboItem(item.Item1, item.Item2));
        }

        SearchMinAgeComboBox.SelectedIndex = 0;

        SearchMaxAgeComboBox.Items.Clear();
        foreach (var item in new[] { ("Any", ""), ("21", "21"), ("24", "24"), ("27", "27"), ("30", "30"), ("34", "34") })
        {
            SearchMaxAgeComboBox.Items.Add(CreateComboItem(item.Item1, item.Item2));
        }

        SearchMaxAgeComboBox.SelectedIndex = 0;

        SearchFormComboBox.Items.Clear();
        foreach (var item in new[] { ("Any", ""), ("Excellent", "Excellent"), ("Good", "Good"), ("Average", "Average"), ("Poor", "Poor"), ("Very Poor", "VeryPoor") })
        {
            SearchFormComboBox.Items.Add(CreateComboItem(item.Item1, item.Item2));
        }

        SearchFormComboBox.SelectedIndex = 0;
    }

    private int GetCurrentRound()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return 1;
        }

        return _state.League.Fixtures
            .Where(fixture => !fixture.IsPlayed &&
                (fixture.HomeTeam == _state.SelectedTeam || fixture.AwayTeam == _state.SelectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .Select(fixture => fixture.RoundNumber)
            .FirstOrDefault(_state.League.Fixtures.Count == 0 ? 1 : _state.League.Fixtures.Max(fixture => fixture.RoundNumber));
    }

    private TransferPlayerRow CreatePlayerRow(TransferPlayerListing listing)
    {
        return CreatePlayerRow(listing, string.Empty);
    }

    private TransferPlayerRow CreatePlayerRow(TransferPlayerListing listing, string reason)
    {
        var player = listing.Player;
        var formBadge = PlayerFormBadgeHelper.Create(player.FormStatus);
        var saleStatus = GetSaleStatus(player);
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        return new TransferPlayerRow(
            listing,
            player.PlayerId,
            player.Name,
            nationality.FlagImagePath,
            nationality.Name,
            ClubLogoService.GetClubLogoPath(listing.Team.Name, listing.LeagueId),
            listing.Team.Name,
            listing.LeagueName,
            player.PreferredPosition,
            player.Age?.ToString(CultureInfo.InvariantCulture) ?? "-",
            player.OverallRating.ToString(CultureInfo.InvariantCulture),
            formBadge.Text,
            TransferMarketService.FormatMoney(listing.MarketValue),
            TransferMarketService.FormatMoney(listing.AskingPrice),
            listing.StatusText,
            saleStatus,
            reason,
            $"{player.PreferredPosition} · Age {player.Age?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
            GetStatusBrush(listing.StatusText),
            formBadge.Background,
            formBadge.Foreground,
            GetSaleBrush(saleStatus),
            GetSaleTextBrush(saleStatus));
    }

    private static NationalityDisplayInfo ResolveNationality(Player? player, string playerName)
    {
        return PlayerNationalityDisplayService.Resolve(player ?? new Player { Name = playerName });
    }

    private void ShowEmptyPlayerDetails()
    {
        MarketDetailPanel.ShowEmpty();
        RecommendedDetailPanel.ShowEmpty();
        SquadDetailPanel.ShowEmpty();
        OffersDetailPanel.ShowEmpty();
        HistoryDetailPanel.ShowEmpty();
        _selectedListing = null;
        _selectedOffer = null;
        _selectedHistoryItem = null;
        _activeDetailPanel = null;
    }

    private TransferPlayerDetailPanel GetDetailPanel(TransferDetailMode mode)
    {
        return mode switch
        {
            TransferDetailMode.Scout => RecommendedDetailPanel,
            TransferDetailMode.Squad => SquadDetailPanel,
            TransferDetailMode.Offers => OffersDetailPanel,
            TransferDetailMode.History => HistoryDetailPanel,
            _ => MarketDetailPanel
        };
    }

    private static IEnumerable<TraitBadgeRow> CreateTraitBadges(Player player)
    {
        if (player.Traits.Count == 0)
        {
            return [new TraitBadgeRow("•", "No traits", "No special traits")];
        }

        return player.Traits.Select(trait => new TraitBadgeRow(
            PlayerTraitDisplayService.GetIcon(trait),
            PlayerTraitDisplayService.GetShortLabel(trait),
            $"{PlayerTraitDisplayService.GetLabel(trait)}: {PlayerTraitDisplayService.GetEffectDescription(trait)}"));
    }

    private static string CreateScoutSummary(Player player, TransferPlayerListing listing, PlayerSeasonStats? stat)
    {
        var status = player.IsInjured ? $"Currently injured: {player.InjuryType}" : "Available for selection";
        var output = $"{status}. {player.Role} profile with {listing.StatusText.ToLowerInvariant()} transfer status.";
        if (stat is not null && stat.Appearances > 0)
        {
            output += $" Season: {stat.Appearances} apps, {stat.Goals} goals, {stat.Assists} assists, {stat.AverageRating:0.00} avg rating.";
        }

        return output;
    }

    private static int? TryParseInt(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static bool TryParseMillionAmount(string text, out decimal amount)
    {
        amount = 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var millions))
        {
            return false;
        }

        amount = Math.Max(0, millions * 1_000_000m);
        return amount > 0;
    }

    private static ComboBoxItem CreateComboItem(string label, string tag)
    {
        return new ComboBoxItem { Content = label, Tag = tag };
    }

    private static string GetSelectedComboTag(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? string.Empty
            : string.Empty;
    }

    private static PlayerFormStatus? TryParseFormStatus(string value)
    {
        return Enum.TryParse<PlayerFormStatus>(value, out var status) ? status : null;
    }

    private static string GetStatusBrush(string status)
    {
        return status switch
        {
            "Listed" => "#F59E0B",
            "Negotiating" => "#8B5CF6",
            "Unavailable" => "#64748B",
            "Injured" => "#EF4444",
            _ => "#10B981"
        };
    }

    private static string GetOfferStatusBrush(OfferStatus status)
    {
        return status switch
        {
            OfferStatus.Completed or OfferStatus.Accepted => "#10B981",
            OfferStatus.Countered => "#8B5CF6",
            OfferStatus.Rejected or OfferStatus.Withdrawn => "#EF4444",
            _ => "#2563EB"
        };
    }

    private static string GetSaleStatus(Player player)
    {
        return player.TransferStatus switch
        {
            PlayerTransferStatus.Listed => "Listed",
            PlayerTransferStatus.Negotiating => "Offer Received",
            _ => "Not Listed"
        };
    }

    private static string GetSaleBrush(string saleStatus)
    {
        return saleStatus switch
        {
            "Listed" => "#F59E0B",
            "Offer Received" => "#8B5CF6",
            _ => "#E5E7EB"
        };
    }

    private static string GetSaleTextBrush(string saleStatus)
    {
        return saleStatus == "Not Listed" ? "#334155" : "#FFFFFF";
    }

    private static Brush ToBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }

    private record TransferPlayerRow(
        TransferPlayerListing Listing,
        string PlayerId,
        string PlayerName,
        string NationalityFlagImagePath,
        string NationalityName,
        string ClubLogoPath,
        string ClubName,
        string LeagueName,
        string Position,
        string Age,
        string OverallText,
        string Form,
        string MarketValueText,
        string AskingPriceText,
        string Status,
        string SaleStatus,
        string Reason,
        string PlayerMeta,
        string StatusBrush,
        string FormBrush,
        string FormTextBrush,
        string SaleBrush,
        string SaleTextBrush);

    private record TraitBadgeRow(string Icon, string Label, string Tooltip);

    private record TransferOfferRow(
        TransferOffer Offer,
        TransferPlayerListing? Listing,
        string Message,
        string PlayerName,
        string NationalityFlagImagePath,
        string NationalityName,
        string BuyingClubName,
        string BuyingClubLogoPath,
        string OverallText,
        string FeeText,
        string MarketValueText,
        string Status,
        string StatusBrush);

    private record TransferHistoryRow(
        TransferHistoryItem HistoryItem,
        TransferPlayerListing? Listing,
        int RoundNumber,
        string PlayerName,
        string NationalityFlagImagePath,
        string NationalityName,
        string OverallText,
        string FromClubName,
        string FromLogoPath,
        string ToClubName,
        string ToLogoPath,
        string FeeText,
        string Type);
}
