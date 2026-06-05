namespace FootballSimulation.Services;

public static class FormationSlotService
{
    public static IReadOnlyList<string> GetSlots(string formation)
    {
        return FormationCatalogService.NormalizeFormationName(formation) switch
        {
            "4-3-3 Attack" => ["GK", "LB", "CB", "CB", "RB", "CM", "CM", "CAM", "LW", "ST", "RW"],
            "4-3-3 Holding" => ["GK", "LB", "CB", "CB", "RB", "CDM", "CM", "CM", "LW", "ST", "RW"],
            "4-3-3 Flat" => ["GK", "LB", "CB", "CB", "RB", "CM", "CM", "CM", "LW", "ST", "RW"],
            "4-2-3-1 Wide" => ["GK", "LB", "CB", "CB", "RB", "CDM", "CDM", "LW", "CAM", "RW", "ST"],
            "4-2-3-1 Narrow" => ["GK", "LB", "CB", "CB", "RB", "CDM", "CDM", "CAM", "CAM", "CAM", "ST"],
            "4-4-2" => ["GK", "LB", "CB", "CB", "RB", "CM", "CM", "CM", "CM", "ST", "ST"],
            "4-2-4" => ["GK", "LB", "CB", "CB", "RB", "CM", "CM", "LW", "ST", "ST", "RW"],
            "3-5-2" => ["GK", "CB", "CB", "CB", "LWB", "CDM", "CM", "CDM", "RWB", "ST", "ST"],
            "3-5-2 Attack" => ["GK", "CB", "CB", "CB", "LWB", "CM", "CAM", "CM", "RWB", "ST", "ST"],
            "3-4-3" => ["GK", "CB", "CB", "CB", "LM", "CM", "CM", "RM", "LW", "ST", "RW"],
            "4-1-2-1-2 Narrow" => ["GK", "LB", "CB", "CB", "RB", "CDM", "CM", "CM", "CAM", "ST", "ST"],
            "4-2-2-2" => ["GK", "LB", "CB", "CB", "RB", "CDM", "CDM", "CAM", "CAM", "ST", "ST"],
            "4-1-4-1" => ["GK", "LB", "CB", "CB", "RB", "CDM", "LM", "CM", "CM", "RM", "ST"],
            "5-2-3" => ["GK", "LWB", "CB", "CB", "CB", "RWB", "CM", "CM", "LW", "ST", "RW"],
            "4-5-1" => ["GK", "LB", "CB", "CB", "RB", "LM", "CM", "CDM", "CM", "RM", "ST"],
            "5-3-2" => ["GK", "LWB", "CB", "CB", "CB", "RWB", "CM", "CM", "CM", "ST", "ST"],
            "5-4-1" => ["GK", "LWB", "CB", "CB", "CB", "RWB", "LM", "CM", "CM", "RM", "ST"],
            "5-2-2-1" => ["GK", "LWB", "CB", "CB", "CB", "RWB", "CDM", "CDM", "CAM", "CAM", "ST"],
            "4-1-4-1 Defensive" => ["GK", "LB", "CB", "CB", "RB", "CDM", "LM", "CM", "CM", "RM", "ST"],
            "3-4-2-1" => ["GK", "CB", "CB", "CB", "LM", "CM", "CM", "RM", "CAM", "CAM", "ST"],
            _ => ["GK", "LB", "CB", "CB", "RB", "CDM", "CM", "CM", "LW", "ST", "RW"]
        };
    }
}
