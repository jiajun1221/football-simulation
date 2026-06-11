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

    private static readonly UclClubDefinition[] ChampionsLeagueClubPool =
    [
        new("Real Madrid", "Spain", 1, 90),
        new("Manchester City", "England", 1, 90),
        new("Bayern Munich", "Germany", 1, 89),
        new("Paris Saint-Germain", "France", 1, 88),
        new("Inter Milan", "Italy", 1, 87),
        new("Liverpool", "England", 1, 87),
        new("Barcelona", "Spain", 1, 87),
        new("Borussia Dortmund", "Germany", 1, 85),
        new("Atletico Madrid", "Spain", 1, 85),

        new("Arsenal", "England", 2, 86),
        new("Bayer Leverkusen", "Germany", 2, 84),
        new("Juventus", "Italy", 2, 84),
        new("AC Milan", "Italy", 2, 83),
        new("Benfica", "Portugal", 2, 82),
        new("Porto", "Portugal", 2, 82),
        new("Napoli", "Italy", 2, 83),
        new("RB Leipzig", "Germany", 2, 82),
        new("Chelsea", "England", 2, 84),

        new("Ajax", "Netherlands", 3, 80),
        new("PSV", "Netherlands", 3, 80),
        new("Sporting CP", "Portugal", 3, 80),
        new("Feyenoord", "Netherlands", 3, 79),
        new("Celtic", "Scotland", 3, 78),
        new("Galatasaray", "Turkey", 3, 79),
        new("Monaco", "France", 3, 80),
        new("Marseille", "France", 3, 79),
        new("Atalanta", "Italy", 3, 80),

        new("Club Brugge", "Belgium", 4, 76),
        new("Salzburg", "Austria", 4, 76),
        new("Shakhtar Donetsk", "Ukraine", 4, 76),
        new("Dinamo Zagreb", "Croatia", 4, 74),
        new("Young Boys", "Switzerland", 4, 74),
        new("Sparta Prague", "Czech Republic", 4, 73),
        new("Red Star Belgrade", "Serbia", 4, 73),
        new("Sturm Graz", "Austria", 4, 72),
        new("Slovan Bratislava", "Slovakia", 4, 71),

        // Reserve clubs fill slots when a user's league team misses UCL qualification after rollover.
        new("Athletic Club", "Spain", 1, 80),
        new("Villarreal", "Spain", 1, 81),
        new("Roma", "Italy", 1, 81),
        new("Eintracht Frankfurt", "Germany", 1, 80),
        new("Lille", "France", 1, 79),
        new("Newcastle United", "England", 1, 81),

        new("Real Sociedad", "Spain", 2, 79),
        new("Lazio", "Italy", 2, 79),
        new("Stuttgart", "Germany", 2, 78),
        new("Lyon", "France", 2, 78),
        new("Aston Villa", "England", 2, 80),
        new("Sevilla", "Spain", 2, 76),

        new("Fiorentina", "Italy", 3, 76),
        new("Freiburg", "Germany", 3, 75),
        new("Nice", "France", 3, 75),
        new("Brentford", "England", 3, 75),
        new("Valencia", "Spain", 3, 75),
        new("Mainz 05", "Germany", 3, 74),

        new("Real Betis", "Spain", 4, 76),
        new("Torino", "Italy", 4, 74),
        new("FC Koln", "Germany", 4, 73),
        new("Auxerre", "France", 4, 72),
        new("Wolverhampton Wanderers", "England", 4, 74)
    ];

    private static readonly Dictionary<string, UclClubDefinition> ChampionsLeagueClubByName = ChampionsLeagueClubPool
        .ToDictionary(club => club.Name, StringComparer.OrdinalIgnoreCase);

    public List<Fixture> GenerateSeasonFixtures(
        IReadOnlyList<Team> premierLeagueTeams,
        string season,
        IReadOnlyCollection<string>? championsLeagueQualifiedTeamNames = null)
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
        fixtures.AddRange(CreateChampionsLeagueLeaguePhaseFixtures(premierLeagueTeams, season, championsLeagueQualifiedTeamNames));

        return fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
    }

    public List<SeasonCompetitionState> CreateInitialCompetitionStates(
        IReadOnlyList<Team> premierLeagueTeams,
        IReadOnlyCollection<string>? championsLeagueQualifiedTeamNames = null)
    {
        var leagueTeams = premierLeagueTeams.Select(team => team.Name).ToList();
        var uclTeams = CreateChampionsLeagueEntrants(premierLeagueTeams, championsLeagueQualifiedTeamNames)
            .Select(entry => entry.Team.Name)
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

    private static List<Fixture> CreateChampionsLeagueLeaguePhaseFixtures(
        IReadOnlyList<Team> premierLeagueTeams,
        string season,
        IReadOnlyCollection<string>? championsLeagueQualifiedTeamNames)
    {
        var entrants = CreateChampionsLeagueEntrants(premierLeagueTeams, championsLeagueQualifiedTeamNames);
        if (entrants.Count < 8)
        {
            return [];
        }

        var pairings = CreateSwissPhasePairings(entrants);
        var fixtures = CreateSwissPhaseFixtures(pairings, season);
        return fixtures
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.RoundName)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ToList();
    }

    private static SeasonCompetitionState CreateChampionsLeagueState(List<string> teamNames)
    {
        var state = new SeasonCompetitionState
        {
            Competition = CompetitionType.ChampionsLeague,
            Name = CompetitionNames.GetDisplayName(CompetitionType.ChampionsLeague),
            QualifiedTeamNames = teamNames,
            RoundOrder = ["League Phase", "Round of 16", "Quarter Final", "Semi Final", "Final"],
            CurrentRoundName = "League Phase",
            Standings = teamNames.Select(teamName => new CompetitionStandingRow
            {
                TeamName = teamName,
                GroupName = "League Phase"
            }).ToList()
        };

        return state;
    }

    private static IEnumerable<Team> SelectChampionsLeagueTeams(
        IReadOnlyList<Team> premierLeagueTeams,
        IReadOnlyCollection<string>? championsLeagueQualifiedTeamNames)
    {
        if (championsLeagueQualifiedTeamNames is not null)
        {
            return championsLeagueQualifiedTeamNames
                .Select(name => premierLeagueTeams.FirstOrDefault(team => team.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Where(team => team is not null)
                .Cast<Team>()
                .DistinctBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
                .Take(4);
        }

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

    private static List<UclTeamEntry> CreateChampionsLeagueEntrants(
        IReadOnlyList<Team> premierLeagueTeams,
        IReadOnlyCollection<string>? championsLeagueQualifiedTeamNames = null)
    {
        var availableEuropeanTeams = LoadAvailableEuropeanTeamsByName();
        var selectedPremierLeagueTeams = SelectChampionsLeagueTeams(premierLeagueTeams, championsLeagueQualifiedTeamNames).ToList();
        var entries = selectedPremierLeagueTeams
            .Select(team =>
            {
                var definition = ChampionsLeagueClubByName.GetValueOrDefault(team.Name) ??
                    new UclClubDefinition(team.Name, "England", 3, GetTeamStrength(team));
                return new UclTeamEntry(team, definition.Country, definition.Pot, definition.Strength);
            })
            .ToList();

        var selectedNames = entries.Select(entry => entry.Team.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentLeagueTeamNames = premierLeagueTeams.Select(team => team.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var useQualifiedLeagueTeamsOnly = championsLeagueQualifiedTeamNames is not null;
        foreach (var pot in Enumerable.Range(1, 4))
        {
            foreach (var definition in ChampionsLeagueClubPool.Where(definition => definition.Pot == pot))
            {
                if (selectedNames.Contains(definition.Name) ||
                    (useQualifiedLeagueTeamsOnly && currentLeagueTeamNames.Contains(definition.Name)) ||
                    entries.Count(entry => entry.Pot == pot) >= 9)
                {
                    continue;
                }

                var team = availableEuropeanTeams.GetValueOrDefault(definition.Name) ??
                    PlaceholderTeamFactory.Create(definition.Name, definition.Strength, venueSuffix: "Arena", country: definition.Country);
                entries.Add(new UclTeamEntry(team, definition.Country, definition.Pot, definition.Strength));
                selectedNames.Add(definition.Name);
            }
        }

        return entries
            .OrderBy(entry => entry.Pot)
            .ThenByDescending(entry => entry.Strength)
            .ThenBy(entry => entry.Team.Name)
            .ToList();
    }

    private static Dictionary<string, Team> LoadAvailableEuropeanTeamsByName()
    {
        try
        {
            var dataService = new LeagueDataService();
            return dataService.LoadSquadSourceDefinitions()
                .SelectMany(dataService.LoadTeams)
                .GroupBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static List<UclPairing> CreateSwissPhasePairings(IReadOnlyList<UclTeamEntry> entrants)
    {
        var pairings = new List<UclPairing>();
        var pairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teamsByPot = entrants
            .GroupBy(entry => entry.Pot)
            .ToDictionary(group => group.Key, group => OrderTeamsForPot(group.ToList()));

        foreach (var pot in Enumerable.Range(1, 4))
        {
            if (!teamsByPot.TryGetValue(pot, out var potTeams) || potTeams.Count < 3)
            {
                continue;
            }

            for (var index = 0; index < potTeams.Count; index++)
            {
                AddPairingIfNew(potTeams[index], potTeams[(index + 1) % potTeams.Count], pairings, pairKeys);
            }
        }

        for (var firstPot = 1; firstPot <= 4; firstPot++)
        {
            for (var secondPot = firstPot + 1; secondPot <= 4; secondPot++)
            {
                if (teamsByPot.TryGetValue(firstPot, out var firstPotTeams) &&
                    teamsByPot.TryGetValue(secondPot, out var secondPotTeams))
                {
                    AddCrossPotPairings(firstPotTeams, secondPotTeams, pairings, pairKeys);
                }
            }
        }

        return pairings;
    }

    private static List<UclTeamEntry> OrderTeamsForPot(List<UclTeamEntry> teams)
    {
        var remaining = teams
            .OrderByDescending(team => team.Strength)
            .ThenBy(team => team.Team.Name)
            .ToList();
        var ordered = new List<UclTeamEntry>();

        while (remaining.Count > 0)
        {
            var last = ordered.LastOrDefault();
            var next = remaining
                .Where(team => last is null || !team.Country.Equals(last.Country, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(team => team.Strength)
                .FirstOrDefault() ?? remaining[0];

            ordered.Add(next);
            remaining.Remove(next);
        }

        if (ordered.Count > 2 &&
            ordered[0].Country.Equals(ordered[^1].Country, StringComparison.OrdinalIgnoreCase))
        {
            for (var index = ordered.Count - 2; index > 0; index--)
            {
                if (!ordered[index].Country.Equals(ordered[0].Country, StringComparison.OrdinalIgnoreCase) &&
                    !ordered[index].Country.Equals(ordered[^2].Country, StringComparison.OrdinalIgnoreCase))
                {
                    (ordered[index], ordered[^1]) = (ordered[^1], ordered[index]);
                    break;
                }
            }
        }

        return ordered;
    }

    private static void AddCrossPotPairings(
        IReadOnlyList<UclTeamEntry> firstPotTeams,
        IReadOnlyList<UclTeamEntry> secondPotTeams,
        List<UclPairing> pairings,
        HashSet<string> pairKeys)
    {
        if (firstPotTeams.Count == 0 || secondPotTeams.Count == 0)
        {
            return;
        }

        var (firstOffset, secondOffset) = FindBestPotOffsets(firstPotTeams, secondPotTeams);
        for (var index = 0; index < firstPotTeams.Count; index++)
        {
            AddPairingIfNew(firstPotTeams[index], secondPotTeams[(index + firstOffset) % secondPotTeams.Count], pairings, pairKeys);
            AddPairingIfNew(firstPotTeams[index], secondPotTeams[(index + secondOffset) % secondPotTeams.Count], pairings, pairKeys);
        }
    }

    private static (int FirstOffset, int SecondOffset) FindBestPotOffsets(
        IReadOnlyList<UclTeamEntry> firstPotTeams,
        IReadOnlyList<UclTeamEntry> secondPotTeams)
    {
        var bestOffsets = (FirstOffset: 0, SecondOffset: 1);
        var bestScore = int.MaxValue;
        for (var firstOffset = 0; firstOffset < secondPotTeams.Count; firstOffset++)
        {
            for (var secondOffset = 0; secondOffset < secondPotTeams.Count; secondOffset++)
            {
                if (firstOffset == secondOffset)
                {
                    continue;
                }

                var score = 0;
                for (var index = 0; index < firstPotTeams.Count; index++)
                {
                    if (firstPotTeams[index].Country.Equals(secondPotTeams[(index + firstOffset) % secondPotTeams.Count].Country, StringComparison.OrdinalIgnoreCase))
                    {
                        score++;
                    }

                    if (firstPotTeams[index].Country.Equals(secondPotTeams[(index + secondOffset) % secondPotTeams.Count].Country, StringComparison.OrdinalIgnoreCase))
                    {
                        score++;
                    }
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestOffsets = (firstOffset, secondOffset);
                }
            }
        }

        return bestOffsets;
    }

    private static void AddPairingIfNew(
        UclTeamEntry first,
        UclTeamEntry second,
        List<UclPairing> pairings,
        HashSet<string> pairKeys)
    {
        if (first.Team.Name.Equals(second.Team.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var orderedNames = new[] { first.Team.Name, second.Team.Name }.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var pairKey = $"{orderedNames[0]}|{orderedNames[1]}";
        if (pairKeys.Add(pairKey))
        {
            pairings.Add(new UclPairing(first, second));
        }
    }

    private static List<Fixture> CreateSwissPhaseFixtures(IReadOnlyList<UclPairing> pairings, string season)
    {
        var homeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var awayCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var calendarRounds = new[] { 5, 13, 23, 33, 43, 53, 63, 73 };
        var fixtures = new List<Fixture>();
        var scheduledPairings = AssignMatchdays(pairings);

        foreach (var scheduledPairing in scheduledPairings
            .OrderBy(item => item.Matchday)
            .ThenBy(item => item.Pairing.First.Pot + item.Pairing.Second.Pot)
            .ThenBy(item => item.Pairing.First.Team.Name)
            .ThenBy(item => item.Pairing.Second.Team.Name))
        {
            var pairing = scheduledPairing.Pairing;
            var home = ChooseHomeTeam(pairing.First, pairing.Second, homeCounts, awayCounts);
            var away = home.Team.Name.Equals(pairing.First.Team.Name, StringComparison.OrdinalIgnoreCase) ? pairing.Second : pairing.First;
            var matchday = scheduledPairing.Matchday;
            homeCounts[home.Team.Name] = homeCounts.GetValueOrDefault(home.Team.Name) + 1;
            awayCounts[away.Team.Name] = awayCounts.GetValueOrDefault(away.Team.Name) + 1;

            fixtures.Add(new Fixture
            {
                RoundNumber = matchday,
                CalendarRound = calendarRounds[Math.Clamp(matchday - 1, 0, calendarRounds.Length - 1)],
                ScheduledDate = CreateSeasonDate(season, calendarRounds[Math.Clamp(matchday - 1, 0, calendarRounds.Length - 1)]),
                Competition = CompetitionType.ChampionsLeague,
                RoundName = $"League Phase MD{matchday}",
                HomeTeam = home.Team,
                AwayTeam = away.Team,
                Venue = GetVenueName(home.Team),
                AffectsLeagueTable = false,
                IsKnockout = false,
                KnockoutRoundKey = "League Phase",
                Importance = FixtureImportance.High
            });
        }

        return fixtures;
    }

    private static List<ScheduledUclPairing> AssignMatchdays(IReadOnlyList<UclPairing> pairings)
    {
        var assignedMatchdays = new int[pairings.Count];
        var matchdayCounts = new int[9];
        var teamMatchdayUsage = pairings
            .SelectMany(pairing => new[] { pairing.First.Team.Name, pairing.Second.Team.Name })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(teamName => teamName, _ => new HashSet<int>(), StringComparer.OrdinalIgnoreCase);

        if (!TryAssignMatchday(pairings, assignedMatchdays, matchdayCounts, teamMatchdayUsage, assignedCount: 0))
        {
            throw new InvalidOperationException("Unable to create a balanced Champions League matchday schedule.");
        }

        return pairings
            .Select((pairing, index) => new ScheduledUclPairing(pairing, assignedMatchdays[index]))
            .ToList();
    }

    private static bool TryAssignMatchday(
        IReadOnlyList<UclPairing> pairings,
        int[] assignedMatchdays,
        int[] matchdayCounts,
        Dictionary<string, HashSet<int>> teamMatchdayUsage,
        int assignedCount)
    {
        if (assignedCount == pairings.Count)
        {
            return true;
        }

        var selectedIndex = -1;
        List<int>? selectedOptions = null;
        for (var index = 0; index < pairings.Count; index++)
        {
            if (assignedMatchdays[index] != 0)
            {
                continue;
            }

            var options = GetAvailableMatchdays(pairings[index], matchdayCounts, teamMatchdayUsage).ToList();
            if (options.Count == 0)
            {
                return false;
            }

            if (selectedOptions is null || options.Count < selectedOptions.Count)
            {
                selectedIndex = index;
                selectedOptions = options;
                if (options.Count == 1)
                {
                    break;
                }
            }
        }

        if (selectedIndex < 0 || selectedOptions is null)
        {
            return false;
        }

        foreach (var matchday in selectedOptions.OrderBy(matchday => matchdayCounts[matchday]).ThenBy(matchday => matchday))
        {
            AssignMatchday(pairings[selectedIndex], selectedIndex, matchday, assignedMatchdays, matchdayCounts, teamMatchdayUsage);
            if (TryAssignMatchday(pairings, assignedMatchdays, matchdayCounts, teamMatchdayUsage, assignedCount + 1))
            {
                return true;
            }

            UnassignMatchday(pairings[selectedIndex], selectedIndex, matchday, assignedMatchdays, matchdayCounts, teamMatchdayUsage);
        }

        return false;
    }

    private static IEnumerable<int> GetAvailableMatchdays(
        UclPairing pairing,
        IReadOnlyList<int> matchdayCounts,
        IReadOnlyDictionary<string, HashSet<int>> teamMatchdayUsage)
    {
        var firstTeamName = pairing.First.Team.Name;
        var secondTeamName = pairing.Second.Team.Name;
        for (var matchday = 1; matchday <= 8; matchday++)
        {
            if (matchdayCounts[matchday] < 18 &&
                !teamMatchdayUsage[firstTeamName].Contains(matchday) &&
                !teamMatchdayUsage[secondTeamName].Contains(matchday))
            {
                yield return matchday;
            }
        }
    }

    private static void AssignMatchday(
        UclPairing pairing,
        int pairingIndex,
        int matchday,
        int[] assignedMatchdays,
        IList<int> matchdayCounts,
        IDictionary<string, HashSet<int>> teamMatchdayUsage)
    {
        assignedMatchdays[pairingIndex] = matchday;
        matchdayCounts[matchday]++;
        teamMatchdayUsage[pairing.First.Team.Name].Add(matchday);
        teamMatchdayUsage[pairing.Second.Team.Name].Add(matchday);
    }

    private static void UnassignMatchday(
        UclPairing pairing,
        int pairingIndex,
        int matchday,
        int[] assignedMatchdays,
        IList<int> matchdayCounts,
        IDictionary<string, HashSet<int>> teamMatchdayUsage)
    {
        assignedMatchdays[pairingIndex] = 0;
        matchdayCounts[matchday]--;
        teamMatchdayUsage[pairing.First.Team.Name].Remove(matchday);
        teamMatchdayUsage[pairing.Second.Team.Name].Remove(matchday);
    }

    private static UclTeamEntry ChooseHomeTeam(
        UclTeamEntry first,
        UclTeamEntry second,
        IReadOnlyDictionary<string, int> homeCounts,
        IReadOnlyDictionary<string, int> awayCounts)
    {
        var firstHome = homeCounts.GetValueOrDefault(first.Team.Name);
        var secondHome = homeCounts.GetValueOrDefault(second.Team.Name);
        var firstAway = awayCounts.GetValueOrDefault(first.Team.Name);
        var secondAway = awayCounts.GetValueOrDefault(second.Team.Name);

        if (firstHome >= 4 && secondHome < 4)
        {
            return second;
        }

        if (secondHome >= 4 && firstHome < 4)
        {
            return first;
        }

        if (firstAway >= 4 && secondAway < 4)
        {
            return first;
        }

        if (secondAway >= 4 && firstAway < 4)
        {
            return second;
        }

        return firstHome <= secondHome ? first : second;
    }

    private static int GetTeamStrength(Team team)
    {
        return (int)Math.Round(team.Players.Concat(team.Substitutes).DefaultIfEmpty().Average(player => player?.OverallRating ?? 75));
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

    private sealed record UclClubDefinition(string Name, string Country, int Pot, int Strength);

    private sealed record UclTeamEntry(Team Team, string Country, int Pot, int Strength);

    private sealed record UclPairing(UclTeamEntry First, UclTeamEntry Second);

    private sealed record ScheduledUclPairing(UclPairing Pairing, int Matchday);
}
