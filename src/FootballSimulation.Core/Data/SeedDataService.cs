using FootballSimulation.Models;

namespace FootballSimulation.Data;

public class SeedDataService
{
    public Team CreateHomeTeam()
    {
        return new Team
        {
            Name = "Blue Hawks",
            Formation = "4-3-3",
            Players =
            [
                DemoPlayerFactory.CreateGoalkeeper("Daniel Ward", defense: 82, passing: 48, stamina: 80),
                DemoPlayerFactory.CreateDefender("Ryan Cole", attack: 42, defense: 78, passing: 60, stamina: 76, finishing: 28),
                DemoPlayerFactory.CreateDefender("Liam Foster", attack: 38, defense: 81, passing: 57, stamina: 74, finishing: 24),
                DemoPlayerFactory.CreateDefender("Mason Reed", attack: 40, defense: 79, passing: 58, stamina: 77, finishing: 27),
                DemoPlayerFactory.CreateDefender("Noah Blake", attack: 37, defense: 80, passing: 55, stamina: 75, finishing: 22),
                DemoPlayerFactory.CreateMidfielder("Ethan Price", attack: 67, defense: 62, passing: 79, stamina: 83, finishing: 64),
                DemoPlayerFactory.CreateMidfielder("Lucas Hayes", attack: 71, defense: 58, passing: 82, stamina: 84, finishing: 67),
                DemoPlayerFactory.CreateMidfielder("Owen Brooks", attack: 69, defense: 60, passing: 80, stamina: 82, finishing: 65),
                DemoPlayerFactory.CreateForward("Jack Turner", attack: 84, defense: 35, passing: 68, stamina: 79, finishing: 86),
                DemoPlayerFactory.CreateForward("Aiden Ross", attack: 86, defense: 33, passing: 70, stamina: 81, finishing: 88),
                DemoPlayerFactory.CreateForward("Caleb Murphy", attack: 82, defense: 34, passing: 66, stamina: 78, finishing: 84)
            ]
        };
    }

    public Team CreateAwayTeam()
    {
        return new Team
        {
            Name = "Red Lions",
            Formation = "4-4-2",
            Players =
            [
                DemoPlayerFactory.CreateGoalkeeper("Oliver Stone", defense: 84, passing: 50, stamina: 79),
                DemoPlayerFactory.CreateDefender("Henry Miles", attack: 41, defense: 80, passing: 59, stamina: 75, finishing: 27),
                DemoPlayerFactory.CreateDefender("Leo Bennett", attack: 39, defense: 82, passing: 56, stamina: 74, finishing: 25),
                DemoPlayerFactory.CreateDefender("Jacob Shaw", attack: 43, defense: 77, passing: 61, stamina: 76, finishing: 29),
                DemoPlayerFactory.CreateDefender("Nathan Perry", attack: 36, defense: 79, passing: 54, stamina: 75, finishing: 23),
                DemoPlayerFactory.CreateMidfielder("Isaac Long", attack: 68, defense: 61, passing: 78, stamina: 82, finishing: 66),
                DemoPlayerFactory.CreateMidfielder("Adam West", attack: 70, defense: 57, passing: 81, stamina: 83, finishing: 68),
                DemoPlayerFactory.CreateForward("Connor Wade", attack: 83, defense: 32, passing: 67, stamina: 80, finishing: 85),
                DemoPlayerFactory.CreateForward("Dylan Fox", attack: 85, defense: 31, passing: 69, stamina: 79, finishing: 87),
                DemoPlayerFactory.CreateForward("Tyler Grant", attack: 81, defense: 36, passing: 65, stamina: 77, finishing: 83),
                DemoPlayerFactory.CreateForward("Marcus Hale", attack: 79, defense: 34, passing: 64, stamina: 76, finishing: 82)
            ]
        };
    }

    public (Team HomeTeam, Team AwayTeam) CreateDemoTeams()
    {
        return (CreateHomeTeam(), CreateAwayTeam());
    }
}
