using System.Text.Json;
using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class LeagueDataService
{
    public const string DefaultLeagueId = "premier-league";
    private const string LeagueIndexFileName = "leagues-index.json";

    private readonly PlayerStatMappingService _playerStatMappingService;

    public LeagueDataService()
        : this(new PlayerStatMappingService())
    {
    }

    public LeagueDataService(PlayerStatMappingService playerStatMappingService)
    {
        _playerStatMappingService = playerStatMappingService;
    }

    public IReadOnlyList<LeagueDefinition> LoadLeagueDefinitions(bool includePlaceholders = true)
    {
        var index = ReadLeagueIndex();
        return index.Leagues
            .Where(league => includePlaceholders || league.IsAvailable)
            .ToList();
    }

    public IReadOnlyList<LeagueDefinition> LoadSquadSourceDefinitions()
    {
        var index = ReadLeagueIndex();
        return index.Leagues
            .Where(league => !string.IsNullOrWhiteSpace(league.SquadFile))
            .ToList();
    }

    public LeagueDefinition GetLeagueDefinition(string? leagueId)
    {
        var normalizedLeagueId = NormalizeLeagueId(leagueId);
        var definition = LoadLeagueDefinitions()
            .FirstOrDefault(league => string.Equals(league.LeagueId, normalizedLeagueId, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            throw new InvalidOperationException($"League '{normalizedLeagueId}' is not configured.");
        }

        return definition;
    }

    public List<Team> LoadTeams(string? leagueId)
    {
        var definition = GetLeagueDefinition(leagueId);
        if (!definition.IsAvailable)
        {
            throw new InvalidOperationException($"{definition.Name} is not available yet.");
        }

        return LoadTeams(definition);
    }

    public List<Team> LoadTeams(LeagueDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.SquadFile))
        {
            throw new InvalidOperationException($"{definition.Name} does not have a configured squad file.");
        }

        var squadFilePath = Path.Combine(GetDataFolder(), definition.SquadFile);
        var squadsFile = ReadJsonFile<LeagueSquadsFile>(squadFilePath);

        return squadsFile.Teams
            .Where(HasPlayableSquad)
            .Select(CreateTeam)
            .ToList();
    }

    private static LeagueDataIndex ReadLeagueIndex()
    {
        var indexPath = Path.Combine(GetDataFolder(), LeagueIndexFileName);
        if (File.Exists(indexPath))
        {
            return ReadJsonFile<LeagueDataIndex>(indexPath);
        }

        return new LeagueDataIndex
        {
            ActiveSeason = "2025-26",
            Leagues =
            [
                new LeagueDefinition
                {
                    LeagueId = DefaultLeagueId,
                    Name = GameSessionService.PremierLeagueName,
                    ShortName = "Premier League",
                    Country = "England",
                    Season = "2025-26",
                    SquadFile = "premier-league-2025-26-squads.json",
                    LogoPath = "/Assets/Leagues/premier-league.png",
                    IsAvailable = true
                }
            ]
        };
    }

    private Team CreateTeam(TeamSquadRecord teamRecord)
    {
        var starters = teamRecord.StartingXI
            .Select(player => _playerStatMappingService.MapToPlayer(player, isStarter: true))
            .ToList();

        var substitutes = teamRecord.Substitutes
            .Select(player => _playerStatMappingService.MapToPlayer(player, isStarter: false))
            .ToList();

        ValidateSquad(teamRecord.Name, starters, substitutes);
        var venue = TeamVenueService.GetVenue(teamRecord.Name, teamRecord.Venue, teamRecord.StadiumName);

        return new Team
        {
            Name = teamRecord.Name,
            Venue = venue.Venue,
            StadiumName = venue.StadiumName,
            Formation = string.IsNullOrWhiteSpace(teamRecord.Formation) ? "4-3-3" : teamRecord.Formation,
            Players = starters,
            Substitutes = substitutes
        };
    }

    private static bool HasPlayableSquad(TeamSquadRecord teamRecord)
    {
        return teamRecord.StartingXI.Count == 11 &&
            teamRecord.Substitutes.Count is >= 7 and <= 12 &&
            teamRecord.StartingXI.Any(player => IsGoalkeeper(player.Position)) &&
            teamRecord.Substitutes.Any(player => IsGoalkeeper(player.Position));
    }

    private static void ValidateSquad(string teamName, List<Player> starters, List<Player> substitutes)
    {
        if (starters.Count != 11)
        {
            throw new InvalidOperationException($"{teamName} must have exactly 11 starters in the JSON data.");
        }

        if (substitutes.Count is < 7 or > 12)
        {
            throw new InvalidOperationException($"{teamName} must have between 7 and 12 substitutes in the JSON data.");
        }

        if (starters.All(player => player.Position != Position.Goalkeeper))
        {
            throw new InvalidOperationException($"{teamName} must have a starting goalkeeper in the JSON data.");
        }

        if (substitutes.All(player => player.Position != Position.Goalkeeper))
        {
            throw new InvalidOperationException($"{teamName} must have a substitute goalkeeper in the JSON data.");
        }
    }

    private static bool IsGoalkeeper(string position)
    {
        return position.Trim().Equals("GK", StringComparison.OrdinalIgnoreCase) ||
            position.Trim().Equals("Goalkeeper", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLeagueId(string? leagueId)
    {
        return string.IsNullOrWhiteSpace(leagueId)
            ? DefaultLeagueId
            : leagueId.Trim().ToLowerInvariant();
    }

    private static string GetDataFolder()
    {
        return Path.Combine(AppContext.BaseDirectory, "Data", "Json");
    }

    private static T ReadJsonFile<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The league JSON data file could not be found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<T>(json, options)
            ?? throw new InvalidOperationException($"The JSON data file is empty or invalid: {filePath}");
    }
}
