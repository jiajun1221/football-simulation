namespace FootballSimulation.Data.JsonModels;

public class PlayerNationalityDataFile
{
    public List<PlayerNationalityRecord> Players { get; set; } = [];
}

public class PlayerNationalityRecord
{
    public string Name { get; set; } = string.Empty;
    public string NationalityCode { get; set; } = string.Empty;
    public string NationalityName { get; set; } = string.Empty;
    public string FlagImagePath { get; set; } = string.Empty;
}
