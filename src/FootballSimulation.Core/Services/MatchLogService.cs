using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class MatchLogService
{
    private readonly List<MatchEvent> _events = [];

    public void AddEvent(MatchEvent matchEvent)
    {
        _events.Add(matchEvent);
    }

    public void AddEvents(IEnumerable<MatchEvent> matchEvents)
    {
        _events.AddRange(matchEvents);
    }

    public List<MatchEvent> GetEvents()
    {
        return [.. _events];
    }
}
