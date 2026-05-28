using System.Globalization;
using System.Text;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class TransferMarketService
{
    private readonly LeagueDataService _leagueDataService;
    private readonly ClubFinanceService _financeService;
    private readonly PlayerMarketValueCalculator _valueCalculator;
    private readonly TransferWindowService _windowService;
    private readonly Random _random;

    public TransferMarketService()
        : this(new LeagueDataService(), new ClubFinanceService(), new PlayerMarketValueCalculator(), new TransferWindowService())
    {
    }

    public TransferMarketService(
        LeagueDataService leagueDataService,
        ClubFinanceService financeService,
        PlayerMarketValueCalculator valueCalculator,
        TransferWindowService windowService,
        int seed = 42)
    {
        _leagueDataService = leagueDataService;
        _financeService = financeService;
        _valueCalculator = valueCalculator;
        _windowService = windowService;
        _random = new Random(seed);
    }

    public TransferMarketState CreateInitialState(League activeLeague)
    {
        ArgumentNullException.ThrowIfNull(activeLeague);

        var definitions = _leagueDataService.LoadLeagueDefinitions(includePlaceholders: false);
        var state = new TransferMarketState
        {
            ActiveSeason = definitions.FirstOrDefault()?.Season ?? activeLeague.Season
        };

        foreach (var definition in definitions)
        {
            var teams = definition.LeagueId.Equals(activeLeague.LeagueId, StringComparison.OrdinalIgnoreCase)
                ? activeLeague.Teams
                : _leagueDataService.LoadTeams(definition);

            EnsureTeamPlayers(teams, definition.LeagueId);
            EnrichMissingPlayerData(teams, definition.LeagueId);
            state.Leagues.Add(new TransferLeagueState
            {
                LeagueId = definition.LeagueId,
                LeagueName = definition.Name,
                Season = definition.Season,
                Teams = teams
            });

            foreach (var team in teams)
            {
                _financeService.GetOrCreateFinance(state, definition.LeagueId, team);
            }
        }

        return state;
    }

    public void BindActiveLeague(TransferMarketState state, League activeLeague)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(activeLeague);

        var leagueState = state.Leagues.FirstOrDefault(league =>
            league.LeagueId.Equals(activeLeague.LeagueId, StringComparison.OrdinalIgnoreCase));

        if (leagueState is null)
        {
            leagueState = new TransferLeagueState
            {
                LeagueId = activeLeague.LeagueId,
                LeagueName = activeLeague.Name,
                Season = activeLeague.Season
            };
            state.Leagues.Add(leagueState);
        }

        leagueState.LeagueName = activeLeague.Name;
        leagueState.Season = activeLeague.Season;
        leagueState.Teams = activeLeague.Teams;
        EnsureTeamPlayers(activeLeague.Teams, activeLeague.LeagueId);
        EnrichMissingPlayerData(activeLeague.Teams, activeLeague.LeagueId);

        foreach (var team in activeLeague.Teams)
        {
            _financeService.GetOrCreateFinance(state, activeLeague.LeagueId, team);
        }
    }

    public IReadOnlyList<TransferPlayerListing> SearchPlayers(
        TransferMarketState state,
        TransferSearchCriteria criteria,
        IEnumerable<PlayerSeasonStats>? stats = null)
    {
        return GetAllListings(state, stats)
            .Where(listing => MatchesCriteria(listing, criteria))
            .OrderByDescending(listing => listing.Player.OverallRating)
            .ThenBy(listing => listing.AskingPrice)
            .ThenBy(listing => listing.Player.Name)
            .ToList();
    }

    public IReadOnlyList<TransferPlayerListing> GetAllPlayerListings(
        TransferMarketState state,
        IEnumerable<PlayerSeasonStats>? stats = null)
    {
        return GetAllListings(state, stats);
    }

    public IReadOnlyList<TransferPlayerListing> GetClubListings(
        TransferMarketState state,
        string leagueId,
        Team team,
        IEnumerable<PlayerSeasonStats>? stats = null)
    {
        return GetAllListings(state, stats)
            .Where(listing => listing.LeagueId.Equals(leagueId, StringComparison.OrdinalIgnoreCase) && listing.Team == team)
            .OrderByDescending(listing => listing.Player.OverallRating)
            .ThenBy(listing => listing.Player.Name)
            .ToList();
    }

    public IReadOnlyList<TransferRecommendation> GetRecommendedPlayers(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        int maxCount = 18)
    {
        var userFinance = _financeService.GetOrCreateFinance(state, activeLeague.LeagueId, selectedTeam);
        var weakPositions = GetWeakPositions(selectedTeam);
        var ownPlayerIds = selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .Select(player => player.PlayerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetAllListings(state, activeLeague.PlayerStats)
            .Where(listing => !ownPlayerIds.Contains(listing.Player.PlayerId))
            .Where(listing => listing.AskingPrice <= userFinance.AvailableTransferBudget)
            .Select(listing => new
            {
                Listing = listing,
                Score = ScoreRecommendation(listing, weakPositions, selectedTeam),
                Reason = CreateRecommendationReason(listing, weakPositions, selectedTeam)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Listing.AskingPrice)
            .Take(maxCount)
            .Select(item => new TransferRecommendation(item.Listing, item.Reason))
            .ToList();
    }

    public TransferOffer MakeUserOffer(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        string playerId,
        decimal fee,
        int currentRound)
    {
        if (!_windowService.IsWindowOpen(activeLeague, currentRound))
        {
            return CreateBlockedOffer(state, playerId, selectedTeam, fee, currentRound, TransferNotificationType.WindowClosed, "Transfer window is closed.");
        }

        var listing = GetAllListings(state, activeLeague.PlayerStats).First(item => item.Player.PlayerId == playerId);
        if (listing.Team == selectedTeam)
        {
            return CreateBlockedOffer(state, playerId, selectedTeam, fee, currentRound, TransferNotificationType.Info, "You cannot bid for your own player.");
        }

        var buyerFinance = _financeService.GetOrCreateFinance(state, activeLeague.LeagueId, selectedTeam);
        if (buyerFinance.AvailableTransferBudget < fee)
        {
            return CreateBlockedOffer(state, playerId, selectedTeam, fee, currentRound, TransferNotificationType.InsufficientBudget, "Insufficient transfer budget.");
        }

        var offer = new TransferOffer
        {
            PlayerId = listing.Player.PlayerId,
            PlayerName = listing.Player.Name,
            FromLeagueId = listing.LeagueId,
            FromClubName = listing.Team.Name,
            ToLeagueId = activeLeague.LeagueId,
            ToClubName = selectedTeam.Name,
            Fee = fee,
            CreatedRound = currentRound,
            IsUserOffer = true
        };

        EvaluateSellingClubResponse(state, offer, listing, activeLeague, selectedTeam, currentRound);
        state.Offers.Add(offer);
        return offer;
    }

    public TransferOffer CreateAiOfferForUserPlayer(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        Player player,
        int currentRound)
    {
        var candidates = GetAllTeams(state)
            .Where(item => item.Team != selectedTeam)
            .Where(item => _financeService.GetOrCreateFinance(state, item.LeagueId, item.Team).AvailableTransferBudget > 1_000_000)
            .OrderByDescending(item => item.Team.Players.Concat(item.Team.Substitutes).Average(candidate => candidate.OverallRating))
            .Take(12)
            .ToList();

        var buyer = candidates.Count == 0 ? default : candidates[_random.Next(candidates.Count)];
        if (buyer.Team is null)
        {
            throw new InvalidOperationException("No AI club can afford to make an offer.");
        }

        var marketValue = _valueCalculator.CalculateMarketValue(player, activeLeague.LeagueId, activeLeague.PlayerStats);
        var listedDiscount = player.TransferStatus == PlayerTransferStatus.Listed ? 0.95m : 1.05m;
        var fee = RoundFee(marketValue * listedDiscount);
        var offer = new TransferOffer
        {
            PlayerId = player.PlayerId,
            PlayerName = player.Name,
            FromLeagueId = activeLeague.LeagueId,
            FromClubName = selectedTeam.Name,
            ToLeagueId = buyer.LeagueId,
            ToClubName = buyer.Team.Name,
            Fee = fee,
            Status = OfferStatus.Pending,
            CreatedRound = currentRound,
            IsUserOffer = false,
            Message = $"{buyer.Team.Name} want to buy {player.Name}."
        };

        player.TransferStatus = PlayerTransferStatus.Negotiating;
        state.Offers.Add(offer);
        AddNotification(state, TransferNotificationType.AiOfferReceived, offer.Message, currentRound, offer.OfferId);
        return offer;
    }

    public void GenerateAiOffersForUserPlayers(TransferMarketState state, League activeLeague, Team selectedTeam, int currentRound)
    {
        if (!_windowService.IsWindowOpen(activeLeague, currentRound))
        {
            return;
        }

        var existingPlayerIds = state.Offers
            .Where(offer => offer.Status is OfferStatus.Pending or OfferStatus.Countered)
            .Select(offer => offer.PlayerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targets = selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .Where(player => !player.RejectTransferOffers)
            .Where(player => !existingPlayerIds.Contains(player.PlayerId))
            .OrderByDescending(player => player.TransferStatus == PlayerTransferStatus.Listed)
            .ThenByDescending(player => player.OverallRating)
            .Take(2)
            .ToList();

        foreach (var target in targets.Take(_random.Next(0, 2)))
        {
            CreateAiOfferForUserPlayer(state, activeLeague, selectedTeam, target, currentRound);
        }
    }

    public void RunAiTransferActivity(TransferMarketState state, League activeLeague, Team selectedTeam, int currentRound)
    {
        if (!_windowService.IsWindowOpen(activeLeague, currentRound) || state.LastAiActivityRound == currentRound)
        {
            return;
        }

        state.LastAiActivityRound = currentRound;
        GenerateAiOffersForUserPlayers(state, activeLeague, selectedTeam, currentRound);

        if (_random.NextDouble() > 0.35)
        {
            return;
        }

        var listings = GetAllListings(state, activeLeague.PlayerStats)
            .Where(listing => listing.Team != selectedTeam)
            .Where(listing => listing.Player.Role is PlayerRole.Rotation or PlayerRole.Backup or PlayerRole.Prospect)
            .OrderBy(_ => _random.Next())
            .Take(20)
            .ToList();

        foreach (var listing in listings)
        {
            var buyer = GetAllTeams(state)
                .Where(item => item.Team != listing.Team && item.Team != selectedTeam)
                .OrderBy(_ => _random.Next())
                .FirstOrDefault();

            if (buyer.Team is null)
            {
                continue;
            }

            var finance = _financeService.GetOrCreateFinance(state, buyer.LeagueId, buyer.Team);
            if (finance.AvailableTransferBudget < listing.MarketValue)
            {
                continue;
            }

            CompleteTransfer(state, listing, buyer.LeagueId, buyer.Team, RoundFee(listing.MarketValue), currentRound, "AI Transfer");
            return;
        }
    }

    public void ListPlayer(TransferMarketState state, League activeLeague, Team selectedTeam, Player player, int currentRound)
    {
        if (!_windowService.IsWindowOpen(activeLeague, currentRound))
        {
            AddNotification(state, TransferNotificationType.WindowClosed, "Transfer window is closed.", currentRound);
            return;
        }

        player.TransferStatus = PlayerTransferStatus.Listed;
    }

    public void RemovePlayerFromSale(Player player)
    {
        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            player.TransferStatus = PlayerTransferStatus.None;
        }
    }

    public void AcceptOffer(TransferMarketState state, string offerId, League activeLeague, int currentRound)
    {
        var offer = state.Offers.First(item => item.OfferId == offerId);
        var listing = GetAllListings(state, activeLeague.PlayerStats).First(item => item.Player.PlayerId == offer.PlayerId);
        var buyer = FindTeam(state, offer.ToLeagueId, offer.ToClubName);
        var fee = offer.CounterFee ?? offer.Fee;

        CompleteTransfer(state, listing, offer.ToLeagueId, buyer, fee, currentRound, "Permanent");
        offer.Fee = fee;
        offer.Status = OfferStatus.Completed;
        offer.Message = $"Transfer completed: {offer.PlayerName} joined {offer.ToClubName} for {FormatMoney(fee)}.";
    }

    public void RejectOffer(TransferMarketState state, string offerId)
    {
        var offer = state.Offers.First(item => item.OfferId == offerId);
        offer.Status = OfferStatus.Rejected;
        offer.Message = "Offer rejected.";

        var player = FindPlayer(state, offer.PlayerId);
        if (player.TransferStatus == PlayerTransferStatus.Negotiating)
        {
            player.TransferStatus = PlayerTransferStatus.None;
        }
    }

    public void CounterOffer(TransferMarketState state, string offerId, decimal counterFee, League activeLeague, int currentRound)
    {
        var offer = state.Offers.First(item => item.OfferId == offerId);
        offer.CounterFee = counterFee;
        offer.Status = OfferStatus.Countered;
        offer.Message = $"Counter offer sent at {FormatMoney(counterFee)}.";

        if (offer.IsUserOffer)
        {
            var listing = GetAllListings(state, activeLeague.PlayerStats).First(item => item.Player.PlayerId == offer.PlayerId);
            EvaluateSellingClubResponse(state, offer, listing, activeLeague, FindTeam(state, offer.ToLeagueId, offer.ToClubName), currentRound);
        }
        else if (counterFee <= offer.Fee * 1.2m)
        {
            AcceptOffer(state, offerId, activeLeague, currentRound);
        }
        else
        {
            offer.Status = OfferStatus.Rejected;
            offer.Message = $"{offer.ToClubName} withdrew after the counter offer.";
        }
    }

    public ClubFinance GetFinance(TransferMarketState state, string leagueId, Team team)
    {
        return _financeService.GetOrCreateFinance(state, leagueId, team);
    }

    public TransferWindowInfo GetWindowInfo(League league, int currentRound)
    {
        return _windowService.GetWindowInfo(league, currentRound);
    }

    public decimal GetMarketValue(Player player, string leagueId, IEnumerable<PlayerSeasonStats>? stats = null)
    {
        return _valueCalculator.CalculateMarketValue(player, leagueId, stats);
    }

    public decimal GetAskingPrice(Player player, string leagueId, IEnumerable<PlayerSeasonStats>? stats = null)
    {
        return _valueCalculator.CalculateAskingPrice(player, leagueId, stats);
    }

    private void EvaluateSellingClubResponse(
        TransferMarketState state,
        TransferOffer offer,
        TransferPlayerListing listing,
        League activeLeague,
        Team selectedTeam,
        int currentRound)
    {
        var askingPrice = listing.AskingPrice;
        var offerFee = offer.CounterFee ?? offer.Fee;
        var ratio = askingPrice == 0 ? 1 : offerFee / askingPrice;

        if (ratio < 0.8m)
        {
            offer.Status = OfferStatus.Rejected;
            offer.Message = $"{listing.Team.Name} rejected the offer for {listing.Player.Name}.";
            AddNotification(state, TransferNotificationType.UserOfferRejected, offer.Message, currentRound, offer.OfferId);
            return;
        }

        if (ratio < 0.95m)
        {
            offer.Status = OfferStatus.Countered;
            offer.CounterFee = RoundFee(askingPrice * 1.03m);
            offer.Message = $"{listing.Team.Name} countered at {FormatMoney(offer.CounterFee.Value)}.";
            listing.Player.TransferStatus = PlayerTransferStatus.Negotiating;
            AddNotification(state, TransferNotificationType.UserOfferCountered, offer.Message, currentRound, offer.OfferId);
            return;
        }

        CompleteTransfer(state, listing, activeLeague.LeagueId, selectedTeam, offerFee, currentRound, "Permanent");
        offer.Status = OfferStatus.Completed;
        offer.Message = $"Transfer completed: {listing.Player.Name} joined {selectedTeam.Name} for {FormatMoney(offerFee)}.";
        AddNotification(state, TransferNotificationType.TransferCompleted, offer.Message, currentRound, offer.OfferId);
    }

    private void CompleteTransfer(
        TransferMarketState state,
        TransferPlayerListing listing,
        string toLeagueId,
        Team buyingTeam,
        decimal fee,
        int currentRound,
        string type)
    {
        var sellingTeam = listing.Team;
        var player = listing.Player;
        var buyerFinance = _financeService.GetOrCreateFinance(state, toLeagueId, buyingTeam);
        var sellerFinance = _financeService.GetOrCreateFinance(state, listing.LeagueId, sellingTeam);

        if (buyerFinance.AvailableTransferBudget < fee)
        {
            AddNotification(state, TransferNotificationType.InsufficientBudget, $"{buyingTeam.Name} cannot afford {player.Name}.", currentRound);
            return;
        }

        RemovePlayerFromTeam(sellingTeam, player);
        player.TransferStatus = PlayerTransferStatus.None;
        player.IsStarter = false;
        player.IsOnPitch = false;
        buyingTeam.Substitutes.Add(player);

        buyerFinance.TransferSpent += fee;
        sellerFinance.TransferIncome += fee;

        state.TransferHistory.Add(new TransferHistoryItem
        {
            RoundNumber = currentRound,
            PlayerId = player.PlayerId,
            PlayerName = player.Name,
            FromLeagueId = listing.LeagueId,
            FromClubName = sellingTeam.Name,
            ToLeagueId = toLeagueId,
            ToClubName = buyingTeam.Name,
            Fee = fee,
            Type = type
        });

        AddNotification(state, TransferNotificationType.TransferCompleted, $"Transfer completed: {player.Name} joined {buyingTeam.Name} for {FormatMoney(fee)}.", currentRound);
    }

    private static void RemovePlayerFromTeam(Team team, Player player)
    {
        if (team.Substitutes.Remove(player))
        {
            return;
        }

        if (!team.Players.Remove(player))
        {
            return;
        }

        player.IsStarter = false;
        player.IsOnPitch = false;
        var replacement = team.Substitutes
            .OrderByDescending(candidate => candidate.Position == player.Position)
            .ThenByDescending(candidate => candidate.OverallRating)
            .FirstOrDefault();

        if (replacement is null)
        {
            return;
        }

        team.Substitutes.Remove(replacement);
        replacement.IsStarter = true;
        replacement.IsOnPitch = true;
        team.Players.Add(replacement);
    }

    private TransferOffer CreateBlockedOffer(
        TransferMarketState state,
        string playerId,
        Team selectedTeam,
        decimal fee,
        int currentRound,
        TransferNotificationType type,
        string message)
    {
        var player = FindPlayer(state, playerId);
        var offer = new TransferOffer
        {
            PlayerId = playerId,
            PlayerName = player.Name,
            ToClubName = selectedTeam.Name,
            Fee = fee,
            Status = OfferStatus.Rejected,
            CreatedRound = currentRound,
            IsUserOffer = true,
            Message = message
        };
        state.Offers.Add(offer);
        AddNotification(state, type, message, currentRound, offer.OfferId);
        return offer;
    }

    private List<TransferPlayerListing> GetAllListings(TransferMarketState state, IEnumerable<PlayerSeasonStats>? stats)
    {
        return state.Leagues
            .SelectMany(league => league.Teams.SelectMany(team =>
                team.Players.Concat(team.Substitutes).Select(player =>
                {
                    EnsurePlayerId(player, league.LeagueId, team.Name);
                    var marketValue = _valueCalculator.CalculateMarketValue(player, league.LeagueId, stats);
                    return new TransferPlayerListing(
                        player,
                        team,
                        league.LeagueId,
                        league.LeagueName,
                        marketValue,
                        _valueCalculator.CalculateAskingPrice(player, league.LeagueId, stats),
                        GetStatusText(player));
                })))
            .ToList();
    }

    private static bool MatchesCriteria(TransferPlayerListing listing, TransferSearchCriteria criteria)
    {
        return MatchesText(listing.Player.Name, criteria.PlayerName) &&
            MatchesText(listing.Team.Name, criteria.ClubName) &&
            MatchesText(listing.LeagueId, criteria.LeagueId) &&
            MatchesPosition(listing.Player, criteria.Position) &&
            (!criteria.MinimumOverall.HasValue || listing.Player.OverallRating >= criteria.MinimumOverall.Value) &&
            (!criteria.MaximumPrice.HasValue || listing.AskingPrice <= criteria.MaximumPrice.Value) &&
            (!criteria.MinimumAge.HasValue || (listing.Player.Age ?? 0) >= criteria.MinimumAge.Value) &&
            (!criteria.MaximumAge.HasValue || (listing.Player.Age ?? 99) <= criteria.MaximumAge.Value) &&
            (string.IsNullOrWhiteSpace(criteria.Trait) || listing.Player.Traits.Any(trait => trait.ToString().Contains(criteria.Trait, StringComparison.OrdinalIgnoreCase))) &&
            (!criteria.FormStatus.HasValue || listing.Player.FormStatus == criteria.FormStatus.Value);
    }

    private static bool MatchesText(string value, string filter)
    {
        return string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPosition(Player player, string position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return true;
        }

        var normalized = PositionSuitabilityService.NormalizeExactPosition(position);
        return player.PreferredPosition.Equals(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<Position, double> GetWeakPositions(Team selectedTeam)
    {
        return Enum.GetValues<Position>()
            .ToDictionary(
                position => position,
                position =>
                {
                    var players = selectedTeam.Players.Concat(selectedTeam.Substitutes)
                        .Where(player => player.Position == position)
                        .ToList();
                    return players.Count == 0 ? 100 : 82 - players.Average(player => player.OverallRating);
                });
    }

    private static double ScoreRecommendation(TransferPlayerListing listing, Dictionary<Position, double> weakPositions, Team selectedTeam)
    {
        var weakness = weakPositions.TryGetValue(listing.Player.Position, out var value) ? value : 0;
        var ageBonus = listing.Player.Age is <= 23 ? 5 : 0;
        var ratingGap = listing.Player.OverallRating - selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .Where(player => player.Position == listing.Player.Position)
            .DefaultIfEmpty()
            .Average(player => player?.OverallRating ?? 70);

        return weakness + Math.Max(0, ratingGap) + ageBonus;
    }

    private static string CreateRecommendationReason(TransferPlayerListing listing, Dictionary<Position, double> weakPositions, Team selectedTeam)
    {
        if (weakPositions.TryGetValue(listing.Player.Position, out var weakness) && weakness > 4)
        {
            return $"Recommended because your {listing.Player.PreferredPosition} depth is weak.";
        }

        if (listing.Player.Age is <= 23)
        {
            return "Recommended as a young high-upside target within budget.";
        }

        return $"Recommended as an upgrade for your {listing.Player.Position.ToString().ToLowerInvariant()} group.";
    }

    private IEnumerable<(string LeagueId, Team Team)> GetAllTeams(TransferMarketState state)
    {
        return state.Leagues.SelectMany(league => league.Teams.Select(team => (league.LeagueId, team)));
    }

    private Team FindTeam(TransferMarketState state, string leagueId, string clubName)
    {
        return state.Leagues
            .Where(league => league.LeagueId.Equals(leagueId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(league => league.Teams)
            .First(team => team.Name.Equals(clubName, StringComparison.OrdinalIgnoreCase));
    }

    private Player FindPlayer(TransferMarketState state, string playerId)
    {
        return state.Leagues
            .SelectMany(league => league.Teams)
            .SelectMany(team => team.Players.Concat(team.Substitutes))
            .First(player => player.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStatusText(Player player)
    {
        return player.TransferStatus switch
        {
            PlayerTransferStatus.Listed => "Listed",
            PlayerTransferStatus.Negotiating => "Negotiating",
            PlayerTransferStatus.Unavailable => "Unavailable",
            PlayerTransferStatus.Transferred => "Transferred",
            _ => player.IsInjured ? "Injured" : "Available"
        };
    }

    private void AddNotification(
        TransferMarketState state,
        TransferNotificationType type,
        string message,
        int currentRound,
        string? relatedOfferId = null)
    {
        state.Inbox.Insert(0, new TransferNotification
        {
            Type = type,
            Message = message,
            CreatedRound = currentRound,
            RelatedOfferId = relatedOfferId
        });
    }

    private void EnrichMissingPlayerData(IEnumerable<Team> teams, string leagueId)
    {
        List<Team> sourceTeams;
        try
        {
            sourceTeams = _leagueDataService.LoadTeams(leagueId);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var sourceRows = sourceTeams
            .SelectMany(team => team.Players.Concat(team.Substitutes)
                .Select(player => new
                {
                    TeamKey = NormalizeKey(team.Name),
                    PlayerKey = NormalizeKey(player.Name),
                    player.SquadNumber,
                    Player = player
                }))
            .ToList();

        var sourcePlayersByTeamNameAndNumber = sourceRows
            .GroupBy(item => $"{item.TeamKey}|{item.PlayerKey}|{item.SquadNumber}")
            .ToDictionary(group => group.Key, group => group.First().Player, StringComparer.OrdinalIgnoreCase);
        var sourcePlayersByTeamAndName = sourceRows
            .GroupBy(item => $"{item.TeamKey}|{item.PlayerKey}")
            .ToDictionary(group => group.Key, group => group.First().Player, StringComparer.OrdinalIgnoreCase);
        var uniqueSourcePlayersByName = sourceRows
            .GroupBy(item => item.PlayerKey)
            .Where(group => group.Select(item => item.Player.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Player, StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            foreach (var player in team.Players.Concat(team.Substitutes))
            {
                var teamKey = NormalizeKey(team.Name);
                var playerKey = NormalizeKey(player.Name);
                var exactKey = $"{teamKey}|{playerKey}|{player.SquadNumber}";
                var teamNameKey = $"{teamKey}|{playerKey}";
                if (!sourcePlayersByTeamNameAndNumber.TryGetValue(exactKey, out var sourcePlayer) &&
                    !sourcePlayersByTeamAndName.TryGetValue(teamNameKey, out sourcePlayer) &&
                    !uniqueSourcePlayersByName.TryGetValue(playerKey, out sourcePlayer))
                {
                    if (PlayerNationalityDataService.IsMissingOrDefault(player))
                    {
                        _ = PlayerNationalityDataService.TryApply(player);
                    }

                    continue;
                }

                player.Age ??= sourcePlayer.Age;
                player.PotentialOverall ??= sourcePlayer.PotentialOverall;
                if (PlayerNationalityDataService.IsMissingOrDefault(player) &&
                    string.IsNullOrWhiteSpace(sourcePlayer.FlagImagePath))
                {
                    _ = PlayerNationalityDataService.TryApply(player);
                }

                if (PlayerNationalityDataService.IsMissingOrDefault(player))
                {
                    player.NationalityCode = sourcePlayer.NationalityCode;
                    player.NationalityName = sourcePlayer.NationalityName;
                    player.Nationality = sourcePlayer.Nationality;
                    player.FlagImagePath = sourcePlayer.FlagImagePath;
                }

                if (PlayerNationalityDataService.IsMissingOrDefault(player))
                {
                    _ = PlayerNationalityDataService.TryApply(player);
                }

                if (string.IsNullOrWhiteSpace(player.PreferredFoot))
                {
                    player.PreferredFoot = sourcePlayer.PreferredFoot;
                }
            }
        }
    }

    private static void EnsureTeamPlayers(IEnumerable<Team> teams, string leagueId)
    {
        foreach (var team in teams)
        {
            foreach (var player in team.Players.Concat(team.Substitutes))
            {
                EnsurePlayerId(player, leagueId, team.Name);
                PlayerAttributeService.ApplyMissingAttributes(player);
                if (player.TransferStatus == PlayerTransferStatus.Transferred)
                {
                    player.TransferStatus = PlayerTransferStatus.None;
                }
            }
        }
    }

    private static void EnsurePlayerId(Player player, string leagueId, string teamName)
    {
        if (!string.IsNullOrWhiteSpace(player.PlayerId) && player.PlayerId.Contains(':', StringComparison.Ordinal))
        {
            return;
        }

        player.PlayerId = $"{leagueId}:{NormalizeKey(teamName)}:{NormalizeKey(player.Name)}:{player.SquadNumber}";
    }

    private static string NormalizeKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static decimal RoundFee(decimal value)
    {
        var step = value >= 20_000_000 ? 500_000 : 100_000;
        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }

    public static string FormatMoney(decimal value)
    {
        return value >= 1_000_000_000
            ? $"€{value / 1_000_000_000:0.#}B"
            : value >= 1_000_000
            ? $"€{value / 1_000_000:0.#}M"
            : $"€{value / 1_000:0}K";
    }
}
