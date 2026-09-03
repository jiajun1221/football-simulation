using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class MultiCompetitionSeasonTests
{
    [Fact]
    public void SeasonCalendar_IncludesAllTrackedCompetitions()
    {
        var league = CreateLeague(teamCount: 20);

        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.PremierLeague && fixture.AffectsLeagueTable);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.FACup && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.LeagueCup && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.AffectsLeagueTable);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.CopaDelRey && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.DfbPokal && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.CoppaItalia && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.CoupeDeFrance && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.EuropaLeague && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.ConferenceLeague && fixture.IsKnockout);
        Assert.All(league.Fixtures, fixture => Assert.True(fixture.CalendarRound > 0));
    }

    [Fact]
    public void ChampionsLeague_SquadsDoNotDuplicateArsenalGyokeresAtSporting()
    {
        var league = CreateLeague(teamCount: 20);
        var uclTeams = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague)
            .SelectMany(fixture => new[] { fixture.HomeTeam, fixture.AwayTeam })
            .DistinctBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var owners = uclTeams
            .Where(team => team.AllPlayers
                .Any(player => player.Name.StartsWith("Viktor Gy", StringComparison.OrdinalIgnoreCase)))
            .Select(team => team.Name)
            .ToList();

        Assert.Equal(["Arsenal"], owners);
    }

    [Fact]
    public void NextFixture_UsesCalendarOrderAcrossCompetitions()
    {
        var league = CreateLeague(teamCount: 20);
        var selectedTeam = league.Teams[0];
        var expected = league.Fixtures
            .Where(fixture => IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .First();

        var actual = new GameSessionService().FindNextFixtureForTeam(league, selectedTeam);

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Progression_OnlyPremierLeagueFixturesUpdateLeagueTable()
    {
        var engine = new LeagueEngine();
        var league = CreateLeague(teamCount: 8, engine);
        var faCupFixture = league.Fixtures.First(fixture => fixture.Competition == CompetitionType.FACup);
        var premierLeagueFixture = league.Fixtures.First(fixture => fixture.Competition == CompetitionType.PremierLeague);

        engine.SimulateFixture(league, faCupFixture, seed: 41);

        Assert.All(league.Table, row => Assert.Equal(0, row.Played));

        engine.SimulateFixture(league, premierLeagueFixture, seed: 42);

        Assert.Equal(2, league.Table.Sum(row => row.Played));
    }

    [Fact]
    public void CupProgression_GeneratesNextRoundWhenRoundCompletes()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var thirdRoundFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.LeagueCup && fixture.RoundName == "Third Round")
            .ToList();

        foreach (var fixture in thirdRoundFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 2, awayScore: 1);
        }

        Assert.Contains(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.LeagueCup &&
            fixture.RoundName == "Fourth Round" &&
            !fixture.IsPlayed);
        Assert.All(thirdRoundFixtures, fixture => Assert.False(string.IsNullOrWhiteSpace(fixture.WinningTeamName)));
    }

    [Fact]
    public void FaCupProgression_GeneratesPlayableFinalAfterSemifinals()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 20);
        var roundNames = new[] { "Third Round", "Fourth Round", "Fifth Round", "Quarter Final", "Semi Final" };

        foreach (var roundName in roundNames)
        {
            var fixtures = league.Fixtures
                .Where(fixture => fixture.Competition == CompetitionType.FACup &&
                    fixture.RoundName == roundName)
                .ToList();

            Assert.NotEmpty(fixtures);
            if (roundName == "Semi Final")
            {
                Assert.Equal(2, fixtures.Count);
            }

            foreach (var fixture in fixtures)
            {
                CompleteFixture(progression, league, fixture, homeScore: 2, awayScore: 1);
            }
        }

        var finalFixture = Assert.Single(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.FACup &&
            fixture.RoundName == "Final");
        Assert.False(finalFixture.IsPlayed);
        Assert.Equal(FixtureImportance.Final, finalFixture.Importance);
    }

    [Fact]
    public void Recovery_RestoresMissingFaCupFinalFromCompletedSingleSemifinal()
    {
        var progression = new CompetitionProgressionService();
        var semiWinner = CreateTeam("Chelsea");
        var semiLoser = CreateTeam("Leicester City");
        var droppedOpponent = CreateTeam("Arsenal");
        var eliminatedOpponent = CreateTeam("Everton");
        var league = new League
        {
            Name = GameSessionService.PremierLeagueName,
            Season = "2025-26",
            Teams = [semiWinner, semiLoser, droppedOpponent, eliminatedOpponent],
            Fixtures =
            [
                CreateCompletedKnockoutFixture(CompetitionType.FACup, "Quarter Final", droppedOpponent, eliminatedOpponent, droppedOpponent),
                CreateCompletedKnockoutFixture(CompetitionType.FACup, "Semi Final", semiWinner, semiLoser, semiWinner)
            ],
            CompetitionStates =
            [
                new SeasonCompetitionState
                {
                    Competition = CompetitionType.FACup,
                    Name = CompetitionNames.GetDisplayName(CompetitionType.FACup),
                    WinnerTeamName = semiWinner.Name,
                    CurrentRoundName = "Complete",
                    IsActive = false
                }
            ]
        };

        var recovered = progression.RecoverMissingKnockoutRound(league);

        Assert.True(recovered);
        var finalFixture = Assert.Single(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.FACup &&
            fixture.RoundName == "Final");
        Assert.False(finalFixture.IsPlayed);
        var finalTeamNames = new[] { finalFixture.HomeTeam.Name, finalFixture.AwayTeam.Name };
        Assert.Contains(semiWinner.Name, finalTeamNames);
        Assert.Contains(droppedOpponent.Name, finalTeamNames);
        Assert.Equal(string.Empty, league.CompetitionStates.Single().WinnerTeamName);
        Assert.True(league.CompetitionStates.Single().IsActive);
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseUsesSwissStyleEuropeanOpponents()
    {
        var league = CreateLeague(teamCount: 20);
        var selectedTeam = league.Teams.First(team => team.Name == "Arsenal");
        var uclFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();
        var selectedFixtures = uclFixtures
            .Where(fixture => IsTeamInFixture(fixture, selectedTeam))
            .ToList();
        var opponentNames = selectedFixtures
            .Select(fixture => fixture.HomeTeam.Name == selectedTeam.Name ? fixture.AwayTeam.Name : fixture.HomeTeam.Name)
            .ToList();
        var englishOpponents = opponentNames.Count(name => name is "Arsenal" or "Manchester City" or "Liverpool" or "Manchester United" or "Tottenham Hotspur");

        Assert.Equal(36, league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague).Standings.Count);
        Assert.All(league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague).Standings, row =>
        {
            Assert.Equal(8, uclFixtures.Count(fixture => fixture.HomeTeam.Name == row.TeamName || fixture.AwayTeam.Name == row.TeamName));
        });
        Assert.Equal(8, selectedFixtures.Count);
        Assert.Equal(4, selectedFixtures.Count(fixture => fixture.HomeTeam.Name == selectedTeam.Name));
        Assert.Equal(4, selectedFixtures.Count(fixture => fixture.AwayTeam.Name == selectedTeam.Name));
        Assert.Equal(8, opponentNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(englishOpponents <= 1, $"Expected at most one English UCL opponent but found: {string.Join(", ", opponentNames)}");
        Assert.All(selectedFixtures, fixture =>
        {
            Assert.StartsWith("League Phase MD", fixture.RoundName);
            Assert.False(fixture.AffectsLeagueTable);
        });
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseSchedulesEveryClubOncePerMatchday()
    {
        var league = CreateLeague(teamCount: 20);
        var uclTeamNames = league.CompetitionStates
            .First(state => state.Competition == CompetitionType.ChampionsLeague)
            .Standings
            .Select(row => row.TeamName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fixturesByMatchday = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .GroupBy(fixture => fixture.RoundNumber)
            .ToList();

        Assert.Equal(8, fixturesByMatchday.Count);
        Assert.All(fixturesByMatchday, matchday =>
        {
            var matchdayTeamNames = matchday
                .SelectMany(fixture => new[] { fixture.HomeTeam.Name, fixture.AwayTeam.Name })
                .ToList();

            Assert.Equal(18, matchday.Count());
            Assert.Equal(uclTeamNames.Count, matchdayTeamNames.Count);
            Assert.Equal(uclTeamNames.Count, matchdayTeamNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.True(
                uclTeamNames.SetEquals(matchdayTeamNames),
                $"Matchday {matchday.Key} does not include every UCL club exactly once.");
        });
    }

    [Fact]
    public void ChampionsLeague_CompletedUserMatchdayLevelsPlayedCountForEveryClub()
    {
        var engine = new LeagueEngine();
        var league = CreateLeague(teamCount: 20, engine);
        var selectedFixture = league.Fixtures.First(fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            !fixture.IsKnockout &&
            fixture.RoundNumber == 1);

        engine.SimulateFixture(league, selectedFixture, seed: 27);
        engine.SimulateRemainingFixturesForCompetitionRound(league, selectedFixture, seed: 28);

        var uclState = league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague);
        Assert.All(uclState.Standings, row => Assert.Equal(1, row.Played));
        Assert.DoesNotContain(league.Fixtures.Where(fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            !fixture.IsKnockout &&
            fixture.RoundNumber == selectedFixture.RoundNumber), fixture => !fixture.IsPlayed);
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseUsesRealSquadNamesForAllEntrants()
    {
        var league = CreateLeague(teamCount: 20);
        var uclTeams = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague)
            .SelectMany(fixture => new[] { fixture.HomeTeam, fixture.AwayTeam })
            .GroupBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        Assert.Equal(36, uclTeams.Count);
        Assert.All(uclTeams, team =>
        {
            Assert.Equal(11, team.Players.Count);
            Assert.InRange(team.Substitutes.Count, 7, 12);
            Assert.True(team.AllPlayers.Count() >= 18);
            Assert.DoesNotContain(team.AllPlayers, player =>
                player.Name.Contains(" Player ", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(team.AllPlayers, player =>
                player.PlayerId.StartsWith("placeholder-", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void ChampionsLeague_InitialSeasonUsesExact2026_27Entrants()
    {
        var league = CreateLeague(teamCount: 20);
        var expected = new[]
        {
            "Paris Saint-Germain", "Real Madrid", "Manchester City", "Bayern Munich", "Liverpool", "Inter Milan", "Arsenal", "Atletico Madrid", "Barcelona",
            "Borussia Dortmund", "Roma", "Sporting CP", "Aston Villa", "Porto", "Manchester United", "Club Brugge", "Real Betis", "PSV",
            "Feyenoord", "Lille", "Bodo/Glimt", "Napoli", "RB Leipzig", "Villarreal", "Shakhtar Donetsk", "Galatasaray", "Fenerbahce",
            "Slavia Prague", "Stuttgart", "LASK", "Como", "Lens", "Sabah", "AEK Athens", "Viking", "Slovan Bratislava"
        };
        var actual = league.CompetitionStates
            .Single(state => state.Competition == CompetitionType.ChampionsLeague)
            .Standings.Select(row => row.TeamName)
            .Order()
            .ToList();

        Assert.Equal(expected.Order(), actual);
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseCompletionCreatesRoundOf16()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var leaguePhaseFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();

        foreach (var fixture in leaguePhaseFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 1, awayScore: 0);
        }

        Assert.Contains(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            fixture.RoundName == "Round of 16" &&
            fixture.IsKnockout);
        var state = league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague);
        Assert.Equal(36, state.Standings.Count);
        Assert.Equal("Round of 16", state.CurrentRoundName);
        Assert.NotEmpty(state.ProgressRecords);
        Assert.Equal(
            [51, 53],
            league.Fixtures
                .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && fixture.RoundName == "Round of 16")
                .Select(fixture => fixture.CalendarRound)
                .Distinct()
                .Order()
                .ToArray());
    }

    [Fact]
    public void ChampionsLeague_QuarterFinalIsKnockoutImportanceNotFinal()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var leaguePhaseFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();

        foreach (var fixture in leaguePhaseFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 1, awayScore: 0);
        }

        var roundOf16Fixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && fixture.RoundName == "Round of 16")
            .ToList();
        foreach (var fixture in roundOf16Fixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 2, awayScore: 1);
        }

        var quarterFinalFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && fixture.RoundName == "Quarter Final")
            .ToList();

        Assert.NotEmpty(quarterFinalFixtures);
        Assert.All(quarterFinalFixtures, fixture => Assert.Equal(FixtureImportance.Knockout, fixture.Importance));
    }

    [Fact]
    public void ChampionsLeague_KnockoutRoundsUseTwoLegAggregateTies()
    {
        var calendar = new SeasonCalendarService();
        var progression = new CompetitionProgressionService();
        var teams = Enumerable.Range(1, 4).Select(index => CreateTeam($"UCL Team {index}")).ToList();
        var league = new League
        {
            Name = "Test League",
            Season = "2025-26",
            Teams = teams,
            CompetitionStates =
            [
                new SeasonCompetitionState
                {
                    Competition = CompetitionType.ChampionsLeague,
                    CurrentRoundName = "Round of 16",
                    QualifiedTeamNames = teams.Select(team => team.Name).ToList()
                }
            ]
        };
        league.Fixtures = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Round of 16",
            teams,
            61,
            league.Season);

        Assert.Equal(4, league.Fixtures.Count);
        Assert.Equal(2, league.Fixtures.Select(fixture => fixture.KnockoutTieId).Distinct().Count());
        Assert.Equal([61, 63], league.Fixtures.Select(fixture => fixture.CalendarRound).Distinct().Order().ToArray());
        Assert.All(league.Fixtures, fixture => Assert.True(fixture.IsTwoLeggedTie));

        foreach (var tie in league.Fixtures.GroupBy(fixture => fixture.KnockoutTieId).ToList())
        {
            var firstLeg = tie.Single(fixture => fixture.LegNumber == 1);
            var secondLeg = tie.Single(fixture => fixture.LegNumber == 2);
            Assert.Same(firstLeg.HomeTeam, secondLeg.AwayTeam);
            Assert.Same(firstLeg.AwayTeam, secondLeg.HomeTeam);

            CompleteFixture(progression, league, firstLeg, homeScore: 2, awayScore: 0);
            Assert.Equal(string.Empty, firstLeg.WinningTeamName);
            CompleteFixture(progression, league, secondLeg, homeScore: 1, awayScore: 1);

            Assert.Equal(firstLeg.HomeTeam.Name, firstLeg.WinningTeamName);
            Assert.Equal(firstLeg.HomeTeam.Name, secondLeg.WinningTeamName);
            Assert.Equal(3, firstLeg.AggregateHomeScore);
            Assert.Equal(1, firstLeg.AggregateAwayScore);
        }

        var quarterFinals = league.Fixtures
            .Where(fixture => fixture.RoundName == "Quarter Final")
            .ToList();
        Assert.Equal(2, quarterFinals.Count);
        Assert.Equal([69, 71], quarterFinals.Select(fixture => fixture.CalendarRound).Distinct().Order().ToArray());
    }

    [Fact]
    public void ChampionsLeague_FirstLegDefeatStillCarriesScoreIntoSecondLegAggregate()
    {
        var calendar = new SeasonCalendarService();
        var progression = new CompetitionProgressionService();
        var teams = Enumerable.Range(1, 2).Select(index => CreateTeam($"Aggregate Team {index}")).ToList();
        var league = new League
        {
            Name = "Test League",
            Season = "2025-26",
            Teams = teams,
            CompetitionStates =
            [
                new SeasonCompetitionState
                {
                    Competition = CompetitionType.ChampionsLeague,
                    CurrentRoundName = "Round of 16",
                    QualifiedTeamNames = teams.Select(team => team.Name).ToList()
                }
            ]
        };
        league.Fixtures = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Round of 16",
            teams,
            51,
            league.Season);
        var firstLeg = league.Fixtures.Single(fixture => fixture.LegNumber == 1);
        var secondLeg = league.Fixtures.Single(fixture => fixture.LegNumber == 2);

        CompleteFixture(progression, league, firstLeg, homeScore: 0, awayScore: 2);

        var liveAggregate = KnockoutAggregateService.GetLiveAggregateScore(
            league,
            secondLeg,
            currentHomeScore: 1,
            currentAwayScore: 0);
        Assert.Equal((3, 0), liveAggregate);

        CompleteFixture(progression, league, secondLeg, homeScore: 1, awayScore: 0);

        Assert.Equal(3, secondLeg.AggregateHomeScore);
        Assert.Equal(0, secondLeg.AggregateAwayScore);
        Assert.Equal(firstLeg.AwayTeam.Name, secondLeg.WinningTeamName);
    }

    [Fact]
    public void ChampionsLeague_RecordedPenaltyWinnerAdvancesFromLevelAggregateTie()
    {
        var calendar = new SeasonCalendarService();
        var progression = new CompetitionProgressionService();
        var chelsea = CreateTeam("Chelsea");
        var leverkusen = CreateTeam("Bayer Leverkusen");
        var teams = new List<Team>
        {
            chelsea,
            leverkusen,
            CreateTeam("Inter Milan"),
            CreateTeam("Barcelona")
        };
        var league = new League
        {
            Name = "Test League",
            Season = "2025-26",
            Teams = teams,
            CompetitionStates =
            [
                new SeasonCompetitionState
                {
                    Competition = CompetitionType.ChampionsLeague,
                    CurrentRoundName = "Quarter Final",
                    QualifiedTeamNames = teams.Select(team => team.Name).ToList()
                }
            ]
        };
        league.Fixtures = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Quarter Final",
            teams,
            59,
            league.Season);
        var firstLeg = league.Fixtures.Single(fixture =>
            fixture.LegNumber == 1 &&
            (fixture.HomeTeam.Name == chelsea.Name || fixture.AwayTeam.Name == chelsea.Name));
        var secondLeg = league.Fixtures.Single(fixture =>
            fixture.LegNumber == 2 && fixture.KnockoutTieId == firstLeg.KnockoutTieId);
        var otherFirstLeg = league.Fixtures.Single(fixture =>
            fixture.LegNumber == 1 && fixture.KnockoutTieId != firstLeg.KnockoutTieId);
        var otherSecondLeg = league.Fixtures.Single(fixture =>
            fixture.LegNumber == 2 && fixture.KnockoutTieId == otherFirstLeg.KnockoutTieId);

        CompleteFixture(progression, league, firstLeg, homeScore: 2, awayScore: 3);
        CompleteFixture(progression, league, otherFirstLeg, homeScore: 2, awayScore: 0);
        secondLeg.PenaltyHomeScore = 4;
        secondLeg.PenaltyAwayScore = 3;
        secondLeg.WinningTeamName = chelsea.Name;
        secondLeg.LosingTeamName = leverkusen.Name;
        CompleteFixture(progression, league, secondLeg, homeScore: 2, awayScore: 3);
        CompleteFixture(progression, league, otherSecondLeg, homeScore: 1, awayScore: 0);

        Assert.Equal(leverkusen.Name, secondLeg.WinningTeamName);
        Assert.Equal(leverkusen.Name, firstLeg.WinningTeamName);
        Assert.Contains(league.Fixtures, fixture =>
            fixture.RoundName == "Semi Final" &&
            (fixture.HomeTeam.Name == leverkusen.Name || fixture.AwayTeam.Name == leverkusen.Name));
        Assert.DoesNotContain(league.Fixtures, fixture =>
            fixture.RoundName == "Semi Final" &&
            (fixture.HomeTeam.Name == chelsea.Name || fixture.AwayTeam.Name == chelsea.Name));

        foreach (var tieFixture in league.Fixtures.Where(fixture =>
            fixture.KnockoutTieId == secondLeg.KnockoutTieId))
        {
            tieFixture.WinningTeamName = chelsea.Name;
            tieFixture.LosingTeamName = leverkusen.Name;
        }
        var semiFinal = league.Fixtures.First(fixture =>
            fixture.RoundName == "Semi Final" &&
            (fixture.HomeTeam.Name == leverkusen.Name || fixture.AwayTeam.Name == leverkusen.Name));
        if (semiFinal.HomeTeam.Name == leverkusen.Name)
        {
            semiFinal.HomeTeam = chelsea;
        }
        else
        {
            semiFinal.AwayTeam = chelsea;
        }

        Assert.True(progression.RecoverMissingKnockoutRound(league));
        Assert.Equal(leverkusen.Name, secondLeg.WinningTeamName);
        Assert.Contains(league.Fixtures, fixture =>
            fixture.RoundName == "Semi Final" &&
            (fixture.HomeTeam.Name == leverkusen.Name || fixture.AwayTeam.Name == leverkusen.Name));
    }

    [Fact]
    public void ChampionsLeague_CompletingFirstLegRoundDoesNotSimulateSecondLegs()
    {
        var calendar = new SeasonCalendarService();
        var engine = new LeagueEngine();
        var teams = Enumerable.Range(1, 4).Select(index => CreateTeam($"Leg Team {index}")).ToList();
        var league = new League
        {
            Name = "Test League",
            Season = "2025-26",
            Teams = teams,
            CompetitionStates =
            [
                new SeasonCompetitionState
                {
                    Competition = CompetitionType.ChampionsLeague,
                    CurrentRoundName = "Round of 16",
                    QualifiedTeamNames = teams.Select(team => team.Name).ToList()
                }
            ]
        };
        league.Fixtures = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Round of 16",
            teams,
            51,
            league.Season);
        var selectedFirstLeg = league.Fixtures.First(fixture => fixture.LegNumber == 1);
        var selectedSecondLeg = league.Fixtures.Single(fixture =>
            fixture.LegNumber == 2 && fixture.KnockoutTieId == selectedFirstLeg.KnockoutTieId);

        engine.SimulateFixture(league, selectedFirstLeg, seed: 901);
        engine.SimulateRemainingFixturesForCompetitionRound(league, selectedFirstLeg, seed: 902);

        Assert.All(league.Fixtures.Where(fixture => fixture.LegNumber == 1), fixture => Assert.True(fixture.IsPlayed));
        Assert.All(league.Fixtures.Where(fixture => fixture.LegNumber == 2), fixture => Assert.False(fixture.IsPlayed));
        Assert.False(selectedSecondLeg.IsPlayed);
        Assert.Equal(string.Empty, selectedSecondLeg.WinningTeamName);
    }

    [Fact]
    public void ChampionsLeague_KnockoutScheduleLeavesRecoveryGapsBetweenRounds()
    {
        var calendar = new SeasonCalendarService();
        var teams = Enumerable.Range(1, 16).Select(index => CreateTeam($"Schedule Team {index}")).ToList();

        var roundOf16 = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague, "Round of 16", teams, 51, "2025-26");
        var quarterFinal = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague, "Quarter Final", teams.Take(8).ToList(), 59, "2025-26");
        var semiFinal = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague, "Semi Final", teams.Take(4).ToList(), 67, "2025-26");
        var final = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague, "Final", teams.Take(2).ToList(), 77, "2025-26");

        Assert.True(quarterFinal.Min(fixture => fixture.CalendarRound) - roundOf16.Max(fixture => fixture.CalendarRound) >= 6);
        Assert.True(semiFinal.Min(fixture => fixture.CalendarRound) - quarterFinal.Max(fixture => fixture.CalendarRound) >= 6);
        Assert.True(final.Min(fixture => fixture.CalendarRound) - semiFinal.Max(fixture => fixture.CalendarRound) >= 8);
        Assert.Single(final);
        Assert.False(final[0].IsTwoLeggedTie);
    }

    [Fact]
    public void ChampionsLeague_OverdueTwoLegTieIsMovedAfterCurrentRoundWithLeagueMatchBetweenLegs()
    {
        var calendar = new SeasonCalendarService();
        var progression = new CompetitionProgressionService();
        var teams = Enumerable.Range(1, 4).Select(index => CreateTeam($"UCL Team {index}")).ToList();
        var league = new League
        {
            Name = "Test League",
            Season = "2025-26",
            Teams = teams
        };
        league.Fixtures = calendar.GenerateNextCupRoundFixtures(
            CompetitionType.ChampionsLeague,
            "Round of 16",
            teams,
            61,
            league.Season);
        league.Fixtures.Add(new Fixture
        {
            Competition = CompetitionType.PremierLeague,
            RoundName = "Round 36",
            CalendarRound = 72,
            RoundNumber = 36,
            HomeTeam = teams[0],
            AwayTeam = teams[1],
            IsPlayed = true
        });
        league.Fixtures.Add(new Fixture
        {
            Competition = CompetitionType.PremierLeague,
            RoundName = "Round 37",
            CalendarRound = 74,
            RoundNumber = 37,
            HomeTeam = teams[0],
            AwayTeam = teams[2]
        });

        Assert.True(progression.RecoverMissingKnockoutRound(league));

        var knockoutRounds = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague)
            .Select(fixture => fixture.CalendarRound)
            .Distinct()
            .Order()
            .ToArray();
        Assert.Equal([73, 75], knockoutRounds);
        Assert.Contains(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.PremierLeague &&
            fixture.CalendarRound > knockoutRounds[0] &&
            fixture.CalendarRound < knockoutRounds[1]);
    }

    [Fact]
    public void SaveLoad_PreservesCompetitionFixturesStateAndStats()
    {
        var saveDirectory = Path.Combine(Path.GetTempPath(), $"football-multi-competition-{Guid.NewGuid():N}");
        var saveGameService = new SaveGameService(saveDirectory);
        var engine = new LeagueEngine();
        var league = CreateLeague(teamCount: 8, engine);
        var selectedTeam = league.Teams[0];
        engine.SimulateFixture(league, league.Fixtures.First(fixture => fixture.Competition == CompetitionType.PremierLeague), seed: 101);
        engine.SimulateFixture(league, league.Fixtures.First(fixture => fixture.Competition == CompetitionType.FACup), seed: 102);

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, selectedTeam));

            var loadedData = saveGameService.LoadGame(1)!;
            var loadedLeague = SaveGameService.CreateLeague(loadedData);

            Assert.Contains(loadedLeague.Fixtures, fixture => fixture.Competition == CompetitionType.FACup);
            Assert.Contains(loadedLeague.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague);
            Assert.NotEmpty(loadedLeague.CompetitionStates);
            Assert.NotEmpty(loadedLeague.PlayerCompetitionStats);
        }
        finally
        {
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, recursive: true);
            }
        }
    }

    private static League CreateLeague(int teamCount, LeagueEngine? engine = null)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(teamCount).ToList();
        return (engine ?? new LeagueEngine()).CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
    }

    private static void CompleteFixture(
        CompetitionProgressionService progression,
        League league,
        Fixture fixture,
        int homeScore,
        int awayScore)
    {
        fixture.Result = new Match
        {
            HomeTeam = fixture.HomeTeam,
            AwayTeam = fixture.AwayTeam,
            HomeScore = homeScore,
            AwayScore = awayScore,
            CurrentPhase = MatchPhase.Fulltime,
            CurrentMinute = 90
        };
        fixture.IsPlayed = true;
        progression.ProcessCompletedFixture(league, fixture, seed: 7);
    }

    private static Fixture CreateCompletedKnockoutFixture(
        CompetitionType competition,
        string roundName,
        Team homeTeam,
        Team awayTeam,
        Team winningTeam)
    {
        var losingTeam = winningTeam.Name.Equals(homeTeam.Name, StringComparison.OrdinalIgnoreCase)
            ? awayTeam
            : homeTeam;
        return new Fixture
        {
            Competition = competition,
            RoundName = roundName,
            KnockoutRoundKey = roundName,
            CalendarRound = 67,
            RoundNumber = 67,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            IsKnockout = true,
            IsPlayed = true,
            WinningTeamName = winningTeam.Name,
            LosingTeamName = losingTeam.Name,
            Result = new Match
            {
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                HomeScore = winningTeam.Name.Equals(homeTeam.Name, StringComparison.OrdinalIgnoreCase) ? 2 : 1,
                AwayScore = winningTeam.Name.Equals(awayTeam.Name, StringComparison.OrdinalIgnoreCase) ? 2 : 1,
                CurrentPhase = MatchPhase.Fulltime,
                CurrentMinute = 90
            }
        };
    }

    private static Team CreateTeam(string name)
    {
        return new Team { Name = name };
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }
}
