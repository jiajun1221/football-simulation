using FootballSimulation.Data;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class GameFlowService
{
    private readonly LeagueSeedDataService _leagueSeedDataService;
    private readonly GameSessionService _gameSessionService;

    public GameFlowService()
        : this(new LeagueSeedDataService(), new GameSessionService())
    {
    }

    public GameFlowService(LeagueSeedDataService leagueSeedDataService, GameSessionService gameSessionService)
    {
        _leagueSeedDataService = leagueSeedDataService;
        _gameSessionService = gameSessionService;
    }

    public void Run()
    {
        PrintWelcomeScreen();

        var teams = _leagueSeedDataService.CreateLeagueTeams();
        var selectedTeam = SelectTeam(teams);
        var league = _gameSessionService.CreatePremierLeague(teams);
        var fixture = _gameSessionService.FindNextFixtureForTeam(league, selectedTeam);

        PrintMatchIntro(fixture, selectedTeam);

        var match = _gameSessionService.SimulateSelectedTeamFixture(league, selectedTeam);

        PrintMatchEvents(match);
        PrintFinalScore(match);
        PrintRoundResults(league, fixture.RoundNumber);
        PrintLeagueTable(league);
    }

    private static void PrintWelcomeScreen()
    {
        global::System.Console.WriteLine("Football Simulation");
        global::System.Console.WriteLine(new string('=', 50));
        global::System.Console.WriteLine($"Welcome to the {GameSessionService.PremierLeagueName}");
        global::System.Console.WriteLine();
    }

    private static Team SelectTeam(IReadOnlyList<Team> teams)
    {
        global::System.Console.WriteLine("Choose your team");
        global::System.Console.WriteLine(new string('-', 50));

        for (var index = 0; index < teams.Count; index++)
        {
            var team = teams[index];
            global::System.Console.WriteLine($"{index + 1}. {team.Name} ({team.Formation})");
        }

        global::System.Console.WriteLine();

        while (true)
        {
            global::System.Console.Write("Enter team number: ");
            var input = global::System.Console.ReadLine();

            if (int.TryParse(input, out var teamNumber) &&
                teamNumber >= 1 &&
                teamNumber <= teams.Count)
            {
                var selectedTeam = teams[teamNumber - 1];
                global::System.Console.WriteLine();
                global::System.Console.WriteLine($"You selected {selectedTeam.Name}.");
                global::System.Console.WriteLine();
                return selectedTeam;
            }

            global::System.Console.WriteLine("Please enter a valid team number.");
        }
    }

    private static void PrintMatchIntro(Fixture fixture, Team selectedTeam)
    {
        global::System.Console.WriteLine($"Round {fixture.RoundNumber} Match");
        global::System.Console.WriteLine(new string('-', 50));
        global::System.Console.WriteLine($"{fixture.HomeTeam.Name} vs {fixture.AwayTeam.Name}");
        global::System.Console.WriteLine($"{selectedTeam.Name} is ready for kickoff.");
        global::System.Console.WriteLine();
    }

    private static void PrintMatchEvents(Match match)
    {
        global::System.Console.WriteLine("Match Events");
        global::System.Console.WriteLine(new string('-', 50));

        foreach (var matchEvent in match.Events.OrderBy(matchEvent => matchEvent.Minute))
        {
            global::System.Console.WriteLine($"{matchEvent.Minute,2}' [{matchEvent.EventType}] {matchEvent.Description}");
        }

        global::System.Console.WriteLine();
    }

    private static void PrintFinalScore(Match match)
    {
        global::System.Console.WriteLine("Final Score");
        global::System.Console.WriteLine(new string('-', 50));
        global::System.Console.WriteLine($"{match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}");
        global::System.Console.WriteLine();
    }

    private static void PrintRoundResults(League league, int roundNumber)
    {
        var roundFixtures = league.Fixtures
            .Where(fixture => fixture.RoundNumber == roundNumber)
            .ToList();

        global::System.Console.WriteLine($"Round {roundNumber} Results");
        global::System.Console.WriteLine(new string('-', 50));

        foreach (var fixture in roundFixtures)
        {
            if (fixture.Result is null)
            {
                global::System.Console.WriteLine($"{fixture.HomeTeam.Name} vs {fixture.AwayTeam.Name} ");
                continue;
            }

            global::System.Console.WriteLine(
                $"{fixture.HomeTeam.Name} {fixture.Result.HomeScore} - {fixture.Result.AwayScore} {fixture.AwayTeam.Name}");
        }

        global::System.Console.WriteLine();
    }

    private static void PrintLeagueTable(League league)
    {
        global::System.Console.WriteLine("Updated League Table");
        global::System.Console.WriteLine(new string('-', 80));
        global::System.Console.WriteLine($"{"Pos",3} {"Team",-18} {"P",3} {"W",3} {"D",3} {"L",3} {"GF",4} {"GA",4} {"GD",4} {"Pts",4}");

        for (var index = 0; index < league.Table.Count; index++)
        {
            var entry = league.Table[index];
            global::System.Console.WriteLine(
                $"{index + 1,3} {entry.TeamName,-18} {entry.Played,3} {entry.Wins,3} {entry.Draws,3} {entry.Losses,3} {entry.GoalsFor,4} {entry.GoalsAgainst,4} {entry.GoalDifference,4} {entry.Points,4}");
        }
    }
}
