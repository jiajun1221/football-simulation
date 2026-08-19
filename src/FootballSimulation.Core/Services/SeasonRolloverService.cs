using FootballSimulation.Models;
using FootballSimulation.Engine;

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
    private readonly LeagueEngine _leagueEngine = new();
    private readonly CompetitionProgressionService _competitionProgressionService = new();
    private readonly YouthAcademyService _youthAcademyService = new();
    private readonly YouthScoutService _youthScoutService = new();
    private readonly FreeAgentRegenService _freeAgentRegenService = new();

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

        CompleteRemainingAiFixturesIfSelectedTeamFinished(league, selectedTeam);

        if (!_completionService.IsLeagueComplete(league))
        {
            throw new InvalidOperationException("The league season is not complete yet.");
        }

        transferMarketState ??= new TransferMarketState();
        var sortedTable = _leagueTableService.SortTable(league.Table);
        var archive = _awardsService.CreateArchive(league, selectedTeam);
        var championsLeagueQualifiedTeamNames = GetChampionsLeagueQualifiedTeamNames(sortedTable, archive);
        var selectedSummary = ApplyBudgetRolloverForCompletedLeague(league, selectedTeam, transferMarketState, sortedTable);
        archive.BudgetSummary = selectedSummary;

        league.SeasonHistory.Add(archive);

        var nextSeason = AdvanceSeasonLabel(league.Season);
        RenewValuableExpiringPlayers(league, transferMarketState, GetSeasonEndYear(nextSeason));
        var removedClubNames = GetClubsToReplace(archive.FinalTable, selectedTeam.Name);
        var promotedClubs = _promotedClubGeneratorService.GeneratePromotedClubs(
            removedClubNames.Count,
            league.Teams.Select(team => team.Name),
            nextSeason);

        AgePlayersForSeasonRollover(league, transferMarketState);

        league.Teams = league.Teams
            .Where(team => !removedClubNames.Contains(team.Name, StringComparer.OrdinalIgnoreCase))
            .Concat(promotedClubs)
            .ToList();
        selectedTeam = league.Teams.FirstOrDefault(team => team.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase))
            ?? selectedTeam;

        league.Season = nextSeason;
        league.Table = _leagueTableService.CreateTable(league.Teams);
        league.Fixtures = _seasonCalendarService.GenerateSeasonFixtures(league.Teams, nextSeason, championsLeagueQualifiedTeamNames);
        league.PlayerStats = [];
        league.PlayerCompetitionStats = [];
        league.CompetitionStates = _seasonCalendarService.CreateInitialCompetitionStates(league.Teams, championsLeagueQualifiedTeamNames);
        league.IsCompleted = false;
        league.HasShownLeagueTrophyCelebration = false;
        league.ShownTrophyCelebrationKeys = [];

        _youthAcademyService.ApplySeasonRollover(league, selectedTeam);
        _youthScoutService.EnsureScoutNetwork(league);
        UpdateTransferMarket(league, selectedTeam, transferMarketState, promotedClubs);
        ApplyOffseasonPlayerReset(league.Teams, transferMarketState);

        return new SeasonRolloverResult(
            archive,
            league,
            selectedTeam,
            transferMarketState,
            promotedClubs,
            removedClubNames);
    }

    public void CompleteRemainingAiFixturesIfSelectedTeamFinished(League league, Team selectedTeam)
    {
        if (!_completionService.IsSelectedTeamSeasonComplete(league, selectedTeam))
        {
            return;
        }

        var safety = 0;
        while (league.Fixtures.Any(fixture => !fixture.IsPlayed) ||
            _competitionProgressionService.RecoverMissingKnockoutRound(league))
        {
            if (safety++ > 500)
            {
                throw new InvalidOperationException("Unable to complete remaining AI fixtures before season rollover.");
            }

            if (!league.Fixtures.Any(fixture => !fixture.IsPlayed))
            {
                continue;
            }

            var fixture = league.Fixtures
                .Where(fixture => !fixture.IsPlayed)
                .OrderBy(GetFixtureCalendarRound)
                .ThenBy(fixture => fixture.Competition)
                .ThenBy(fixture => fixture.RoundName)
                .ThenBy(fixture => fixture.HomeTeam.Name)
                .First();

            _leagueEngine.SimulateFixture(league, fixture, options: CreateSeasonCloseSimulationOptions());
        }

        league.IsCompleted = _completionService.IsLeagueComplete(league);
    }

    private static MatchSimulationOptions CreateSeasonCloseSimulationOptions()
    {
        return new MatchSimulationOptions
        {
            EnableInjuries = false,
            EnableDynamicFatigue = false,
            PreserveMatchStartStamina = true
        };
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

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static List<string> GetChampionsLeagueQualifiedTeamNames(
        IReadOnlyList<LeagueTableEntry> sortedTable,
        SeasonArchive archive)
    {
        var qualifiedTeamNames = sortedTable
            .Take(4)
            .Select(entry => entry.TeamName)
            .Where(teamName => !string.IsNullOrWhiteSpace(teamName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var championsLeagueWinner = archive.CompetitionResults
            .FirstOrDefault(result => result.Competition == CompetitionType.ChampionsLeague)
            ?.WinnerTeamName;
        if (!string.IsNullOrWhiteSpace(championsLeagueWinner) &&
            !qualifiedTeamNames.Contains(championsLeagueWinner, StringComparer.OrdinalIgnoreCase))
        {
            qualifiedTeamNames.Add(championsLeagueWinner);
        }

        return qualifiedTeamNames;
    }

    private static void ApplyOffseasonPlayerReset(
        IEnumerable<Team> activeLeagueTeams,
        TransferMarketState transferMarketState)
    {
        var allPlayers = activeLeagueTeams
            .Concat(transferMarketState.Leagues.SelectMany(leagueState => leagueState.Teams))
            .SelectMany(team => team.Players.Concat(team.Substitutes))
            .Concat(transferMarketState.FreeAgents)
            .Distinct();

        foreach (var player in allPlayers)
        {
            player.SuspendedMatches = 0;
            player.IsSentOff = false;
            player.RedCardMinute = null;
            player.NewlyInjuredThisMatch = false;
            player.NewlySuspendedThisMatch = false;
            player.MatchesPlayedRecently = 0;
            player.RecentMatchMinutes.Clear();
            player.ConsecutiveFullMatches = 0;
            player.SeasonFatigue = 0;
            player.ConsecutiveStarts = 0;
            player.LiveMatchModifier = 1.0;
            player.Stamina = 100;

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

    private static void RenewValuableExpiringPlayers(
        League league,
        TransferMarketState transferMarketState,
        int nextSeasonEndYear)
    {
        var teams = league.Teams
            .Concat(transferMarketState.Leagues.SelectMany(leagueState => leagueState.Teams))
            .Distinct()
            .ToList();

        foreach (var team in teams)
        {
            var roster = team.Players
                .Concat(team.Substitutes)
                .Distinct()
                .ToList();
            if (roster.Count == 0)
            {
                continue;
            }

            var coreSquad = roster
                .OrderByDescending(player => player.OverallRating)
                .ThenByDescending(player => player.PotentialOverall ?? player.OverallRating)
                .Take(Math.Min(18, roster.Count))
                .ToHashSet();
            var averageOverall = roster.Average(player => player.OverallRating);

            foreach (var player in roster.Where(player =>
                player.ContractEndYear.HasValue &&
                player.ContractEndYear.Value < nextSeasonEndYear))
            {
                var isValuableProspect = player.Age is <= 24 &&
                    (player.PotentialOverall ?? player.OverallRating) >= averageOverall + 3;
                var isImportantPlayer = player.IsStarter ||
                    player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter ||
                    player.OverallRating >= averageOverall - 1;

                if (!coreSquad.Contains(player) && !isImportantPlayer && !isValuableProspect)
                {
                    continue;
                }

                var extensionYears = player.Age switch
                {
                    >= 34 => 1,
                    >= 31 => 2,
                    <= 23 => 4,
                    _ => 3
                };
                var leagueId = FindLeagueIdForTeam(league, transferMarketState, team);
                var expectedWage = PlayerContractService.EstimateWeeklyWage(player, leagueId);

                player.ContractEndYear = nextSeasonEndYear + extensionYears;
                player.WeeklyWage = Math.Max(player.WeeklyWage ?? 0, Math.Round(expectedWage * 1.08m, 0));
                player.ContractStatus = PlayerContractStatus.Active;
            }
        }
    }

    private static string FindLeagueIdForTeam(
        League league,
        TransferMarketState transferMarketState,
        Team team)
    {
        if (league.Teams.Contains(team))
        {
            return league.LeagueId;
        }

        return transferMarketState.Leagues
            .FirstOrDefault(leagueState => leagueState.Teams.Contains(team))
            ?.LeagueId ?? league.LeagueId;
    }

    private static int GetSeasonEndYear(string season)
    {
        var normalizedSeason = season.Trim().Replace('/', '-');
        var parts = normalizedSeason.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var startYear) &&
            int.TryParse(parts[1], out var endYear))
        {
            return endYear < 100 ? (startYear / 100 * 100) + endYear : endYear;
        }

        return int.TryParse(normalizedSeason, out var year)
            ? year + 1
            : PlayerContractService.DefaultSeasonEndYear;
    }

    private static void AgePlayersForSeasonRollover(League league, TransferMarketState transferMarketState)
    {
        var agedPlayerReferences = new HashSet<Player>();
        var agedPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var player in league.Teams.SelectMany(team => team.Players.Concat(team.Substitutes)))
        {
            AgePlayerForSeasonRollover(player, agedPlayerReferences, agedPlayerIds);
        }

        foreach (var player in transferMarketState.Leagues
            .SelectMany(leagueState => leagueState.Teams)
            .SelectMany(team => team.Players.Concat(team.Substitutes))
            .Concat(transferMarketState.FreeAgents))
        {
            AgePlayerForSeasonRollover(player, agedPlayerReferences, agedPlayerIds);
        }
    }

    private static void AgePlayerForSeasonRollover(
        Player player,
        HashSet<Player> agedPlayerReferences,
        HashSet<string> agedPlayerIds)
    {
        if (!agedPlayerReferences.Add(player))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(player.PlayerId) && !agedPlayerIds.Add(player.PlayerId))
        {
            return;
        }

        if (player.Age.HasValue)
        {
            player.Age++;
            VeteranDeclineService.ApplySeasonDecline(player);
        }
    }

    private void UpdateTransferMarket(
        League league,
        Team selectedTeam,
        TransferMarketState transferMarketState,
        IReadOnlyList<Team> promotedClubs)
    {
        transferMarketState.ActiveSeason = league.Season;
        transferMarketState.LastAiActivityRound = 0;
        transferMarketState.Offers.RemoveAll(offer =>
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered);
        transferMarketState.Inbox.RemoveAll(notification =>
            notification.Type == TransferNotificationType.WindowClosed || !notification.IsRead);
        _freeAgentRegenService.ProcessSeasonRollover(transferMarketState, league.Season);

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
        ReplenishAiClubRosters(
            transferMarketState,
            selectedTeam,
            GetSeasonEndYear(league.Season));
    }

    public static void ReplenishAiClubRosters(
        TransferMarketState transferMarketState,
        Team selectedTeam,
        int seasonEndYear,
        int minimumRosterSize = 18)
    {
        ArgumentNullException.ThrowIfNull(transferMarketState);
        ArgumentNullException.ThrowIfNull(selectedTeam);

        foreach (var leagueState in transferMarketState.Leagues)
        {
            foreach (var team in leagueState.Teams.Where(team =>
                !ReferenceEquals(team, selectedTeam) &&
                !team.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var roster = team.Players.Concat(team.Substitutes).ToList();
                var playersNeededToFieldEleven = Math.Max(0, 11 - roster.Count);
                if (playersNeededToFieldEleven == 0 ||
                    transferMarketState.FreeAgents.Count < playersNeededToFieldEleven)
                {
                    continue;
                }

                while (roster.Count < minimumRosterSize)
                {
                    var freeAgent = SelectFreeAgentForRoster(transferMarketState.FreeAgents, roster);
                    if (freeAgent is null)
                    {
                        break;
                    }

                    transferMarketState.FreeAgents.Remove(freeAgent);
                    freeAgent.ClubId = $"{leagueState.LeagueId}:{NormalizeClubKey(team.Name)}";
                    freeAgent.PreviousClubId = string.Empty;
                    freeAgent.TransferStatus = PlayerTransferStatus.RecentlyTransferred;
                    freeAgent.ContractEndYear = seasonEndYear + GetAiContractYears(freeAgent);
                    freeAgent.ContractStatus = PlayerContractStatus.Active;
                    freeAgent.WeeklyWage = Math.Max(
                        freeAgent.WeeklyWage ?? 0,
                        PlayerContractService.EstimateWeeklyWage(freeAgent, leagueState.LeagueId));
                    freeAgent.IsStarter = false;
                    freeAgent.IsOnPitch = false;
                    team.Substitutes.Add(freeAgent);
                    roster.Add(freeAgent);
                }

                _ = LineupValidationService.RepairGoalkeeperSlot(team);
            }
        }
    }

    private static Player? SelectFreeAgentForRoster(
        IEnumerable<Player> freeAgents,
        IReadOnlyCollection<Player> roster)
    {
        var positionNeeds = new Dictionary<Position, int>
        {
            [Position.Goalkeeper] = Math.Max(0, 2 - roster.Count(player => player.Position == Position.Goalkeeper)),
            [Position.Defender] = Math.Max(0, 6 - roster.Count(player => player.Position == Position.Defender)),
            [Position.Midfielder] = Math.Max(0, 6 - roster.Count(player => player.Position == Position.Midfielder)),
            [Position.Forward] = Math.Max(0, 4 - roster.Count(player => player.Position == Position.Forward))
        };
        var neededPosition = positionNeeds
            .Where(item => item.Value > 0)
            .OrderByDescending(item => item.Value)
            .Select(item => (Position?)item.Key)
            .FirstOrDefault();

        return freeAgents
            .Where(player => player.ContractStatus is PlayerContractStatus.FreeAgent or PlayerContractStatus.Expired)
            .Where(player => neededPosition is null || player.Position == neededPosition)
            .OrderByDescending(player => player.OverallRating)
            .ThenByDescending(player => player.PotentialOverall ?? player.OverallRating)
            .FirstOrDefault()
            ?? freeAgents
                .OrderByDescending(player => player.OverallRating)
                .ThenByDescending(player => player.PotentialOverall ?? player.OverallRating)
                .FirstOrDefault();
    }

    private static int GetAiContractYears(Player player)
    {
        return player.Age switch
        {
            >= 34 => 1,
            >= 30 => 2,
            <= 23 => 4,
            _ => 3
        };
    }

    private static string NormalizeClubKey(string clubName)
    {
        return new string(clubName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}

public sealed record SeasonRolloverResult(
    SeasonArchive Archive,
    League League,
    Team SelectedTeam,
    TransferMarketState TransferMarketState,
    IReadOnlyList<Team> PromotedClubs,
    IReadOnlyList<string> ReplacedClubNames);
