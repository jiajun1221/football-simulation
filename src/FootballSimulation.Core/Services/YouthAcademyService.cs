using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class YouthAcademyService
{
    public const int MinimumPromotionAge = 16;
    public const int MinimumPromotionOverall = 55;
    public const int MaximumSeniorSquadSize = 30;

    private readonly YouthPlayerGeneratorService _generator;
    private readonly YouthDevelopmentService _developmentService;

    public YouthAcademyService()
        : this(new YouthPlayerGeneratorService(), new YouthDevelopmentService())
    {
    }

    public YouthAcademyService(YouthPlayerGeneratorService generator, YouthDevelopmentService developmentService)
    {
        _generator = generator;
        _developmentService = developmentService;
    }

    public void EnsureAcademies(League league)
    {
        ArgumentNullException.ThrowIfNull(league);
        league.YouthAcademies ??= [];
        EnsureAcademies(league.YouthAcademies, league.Teams, league.LeagueId, league.Season);
    }

    public void EnsureAcademies(List<YouthAcademy> academies, IEnumerable<Team> teams, string leagueId, string season)
    {
        var teamList = teams.ToList();
        var teamNames = teamList.Select(team => team.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        academies.RemoveAll(academy => !teamNames.Contains(academy.ClubName));
        var academiesByClub = academies
            .Where(academy => !string.IsNullOrWhiteSpace(academy.ClubName))
            .GroupBy(academy => academy.ClubName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var team in teamList)
        {
            if (!academiesByClub.TryGetValue(team.Name, out var academy))
            {
                academy = _generator.CreateAcademy(team, leagueId, season);
                academies.Add(academy);
            }

            academy.ClubId = string.IsNullOrWhiteSpace(academy.ClubId)
                ? YouthPlayerGeneratorService.CreateClubId(leagueId, team.Name)
                : academy.ClubId;
            academy.ClubName = team.Name;
            academy.YouthPlayers ??= [];
            academy.IntakeHistory ??= [];
            academy.TransferHistory ??= [];
            academy.ScoutAssignments ??= [];
            academy.ScoutReports ??= [];
            foreach (var player in academy.YouthPlayers)
            {
                NormalizeYouthPlayer(player, academy);
            }

            foreach (var report in academy.ScoutReports)
            {
                report.Prospects ??= [];
                foreach (var prospect in report.Prospects)
                {
                    prospect.SecondaryPositions ??= [];
                    prospect.Traits ??= [];
                }
            }
        }
    }

    public IReadOnlyList<YouthPlayer> GenerateSeasonalIntake(League league, string season)
    {
        EnsureAcademies(league);
        var createdPlayers = new List<YouthPlayer>();
        foreach (var team in league.Teams)
        {
            var academy = GetAcademy(league, team.Name);
            if (academy.IntakeHistory.Any(record => record.Season.Equals(season, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var count = CalculateIntakeCount(academy);
            var players = _generator.GenerateSeasonalIntake(academy, team, season, count);
            foreach (var player in players)
            {
                academy.YouthPlayers.Add(player);
                createdPlayers.Add(player);
            }

            academy.IntakeHistory.Add(new YouthIntakeRecord
            {
                Season = season,
                CalendarRound = 1,
                PlayerCount = players.Count,
                PlayerIds = players.Select(player => player.PlayerId).ToList(),
                Summary = CreateIntakeSummary(players)
            });
        }

        return createdPlayers;
    }

    public YouthPlayer? AddScoutDiscovery(League league, Team team, int currentRound)
    {
        EnsureAcademies(league);
        if (currentRound is < 10 or > 28 || currentRound % 9 != 0)
        {
            return null;
        }

        var academy = GetAcademy(league, team.Name);
        var existingDiscovery = academy.IntakeHistory.Any(record =>
            record.Season.Equals(league.Season, StringComparison.OrdinalIgnoreCase) &&
            record.CalendarRound == currentRound);
        if (existingDiscovery)
        {
            return null;
        }

        var player = _generator.GenerateScoutDiscovery(academy, team, league.Season, currentRound);
        academy.YouthPlayers.Add(player);
        academy.IntakeHistory.Add(new YouthIntakeRecord
        {
            Season = league.Season,
            CalendarRound = currentRound,
            PlayerCount = 1,
            PlayerIds = [player.PlayerId],
            Summary = $"Scout discovery: {player.Name}, {player.PreferredPosition}, potential {player.PotentialMin}-{player.PotentialMax}."
        });
        return player;
    }

    public void ApplyDevelopment(League league, int months = 1)
    {
        EnsureAcademies(league);
        foreach (var academy in league.YouthAcademies)
        {
            foreach (var player in academy.YouthPlayers.Where(player => !player.IsPromoted))
            {
                _developmentService.ApplyDevelopment(player, academy, months);
                player.MarketValue = YouthMarketValueCalculator.CalculateMarketValue(player, academy);
                player.ScoutReport = YouthScoutReportService.CreateScoutReport(player);
            }
        }
    }

    public void ApplySeasonRollover(League league)
    {
        EnsureAcademies(league);
        foreach (var academy in league.YouthAcademies)
        {
            foreach (var player in academy.YouthPlayers.Where(player => !player.IsPromoted))
            {
                player.Age = Math.Min(21, player.Age + 1);
            }
        }

        ApplyDevelopment(league, months: 3);
        GenerateSeasonalIntake(league, league.Season);
        ApplyAiAcademyLogic(league);
    }

    public YouthOperationResult PromoteYouthPlayer(League league, Team team, string youthPlayerId)
    {
        EnsureAcademies(league);
        var academy = GetAcademy(league, team.Name);
        var youthPlayer = academy.YouthPlayers.FirstOrDefault(player =>
            player.PlayerId.Equals(youthPlayerId, StringComparison.OrdinalIgnoreCase));
        if (youthPlayer is null)
        {
            return new YouthOperationResult(false, "Youth player not found.");
        }

        if (!CanPromote(team, youthPlayer, out var reason))
        {
            return new YouthOperationResult(false, reason, youthPlayer);
        }

        var player = CreateSeniorPlayer(youthPlayer, team);
        team.Substitutes.Add(player);
        youthPlayer.IsPromoted = true;
        academy.YouthPlayers.Remove(youthPlayer);
        return new YouthOperationResult(true, $"{youthPlayer.Name} promoted to the senior squad.", youthPlayer, player);
    }

    public bool CanPromote(Team team, YouthPlayer youthPlayer, out string reason)
    {
        if (youthPlayer.Age < MinimumPromotionAge)
        {
            reason = "Player must be at least 16 before promotion.";
            return false;
        }

        if (youthPlayer.CurrentOVR < MinimumPromotionOverall)
        {
            reason = $"Player must reach {MinimumPromotionOverall} OVR before promotion.";
            return false;
        }

        if (team.Players.Count + team.Substitutes.Count >= MaximumSeniorSquadSize)
        {
            reason = "Senior squad is full.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public Player CreateSeniorPlayer(YouthPlayer youthPlayer, Team team)
    {
        var player = new Player
        {
            PlayerId = string.IsNullOrWhiteSpace(youthPlayer.PlayerId) ? Guid.NewGuid().ToString("N") : youthPlayer.PlayerId,
            Name = youthPlayer.Name,
            SquadNumber = GetNextSquadNumber(team),
            Position = youthPlayer.Position,
            PreferredPosition = youthPlayer.PreferredPosition,
            AssignedPosition = youthPlayer.PreferredPosition,
            SecondaryPositions = youthPlayer.SecondaryPositions.ToList(),
            Nationality = youthPlayer.Nationality,
            NationalityCode = youthPlayer.NationalityCode,
            NationalityName = youthPlayer.NationalityName,
            FlagImagePath = youthPlayer.FlagImagePath,
            OverallRating = youthPlayer.CurrentOVR,
            BaseOverallRating = youthPlayer.CurrentOVR,
            Age = youthPlayer.Age,
            PotentialOverall = youthPlayer.HiddenTruePotential,
            Role = PlayerRole.Prospect,
            Form = "Average",
            CurrentForm = 50,
            Morale = 55,
            Stamina = 88,
            Traits = youthPlayer.Traits.ToList(),
            PreferredFoot = youthPlayer.PreferredPosition is "LB" or "LW" ? "Left" : "Right",
            ContractEndYear = PlayerContractService.DefaultSeasonEndYear + 3,
            WeeklyWage = Math.Max(1_000, Math.Round(youthPlayer.MarketValue / 3_500m, 0)),
            TransferStatus = PlayerTransferStatus.None,
            IsStarter = false,
            IsOnPitch = false
        };

        var attributes = PlayerAttributeService.DeriveAttributes(
            player.Position,
            player.PreferredPosition,
            player.OverallRating,
            player.Traits,
            (int)Math.Round(player.Stamina));
        player.Pace = attributes.Pace;
        player.Shooting = attributes.Shooting;
        player.Passing = attributes.Passing;
        player.Dribbling = attributes.Dribbling;
        player.Defending = attributes.Defending;
        player.Physical = attributes.Physical;
        player.ReleaseClause = PlayerContractService.EstimateReleaseClause(player, youthPlayer.MarketValue, string.Empty);
        return player;
    }

    public YouthAcademy GetAcademy(League league, string clubName)
    {
        EnsureAcademies(league);
        return league.YouthAcademies.First(academy =>
            academy.ClubName.Equals(clubName, StringComparison.OrdinalIgnoreCase));
    }

    public static YouthAcademy? FindAcademy(IEnumerable<YouthAcademy> academies, string clubName)
    {
        return academies.FirstOrDefault(academy =>
            academy.ClubName.Equals(clubName, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyAiAcademyLogic(League league)
    {
        foreach (var team in league.Teams)
        {
            var academy = GetAcademy(league, team.Name);
            var strongestProspect = academy.YouthPlayers
                .Where(player => !player.IsPromoted)
                .Where(player => player.Age >= MinimumPromotionAge)
                .Where(player => player.CurrentOVR >= MinimumPromotionOverall)
                .OrderByDescending(player => GetAiPromotionScore(team, player))
                .FirstOrDefault();
            if (strongestProspect is null)
            {
                continue;
            }

            var score = GetAiPromotionScore(team, strongestProspect);
            if (score >= 112)
            {
                _ = PromoteYouthPlayer(league, team, strongestProspect.PlayerId);
            }
        }
    }

    private static double GetAiPromotionScore(Team team, YouthPlayer player)
    {
        var squadDepth = team.Players.Concat(team.Substitutes)
            .Count(existing => existing.Position == player.Position);
        var score = player.CurrentOVR + (player.HiddenTruePotential - player.CurrentOVR) * 0.9;
        if (squadDepth <= 4)
        {
            score += 12;
        }

        if (player.PotentialTier >= YouthPotentialTier.EliteProspect)
        {
            score += 10;
        }

        return score;
    }

    private static void NormalizeYouthPlayer(YouthPlayer player, YouthAcademy academy)
    {
        player.ClubId = academy.ClubId;
        player.ClubName = academy.ClubName;
        player.SecondaryPositions ??= [];
        player.Traits ??= [];
        if (string.IsNullOrWhiteSpace(player.PlayerId))
        {
            player.PlayerId = $"youth-{Guid.NewGuid():N}";
        }

        if (string.IsNullOrWhiteSpace(player.ScoutReport))
        {
            player.ScoutReport = YouthScoutReportService.CreateScoutReport(player);
        }

        player.MarketValue = player.MarketValue > 0
            ? player.MarketValue
            : YouthMarketValueCalculator.CalculateMarketValue(player, academy);
    }

    private static int CalculateIntakeCount(YouthAcademy academy)
    {
        var seed = Math.Abs(HashCode.Combine(academy.ClubId, academy.IntakeHistory.Count, academy.YouthPlayers.Count));
        return 4 + seed % 5;
    }

    private static string CreateIntakeSummary(IReadOnlyList<YouthPlayer> players)
    {
        var best = players.OrderByDescending(player => player.PotentialMax).FirstOrDefault();
        return best is null
            ? "No youth intake generated."
            : $"{players.Count} players joined. Top prospect: {best.Name}, {best.PreferredPosition}, potential {best.PotentialMin}-{best.PotentialMax}.";
    }

    private static int GetNextSquadNumber(Team team)
    {
        var used = team.Players.Concat(team.Substitutes)
            .Select(player => player.SquadNumber)
            .ToHashSet();
        for (var number = 12; number <= 99; number++)
        {
            if (!used.Contains(number))
            {
                return number;
            }
        }

        return 0;
    }
}

public class YouthDevelopmentService
{
    public void ApplyDevelopment(YouthPlayer player, YouthAcademy academy, int months = 1)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(academy);

        var potentialGap = Math.Max(0, player.HiddenTruePotential - player.CurrentOVR);
        if (potentialGap == 0)
        {
            return;
        }

        var monthlyGrowth = 0.22 *
            GetPotentialMultiplier(player) *
            GetAgeMultiplier(player.Age) *
            GetAcademyMultiplier(academy.AcademyLevel) *
            GetDevelopmentRateMultiplier(player.DevelopmentRate) *
            GetPersonalityMultiplier(player.Personality) *
            GetTrainingFocusMultiplier(player, academy.TrainingFocus);

        player.DevelopmentProgress += monthlyGrowth * Math.Max(1, months);
        while (player.DevelopmentProgress >= 1.0 && player.CurrentOVR < player.HiddenTruePotential)
        {
            player.CurrentOVR++;
            player.DevelopmentProgress -= 1.0;
        }
    }

    private static double GetPotentialMultiplier(YouthPlayer player)
    {
        return player.PotentialTier switch
        {
            YouthPotentialTier.GenerationalTalent => 1.45,
            YouthPotentialTier.EliteProspect => 1.25,
            YouthPotentialTier.ExcitingProspect => 1.10,
            YouthPotentialTier.CommonProspect => 0.86,
            _ => 1.0
        };
    }

    private static double GetAgeMultiplier(int age)
    {
        return age switch
        {
            <= 15 => 1.18,
            16 => 1.12,
            17 => 1.00,
            18 => 0.86,
            _ => 0.72
        };
    }

    private static double GetAcademyMultiplier(AcademyLevel level)
    {
        return level switch
        {
            AcademyLevel.Elite => 1.28,
            AcademyLevel.Gold => 1.14,
            AcademyLevel.Bronze => 0.84,
            _ => 1.0
        };
    }

    private static double GetDevelopmentRateMultiplier(YouthDevelopmentRate rate)
    {
        return rate switch
        {
            YouthDevelopmentRate.Explosive => 1.65,
            YouthDevelopmentRate.Fast => 1.28,
            YouthDevelopmentRate.Slow => 0.68,
            _ => 1.0
        };
    }

    private static double GetPersonalityMultiplier(YouthPersonality personality)
    {
        return personality switch
        {
            YouthPersonality.Determined => 1.16,
            YouthPersonality.Professional => 1.12,
            YouthPersonality.Ambitious => 1.08,
            YouthPersonality.Reserved => 0.93,
            _ => 1.0
        };
    }

    private static double GetTrainingFocusMultiplier(YouthPlayer player, YouthTrainingFocus focus)
    {
        return focus switch
        {
            YouthTrainingFocus.Technical when player.PreferredPosition is "CM" or "CAM" or "LW" or "RW" => 1.10,
            YouthTrainingFocus.Physical when player.PreferredPosition is "CB" or "CDM" or "ST" => 1.08,
            YouthTrainingFocus.Attacking when player.Position == Position.Forward => 1.10,
            YouthTrainingFocus.Defensive when player.Position is Position.Defender or Position.Goalkeeper => 1.10,
            YouthTrainingFocus.Playmaking when player.PreferredPosition is "CM" or "CAM" => 1.12,
            _ => 1.0
        };
    }
}
