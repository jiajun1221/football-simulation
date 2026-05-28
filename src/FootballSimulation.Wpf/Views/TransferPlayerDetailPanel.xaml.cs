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

    private bool _usesTransferListToggle;

    public TransferPlayerDetailPanel()
    {
        InitializeComponent();
        ShowEmpty();
    }

    public string OfferFeeText => OfferFeeTextBox.Text;

    public string CounterFeeText => CounterFeeTextBox.Text;

    public void ShowEmpty()
    {
        EmptyStatePanel.Visibility = Visibility.Visible;
        DetailContentPanel.Visibility = Visibility.Collapsed;
        CornerActionButton.Visibility = Visibility.Collapsed;
    }

    public void ShowPlayer(TransferPlayerDetailContext context)
    {
        var listing = context.Listing;
        var player = listing.Player;
        var stat = context.Stat;

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;
        UpdateCornerAction(context);
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

        StatusBadgeBorder.Background = ToBrush(GetStatusBrush(listing.StatusText));
        StatusBadgeTextBlock.Text = listing.StatusText;
        MarketValueTextBlock.Text = TransferMarketService.FormatMoney(listing.MarketValue);
        PriceLabelTextBlock.Text = context.Mode is TransferDetailMode.Squad ? "Sale Price" : "Asking Price";
        PriceTextBlock.Text = TransferMarketService.FormatMoney(listing.AskingPrice);
        GoalsTextBlock.Text = (stat?.Goals ?? 0).ToString(CultureInfo.InvariantCulture);
        AssistsTextBlock.Text = (stat?.Assists ?? 0).ToString(CultureInfo.InvariantCulture);
        MatchesTextBlock.Text = (stat?.Appearances ?? 0).ToString(CultureInfo.InvariantCulture);
        RatingTextBlock.Text = stat is { Appearances: > 0 }
            ? stat.AverageRating.ToString("0.00", CultureInfo.InvariantCulture)
            : "-";
        TraitsItemsControl.ItemsSource = CreateTraitBadges(player);
        AttributeItemsControl.ItemsSource = CreateAttributeRows(player);
        OfferFeeTextBox.Text = (listing.AskingPrice / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture);
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
            OfferInfoTextBlock.Text = $"{context.Offer.FromClubName} offered {TransferMarketService.FormatMoney(context.Offer.CounterFee ?? context.Offer.Fee)} to {context.Offer.ToClubName}.";
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
        SquadActionPanel.Visibility = Visibility.Collapsed;
        OfferActionPanel.Visibility = context.Mode is TransferDetailMode.Offers ? Visibility.Visible : Visibility.Collapsed;

        var tooltip = context.IsTransferWindowOpen ? null : context.TransferWindowTooltip;
        if (context.IsOwnPlayer && context.Mode is TransferDetailMode.Market or TransferDetailMode.Scout)
        {
            tooltip = "This player is already in your squad";
        }

        MakeOfferButton.IsEnabled = context.IsTransferWindowOpen && !context.IsOwnPlayer;
        OfferFeeTextBox.IsEnabled = MakeOfferButton.IsEnabled;
        AcceptOfferButton.IsEnabled = context.IsTransferWindowOpen && context.Offer is not null;
        RejectOfferButton.IsEnabled = context.IsTransferWindowOpen && context.Offer is not null;
        CounterOfferButton.IsEnabled = context.IsTransferWindowOpen && context.Offer is not null;
        CounterFeeTextBox.IsEnabled = context.IsTransferWindowOpen && context.Offer is not null;

        MakeOfferButton.ToolTip = tooltip;
        OfferFeeTextBox.ToolTip = tooltip ?? "Offer fee in millions";
        AcceptOfferButton.ToolTip = context.IsTransferWindowOpen ? null : context.TransferWindowTooltip;
        RejectOfferButton.ToolTip = context.IsTransferWindowOpen ? null : context.TransferWindowTooltip;
        CounterOfferButton.ToolTip = context.IsTransferWindowOpen ? null : context.TransferWindowTooltip;
        CounterFeeTextBox.ToolTip = context.IsTransferWindowOpen ? "Counter fee in millions" : context.TransferWindowTooltip;
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
            SetCornerActionGlow(context.IsListedForSale ? "#22C55E" : "#0F172A", context.IsListedForSale ? 0.50 : 0.45);
            CornerActionButton.ToolTip = context.CanToggleShortlist
                ? context.IsListedForSale ? "Remove from transfer list" : "Put on transfer list"
                : "Transfer list cannot be changed from history.";
            return;
        }

        CornerActionButton.Content = context.IsShortlisted ? "★" : "☆";
        CornerActionButton.Background = ToBrush("#061226");
        CornerActionButton.Foreground = ToBrush(context.IsShortlisted ? "#FACC15" : "#FFFFFF");
        CornerActionButton.BorderBrush = ToBrush(context.IsShortlisted ? "#FACC15" : "#233756");
        SetCornerActionGlow(context.IsShortlisted ? "#FACC15" : "#0F172A", context.IsShortlisted ? 0.42 : 0.45);
        CornerActionButton.ToolTip = context.CanToggleShortlist
            ? context.IsShortlisted ? "Remove from shortlist" : "Add to shortlist"
            : "Shortlist cannot be changed from history.";
    }

    private void SetCornerActionGlow(string color, double opacity)
    {
        CornerActionButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
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

    private static Brush ToBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }

    private record TraitBadgeRow(string Icon, string Label, string Tooltip);
    private record AttributeRow(string Icon, string Label, string Value);
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
    string? RecommendationReason = null,
    TransferOffer? Offer = null,
    TransferHistoryItem? HistoryItem = null);
