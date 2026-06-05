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
            ["Round of 16"] = ("Quarter Final", 69),
            ["Quarter Final"] = ("Semi Final", 77),
            ["Semi Final"] = ("Final", 85),
            ["Final"] = (null, 85)
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
            ResolveKnockoutFixture(fixture, seed);
            AdvanceKnockoutCompetitionIfReady(league, fixture.Competition, fixture.RoundName);
        }
        else if (fixture.Competition == CompetitionType.ChampionsLeague)
        {
            UpdateChampionsLeagueLeaguePhaseTable(league, fixture);
            TryCreateChampionsLeagueKnockoutRound(league);
        }
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

    private static void ResolveKnockoutFixture(Fixture fixture, int? seed)
    {
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
            .ToList();

        state.ProgressRecords.Add(new CompetitionProgressRecord
        {
            Competition = competition,
            RoundName = roundName,
            QualifiedTeamNames = winners.Select(team => team.Name).ToList(),
            EliminatedTeamNames = roundFixtures.Select(fixture => fixture.LosingTeamName).Where(name => !string.IsNullOrWhiteSpace(name)).ToList()
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
        league.Fixtures.AddRange(_calendarService.GenerateNextCupRoundFixtures(
            competition,
            nextRoundInfo.NextRound,
            winners,
            nextRoundInfo.CalendarRound,
            league.Season));
        league.Fixtures = league.Fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
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
        league.Fixtures.AddRange(_calendarService.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Round of 16",
            qualifiers,
            61,
            league.Season));
        league.Fixtures = league.Fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
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
