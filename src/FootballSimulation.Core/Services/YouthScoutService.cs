using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class YouthScoutService
{
    public const int ScoutSlotCount = 3;
    public const int RequiredMatchesPerReport = 3;

    private static readonly YouthScoutCountry[] Countries =
    [
        Country("England", "GB-ENG", "england"),
        Country("Brazil", "BR", "brazil"),
        Country("Argentina", "AR", "argentina"),
        Country("France", "FR", "france"),
        Country("Spain", "ES", "spain"),
        Country("Germany", "DE", "germany"),
        Country("Portugal", "PT", "portugal"),
        Country("Netherlands", "NL", "netherlands"),
        Country("Belgium", "BE", "belgium"),
        Country("Italy", "IT", "italy"),
        Country("Croatia", "HR", "croatia"),
        Country("Uruguay", "UY", "uruguay"),
        Country("Colombia", "CO", "colombia"),
        Country("Norway", "NO", "norway"),
        Country("Denmark", "DK", "denmark"),
        Country("Japan", "JP", "japan"),
        Country("South Korea", "KR", "south-korea")
    ];

    private static readonly string[] AllPositions = ["GK", "CB", "LB", "RB", "CDM", "CM", "CAM", "LW", "RW", "ST", "CF"];

    private static readonly YouthScoutPositionFocus[] PositionFocuses =
    [
        YouthScoutPositionFocus.AnyPosition,
        YouthScoutPositionFocus.GK,
        YouthScoutPositionFocus.CB,
        YouthScoutPositionFocus.LB,
        YouthScoutPositionFocus.RB,
        YouthScoutPositionFocus.CDM,
        YouthScoutPositionFocus.CM,
        YouthScoutPositionFocus.CAM,
        YouthScoutPositionFocus.LW,
        YouthScoutPositionFocus.RW,
        YouthScoutPositionFocus.ST,
    ];

    private static readonly Dictionary<string, CountryProfile> CountryProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brazil"] = new(["Lucas", "Joao", "Gabriel", "Rafael", "Bruno", "Caio"], ["Silva", "Mendes", "Costa", "Pereira", "Lima", "Alves"], [PlayerTrait.Flair, PlayerTrait.TechnicalDribbler, PlayerTrait.OutsideFootShot], YouthDevelopmentRate.Fast),
        ["Argentina"] = new(["Mateo", "Thiago", "Nico", "Lautaro", "Julian", "Tomas"], ["Fernandez", "Romero", "Alvarez", "Sosa", "Acuna", "Vega"], [PlayerTrait.Playmaker, PlayerTrait.Flair, PlayerTrait.BigMatchPlayer], YouthDevelopmentRate.Fast),
        ["France"] = new(["Enzo", "Hugo", "Lucas", "Mathis", "Noah", "Theo"], ["Bernard", "Dubois", "Moreau", "Roux", "Lefevre", "Girard"], [PlayerTrait.Rapid, PlayerTrait.Engine, PlayerTrait.AerialThreat], YouthDevelopmentRate.Fast),
        ["Spain"] = new(["Diego", "Hugo", "Iker", "Mateo", "Nico", "Pablo"], ["Alonso", "Garcia", "Ramos", "Santos", "Vega", "Torres"], [PlayerTrait.Playmaker, PlayerTrait.PressResistant, PlayerTrait.TechnicalDribbler], YouthDevelopmentRate.Normal),
        ["England"] = new(["Alfie", "Archie", "Ethan", "Leo", "Oscar", "Theo"], ["Bennett", "Cole", "Foster", "Hughes", "Parker", "Ward"], [PlayerTrait.Leadership, PlayerTrait.Engine, PlayerTrait.TeamPlayer], YouthDevelopmentRate.Normal),
        ["Portugal"] = new(["Diogo", "Tomas", "Ruben", "Goncalo", "Tiago", "Andre"], ["Costa", "Fernandes", "Mendes", "Neves", "Ramos", "Silva"], [PlayerTrait.Flair, PlayerTrait.Playmaker, PlayerTrait.TechnicalDribbler], YouthDevelopmentRate.Fast),
        ["Germany"] = new(["Ben", "Emil", "Finn", "Jonas", "Leon", "Lukas"], ["Bauer", "Fischer", "Klein", "Meyer", "Schulz", "Weber"], [PlayerTrait.TeamPlayer, PlayerTrait.BigMatchPlayer, PlayerTrait.LongPasser], YouthDevelopmentRate.Normal),
        ["Netherlands"] = new(["Daan", "Finn", "Jens", "Lars", "Milan", "Sem"], ["Bakker", "Jansen", "Meijer", "Smit", "Visser", "Vos"], [PlayerTrait.BoxToBox, PlayerTrait.Playmaker, PlayerTrait.PressResistant], YouthDevelopmentRate.Normal),
        ["Belgium"] = new(["Arthur", "Elias", "Liam", "Noah", "Milan", "Jules"], ["Claes", "Peeters", "Janssens", "Dubois", "Lambert", "Vermeulen"], [PlayerTrait.Playmaker, PlayerTrait.ClinicalFinisher, PlayerTrait.PressResistant], YouthDevelopmentRate.Normal),
        ["Italy"] = new(["Luca", "Matteo", "Gabriele", "Andrea", "Marco", "Nico"], ["Ricci", "Conti", "Ferrari", "Rossi", "Bianchi", "Romano"], [PlayerTrait.Interceptor, PlayerTrait.TeamPlayer, PlayerTrait.LongPasser], YouthDevelopmentRate.Normal),
        ["Croatia"] = new(["Luka", "Ivan", "Marko", "Ante", "Dino", "Niko"], ["Horvat", "Kovac", "Maric", "Novak", "Peric", "Vidic"], [PlayerTrait.Playmaker, PlayerTrait.LongPasser, PlayerTrait.PressResistant], YouthDevelopmentRate.Normal),
        ["Uruguay"] = new(["Santiago", "Facundo", "Mateo", "Lucas", "Bruno", "Emiliano"], ["Suarez", "Nunez", "Rojas", "Silva", "Pereira", "Mendez"], [PlayerTrait.BigMatchPlayer, PlayerTrait.TeamPlayer, PlayerTrait.AggressiveTackler], YouthDevelopmentRate.Normal),
        ["Colombia"] = new(["Juan", "Santiago", "Mateo", "Daniel", "Luis", "Andres"], ["Diaz", "Quintero", "Moreno", "Gomez", "Restrepo", "Torres"], [PlayerTrait.Rapid, PlayerTrait.Flair, PlayerTrait.TechnicalDribbler], YouthDevelopmentRate.Fast),
        ["Norway"] = new(["Emil", "Magnus", "Oscar", "Sander", "Jonas", "Noah"], ["Hansen", "Johansen", "Larsen", "Olsen", "Berg", "Solberg"], [PlayerTrait.Engine, PlayerTrait.AerialThreat, PlayerTrait.TeamPlayer], YouthDevelopmentRate.Normal),
        ["Denmark"] = new(["Mikkel", "Noah", "Oscar", "Viktor", "Emil", "Frederik"], ["Nielsen", "Jensen", "Larsen", "Madsen", "Pedersen", "Poulsen"], [PlayerTrait.TeamPlayer, PlayerTrait.Leadership, PlayerTrait.LongPasser], YouthDevelopmentRate.Normal),
        ["Japan"] = new(["Haruto", "Ren", "Yuto", "Sota", "Riku", "Kaito"], ["Sato", "Suzuki", "Takahashi", "Tanaka", "Ito", "Kobayashi"], [PlayerTrait.Engine, PlayerTrait.TeamPlayer, PlayerTrait.TechnicalDribbler], YouthDevelopmentRate.Fast),
        ["South Korea"] = new(["Min-jun", "Seo-jun", "Ji-ho", "Hyun-woo", "Do-yun", "Joon"], ["Kim", "Lee", "Park", "Choi", "Jung", "Kang"], [PlayerTrait.Engine, PlayerTrait.Rapid, PlayerTrait.TeamPlayer], YouthDevelopmentRate.Fast)
    };

    private readonly YouthAcademyService _academyService;
    private readonly ClubFinanceService _financeService;

    public YouthScoutService()
        : this(new YouthAcademyService(), new ClubFinanceService())
    {
    }

    public YouthScoutService(YouthAcademyService academyService, ClubFinanceService financeService)
    {
        _academyService = academyService;
        _financeService = financeService;
    }

    public IReadOnlyList<YouthScoutCountry> GetAvailableCountries()
    {
        return Countries;
    }

    public IReadOnlyList<YouthScoutPositionFocus> GetAvailablePositionFocuses()
    {
        return PositionFocuses;
    }

    public void EnsureScoutNetwork(League league)
    {
        _academyService.EnsureAcademies(league);
        foreach (var academy in league.YouthAcademies)
        {
            EnsureScoutNetwork(academy);
        }
    }

    public void EnsureScoutNetwork(YouthAcademy academy)
    {
        academy.ScoutAssignments ??= [];
        academy.ScoutReports ??= [];

        var defaults = GetDefaultCountries(academy);
        for (var index = 0; index < ScoutSlotCount; index++)
        {
            var scoutId = $"scout-{index + 1}";
            var assignment = academy.ScoutAssignments.FirstOrDefault(item =>
                item.ScoutId.Equals(scoutId, StringComparison.OrdinalIgnoreCase));
            if (assignment is null)
            {
                var country = defaults[index % defaults.Length];
                assignment = new YouthScoutAssignment
                {
                    ScoutId = scoutId,
                    ScoutName = $"Scout #{index + 1}",
                    Rating = GetDefaultScoutRating(academy, index),
                    AssignedCountry = country.Name,
                    CountryCode = country.Code,
                    FlagImagePath = country.FlagImagePath,
                    RequiredMatches = RequiredMatchesPerReport
                };
                academy.ScoutAssignments.Add(assignment);
            }

            NormalizeAssignment(assignment, index);
        }

        academy.ScoutAssignments.RemoveAll(assignment => !assignment.ScoutId.StartsWith("scout-", StringComparison.OrdinalIgnoreCase));
        foreach (var report in academy.ScoutReports)
        {
            report.Prospects ??= [];
            foreach (var prospect in report.Prospects)
            {
                prospect.SecondaryPositions ??= [];
                prospect.Traits ??= [];
                prospect.WeeklyWage = prospect.WeeklyWage > 0 ? prospect.WeeklyWage : CalculateWeeklyWage(prospect);
            }
        }
    }

    public YouthOperationResult AssignCountry(YouthAcademy academy, string scoutId, string countryName)
    {
        EnsureScoutNetwork(academy);
        var assignment = academy.ScoutAssignments.FirstOrDefault(item =>
            item.ScoutId.Equals(scoutId, StringComparison.OrdinalIgnoreCase));
        return AssignScoutingPlan(
            academy,
            scoutId,
            countryName,
            assignment?.PrimaryFocus ?? YouthScoutPositionFocus.AnyPosition);
    }

    public YouthOperationResult AssignScoutingPlan(
        YouthAcademy academy,
        string scoutId,
        string countryName,
        YouthScoutPositionFocus primaryFocus,
        YouthScoutPositionFocus secondaryFocus = YouthScoutPositionFocus.AnyPosition)
    {
        EnsureScoutNetwork(academy);
        var assignment = academy.ScoutAssignments.FirstOrDefault(item =>
            item.ScoutId.Equals(scoutId, StringComparison.OrdinalIgnoreCase));
        if (assignment is null)
        {
            return new YouthOperationResult(false, "Scout slot not found.");
        }

        if (assignment.ProgressMatches is > 0 && assignment.ProgressMatches < assignment.RequiredMatches)
        {
            return new YouthOperationResult(false, $"{assignment.ScoutName} must finish the current report before changing country.");
        }

        var country = FindCountry(countryName);
        if (country is null)
        {
            return new YouthOperationResult(false, "Country is not available for scouting.");
        }

        var clearedReportCount = ClearScoutReports(academy, scoutId);
        assignment.AssignedCountry = country.Name;
        assignment.CountryCode = country.Code;
        assignment.FlagImagePath = country.FlagImagePath;
        assignment.PrimaryFocus = primaryFocus;
        assignment.SecondaryFocus = YouthScoutPositionFocus.AnyPosition;
        assignment.ProgressMatches = 0;
        assignment.ActiveReportId = string.Empty;
        assignment.LastProgressRound = 0;
        var clearMessage = clearedReportCount > 0 ? " Previous report cleared." : string.Empty;
        return new YouthOperationResult(true, $"{assignment.ScoutName} assigned to {country.Name} with {FormatFocusLabel(assignment.PrimaryFocus)} focus.{clearMessage}");
    }

    public IReadOnlyList<YouthScoutReport> AdvanceScoutingAfterCompletedCalendarSlot(League league, int completedCalendarRound)
    {
        EnsureScoutNetwork(league);
        var teamNames = league.Fixtures
            .Where(fixture => fixture.IsPlayed)
            .Where(fixture => GetFixtureCalendarRound(fixture) == completedCalendarRound)
            .SelectMany(fixture => new[] { fixture.HomeTeam.Name, fixture.AwayTeam.Name })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reports = new List<YouthScoutReport>();
        foreach (var teamName in teamNames)
        {
            var team = league.Teams.FirstOrDefault(item => item.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase));
            if (team is null)
            {
                continue;
            }

            reports.AddRange(AdvanceScoutingAfterClubMatch(league, team, completedCalendarRound));
        }

        return reports;
    }

    public IReadOnlyList<YouthScoutReport> AdvanceScoutingAfterClubMatch(League league, Team team, int currentRound)
    {
        EnsureScoutNetwork(league);
        var academy = _academyService.GetAcademy(league, team.Name);
        return AdvanceScoutingAfterClubMatch(academy, league.Season, currentRound);
    }

    public IReadOnlyList<YouthScoutReport> AdvanceScoutingAfterClubMatch(YouthAcademy academy, string season, int currentRound)
    {
        EnsureScoutNetwork(academy);
        var generatedReports = new List<YouthScoutReport>();
        foreach (var assignment in academy.ScoutAssignments)
        {
            if (assignment.LastProgressRound == currentRound || assignment.ProgressMatches >= assignment.RequiredMatches)
            {
                continue;
            }

            assignment.ProgressMatches++;
            assignment.LastProgressRound = currentRound;
            if (assignment.ProgressMatches >= assignment.RequiredMatches)
            {
                var report = GenerateReport(academy, assignment, season, currentRound);
                assignment.ActiveReportId = report.ReportId;
                academy.ScoutReports.Add(report);
                generatedReports.Add(report);
            }
        }

        return generatedReports;
    }

    public YouthOperationResult SignProspect(
        League league,
        TransferMarketState transferMarketState,
        Team team,
        string reportId,
        string prospectId,
        int currentRound)
    {
        EnsureScoutNetwork(league);
        var academy = _academyService.GetAcademy(league, team.Name);
        var report = academy.ScoutReports.FirstOrDefault(item =>
            item.ReportId.Equals(reportId, StringComparison.OrdinalIgnoreCase));
        var prospect = report?.Prospects.FirstOrDefault(item =>
            item.ProspectId.Equals(prospectId, StringComparison.OrdinalIgnoreCase));
        if (report is null || prospect is null)
        {
            return new YouthOperationResult(false, "Scout prospect not found.");
        }

        if (prospect.IsSigned)
        {
            return new YouthOperationResult(false, $"{prospect.Name} has already signed.");
        }

        prospect.WeeklyWage = prospect.WeeklyWage > 0 ? prospect.WeeklyWage : CalculateWeeklyWage(prospect);
        var finance = _financeService.GetOrCreateFinance(transferMarketState, league.LeagueId, team);
        if (finance.AvailableWageBudget < prospect.WeeklyWage)
        {
            return new YouthOperationResult(false, "Insufficient wage budget.", Fee: prospect.WeeklyWage);
        }

        var youthPlayer = CreateYouthPlayerFromProspect(prospect, academy, league.Season);
        academy.YouthPlayers.Add(youthPlayer);
        _academyService.RecordAcademySigning(
            academy,
            youthPlayer,
            league.Season,
            currentRound,
            "Scout Signing",
            $"Signed from {report.Country} scout report on {FormatWeeklyWage(prospect.WeeklyWage)}.");
        prospect.IsSigned = true;
        prospect.SignedByClubId = academy.ClubId;
        prospect.SignedByClubName = academy.ClubName;
        finance.YouthWageSpent += prospect.WeeklyWage;
        academy.IntakeHistory.Add(new YouthIntakeRecord
        {
            Season = league.Season,
            CalendarRound = currentRound,
            PlayerCount = 1,
            PlayerIds = [youthPlayer.PlayerId],
            Summary = $"Scouted signing: {youthPlayer.Name}, {youthPlayer.PreferredPosition}, potential {youthPlayer.PotentialMin}-{youthPlayer.PotentialMax}."
        });

        return new YouthOperationResult(true, $"You signed {prospect.Name} on {FormatWeeklyWage(prospect.WeeklyWage)}.", youthPlayer);
    }

    public void RunAiScoutingActivity(League league, TransferMarketState transferMarketState, Team selectedTeam, int currentRound)
    {
        EnsureScoutNetwork(league);
        foreach (var team in league.Teams.Where(team => !team.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var academy = _academyService.GetAcademy(league, team.Name);
            AssignNewCountriesForCompletedReports(academy, team.Name, currentRound);

            var target = academy.ScoutReports
                .SelectMany(report => report.Prospects.Select(prospect => new { Report = report, Prospect = prospect }))
                .Where(item => !item.Prospect.IsSigned)
                .OrderByDescending(item => GetAiProspectScore(team, item.Prospect))
                .FirstOrDefault();
            if (target is null || target.Prospect.PotentialMax < 84)
            {
                continue;
            }

            target.Prospect.WeeklyWage = target.Prospect.WeeklyWage > 0 ? target.Prospect.WeeklyWage : CalculateWeeklyWage(target.Prospect);
            var finance = _financeService.GetOrCreateFinance(transferMarketState, league.LeagueId, team);
            if (finance.AvailableWageBudget < target.Prospect.WeeklyWage)
            {
                continue;
            }

            var shouldSign = target.Prospect.PotentialMax >= 92 || GetDeterministicRoll(team.Name, target.Prospect.ProspectId, currentRound) < 0.35;
            if (shouldSign)
            {
                _ = SignProspect(league, transferMarketState, team, target.Report.ReportId, target.Prospect.ProspectId, currentRound);
            }
        }
    }

    private void AssignNewCountriesForCompletedReports(YouthAcademy academy, string clubName, int currentRound)
    {
        foreach (var assignment in academy.ScoutAssignments.Where(item => item.ProgressMatches >= item.RequiredMatches))
        {
            var seed = Math.Abs(HashCode.Combine(clubName, assignment.ScoutId, currentRound, academy.ScoutReports.Count));
            var country = Countries[seed % Countries.Length];
            _ = AssignCountry(academy, assignment.ScoutId, country.Name);
        }
    }

    private static int ClearScoutReports(YouthAcademy academy, string scoutId)
    {
        return academy.ScoutReports.RemoveAll(report =>
            report.ScoutId.Equals(scoutId, StringComparison.OrdinalIgnoreCase));
    }

    private YouthScoutReport GenerateReport(YouthAcademy academy, YouthScoutAssignment assignment, string season, int currentRound)
    {
        var country = FindCountry(assignment.AssignedCountry) ?? Countries[0];
        var profile = CountryProfiles.GetValueOrDefault(country.Name, CountryProfiles["England"]);
        var seed = Math.Abs(HashCode.Combine(academy.ClubId, assignment.ScoutId, country.Name, season, currentRound, academy.ScoutReports.Count));
        var random = new Random(seed);
        var count = GetProspectCount(assignment.Rating, random);
        var report = new YouthScoutReport
        {
            ScoutId = assignment.ScoutId,
            ScoutName = assignment.ScoutName,
            ScoutRating = assignment.Rating,
            Country = country.Name,
            CountryCode = country.Code,
            FlagImagePath = country.FlagImagePath,
            PrimaryFocus = assignment.PrimaryFocus,
            SecondaryFocus = YouthScoutPositionFocus.AnyPosition,
            Season = season,
            CreatedRound = currentRound
        };

        var hasPositionFocus = GetActiveFocusPositions(assignment).Any();
        var focusedTarget = hasPositionFocus ? (int)Math.Ceiling(count * 0.70) : 0;
        for (var index = 0; index < count; index++)
        {
            report.Prospects.Add(GenerateProspect(country, profile, assignment, random, index, index < focusedTarget));
        }

        return report;
    }

    private static YouthScoutProspect GenerateProspect(
        YouthScoutCountry country,
        CountryProfile profile,
        YouthScoutAssignment assignment,
        Random random,
        int index,
        bool shouldMatchFocus)
    {
        var focusPositions = GetActiveFocusPositions(assignment).ToList();
        var isFocusedMatch = focusPositions.Count > 0 && shouldMatchFocus;
        var positionPool = isFocusedMatch
            ? focusPositions
            : AllPositions.Where(position => !focusPositions.Contains(position, StringComparer.OrdinalIgnoreCase)).DefaultIfEmpty(AllPositions[random.Next(AllPositions.Length)]).ToList();
        var position = positionPool[random.Next(positionPool.Count)];
        var tier = PickPotentialTier(assignment.Rating, isFocusedMatch, random);
        var potential = GetPotentialRange(tier, isFocusedMatch, random);
        var truePotential = random.Next(potential.Min, potential.Max + 1);
        var currentOverall = Math.Clamp(random.Next(45, 63) + GetCurrentOverallBonus(tier, assignment.Rating, isFocusedMatch, random), 45, Math.Min(70, truePotential - 4));
        var prospect = new YouthScoutProspect
        {
            ProspectId = $"scout-{NormalizeId(country.Name)}-{Guid.NewGuid():N}",
            Name = $"{profile.FirstNames[random.Next(profile.FirstNames.Length)]} {profile.LastNames[random.Next(profile.LastNames.Length)]}",
            Nationality = country.Name,
            NationalityName = country.Name,
            NationalityCode = country.Code,
            FlagImagePath = country.FlagImagePath,
            Age = random.Next(15, 19),
            Position = MapPosition(position),
            PreferredPosition = position,
            SecondaryPositions = GetSecondaryPositions(position),
            CurrentOVR = currentOverall,
            PotentialMin = potential.Min,
            PotentialMax = potential.Max,
            HiddenTruePotential = truePotential,
            PotentialTier = tier,
            Personality = PickPersonality(random),
            DevelopmentRate = PickDevelopmentRate(tier, assignment.Rating, profile.DevelopmentLean, random),
            Traits = PickTraits(position, tier, profile.TraitBiases, random),
        };
        prospect.SigningCost = CalculateSigningCost(prospect);
        prospect.WeeklyWage = CalculateWeeklyWage(prospect);
        prospect.ScoutNotes = CreateScoutNotes(prospect, index);
        return prospect;
    }

    private static YouthPlayer CreateYouthPlayerFromProspect(YouthScoutProspect prospect, YouthAcademy academy, string season)
    {
        var player = new YouthPlayer
        {
            PlayerId = $"youth-{NormalizeId(academy.ClubName)}-scout-{Guid.NewGuid():N}",
            Name = prospect.Name,
            Nationality = prospect.Nationality,
            NationalityCode = prospect.NationalityCode,
            NationalityName = prospect.NationalityName,
            FlagImagePath = prospect.FlagImagePath,
            Age = prospect.Age,
            Position = prospect.Position,
            PreferredPosition = prospect.PreferredPosition,
            SecondaryPositions = prospect.SecondaryPositions.ToList(),
            CurrentOVR = prospect.CurrentOVR,
            PotentialMin = prospect.PotentialMin,
            PotentialMax = prospect.PotentialMax,
            HiddenTruePotential = prospect.HiddenTruePotential,
            Traits = prospect.Traits.ToList(),
            Personality = prospect.Personality,
            DevelopmentRate = prospect.DevelopmentRate,
            PotentialTier = prospect.PotentialTier,
            MarketValue = prospect.SigningCost,
            WeeklyWage = prospect.WeeklyWage > 0 ? prospect.WeeklyWage : CalculateWeeklyWage(prospect),
            ClubId = academy.ClubId,
            ClubName = academy.ClubName,
            ScoutReport = prospect.ScoutNotes,
            IntakeSeason = GetSeasonStartYear(season)
        };
        player.MarketValue = YouthMarketValueCalculator.CalculateMarketValue(player, academy);
        return player;
    }

    private static YouthPotentialTier PickPotentialTier(YouthScoutRating rating, bool isFocusedMatch, Random random)
    {
        var wonderkidChance = 0.05;
        var eliteChance = 0.18;
        var ratingBoost = rating switch
        {
            YouthScoutRating.EliteScout => 0.05,
            YouthScoutRating.SeniorScout => 0.025,
            YouthScoutRating.RegionalScout => 0.01,
            _ => 0
        };
        if (isFocusedMatch)
        {
            wonderkidChance += 0.025;
            eliteChance += 0.06;
        }

        wonderkidChance += ratingBoost;
        eliteChance += ratingBoost * 1.5;

        var roll = random.NextDouble();
        if (roll < wonderkidChance)
        {
            return YouthPotentialTier.GenerationalTalent;
        }

        if (roll < wonderkidChance + eliteChance)
        {
            return YouthPotentialTier.EliteProspect;
        }

        if (roll < wonderkidChance + eliteChance + 0.28)
        {
            return YouthPotentialTier.ExcitingProspect;
        }

        return roll < 0.78 ? YouthPotentialTier.GoodProspect : YouthPotentialTier.CommonProspect;
    }

    private static (int Min, int Max) GetPotentialRange(YouthPotentialTier tier, bool isFocusedMatch, Random random)
    {
        var range = tier switch
        {
            YouthPotentialTier.GenerationalTalent => (random.Next(90, 94), random.Next(95, 100)),
            YouthPotentialTier.EliteProspect => (random.Next(86, 89), random.Next(91, 96)),
            YouthPotentialTier.ExcitingProspect => (random.Next(80, 84), random.Next(86, 91)),
            YouthPotentialTier.GoodProspect => (random.Next(73, 77), random.Next(80, 86)),
            _ => (random.Next(60, 66), random.Next(70, 78))
        };
        return isFocusedMatch
            ? (Math.Min(99, range.Item1 + 1), Math.Min(99, range.Item2 + 1))
            : range;
    }

    private static int GetCurrentOverallBonus(YouthPotentialTier tier, YouthScoutRating rating, bool isFocusedMatch, Random random)
    {
        var tierBonus = tier switch
        {
            YouthPotentialTier.GenerationalTalent => random.Next(8, 13),
            YouthPotentialTier.EliteProspect => random.Next(6, 11),
            YouthPotentialTier.ExcitingProspect => random.Next(4, 9),
            YouthPotentialTier.GoodProspect => random.Next(2, 7),
            _ => random.Next(0, 5)
        };
        var scoutBonus = rating switch
        {
            YouthScoutRating.EliteScout => 3,
            YouthScoutRating.SeniorScout => 2,
            YouthScoutRating.RegionalScout => 1,
            _ => 0
        };
        return tierBonus + scoutBonus + (isFocusedMatch ? 1 : 0);
    }

    private static int GetProspectCount(YouthScoutRating rating, Random random)
    {
        return rating switch
        {
            YouthScoutRating.EliteScout => random.Next(6, 9),
            YouthScoutRating.SeniorScout => random.Next(5, 8),
            YouthScoutRating.RegionalScout => random.Next(4, 7),
            _ => random.Next(3, 6)
        };
    }

    private static decimal CalculateSigningCost(YouthScoutProspect prospect)
    {
        var baseCost = prospect.PotentialMax switch
        {
            >= 94 => 6_500_000m,
            >= 90 => 4_000_000m,
            >= 86 => 2_500_000m,
            >= 80 => 1_200_000m,
            _ => 500_000m
        };
        baseCost += Math.Max(0, prospect.CurrentOVR - 55) * 120_000m;
        if (prospect.Age <= 16)
        {
            baseCost += 350_000m;
        }

        return Math.Clamp(Math.Round(baseCost / 50_000m, 0) * 50_000m, 500_000m, 8_000_000m);
    }

    private static decimal CalculateWeeklyWage(YouthScoutProspect prospect)
    {
        var baseWage = prospect.PotentialMax switch
        {
            >= 94 => 12_000m,
            >= 90 => 8_000m,
            >= 86 => 5_000m,
            >= 80 => 3_000m,
            _ => 1_500m
        };
        baseWage += Math.Max(0, prospect.CurrentOVR - 55) * 350m;
        if (prospect.Age <= 16)
        {
            baseWage *= 0.85m;
        }

        return Math.Clamp(Math.Round(baseWage / 500m, 0) * 500m, 1_000m, 18_000m);
    }


    private static List<PlayerTrait> PickTraits(string slot, YouthPotentialTier tier, IReadOnlyList<PlayerTrait> countryBiases, Random random)
    {
        var candidates = slot switch
        {
            "GK" => new[] { PlayerTrait.OneOnOnes, PlayerTrait.RushesOutOfGoal, PlayerTrait.Puncher },
            "CB" => new[] { PlayerTrait.Interceptor, PlayerTrait.AerialThreat, PlayerTrait.DivesIntoTackles },
            "LB" or "RB" => new[] { PlayerTrait.Engine, PlayerTrait.EarlyCrosser, PlayerTrait.Rapid },
            "CDM" => new[] { PlayerTrait.TeamPlayer, PlayerTrait.DivesIntoTackles, PlayerTrait.LongPasser },
            "CM" => new[] { PlayerTrait.BoxToBox, PlayerTrait.Playmaker, PlayerTrait.PressResistant },
            "CAM" => new[] { PlayerTrait.Playmaker, PlayerTrait.Flair, PlayerTrait.TechnicalDribbler },
            "LW" or "RW" => new[] { PlayerTrait.Rapid, PlayerTrait.TechnicalDribbler, PlayerTrait.Flair },
            "ST" => new[] { PlayerTrait.ClinicalFinisher, PlayerTrait.PowerHeader, PlayerTrait.TriesToBeatOffsideTrap },
            _ => Array.Empty<PlayerTrait>()
        };
        var chance = tier switch
        {
            YouthPotentialTier.GenerationalTalent => 0.85,
            YouthPotentialTier.EliteProspect => 0.65,
            YouthPotentialTier.ExcitingProspect => 0.45,
            _ => 0.22
        };
        var traits = candidates.Where(_ => random.NextDouble() < chance).Take(2).ToList();
        if (countryBiases.Count > 0 && random.NextDouble() < 0.45)
        {
            traits.Add(countryBiases[random.Next(countryBiases.Count)]);
        }

        return traits.Distinct().Take(2).ToList();
    }

    private static string CreateScoutNotes(YouthScoutProspect prospect, int index)
    {
        if (prospect.PotentialTier == YouthPotentialTier.GenerationalTalent)
        {
            return "Exceptional vision and creativity. Potential future superstar.";
        }

        if (prospect.PotentialTier == YouthPotentialTier.EliteProspect)
        {
            return "High ceiling prospect with the tools to become a top-level player.";
        }

        if (prospect.Traits.Contains(PlayerTrait.Rapid))
        {
            return "Elite pace for his age and dangerous in transition.";
        }

        if (prospect.Traits.Contains(PlayerTrait.Playmaker))
        {
            return "Technically gifted creator with strong passing instincts.";
        }

        return index % 2 == 0
            ? "Raw but physically impressive. Needs academy minutes and focused development."
            : "Could become a useful first-team player with the right pathway.";
    }

    private static double GetAiProspectScore(Team team, YouthScoutProspect prospect)
    {
        var depth = team.Players.Concat(team.Substitutes).Count(player => player.Position == prospect.Position);
        var score = prospect.PotentialMax + prospect.CurrentOVR * 0.35;
        if (depth <= 4)
        {
            score += 10;
        }

        if (IsWonderkidClub(team.Name) && prospect.PotentialTier >= YouthPotentialTier.EliteProspect)
        {
            score += 14;
        }

        return score;
    }

    private static bool IsWonderkidClub(string clubName)
    {
        return clubName.Contains("Real Madrid", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Barcelona", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("PSG", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Paris Saint-Germain", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Bayern", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Chelsea", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Manchester City", StringComparison.OrdinalIgnoreCase) ||
            clubName.Contains("Man City", StringComparison.OrdinalIgnoreCase);
    }

    private static YouthDevelopmentRate PickDevelopmentRate(YouthPotentialTier tier, YouthScoutRating rating, YouthDevelopmentRate developmentLean, Random random)
    {
        var roll = random.NextDouble();
        if (rating == YouthScoutRating.EliteScout)
        {
            roll -= 0.08;
        }

        if (developmentLean == YouthDevelopmentRate.Fast)
        {
            roll -= 0.05;
        }
        else if (developmentLean == YouthDevelopmentRate.Slow)
        {
            roll += 0.05;
        }

        return tier switch
        {
            YouthPotentialTier.GenerationalTalent => roll < 0.50 ? YouthDevelopmentRate.Explosive : YouthDevelopmentRate.Fast,
            YouthPotentialTier.EliteProspect => roll < 0.22 ? YouthDevelopmentRate.Explosive : roll < 0.76 ? YouthDevelopmentRate.Fast : YouthDevelopmentRate.Normal,
            YouthPotentialTier.ExcitingProspect => roll < 0.48 ? YouthDevelopmentRate.Fast : YouthDevelopmentRate.Normal,
            YouthPotentialTier.CommonProspect => roll < 0.24 ? YouthDevelopmentRate.Slow : YouthDevelopmentRate.Normal,
            _ => roll < 0.20 ? YouthDevelopmentRate.Fast : YouthDevelopmentRate.Normal
        };
    }

    private static YouthPersonality PickPersonality(Random random)
    {
        var values = Enum.GetValues<YouthPersonality>();
        return values[random.Next(values.Length)];
    }

    private static Position MapPosition(string exactPosition)
    {
        return exactPosition switch
        {
            "GK" => Position.Goalkeeper,
            "CB" or "LB" or "RB" => Position.Defender,
            "CDM" or "CM" or "CAM" => Position.Midfielder,
            _ => Position.Forward
        };
    }

    private static List<string> GetSecondaryPositions(string exactPosition)
    {
        return exactPosition switch
        {
            "RB" => ["RWB", "LB"],
            "LB" => ["LWB", "RB"],
            "CB" => ["CDM"],
            "CDM" => ["CM", "CB"],
            "CM" => ["CDM", "CAM"],
            "CAM" => ["CM", "LW", "RW"],
            "RW" => ["LW", "RM", "ST"],
            "LW" => ["RW", "LM", "ST"],
            "ST" => ["CF"],
            "CF" => ["ST", "CAM"],
            _ => []
        };
    }

    private static IEnumerable<string> GetActiveFocusPositions(YouthScoutAssignment assignment)
    {
        if (assignment.PrimaryFocus != YouthScoutPositionFocus.AnyPosition)
        {
            yield return assignment.PrimaryFocus.ToString();
        }
    }

    private static string FormatFocusLabel(YouthScoutPositionFocus primaryFocus)
    {
        return FormatFocus(primaryFocus);
    }

    private static string FormatFocus(YouthScoutPositionFocus focus)
    {
        return focus == YouthScoutPositionFocus.AnyPosition ? "None" : focus.ToString();
    }

    private static YouthScoutRating GetDefaultScoutRating(YouthAcademy academy, int index)
    {
        return academy.AcademyLevel switch
        {
            AcademyLevel.Elite => index == 0 ? YouthScoutRating.EliteScout : index == 1 ? YouthScoutRating.SeniorScout : YouthScoutRating.RegionalScout,
            AcademyLevel.Gold => index == 0 ? YouthScoutRating.SeniorScout : YouthScoutRating.RegionalScout,
            AcademyLevel.Silver => index == 0 ? YouthScoutRating.RegionalScout : YouthScoutRating.JuniorScout,
            _ => YouthScoutRating.JuniorScout
        };
    }

    private static YouthScoutCountry[] GetDefaultCountries(YouthAcademy academy)
    {
        return academy.AcademyLevel switch
        {
            AcademyLevel.Elite => [Countries[1], Countries[2], Countries[3]],
            AcademyLevel.Gold => [Countries[0], Countries[4], Countries[6]],
            _ => [Countries[0], Countries[5], Countries[7]]
        };
    }

    private static YouthScoutCountry? FindCountry(string countryName)
    {
        return Countries.FirstOrDefault(country => country.Name.Equals(countryName, StringComparison.OrdinalIgnoreCase));
    }

    private static void NormalizeAssignment(YouthScoutAssignment assignment, int index)
    {
        assignment.ScoutId = string.IsNullOrWhiteSpace(assignment.ScoutId) ? $"scout-{index + 1}" : assignment.ScoutId;
        assignment.ScoutName = string.IsNullOrWhiteSpace(assignment.ScoutName) ? $"Scout #{index + 1}" : assignment.ScoutName;
        assignment.RequiredMatches = assignment.RequiredMatches <= 0 ? RequiredMatchesPerReport : assignment.RequiredMatches;
        assignment.ProgressMatches = Math.Clamp(assignment.ProgressMatches, 0, assignment.RequiredMatches);
        if (!Enum.IsDefined(assignment.PrimaryFocus) || !PositionFocuses.Contains(assignment.PrimaryFocus))
        {
            assignment.PrimaryFocus = YouthScoutPositionFocus.AnyPosition;
        }

        assignment.SecondaryFocus = YouthScoutPositionFocus.AnyPosition;

        var country = FindCountry(assignment.AssignedCountry) ?? Countries[0];
        assignment.AssignedCountry = country.Name;
        assignment.CountryCode = country.Code;
        assignment.FlagImagePath = country.FlagImagePath;
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static int GetSeasonStartYear(string season)
    {
        var normalized = season.Replace('/', '-');
        var first = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(first, out var value) ? value : DateTime.Now.Year;
    }

    private static double GetDeterministicRoll(string clubName, string prospectId, int currentRound)
    {
        var seed = Math.Abs(HashCode.Combine(clubName, prospectId, currentRound));
        return new Random(seed).NextDouble();
    }

    private static string FormatMoney(decimal value)
    {
        return value >= 1_000_000m
            ? $"€{value / 1_000_000m:0.#}M"
            : $"€{value / 1_000m:0}K";
    }

    private static string FormatWeeklyWage(decimal value)
    {
        return value >= 1_000_000m
            ? $"€{value / 1_000_000m:0.#}M/w"
            : $"€{value / 1_000m:0.#}K/w";
    }

    private static string NormalizeId(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static YouthScoutCountry Country(string name, string code, string slug)
    {
        return new YouthScoutCountry(name, code, $"/Assets/Flags/{slug}.png");
    }

    private sealed record CountryProfile(
        string[] FirstNames,
        string[] LastNames,
        PlayerTrait[] TraitBiases,
        YouthDevelopmentRate DevelopmentLean);
}
