namespace FootballSimulation.Services;

public static class FormationSlotService
{
    public static IReadOnlyList<string> GetSlots(string formation)
    {
        return formation switch
        {
            "4-2-3-1" => ["GK", "LB", "CB", "CB", "RB", "CDM", "CDM", "LW", "RW", "CAM", "ST"],
            "4-4-2" => ["GK", "LB", "CB", "CB", "RB", "CM", "CM", "CM", "CM", "ST", "ST"],
            "3-5-2" => ["GK", "CB", "CB", "CB", "LB", "CM", "CDM", "CM", "RB", "ST", "ST"],
            _ => ["GK", "LB", "CB", "CB", "RB", "CM", "CAM", "CM", "LW", "ST", "RW"]
        };
    }
}
