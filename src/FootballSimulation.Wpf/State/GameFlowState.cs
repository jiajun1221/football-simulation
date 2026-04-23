using FootballSimulation.Models;
using FootballSimulation.Wpf.Models;

namespace FootballSimulation.Wpf.State;

public class GameFlowState
{
    public List<Team> Teams { get; set; } = [];
    public Team? SelectedTeam { get; set; }
    public League? League { get; set; }
    public Fixture? CurrentFixture { get; set; }
    public Match? CurrentMatch { get; set; }
    public MatchSpeed CurrentMatchSpeed { get; set; } = MatchSpeed.Medium;
}
