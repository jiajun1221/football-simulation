using System.Globalization;
using System.Text;
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
    private Action? _modalPrimaryAction;
    private Action? _modalSecondaryAction;
    private Action? _modalCancelAction;

    public TransferMarketView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();
        _state = state;
        _navigate = navigate;
        EnsureTransferState();
        InitializeFilterControls();
        TransferModal.ActionRequested += TransferModal_ActionRequested;
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
            ShowSimpleTransferModal(
                "Invalid Offer",
                "Transfer Market",
                _selectedListing.Player.Name,
                CreatePlayerMeta(_selectedListing),
                "Enter a valid transfer fee in millions.",
                "The board needs a valid amount before submitting the bid.",
                "Continue");
            return;
        }

        var listing = _selectedListing;
        var offer = _transferMarketService.MakeUserOffer(
            _state.TransferMarket,
            _state.League,
            _state.SelectedTeam,
            _selectedListing.Player.PlayerId,
            fee,
            GetCurrentRound());

        RefreshAll();
        ShowOfferOutcomeModal(offer, listing, submittedFee: fee);
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

        if (HasAgreedTransfer(_selectedListing.Player))
        {
            ShowSimpleTransferModal(
                "Transfer Agreed",
                "Transfer Market",
                _selectedListing.Player.Name,
                CreatePlayerMeta(_selectedListing),
                "This player already has a transfer agreed for the next window.",
                "The sale status cannot be changed unless the agreement is cancelled.",
                "Continue");
            return;
        }

        _selectedListing.Player.IsListedForSale = !_selectedListing.Player.IsListedForSale;
        RefreshAll();
        UpdateActionAvailability();
    }

    private void DetailPanel_ContractRenewalRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _state.League is null || _state.SelectedTeam is null || _selectedListing is null)
        {
            return;
        }

        var panel = sender as TransferPlayerDetailPanel ?? _activeDetailPanel;
        if (panel is null || !TryParseThousandAmount(panel.RenewalWageText, out var weeklyWage))
        {
            ShowSimpleTransferModal(
                "Invalid Contract Offer",
                "Contract Renewal",
                _selectedListing.Player.Name,
                CreatePlayerMeta(_selectedListing),
                "Enter a valid weekly wage in thousands.",
                "The player representative needs a clear wage proposal.",
                "Continue");
            return;
        }

        var result = _transferMarketService.OfferContractExtension(
            _state.TransferMarket,
            _state.League.LeagueId,
            _state.SelectedTeam,
            _selectedListing.Player,
            weeklyWage,
            panel.RenewalYears,
            panel.RenewalRole,
            GetCurrentRound());
        RefreshAll();
        ShowContractRenewalModal(_selectedListing, result);
    }

    private void DetailPanel_AcceptOfferRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _state.League is null || _selectedOffer is null)
        {
            return;
        }

        var offer = _selectedOffer;
        var listing = _selectedListing;
        _transferMarketService.AcceptOffer(_state.TransferMarket, offer.OfferId, _state.League, GetCurrentRound());
        RefreshAll();
        ShowOfferOutcomeModal(offer, listing);
    }

    private void DetailPanel_RejectOfferRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _selectedOffer is null)
        {
            return;
        }

        var offer = _selectedOffer;
        var listing = _selectedListing;
        _transferMarketService.RejectOffer(_state.TransferMarket, offer.OfferId);
        RefreshAll();
        ShowOfferOutcomeModal(offer, listing);
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
            ShowSimpleTransferModal(
                "Invalid Counter",
                "Transfer Negotiation",
                _selectedOffer.PlayerName,
                $"{_selectedOffer.FromClubName} → {_selectedOffer.ToClubName}",
                "Enter a valid counter fee in millions.",
                "Your negotiation team needs a valid counter amount.",
                "Continue");
            return;
        }

        var offer = _selectedOffer;
        var listing = _selectedListing;
        _transferMarketService.CounterOffer(_state.TransferMarket, offer.OfferId, fee, _state.League, GetCurrentRound());
        RefreshAll();
        ShowOfferOutcomeModal(offer, listing, submittedFee: fee);
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

    private void TransferModal_ActionRequested(object? sender, TransferNotificationModalAction action)
    {
        var callback = action switch
        {
            TransferNotificationModalAction.Primary => _modalPrimaryAction,
            TransferNotificationModalAction.Secondary => _modalSecondaryAction,
            TransferNotificationModalAction.Cancel => _modalCancelAction,
            _ => null
        };

        _modalPrimaryAction = null;
        _modalSecondaryAction = null;
        _modalCancelAction = null;
        callback?.Invoke();
    }

    private void ShowTransferModal(
        TransferNotificationModalContext context,
        Action? primaryAction = null,
        Action? secondaryAction = null,
        Action? cancelAction = null)
    {
        _modalPrimaryAction = primaryAction;
        _modalSecondaryAction = secondaryAction;
        _modalCancelAction = cancelAction;
        TransferModal.Show(context);
    }

    private void ShowOfferOutcomeModal(TransferOffer offer, TransferPlayerListing? listing, decimal? submittedFee = null)
    {
        listing ??= TryFindListingForOffer(offer);
        switch (offer.Status)
        {
            case OfferStatus.Countered:
                ShowTransferModal(
                    CreateNegotiationModalContext(offer, listing, submittedFee),
                    primaryAction: () => AcceptOfferFromModal(offer, listing),
                    secondaryAction: () => OpenOfferInOffersTab(offer),
                    cancelAction: () => RejectOfferFromModal(offer, listing));
                break;
            case OfferStatus.Completed:
            case OfferStatus.CompletedWhenWindowOpens:
                ShowTransferModal(CreateCompletedModalContext(offer, listing));
                break;
            case OfferStatus.AgreedForNextWindow:
                ShowTransferModal(CreateAgreementModalContext(offer, listing));
                break;
            case OfferStatus.Rejected:
            case OfferStatus.Withdrawn:
                ShowTransferModal(CreateFailedModalContext(offer, listing, submittedFee));
                break;
            default:
                ShowTransferModal(CreateSubmittedModalContext(offer, listing, submittedFee));
                break;
        }
    }

    private void AcceptOfferFromModal(TransferOffer offer, TransferPlayerListing? listing)
    {
        if (_state.TransferMarket is null || _state.League is null)
        {
            return;
        }

        _transferMarketService.AcceptOffer(_state.TransferMarket, offer.OfferId, _state.League, GetCurrentRound());
        RefreshAll();
        ShowOfferOutcomeModal(offer, listing);
    }

    private void RejectOfferFromModal(TransferOffer offer, TransferPlayerListing? listing)
    {
        if (_state.TransferMarket is null)
        {
            return;
        }

        _transferMarketService.RejectOffer(_state.TransferMarket, offer.OfferId);
        RefreshAll();
        ShowOfferOutcomeModal(offer, listing);
    }

    private void OpenOfferInOffersTab(TransferOffer offer)
    {
        TransferTabControl.SelectedIndex = 3;
        SyncTransferTabButtons();
        RefreshOffers();
        var row = OffersDataGrid.Items
            .OfType<TransferOfferRow>()
            .FirstOrDefault(item => item.Offer.OfferId == offer.OfferId);
        if (row is not null)
        {
            OffersDataGrid.SelectedItem = row;
            OffersDataGrid.ScrollIntoView(row);
        }
    }

    private TransferNotificationModalContext CreateNegotiationModalContext(
        TransferOffer offer,
        TransferPlayerListing? listing,
        decimal? submittedFee)
    {
        var playerName = listing?.Player.Name ?? offer.PlayerName;
        var marketValue = listing is null ? "-" : TransferMarketService.FormatMoney(listing.MarketValue);
        return CreateModalContext(
            "Transfer Negotiation",
            $"{offer.FromClubName} responded to your offer.",
            playerName,
            listing is null ? $"{offer.FromClubName} → {offer.ToClubName}" : CreatePlayerMeta(listing),
            $"{offer.FromClubName} are willing to negotiate.",
            $"{offer.FromClubName} believe the player is worth more. Accept their valuation or return with another proposal.",
            listing,
            offer.FromClubName,
            offer.FromLeagueId,
            "Your Offer",
            TransferMarketService.FormatMoney(submittedFee ?? offer.Fee),
            "Counter Offer",
            TransferMarketService.FormatMoney(offer.CounterFee ?? offer.Fee),
            "Current Wage",
            PlayerContractService.FormatWage(listing?.WeeklyWage ?? offer.WeeklyWage ?? 0),
            "Contract",
            listing?.Player is null ? "-" : PlayerContractService.FormatContractExpiry(listing.Player),
            "Accept Deal",
            "Counter Again",
            "Cancel",
            "#061226",
            "#EFF6FF",
            "#BFDBFE",
            "#1E3A8A");
    }

    private TransferNotificationModalContext CreateCompletedModalContext(TransferOffer offer, TransferPlayerListing? listing)
    {
        var playerName = listing?.Player.Name ?? offer.PlayerName;
        var budgetRemaining = GetBudgetRemainingText(offer);
        return CreateModalContext(
            "Transfer Completed",
            "Official club announcement",
            playerName,
            listing is null ? $"{offer.FromClubName} → {offer.ToClubName}" : CreatePlayerMeta(listing),
            $"{playerName} officially joins {offer.ToClubName}.",
            "Player is excited to join the club. The deal has been added to transfer history.",
            listing,
            offer.ToClubName,
            offer.ToLeagueId,
            "Transfer Fee",
            TransferMarketService.FormatMoney(offer.CounterFee ?? offer.Fee),
            "Contract",
            listing?.Player is null ? $"{offer.ContractYears} Years" : PlayerContractService.FormatContractExpiry(listing.Player),
            "Squad Role",
            PlayerContractService.FormatRole(offer.SquadRole),
            "Budget Remaining",
            budgetRemaining,
            "Continue",
            string.Empty,
            string.Empty,
            "#061226",
            "#ECFDF5",
            "#BBF7D0",
            "#166534");
    }

    private TransferNotificationModalContext CreateAgreementModalContext(TransferOffer offer, TransferPlayerListing? listing)
    {
        var playerName = listing?.Player.Name ?? offer.PlayerName;
        return CreateModalContext(
            "Agreement Reached",
            $"{offer.ToClubName} have secured a future transfer.",
            playerName,
            listing is null ? $"{offer.FromClubName} → {offer.ToClubName}" : CreatePlayerMeta(listing),
            $"{playerName} will join {offer.ToClubName} when the transfer window opens.",
            "The player remains available for selection until the window opens.",
            listing,
            offer.ToClubName,
            offer.ToLeagueId,
            "Agreed Fee",
            TransferMarketService.FormatMoney(offer.CounterFee ?? offer.Fee),
            "Status",
            "Agreed for next window",
            "Current Club",
            offer.FromClubName,
            "Buying Club",
            offer.ToClubName,
            "Continue",
            string.Empty,
            string.Empty,
            "#0F172A",
            "#EFF6FF",
            "#BFDBFE",
            "#1D4ED8");
    }

    private TransferNotificationModalContext CreateFailedModalContext(
        TransferOffer offer,
        TransferPlayerListing? listing,
        decimal? submittedFee)
    {
        var playerName = listing?.Player.Name ?? offer.PlayerName;
        var isWithdrawn = offer.Status == OfferStatus.Withdrawn;
        return CreateModalContext(
            isWithdrawn ? "Transfer Failed" : "Offer Rejected",
            $"{offer.FromClubName} responded to the proposal.",
            playerName,
            listing is null ? $"{offer.FromClubName} → {offer.ToClubName}" : CreatePlayerMeta(listing),
            isWithdrawn ? $"{offer.ToClubName} walked away from talks." : $"{offer.FromClubName} rejected the offer immediately.",
            string.IsNullOrWhiteSpace(offer.Message) ? "The selling club did not feel the bid matched their valuation." : offer.Message,
            listing,
            offer.FromClubName,
            offer.FromLeagueId,
            "Your Offer",
            TransferMarketService.FormatMoney(submittedFee ?? offer.CounterFee ?? offer.Fee),
            "Market Value",
            listing is null ? "-" : TransferMarketService.FormatMoney(listing.MarketValue),
            "Asking Price",
            listing is null ? "-" : TransferMarketService.FormatMoney(listing.AskingPrice),
            "Contract",
            listing?.Player is null ? "-" : PlayerContractService.FormatContractExpiry(listing.Player),
            "Continue",
            string.Empty,
            string.Empty,
            "#7F1D1D",
            "#FEF2F2",
            "#FECACA",
            "#991B1B");
    }

    private TransferNotificationModalContext CreateSubmittedModalContext(
        TransferOffer offer,
        TransferPlayerListing? listing,
        decimal? submittedFee)
    {
        return CreateModalContext(
            "Offer Submitted",
            "Transfer Market",
            listing?.Player.Name ?? offer.PlayerName,
            listing is null ? $"{offer.FromClubName} → {offer.ToClubName}" : CreatePlayerMeta(listing),
            string.IsNullOrWhiteSpace(offer.Message) ? "The proposal has been sent." : offer.Message,
            "The club will continue monitoring the negotiation.",
            listing,
            offer.ToClubName,
            offer.ToLeagueId,
            "Offer",
            TransferMarketService.FormatMoney(submittedFee ?? offer.Fee),
            "Current Wage",
            PlayerContractService.FormatWage(listing?.WeeklyWage ?? offer.WeeklyWage ?? 0),
            "Contract",
            listing?.Player is null ? "-" : PlayerContractService.FormatContractExpiry(listing.Player),
            "Status",
            listing?.ContractStatusText ?? "Active",
            "Continue",
            string.Empty,
            string.Empty,
            "#061226",
            "#EFF6FF",
            "#BFDBFE",
            "#1E3A8A");
    }

    private void ShowSimpleTransferModal(
        string title,
        string subtitle,
        string playerName,
        string playerMeta,
        string story,
        string message,
        string primaryButtonText)
    {
        ShowTransferModal(CreateModalContext(
            title,
            subtitle,
            playerName,
            playerMeta,
            story,
            message,
            null,
            _state.SelectedTeam?.Name ?? string.Empty,
            _state.League?.LeagueId ?? _state.SelectedLeagueId,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            primaryButtonText,
            string.Empty,
            string.Empty,
            "#061226",
            "#EFF6FF",
            "#BFDBFE",
            "#1E3A8A"));
    }

    private void ShowContractRenewalModal(TransferPlayerListing listing, ContractRenewalResult result)
    {
        var accepted = result.Accepted;
        ShowTransferModal(CreateModalContext(
            accepted ? "Contract Extended" : "Contract Rejected",
            accepted ? "Club contract office" : "Player representative response",
            listing.Player.Name,
            CreatePlayerMeta(listing),
            result.Message,
            accepted
                ? "The squad planning screen has been updated with the new deal."
                : "Improve the wage, role, or contract length before trying again.",
            listing,
            listing.Team.Name,
            listing.LeagueId,
            "Weekly Wage",
            PlayerContractService.FormatWage(result.WeeklyWage),
            "Contract",
            $"Expires {result.ContractEndYear}",
            "Squad Role",
            PlayerContractService.FormatRole(result.SquadRole),
            "Status",
            accepted ? "Accepted" : "Rejected",
            "Continue",
            string.Empty,
            string.Empty,
            accepted ? "#061226" : "#7F1D1D",
            accepted ? "#ECFDF5" : "#FEF2F2",
            accepted ? "#BBF7D0" : "#FECACA",
            accepted ? "#166534" : "#991B1B"));
    }

    private TransferNotificationModalContext CreateModalContext(
        string title,
        string subtitle,
        string playerName,
        string playerMeta,
        string story,
        string message,
        TransferPlayerListing? listing,
        string clubName,
        string leagueId,
        string detailOneLabel,
        string detailOneValue,
        string detailTwoLabel,
        string detailTwoValue,
        string detailThreeLabel,
        string detailThreeValue,
        string detailFourLabel,
        string detailFourValue,
        string primaryButtonText,
        string secondaryButtonText,
        string cancelButtonText,
        string headerBrush,
        string messageBackground,
        string messageBorderBrush,
        string messageForeground)
    {
        return new TransferNotificationModalContext(
            title,
            subtitle,
            playerName,
            playerMeta,
            story,
            message,
            listing?.Player is null ? GetDefaultPlayerImagePath() : GetPlayerImagePath(listing.Player),
            ClubLogoService.GetClubLogoPath(clubName, leagueId),
            detailOneLabel,
            detailOneValue,
            detailTwoLabel,
            detailTwoValue,
            detailThreeLabel,
            detailThreeValue,
            detailFourLabel,
            detailFourValue,
            primaryButtonText,
            secondaryButtonText,
            cancelButtonText,
            ToBrush(headerBrush),
            ToBrush(messageBackground),
            ToBrush(messageBorderBrush),
            ToBrush(messageForeground));
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
                    FormatOfferStatus(offer.Status),
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
        var status = CreateStatusDisplay(player);

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
            mode is not TransferDetailMode.History && !(isOwnPlayer && HasAgreedTransfer(player)),
            status.Text,
            status.Brush,
            status.Tooltip,
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
        ShowPlayerDetails(_selectedListing, mode, string.Empty, _selectedOffer, _selectedHistoryItem, _activeDetailPanel);
    }

    private void EnsureTransferState()
    {
        if (_state.League is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        if (_state.SelectedTeam is not null)
        {
            _transferMarketService.RunAiTransferActivity(_state.TransferMarket, _state.League, _state.SelectedTeam, GetCurrentRound());
        }
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
        var status = CreateStatusDisplay(player);
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
            FormatContractYear(player),
            PlayerContractService.FormatWage(listing.WeeklyWage),
            listing.ContractStatusText,
            status.Text,
            status.Tooltip,
            reason,
            $"{player.PreferredPosition} · Age {player.Age?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
            status.Brush,
            formBadge.Background,
            formBadge.Foreground);
    }

    private static NationalityDisplayInfo ResolveNationality(Player? player, string playerName)
    {
        return PlayerNationalityDisplayService.Resolve(player ?? new Player { Name = playerName });
    }

    private TransferPlayerListing? TryFindListingForOffer(TransferOffer offer)
    {
        if (_state.TransferMarket is null)
        {
            return null;
        }

        return _transferMarketService
            .GetAllPlayerListings(_state.TransferMarket, _state.League?.PlayerStats)
            .FirstOrDefault(listing => listing.Player.PlayerId.Equals(offer.PlayerId, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreatePlayerMeta(TransferPlayerListing listing)
    {
        var age = listing.Player.Age?.ToString(CultureInfo.InvariantCulture) ?? "-";
        return $"{listing.Player.PreferredPosition} · {age} · {listing.Team.Name}";
    }

    private static string FormatContractYear(Player player)
    {
        return player.ContractEndYear?.ToString(CultureInfo.InvariantCulture) ?? "-";
    }

    private string GetBudgetRemainingText(TransferOffer offer)
    {
        if (_state.TransferMarket is null || _state.SelectedTeam is null || _state.League is null ||
            !offer.ToClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        var finance = _transferMarketService.GetFinance(_state.TransferMarket, _state.League.LeagueId, _state.SelectedTeam);
        return TransferMarketService.FormatMoney(finance.AvailableTransferBudget);
    }

    private static string CreateSquadRoleText(Player? player)
    {
        return player?.OverallRating switch
        {
            >= 86 => "Key Player",
            >= 80 => "Important Player",
            >= 74 => "Rotation Player",
            _ => "Squad Player"
        };
    }

    private static string GetPlayerImagePath(Player player)
    {
        var playerImage = $"pack://application:,,,/Assets/Players/{CreatePlayerImageSlug(player.Name)}.png";
        return ResourceExists(playerImage) ? playerImage : GetDefaultPlayerImagePath();
    }

    private static string GetDefaultPlayerImagePath()
    {
        const string defaultImage = "pack://application:,,,/Assets/Players/default.png";
        return ResourceExists(defaultImage) ? defaultImage : string.Empty;
    }

    private static string CreatePlayerImageSlug(string playerName)
    {
        var normalized = playerName.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousWasHyphen = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasHyphen = false;
                continue;
            }

            if (!previousWasHyphen)
            {
                builder.Append('-');
                previousWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "default" : slug;
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

    private static bool TryParseThousandAmount(string text, out decimal amount)
    {
        amount = 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var thousands))
        {
            return false;
        }

        amount = Math.Max(0, thousands * 1_000m);
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

    private StatusDisplay CreateStatusDisplay(Player player)
    {
        var secondaryStatuses = GetSecondaryStatuses(player).ToList();
        var primaryStatus = secondaryStatuses.FirstOrDefault() ?? "Available";
        var displayStatus = GetCompactStatusText(primaryStatus);
        var tooltip = secondaryStatuses.Count > 1
            ? $"Also: {string.Join(", ", secondaryStatuses.Skip(1))}"
            : primaryStatus;

        return new StatusDisplay(displayStatus, GetStatusBrush(primaryStatus), tooltip);
    }

    private static string GetCompactStatusText(string status)
    {
        return status switch
        {
            "Offer Received" => "Offer",
            "Transfer Agreed" => "Agreed",
            _ => status
        };
    }

    private IEnumerable<string> GetSecondaryStatuses(Player player)
    {
        if (player.IsInjured || player.InjuryRecoveryMatches > 0)
        {
            yield return "Injured";
        }

        if (player.IsSuspended || player.IsSentOff)
        {
            yield return "Banned";
        }

        if (HasAgreedTransfer(player))
        {
            yield return "Transfer Agreed";
        }

        if (HasActiveAiOffer(player))
        {
            yield return "Offer Received";
        }

        if (HasActiveUserNegotiation(player) || player.TransferStatus == PlayerTransferStatus.Negotiating)
        {
            yield return "Negotiating";
        }

        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            yield return "Listed";
        }

        if (player.RejectTransferOffers || player.TransferStatus == PlayerTransferStatus.Unavailable)
        {
            yield return "Untouchable";
        }
    }

    private static string GetStatusBrush(string status)
    {
        return status switch
        {
            "Available" => "#10B981",
            "Untouchable" => "#061226",
            "Listed" => "#2563EB",
            "Offer Received" => "#8B5CF6",
            "Negotiating" => "#F59E0B",
            "Injured" => "#EF4444",
            "Banned" => "#991B1B",
            "Transfer Agreed" => "#0EA5E9",
            _ => "#10B981"
        };
    }

    private static string GetOfferStatusBrush(OfferStatus status)
    {
        return status switch
        {
            OfferStatus.Completed or OfferStatus.CompletedWhenWindowOpens or OfferStatus.Accepted => "#10B981",
            OfferStatus.AgreedForNextWindow => "#0EA5E9",
            OfferStatus.Countered => "#8B5CF6",
            OfferStatus.Rejected or OfferStatus.Withdrawn => "#EF4444",
            OfferStatus.PendingUntilWindowOpens => "#F97316",
            _ => "#2563EB"
        };
    }

    private static string FormatOfferStatus(OfferStatus status)
    {
        return status switch
        {
            OfferStatus.PendingUntilWindowOpens => "Pending Window",
            OfferStatus.AgreedForNextWindow => "Agreed for next window",
            OfferStatus.CompletedWhenWindowOpens => "Completed",
            _ => status.ToString()
        };
    }

    private bool HasActiveAiOffer(Player player)
    {
        return _state.TransferMarket?.Offers.Any(offer =>
            !offer.IsUserOffer &&
            offer.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered) == true;
    }

    private bool HasActiveUserNegotiation(Player player)
    {
        return _state.TransferMarket?.Offers.Any(offer =>
            offer.IsUserOffer &&
            offer.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered) == true;
    }

    private bool HasAgreedTransfer(Player player)
    {
        return _state.TransferMarket?.Offers.Any(offer =>
            offer.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status == OfferStatus.AgreedForNextWindow) == true;
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
        string ContractText,
        string WageText,
        string ContractStatusText,
        string Status,
        string StatusTooltip,
        string Reason,
        string PlayerMeta,
        string StatusBrush,
        string FormBrush,
        string FormTextBrush);

    private record TraitBadgeRow(string Icon, string Label, string Tooltip);

    private record StatusDisplay(string Text, string Brush, string Tooltip);

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
