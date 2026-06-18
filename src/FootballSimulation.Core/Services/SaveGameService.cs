using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SaveGameService
{
    public const int CurrentSaveVersion = 7;
    public const int MaxSaveSlots = 3;

    private const string SaveFolderName = "WPFFootballSimulator";
    private const string SaveSubfolderName = "Saves";

    private static readonly KnownPositionCorrection[] KnownPositionCorrections =
    [
        new("vitinha", "parissaintgermain", Position.Midfielder, "CM", ["CDM", "CAM"]),
        new("brunofernandes", "manchesterunited", Position.Midfielder, "CAM", ["CM"]),
        new("rodri", "manchestercity", Position.Midfielder, "CDM", ["CM", "CB"]),
        new("martinodegaard", "arsenal", Position.Midfielder, "CAM", ["CM"]),
        new("joaopedro", "chelsea", Position.Forward, "CF", ["ST", "CAM", "LW"]),
        new("kaihavertz", "arsenal", Position.Forward, "CF", ["ST", "CAM", "LW", "RW"]),
        new("matheuscunha", "manchesterunited", Position.Forward, "CF", ["ST", "CAM", "LW"]),
        new("julianalvarez", "atleticomadrid", Position.Forward, "CF", ["ST", "CAM", "LW", "RW"]),
        new("christophernkunku", "acmilan", Position.Forward, "CF", ["ST", "CAM", "LW", "RW"])
    ];

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
        ApplyKnownPlayerDataCorrections(data);
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
                Table = league.Table,
                IsCompleted = league.IsCompleted,
                HasShownLeagueTrophyCelebration = league.HasShownLeagueTrophyCelebration,
                ShownTrophyCelebrationKeys = league.ShownTrophyCelebrationKeys
            },
            Teams = league.Teams,
            Fixtures = league.Fixtures,
            MatchHistory = CreateMatchHistory(league.Fixtures),
            PlayerStats = league.PlayerStats.Count > 0
                ? league.PlayerStats
                : new PlayerSeasonStatsService().RebuildSeasonStats(league),
            PlayerCompetitionStats = league.PlayerCompetitionStats.Count > 0
                ? league.PlayerCompetitionStats
                : new PlayerSeasonStatsService().RebuildCompetitionStats(league),
            CompetitionStates = league.CompetitionStates,
            YouthAcademies = league.YouthAcademies,
            SeasonHistory = league.SeasonHistory,
            ClubMatchSetups = CreateClubMatchSetups(league.Teams),
            TransferMarketState = transferMarketState ?? new TransferMarketState()
        };
    }

    public static League CreateLeague(SaveGameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        RehydrateReferences(data);

        var league = new League
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
                }),
            PlayerCompetitionStats = data.PlayerCompetitionStats.Count > 0
                ? data.PlayerCompetitionStats
                : new PlayerSeasonStatsService().RebuildCompetitionStats(new League
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
                }),
            CompetitionStates = data.CompetitionStates ?? [],
            YouthAcademies = data.YouthAcademies ?? [],
            SeasonHistory = data.SeasonHistory ?? [],
            IsCompleted = data.LeagueState.IsCompleted,
            HasShownLeagueTrophyCelebration = data.LeagueState.HasShownLeagueTrophyCelebration,
            ShownTrophyCelebrationKeys = data.LeagueState.ShownTrophyCelebrationKeys ?? []
        };

        ApplyKnownPlayerDataCorrections(league);
        return league;
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
                Season = data.LeagueState.Season,
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

    private static void ApplyKnownPlayerDataCorrections(League league)
    {
        var seasonStartYear = TryGetSeasonStartYear(league.Season);
        ApplyKnownPlayerDataCorrections(league.Teams, seasonStartYear);
    }

    private static void ApplyKnownPlayerDataCorrections(SaveGameData data)
    {
        var seasonStartYear = TryGetSeasonStartYear(data.LeagueState.Season);
        ApplyKnownPlayerDataCorrections(data.Teams, seasonStartYear);
        foreach (var league in data.TransferMarketState.Leagues)
        {
            ApplyKnownPlayerDataCorrections(league.Teams, seasonStartYear);
        }

        ApplyKnownPlayerDataCorrections(data.TransferMarketState.FreeAgents, seasonStartYear);
    }

    private static void ApplyKnownPlayerDataCorrections(IEnumerable<Team> teams, int? seasonStartYear)
    {
        foreach (var team in teams)
        {
            foreach (var player in team.Players.Concat(team.Substitutes))
            {
                ApplyKnownPlayerDataCorrections(player, seasonStartYear, team.Name);
            }
        }
    }

    private static void ApplyKnownPlayerDataCorrections(IEnumerable<Player> players, int? seasonStartYear)
    {
        foreach (var player in players)
        {
            ApplyKnownPlayerDataCorrections(player, seasonStartYear, teamName: string.Empty);
        }
    }

    private static void ApplyKnownPlayerDataCorrections(Player player, int? seasonStartYear, string teamName)
    {
        if (IsEstevao(player))
        {
            if (seasonStartYear.HasValue)
            {
                player.Age = Math.Clamp(seasonStartYear.Value - 2007, 16, 45);
            }

            ApplyEstevaoRatingCorrection(player);
        }

        ApplyKnownPositionCorrection(player, teamName);
        RepairMissingSeniorOverallAttributes(player);
        PlayerTraitAssignmentService.EnsureMinimumTraits(player);
    }

    private static void ApplyEstevaoRatingCorrection(Player player)
    {
        player.OverallRating = Math.Max(player.OverallRating, 78);
        player.BaseOverallRating = Math.Max(player.BaseOverallRating, 78);
        player.PotentialOverall = Math.Max(player.PotentialOverall ?? 0, 88);
        player.Position = Position.Forward;
        player.PreferredPosition = "RW";
        player.AssignedPosition = string.IsNullOrWhiteSpace(player.AssignedPosition)
            ? "RW"
            : player.AssignedPosition;
        player.SecondaryPositions = ["LW", "CAM"];
        player.NationalityName = string.IsNullOrWhiteSpace(player.NationalityName) ? "Brazil" : player.NationalityName;
        player.NationalityCode = string.IsNullOrWhiteSpace(player.NationalityCode) ? "BR" : player.NationalityCode;
        player.FlagImagePath = string.IsNullOrWhiteSpace(player.FlagImagePath) ? "/Assets/Flags/brazil.png" : player.FlagImagePath;
        player.Pace = Math.Max(player.Pace, 90);
        player.Shooting = Math.Max(player.Shooting, 74);
        player.Passing = Math.Max(player.Passing, 73);
        player.Dribbling = Math.Max(player.Dribbling, 82);
        player.Defending = Math.Max(player.Defending, 33);
        player.Physical = Math.Max(player.Physical, 57);
        player.Attack = Math.Max(player.Attack, 78);
        player.Defense = Math.Max(player.Defense, 35);
        player.Finishing = Math.Max(player.Finishing, 74);
    }

    private static void RepairMissingSeniorOverallAttributes(Player player)
    {
        if (player.OverallRating < YouthAcademyService.MinimumPromotionOverall)
        {
            return;
        }

        var calculatedOverall = PlayerOverallCalculator.CalculateOverall(player);
        var hasMissingCoreAttributes = player.Attack <= 1 ||
            player.Defense <= 1 ||
            player.Passing <= 1 ||
            player.Finishing <= 1;
        if (!hasMissingCoreAttributes && calculatedOverall >= player.OverallRating - 12)
        {
            return;
        }

        YouthAcademyService.RepairSeniorOverallAttributes(player, player.OverallRating);
    }

    private static void ApplyKnownPositionCorrection(Player player, string teamName)
    {
        var correction = KnownPositionCorrections.FirstOrDefault(item => IsPositionCorrectionMatch(player, teamName, item));
        if (correction is null)
        {
            return;
        }

        var oldPreferredPosition = player.PreferredPosition;
        player.Position = correction.Position;
        player.PreferredPosition = correction.PreferredPosition;
        player.SecondaryPositions = correction.SecondaryPositions
            .Where(position => !position.Equals(correction.PreferredPosition, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(player.AssignedPosition) ||
            player.AssignedPosition.Equals(oldPreferredPosition, StringComparison.OrdinalIgnoreCase))
        {
            player.AssignedPosition = correction.PreferredPosition;
        }
    }

    private static bool IsPositionCorrectionMatch(Player player, string teamName, KnownPositionCorrection correction)
    {
        var playerMatches = player.PlayerId.Contains(correction.PlayerKey, StringComparison.OrdinalIgnoreCase) ||
            NormalizePlayerKey(player.Name).Equals(correction.PlayerKey, StringComparison.OrdinalIgnoreCase);
        if (!playerMatches)
        {
            return false;
        }

        var normalizedTeamName = NormalizePlayerKey(teamName);
        return normalizedTeamName.Equals(correction.TeamKey, StringComparison.OrdinalIgnoreCase) ||
            player.PlayerId.Contains(correction.TeamKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEstevao(Player player)
    {
        return player.PlayerId.Contains("estevao", StringComparison.OrdinalIgnoreCase) ||
            NormalizePlayerKey(player.Name).Equals("estevao", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePlayerKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static int? TryGetSeasonStartYear(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return null;
        }

        var startYearText = season.Trim().Replace('/', '-').Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(startYearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private sealed record KnownPositionCorrection(
        string PlayerKey,
        string TeamKey,
        Position Position,
        string PreferredPosition,
        IReadOnlyList<string> SecondaryPositions);

    private sealed record SourceTeamIdentity(Team Team, string Country);

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
            .OrderBy(GetFixtureCalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .FirstOrDefault();

        if (nextFixture is not null)
        {
            return GetFixtureCalendarRound(nextFixture);
        }

        return league.Fixtures.Count == 0
            ? 1
            : league.Fixtures.Max(GetFixtureCalendarRound);
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
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
            .OrderBy(GetFixtureCalendarRound)
            .ThenBy(fixture => fixture.Competition)
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
        data.SeasonHistory ??= [];
        data.LeagueState ??= new LeagueState();
        data.Teams ??= [];
        data.Fixtures ??= [];
        data.MatchHistory ??= [];
        data.PlayerStats ??= [];
        data.PlayerCompetitionStats ??= [];
        data.CompetitionStates ??= [];
        data.YouthAcademies ??= [];
        data.ClubMatchSetups ??= [];
        data.TransferMarketState ??= new TransferMarketState();

        BackfillPlayerDataFromLeagueData(data);
        RepairPlaceholderTeams(data);
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
            EnsureFixtureMetadata(fixture);
            fixture.HomeTeam = GetCanonicalTeam(teamsByName, fixture.HomeTeam);
            fixture.AwayTeam = GetCanonicalTeam(teamsByName, fixture.AwayTeam);
            ApplyMissingFixtureTeamData(fixture.HomeTeam);
            ApplyMissingFixtureTeamData(fixture.AwayTeam);

            if (fixture.Result is not null)
            {
                RehydrateMatchReferences(fixture.Result, teamsByName);
            }
        }

        EnsureCompleteDoubleRoundRobinFixtures(data, teamsByName);
        EnsureCompetitionStates(data);
        EnsureYouthAcademies(data);

        foreach (var match in data.MatchHistory)
        {
            RehydrateMatchReferences(match, teamsByName);
        }

        RehydrateTransferMarketReferences(data);
        RepairPlaceholderTeams(data);
        data.MatchHistory = CreateMatchHistory(data.Fixtures);
    }

    private static void EnsureFixtureMetadata(Fixture fixture)
    {
        if (string.IsNullOrWhiteSpace(fixture.FixtureId))
        {
            fixture.FixtureId = Guid.NewGuid().ToString("N");
        }

        if (fixture.CalendarRound <= 0)
        {
            fixture.CalendarRound = fixture.RoundNumber;
        }

        if (fixture.RoundNumber <= 0)
        {
            fixture.RoundNumber = fixture.CalendarRound;
        }

        if (fixture.Competition == CompetitionType.PremierLeague)
        {
            fixture.AffectsLeagueTable = true;
            fixture.RoundName = string.IsNullOrWhiteSpace(fixture.RoundName)
                ? $"Round {fixture.RoundNumber}"
                : fixture.RoundName;
        }
        else
        {
            fixture.AffectsLeagueTable = false;
            fixture.RoundName = string.IsNullOrWhiteSpace(fixture.RoundName)
                ? CompetitionNames.GetDisplayName(fixture.Competition)
                : fixture.RoundName;
        }

        if (string.IsNullOrWhiteSpace(fixture.Venue) && fixture.HomeTeam is not null)
        {
            fixture.Venue = !string.IsNullOrWhiteSpace(fixture.HomeTeam.StadiumName)
                ? fixture.HomeTeam.StadiumName
                : !string.IsNullOrWhiteSpace(fixture.HomeTeam.Venue)
                    ? fixture.HomeTeam.Venue
                    : $"{fixture.HomeTeam.Name} Stadium";
        }
    }

    private static void EnsureCompetitionStates(SaveGameData data)
    {
        if (data.CompetitionStates.Count > 0)
        {
            return;
        }

        data.CompetitionStates = new SeasonCalendarService().CreateInitialCompetitionStates(data.Teams);
    }

    private static void EnsureYouthAcademies(SaveGameData data)
    {
        new YouthAcademyService().EnsureAcademies(
            data.YouthAcademies,
            data.Teams,
            string.IsNullOrWhiteSpace(data.LeagueState.LeagueId)
                ? string.IsNullOrWhiteSpace(data.LeagueId) ? LeagueDataService.DefaultLeagueId : data.LeagueId
                : data.LeagueState.LeagueId,
            data.LeagueState.Season);
        var scoutService = new YouthScoutService();
        foreach (var academy in data.YouthAcademies)
        {
            scoutService.EnsureScoutNetwork(academy);
        }
    }

    private static void ApplyMissingFixtureTeamData(Team team)
    {
        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            PlayerAttributeService.ApplyMissingAttributes(player);
        }

        _ = LineupValidationService.RepairGoalkeeperSlot(team);
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
            EnsureFixtureMetadata(generatedFixture);
            generatedFixture.HomeTeam = GetCanonicalTeam(teamsByName, generatedFixture.HomeTeam);
            generatedFixture.AwayTeam = GetCanonicalTeam(teamsByName, generatedFixture.AwayTeam);
            if (!existingKeys.Add(CreateFixtureKey(generatedFixture)))
            {
                continue;
            }

            data.Fixtures.Add(generatedFixture);
        }

        data.Fixtures = data.Fixtures
            .OrderBy(GetFixtureCalendarRound)
            .ThenBy(fixture => fixture.Competition)
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

                ApplyPositionDataFromSource(player, sourcePlayer);
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

    private static void RepairPlaceholderTeams(SaveGameData data)
    {
        var sourceTeams = LoadAvailableSourceTeamsByName();
        foreach (var team in EnumerateSavedTeams(data))
        {
            if (!PlaceholderTeamFactory.HasPlaceholderNames(team))
            {
                continue;
            }

            if (sourceTeams.TryGetValue(team.Name, out var sourceTeam))
            {
                if (PlaceholderTeamFactory.RepairPlaceholderNames(team, sourceTeam.Team, sourceTeam.Country))
                {
                    NormalizeRepairedTeam(team);
                }
            }
            else
            {
                if (PlaceholderTeamFactory.RepairPlaceholderNames(team))
                {
                    NormalizeRepairedTeam(team);
                }
            }
        }
    }

    private static void NormalizeRepairedTeam(Team team)
    {
        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            PositionSuitabilityService.EnsurePositionMetadata(player);
            PlayerAttributeService.ApplyMissingAttributes(player);
        }

        _ = LineupValidationService.RepairGoalkeeperSlot(team);
    }

    private static IEnumerable<Team> EnumerateSavedTeams(SaveGameData data)
    {
        foreach (var team in data.Teams)
        {
            yield return team;
        }

        foreach (var fixture in data.Fixtures)
        {
            if (fixture.HomeTeam is not null)
            {
                yield return fixture.HomeTeam;
            }

            if (fixture.AwayTeam is not null)
            {
                yield return fixture.AwayTeam;
            }
        }

        foreach (var match in data.MatchHistory)
        {
            if (match.HomeTeam is not null)
            {
                yield return match.HomeTeam;
            }

            if (match.AwayTeam is not null)
            {
                yield return match.AwayTeam;
            }
        }

        foreach (var league in data.TransferMarketState.Leagues)
        {
            foreach (var team in league.Teams)
            {
                yield return team;
            }
        }
    }

    private static Dictionary<string, SourceTeamIdentity> LoadAvailableSourceTeamsByName()
    {
        var result = new Dictionary<string, SourceTeamIdentity>(StringComparer.OrdinalIgnoreCase);
        var dataService = new LeagueDataService();
        IReadOnlyList<LeagueDefinition> definitions;
        try
        {
            definitions = dataService.LoadSquadSourceDefinitions();
        }
        catch
        {
            return result;
        }

        foreach (var definition in definitions)
        {
            List<Team> teams;
            try
            {
                teams = dataService.LoadTeams(definition);
            }
            catch
            {
                continue;
            }

            foreach (var team in teams)
            {
                result.TryAdd(team.Name, new SourceTeamIdentity(team, definition.Country));
            }
        }

        return result;
    }

    private static void ApplyPositionDataFromSource(Player player, Player sourcePlayer)
    {
        var oldPreferredPosition = player.PreferredPosition;
        player.Position = sourcePlayer.Position;
        player.PreferredPosition = sourcePlayer.PreferredPosition;
        player.SecondaryPositions = sourcePlayer.SecondaryPositions
            .Where(position => !position.Equals(sourcePlayer.PreferredPosition, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(player.AssignedPosition) ||
            player.AssignedPosition.Equals(oldPreferredPosition, StringComparison.OrdinalIgnoreCase))
        {
            player.AssignedPosition = sourcePlayer.PreferredPosition;
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
