using System.Text.Json;
using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PremierLeagueDataService
{
    private const string SquadIndexFileName = "squad-data-index.json";
    private const string DefaultTeamsFileName = "premier-league-2025-26-teams.json";
    private const string DefaultPlayersFileName = "premier-league-2025-26-players.json";

    private readonly PlayerStatMappingService _playerStatMappingService;

    public PremierLeagueDataService()
        : this(new PlayerStatMappingService())
    {
    }

    public PremierLeagueDataService(PlayerStatMappingService playerStatMappingService)
    {
        _playerStatMappingService = playerStatMappingService;
    }

    public List<Team> LoadTeams()
    {
        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Json");
        var squadIndexPath = Path.Combine(dataFolder, SquadIndexFileName);

        if (File.Exists(squadIndexPath))
        {
            var squadIndex = ReadJsonFile<SquadDataIndex>(squadIndexPath);
            if (!string.IsNullOrWhiteSpace(squadIndex.ActiveSquadFile))
            {
                return LoadTeamsFromSquadFile(Path.Combine(dataFolder, squadIndex.ActiveSquadFile));
            }
        }

        return LoadTeams(
            Path.Combine(dataFolder, DefaultTeamsFileName),
            Path.Combine(dataFolder, DefaultPlayersFileName));
    }

    public List<Team> LoadTeams(string teamsFilePath, string playersFilePath)
    {
        var teamsFile = ReadJsonFile<PremierLeagueTeamsFile>(teamsFilePath);
        var playersFile = ReadJsonFile<PremierLeaguePlayersFile>(playersFilePath);

        return teamsFile.Teams
            .Where(teamRecord => HasPlayableSquad(teamRecord, playersFile.Players))
            .Select(teamRecord => CreateTeam(teamRecord, playersFile.Players))
            .ToList();
    }

    private static bool HasPlayableSquad(TeamDataRecord teamRecord, IEnumerable<PlayerDataRecord> playerRecords)
    {
        var players = playerRecords
            .Where(player => string.Equals(player.TeamId, teamRecord.Id, StringComparison.OrdinalIgnoreCase))
            .Where(player => player.IsStarter)
            .ToList();

        return players.Count == 11 &&
            players.Any(player => string.Equals(player.Position, "Goalkeeper", StringComparison.OrdinalIgnoreCase));
    }

    public List<Team> LoadTeamsFromSquadFile(string squadFilePath)
    {
        var squadsFile = ReadJsonFile<PremierLeagueSquadsFile>(squadFilePath);

        return squadsFile.Teams
            .Where(HasPlayableSquad)
            .Select(CreateTeam)
            .ToList();
    }

    private static bool HasPlayableSquad(TeamSquadRecord teamRecord)
    {
        return teamRecord.StartingXI.Count == 11 &&
            teamRecord.Substitutes.Count is >= 7 and <= 12 &&
            teamRecord.StartingXI.Any(player => IsGoalkeeper(player.Position)) &&
            teamRecord.Substitutes.Any(player => IsGoalkeeper(player.Position));
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

        return new Team
        {
            Name = teamRecord.Name,
            Formation = teamRecord.Formation,
            Players = starters,
            Substitutes = substitutes
        };
    }

    private Team CreateTeam(TeamDataRecord teamRecord, IEnumerable<PlayerDataRecord> playerRecords)
    {
        var teamPlayerRecords = playerRecords
            .Where(player => string.Equals(player.TeamId, teamRecord.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(player => player.SquadNumber)
            .ToList();

        var teamPlayers = teamPlayerRecords
            .Where(player => player.IsStarter)
            .Select(_playerStatMappingService.MapToPlayer)
            .ToList();

        var substitutes = teamPlayerRecords
            .Where(player => !player.IsStarter)
            .Select(_playerStatMappingService.MapToPlayer)
            .ToList();

        if (teamPlayers.Count != 11)
        {
            throw new InvalidOperationException($"{teamRecord.Name} must have exactly 11 starters in the JSON data.");
        }

        if (teamPlayers.All(player => player.Position != Position.Goalkeeper))
        {
            throw new InvalidOperationException($"{teamRecord.Name} must have a goalkeeper in the JSON data.");
        }

        return new Team
        {
            Name = teamRecord.Name,
            Formation = teamRecord.Formation,
            Players = teamPlayers,
            Substitutes = substitutes
        };
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

    private static T ReadJsonFile<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The Premier League JSON data file could not be found.", filePath);
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
