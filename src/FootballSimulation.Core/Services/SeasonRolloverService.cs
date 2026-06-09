using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SeasonRolloverService
{
    private readonly SeasonCompletionService _completionService;
    private readonly SeasonAwardsService _awardsService;
    private readonly PromotedClubGeneratorService _promotedClubGeneratorService;
    private readonly ClubFinanceService _clubFinanceService;
    private readonly LeagueTableService _leagueTableService;
    private readonly LeagueScheduleService _leagueScheduleService;
    private readonly SeasonCalendarService _seasonCalendarService = new();
    private readonly YouthAcademyService _youthAcademyService = new();
    private readonly YouthScoutService _youthScoutService = new();

    public SeasonRolloverService()
        : this(
            new SeasonCompletionService(),
            new SeasonAwardsService(),
            new PromotedClubGeneratorService(),
            new ClubFinanceService(),
            new LeagueTableService(),
            new LeagueScheduleService())
    {
    }

    public SeasonRolloverService(
        SeasonCompletionService completionService,
        SeasonAwardsService awardsService,
        PromotedClubGeneratorService promotedClubGeneratorService,
        ClubFinanceService clubFinanceService,
        LeagueTableService leagueTableService,
        LeagueScheduleService leagueScheduleService)
    {
        _completionService = completionService;
        _awardsService = awardsService;
        _promotedClubGeneratorService = promotedClubGeneratorService;
        _clubFinanceService = clubFinanceService;
        _leagueTableService = leagueTableService;
        _leagueScheduleService = leagueScheduleService;
    }

    public SeasonRolloverResult StartNextSeason(League league, Team selectedTeam, TransferMarketState? transferMarketState)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(selectedTeam);

        if (!_completionService.IsLeagueComplete(league))
        {
            throw new InvalidOperationException("The league season is not complete yet.");
        }

        transferMarketState ??= new TransferMarketState();
        var sortedTable = _leagueTableService.SortTable(league.Table);
        var archive = _awardsService.CreateArchive(league, selectedTeam);
        var selectedSummary = ApplyBudgetRolloverForCompletedLeague(league, selectedTeam, transferMarketState, sortedTable);
        archive.BudgetSummary = selectedSummary;

        league.SeasonHistory.Add(archive);

        var nextSeason = AdvanceSeasonLabel(league.Season);
        var removedClubNames = GetClubsToReplace(archive.FinalTable, selectedTeam.Name);
        var promotedClubs = _promotedClubGeneratorService.GeneratePromotedClubs(
            removedClubNames.Count,
            league.Teams.Select(team => team.Name),
            nextSeason);

        league.Teams = league.Teams
            .Where(team => !removedClubNames.Contains(team.Name, StringComparer.OrdinalIgnoreCase))
            .Concat(promotedClubs)
            .ToList();
        selectedTeam = league.Teams.FirstOrDefault(team => team.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase))
            ?? selectedTeam;

        league.Season = nextSeason;
        league.Table = _leagueTableService.CreateTable(league.Teams);
        league.Fixtures = _seasonCalendarService.GenerateSeasonFixtures(league.Teams, nextSeason);
        league.PlayerStats = [];
        league.PlayerCompetitionStats = [];
        league.CompetitionStates = _seasonCalendarService.CreateInitialCompetitionStates(league.Teams);
        league.IsCompleted = false;
        league.HasShownLeagueTrophyCelebration = false;
        league.ShownTrophyCelebrationKeys = [];

        ApplyOffseasonPlayerReset(league.Teams);
        _youthAcademyService.ApplySeasonRollover(league);
        _youthScoutService.EnsureScoutNetwork(league);
        UpdateTransferMarket(league, transferMarketState, promotedClubs);

        return new SeasonRolloverResult(
            archive,
            league,
            selectedTeam,
            transferMarketState,
            promotedClubs,
            removedClubNames);
    }

    public static string AdvanceSeasonLabel(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return "2026-27";
        }

        var normalized = season.Trim().Replace('/', '-');
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var firstYear) &&
            int.TryParse(parts[1], out var secondYear))
        {
            var nextFirstYear = firstYear + 1;
            var nextSecondYear = secondYear >= 100 ? secondYear + 1 : (secondYear + 1) % 100;
            return secondYear >= 100
                ? $"{nextFirstYear}-{nextSecondYear}"
                : $"{nextFirstYear}-{nextSecondYear:00}";
        }

        if (int.TryParse(normalized, out var singleYear))
        {
            return $"{singleYear + 1}-{(singleYear + 2) % 100:00}";
        }

        return season;
    }

    private BudgetRolloverSummary ApplyBudgetRolloverForCompletedLeague(
        League league,
        Team selectedTeam,
        TransferMarketState transferMarketState,
        IReadOnlyList<LeagueTableEntry> sortedTable)
    {
        var selectedSummary = new BudgetRolloverSummary
        {
            ClubName = selectedTeam.Name
        };

        foreach (var team in league.Teams)
        {
            var finalPosition = GetFinalPosition(sortedTable, team.Name);
            var summary = _clubFinanceService.ApplySeasonRolloverBudget(
                transferMarketState,
                league.LeagueId,
                team,
                finalPosition,
                sortedTable.Count);

            if (team.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase))
            {
                selectedSummary = summary;
            }
        }

        return selectedSummary;
    }

    private static List<string> GetClubsToReplace(IReadOnlyList<ArchivedLeagueTableRow> finalTable, string selectedClubName)
    {
        var relegated = finalTable
            .OrderByDescending(row => row.Position)
            .Take(3)
            .Select(row => row.TeamName)
            .ToList();
        var clubsToReplace = relegated
            .Where(teamName => !teamName.Equals(selectedClubName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (clubsToReplace.Count == 3)
        {
            return clubsToReplace;
        }

        foreach (var row in finalTable.OrderByDescending(row => row.Position))
        {
            if (clubsToReplace.Count == 3)
            {
                break;
            }

            if (row.TeamName.Equals(selectedClubName, StringComparison.OrdinalIgnoreCase) ||
                clubsToReplace.Contains(row.TeamName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            clubsToReplace.Add(row.TeamName);
        }

        return clubsToReplace;
    }

    private static int GetFinalPosition(IReadOnlyList<LeagueTableEntry> sortedTable, string teamName)
    {
        return sortedTable
            .Select((entry, index) => new { entry.TeamName, Position = index + 1 })
            .FirstOrDefault(item => item.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
            ?.Position ?? 0;
    }

    private static void ApplyOffseasonPlayerReset(IEnumerable<Team> teams)
    {
        foreach (var player in teams.SelectMany(team => team.Players.Concat(team.Substitutes)))
        {
            player.SuspendedMatches = 0;
            player.IsSentOff = false;
            player.RedCardMinute = null;
            player.NewlyInjuredThisMatch = false;
            player.NewlySuspendedThisMatch = false;
            player.MatchesPlayedRecently = 0;
            player.LiveMatchModifier = 1.0;
            player.Stamina = Math.Max(player.Stamina, 92);

            if (player.IsInjured)
            {
                player.InjuryRecoveryMatches = Math.Max(0, player.InjuryRecoveryMatches - 8);
                if (player.InjuryRecoveryMatches == 0)
                {
                    player.IsInjured = false;
                    player.InjuryType = string.Empty;
                    player.InjurySeverity = null;
                    player.IsSeasonEndingInjury = false;
                }
            }
        }
    }

    private void UpdateTransferMarket(
        League league,
        TransferMarketState transferMarketState,
        IReadOnlyList<Team> promotedClubs)
    {
        transferMarketState.ActiveSeason = league.Season;
        transferMarketState.LastAiActivityRound = 0;
        transferMarketState.Offers.RemoveAll(offer =>
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered);
        transferMarketState.Inbox.RemoveAll(notification =>
            notification.Type == TransferNotificationType.WindowClosed || !notification.IsRead);

        foreach (var club in promotedClubs)
        {
            _clubFinanceService.GetOrCreateFinance(transferMarketState, league.LeagueId, club);
        }

        var activeLeague = transferMarketState.Leagues.FirstOrDefault(item =>
            item.LeagueId.Equals(league.LeagueId, StringComparison.OrdinalIgnoreCase));
        if (activeLeague is null)
        {
            activeLeague = new TransferLeagueState
            {
                LeagueId = league.LeagueId,
                LeagueName = league.Name
            };
            transferMarketState.Leagues.Add(activeLeague);
        }

        activeLeague.LeagueName = league.Name;
        activeLeague.Season = league.Season;
        activeLeague.Teams = league.Teams;

        new TransferMarketService().BindActiveLeague(transferMarketState, league);
    }
}

public sealed record SeasonRolloverResult(
    SeasonArchive Archive,
    League League,
    Team SelectedTeam,
    TransferMarketState TransferMarketState,
    IReadOnlyList<Team> PromotedClubs,
    IReadOnlyList<string> ReplacedClubNames);
