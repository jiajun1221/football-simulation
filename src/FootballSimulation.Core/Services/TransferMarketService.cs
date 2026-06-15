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

    public TransferMarketService(int seed)
        : this(new LeagueDataService(), new ClubFinanceService(), new PlayerMarketValueCalculator(), new TransferWindowService(), seed)
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

        ProcessExpiredContracts(state, GetSeasonEndYear(state.ActiveSeason));
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

        ProcessExpiredContracts(state, GetSeasonEndYear(activeLeague.Season));
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
        var proposedWage = Math.Max(listing.Player.WeeklyWage ?? 0, PlayerContractService.EstimateWeeklyWage(listing.Player, activeLeague.LeagueId));
        if (buyerFinance.AvailableTransferBudget < fee)
        {
            return CreateBlockedOffer(state, playerId, selectedTeam, fee, currentRound, TransferNotificationType.InsufficientBudget, "Insufficient transfer budget.");
        }

        if (buyerFinance.AvailableWageBudget < proposedWage)
        {
            return CreateBlockedOffer(state, playerId, selectedTeam, fee, currentRound, TransferNotificationType.InsufficientBudget, "Insufficient wage budget.");
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
            WeeklyWage = proposedWage,
            ContractYears = GetNewSigningContractYears(listing.Player),
            SquadRole = listing.Player.Role,
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
        if (player.RejectTransferOffers || player.TransferStatus == PlayerTransferStatus.Unavailable)
        {
            throw new InvalidOperationException($"{player.Name} is marked as untouchable.");
        }

        var marketValue = _valueCalculator.CalculateMarketValue(player, activeLeague.LeagueId, activeLeague.PlayerStats);
        var fee = RoundFee(marketValue * GetAiOfferFeeModifier(player));
        var buyer = SelectAiBuyerForPlayer(state, selectedTeam, player, fee);
        if (buyer is null)
        {
            throw new InvalidOperationException("No AI club can afford to make an offer.");
        }

        var buyerValue = buyer.Value;
        return CreateAiOfferForUserPlayer(state, activeLeague, selectedTeam, player, buyerValue.LeagueId, buyerValue.Team, fee, currentRound);
    }

    private TransferOffer CreateAiOfferForUserPlayer(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        Player player,
        string buyerLeagueId,
        Team buyerTeam,
        decimal fee,
        int currentRound)
    {
        var isWindowOpen = _windowService.IsWindowOpen(activeLeague, currentRound);
        var offer = new TransferOffer
        {
            PlayerId = player.PlayerId,
            PlayerName = player.Name,
            FromLeagueId = activeLeague.LeagueId,
            FromClubName = selectedTeam.Name,
            ToLeagueId = buyerLeagueId,
            ToClubName = buyerTeam.Name,
            Fee = fee,
            Status = isWindowOpen ? OfferStatus.Pending : OfferStatus.PendingUntilWindowOpens,
            CreatedRound = currentRound,
            IsUserOffer = false,
            Message = isWindowOpen
                ? $"{buyerTeam.Name} offer {FormatMoney(fee)} for {player.Name}."
                : $"{buyerTeam.Name} want to sign {player.Name} when the transfer window opens."
        };

        if (player.TransferStatus != PlayerTransferStatus.Listed)
        {
            player.TransferStatus = PlayerTransferStatus.Negotiating;
        }

        state.Offers.Add(offer);
        AddNotification(state, TransferNotificationType.AiOfferReceived, offer.Message, currentRound, offer.OfferId);
        return offer;
    }

    public void GenerateAiOffersForUserPlayers(TransferMarketState state, League activeLeague, Team selectedTeam, int currentRound)
    {
        EvaluateListedPlayersForOffers(state, activeLeague, selectedTeam, currentRound);
    }

    public IReadOnlyList<TransferOffer> EvaluateListedPlayersForOffers(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        int currentRound)
    {
        var existingPlayerIds = GetPlayersWithActiveOffers(state);
        var createdOffers = new List<TransferOffer>();
        var maxOffers = _random.Next(1, 4);
        var targets = selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .Where(player => !player.RejectTransferOffers)
            .Where(player => player.TransferStatus != PlayerTransferStatus.Transferred)
            .Where(player => !existingPlayerIds.Contains(player.PlayerId))
            .OrderByDescending(player => player.TransferStatus == PlayerTransferStatus.Listed)
            .ThenBy(_ => _random.Next())
            .ToList();

        foreach (var target in targets)
        {
            if (createdOffers.Count >= maxOffers)
            {
                break;
            }

            var marketValue = _valueCalculator.CalculateMarketValue(target, activeLeague.LeagueId, activeLeague.PlayerStats);
            var fee = RoundFee(marketValue * GetAiOfferFeeModifier(target));
            var buyer = SelectAiBuyerForPlayer(state, selectedTeam, target, fee);
            if (buyer is null)
            {
                continue;
            }

            var buyerValue = buyer.Value;
            var chance = CalculateAiOfferChance(target, buyerValue.NeedScore, fee, marketValue);
            if (_random.NextDouble() > chance)
            {
                continue;
            }

            createdOffers.Add(CreateAiOfferForUserPlayer(
                state,
                activeLeague,
                selectedTeam,
                target,
                buyerValue.LeagueId,
                buyerValue.Team,
                fee,
                currentRound));
            existingPlayerIds.Add(target.PlayerId);
        }

        return createdOffers;
    }

    public void RunAiTransferActivity(TransferMarketState state, League activeLeague, Team selectedTeam, int currentRound)
    {
        var windowPhase = _windowService.GetWindowPhase(activeLeague, currentRound);
        if (windowPhase != TransferWindowPhase.Closed)
        {
            ProcessAgreedTransfers(state, activeLeague, currentRound);
        }

        ProcessExpiredContracts(state, GetSeasonEndYear(activeLeague.Season));

        if (state.LastAiActivityRound == currentRound)
        {
            return;
        }

        state.LastAiActivityRound = currentRound;
        GenerateAiOffersForUserPlayers(state, activeLeague, selectedTeam, currentRound);

        if (windowPhase == TransferWindowPhase.Closed)
        {
            return;
        }

        RunAiClubTransferActivity(state, activeLeague, selectedTeam, currentRound, windowPhase);
    }

    private void RunAiClubTransferActivity(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        int currentRound,
        TransferWindowPhase windowPhase)
    {
        var listings = GetAllListings(state, activeLeague.PlayerStats)
            .Where(listing => listing.Team != selectedTeam)
            .Where(listing => listing.Team.Name != "Free Agents")
            .Where(listing => listing.Player.TransferStatus != PlayerTransferStatus.Unavailable)
            .Where(listing => !listing.Player.IsInjured)
            .ToList();
        var buyers = GetAiTransferBuyers(state, selectedTeam)
            .OrderByDescending(buyer => buyer.ActivityWeight + _random.NextDouble())
            .Take(GetBuyerPoolSize(windowPhase))
            .ToList();
        var completedTransfers = 0;
        var maxTransfers = GetAiTransferRoundLimit(windowPhase);
        var movedPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var buyer in buyers)
        {
            if (completedTransfers >= maxTransfers)
            {
                break;
            }

            var candidate = SelectAiTransferCandidate(state, buyer, listings, movedPlayerIds, windowPhase);
            if (candidate is null)
            {
                continue;
            }

            if (CompleteTransfer(state, candidate.Listing, buyer.LeagueId, buyer.Team, candidate.Fee, currentRound, candidate.Reason))
            {
                movedPlayerIds.Add(candidate.Listing.Player.PlayerId);
                completedTransfers++;
                AddMajorTransferNotification(state, candidate, currentRound);
            }
        }
    }

    private List<AiTransferBuyer> GetAiTransferBuyers(TransferMarketState state, Team selectedTeam)
    {
        return GetAllTeams(state)
            .Where(item => item.Team != selectedTeam)
            .Select(item =>
            {
                var finance = _financeService.GetOrCreateFinance(state, item.LeagueId, item.Team);
                var averageRating = GetTeamAverageRating(item.Team);
                var reputation = ClubFinanceService.GetReputationScore(item.Team.Name, averageRating);
                return new AiTransferBuyer(
                    item.LeagueId,
                    item.Team,
                    finance,
                    averageRating,
                    reputation,
                    ClubFinanceService.GetTransferActivityWeight(item.Team.Name, finance.AvailableTransferBudget, averageRating),
                    ClubFinanceService.IsBigClub(item.Team.Name),
                    ClubFinanceService.IsEliteClub(item.Team.Name));
            })
            .Where(buyer => buyer.Finance.AvailableTransferBudget >= 5_000_000m)
            .ToList();
    }

    private AiTransferCandidate? SelectAiTransferCandidate(
        TransferMarketState state,
        AiTransferBuyer buyer,
        IReadOnlyList<TransferPlayerListing> listings,
        ISet<string> movedPlayerIds,
        TransferWindowPhase windowPhase)
    {
        var candidates = listings
            .Where(listing => !movedPlayerIds.Contains(listing.Player.PlayerId))
            .Where(listing => listing.Team != buyer.Team)
            .Where(listing => CanAiClubTargetPlayer(buyer, listing, windowPhase))
            .Select(listing =>
            {
                var needScore = GetSquadNeedScore(buyer.Team, listing.Player);
                var fee = CalculateAiTransferFee(listing, buyer, windowPhase, needScore);
                var score = ScoreAiTransferTarget(buyer, listing, needScore, fee, windowPhase);
                return new AiTransferCandidate(listing, buyer, needScore, score, fee, CreateAiTransferType(buyer, listing, needScore, windowPhase));
            })
            .Where(candidate => candidate.Fee <= buyer.Finance.AvailableTransferBudget)
            .Where(candidate => candidate.TargetScore > 0)
            .Where(candidate => CanSellingClubReleasePlayer(candidate.Listing, candidate.Fee))
            .Where(candidate => WouldPlayerAcceptAiMove(candidate.Listing, buyer, candidate.Fee))
            .OrderByDescending(candidate => candidate.TargetScore + _random.NextDouble() * 6)
            .Take(8)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var selected = candidates[_random.Next(Math.Min(3, candidates.Count))];
        return _random.NextDouble() <= GetAiCompletionChance(selected, windowPhase)
            ? selected
            : null;
    }

    private static bool CanAiClubTargetPlayer(AiTransferBuyer buyer, TransferPlayerListing listing, TransferWindowPhase windowPhase)
    {
        var player = listing.Player;
        if (player.ContractStatus is PlayerContractStatus.FreeAgent or PlayerContractStatus.Expired)
        {
            return true;
        }

        if (player.Role is PlayerRole.KeyPlayer && !buyer.IsEliteClub && player.OverallRating >= 86)
        {
            return false;
        }

        if (windowPhase is TransferWindowPhase.January or TransferWindowPhase.JanuaryDeadline &&
            player.Role is PlayerRole.KeyPlayer &&
            player.TransferStatus != PlayerTransferStatus.Listed)
        {
            return false;
        }

        if (buyer.IsBigClub)
        {
            return player.OverallRating >= 76 ||
                player.PotentialOverall >= 84 ||
                player.TransferStatus == PlayerTransferStatus.Listed ||
                PlayerContractService.GetYearsRemaining(player) <= 1;
        }

        return player.Role is PlayerRole.Rotation or PlayerRole.Backup or PlayerRole.Prospect ||
            player.TransferStatus == PlayerTransferStatus.Listed ||
            player.OverallRating <= buyer.AverageRating + 2;
    }

    private decimal CalculateAiTransferFee(
        TransferPlayerListing listing,
        AiTransferBuyer buyer,
        TransferWindowPhase windowPhase,
        double needScore)
    {
        var player = listing.Player;
        var sellingAverageRating = GetTeamAverageRating(listing.Team);
        var sellingReputation = ClubFinanceService.GetReputationScore(listing.Team.Name, sellingAverageRating);
        var isWonderkid = player.Age is <= 23 && player.PotentialOverall >= 86;
        var isUrgent = needScore >= 18 ||
            windowPhase is TransferWindowPhase.SummerDeadline or TransferWindowPhase.JanuaryDeadline ||
            buyer.Team.Players.Concat(buyer.Team.Substitutes).Any(candidate => candidate.Position == player.Position && candidate.IsInjured);
        var modifier = 0.90m + (decimal)_random.NextDouble() * 0.30m;

        if (buyer.IsEliteClub)
        {
            modifier += 0.18m;
        }
        else if (buyer.IsBigClub)
        {
            modifier += 0.10m;
        }

        if (isUrgent)
        {
            modifier += windowPhase is TransferWindowPhase.January or TransferWindowPhase.JanuaryDeadline ? 0.16m : 0.24m;
        }

        if (isWonderkid)
        {
            modifier += 0.20m + (decimal)_random.NextDouble() * 0.30m;
        }

        if (ClubFinanceService.IsBigClub(listing.Team.Name))
        {
            modifier += 0.18m;
        }

        if (buyer.IsBigClub && ClubFinanceService.IsBigClub(listing.Team.Name) &&
            buyer.LeagueId.Equals(listing.LeagueId, StringComparison.OrdinalIgnoreCase))
        {
            modifier += 0.28m;
        }

        modifier += player.Role switch
        {
            PlayerRole.KeyPlayer => 0.28m,
            PlayerRole.Starter => 0.14m,
            PlayerRole.Backup => -0.06m,
            _ => 0.0m
        };

        if (sellingReputation > buyer.Reputation)
        {
            modifier += 0.08m;
        }

        if (windowPhase == TransferWindowPhase.January && !isUrgent)
        {
            modifier -= 0.06m;
        }

        var minimumSellerFee = listing.AskingPrice * (isUrgent ? 0.98m : 0.90m);
        if (player.Role == PlayerRole.KeyPlayer)
        {
            minimumSellerFee = Math.Max(minimumSellerFee, listing.AskingPrice * 1.08m);
        }

        return RoundFee(Math.Max(listing.MarketValue * Math.Clamp(modifier, 0.90m, 1.90m), minimumSellerFee));
    }

    private static double ScoreAiTransferTarget(
        AiTransferBuyer buyer,
        TransferPlayerListing listing,
        double needScore,
        decimal fee,
        TransferWindowPhase windowPhase)
    {
        var player = listing.Player;
        var positionGroup = buyer.Team.Players.Concat(buyer.Team.Substitutes)
            .Where(candidate => candidate.Position == player.Position)
            .ToList();
        var bestCurrent = positionGroup.Count == 0 ? 0 : positionGroup.Max(candidate => candidate.OverallRating);
        var agingStarterBonus = positionGroup.Any(candidate => candidate.IsStarter && candidate.Age >= 31) && player.Age is <= 27 ? 7 : 0;
        var poorFormBonus = positionGroup.Count(candidate => candidate.FormStatus is PlayerFormStatus.Poor or PlayerFormStatus.VeryPoor) * 2.5;
        var potentialBonus = player.Age is <= 23 ? Math.Max(0, (player.PotentialOverall ?? player.OverallRating) - player.OverallRating) * 0.9 : 0;
        var starUpgradeBonus = buyer.IsBigClub && player.OverallRating >= bestCurrent ? 8 : 0;
        var listedBonus = player.TransferStatus == PlayerTransferStatus.Listed ? 5 : 0;
        var deadlineBonus = windowPhase is TransferWindowPhase.SummerDeadline or TransferWindowPhase.JanuaryDeadline ? 4 : 0;
        var affordabilityPenalty = fee > buyer.Finance.AvailableTransferBudget * 0.70m ? 10 : 0;

        return needScore +
            Math.Max(0, player.OverallRating - buyer.AverageRating) +
            agingStarterBonus +
            poorFormBonus +
            potentialBonus +
            starUpgradeBonus +
            listedBonus +
            deadlineBonus -
            affordabilityPenalty;
    }

    private static bool CanSellingClubReleasePlayer(TransferPlayerListing listing, decimal fee)
    {
        if (listing.Team.Name == "Free Agents")
        {
            return true;
        }

        var player = listing.Player;
        var remainingPositionPlayers = listing.Team.Players.Concat(listing.Team.Substitutes)
            .Where(candidate => candidate.PlayerId != player.PlayerId && candidate.Position == player.Position)
            .ToList();
        var minimumDepth = player.Position switch
        {
            Position.Goalkeeper => 1,
            Position.Forward => 2,
            _ => 3
        };

        if (remainingPositionPlayers.Count < minimumDepth && fee < listing.AskingPrice * 1.55m)
        {
            return false;
        }

        var replacementRating = remainingPositionPlayers.OrderByDescending(candidate => candidate.OverallRating).FirstOrDefault()?.OverallRating ?? 0;
        var isCriticalStarter = listing.Team.Players.Contains(player) &&
            player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter &&
            replacementRating < player.OverallRating - 4;
        if (isCriticalStarter && fee < listing.AskingPrice * (player.Role == PlayerRole.KeyPlayer ? 1.40m : 1.20m))
        {
            return false;
        }

        return player.TransferStatus == PlayerTransferStatus.Listed ||
            player.Role is PlayerRole.Backup or PlayerRole.Prospect or PlayerRole.Rotation ||
            fee >= listing.AskingPrice * 0.98m;
    }

    private static bool WouldPlayerAcceptAiMove(TransferPlayerListing listing, AiTransferBuyer buyer, decimal fee)
    {
        var player = listing.Player;
        var sellerAverageRating = GetTeamAverageRating(listing.Team);
        var sellerReputation = ClubFinanceService.GetReputationScore(listing.Team.Name, sellerAverageRating);
        var proposedWage = PlayerContractService.EstimateWeeklyWage(player, buyer.LeagueId);
        var currentWage = player.WeeklyWage ?? proposedWage * 0.85m;
        var sameLeagueBigMove = buyer.LeagueId.Equals(listing.LeagueId, StringComparison.OrdinalIgnoreCase) &&
            buyer.IsBigClub &&
            ClubFinanceService.IsBigClub(listing.Team.Name);
        var score = buyer.Reputation - sellerReputation;

        if (proposedWage > currentWage * 1.15m)
        {
            score += 8;
        }

        if (buyer.IsBigClub)
        {
            score += 9;
        }

        if (player.TransferStatus == PlayerTransferStatus.Listed || player.Morale < 40)
        {
            score += 12;
        }

        if (player.Role is PlayerRole.Backup or PlayerRole.Prospect)
        {
            score += 8;
        }

        if (player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter)
        {
            score -= buyer.Reputation >= sellerReputation ? 2 : 8;
        }

        if (sameLeagueBigMove)
        {
            score -= 8;
        }

        if (fee >= listing.AskingPrice * 1.35m)
        {
            score += 5;
        }

        return score >= -2;
    }

    private static double GetAiCompletionChance(AiTransferCandidate candidate, TransferWindowPhase windowPhase)
    {
        var chance = candidate.Buyer.IsEliteClub ? 0.78 : candidate.Buyer.IsBigClub ? 0.66 : 0.42;
        chance += Math.Clamp(candidate.NeedScore / 80.0, 0.0, 0.18);
        if (candidate.Listing.Player.TransferStatus == PlayerTransferStatus.Listed)
        {
            chance += 0.10;
        }

        if (windowPhase is TransferWindowPhase.SummerDeadline or TransferWindowPhase.JanuaryDeadline)
        {
            chance += 0.12;
        }
        else if (windowPhase == TransferWindowPhase.January)
        {
            chance -= 0.08;
        }

        if (candidate.Listing.Player.Role == PlayerRole.KeyPlayer)
        {
            chance -= 0.12;
        }

        return Math.Clamp(chance, 0.12, 0.86);
    }

    private static int GetAiTransferRoundLimit(TransferWindowPhase windowPhase)
    {
        return windowPhase switch
        {
            TransferWindowPhase.SummerDeadline => 6,
            TransferWindowPhase.Summer => 4,
            TransferWindowPhase.JanuaryDeadline => 4,
            TransferWindowPhase.January => 2,
            _ => 0
        };
    }

    private static int GetBuyerPoolSize(TransferWindowPhase windowPhase)
    {
        return windowPhase switch
        {
            TransferWindowPhase.SummerDeadline => 24,
            TransferWindowPhase.Summer => 18,
            TransferWindowPhase.JanuaryDeadline => 18,
            TransferWindowPhase.January => 12,
            _ => 0
        };
    }

    private static string CreateAiTransferType(AiTransferBuyer buyer, TransferPlayerListing listing, double needScore, TransferWindowPhase windowPhase)
    {
        if (windowPhase is TransferWindowPhase.SummerDeadline or TransferWindowPhase.JanuaryDeadline)
        {
            return "Deadline Transfer";
        }

        if (buyer.IsBigClub && ClubFinanceService.IsBigClub(listing.Team.Name))
        {
            return "Major Transfer";
        }

        if (listing.Player.Age is <= 23 && listing.Player.PotentialOverall >= 86)
        {
            return "Wonderkid Transfer";
        }

        return needScore >= 18 ? "Replacement Transfer" : "AI Transfer";
    }

    private void AddMajorTransferNotification(TransferMarketState state, AiTransferCandidate candidate, int currentRound)
    {
        if (candidate.Fee < 35_000_000m &&
            !candidate.Buyer.IsBigClub &&
            !ClubFinanceService.IsBigClub(candidate.Listing.Team.Name))
        {
            return;
        }

        var message = candidate.Reason == "Major Transfer"
            ? $"{candidate.Buyer.Team.Name} completed a major move for {candidate.Listing.Player.Name} from {candidate.Listing.Team.Name} for {FormatMoney(candidate.Fee)}."
            : $"{candidate.Buyer.Team.Name} signed {candidate.Listing.Player.Name} from {candidate.Listing.Team.Name} for {FormatMoney(candidate.Fee)}.";
        AddNotification(state, TransferNotificationType.TransferCompleted, message, currentRound);
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

    public void ProcessAgreedTransfers(TransferMarketState state, League activeLeague, int currentRound)
    {
        if (!_windowService.IsWindowOpen(activeLeague, currentRound))
        {
            return;
        }

        foreach (var offer in state.Offers
            .Where(offer => offer.Status == OfferStatus.AgreedForNextWindow)
            .ToList())
        {
            CompleteAcceptedOffer(state, offer, activeLeague, currentRound, OfferStatus.CompletedWhenWindowOpens, "Agreed Transfer");
        }
    }

    public void RemovePlayerFromSale(Player player)
    {
        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            player.TransferStatus = PlayerTransferStatus.None;
        }
    }

    private HashSet<string> GetPlayersWithActiveOffers(TransferMarketState state)
    {
        return state.Offers
            .Where(offer => offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered or OfferStatus.AgreedForNextWindow)
            .Select(offer => offer.PlayerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool HasActiveOfferForPlayer(TransferMarketState state, string playerId)
    {
        return state.Offers.Any(offer =>
            offer.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered or OfferStatus.AgreedForNextWindow);
    }

    private (string LeagueId, Team Team, double NeedScore)? SelectAiBuyerForPlayer(
        TransferMarketState state,
        Team selectedTeam,
        Player player,
        decimal fee)
    {
        var candidates = GetAllTeams(state)
            .Where(item => item.Team != selectedTeam)
            .Select(item => new
            {
                item.LeagueId,
                item.Team,
                Finance = _financeService.GetOrCreateFinance(state, item.LeagueId, item.Team),
                NeedScore = GetSquadNeedScore(item.Team, player)
            })
            .Where(item => item.Finance.AvailableTransferBudget >= fee)
            .Where(item => item.Finance.AvailableWageBudget >= PlayerContractService.EstimateWeeklyWage(player, item.LeagueId))
            .Where(item => item.NeedScore > 0)
            .OrderByDescending(item => item.NeedScore)
            .ThenByDescending(item => item.Finance.AvailableTransferBudget)
            .Take(10)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var topPool = candidates.Take(Math.Min(4, candidates.Count)).ToList();
        var selected = topPool[_random.Next(topPool.Count)];
        return (selected.LeagueId, selected.Team, selected.NeedScore);
    }

    private static double GetSquadNeedScore(Team team, Player player)
    {
        var samePositionPlayers = team.Players.Concat(team.Substitutes)
            .Where(candidate => candidate.Position == player.Position)
            .ToList();
        var averageRating = samePositionPlayers.Count == 0
            ? 62
            : samePositionPlayers.Average(candidate => candidate.OverallRating);
        var bestRating = samePositionPlayers.Count == 0
            ? 0
            : samePositionPlayers.Max(candidate => candidate.OverallRating);
        var unavailableCount = samePositionPlayers.Count(candidate => candidate.IsInjured || candidate.IsSuspended || candidate.IsSentOff);
        var depthNeed = samePositionPlayers.Count < 4 ? 10 : 0;
        var upgradeNeed = Math.Max(0, player.OverallRating - averageRating);
        var firstTeamUpgrade = player.OverallRating >= bestRating - 1 ? 8 : 0;

        return depthNeed + upgradeNeed + firstTeamUpgrade + unavailableCount * 7;
    }

    private static double CalculateAiOfferChance(Player player, double buyerNeedScore, decimal fee, decimal marketValue)
    {
        var chance = player.TransferStatus == PlayerTransferStatus.Listed ? 0.16 : 0.05;
        chance += Math.Clamp(buyerNeedScore / 100.0, 0.0, 0.15);
        chance += player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 0.10,
            PlayerFormStatus.Good => 0.06,
            PlayerFormStatus.Poor => -0.07,
            PlayerFormStatus.VeryPoor => -0.12,
            _ => 0.0
        };
        chance += player.OverallRating switch
        {
            >= 88 => 0.12,
            >= 84 => 0.08,
            >= 80 => 0.05,
            >= 74 => 0.02,
            _ => 0.0
        };
        chance += player.Role switch
        {
            PlayerRole.Backup => 0.04,
            PlayerRole.Rotation => 0.03,
            PlayerRole.KeyPlayer => -0.04,
            _ => 0.0
        };
        chance += Math.Min(0.08, player.Traits.Count * 0.015);

        var age = player.Age ?? 26;
        if (age <= 23)
        {
            chance += 0.06;
        }
        else if (age >= 33)
        {
            chance -= 0.12;
        }
        else if (age >= 30)
        {
            chance -= 0.05;
        }

        if (player.IsInjured)
        {
            chance -= 0.20;
        }

        if (player.IsSuspended || player.IsSentOff)
        {
            chance -= 0.10;
        }

        if (marketValue > 0 && fee > marketValue * 1.15m)
        {
            chance -= 0.10;
        }

        return Math.Clamp(chance, 0.01, 0.55);
    }

    private static decimal GetAiOfferFeeModifier(Player player)
    {
        var modifier = player.TransferStatus == PlayerTransferStatus.Listed ? 0.95m : 1.05m;
        modifier += player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 0.08m,
            PlayerFormStatus.Good => 0.04m,
            PlayerFormStatus.Poor => -0.05m,
            PlayerFormStatus.VeryPoor => -0.10m,
            _ => 0m
        };

        if (player.IsInjured)
        {
            modifier -= 0.10m;
        }

        return Math.Clamp(modifier, 0.82m, 1.18m);
    }

    public void AcceptOffer(TransferMarketState state, string offerId, League activeLeague, int currentRound)
    {
        var offer = state.Offers.First(item => item.OfferId == offerId);
        var listing = GetAllListings(state, activeLeague.PlayerStats).FirstOrDefault(item => item.Player.PlayerId == offer.PlayerId);
        if (listing is not null)
        {
            offer.WeeklyWage ??= Math.Max(listing.Player.WeeklyWage ?? 0, PlayerContractService.EstimateWeeklyWage(listing.Player, offer.ToLeagueId));
            offer.ContractYears = offer.ContractYears <= 0 ? GetNewSigningContractYears(listing.Player) : offer.ContractYears;
            offer.SquadRole = listing.Player.Role;
        }

        if (offer.Status is OfferStatus.AgreedForNextWindow or OfferStatus.Completed or OfferStatus.CompletedWhenWindowOpens)
        {
            return;
        }

        if (!_windowService.IsWindowOpen(activeLeague, currentRound) && !offer.IsUserOffer)
        {
            offer.Status = OfferStatus.AgreedForNextWindow;
            offer.AgreementRound = currentRound;
            offer.Message = $"Transfer agreed: {offer.PlayerName} will join {offer.ToClubName} when the transfer window opens.";
            var player = FindPlayer(state, offer.PlayerId);
            if (player.TransferStatus != PlayerTransferStatus.Listed)
            {
                player.TransferStatus = PlayerTransferStatus.Negotiating;
            }

            AddNotification(state, TransferNotificationType.UserOfferAccepted, offer.Message, currentRound, offer.OfferId);
            return;
        }

        CompleteAcceptedOffer(state, offer, activeLeague, currentRound, OfferStatus.Completed, "Permanent");
    }

    public void RejectOffer(TransferMarketState state, string offerId)
    {
        var offer = state.Offers.First(item => item.OfferId == offerId);
        offer.Status = OfferStatus.Rejected;
        offer.Message = "Offer rejected.";

        var player = FindPlayer(state, offer.PlayerId);
        if (player.TransferStatus == PlayerTransferStatus.Negotiating && !HasActiveOfferForPlayer(state, player.PlayerId))
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
            offer.Status = OfferStatus.Withdrawn;
            offer.Message = $"{offer.ToClubName} withdrew after the counter offer.";
        }
    }

    public ClubFinance GetFinance(TransferMarketState state, string leagueId, Team team)
    {
        return _financeService.GetOrCreateFinance(state, leagueId, team);
    }

    public ContractRenewalResult OfferContractExtension(
        TransferMarketState state,
        string leagueId,
        Team team,
        Player player,
        decimal weeklyWage,
        int years,
        PlayerRole squadRole,
        int currentRound)
    {
        years = Math.Clamp(years, 1, 5);
        weeklyWage = Math.Max(1_000m, weeklyWage);
        var finance = _financeService.GetOrCreateFinance(state, leagueId, team);
        var currentWage = player.WeeklyWage ?? 0;
        var extraWage = Math.Max(0, weeklyWage - currentWage);
        if (finance.AvailableWageBudget < extraWage)
        {
            return new ContractRenewalResult(false, "The board blocked the renewal because the wage budget is too tight.", weeklyWage, player.ContractEndYear ?? PlayerContractService.DefaultSeasonEndYear, player.Role);
        }

        var expectedWage = PlayerContractService.EstimateWeeklyWage(player, leagueId);
        var requiredWage = expectedWage * GetRenewalWageExpectation(player, squadRole, years);
        if (weeklyWage < requiredWage)
        {
            return new ContractRenewalResult(false, $"{player.Name} rejected the offer because the wage package is below expectations.", weeklyWage, player.ContractEndYear ?? PlayerContractService.DefaultSeasonEndYear, player.Role);
        }

        if (GetRoleRank(squadRole) < GetRoleRank(player.Role) && player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter)
        {
            return new ContractRenewalResult(false, $"{player.Name} rejected the offer because the proposed squad role is too low.", weeklyWage, player.ContractEndYear ?? PlayerContractService.DefaultSeasonEndYear, player.Role);
        }

        var seasonEndYear = GetSeasonEndYear(state.ActiveSeason);
        player.WeeklyWage = weeklyWage;
        player.ContractEndYear = seasonEndYear + years;
        player.Role = squadRole;
        player.ContractStatus = PlayerContractStatus.Active;
        finance.WageSpent = ClubFinanceService.CalculateWageSpent(team);
        var message = $"{player.Name} signed a contract extension until {player.ContractEndYear} on {PlayerContractService.FormatWage(weeklyWage)}.";
        AddNotification(state, TransferNotificationType.Info, message, currentRound);
        return new ContractRenewalResult(true, message, weeklyWage, player.ContractEndYear.Value, squadRole);
    }

    public ContractRenewalResult OfferPreContract(
        TransferMarketState state,
        League activeLeague,
        Team selectedTeam,
        string playerId,
        decimal weeklyWage,
        int years,
        int currentRound)
    {
        var totalRounds = activeLeague.Fixtures.Select(fixture => fixture.RoundNumber).DefaultIfEmpty(38).Max();
        var seasonEndYear = GetSeasonEndYear(activeLeague.Season);
        var listing = GetAllListings(state, activeLeague.PlayerStats).First(item => item.Player.PlayerId == playerId);
        if (!PlayerContractService.IsPreContractEligible(listing.Player, seasonEndYear, currentRound, totalRounds))
        {
            return new ContractRenewalResult(false, "This player is not eligible for a pre-contract approach yet.", weeklyWage, listing.Player.ContractEndYear ?? seasonEndYear, listing.Player.Role);
        }

        var result = OfferContractExtension(state, activeLeague.LeagueId, selectedTeam, listing.Player, weeklyWage, years, listing.Player.Role, currentRound);
        if (!result.Accepted)
        {
            return result;
        }

        var offer = new TransferOffer
        {
            PlayerId = listing.Player.PlayerId,
            PlayerName = listing.Player.Name,
            FromLeagueId = listing.LeagueId,
            FromClubName = listing.Team.Name,
            ToLeagueId = activeLeague.LeagueId,
            ToClubName = selectedTeam.Name,
            Fee = 0,
            WeeklyWage = weeklyWage,
            ContractYears = years,
            SquadRole = listing.Player.Role,
            Status = OfferStatus.AgreedForNextWindow,
            CreatedRound = currentRound,
            AgreementRound = currentRound,
            IsUserOffer = true,
            Message = $"Pre-contract agreed: {listing.Player.Name} will join {selectedTeam.Name} at the end of the season."
        };
        state.Offers.Add(offer);
        AddNotification(state, TransferNotificationType.UserOfferAccepted, offer.Message, currentRound, offer.OfferId);
        return result with { Message = offer.Message };
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
        var releaseClause = listing.Player.ReleaseClause;
        if (releaseClause is > 0 && offerFee >= releaseClause.Value)
        {
            askingPrice = releaseClause.Value;
        }

        if (listing.Player.ContractStatus == PlayerContractStatus.FreeAgent || listing.Player.ContractStatus == PlayerContractStatus.Expired)
        {
            askingPrice = 0;
            offerFee = 0;
        }

        if (askingPrice > 0 && offerFee < listing.MarketValue * 0.70m)
        {
            offer.Status = OfferStatus.Rejected;
            offer.Message = CreateSellerStanceMessage(listing, "rejected the offer immediately.");
            AddNotification(state, TransferNotificationType.UserOfferRejected, offer.Message, currentRound, offer.OfferId);
            return;
        }

        var ratio = askingPrice == 0 ? 1 : offerFee / askingPrice;
        var minimumAcceptedRatio = ApplyNegotiationVariance(GetMinimumAcceptedRatio(listing.Player));
        var counterRatio = Math.Min(0.98m, Math.Max(minimumAcceptedRatio + 0.08m, ApplyNegotiationVariance(GetCounterRatio(listing.Player))));

        if (ratio < minimumAcceptedRatio)
        {
            offer.Status = OfferStatus.Rejected;
            offer.Message = CreateSellerStanceMessage(listing, "rejected the offer immediately.");
            AddNotification(state, TransferNotificationType.UserOfferRejected, offer.Message, currentRound, offer.OfferId);
            return;
        }

        if (ratio < counterRatio)
        {
            offer.Status = OfferStatus.Countered;
            offer.CounterFee = releaseClause is > 0 && askingPrice == releaseClause.Value
                ? releaseClause.Value
                : RoundFee(askingPrice * ApplyNegotiationVariance(GetCounterMultiplier(listing.Player)));
            offer.Message = CreateSellerStanceMessage(listing, $"countered at {FormatMoney(offer.CounterFee.Value)}.");
            listing.Player.TransferStatus = PlayerTransferStatus.Negotiating;
            AddNotification(state, TransferNotificationType.UserOfferCountered, offer.Message, currentRound, offer.OfferId);
            return;
        }

        if (!CompleteTransfer(state, listing, activeLeague.LeagueId, selectedTeam, offerFee, currentRound, "Permanent"))
        {
            offer.Status = OfferStatus.Withdrawn;
            offer.Message = $"{selectedTeam.Name} could not complete the transfer for {listing.Player.Name}.";
            return;
        }

        offer.Status = OfferStatus.Completed;
        offer.CompletedRound = currentRound;
        offer.Message = $"Transfer completed: {listing.Player.Name} joined {selectedTeam.Name} for {FormatMoney(offerFee)}.";
        AddNotification(state, TransferNotificationType.TransferCompleted, offer.Message, currentRound, offer.OfferId);
    }

    private void CompleteAcceptedOffer(
        TransferMarketState state,
        TransferOffer offer,
        League activeLeague,
        int currentRound,
        OfferStatus completedStatus,
        string type)
    {
        var listing = GetAllListings(state, activeLeague.PlayerStats).FirstOrDefault(item => item.Player.PlayerId == offer.PlayerId);
        if (listing is null)
        {
            offer.Status = OfferStatus.Withdrawn;
            offer.Message = $"{offer.PlayerName} is no longer available.";
            return;
        }

        var buyer = FindTeam(state, offer.ToLeagueId, offer.ToClubName);
        var fee = offer.CounterFee ?? offer.Fee;
        if (!CompleteTransfer(state, listing, offer.ToLeagueId, buyer, fee, currentRound, type, enforceBuyerBudget: offer.IsUserOffer))
        {
            offer.Status = OfferStatus.Withdrawn;
            offer.Message = $"{offer.ToClubName} could not complete the transfer for {offer.PlayerName}.";
            return;
        }

        offer.Fee = fee;
        offer.Status = completedStatus;
        offer.CompletedRound = currentRound;
        offer.Message = completedStatus == OfferStatus.CompletedWhenWindowOpens
            ? $"Transfer completed: {offer.PlayerName} has joined {offer.ToClubName} for {FormatMoney(fee)}."
            : $"Transfer completed: {offer.PlayerName} joined {offer.ToClubName} for {FormatMoney(fee)}.";
    }

    private bool CompleteTransfer(
        TransferMarketState state,
        TransferPlayerListing listing,
        string toLeagueId,
        Team buyingTeam,
        decimal fee,
        int currentRound,
        string type,
        bool enforceBuyerBudget = true)
    {
        var sellingTeam = listing.Team;
        var player = listing.Player;
        var buyerFinance = _financeService.GetOrCreateFinance(state, toLeagueId, buyingTeam);
        var sellerFinance = _financeService.GetOrCreateFinance(state, listing.LeagueId, sellingTeam);
        var proposedWage = PlayerContractService.EstimateWeeklyWage(player, toLeagueId);

        if (enforceBuyerBudget && buyerFinance.AvailableTransferBudget < fee)
        {
            AddNotification(state, TransferNotificationType.InsufficientBudget, $"{buyingTeam.Name} cannot afford {player.Name}.", currentRound);
            return false;
        }

        if (enforceBuyerBudget && buyerFinance.AvailableWageBudget < proposedWage)
        {
            AddNotification(state, TransferNotificationType.InsufficientBudget, $"{buyingTeam.Name} cannot afford {player.Name}'s wage demands.", currentRound);
            return false;
        }

        if (state.FreeAgents.Remove(player))
        {
            fee = 0;
            type = "Free Agent";
        }
        else
        {
            RemovePlayerFromTeam(sellingTeam, player);
        }

        player.TransferStatus = PlayerTransferStatus.None;
        player.ContractEndYear = PlayerContractService.DefaultSeasonEndYear + GetNewSigningContractYears(player);
        player.WeeklyWage = proposedWage;
        player.ReleaseClause = PlayerContractService.EstimateReleaseClause(player, fee, toLeagueId);
        player.ContractStatus = PlayerContractStatus.Active;
        player.IsStarter = false;
        player.IsOnPitch = false;
        buyingTeam.Substitutes.Add(player);

        buyerFinance.TransferSpent += fee;
        sellerFinance.TransferIncome += fee;
        buyerFinance.WageSpent = ClubFinanceService.CalculateWageSpent(buyingTeam);
        sellerFinance.WageSpent = ClubFinanceService.CalculateWageSpent(sellingTeam);

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
            WeeklyWage = player.WeeklyWage,
            ContractEndYear = player.ContractEndYear,
            SquadRole = player.Role,
            Type = type
        });

        AddNotification(state, TransferNotificationType.TransferCompleted, $"Transfer completed: {player.Name} joined {buyingTeam.Name} for {FormatMoney(fee)}.", currentRound);
        return true;
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
        var clubListings = state.Leagues
            .SelectMany(league => league.Teams.SelectMany(team =>
                team.Players.Concat(team.Substitutes).Select(player =>
                {
                    EnsurePlayerId(player, league.LeagueId, team.Name);
                    PlayerContractService.EnsureContract(player, league.LeagueId, GetSeasonEndYear(league.Season));
                    var marketValue = _valueCalculator.CalculateMarketValue(player, league.LeagueId, stats);
                    return new TransferPlayerListing(
                        player,
                        team,
                        league.LeagueId,
                        league.LeagueName,
                        marketValue,
                        _valueCalculator.CalculateAskingPrice(player, league.LeagueId, stats),
                        GetStatusText(player),
                        player.WeeklyWage ?? PlayerContractService.EstimateWeeklyWage(player, league.LeagueId),
                        player.ContractEndYear,
                        PlayerContractService.GetYearsRemaining(player, GetSeasonEndYear(league.Season)),
                        GetContractStatusText(player));
                })))
            .ToList();

        var freeAgentTeam = new Team { Name = "Free Agents" };
        clubListings.AddRange(state.FreeAgents.Select(player =>
        {
            EnsurePlayerId(player, "free-agents", "Free Agents");
            PlayerContractService.EnsureContract(player, "free-agents", GetSeasonEndYear(state.ActiveSeason));
            var marketValue = _valueCalculator.CalculateMarketValue(player, "free-agents", stats);
            return new TransferPlayerListing(
                player,
                freeAgentTeam,
                "free-agents",
                "Free Agents",
                marketValue,
                0,
                "Free Agent",
                player.WeeklyWage ?? PlayerContractService.EstimateWeeklyWage(player, "free-agents"),
                player.ContractEndYear,
                0,
                "Free Agent");
        }));

        return clubListings;
    }

    private static bool MatchesCriteria(TransferPlayerListing listing, TransferSearchCriteria criteria)
    {
        return MatchesText(listing.Player.Name, criteria.PlayerName) &&
            MatchesText(listing.Team.Name, criteria.ClubName) &&
            MatchesNationality(listing.Player, criteria.Nationality) &&
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

    private static bool MatchesNationality(Player player, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var normalizedFilter = filter.Trim();
        return MatchesText(player.NationalityName, normalizedFilter) ||
            MatchesText(player.Nationality, normalizedFilter) ||
            MatchesText(player.NationalityCode, normalizedFilter) ||
            MatchesText(player.FlagEmoji, normalizedFilter);
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

    private static double GetTeamAverageRating(Team team)
    {
        return team.Players.Concat(team.Substitutes)
            .DefaultIfEmpty()
            .Average(player => player?.OverallRating ?? 72);
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
        if (player.ContractStatus == PlayerContractStatus.FreeAgent)
        {
            return "Free Agent";
        }

        return player.TransferStatus switch
        {
            PlayerTransferStatus.Listed => "Listed",
            PlayerTransferStatus.Negotiating => "Negotiating",
            PlayerTransferStatus.Unavailable => "Unavailable",
            PlayerTransferStatus.Transferred => "Transferred",
            _ => player.IsInjured ? "Injured" : "Available"
        };
    }

    private static string GetContractStatusText(Player player)
    {
        return player.ContractStatus switch
        {
            PlayerContractStatus.PreContractEligible => "Pre-contract eligible",
            PlayerContractStatus.ExpiringSoon => "Expiring soon",
            PlayerContractStatus.Expired => "Expired",
            PlayerContractStatus.FreeAgent => "Free Agent",
            _ => "Active"
        };
    }

    private static decimal GetMinimumAcceptedRatio(Player player)
    {
        var ratio = PlayerContractService.GetYearsRemaining(player) switch
        {
            0 => 0.55m,
            1 => 0.68m,
            >= 4 => 0.90m,
            _ => 0.80m
        };

        if (player.Role == PlayerRole.KeyPlayer && PlayerContractService.GetYearsRemaining(player) >= 3)
        {
            ratio += 0.05m;
        }

        if (player.TransferStatus == PlayerTransferStatus.Listed || player.Morale < 35 || player.Role is PlayerRole.Backup)
        {
            ratio -= 0.08m;
        }

        return Math.Clamp(ratio, 0.50m, 0.98m);
    }

    private static decimal GetCounterRatio(Player player)
    {
        return Math.Clamp(GetMinimumAcceptedRatio(player) + 0.14m, 0.74m, 0.98m);
    }

    private static decimal GetCounterMultiplier(Player player)
    {
        return PlayerContractService.GetYearsRemaining(player) >= 3 && player.Role == PlayerRole.KeyPlayer ? 1.04m : 1.01m;
    }

    private decimal ApplyNegotiationVariance(decimal value)
    {
        var variance = (decimal)(_random.NextDouble() * 0.10 - 0.05);
        return Math.Max(0, value + variance);
    }

    private static string CreateSellerStanceMessage(TransferPlayerListing listing, string actionText)
    {
        var contractContext = PlayerContractService.GetYearsRemaining(listing.Player) switch
        {
            0 => "with the player's contract almost finished",
            1 => "with only one year left on the contract",
            >= 4 when listing.Player.Role == PlayerRole.KeyPlayer => "because the player is tied down on a long deal",
            _ when listing.Player.TransferStatus == PlayerTransferStatus.Listed => "because the player is already listed for sale",
            _ => "after reviewing the player's contract situation"
        };

        return $"{listing.Team.Name} {actionText} {contractContext}.";
    }

    private static int GetNewSigningContractYears(Player player)
    {
        if (player.Age >= 33)
        {
            return 1;
        }

        if (player.Age >= 30)
        {
            return 2;
        }

        return player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter ? 5 : 4;
    }

    private static decimal GetRenewalWageExpectation(Player player, PlayerRole squadRole, int years)
    {
        var modifier = 1.0m;
        if (GetRoleRank(squadRole) > GetRoleRank(player.Role))
        {
            modifier -= 0.06m;
        }

        if (years <= 2 && player.Age < 30)
        {
            modifier += 0.08m;
        }

        if (player.Morale < 35)
        {
            modifier += 0.10m;
        }

        if (player.MatchesPlayedRecently < 3 && player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter)
        {
            modifier += 0.06m;
        }

        return Math.Clamp(modifier, 0.88m, 1.25m);
    }

    private static int GetRoleRank(PlayerRole role)
    {
        return role switch
        {
            PlayerRole.KeyPlayer => 5,
            PlayerRole.Starter => 4,
            PlayerRole.Rotation => 3,
            PlayerRole.Prospect => 2,
            PlayerRole.Backup => 1,
            _ => 3
        };
    }

    private static int GetSeasonEndYear(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return PlayerContractService.DefaultSeasonEndYear;
        }

        var parts = season.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startYear) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var endYear))
        {
            return endYear < 100 ? (startYear / 100 * 100) + endYear : endYear;
        }

        return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear)
            ? parsedYear + 1
            : PlayerContractService.DefaultSeasonEndYear;
    }

    private void ProcessExpiredContracts(TransferMarketState state, int seasonEndYear)
    {
        foreach (var league in state.Leagues)
        {
            foreach (var team in league.Teams)
            {
                foreach (var player in team.Players.Concat(team.Substitutes).ToList())
                {
                    PlayerContractService.EnsureContract(player, league.LeagueId, seasonEndYear);
                    if (player.ContractStatus != PlayerContractStatus.FreeAgent)
                    {
                        continue;
                    }

                    RemovePlayerFromTeam(team, player);
                    if (state.FreeAgents.All(existing => !existing.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase)))
                    {
                        player.TransferStatus = PlayerTransferStatus.None;
                        state.FreeAgents.Add(player);
                    }
                }

                _financeService.GetOrCreateFinance(state, league.LeagueId, team);
            }
        }
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
                PlayerContractService.ApplyContractData(
                    player,
                    leagueId,
                    sourcePlayer.ContractEndYear,
                    sourcePlayer.WeeklyWage,
                    sourcePlayer.ReleaseClause,
                    sourcePlayer.ContractStatus);
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
                PlayerContractService.EnsureContract(player, leagueId);
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

    private sealed record AiTransferBuyer(
        string LeagueId,
        Team Team,
        ClubFinance Finance,
        double AverageRating,
        int Reputation,
        double ActivityWeight,
        bool IsBigClub,
        bool IsEliteClub);

    private sealed record AiTransferCandidate(
        TransferPlayerListing Listing,
        AiTransferBuyer Buyer,
        double NeedScore,
        double TargetScore,
        decimal Fee,
        string Reason);
}
