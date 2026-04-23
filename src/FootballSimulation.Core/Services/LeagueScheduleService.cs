using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class LeagueScheduleService
{
    public List<Fixture> GenerateFixtures(IReadOnlyList<Team> teams)
    {
        if (teams.Count < 2)
        {
            throw new ArgumentException("A league needs at least two teams.", nameof(teams));
        }

        var workingTeams = teams.ToList();
        var hasBye = workingTeams.Count % 2 != 0;

        if (hasBye)
        {
            workingTeams.Add(new Team { Name = "BYE" });
        }

        var fixtures = new List<Fixture>();
        var rotatingTeams = workingTeams.ToList();
        var totalRounds = rotatingTeams.Count - 1;
        var matchesPerRound = rotatingTeams.Count / 2;

        for (var round = 1; round <= totalRounds; round++)
        {
            for (var matchIndex = 0; matchIndex < matchesPerRound; matchIndex++)
            {
                var homeTeam = rotatingTeams[matchIndex];
                var awayTeam = rotatingTeams[^ (matchIndex + 1)];

                if (homeTeam.Name == "BYE" || awayTeam.Name == "BYE")
                {
                    continue;
                }

                // Alternate the first pairing to distribute home advantage a bit.
                if (matchIndex == 0 && round % 2 == 0)
                {
                    (homeTeam, awayTeam) = (awayTeam, homeTeam);
                }

                fixtures.Add(new Fixture
                {
                    RoundNumber = round,
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam
                });
            }

            RotateTeams(rotatingTeams);
        }

        return fixtures;
    }

    private static void RotateTeams(List<Team> teams)
    {
        var lastTeam = teams[^1];
        teams.RemoveAt(teams.Count - 1);
        teams.Insert(1, lastTeam);
    }
}
