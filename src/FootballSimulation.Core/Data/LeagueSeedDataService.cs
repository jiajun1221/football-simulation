using FootballSimulation.Models;

namespace FootballSimulation.Data;

public class LeagueSeedDataService
{
    public List<Team> CreateLeagueTeams()
    {
        return
        [
            CreateTeam(
                "Blue Hawks",
                "4-3-3",
                "Daniel", "Ryan", "Liam", "Mason", "Noah", "Ethan", "Lucas", "Owen", "Jack", "Aiden", "Caleb",
                attackBoost: 2,
                defenseBoost: 1,
                passingBoost: 1,
                staminaBoost: 0,
                finishingBoost: 2),
            CreateTeam(
                "Red Lions",
                "4-4-2",
                "Oliver", "Henry", "Leo", "Jacob", "Nathan", "Isaac", "Adam", "Samuel", "Connor", "Dylan", "Marcus",
                attackBoost: 1,
                defenseBoost: 2,
                passingBoost: 0,
                staminaBoost: 1,
                finishingBoost: 1),
            CreateTeam(
                "Green Falcons",
                "3-5-2",
                "Aaron", "Blake", "Carter", "Declan", "Elliot", "Finn", "Gavin", "Harvey", "Ivan", "Jonah", "Kai",
                attackBoost: 0,
                defenseBoost: 1,
                passingBoost: 3,
                staminaBoost: 2,
                finishingBoost: 0),
            CreateTeam(
                "Golden Bears",
                "4-3-3",
                "Logan", "Miles", "Nolan", "Oscar", "Parker", "Quinn", "Reed", "Sawyer", "Toby", "Uri", "Victor",
                attackBoost: 3,
                defenseBoost: 0,
                passingBoost: 1,
                staminaBoost: 0,
                finishingBoost: 2)
        ];
    }

    private static Team CreateTeam(
        string teamName,
        string formation,
        string goalkeeperName,
        string defenderOne,
        string defenderTwo,
        string defenderThree,
        string defenderFour,
        string midfielderOne,
        string midfielderTwo,
        string midfielderThree,
        string forwardOne,
        string forwardTwo,
        string forwardThree,
        int attackBoost,
        int defenseBoost,
        int passingBoost,
        int staminaBoost,
        int finishingBoost)
    {
        return new Team
        {
            Name = teamName,
            Formation = formation,
            Players =
            [
                DemoPlayerFactory.CreateGoalkeeper($"{goalkeeperName} Ward", 82 + defenseBoost, 48 + passingBoost, 80 + staminaBoost),
                DemoPlayerFactory.CreateDefender($"{defenderOne} Cole", 42 + attackBoost, 78 + defenseBoost, 60 + passingBoost, 76 + staminaBoost, 28 + finishingBoost),
                DemoPlayerFactory.CreateDefender($"{defenderTwo} Foster", 38 + attackBoost, 81 + defenseBoost, 57 + passingBoost, 74 + staminaBoost, 24 + finishingBoost),
                DemoPlayerFactory.CreateDefender($"{defenderThree} Reed", 40 + attackBoost, 79 + defenseBoost, 58 + passingBoost, 77 + staminaBoost, 27 + finishingBoost),
                DemoPlayerFactory.CreateDefender($"{defenderFour} Blake", 37 + attackBoost, 80 + defenseBoost, 55 + passingBoost, 75 + staminaBoost, 22 + finishingBoost),
                DemoPlayerFactory.CreateMidfielder($"{midfielderOne} Price", 67 + attackBoost, 62 + defenseBoost, 79 + passingBoost, 83 + staminaBoost, 64 + finishingBoost),
                DemoPlayerFactory.CreateMidfielder($"{midfielderTwo} Hayes", 71 + attackBoost, 58 + defenseBoost, 82 + passingBoost, 84 + staminaBoost, 67 + finishingBoost),
                DemoPlayerFactory.CreateMidfielder($"{midfielderThree} Brooks", 69 + attackBoost, 60 + defenseBoost, 80 + passingBoost, 82 + staminaBoost, 65 + finishingBoost),
                DemoPlayerFactory.CreateForward($"{forwardOne} Turner", 84 + attackBoost, 35 + defenseBoost, 68 + passingBoost, 79 + staminaBoost, 86 + finishingBoost),
                DemoPlayerFactory.CreateForward($"{forwardTwo} Ross", 86 + attackBoost, 33 + defenseBoost, 70 + passingBoost, 81 + staminaBoost, 88 + finishingBoost),
                DemoPlayerFactory.CreateForward($"{forwardThree} Murphy", 82 + attackBoost, 34 + defenseBoost, 66 + passingBoost, 78 + staminaBoost, 84 + finishingBoost)
            ]
        };
    }
}
