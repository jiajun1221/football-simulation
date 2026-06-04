using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SeasonCalendarService
{
    private static readonly string[] LowerLeagueClubNames =
    [
        "Coventry City",
        "Blackburn Rovers",
        "Middlesbrough",
        "Swansea City",
        "Hull City",
        "Bristol City",
        "Preston North End",
        "Cardiff City",
        "QPR",
        "Watford",
        "Millwall",
        "Stoke City",
        "Derby County",
        "Portsmouth",
        "Oxford United",
        "Plymouth Argyle"
    ];

    private static readonly string[] EuropeanClubNames =
    [
        "Real Madrid",
        "Barcelona",
        "Bayern Munich",
        "Paris Saint-Germain",
        "Inter Milan",
        "AC Milan",
        "Juventus",
        "Borussia Dortmund",
        "Atletico Madrid",
        "Benfica",
        "Porto",
        "Ajax",
        "Sporting CP",
        "Bayer Leverkusen",
        "RB Leipzig",
        "Sevilla",
        "Villarreal",
        "Real Sociedad",
        "PSV Eindhoven",
        "Feyenoord",
        "Celtic",
        "Rangers",
        "Galatasaray",
        "Shakhtar Donetsk",
        "Club Brugge",
        "Monaco",
        "Marseille",
        "Lyon",
        "Napoli",
        "Roma",
        "Lazio",
        "Atalanta",
        "Red Bull Salzburg",
        "Dinamo Zagreb",
        "Anderlecht",
        "Fenerbahce",
        "Olympiacos",
        "Copenhagen",
        "Young Boys"
    ];

    public List<Fixture> GenerateSeasonFixtures(IReadOnlyList<Team> premierLeagueTeams, string season)
    {
        ArgumentNullException.ThrowIfNull(premierLeagueTeams);

        var fixtures = new List<Fixture>();
        fixtures.AddRange(CreatePremierLeagueFixtures(premierLeagueTeams, season));
        fixtures.AddRange(CreateDomesticCupOpeningRound(
            premierLeagueTeams,
            CompetitionType.LeagueCup,
            "Third Round",
            calendarRound: 9,
            baseOverall: 66,
            season));
        fixtures.AddRange(CreateDomesticCupOpeningRound(
            premierLeagueTeams,
            CompetitionType.FACup,
            "Third Round",
            calendarRound: 21,
            baseOverall: 64,
            season));
        fixtures.AddRange(CreateChampionsLeagueGroupFixtures(premierLeagueTeams, season));

        return fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
    }

    public List<SeasonCompetitionState> CreateInitialCompetitionStates(IReadOnlyList<Team> premierLeagueTeams)
    {
        var leagueTeams = premierLeagueTeams.Select(team => team.Name).ToList();
        var uclTeams = SelectChampionsLeagueTeams(premierLeagueTeams)
            .Select(team => team.Name)
            .Concat(EuropeanClubNames)
            .Take(32)
            .ToList();

        return
        [
            new()
            {
                Competition = CompetitionType.PremierLeague,
                Name = CompetitionNames.GetDisplayName(CompetitionType.PremierLeague),
                QualifiedTeamNames = leagueTeams,
                RoundOrder = Enumerable.Range(1, Math.Max(1, (premierLeagueTeams.Count - 1) * 2)).Select(round => $"Round {round}").ToList(),
                CurrentRoundName = "Round 1"
            },
            new()
            {
                Competition = CompetitionType.LeagueCup,
                Name = CompetitionNames.GetDisplayName(CompetitionType.LeagueCup),
                QualifiedTeamNames = leagueTeams.Concat(LowerLeagueClubNames.Take(premierLeagueTeams.Count)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                RoundOrder = ["Third Round", "Fourth Round", "Quarter Final", "Semi Final", "Final"],
                CurrentRoundName = "Third Round"
            },
            new()
            {
                Competition = CompetitionType.FACup,
                Name = CompetitionNames.GetDisplayName(CompetitionType.FACup),
                QualifiedTeamNames = leagueTeams.Concat(LowerLeagueClubNames.Take(premierLeagueTeams.Count)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                RoundOrder = ["Third Round", "Fourth Round", "Fifth Round", "Quarter Final", "Semi Final", "Final"],
                CurrentRoundName = "Third Round"
            },
            CreateChampionsLeagueState(uclTeams)
        ];
    }

    public List<Fixture> GenerateNextCupRoundFixtures(
        CompetitionType competition,
        string roundName,
        IReadOnlyList<Team> qualifiedTeams,
        int calendarRound,
        string season)
    {
        return PairTeams(qualifiedTeams)
            .Select(pair => CreateFixture(
                pair.Home,
                pair.Away,
                competition,
                roundName,
                calendarRound,
                season,
                affectsLeagueTable: false,
                isKnockout: true))
            .ToList();
    }

    private static List<Fixture> CreatePremierLeagueFixtures(IReadOnlyList<Team> teams, string season)
    {
        return new LeagueScheduleService().GenerateFixtures(teams)
            .Select(fixture =>
            {
                fixture.Competition = CompetitionType.PremierLeague;
                fixture.RoundName = $"Round {fixture.RoundNumber}";
                fixture.CalendarRound = fixture.RoundNumber * 2;
                fixture.ScheduledDate = CreateSeasonDate(season, fixture.CalendarRound);
                fixture.Venue = GetVenueName(fixture.HomeTeam);
                fixture.AffectsLeagueTable = true;
                fixture.Importance = FixtureImportance.Normal;
                return fixture;
            })
            .ToList();
    }

    private static List<Fixture> CreateDomesticCupOpeningRound(
        IReadOnlyList<Team> premierLeagueTeams,
        CompetitionType competition,
        string roundName,
        int calendarRound,
        int baseOverall,
        string season)
    {
        var placeholders = LowerLeagueClubNames
            .Take(premierLeagueTeams.Count)
            .Select(name => PlaceholderTeamFactory.Create(name, baseOverall))
            .ToList();
        var entrants = premierLeagueTeams.Concat(placeholders).ToList();

        return PairTeams(entrants)
            .Select(pair => CreateFixture(
                pair.Home,
                pair.Away,
                competition,
                roundName,
                calendarRound,
                season,
                affectsLeagueTable: false,
                isKnockout: true))
            .ToList();
    }

    private static List<Fixture> CreateChampionsLeagueGroupFixtures(IReadOnlyList<Team> premierLeagueTeams, string season)
    {
        var premierLeagueQualifiers = SelectChampionsLeagueTeams(premierLeagueTeams).ToList();
        var europeanTeams = EuropeanClubNames
            .Select(name => PlaceholderTeamFactory.Create(name, baseOverall: 82, venueSuffix: "Arena"))
            .ToList();
        var entrants = premierLeagueQualifiers.Concat(europeanTeams).Take(32).ToList();
        if (entrants.Count < 4)
        {
            return [];
        }

        var fixtures = new List<Fixture>();
        var groupNames = new[] { "Group A", "Group B", "Group C", "Group D", "Group E", "Group F", "Group G", "Group H" };
        var calendarRounds = new[] { 5, 13, 23, 33, 43, 53 };
        for (var groupIndex = 0; groupIndex < Math.Min(groupNames.Length, entrants.Count / 4); groupIndex++)
        {
            var groupTeams = entrants.Skip(groupIndex * 4).Take(4).ToList();
            var groupFixtures = new LeagueScheduleService().GenerateFixtures(groupTeams);
            foreach (var fixture in groupFixtures)
            {
                fixture.Competition = CompetitionType.ChampionsLeague;
                fixture.RoundName = $"League Phase Matchday {fixture.RoundNumber}";
                fixture.CalendarRound = calendarRounds[Math.Clamp(fixture.RoundNumber - 1, 0, calendarRounds.Length - 1)];
                fixture.ScheduledDate = CreateSeasonDate(season, fixture.CalendarRound);
                fixture.Venue = GetVenueName(fixture.HomeTeam);
                fixture.AffectsLeagueTable = false;
                fixture.IsKnockout = false;
                fixture.KnockoutRoundKey = groupNames[groupIndex];
                fixture.Importance = FixtureImportance.High;
                fixtures.Add(fixture);
            }
        }

        return fixtures;
    }

    private static SeasonCompetitionState CreateChampionsLeagueState(List<string> teamNames)
    {
        var state = new SeasonCompetitionState
        {
            Competition = CompetitionType.ChampionsLeague,
            Name = CompetitionNames.GetDisplayName(CompetitionType.ChampionsLeague),
            QualifiedTeamNames = teamNames,
            RoundOrder = ["League Phase", "Round of 16", "Quarter Final", "Semi Final", "Final"],
            CurrentRoundName = "League Phase"
        };

        var groupNames = new[] { "Group A", "Group B", "Group C", "Group D", "Group E", "Group F", "Group G", "Group H" };
        for (var groupIndex = 0; groupIndex < Math.Min(groupNames.Length, teamNames.Count / 4); groupIndex++)
        {
            var groupTeams = teamNames.Skip(groupIndex * 4).Take(4).ToList();
            state.ChampionsLeagueGroups.Add(new ChampionsLeagueGroup
            {
                Name = groupNames[groupIndex],
                TeamNames = groupTeams,
                Table = groupTeams.Select(teamName => new CompetitionStandingRow
                {
                    TeamName = teamName,
                    GroupName = groupNames[groupIndex]
                }).ToList()
            });
        }

        return state;
    }

    private static IEnumerable<Team> SelectChampionsLeagueTeams(IReadOnlyList<Team> premierLeagueTeams)
    {
        var preferred = new[] { "Chelsea", "Arsenal", "Manchester City", "Liverpool", "Manchester United", "Tottenham Hotspur" };
        return preferred
            .Select(name => premierLeagueTeams.FirstOrDefault(team => team.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(team => team is not null)
            .Cast<Team>()
            .Concat(premierLeagueTeams
                .OrderByDescending(team => team.Players.Concat(team.Substitutes).DefaultIfEmpty().Average(player => player?.OverallRating ?? 72)))
            .Distinct()
            .Take(4);
    }

    private static IEnumerable<(Team Home, Team Away)> PairTeams(IReadOnlyList<Team> teams)
    {
        for (var index = 0; index + 1 < teams.Count; index += 2)
        {
            yield return index % 4 == 0
                ? (teams[index], teams[index + 1])
                : (teams[index + 1], teams[index]);
        }
    }

    private static Fixture CreateFixture(
        Team homeTeam,
        Team awayTeam,
        CompetitionType competition,
        string roundName,
        int calendarRound,
        string season,
        bool affectsLeagueTable,
        bool isKnockout)
    {
        return new Fixture
        {
            RoundNumber = calendarRound,
            CalendarRound = calendarRound,
            ScheduledDate = CreateSeasonDate(season, calendarRound),
            Competition = competition,
            RoundName = roundName,
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            Venue = GetVenueName(homeTeam),
            AffectsLeagueTable = affectsLeagueTable,
            IsKnockout = isKnockout,
            KnockoutRoundKey = roundName,
            Importance = roundName.Contains("Final", StringComparison.OrdinalIgnoreCase)
                ? FixtureImportance.Final
                : isKnockout ? FixtureImportance.Knockout : FixtureImportance.Normal
        };
    }

    private static DateTime? CreateSeasonDate(string season, int calendarRound)
    {
        var startYearText = (season ?? string.Empty).Split('-', '/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(startYearText, out var startYear)
            ? new DateTime(startYear, 8, 1).AddDays(calendarRound * 4)
            : null;
    }

    private static string GetVenueName(Team homeTeam)
    {
        if (!string.IsNullOrWhiteSpace(homeTeam.StadiumName))
        {
            return homeTeam.StadiumName;
        }

        return string.IsNullOrWhiteSpace(homeTeam.Venue)
            ? $"{homeTeam.Name} Stadium"
            : homeTeam.Venue;
    }
}
