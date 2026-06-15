using FootballSimulation.Models;
using FootballSimulation.Wpf.Models;

namespace FootballSimulation.Wpf.State;

public class GameFlowState
{
    public string SelectedLeagueId { get; set; } = "premier-league";
    public LeagueDefinition? SelectedLeagueDefinition { get; set; }
    public List<Team> Teams { get; set; } = [];
    public Team? SelectedTeam { get; set; }
    public League? League { get; set; }
    public TransferMarketState? TransferMarket { get; set; }
    public Fixture? CurrentFixture { get; set; }
    public Match? CurrentMatch { get; set; }
    public int? CurrentSaveSlotNumber { get; set; }
    public MatchSpeed CurrentMatchSpeed { get; set; } = MatchSpeed.Medium;
    public bool IsCompactLiveMatchView { get; set; }
    public LiveMatchSegment CurrentLiveMatchSegment { get; set; } = LiveMatchSegment.FirstHalf;
    public Queue<TrophyCelebrationEvent> TrophyCelebrationQueue { get; } = new();
}

public enum LiveMatchSegment
{
    FirstHalf,
    SecondHalf,
    ExtraTimeFirstHalf,
    ExtraTimeSecondHalf
}

public enum MatchSetupMode
{
    Halftime,
    ExtraTimeSetup,
    ExtraTimeHalftime
}
