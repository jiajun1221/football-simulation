using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class LoanService
{
    public decimal CalculateRecallPenalty(Player player, LoanAgreement agreement)
    {
        var weeklyWage = player.WeeklyWage ?? PlayerContractService.EstimateWeeklyWage(player, agreement.ParentLeagueId);
        return Math.Max(250_000m, agreement.LoanFee + weeklyWage * 4);
    }

    public decimal RecallLoan(TransferMarketState state, string parentLeagueId, Team parentClub, Player player, int currentRound)
    {
        var agreement = state.LoanAgreements.FirstOrDefault(item => item.IsActive &&
            item.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            item.ParentClubName.Equals(parentClub.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No active loan agreement was found for this player.");
        var borrower = FindClub(state, agreement.BorrowerClubId, agreement.BorrowerClubName);
        if (borrower.Team is null)
        {
            throw new InvalidOperationException("The borrowing club could not be found.");
        }
        var penalty = CalculateRecallPenalty(player, agreement);
        var finance = new ClubFinanceService().GetOrCreateFinance(state, parentLeagueId, parentClub);
        if (finance.AvailableTransferBudget < penalty)
        {
            throw new InvalidOperationException($"The club needs {TransferMarketService.FormatMoney(penalty)} to recall this player.");
        }

        RemovePlayer(borrower.Team, player);
        parentClub.LoanedOutPlayers.RemoveAll(candidate => SamePlayer(candidate.PlayerId, player.PlayerId));
        ClearLoan(player);
        player.IsStarter = false;
        player.IsOnPitch = false;
        parentClub.Reserves.Add(player);
        agreement.IsActive = false;
        finance.TransferSpent += penalty;
        state.TransferHistory.Add(new TransferHistoryItem
        {
            RoundNumber = currentRound, PlayerId = player.PlayerId, PlayerName = player.Name,
            FromLeagueId = agreement.BorrowerLeagueId, FromClubId = agreement.BorrowerClubId, FromClubName = agreement.BorrowerClubName,
            ToLeagueId = parentLeagueId, ToClubId = agreement.ParentClubId, ToClubName = parentClub.Name,
            WindowId = state.ActiveSeason, Fee = penalty, PlayerSnapshot = player, Type = "Loan Recall"
        });
        return penalty;
    }

    public void InitializeFromSquads(TransferMarketState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.LoanAgreements ??= [];

        foreach (var league in state.Leagues)
        {
            foreach (var team in league.Teams)
            {
                foreach (var player in team.AllPlayers.Where(player => player.IsOnLoan))
                {
                    if (state.LoanAgreements.Any(item => item.IsActive && SamePlayer(item.PlayerId, player.PlayerId)))
                    {
                        continue;
                    }

                    var parent = FindClub(state, player.ParentClubId, player.ParentClubName);
                    state.LoanAgreements.Add(new LoanAgreement
                    {
                        PlayerId = player.PlayerId,
                        PlayerName = player.Name,
                        ParentLeagueId = parent.LeagueId,
                        ParentClubId = string.IsNullOrWhiteSpace(player.ParentClubId) ? parent.ClubId : player.ParentClubId,
                        ParentClubName = player.ParentClubName,
                        BorrowerLeagueId = league.LeagueId,
                        BorrowerClubId = ClubId(league.LeagueId, team.Name),
                        BorrowerClubName = team.Name,
                        EndSeason = string.IsNullOrWhiteSpace(player.LoanEndSeason) ? state.ActiveSeason : player.LoanEndSeason,
                        WagePercentage = Math.Clamp(player.LoanWagePercentage, 0, 100)
                    });
                }
            }
        }

        foreach (var league in state.Leagues)
        {
            foreach (var team in league.Teams)
            {
                foreach (var player in team.LoanedOutPlayers.Where(player => player.IsOnLoan))
                {
                    if (state.LoanAgreements.Any(item => item.IsActive && SamePlayer(item.PlayerId, player.PlayerId)))
                    {
                        continue;
                    }

                    var borrower = FindClub(state, string.Empty, player.LoanClubName);
                    state.LoanAgreements.Add(new LoanAgreement
                    {
                        PlayerId = player.PlayerId,
                        PlayerName = player.Name,
                        ParentLeagueId = league.LeagueId,
                        ParentClubId = ClubId(league.LeagueId, team.Name),
                        ParentClubName = team.Name,
                        BorrowerLeagueId = borrower.LeagueId,
                        BorrowerClubId = borrower.ClubId,
                        BorrowerClubName = player.LoanClubName,
                        EndSeason = string.IsNullOrWhiteSpace(player.LoanEndSeason) ? state.ActiveSeason : player.LoanEndSeason,
                        WagePercentage = Math.Clamp(player.LoanWagePercentage, 0, 100)
                    });
                }
            }
        }
    }

    public LoanAgreement CreateLoan(
        TransferMarketState state,
        string parentLeagueId,
        Team parentClub,
        string borrowerLeagueId,
        Team borrowerClub,
        Player player,
        string endSeason,
        int currentRound,
        decimal loanFee = 0,
        int wagePercentage = 100)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parentClub);
        ArgumentNullException.ThrowIfNull(borrowerClub);
        ArgumentNullException.ThrowIfNull(player);

        if (!LoanEligibilityService.CanJoinOnLoan(player, out var eligibilityReason))
        {
            throw new InvalidOperationException(eligibilityReason);
        }

        if (ReferenceEquals(parentClub, borrowerClub) || parentClub.Name.Equals(borrowerClub.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A player cannot be loaned to the same club.");
        }

        if (player.IsOnLoan || state.LoanAgreements.Any(item => item.IsActive && SamePlayer(item.PlayerId, player.PlayerId)))
        {
            throw new InvalidOperationException($"{player.Name} is already on loan.");
        }

        if (!RemovePlayer(parentClub, player))
        {
            throw new InvalidOperationException($"{player.Name} does not belong to {parentClub.Name}.");
        }

        player.IsOnLoan = true;
        player.ParentClubId = ClubId(parentLeagueId, parentClub.Name);
        player.ParentClubName = parentClub.Name;
        player.LoanClubName = borrowerClub.Name;
        player.LoanEndSeason = endSeason;
        player.LoanWagePercentage = Math.Clamp(wagePercentage, 0, 100);
        player.IsStarter = false;
        player.IsOnPitch = false;
        parentClub.LoanedOutPlayers.Add(player);
        borrowerClub.Reserves.Add(player);

        var agreement = new LoanAgreement
        {
            PlayerId = player.PlayerId,
            PlayerName = player.Name,
            ParentLeagueId = parentLeagueId,
            ParentClubId = player.ParentClubId,
            ParentClubName = parentClub.Name,
            BorrowerLeagueId = borrowerLeagueId,
            BorrowerClubId = ClubId(borrowerLeagueId, borrowerClub.Name),
            BorrowerClubName = borrowerClub.Name,
            EndSeason = endSeason,
            LoanFee = Math.Max(0, loanFee),
            WagePercentage = player.LoanWagePercentage
        };
        state.LoanAgreements.Add(agreement);
        state.TransferHistory.Add(new TransferHistoryItem
        {
            RoundNumber = currentRound,
            PlayerId = player.PlayerId,
            PlayerName = player.Name,
            FromLeagueId = parentLeagueId,
            FromClubId = agreement.ParentClubId,
            FromClubName = parentClub.Name,
            ToLeagueId = borrowerLeagueId,
            ToClubId = agreement.BorrowerClubId,
            ToClubName = borrowerClub.Name,
            WindowId = endSeason,
            Fee = agreement.LoanFee,
            PlayerSnapshot = player,
            Type = "Loan"
        });
        return agreement;
    }

    public int ReturnExpiredLoans(TransferMarketState state, string completedSeason, int currentRound = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.LoanAgreements ??= [];
        var returned = 0;
        foreach (var agreement in state.LoanAgreements
                     .Where(item => item.IsActive && IsDue(item.EndSeason, completedSeason))
                     .ToList())
        {
            var borrower = FindClub(state, agreement.BorrowerClubId, agreement.BorrowerClubName);
            var player = borrower.Team?.AllPlayers.FirstOrDefault(candidate => SamePlayer(candidate.PlayerId, agreement.PlayerId));
            if (player is null)
            {
                agreement.IsActive = false;
                continue;
            }

            RemovePlayer(borrower.Team!, player);
            var parent = FindClub(state, agreement.ParentClubId, agreement.ParentClubName);
            if (parent.Team is not null)
            {
                parent.Team.LoanedOutPlayers.RemoveAll(candidate => SamePlayer(candidate.PlayerId, agreement.PlayerId));
                ClearLoan(player);
                parent.Team.Reserves.Add(player);
            }
            else
            {
                ClearLoan(player);
                state.FreeAgents.Add(player);
            }

            agreement.IsActive = false;
            state.TransferHistory.Add(new TransferHistoryItem
            {
                RoundNumber = currentRound,
                PlayerId = player.PlayerId,
                PlayerName = player.Name,
                FromLeagueId = agreement.BorrowerLeagueId,
                FromClubId = agreement.BorrowerClubId,
                FromClubName = agreement.BorrowerClubName,
                ToLeagueId = agreement.ParentLeagueId,
                ToClubId = agreement.ParentClubId,
                ToClubName = agreement.ParentClubName,
                WindowId = completedSeason,
                PlayerSnapshot = player,
                Type = "Loan Return"
            });
            returned++;
        }

        return returned;
    }

    private static void ClearLoan(Player player)
    {
        player.IsOnLoan = false;
        player.ParentClubId = string.Empty;
        player.ParentClubName = string.Empty;
        player.LoanClubName = string.Empty;
        player.LoanEndSeason = string.Empty;
        player.LoanWagePercentage = 0;
        player.IsStarter = false;
        player.IsOnPitch = false;
    }

    private static bool RemovePlayer(Team team, Player player)
    {
        return team.Players.Remove(player) || team.Substitutes.Remove(player) || team.Reserves.Remove(player);
    }

    private static bool IsDue(string endSeason, string completedSeason)
    {
        return SeasonEndYear(endSeason) <= SeasonEndYear(completedSeason);
    }

    private static int SeasonEndYear(string season)
    {
        var parts = season.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var startYear) && int.TryParse(parts[1], out var shortEnd))
        {
            return shortEnd < 100 ? (startYear / 100 * 100) + shortEnd : shortEnd;
        }

        return int.TryParse(season, out var year) ? year : int.MaxValue;
    }

    private static (TransferLeagueState? League, Team? Team, string LeagueId, string ClubId) FindClub(
        TransferMarketState state,
        string clubId,
        string clubName)
    {
        foreach (var league in state.Leagues)
        {
            var team = league.Teams.FirstOrDefault(candidate =>
                (!string.IsNullOrWhiteSpace(clubId) && ClubId(league.LeagueId, candidate.Name).Equals(clubId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(clubName) && candidate.Name.Equals(clubName, StringComparison.OrdinalIgnoreCase)));
            if (team is not null)
            {
                return (league, team, league.LeagueId, ClubId(league.LeagueId, team.Name));
            }
        }

        return (null, null, string.Empty, clubId);
    }

    private static string ClubId(string leagueId, string clubName)
    {
        var slug = new string(clubName.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return $"{leagueId}:{slug.Trim('-')}";
    }

    private static bool SamePlayer(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) && first.Equals(second, StringComparison.OrdinalIgnoreCase);
}
