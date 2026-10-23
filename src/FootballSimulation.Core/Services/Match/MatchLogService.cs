using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class MatchLogService
{
    private readonly List<MatchEvent> _events = [];
    public Action<MatchEvent>? EventAdded { get; set; }

    public void AddEvent(MatchEvent matchEvent)
    {
        _events.Add(matchEvent);
        EventAdded?.Invoke(matchEvent);
    }

    public void AddEvents(IEnumerable<MatchEvent> matchEvents)
    {
        var additions = matchEvents.ToList();
        _events.AddRange(additions);
        if (EventAdded is not null)
        {
            foreach (var matchEvent in additions)
            {
                EventAdded(matchEvent);
            }
        }
    }

    public void ReplaceLastEvent(MatchEvent matchEvent)
    {
        if (_events.Count == 0)
        {
            AddEvent(matchEvent);
            return;
        }

        _events[^1] = matchEvent;
    }

    public List<MatchEvent> GetEvents()
    {
        return [.. _events];
    }
}
