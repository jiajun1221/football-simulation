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
        Assert.True(windowService.IsWindowOpen(league, currentRound: 10));
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

        Assert.Equal("GB", legacyPlayer.NationalityCode);
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
