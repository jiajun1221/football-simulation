using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SeasonAwardsServiceTests
{
    [Fact]
    public void CreateAwards_RequiresMeaningfulAppearancesForMajorAwards()
    {
        var league = new League
        {
            Teams =
            [
                new Team
                {
                    Name = "Chelsea",
                    Players =
                    [
                        new Player { PlayerId = "palmer", Name = "Cole Palmer", Age = 23, Position = Position.Midfielder },
                        new Player { PlayerId = "isidor", Name = "Wilson Isidor", Age = 24, Position = Position.Forward },
                        new Player { PlayerId = "young-low", Name = "Young Prospect", Age = 20, Position = Position.Forward }
                    ]
                }
            ]
        };
        var stats = new List<ArchivedPlayerStatRow>
        {
            new()
            {
                PlayerId = "isidor",
                PlayerName = "Wilson Isidor",
                TeamName = "Chelsea",
                Position = Position.Forward,
                ExactPosition = "ST",
                Appearances = 2,
                Goals = 4,
                Assists = 1,
                AverageRating = 10.00
            },
            new()
            {
                PlayerId = "young-low",
                PlayerName = "Young Prospect",
                TeamName = "Chelsea",
                Position = Position.Forward,
                ExactPosition = "ST",
                Appearances = 4,
                Goals = 8,
                Assists = 2,
                AverageRating = 9.50
            },
            new()
            {
                PlayerId = "palmer",
                PlayerName = "Cole Palmer",
                TeamName = "Chelsea",
                Position = Position.Midfielder,
                ExactPosition = "CAM",
                Appearances = 35,
                Goals = 20,
                Assists = 15,
                AverageRating = 8.13
            }
        };

        var awards = new SeasonAwardsService().CreateAwards(league, stats);

        Assert.Equal("Cole Palmer", awards.PlayerOfTheSeason.PlayerName);
        Assert.Equal("Cole Palmer", awards.YoungPlayerOfTheSeason.PlayerName);
    }

    [Fact]
    public void CreateHighlights_UsesCupFinalFixtureWhenCompetitionStateHasNoWinner()
    {
        var chelsea = new Team { Name = "Chelsea" };
        var arsenal = new Team { Name = "Arsenal" };
        var liverpool = new Team { Name = "Liverpool" };
        var league = new League
        {
            Teams = [chelsea, arsenal, liverpool],
            Table =
            [
                new LeagueTableEntry { TeamName = "Chelsea", Played = 38, Wins = 22, Draws = 7, Losses = 9, Points = 73 },
                new LeagueTableEntry { TeamName = "Arsenal", Played = 38, Wins = 20, Draws = 9, Losses = 9, Points = 69 },
                new LeagueTableEntry { TeamName = "Liverpool", Played = 38, Wins = 19, Draws = 10, Losses = 9, Points = 67 }
            ],
            CompetitionStates =
            [
                new SeasonCompetitionState { Competition = CompetitionType.FACup, Name = "FA Cup" }
            ],
            Fixtures =
            [
                new Fixture
                {
                    Competition = CompetitionType.FACup,
                    RoundName = "Third Round",
                    CalendarRound = 42,
                    IsKnockout = true,
                    IsPlayed = true,
                    HomeTeam = chelsea,
                    AwayTeam = arsenal,
                    WinningTeamName = "Arsenal",
                    LosingTeamName = "Chelsea"
                },
                new Fixture
                {
                    Competition = CompetitionType.FACup,
                    RoundName = "Final",
                    KnockoutRoundKey = "Final",
                    CalendarRound = 79,
                    IsKnockout = true,
                    IsPlayed = true,
                    HomeTeam = arsenal,
                    AwayTeam = liverpool,
                    WinningTeamName = "Arsenal",
                    LosingTeamName = "Liverpool"
                }
            ]
        };

        var highlights = new SeasonAwardsService().CreateHighlights(league, chelsea, league.Table, []);

        var faCup = Assert.Single(highlights, highlight => highlight.Title == "FA Cup");
        Assert.Equal("Arsenal won the competition.", faCup.PrimaryText);
        Assert.Contains("Runner-up: Liverpool.", faCup.SecondaryText);
        Assert.Contains("Eliminated in the Third Round.", faCup.SecondaryText);
    }
}
