using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Services;

namespace FootballSimulation.Wpf.Views;

public partial class TransferPlayerDetailPanel : UserControl
{
    public event EventHandler? MakeOfferRequested;
    public event EventHandler? ShortlistToggled;
    public event EventHandler? TransferListToggled;
    public event EventHandler? AcceptOfferRequested;
    public event EventHandler? RejectOfferRequested;
    public event EventHandler? CounterOfferRequested;
    public event EventHandler? ContractRenewalRequested;
    public event EventHandler? CaptainAssignmentRequested;
    public event EventHandler? TransferLockToggled;

    private bool _usesTransferListToggle;

    public TransferPlayerDetailPanel()
    {
        InitializeComponent();
        InitializeContractControls();
        ShowEmpty();
    }

    public string OfferFeeText => OfferFeeTextBox.Text;

    public string CounterFeeText => CounterFeeTextBox.Text;

    public string RenewalWageText => RenewalWageTextBox.Text;

    public int RenewalYears => RenewalYearsComboBox.SelectedItem is ComboBoxItem { Tag: int years } ? years : 3;

    public PlayerRole RenewalRole => RenewalRoleComboBox.SelectedItem is ComboBoxItem { Tag: PlayerRole role } ? role : PlayerRole.Rotation;

    public void ShowEmpty()
    {
        EmptyStatePanel.Visibility = Visibility.Visible;
        DetailContentPanel.Visibility = Visibility.Collapsed;
        CornerActionButton.Visibility = Visibility.Collapsed;
        CaptainActionButton.Visibility = Visibility.Collapsed;
        LockActionButton.Visibility = Visibility.Collapsed;
    }

    public void ShowPlayer(TransferPlayerDetailContext context)
    {
        var listing = context.Listing;
        var player = listing.Player;
        var stat = context.Stat;

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;
        UpdateCornerAction(context);
        UpdateCaptainAction(context);
        UpdateLockAction(context);
        ClubLogoImage.Source = ClubLogoService.LoadClubLogo(listing.Team.Name, listing.LeagueId);
        NameTextBlock.Text = player.Name;
        ClubTextBlock.Text = $"{listing.Team.Name} · {listing.LeagueName} · {player.PreferredPosition} · Age {player.Age?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}";
        OverallBadgeTextBlock.Text = player.OverallRating.ToString(CultureInfo.InvariantCulture);
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        NationalityFlagImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(nationality.FlagImagePath, UriKind.Relative));
        NationalityBadgeTextBlock.Text = nationality.Name;
        NationalityBadgeBorder.ToolTip = nationality.Name;

        var formBadge = PlayerFormBadgeHelper.Create(player.FormStatus);
        FormBadgeBorder.Background = ToBrush(formBadge.Background);
        FormBadgeTextBlock.Foreground = ToBrush(formBadge.Foreground);
        FormBadgeTextBlock.Text = formBadge.Text;

        StatusBadgeBorder.Background = ToBrush(context.StatusBrush);
        StatusBadgeBorder.ToolTip = context.StatusTooltip;
        StatusBadgeTextBlock.Text = context.StatusText;
        ContractBadgeTextBlock.Text = PlayerContractService.FormatContractExpiry(player);
        WageBadgeTextBlock.Text = PlayerContractService.FormatWage(listing.WeeklyWage);
        ContractExpiryTextBlock.Text = PlayerContractService.FormatContractExpiry(player);
        ContractWageTextBlock.Text = PlayerContractService.FormatWage(listing.WeeklyWage);
        ContractRoleTextBlock.Text = PlayerContractService.FormatRole(player.Role);
        ReleaseClauseBadgeBorder.Visibility = player.ReleaseClause is > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReleaseClauseTextBlock.Text = player.ReleaseClause is > 0 ? $"Release Clause {TransferMarketService.FormatMoney(player.ReleaseClause.Value)}" : string.Empty;
        ContractWarningTextBlock.Visibility = context.IsOwnPlayer && player.ContractStatus is PlayerContractStatus.ExpiringSoon or PlayerContractStatus.PreContractEligible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ContractWarningTextBlock.Text = player.ContractStatus == PlayerContractStatus.PreContractEligible
            ? "Contract warning: this player can now be approached on a pre-contract."
            : "Contract warning: this player's deal is expiring soon.";
        MarketValueTextBlock.Text = TransferMarketService.FormatMoney(listing.MarketValue);
        PriceLabelTextBlock.Text = context.Mode is TransferDetailMode.Squad ? "Sale Price" : "Asking Price";
        PriceTextBlock.Text = TransferMarketService.FormatMoney(listing.AskingPrice);
        var difficulty = CreateNegotiationDifficulty(listing);
        NegotiationDifficultyTextBlock.Text = difficulty.Label;
        NegotiationDifficultyBadgeBorder.Background = ToBrush(difficulty.Brush);
        NegotiationDifficultyBadgeBorder.ToolTip = difficulty.Tooltip;
        GoalsTextBlock.Text = (stat?.Goals ?? 0).ToString(CultureInfo.InvariantCulture);
        AssistsTextBlock.Text = (stat?.Assists ?? 0).ToString(CultureInfo.InvariantCulture);
        MatchesTextBlock.Text = (stat?.Appearances ?? 0).ToString(CultureInfo.InvariantCulture);
        RatingTextBlock.Text = stat is { Appearances: > 0 }
            ? stat.AverageRating.ToString("0.00", CultureInfo.InvariantCulture)
            : "-";
        TraitsItemsControl.ItemsSource = CreateTraitBadges(player);
        AttributeItemsControl.ItemsSource = CreateAttributeRows(player);
        OfferFeeTextBox.Text = (listing.AskingPrice / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture);
        RenewalWageTextBox.Text = ((listing.WeeklyWage * 1.10m) / 1_000m).ToString("0", CultureInfo.InvariantCulture);
        SelectRenewalYears(PlayerContractService.GetYearsRemaining(player) <= 1 ? 4 : 3);
        SelectRenewalRole(player.Role);
        CounterFeeTextBox.Text = context.Offer is null
            ? string.Empty
            : ((context.Offer.CounterFee ?? context.Offer.Fee) / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture);

        RecommendationTextBlock.Visibility = context.Mode is TransferDetailMode.Scout && !string.IsNullOrWhiteSpace(context.RecommendationReason)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecommendationTextBlock.Text = string.IsNullOrWhiteSpace(context.RecommendationReason)
            ? string.Empty
            : $"Recommendation: {context.RecommendationReason}";

        UpdateOfferInfo(context);
        UpdateActionPanels(context);
    }

    private void UpdateOfferInfo(TransferPlayerDetailContext context)
    {
        OfferInfoPanel.Visibility = Visibility.Collapsed;
        OfferInfoTitleTextBlock.Text = string.Empty;
        OfferInfoTextBlock.Text = string.Empty;

        if (context.Mode is TransferDetailMode.Offers && context.Offer is not null)
        {
            OfferInfoPanel.Visibility = Visibility.Visible;
            OfferInfoTitleTextBlock.Text = "Offer Details";
            OfferInfoTextBlock.Text = context.Offer.Status switch
            {
                OfferStatus.PendingUntilWindowOpens => $"{context.Offer.ToClubName} want to sign {context.Offer.PlayerName} when the transfer window opens.",
                OfferStatus.AgreedForNextWindow => "Transfer will complete when the transfer window opens.",
                OfferStatus.CompletedWhenWindowOpens => $"{context.Offer.PlayerName} joined {context.Offer.ToClubName} when the transfer window opened.",
                _ => $"{context.Offer.FromClubName} offered {TransferMarketService.FormatMoney(context.Offer.CounterFee ?? context.Offer.Fee)} to {context.Offer.ToClubName}."
            };
            PriceLabelTextBlock.Text = "Offer Amount";
            PriceTextBlock.Text = TransferMarketService.FormatMoney(context.Offer.CounterFee ?? context.Offer.Fee);
            return;
        }

        if (context.Mode is TransferDetailMode.History && context.HistoryItem is not null)
        {
            OfferInfoPanel.Visibility = Visibility.Visible;
            OfferInfoTitleTextBlock.Text = "Transfer Details";
            OfferInfoTextBlock.Text = $"{context.HistoryItem.FromClubName} to {context.HistoryItem.ToClubName} for {TransferMarketService.FormatMoney(context.HistoryItem.Fee)} ({context.HistoryItem.Type}).";
            PriceLabelTextBlock.Text = "Transfer Fee";
            PriceTextBlock.Text = TransferMarketService.FormatMoney(context.HistoryItem.Fee);
        }
    }

    private void UpdateActionPanels(TransferPlayerDetailContext context)
    {
        TransferActionPanel.Visibility = context.Mode is TransferDetailMode.Market or TransferDetailMode.Scout
            ? Visibility.Visible
            : Visibility.Collapsed;
        SquadActionPanel.Visibility = context.Mode is TransferDetailMode.Squad && context.IsOwnPlayer ? Visibility.Visible : Visibility.Collapsed;
        OfferActionPanel.Visibility = context.Mode is TransferDetailMode.Offers ? Visibility.Visible : Visibility.Collapsed;

        var tooltip = context.IsTransferWindowOpen ? null : context.TransferWindowTooltip;
        if (context.IsOwnPlayer && context.Mode is TransferDetailMode.Market or TransferDetailMode.Scout)
        {
            tooltip = "This player is already in your squad";
        }

        var canRespondToOffer = context.Offer is not null &&
            context.Offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered;
        var isPreWindowAiOffer = context.Offer is { IsUserOffer: false, Status: OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered } &&
            !context.IsTransferWindowOpen;

        MakeOfferButton.IsEnabled = context.IsTransferWindowOpen && !context.IsOwnPlayer;
        OfferFeeTextBox.IsEnabled = MakeOfferButton.IsEnabled;
        AcceptOfferButton.Content = isPreWindowAiOffer ? "Accept Agreement" : "Accept";
        AcceptOfferButton.Width = isPreWindowAiOffer ? 142 : 90;
        AcceptOfferButton.IsEnabled = canRespondToOffer && (context.IsTransferWindowOpen || !context.Offer!.IsUserOffer);
        RejectOfferButton.IsEnabled = canRespondToOffer;
        CounterOfferButton.IsEnabled = canRespondToOffer;
        CounterFeeTextBox.IsEnabled = canRespondToOffer;

        MakeOfferButton.ToolTip = tooltip;
        OfferFeeTextBox.ToolTip = tooltip ?? "Offer fee in millions";
        AcceptOfferButton.ToolTip = isPreWindowAiOffer ? "Agree now; transfer completes when the window opens." : context.IsTransferWindowOpen ? null : context.TransferWindowTooltip;
        RejectOfferButton.ToolTip = canRespondToOffer ? null : "Offer is no longer active.";
        CounterOfferButton.ToolTip = canRespondToOffer ? null : "Offer is no longer active.";
        CounterFeeTextBox.ToolTip = canRespondToOffer ? "Counter fee in millions" : "Offer is no longer active.";
        RenewContractButton.IsEnabled = context.Mode is TransferDetailMode.Squad && context.IsOwnPlayer;
    }

    private void UpdateCornerAction(TransferPlayerDetailContext context)
    {
        _usesTransferListToggle = context.IsOwnPlayer;
        CornerActionButton.Visibility = Visibility.Visible;
        CornerActionButton.IsEnabled = context.CanToggleShortlist;

        if (_usesTransferListToggle)
        {
            CornerActionButton.Content = "💰";
            CornerActionButton.Background = ToBrush(context.IsListedForSale ? "#0B2A1B" : "#061226");
            CornerActionButton.Foreground = ToBrush(context.IsListedForSale ? "#22C55E" : "#FFFFFF");
            CornerActionButton.BorderBrush = ToBrush(context.IsListedForSale ? "#22C55E" : "#233756");
            SetActionGlow(CornerActionButton, context.IsListedForSale ? "#22C55E" : "#0F172A", context.IsListedForSale ? 0.50 : 0.45);
            CornerActionButton.ToolTip = context.CanToggleShortlist
                ? context.IsListedForSale ? "Remove from transfer list" : "Put on transfer list"
                : "Transfer list cannot be changed from history.";
            return;
        }

        CornerActionButton.Content = context.IsShortlisted ? "★" : "☆";
        CornerActionButton.Background = ToBrush("#061226");
        CornerActionButton.Foreground = ToBrush(context.IsShortlisted ? "#FACC15" : "#FFFFFF");
        CornerActionButton.BorderBrush = ToBrush(context.IsShortlisted ? "#FACC15" : "#233756");
        SetActionGlow(CornerActionButton, context.IsShortlisted ? "#FACC15" : "#0F172A", context.IsShortlisted ? 0.42 : 0.45);
        CornerActionButton.ToolTip = context.CanToggleShortlist
            ? context.IsShortlisted ? "Remove from shortlist" : "Add to shortlist"
            : "Shortlist cannot be changed from history.";
    }

    private void UpdateCaptainAction(TransferPlayerDetailContext context)
    {
        var showCaptainAction = context.Mode is TransferDetailMode.Squad &&
            context.IsOwnPlayer &&
            context.CanAssignCaptain;

        CaptainActionButton.Visibility = showCaptainAction ? Visibility.Visible : Visibility.Collapsed;
        if (!showCaptainAction)
        {
            return;
        }

        CaptainActionButton.Background = ToBrush(context.IsCaptain ? "#FACC15" : "#061226");
        CaptainActionButton.Foreground = ToBrush(context.IsCaptain ? "#061226" : "#FFFFFF");
        CaptainActionButton.BorderBrush = ToBrush(context.IsCaptain ? "#EAB308" : "#233756");
        CaptainActionButton.ToolTip = context.IsCaptain ? "Current Team Captain" : "Assign as Captain";
        SetActionGlow(CaptainActionButton, context.IsCaptain ? "#FACC15" : "#0F172A", context.IsCaptain ? 0.50 : 0.45);
    }

    private void UpdateLockAction(TransferPlayerDetailContext context)
    {
        var showLockAction = context.Mode is TransferDetailMode.Squad &&
            context.IsOwnPlayer &&
            context.CanToggleTransferLock;

        LockActionButton.Visibility = showLockAction ? Visibility.Visible : Visibility.Collapsed;
        if (!showLockAction)
        {
            return;
        }

        LockActionButton.Content = context.IsTransferLocked ? "🔒" : "🔓";
        LockActionButton.Background = ToBrush(context.IsTransferLocked ? "#111827" : "#061226");
        LockActionButton.Foreground = ToBrush(context.IsTransferLocked ? "#FACC15" : "#FFFFFF");
        LockActionButton.BorderBrush = ToBrush(context.IsTransferLocked ? "#FACC15" : "#233756");
        LockActionButton.ToolTip = context.IsTransferLocked
            ? "Untouchable: AI clubs will not send new offers. Click to unlock."
            : "Mark as untouchable: block future AI transfer offers.";
        SetActionGlow(LockActionButton, context.IsTransferLocked ? "#FACC15" : "#0F172A", context.IsTransferLocked ? 0.50 : 0.45);
    }

    private static void SetActionGlow(Button button, string color, double opacity)
    {
        button.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 9,
            ShadowDepth = 0,
            Opacity = opacity,
            Color = (Color)ColorConverter.ConvertFromString(color)!
        };
    }

    private void MakeOfferButton_Click(object sender, RoutedEventArgs e)
    {
        MakeOfferRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CornerActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_usesTransferListToggle)
        {
            TransferListToggled?.Invoke(this, EventArgs.Empty);
            return;
        }

        ShortlistToggled?.Invoke(this, EventArgs.Empty);
    }

    private void CaptainActionButton_Click(object sender, RoutedEventArgs e)
    {
        CaptainAssignmentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LockActionButton_Click(object sender, RoutedEventArgs e)
    {
        TransferLockToggled?.Invoke(this, EventArgs.Empty);
    }

    private void AcceptOfferButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptOfferRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RejectOfferButton_Click(object sender, RoutedEventArgs e)
    {
        RejectOfferRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CounterOfferButton_Click(object sender, RoutedEventArgs e)
    {
        CounterOfferRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RenewContractButton_Click(object sender, RoutedEventArgs e)
    {
        ContractRenewalRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InitializeContractControls()
    {
        RenewalYearsComboBox.Items.Clear();
        foreach (var years in Enumerable.Range(1, 5))
        {
            RenewalYearsComboBox.Items.Add(new ComboBoxItem { Content = $"{years} Years", Tag = years });
        }

        RenewalRoleComboBox.Items.Clear();
        foreach (var role in Enum.GetValues<PlayerRole>())
        {
            RenewalRoleComboBox.Items.Add(new ComboBoxItem { Content = PlayerContractService.FormatRole(role), Tag = role });
        }

        RenewalYearsComboBox.SelectedIndex = 2;
        RenewalRoleComboBox.SelectedIndex = 2;
    }

    private void SelectRenewalYears(int years)
    {
        foreach (ComboBoxItem item in RenewalYearsComboBox.Items)
        {
            if (item.Tag is int value && value == years)
            {
                RenewalYearsComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SelectRenewalRole(PlayerRole role)
    {
        foreach (ComboBoxItem item in RenewalRoleComboBox.Items)
        {
            if (item.Tag is PlayerRole value && value == role)
            {
                RenewalRoleComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static IEnumerable<TraitBadgeRow> CreateTraitBadges(Player player)
    {
        if (player.Traits.Count == 0)
        {
            return [new TraitBadgeRow("*", "No traits", "No special traits")];
        }

        return player.Traits.Select(trait => new TraitBadgeRow(
            PlayerTraitDisplayService.GetIcon(trait),
            PlayerTraitDisplayService.GetLabel(trait),
            $"{PlayerTraitDisplayService.GetLabel(trait)}: {PlayerTraitDisplayService.GetEffectDescription(trait)}"));
    }

    private static IEnumerable<AttributeRow> CreateAttributeRows(Player player)
    {
        var attributes = PlayerAttributeService.GetAttributes(player);
        return
        [
            new AttributeRow("⚡", "Pace", attributes.Pace.ToString(CultureInfo.InvariantCulture)),
            new AttributeRow("🎯", "Shooting", attributes.Shooting.ToString(CultureInfo.InvariantCulture)),
            new AttributeRow("↗", "Passing", attributes.Passing.ToString(CultureInfo.InvariantCulture)),
            new AttributeRow("✦", "Dribbling", attributes.Dribbling.ToString(CultureInfo.InvariantCulture)),
            new AttributeRow("🛡", "Defending", attributes.Defending.ToString(CultureInfo.InvariantCulture)),
            new AttributeRow("💪", "Physical", attributes.Physical.ToString(CultureInfo.InvariantCulture))
        ];
    }

    private static NegotiationDifficulty CreateNegotiationDifficulty(TransferPlayerListing listing)
    {
        var ratio = listing.MarketValue <= 0 ? 1m : listing.AskingPrice / listing.MarketValue;
        return ratio switch
        {
            <= 1.05m => new NegotiationDifficulty("Easy", "#10B981", "Seller is close to market value."),
            <= 1.20m => new NegotiationDifficulty("Balanced", "#2563EB", "Seller expects a fair market offer."),
            <= 1.35m => new NegotiationDifficulty("Expensive", "#F59E0B", "Seller will likely counter fair offers."),
            <= 1.55m => new NegotiationDifficulty("Very Expensive", "#EA580C", "Seller values the player well above market."),
            _ => new NegotiationDifficulty("Untouchable", "#061226", "Seller is strongly resisting a transfer.")
        };
    }

    private static Brush ToBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }

    private record TraitBadgeRow(string Icon, string Label, string Tooltip);
    private record AttributeRow(string Icon, string Label, string Value);
    private record NegotiationDifficulty(string Label, string Brush, string Tooltip);
}

public enum TransferDetailMode
{
    Market,
    Scout,
    Squad,
    Offers,
    History
}

public sealed record TransferPlayerDetailContext(
    TransferPlayerListing Listing,
    TransferDetailMode Mode,
    PlayerSeasonStats? Stat,
    bool IsOwnPlayer,
    bool IsTransferWindowOpen,
    string? TransferWindowTooltip,
    bool IsShortlisted,
    bool IsListedForSale,
    bool CanToggleShortlist,
    string StatusText,
    string StatusBrush,
    string StatusTooltip,
    string? RecommendationReason = null,
    TransferOffer? Offer = null,
    TransferHistoryItem? HistoryItem = null,
    bool CanAssignCaptain = false,
    bool IsCaptain = false,
    bool CanToggleTransferLock = false,
    bool IsTransferLocked = false);
