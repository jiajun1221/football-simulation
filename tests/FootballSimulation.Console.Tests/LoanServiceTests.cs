using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LoanServiceTests
{
    [Fact]
    public void CreateLoan_MovesPlayerToBorrowerReservesAndTracksOwnership()
    {
        var player = CreatePlayer("loan-player", "Loan Player");
        var parent = new Team { Name = "Parent FC", Players = [player] };
        var borrower = new Team { Name = "Borrower FC" };
        var state = CreateState(parent, borrower);

        var agreement = new LoanService().CreateLoan(
            state, "league-a", parent, "league-b", borrower, player, "2026-27", 4, 1_000_000m, 60);

        Assert.DoesNotContain(player, parent.AllPlayers);
        Assert.Contains(player, parent.LoanedOutPlayers);
        Assert.Contains(player, borrower.Reserves);
        Assert.True(player.IsOnLoan);
        Assert.Equal("Parent FC", player.ParentClubName);
        Assert.Equal(60, player.LoanWagePercentage);
        Assert.True(agreement.IsActive);
        Assert.Equal("Loan", state.TransferHistory.Single().Type);
        Assert.Single(
            state.Leagues.SelectMany(league => league.Teams).SelectMany(team => team.AllPlayers),
            candidate => candidate.PlayerId == player.PlayerId);
    }

    [Fact]
    public void ReturnExpiredLoans_ReturnsPlayerToParentReserves()
    {
        var player = CreatePlayer("loan-player", "Loan Player");
        var parent = new Team { Name = "Parent FC", Players = [player] };
        var borrower = new Team { Name = "Borrower FC" };
        var state = CreateState(parent, borrower);
        var service = new LoanService();
        var agreement = service.CreateLoan(state, "league-a", parent, "league-b", borrower, player, "2026-27", 1);

        var returned = service.ReturnExpiredLoans(state, "2026-27", 38);

        Assert.Equal(1, returned);
        Assert.Contains(player, parent.Reserves);
        Assert.DoesNotContain(parent.LoanedOutPlayers, candidate => candidate.PlayerId == player.PlayerId);
        Assert.DoesNotContain(player, borrower.AllPlayers);
        Assert.False(player.IsOnLoan);
        Assert.False(agreement.IsActive);
        Assert.Equal("Loan Return", state.TransferHistory.Last().Type);
    }

    [Fact]
    public void RecallLoan_ReturnsPlayerEarlyAndChargesParentClubPenalty()
    {
        var player = CreatePlayer("loan-player", "Loan Player");
        player.Age = 20;
        player.WeeklyWage = 20_000m;
        var parent = new Team { Name = "Parent FC", Players = [player] };
        var borrower = new Team { Name = "Borrower FC" };
        var state = CreateState(parent, borrower);
        var service = new LoanService();
        service.CreateLoan(state, "league-a", parent, "league-b", borrower, player, "2026-27", 1, 100_000m, 50);
        var finance = new ClubFinanceService().GetOrCreateFinance(state, "league-a", parent);
        var budgetBeforeRecall = finance.AvailableTransferBudget;

        var penalty = service.RecallLoan(state, "league-a", parent, player, currentRound: 8);

        Assert.Equal(250_000m, penalty);
        Assert.Equal(budgetBeforeRecall - penalty, finance.AvailableTransferBudget);
        Assert.Contains(player, parent.Reserves);
        Assert.DoesNotContain(player, borrower.AllPlayers);
        Assert.DoesNotContain(player, parent.LoanedOutPlayers);
        Assert.False(player.IsOnLoan);
        Assert.Contains(state.TransferHistory, item => item.PlayerId == player.PlayerId && item.Type == "Loan Recall");
    }

    [Fact]
    public void CreateLoan_RejectsEliteKeyPlayer()
    {
        var player = CreatePlayer("elite-player", "Elite Player");
        player.Age = 27;
        player.OverallRating = 91;
        player.Role = PlayerRole.KeyPlayer;
        var parent = new Team { Name = "Parent FC", Players = [player] };
        var borrower = new Team { Name = "Borrower FC" };
        var state = CreateState(parent, borrower);

        var exception = Assert.Throws<InvalidOperationException>(() => new LoanService().CreateLoan(
            state, "league-a", parent, "league-b", borrower, player, "2026-27", 1));

        Assert.Contains("not available for loan", exception.Message);
        Assert.Contains(player, parent.AllPlayers);
        Assert.Empty(borrower.AllPlayers);
    }

    [Fact]
    public void CreateInitialState_LoadsAttachedLoanMetadata()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition);
        var league = new GameSessionService().CreateLeague(definition, teams);

        var state = new TransferMarketService().CreateInitialState(league);
        var garnacho = league.Teams.Single(team => team.Name == "Aston Villa").AllPlayers
            .Single(player => player.Name == "Alejandro Garnacho");

        Assert.True(garnacho.IsOnLoan);
        Assert.Equal("Chelsea", garnacho.ParentClubName);
        Assert.Contains(state.LoanAgreements, agreement => agreement.PlayerId == garnacho.PlayerId && agreement.IsActive);

        var chelsea = state.Leagues.Single(item => item.LeagueId == "premier-league").Teams
            .Single(team => team.Name == "Chelsea");
        var loanedOut = chelsea.LoanedOutPlayers.Single(player => player.Name == "Alejandro Garnacho");
        Assert.Equal("Aston Villa", loanedOut.LoanClubName);
        Assert.Equal("Loaned Out", new TransferMarketService().GetClubListings(state, "premier-league", chelsea)
            .Single(listing => listing.Player == loanedOut).StatusText);
    }

    private static TransferMarketState CreateState(Team parent, Team borrower)
    {
        return new TransferMarketState
        {
            ActiveSeason = "2026-27",
            Leagues =
            [
                new TransferLeagueState { LeagueId = "league-a", LeagueName = "League A", Season = "2026-27", Teams = [parent] },
                new TransferLeagueState { LeagueId = "league-b", LeagueName = "League B", Season = "2026-27", Teams = [borrower] }
            ]
        };
    }

    private static Player CreatePlayer(string id, string name)
    {
        return new Player
        {
            PlayerId = id,
            Name = name,
            Position = Position.Midfielder,
            PreferredPosition = "CM",
            OverallRating = 75
        };
    }
}
