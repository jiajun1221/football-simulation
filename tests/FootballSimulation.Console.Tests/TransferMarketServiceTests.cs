using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class TransferMarketServiceTests
{
    [Fact]
    public void TransferWindowService_UsesRoundBasedWindows()
    {
        var league = CreateLeague("premier-league");
        var windowService = new TransferWindowService();

        Assert.True(windowService.IsWindowOpen(league, currentRound: 1));
        Assert.True(windowService.IsWindowOpen(league, currentRound: 4));
        Assert.False(windowService.IsWindowOpen(league, currentRound: 7));
        Assert.True(windowService.IsWindowOpen(league, currentRound: 19));
    }

    [Fact]
    public void PlayerMarketValueCalculator_RewardsPotentialAndDiscountsInjury()
    {
        var calculator = new PlayerMarketValueCalculator();
        var prospect = new Player
        {
            Name = "Elite Prospect",
            OverallRating = 82,
            PotentialOverall = 90,
            Age = 20,
            Position = Position.Forward,
            FormStatus = PlayerFormStatus.Good
        };
        var injuredVeteran = new Player
        {
            Name = "Injured Veteran",
            OverallRating = 82,
            PotentialOverall = 82,
            Age = 34,
            Position = Position.Forward,
            FormStatus = PlayerFormStatus.Poor,
            IsInjured = true,
            InjuryRecoveryMatches = 4
        };

        var prospectValue = calculator.CalculateMarketValue(prospect, "premier-league");
        var veteranValue = calculator.CalculateMarketValue(injuredVeteran, "premier-league");

        Assert.True(prospectValue > veteranValue);
    }

    [Fact]
    public void PlayerMarketValueCalculator_KeepsSuperstarValuesRealistic()
    {
        var calculator = new PlayerMarketValueCalculator();
        var superstar = new Player
        {
            Name = "World Class Forward",
            OverallRating = 92,
            PotentialOverall = 94,
            Age = 26,
            Position = Position.Forward,
            FormStatus = PlayerFormStatus.Good
        };

        var value = calculator.CalculateMarketValue(superstar, "la-liga");

        Assert.InRange(value, 160_000_000m, 250_000_000m);
    }

    [Theory]
    [InlineData("Bradley Barcola", 86, 88, 24, PlayerRole.Rotation, "ligue-1", 65_000_000, 95_000_000, 75_000_000, 120_000_000)]
    [InlineData("Kenan Yildiz", 88, 91, 21, PlayerRole.Prospect, "serie-a", 90_000_000, 140_000_000, 110_000_000, 175_000_000)]
    [InlineData("Rafael Leao", 88, 90, 27, PlayerRole.KeyPlayer, "serie-a", 90_000_000, 130_000_000, 115_000_000, 180_000_000)]
    [InlineData("Kylian Mbappe", 92, 94, 28, PlayerRole.KeyPlayer, "la-liga", 180_000_000, 250_000_000, 220_000_000, 300_000_000)]
    public void PlayerMarketValueCalculator_UsesRealisticStarPriceBands(
        string playerName,
        int overall,
        int potential,
        int age,
        PlayerRole role,
        string leagueId,
        decimal minMarketValue,
        decimal maxMarketValue,
        decimal minAskingPrice,
        decimal maxAskingPrice)
    {
        var calculator = new PlayerMarketValueCalculator();
        var player = new Player
        {
            Name = playerName,
            OverallRating = overall,
            PotentialOverall = potential,
            Age = age,
            Position = Position.Forward,
            Role = role,
            FormStatus = PlayerFormStatus.Average
        };

        var marketValue = calculator.CalculateMarketValue(player, leagueId);
        var askingPrice = calculator.CalculateAskingPrice(player, leagueId);

        Assert.InRange(marketValue, minMarketValue, maxMarketValue);
        Assert.InRange(askingPrice, minAskingPrice, maxAskingPrice);
        Assert.True(askingPrice >= marketValue);
    }

    [Fact]
    public void PlayerMarketValueCalculator_DiscountsListedPlayers()
    {
        var calculator = new PlayerMarketValueCalculator();
        var player = new Player
        {
            Name = "Listed Starter",
            OverallRating = 86,
            PotentialOverall = 88,
            Age = 24,
            Position = Position.Forward,
            Role = PlayerRole.Starter,
            FormStatus = PlayerFormStatus.Average
        };

        var normalAskingPrice = calculator.CalculateAskingPrice(player, "ligue-1");
        player.TransferStatus = PlayerTransferStatus.Listed;
        var listedAskingPrice = calculator.CalculateAskingPrice(player, "ligue-1");

        Assert.True(listedAskingPrice < normalAskingPrice);
    }

    [Fact]
    public void PlayerMarketValueCalculator_DiscountsShortContractsAndRewardsLongContracts()
    {
        var calculator = new PlayerMarketValueCalculator();
        var player = new Player
        {
            Name = "Contract Test Forward",
            OverallRating = 84,
            PotentialOverall = 86,
            Age = 25,
            Position = Position.Forward,
            Role = PlayerRole.Starter,
            FormStatus = PlayerFormStatus.Average,
            ContractEndYear = PlayerContractService.DefaultSeasonEndYear + 1
        };

        var shortDealAskingPrice = calculator.CalculateAskingPrice(player, "premier-league");
        player.ContractEndYear = PlayerContractService.DefaultSeasonEndYear + 5;
        var longDealAskingPrice = calculator.CalculateAskingPrice(player, "premier-league");

        Assert.True(longDealAskingPrice > shortDealAskingPrice);
    }

    [Fact]
    public void OfferContractExtension_AcceptsRealisticWageIncrease()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);
        var player = selectedTeam.Players.Concat(selectedTeam.Substitutes).First();
        var wage = PlayerContractService.EstimateWeeklyWage(player, league.LeagueId) * 1.2m;

        var result = service.OfferContractExtension(state, league.LeagueId, selectedTeam, player, wage, 4, player.Role, currentRound: 3);

        Assert.True(result.Accepted);
        Assert.Equal(PlayerContractService.DefaultSeasonEndYear + 4, player.ContractEndYear);
        Assert.Equal(wage, player.WeeklyWage);
    }

    [Fact]
    public void MakeUserOffer_CompletesTransferAndUpdatesBudgets()
    {
        var league = CreateLeague("la-liga");
        var selectedTeam = league.Teams.Single(team => team.Name == "Real Madrid");
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);
        var target = service.GetAllPlayerListings(state, league.PlayerStats)
            .Where(listing => listing.Team != selectedTeam)
            .Where(listing => listing.AskingPrice < service.GetFinance(state, league.LeagueId, selectedTeam).AvailableTransferBudget)
            .OrderByDescending(listing => listing.Player.OverallRating)
            .First();
        var sellingTeam = target.Team;
        var buyerBudgetBefore = service.GetFinance(state, league.LeagueId, selectedTeam).AvailableTransferBudget;
        var sellerIncomeBefore = service.GetFinance(state, target.LeagueId, sellingTeam).TransferIncome;

        var offer = service.MakeUserOffer(state, league, selectedTeam, target.Player.PlayerId, target.AskingPrice, currentRound: 1);

        Assert.Equal(OfferStatus.Completed, offer.Status);
        Assert.Contains(selectedTeam.Substitutes, player => player.PlayerId == target.Player.PlayerId);
        Assert.DoesNotContain(sellingTeam.Players.Concat(sellingTeam.Substitutes), player => player.PlayerId == target.Player.PlayerId);
        Assert.True(service.GetFinance(state, league.LeagueId, selectedTeam).AvailableTransferBudget < buyerBudgetBefore);
        Assert.True(service.GetFinance(state, target.LeagueId, sellingTeam).TransferIncome > sellerIncomeBefore);
        Assert.Contains(state.TransferHistory, item => item.PlayerId == target.Player.PlayerId);
    }

    [Fact]
    public void MakeUserOffer_BlocksOutsideTransferWindow()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);
        var target = service.GetAllPlayerListings(state, league.PlayerStats)
            .First(listing => listing.Team != selectedTeam);

        var offer = service.MakeUserOffer(state, league, selectedTeam, target.Player.PlayerId, target.AskingPrice, currentRound: 7);

        Assert.Equal(OfferStatus.Rejected, offer.Status);
        Assert.Equal("Transfer window is closed.", offer.Message);
        Assert.DoesNotContain(selectedTeam.Substitutes, player => player.PlayerId == target.Player.PlayerId);
    }

    [Fact]
    public void AcceptAiOfferOutsideWindow_AgreesTransferWithoutMovingPlayer()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);
        var player = selectedTeam.Players.Concat(selectedTeam.Substitutes).OrderBy(player => player.OverallRating).First();

        var offer = service.CreateAiOfferForUserPlayer(state, league, selectedTeam, player, currentRound: 7);
        service.AcceptOffer(state, offer.OfferId, league, currentRound: 7);

        Assert.Equal(OfferStatus.AgreedForNextWindow, offer.Status);
        Assert.Contains(selectedTeam.Players.Concat(selectedTeam.Substitutes), squadPlayer => squadPlayer.PlayerId == player.PlayerId);
        Assert.DoesNotContain(state.TransferHistory, item => item.PlayerId == player.PlayerId);
    }

    [Fact]
    public void ProcessAgreedTransfers_MovesPlayerWhenWindowOpens()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);
        var player = selectedTeam.Players.Concat(selectedTeam.Substitutes).OrderBy(player => player.OverallRating).First();

        var offer = service.CreateAiOfferForUserPlayer(state, league, selectedTeam, player, currentRound: 7);
        service.AcceptOffer(state, offer.OfferId, league, currentRound: 7);
        service.ProcessAgreedTransfers(state, league, currentRound: 19);
        var buyer = state.Leagues
            .Single(item => item.LeagueId == offer.ToLeagueId)
            .Teams
            .Single(team => team.Name == offer.ToClubName);

        Assert.Equal(OfferStatus.CompletedWhenWindowOpens, offer.Status);
        Assert.DoesNotContain(selectedTeam.Players.Concat(selectedTeam.Substitutes), squadPlayer => squadPlayer.PlayerId == player.PlayerId);
        Assert.Contains(buyer.Substitutes, squadPlayer => squadPlayer.PlayerId == player.PlayerId);
        Assert.Contains(state.TransferHistory, item => item.PlayerId == player.PlayerId && item.Type == "Agreed Transfer");
    }

    [Fact]
    public void GetRecommendedPlayers_ReflectsWeakSquadAreas()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        foreach (var defender in selectedTeam.Players.Concat(selectedTeam.Substitutes).Where(player => player.Position == Position.Defender))
        {
            defender.OverallRating = 60;
        }

        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);

        var recommendations = service.GetRecommendedPlayers(state, league, selectedTeam);

        Assert.Contains(recommendations, recommendation =>
            recommendation.Listing.Player.Position == Position.Defender &&
            recommendation.Reason.Contains("depth is weak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchPlayers_PositionFilterMatchesDisplayedPreferredPositionOnly()
    {
        var league = CreateLeague("premier-league");
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);

        var results = service.SearchPlayers(state, new TransferSearchCriteria { Position = "LW" }, league.PlayerStats);

        Assert.NotEmpty(results);
        Assert.All(results, listing => Assert.Equal("LW", listing.Player.PreferredPosition));
    }

    [Fact]
    public void RunAiTransferActivity_OpenWindowCreatesVisibleAiTransfers()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService(seed: 12);
        var state = service.CreateInitialState(league);

        service.RunAiTransferActivity(state, league, selectedTeam, currentRound: 1);

        Assert.NotEmpty(state.TransferHistory);
        Assert.All(state.TransferHistory, item => Assert.NotEqual(selectedTeam.Name, item.FromClubName));
        Assert.Contains(state.Inbox, item => item.Type == TransferNotificationType.TransferCompleted);
    }

    [Fact]
    public void RunAiTransferActivity_ClosedWindowDoesNotCompleteAiTransfers()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService(seed: 12);
        var state = service.CreateInitialState(league);

        service.RunAiTransferActivity(state, league, selectedTeam, currentRound: 7);

        Assert.Empty(state.TransferHistory);
    }

    [Fact]
    public void RunAiTransferActivity_BigClubTransfersCanHappenBetweenBigClubs()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService(seed: 42);
        var state = service.CreateInitialState(league);

        foreach (var round in Enumerable.Range(1, 4))
        {
            service.RunAiTransferActivity(state, league, selectedTeam, round);
        }

        Assert.Contains(state.TransferHistory, item =>
            ClubFinanceService.IsBigClub(item.FromClubName) &&
            ClubFinanceService.IsBigClub(item.ToClubName));
    }

    [Fact]
    public void RunAiTransferActivity_UserClubPlayersAreNotAutoSold()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var service = new TransferMarketService(seed: 99);
        var state = service.CreateInitialState(league);
        var protectedPlayerIds = selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .Select(player => player.PlayerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var round in Enumerable.Range(1, 4))
        {
            service.RunAiTransferActivity(state, league, selectedTeam, round);
        }

        Assert.All(protectedPlayerIds, playerId =>
            Assert.Contains(selectedTeam.Players.Concat(selectedTeam.Substitutes), player => player.PlayerId == playerId));
        Assert.DoesNotContain(state.TransferHistory, item => item.FromClubName == selectedTeam.Name);
    }

    [Fact]
    public void TransferWindowService_IdentifiesDeadlineRounds()
    {
        var league = CreateLeague("premier-league");
        var windowService = new TransferWindowService();

        Assert.Equal(TransferWindowPhase.Summer, windowService.GetWindowPhase(league, currentRound: 1));
        Assert.Equal(TransferWindowPhase.SummerDeadline, windowService.GetWindowPhase(league, currentRound: 4));
        Assert.Equal(TransferWindowPhase.Closed, windowService.GetWindowPhase(league, currentRound: 7));
        Assert.Equal(TransferWindowPhase.JanuaryDeadline, windowService.GetWindowPhase(league, currentRound: 22));
    }

    [Fact]
    public void SaveGame_RestoresTransferMarketState()
    {
        var saveDirectory = Path.Combine(Path.GetTempPath(), "football-transfer-tests", Guid.NewGuid().ToString("N"));
        var saveGameService = new SaveGameService(saveDirectory);
        var league = CreateLeague("la-liga");
        var selectedTeam = league.Teams.Single(team => team.Name == "Real Madrid");
        var transferService = new TransferMarketService();
        var transferState = transferService.CreateInitialState(league);
        var target = transferService.GetAllPlayerListings(transferState, league.PlayerStats)
            .Where(listing => listing.Team != selectedTeam)
            .Where(listing => listing.AskingPrice < transferService.GetFinance(transferState, league.LeagueId, selectedTeam).AvailableTransferBudget)
            .First();
        transferService.MakeUserOffer(transferState, league, selectedTeam, target.Player.PlayerId, target.AskingPrice, currentRound: 1);

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, selectedTeam, transferState));
            var loadedData = saveGameService.LoadGame(1);

            Assert.NotNull(loadedData);
            Assert.NotEmpty(loadedData!.TransferMarketState.TransferHistory);
            Assert.Contains(loadedData.TransferMarketState.Leagues, item => item.LeagueId == "premier-league");
            Assert.Contains(loadedData.TransferMarketState.Leagues.Single(item => item.LeagueId == league.LeagueId).Teams.Single(team => team.Name == selectedTeam.Name).Substitutes,
                player => player.PlayerId == target.Player.PlayerId);
        }
        finally
        {
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BindActiveLeague_FillsMissingPlayerDataFromLeagueData()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var colePalmer = selectedTeam.Players.Concat(selectedTeam.Substitutes).Single(player => player.Name == "Cole Palmer");
        var expectedAge = colePalmer.Age;
        var expectedFlagPath = colePalmer.FlagImagePath;
        colePalmer.Age = null;
        colePalmer.NationalityCode = string.Empty;
        colePalmer.NationalityName = string.Empty;
        colePalmer.Nationality = string.Empty;
        colePalmer.FlagImagePath = string.Empty;
        colePalmer.SquadNumber = 99;
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);

        service.BindActiveLeague(state, league);

        Assert.Equal(expectedAge, colePalmer.Age);
        Assert.Equal("GB", colePalmer.NationalityCode);
        Assert.Equal("England", colePalmer.NationalityName);
        Assert.Equal(expectedFlagPath, colePalmer.FlagImagePath);
    }

    [Fact]
    public void BindActiveLeague_FillsLegacySaveOnlyNationalityFromJsonData()
    {
        var league = CreateLeague("premier-league");
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var legacyPlayer = new Player
        {
            Name = "Tosin Adarabioyo",
            SquadNumber = 4,
            Position = Position.Defender,
            PreferredPosition = "CB",
            AssignedPosition = "CB",
            OverallRating = 78,
            BaseOverallRating = 78,
            Age = 24
        };
        selectedTeam.Substitutes.Add(legacyPlayer);
        var service = new TransferMarketService();
        var state = service.CreateInitialState(league);

        service.BindActiveLeague(state, league);

        Assert.Equal("GB-ENG", legacyPlayer.NationalityCode);
        Assert.Equal("England", legacyPlayer.NationalityName);
        Assert.Equal("/Assets/Flags/england.png", legacyPlayer.FlagImagePath);
    }

    private static League CreateLeague(string leagueId)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition(leagueId);
        return new GameSessionService().CreateLeague(definition, dataService.LoadTeams(definition));
    }
}
