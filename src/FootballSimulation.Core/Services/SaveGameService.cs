using System.Text.Json;
using System.Text.Json.Serialization;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SaveGameService
{
    public const int CurrentSaveVersion = 2;
    public const int MaxSaveSlots = 3;

    private const string SaveFolderName = "WPFFootballSimulator";
    private const string SaveSubfolderName = "Saves";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _saveDirectory;

    public SaveGameService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            SaveFolderName,
            SaveSubfolderName))
    {
    }

    public SaveGameService(string saveDirectory)
    {
        _saveDirectory = saveDirectory;
    }

    public void SaveGame(int slotNumber, SaveGameData data)
    {
        ValidateSlotNumber(slotNumber);
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(_saveDirectory);

        data.SaveVersion = CurrentSaveVersion;
        data.SavedAt = DateTime.Now;
        data.MatchHistory = CreateMatchHistory(data.Fixtures);
        RehydrateReferences(data);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(GetSlotPath(slotNumber), json);
    }

    public SaveGameData? LoadGame(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);

        var slotPath = GetSlotPath(slotNumber);
        if (!File.Exists(slotPath))
        {
            return null;
        }

        var data = ReadSaveData(slotPath);
        ValidateSaveVersion(data);
        RehydrateReferences(data);
        return data;
    }

    public void DeleteSave(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);

        var slotPath = GetSlotPath(slotNumber);
        if (File.Exists(slotPath))
        {
            File.Delete(slotPath);
        }
    }

    public List<SaveGameSlotInfo> GetSaveSlots()
    {
        return Enumerable.Range(1, MaxSaveSlots)
            .Select(GetSaveSlotInfo)
            .ToList();
    }

    public static SaveGameData CreateSaveData(League league, Team selectedTeam)
    {
        return CreateSaveData(league, selectedTeam, transferMarketState: null);
    }

    public static SaveGameData CreateSaveData(League league, Team selectedTeam, TransferMarketState? transferMarketState)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(selectedTeam);
        if (transferMarketState is not null)
        {
            new TransferMarketService().BindActiveLeague(transferMarketState, league);
        }

        return new SaveGameData
        {
            SaveVersion = CurrentSaveVersion,
            SavedAt = DateTime.Now,
            LeagueId = string.IsNullOrWhiteSpace(league.LeagueId) ? LeagueDataService.DefaultLeagueId : league.LeagueId,
            SelectedClubName = selectedTeam.Name,
            CurrentRound = GetCurrentRound(league, selectedTeam),
            LeagueState = new LeagueState
            {
                LeagueId = string.IsNullOrWhiteSpace(league.LeagueId) ? LeagueDataService.DefaultLeagueId : league.LeagueId,
                Name = league.Name,
                Season = league.Season,
                Table = league.Table
            },
            Teams = league.Teams,
            Fixtures = league.Fixtures,
            MatchHistory = CreateMatchHistory(league.Fixtures),
            PlayerStats = league.PlayerStats.Count > 0
                ? league.PlayerStats
                : new PlayerSeasonStatsService().RebuildSeasonStats(league),
            ClubMatchSetups = CreateClubMatchSetups(league.Teams),
            TransferMarketState = transferMarketState ?? new TransferMarketState()
        };
    }

    public static League CreateLeague(SaveGameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        RehydrateReferences(data);

        return new League
        {
            LeagueId = string.IsNullOrWhiteSpace(data.LeagueState.LeagueId)
                ? string.IsNullOrWhiteSpace(data.LeagueId) ? LeagueDataService.DefaultLeagueId : data.LeagueId
                : data.LeagueState.LeagueId,
            Name = string.IsNullOrWhiteSpace(data.LeagueState.Name)
                ? GameSessionService.PremierLeagueName
                : data.LeagueState.Name,
            Season = data.LeagueState.Season,
            Teams = data.Teams,
            Fixtures = data.Fixtures,
            Table = data.LeagueState.Table,
            PlayerStats = data.PlayerStats.Count > 0
                ? data.PlayerStats
                : new PlayerSeasonStatsService().RebuildSeasonStats(new League
                {
                    LeagueId = string.IsNullOrWhiteSpace(data.LeagueState.LeagueId)
                        ? string.IsNullOrWhiteSpace(data.LeagueId) ? LeagueDataService.DefaultLeagueId : data.LeagueId
                        : data.LeagueState.LeagueId,
                    Name = string.IsNullOrWhiteSpace(data.LeagueState.Name)
                        ? GameSessionService.PremierLeagueName
                        : data.LeagueState.Name,
                    Season = data.LeagueState.Season,
                    Teams = data.Teams,
                    Fixtures = data.Fixtures,
                    Table = data.LeagueState.Table
                })
        };
    }

    public string GetSlotPath(int slotNumber)
    {
        ValidateSlotNumber(slotNumber);
        return Path.Combine(_saveDirectory, $"save_slot_{slotNumber}.json");
    }

    private SaveGameSlotInfo GetSaveSlotInfo(int slotNumber)
    {
        var slotPath = GetSlotPath(slotNumber);
        if (!File.Exists(slotPath))
        {
            return new SaveGameSlotInfo
            {
                SlotNumber = slotNumber,
                IsEmpty = true,
                FilePath = slotPath
            };
        }

        try
        {
            var data = ReadSaveData(slotPath);
            ValidateSaveVersion(data);

            var tableEntry = data.LeagueState.Table
                .Select((entry, index) => new { Entry = entry, Position = index + 1 })
                .FirstOrDefault(item => string.Equals(
                    item.Entry.TeamName,
                    data.SelectedClubName,
                    StringComparison.OrdinalIgnoreCase));

            return new SaveGameSlotInfo
            {
                SlotNumber = slotNumber,
                FilePath = slotPath,
                SaveVersion = data.SaveVersion,
                SavedAt = data.SavedAt,
                LeagueId = string.IsNullOrWhiteSpace(data.LeagueState.LeagueId)
                    ? data.LeagueId
                    : data.LeagueState.LeagueId,
                LeagueName = data.LeagueState.Name,
                SelectedClubName = data.SelectedClubName,
                CurrentRound = data.CurrentRound,
                LeaguePosition = tableEntry?.Position,
                Points = tableEntry?.Entry.Points
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException)
        {
            return new SaveGameSlotInfo
            {
                SlotNumber = slotNumber,
                FilePath = slotPath,
                IsCorrupted = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static SaveGameData ReadSaveData(string slotPath)
    {
        var json = File.ReadAllText(slotPath);
        var data = JsonSerializer.Deserialize<SaveGameData>(json, JsonOptions);
        return data ?? throw new InvalidDataException("The save file is empty or invalid.");
    }

    private static void ValidateSaveVersion(SaveGameData data)
    {
        if (data.SaveVersion is < 1 or > CurrentSaveVersion)
        {
            throw new InvalidDataException($"Unsupported save version {data.SaveVersion}.");
        }
    }

    private static void ValidateSlotNumber(int slotNumber)
    {
        if (slotNumber is < 1 or > MaxSaveSlots)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                $"Save slot must be between 1 and {MaxSaveSlots}.");
        }
    }

    private static int GetCurrentRound(League league, Team selectedTeam)
    {
        var nextFixture = league.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .FirstOrDefault();

        if (nextFixture is not null)
        {
            return nextFixture.RoundNumber;
        }

        return league.Fixtures.Count == 0
            ? 1
            : league.Fixtures.Max(fixture => fixture.RoundNumber);
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return string.Equals(fixture.HomeTeam.Name, team.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fixture.AwayTeam.Name, team.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static List<Match> CreateMatchHistory(IEnumerable<Fixture> fixtures)
    {
        return fixtures
            .Where(fixture => fixture.IsPlayed && fixture.Result is not null)
            .OrderBy(fixture => fixture.RoundNumber)
            .Select(fixture => fixture.Result!)
            .ToList();
    }

    private static List<ClubMatchSetup> CreateClubMatchSetups(IEnumerable<Team> teams)
    {
        return teams
            .Select(ClubMatchSetupService.Capture)
            .ToList();
    }

    private static void RehydrateReferences(SaveGameData data)
    {
        BackfillPlayerDataFromLeagueData(data);
        foreach (var team in data.Teams)
        {
            foreach (var player in team.Players.Concat(team.Substitutes))
            {
                PlayerAttributeService.ApplyMissingAttributes(player);
            }

            _ = LineupValidationService.RepairGoalkeeperSlot(team);
        }

        var teamsByName = data.Teams
            .GroupBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var setup in data.ClubMatchSetups)
        {
            if (teamsByName.TryGetValue(setup.ClubName, out var team))
            {
                ClubMatchSetupService.Apply(team, setup);
                _ = LineupValidationService.RepairGoalkeeperSlot(team);
            }
        }

        foreach (var fixture in data.Fixtures)
        {
            fixture.HomeTeam = GetCanonicalTeam(teamsByName, fixture.HomeTeam);
            fixture.AwayTeam = GetCanonicalTeam(teamsByName, fixture.AwayTeam);

            if (fixture.Result is not null)
            {
                RehydrateMatchReferences(fixture.Result, teamsByName);
            }
        }

        EnsureCompleteDoubleRoundRobinFixtures(data, teamsByName);

        foreach (var match in data.MatchHistory)
        {
            RehydrateMatchReferences(match, teamsByName);
        }

        RehydrateTransferMarketReferences(data);
        data.MatchHistory = CreateMatchHistory(data.Fixtures);
    }

    private static void EnsureCompleteDoubleRoundRobinFixtures(
        SaveGameData data,
        IReadOnlyDictionary<string, Team> teamsByName)
    {
        if (data.Teams.Count < 2)
        {
            return;
        }

        var expectedRoundCount = (data.Teams.Count - 1) * 2;
        var expectedFixtureCount = data.Teams.Count * (data.Teams.Count - 1);
        if (data.Fixtures.Count >= expectedFixtureCount &&
            data.Fixtures.Select(fixture => fixture.RoundNumber).DefaultIfEmpty(0).Max() >= expectedRoundCount)
        {
            return;
        }

        var existingKeys = data.Fixtures
            .Select(CreateFixtureKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedFixtures = new LeagueScheduleService().GenerateFixtures(data.Teams);
        foreach (var generatedFixture in generatedFixtures)
        {
            generatedFixture.HomeTeam = GetCanonicalTeam(teamsByName, generatedFixture.HomeTeam);
            generatedFixture.AwayTeam = GetCanonicalTeam(teamsByName, generatedFixture.AwayTeam);
            if (!existingKeys.Add(CreateFixtureKey(generatedFixture)))
            {
                continue;
            }

            data.Fixtures.Add(generatedFixture);
        }

        data.Fixtures = data.Fixtures
            .OrderBy(fixture => fixture.RoundNumber)
            .ThenBy(fixture => fixture.HomeTeam.Name)
            .ThenBy(fixture => fixture.AwayTeam.Name)
            .ToList();
    }

    private static string CreateFixtureKey(Fixture fixture)
    {
        return $"{fixture.RoundNumber}|{fixture.HomeTeam.Name}|{fixture.AwayTeam.Name}";
    }

    private static void BackfillPlayerDataFromLeagueData(SaveGameData data)
    {
        var leagueId = string.IsNullOrWhiteSpace(data.LeagueState.LeagueId)
            ? string.IsNullOrWhiteSpace(data.LeagueId) ? LeagueDataService.DefaultLeagueId : data.LeagueId
            : data.LeagueState.LeagueId;

        List<Team> sourceTeams;
        try
        {
            sourceTeams = new LeagueDataService().LoadTeams(leagueId);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var sourceRows = sourceTeams
            .SelectMany(team => team.Players.Concat(team.Substitutes)
                .Select(player => new
                {
                    TeamName = team.Name,
                    Player = player
                }))
            .ToList();

        foreach (var team in data.Teams)
        {
            foreach (var player in team.Players.Concat(team.Substitutes))
            {
                var sourcePlayer = sourceRows.FirstOrDefault(row =>
                        row.TeamName.Equals(team.Name, StringComparison.OrdinalIgnoreCase) &&
                        row.Player.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase) &&
                        row.Player.SquadNumber == player.SquadNumber)?.Player
                    ?? sourceRows.FirstOrDefault(row =>
                        row.TeamName.Equals(team.Name, StringComparison.OrdinalIgnoreCase) &&
                        row.Player.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase))?.Player
                    ?? sourceRows
                        .Where(row => row.Player.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(row => row.Player)
                        .FirstOrDefault();

                player.Age ??= sourcePlayer?.Age ?? EstimateAge(player);
                if (sourcePlayer is null)
                {
                    PlayerContractService.EnsureContract(player, leagueId);
                    if (PlayerNationalityDataService.IsMissingOrDefault(player))
                    {
                        _ = PlayerNationalityDataService.TryApply(player);
                    }

                    continue;
                }

                PlayerContractService.ApplyContractData(
                    player,
                    leagueId,
                    sourcePlayer.ContractEndYear,
                    sourcePlayer.WeeklyWage,
                    sourcePlayer.ReleaseClause,
                    sourcePlayer.ContractStatus);

                if (PlayerNationalityDataService.IsMissingOrDefault(player))
                {
                    player.NationalityCode = sourcePlayer.NationalityCode;
                    player.NationalityName = sourcePlayer.NationalityName;
                    player.Nationality = sourcePlayer.Nationality;
                    player.FlagImagePath = sourcePlayer.FlagImagePath;
                }

                if (PlayerNationalityDataService.IsMissingOrDefault(player))
                {
                    _ = PlayerNationalityDataService.TryApply(player);
                }
            }
        }
    }

    private static int EstimateAge(Player player)
    {
        return player.Position switch
        {
            Position.Goalkeeper => player.OverallRating >= 80 ? 29 : 25,
            Position.Defender => player.OverallRating >= 80 ? 27 : 24,
            Position.Midfielder => player.OverallRating >= 80 ? 26 : 23,
            Position.Forward => player.OverallRating >= 82 ? 25 : 22,
            _ => 24
        };
    }

    private static void RehydrateTransferMarketReferences(SaveGameData data)
    {
        if (data.TransferMarketState.Leagues.Count == 0)
        {
            return;
        }

        var activeLeagueId = string.IsNullOrWhiteSpace(data.LeagueState.LeagueId)
            ? data.LeagueId
            : data.LeagueState.LeagueId;

        foreach (var league in data.TransferMarketState.Leagues)
        {
            if (league.LeagueId.Equals(activeLeagueId, StringComparison.OrdinalIgnoreCase))
            {
                league.Teams = data.Teams;
            }

            foreach (var team in league.Teams)
            {
                _ = LineupValidationService.RepairGoalkeeperSlot(team);
            }
        }

        new TransferMarketService().BindActiveLeague(data.TransferMarketState, new League
        {
            LeagueId = activeLeagueId,
            Name = string.IsNullOrWhiteSpace(data.LeagueState.Name) ? GameSessionService.PremierLeagueName : data.LeagueState.Name,
            Season = data.LeagueState.Season,
            Teams = data.Teams,
            Fixtures = data.Fixtures,
            Table = data.LeagueState.Table,
            PlayerStats = data.PlayerStats
        });
    }

    private static void RehydrateMatchReferences(Match match, IReadOnlyDictionary<string, Team> teamsByName)
    {
        match.HomeTeam = GetCanonicalTeam(teamsByName, match.HomeTeam);
        match.AwayTeam = GetCanonicalTeam(teamsByName, match.AwayTeam);
    }

    private static Team GetCanonicalTeam(IReadOnlyDictionary<string, Team> teamsByName, Team team)
    {
        return teamsByName.TryGetValue(team.Name, out var canonicalTeam)
            ? canonicalTeam
            : team;
    }
}
