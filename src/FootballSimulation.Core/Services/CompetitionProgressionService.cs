using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class CompetitionProgressionService
{
    private readonly LeagueTableService _tableService = new();
    private readonly SeasonCalendarService _calendarService = new();

    private static readonly Dictionary<CompetitionType, Dictionary<string, (string? NextRound, int CalendarRound)>> CupRoundMap = new()
    {
        [CompetitionType.LeagueCup] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Third Round"] = ("Fourth Round", 19),
            ["Fourth Round"] = ("Quarter Final", 35),
            ["Quarter Final"] = ("Semi Final", 51),
            ["Semi Final"] = ("Final", 65),
            ["Final"] = (null, 65)
        },
        [CompetitionType.FACup] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Third Round"] = ("Fourth Round", 31),
            ["Fourth Round"] = ("Fifth Round", 41),
            ["Fifth Round"] = ("Quarter Final", 55),
            ["Quarter Final"] = ("Semi Final", 67),
            ["Semi Final"] = ("Final", 79),
            ["Final"] = (null, 79)
        },
        [CompetitionType.ChampionsLeague] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 59),
            ["Quarter Final"] = ("Semi Final", 67),
            ["Semi Final"] = ("Final", 77),
            ["Final"] = (null, 77)
        },
        [CompetitionType.CopaDelRey] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 43),
            ["Quarter Final"] = ("Semi Final", 59),
            ["Semi Final"] = ("Final", 75),
            ["Final"] = (null, 75)
        },
        [CompetitionType.DfbPokal] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 43),
            ["Quarter Final"] = ("Semi Final", 59),
            ["Semi Final"] = ("Final", 75),
            ["Final"] = (null, 75)
        },
        [CompetitionType.CoppaItalia] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 43),
            ["Quarter Final"] = ("Semi Final", 59),
            ["Semi Final"] = ("Final", 75),
            ["Final"] = (null, 75)
        },
        [CompetitionType.CoupeDeFrance] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 43),
            ["Quarter Final"] = ("Semi Final", 59),
            ["Semi Final"] = ("Final", 75),
            ["Final"] = (null, 75)
        },
        [CompetitionType.EuropaLeague] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 69),
            ["Quarter Final"] = ("Semi Final", 77),
            ["Semi Final"] = ("Final", 83),
            ["Final"] = (null, 83)
        },
        [CompetitionType.ConferenceLeague] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Round of 16"] = ("Quarter Final", 69),
            ["Quarter Final"] = ("Semi Final", 77),
            ["Semi Final"] = ("Final", 83),
            ["Final"] = (null, 83)
        }
    };

    public void ProcessCompletedFixture(League league, Fixture fixture, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(fixture);

        EnsureFixtureMetadata(fixture);
        if (fixture.Result is null)
        {
            return;
        }

        if (fixture.AffectsLeagueTable)
        {
            _tableService.ApplyMatchResult(league.Table, fixture.Result);
            league.Table = _tableService.SortTable(league.Table);
        }

        if (fixture.IsKnockout)
        {
            ResolveKnockoutFixture(league, fixture, seed);
            AdvanceKnockoutCompetitionIfReady(league, fixture.Competition, fixture.RoundName);
        }
        else if (fixture.Competition == CompetitionType.ChampionsLeague)
        {
            UpdateChampionsLeagueLeaguePhaseTable(league, fixture);
            TryCreateChampionsLeagueKnockoutRound(league);
        }
    }

    public bool RecoverMissingKnockoutRound(League league)
    {
        ArgumentNullException.ThrowIfNull(league);

        if (RescheduleOverdueChampionsLeagueTies(league))
        {
            return true;
        }

        foreach (var competition in CupRoundMap.Keys)
        {
            var state = league.CompetitionStates.FirstOrDefault(state => state.Competition == competition);
            if (league.Fixtures.Any(fixture => fixture.Competition == competition && !fixture.IsPlayed))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(state?.WinnerTeamName))
            {
                if (RecoverMissingFinalFromCompletedSingleSemifinal(league, competition, state))
                {
                    return true;
                }

                continue;
            }

            if (RecoverMissingKnockoutRound(league, competition))
            {
                return true;
            }
        }

        return false;
    }

    public void EnsureFixtureMetadata(Fixture fixture)
    {
        if (fixture.CalendarRound <= 0)
        {
            fixture.CalendarRound = fixture.RoundNumber;
        }

        if (fixture.RoundNumber <= 0)
        {
            fixture.RoundNumber = fixture.CalendarRound;
        }

        if (string.IsNullOrWhiteSpace(fixture.RoundName))
        {
            fixture.RoundName = fixture.Competition == CompetitionType.PremierLeague
                ? $"Round {fixture.RoundNumber}"
                : fixture.KnockoutRoundKey;
        }

        fixture.AffectsLeagueTable = fixture.Competition == CompetitionType.PremierLeague;
    }

    private static void ResolveKnockoutFixture(League league, Fixture fixture, int? seed)
    {
        if (fixture.IsTwoLeggedTie)
        {
            ResolveTwoLeggedTie(league, fixture, seed);
            return;
        }

        if (!string.IsNullOrWhiteSpace(fixture.WinningTeamName) || fixture.Result is null)
        {
            return;
        }

        if (fixture.Result.HomeScore > fixture.Result.AwayScore)
        {
            SetWinner(fixture, fixture.HomeTeam, fixture.AwayTeam);
            return;
        }

        if (fixture.Result.AwayScore > fixture.Result.HomeScore)
        {
            SetWinner(fixture, fixture.AwayTeam, fixture.HomeTeam);
            return;
        }

        var random = seed.HasValue
            ? new Random(unchecked(seed.Value * 397 ^ fixture.FixtureId.GetHashCode()))
            : Random.Shared;
        var homeStrength = GetTeamStrength(fixture.HomeTeam);
        var awayStrength = GetTeamStrength(fixture.AwayTeam);
        var homeWinChance = homeStrength / Math.Max(1.0, homeStrength + awayStrength);
        var homeWins = random.NextDouble() < homeWinChance;

        if (random.NextDouble() < 0.35)
        {
            fixture.ExtraTimeHomeScore = fixture.Result.HomeScore + (homeWins ? 1 : 0);
            fixture.ExtraTimeAwayScore = fixture.Result.AwayScore + (homeWins ? 0 : 1);
        }
        else
        {
            fixture.ExtraTimeHomeScore = fixture.Result.HomeScore;
            fixture.ExtraTimeAwayScore = fixture.Result.AwayScore;
            fixture.PenaltyHomeScore = homeWins ? random.Next(4, 6) : random.Next(2, 5);
            fixture.PenaltyAwayScore = homeWins ? random.Next(2, 5) : random.Next(4, 6);
            if (fixture.PenaltyHomeScore == fixture.PenaltyAwayScore)
            {
                if (homeWins)
                {
                    fixture.PenaltyHomeScore++;
                }
                else
                {
                    fixture.PenaltyAwayScore++;
                }
            }
        }

        SetWinner(
            fixture,
            homeWins ? fixture.HomeTeam : fixture.AwayTeam,
            homeWins ? fixture.AwayTeam : fixture.HomeTeam);
    }

    private static void ResolveTwoLeggedTie(League league, Fixture completedFixture, int? seed)
    {
        var tieFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == completedFixture.Competition &&
                fixture.IsTwoLeggedTie &&
                fixture.KnockoutTieId.Equals(completedFixture.KnockoutTieId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(fixture => fixture.LegNumber)
            .ToList();
        if (tieFixtures.Count != 2)
        {
            return;
        }

        var aggregateScores = tieFixtures
            .SelectMany(fixture => new[] { fixture.HomeTeam.Name, fixture.AwayTeam.Name })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(teamName => teamName, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var fixture in tieFixtures.Where(fixture => fixture.IsPlayed && fixture.Result is not null))
        {
            aggregateScores[fixture.HomeTeam.Name] += fixture.Result!.HomeScore;
            aggregateScores[fixture.AwayTeam.Name] += fixture.Result.AwayScore;
        }

        foreach (var fixture in tieFixtures)
        {
            fixture.AggregateHomeScore = aggregateScores[fixture.HomeTeam.Name];
            fixture.AggregateAwayScore = aggregateScores[fixture.AwayTeam.Name];
        }

        if (tieFixtures.Any(fixture => !fixture.IsPlayed || fixture.Result is null))
        {
            return;
        }

        var teams = tieFixtures
            .SelectMany(fixture => new[] { fixture.HomeTeam, fixture.AwayTeam })
            .DistinctBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (teams.Count != 2)
        {
            return;
        }

        Team winner;
        Team loser;
        var firstScore = aggregateScores[teams[0].Name];
        var secondScore = aggregateScores[teams[1].Name];
        if (firstScore != secondScore)
        {
            winner = firstScore > secondScore ? teams[0] : teams[1];
            loser = ReferenceEquals(winner, teams[0]) ? teams[1] : teams[0];
        }
        else
        {
            var decidingLeg = tieFixtures.Single(fixture => fixture.LegNumber == 2);
            var random = seed.HasValue
                ? new Random(unchecked(seed.Value * 397 ^ decidingLeg.FixtureId.GetHashCode()))
                : Random.Shared;
            var homeStrength = GetTeamStrength(decidingLeg.HomeTeam);
            var awayStrength = GetTeamStrength(decidingLeg.AwayTeam);
            var homeWins = random.NextDouble() < homeStrength / Math.Max(1.0, homeStrength + awayStrength);

            decidingLeg.ExtraTimeHomeScore = decidingLeg.Result!.HomeScore;
            decidingLeg.ExtraTimeAwayScore = decidingLeg.Result.AwayScore;
            decidingLeg.PenaltyHomeScore = homeWins ? random.Next(4, 6) : random.Next(2, 5);
            decidingLeg.PenaltyAwayScore = homeWins ? random.Next(2, 5) : random.Next(4, 6);
            if (decidingLeg.PenaltyHomeScore == decidingLeg.PenaltyAwayScore)
            {
                if (homeWins)
                {
                    decidingLeg.PenaltyHomeScore++;
                }
                else
                {
                    decidingLeg.PenaltyAwayScore++;
                }
            }

            winner = homeWins ? decidingLeg.HomeTeam : decidingLeg.AwayTeam;
            loser = homeWins ? decidingLeg.AwayTeam : decidingLeg.HomeTeam;
        }

        foreach (var fixture in tieFixtures)
        {
            SetWinner(fixture, winner, loser);
        }
    }

    private void AdvanceKnockoutCompetitionIfReady(League league, CompetitionType competition, string roundName)
    {
        if (!CupRoundMap.TryGetValue(competition, out var roundMap) ||
            !roundMap.TryGetValue(roundName, out var nextRoundInfo))
        {
            return;
        }

        var roundFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == competition &&
                fixture.RoundName.Equals(roundName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (roundFixtures.Count == 0 ||
            roundFixtures.Any(fixture => !fixture.IsPlayed || string.IsNullOrWhiteSpace(fixture.WinningTeamName)))
        {
            return;
        }

        var state = GetOrCreateState(league, competition);
        foreach (var fixture in roundFixtures)
        {
            if (!state.EliminatedTeamNames.Contains(fixture.LosingTeamName, StringComparer.OrdinalIgnoreCase))
            {
                state.EliminatedTeamNames.Add(fixture.LosingTeamName);
            }
        }

        var winners = roundFixtures
            .Select(fixture => ResolveTeam(league, fixture.WinningTeamName))
            .Where(team => team is not null)
            .Cast<Team>()
            .DistinctBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (nextRoundInfo.NextRound is not null &&
            winners.Count == 1 &&
            IsSemiFinalToFinal(roundName, nextRoundInfo.NextRound) &&
            TryFindUneliminatedRecoveryOpponent(league, competition, winners[0].Name) is { } recoveryOpponent)
        {
            winners.Add(recoveryOpponent);
        }

        state.ProgressRecords.Add(new CompetitionProgressRecord
        {
            Competition = competition,
            RoundName = roundName,
            QualifiedTeamNames = winners.Select(team => team.Name).ToList(),
            EliminatedTeamNames = roundFixtures
                .Select(fixture => fixture.LosingTeamName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        });

        if (nextRoundInfo.NextRound is null || winners.Count <= 1)
        {
            state.WinnerTeamName = winners.FirstOrDefault()?.Name ?? string.Empty;
            state.RunnerUpTeamName = roundFixtures.LastOrDefault()?.LosingTeamName ?? string.Empty;
            state.CurrentRoundName = "Complete";
            state.IsActive = false;
            return;
        }

        if (league.Fixtures.Any(fixture => fixture.Competition == competition &&
            fixture.RoundName.Equals(nextRoundInfo.NextRound, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        state.QualifiedTeamNames = winners.Select(team => team.Name).ToList();
        state.CurrentRoundName = nextRoundInfo.NextRound;
        var calendarRound = GetNextAvailableCupCalendarRound(
            league,
            competition,
            nextRoundInfo.CalendarRound);
        league.Fixtures.AddRange(_calendarService.GenerateNextCupRoundFixtures(
            competition,
            nextRoundInfo.NextRound,
            winners,
            calendarRound,
            league.Season));
        league.Fixtures = league.Fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
    }

    private static bool IsSemiFinalToFinal(string roundName, string nextRoundName)
    {
        return roundName.Equals("Semi Final", StringComparison.OrdinalIgnoreCase) &&
            nextRoundName.Equals("Final", StringComparison.OrdinalIgnoreCase);
    }

    private static Team? TryFindUneliminatedRecoveryOpponent(League league, CompetitionType competition, string winnerTeamName)
    {
        var competitionFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == competition)
            .ToList();
        if (competitionFixtures.Count == 0)
        {
            return null;
        }

        var eliminatedTeamNames = competitionFixtures
            .Select(fixture => fixture.LosingTeamName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return competitionFixtures
            .SelectMany(fixture => new[] { fixture.HomeTeam, fixture.AwayTeam })
            .Concat(league.Teams)
            .Where(team =>
                !team.Name.Equals(winnerTeamName, StringComparison.OrdinalIgnoreCase) &&
                !eliminatedTeamNames.Contains(team.Name))
            .DistinctBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(GetTeamStrength)
            .ThenBy(team => team.Name)
            .FirstOrDefault();
    }

    private bool RecoverMissingKnockoutRound(League league, CompetitionType competition)
    {
        if (!CupRoundMap.TryGetValue(competition, out var roundMap))
        {
            return false;
        }

        foreach (var round in roundMap.Keys.Reverse())
        {
            var fixtures = league.Fixtures
                .Where(fixture => fixture.Competition == competition &&
                    fixture.RoundName.Equals(round, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (fixtures.Count == 0 ||
                fixtures.Any(fixture => !fixture.IsPlayed) ||
                fixtures.Any(fixture => string.IsNullOrWhiteSpace(fixture.WinningTeamName)))
            {
                continue;
            }

            AdvanceKnockoutCompetitionIfReady(league, competition, round);
            var nextRound = roundMap[round].NextRound;
            return nextRound is not null &&
                league.Fixtures.Any(fixture => fixture.Competition == competition &&
                    fixture.RoundName.Equals(nextRound, StringComparison.OrdinalIgnoreCase) &&
                    !fixture.IsPlayed);
        }

        return false;
    }

    private bool RecoverMissingFinalFromCompletedSingleSemifinal(
        League league,
        CompetitionType competition,
        SeasonCompetitionState state)
    {
        if (!CupRoundMap.TryGetValue(competition, out var roundMap) ||
            !roundMap.TryGetValue("Semi Final", out var semiFinalInfo) ||
            !string.Equals(semiFinalInfo.NextRound, "Final", StringComparison.OrdinalIgnoreCase) ||
            league.Fixtures.Any(fixture => fixture.Competition == competition &&
                fixture.RoundName.Equals("Final", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var completedSemifinals = league.Fixtures
            .Where(fixture => fixture.Competition == competition &&
                fixture.RoundName.Equals("Semi Final", StringComparison.OrdinalIgnoreCase) &&
                fixture.IsPlayed &&
                !string.IsNullOrWhiteSpace(fixture.WinningTeamName))
            .ToList();
        if (completedSemifinals.Count != 1)
        {
            return false;
        }

        var winner = ResolveTeam(league, completedSemifinals[0].WinningTeamName);
        if (winner is null ||
            TryFindUneliminatedRecoveryOpponent(league, competition, winner.Name) is not { } recoveryOpponent)
        {
            return false;
        }

        state.WinnerTeamName = string.Empty;
        state.RunnerUpTeamName = string.Empty;
        state.QualifiedTeamNames = [winner.Name, recoveryOpponent.Name];
        state.CurrentRoundName = "Final";
        state.IsActive = true;
        league.Fixtures.AddRange(_calendarService.GenerateNextCupRoundFixtures(
            competition,
            "Final",
            [winner, recoveryOpponent],
            semiFinalInfo.CalendarRound,
            league.Season));
        league.Fixtures = league.Fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
        return true;
    }

    private static void UpdateChampionsLeagueLeaguePhaseTable(League league, Fixture fixture)
    {
        if (fixture.Result is null)
        {
            return;
        }

        var state = GetOrCreateState(league, CompetitionType.ChampionsLeague);
        var homeRow = GetOrCreateStandingRow(state.Standings, fixture.HomeTeam.Name);
        var awayRow = GetOrCreateStandingRow(state.Standings, fixture.AwayTeam.Name);
        ApplyStandingResult(homeRow, fixture.Result.HomeScore, fixture.Result.AwayScore);
        ApplyStandingResult(awayRow, fixture.Result.AwayScore, fixture.Result.HomeScore);
        state.Standings = SortStandings(state.Standings);
    }

    private void TryCreateChampionsLeagueKnockoutRound(League league)
    {
        if (league.Fixtures.Any(fixture => fixture.Competition == CompetitionType.ChampionsLeague &&
            fixture.RoundName.Equals("Round of 16", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var leaguePhaseFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();
        if (leaguePhaseFixtures.Count == 0 || leaguePhaseFixtures.Any(fixture => !fixture.IsPlayed))
        {
            return;
        }

        var state = GetOrCreateState(league, CompetitionType.ChampionsLeague);
        state.Standings = SortStandings(state.Standings);
        var directRoundOf16 = state.Standings.Take(8).ToList();
        var playoffPlaces = state.Standings.Skip(8).Take(16).ToList();
        var eliminated = state.Standings.Skip(24).ToList();
        var qualifiers = directRoundOf16
            .Concat(playoffPlaces.Take(8))
            .Select(row => ResolveTeam(league, row.TeamName))
            .Where(team => team is not null)
            .Cast<Team>()
            .ToList();

        if (qualifiers.Count < 2)
        {
            return;
        }

        state.QualifiedTeamNames = qualifiers.Select(team => team.Name).ToList();
        state.EliminatedTeamNames = eliminated.Select(row => row.TeamName).ToList();
        state.ProgressRecords.Add(new CompetitionProgressRecord
        {
            Competition = CompetitionType.ChampionsLeague,
            RoundName = "League Phase",
            QualifiedTeamNames = directRoundOf16.Select(row => row.TeamName).ToList(),
            EliminatedTeamNames = eliminated.Select(row => row.TeamName).ToList()
        });
        state.ProgressRecords.Add(new CompetitionProgressRecord
        {
            Competition = CompetitionType.ChampionsLeague,
            RoundName = "League Phase Playoff Places",
            QualifiedTeamNames = playoffPlaces.Select(row => row.TeamName).ToList()
        });
        state.CurrentRoundName = "Round of 16";
        var calendarRound = GetNextAvailableCupCalendarRound(
            league,
            CompetitionType.ChampionsLeague,
            51);
        league.Fixtures.AddRange(_calendarService.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Round of 16",
            qualifiers,
            calendarRound,
            league.Season));
        league.Fixtures = league.Fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
    }

    private static int GetNextAvailableCupCalendarRound(
        League league,
        CompetitionType competition,
        int plannedCalendarRound)
    {
        if (competition != CompetitionType.ChampionsLeague)
        {
            return plannedCalendarRound;
        }

        var latestCompletedRound = league.Fixtures
            .Where(fixture => fixture.IsPlayed)
            .Select(fixture => fixture.CalendarRound)
            .DefaultIfEmpty(0)
            .Max();
        var latestCompletedKnockoutRound = league.Fixtures
            .Where(fixture =>
                fixture.Competition == CompetitionType.ChampionsLeague &&
                fixture.IsKnockout &&
                fixture.IsPlayed)
            .Select(fixture => fixture.CalendarRound)
            .DefaultIfEmpty(0)
            .Max();
        var calendarRound = Math.Max(
            plannedCalendarRound,
            Math.Max(latestCompletedRound + 1, latestCompletedKnockoutRound + 6));

        // Premier League fixtures occupy even calendar rounds. Keeping UCL ties on
        // odd rounds leaves an actual domestic match slot between the two legs.
        return calendarRound % 2 == 0 ? calendarRound + 1 : calendarRound;
    }

    private static bool RescheduleOverdueChampionsLeagueTies(League league)
    {
        var latestCompletedRound = league.Fixtures
            .Where(fixture => fixture.IsPlayed)
            .Select(fixture => fixture.CalendarRound)
            .DefaultIfEmpty(0)
            .Max();
        var overdueRounds = league.Fixtures
            .Where(fixture =>
                fixture.Competition == CompetitionType.ChampionsLeague &&
                fixture.IsTwoLeggedTie &&
                !fixture.IsPlayed)
            .GroupBy(fixture => fixture.RoundName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(fixture => fixture.CalendarRound <= latestCompletedRound))
            .OrderBy(group => group.Min(fixture => fixture.CalendarRound))
            .ToList();
        if (overdueRounds.Count == 0)
        {
            return false;
        }

        var nextRound = latestCompletedRound + 1;
        if (nextRound % 2 == 0)
        {
            nextRound++;
        }

        foreach (var round in overdueRounds)
        {
            foreach (var fixture in round)
            {
                fixture.CalendarRound = nextRound + (fixture.LegNumber == 2 ? 2 : 0);
                fixture.RoundNumber = fixture.CalendarRound;
                fixture.ScheduledDate = CreateSeasonDate(league.Season, fixture.CalendarRound);
            }

            nextRound += 8;
        }

        league.Fixtures = league.Fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
        return true;
    }

    private static DateTime? CreateSeasonDate(string season, int calendarRound)
    {
        var startYearText = (season ?? string.Empty)
            .Split('-', '/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return int.TryParse(startYearText, out var startYear)
            ? new DateTime(startYear, 8, 1).AddDays(calendarRound * 4)
            : null;
    }

    private static CompetitionStandingRow GetOrCreateStandingRow(List<CompetitionStandingRow> table, string teamName)
    {
        var row = table.FirstOrDefault(row => row.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            return row;
        }

        row = new CompetitionStandingRow
        {
            TeamName = teamName,
            GroupName = "League Phase"
        };
        table.Add(row);
        return row;
    }

    private static List<CompetitionStandingRow> SortStandings(IEnumerable<CompetitionStandingRow> table)
    {
        return table
            .OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.GoalDifference)
            .ThenByDescending(row => row.GoalsFor)
            .ThenBy(row => row.TeamName)
            .ToList();
    }

    private static void ApplyStandingResult(CompetitionStandingRow row, int goalsFor, int goalsAgainst)
    {
        row.Played++;
        row.GoalsFor += goalsFor;
        row.GoalsAgainst += goalsAgainst;
        if (goalsFor > goalsAgainst)
        {
            row.Wins++;
            row.Points += 3;
        }
        else if (goalsFor < goalsAgainst)
        {
            row.Losses++;
        }
        else
        {
            row.Draws++;
            row.Points++;
        }
    }

    private static SeasonCompetitionState GetOrCreateState(League league, CompetitionType competition)
    {
        var state = league.CompetitionStates.FirstOrDefault(state => state.Competition == competition);
        if (state is not null)
        {
            return state;
        }

        state = new SeasonCompetitionState
        {
            Competition = competition,
            Name = CompetitionNames.GetDisplayName(competition),
            IsActive = true
        };
        league.CompetitionStates.Add(state);
        return state;
    }

    private static void SetWinner(Fixture fixture, Team winner, Team loser)
    {
        fixture.WinningTeamName = winner.Name;
        fixture.LosingTeamName = loser.Name;
    }

    private static Team? ResolveTeam(League league, string teamName)
    {
        return league.Fixtures
            .SelectMany(fixture => new[] { fixture.HomeTeam, fixture.AwayTeam })
            .Concat(league.Teams)
            .FirstOrDefault(team => team.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase));
    }

    private static double GetTeamStrength(Team team)
    {
        return team.Players.Concat(team.Substitutes).DefaultIfEmpty().Average(player => player?.OverallRating ?? 70);
    }
}
