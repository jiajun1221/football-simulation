using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;
using Xunit.Abstractions;
using System.Text.Json.Nodes;
using System.Diagnostics;

namespace FootballSimulation.Console.Tests;

public class MentalSystemTests
{
    private readonly ITestOutputHelper _output;

    public MentalSystemTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_FeatureOff_PreservesSeededMatchExactly()
    {
        var seedData = new SeedDataService();
        var firstTeams = seedData.CreateDemoTeams();
        var secondTeams = seedData.CreateDemoTeams();
        SetExtremeMentalState(secondTeams.HomeTeam, secondTeams.AwayTeam);
        var engine = new MatchEngine();

        var baseline = engine.SimulateMatch(firstTeams.HomeTeam, firstTeams.AwayTeam, 741,
            options: new MatchSimulationOptions { EnableMentalSystem = false });
        var comparison = engine.SimulateMatch(secondTeams.HomeTeam, secondTeams.AwayTeam, 741,
            options: new MatchSimulationOptions { EnableMentalSystem = false });

        Assert.Equal(baseline.HomeScore, comparison.HomeScore);
        Assert.Equal(baseline.AwayScore, comparison.AwayScore);
        Assert.Equal(baseline.HomeStats.Fouls, comparison.HomeStats.Fouls);
        Assert.Equal(baseline.AwayStats.Fouls, comparison.AwayStats.Fouls);
        Assert.Equal(baseline.Events.Select(e => (e.Minute, e.EventType, e.Description)),
            comparison.Events.Select(e => (e.Minute, e.EventType, e.Description)));
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_PersonalityIsStableBoundedAndProducesArchetype()
    {
        var first = CreatePlayer("p-1", "Alex Example", "England");
        var second = CreatePlayer("p-1", "Alex Example", "England");
        first.Traits.Add(PlayerTrait.Leadership);
        second.Traits.Add(PlayerTrait.Leadership);

        PersonalityService.EnsureInitialized(first, "Test FC");
        PersonalityService.EnsureInitialized(second, "Test FC");

        Assert.Equal(first.MentalState.Personality.Leadership, second.MentalState.Personality.Leadership);
        Assert.InRange(first.MentalState.Personality.Leadership, 1, 20);
        Assert.False(string.IsNullOrWhiteSpace(PersonalityService.GetArchetype(first)));
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_ArchetypeBoundariesMatchAllConfiguredDefinitions()
    {
        var cases = new (string Name, Action<PersonalityProfile> Configure)[]
        {
            ("Captain Material", p => { p.Leadership = 16; p.Resilience = 13; }),
            ("Hothead", p => { p.Temperament = 6; p.Sportsmanship = 9; }),
            ("Mercenary", p => { p.Ambition = 16; p.Loyalty = 7; }),
            ("Diva", p => { p.Ego = 16; p.Sociability = 9; }),
            ("Club Man", p => { p.Loyalty = 16; p.Professionalism = 12; }),
            ("Quiet Professional", p => { p.Professionalism = 15; p.Temperament = 13; }),
            ("Big-Game Player", p => { p.BigMatchTemperament = 15; p.Resilience = 11; }),
            ("Fragile Talent", p => { p.Resilience = 8; p.BigMatchTemperament = 9; }),
            ("Sensitive Soul", p => { p.Ego = 14; p.Resilience = 10; }),
            ("Enforcer", p => { p.Sportsmanship = 8; p.Temperament = 11; }),
            ("Social Glue", p => { p.Sociability = 14; p.Adaptability = 12; }),
            ("Lone Wolf", p => { p.Sociability = 8; p.Ego = 13; }),
            ("Driven Professional", p => { p.Ambition = 14; p.Professionalism = 12; }),
            ("Humble Servant", p => { p.Loyalty = 13; p.Ego = 8; }),
            ("Free Spirit", p => { p.Professionalism = 8; p.Sociability = 12; }),
            ("Passenger", p => { p.Professionalism = 8; p.Ambition = 9; }),
            ("Gentleman", p => { p.Sportsmanship = 16; p.Temperament = 11; })
        };

        foreach (var item in cases)
        {
            var profile = CreateNeutralProfile();
            item.Configure(profile);
            Assert.Equal(item.Name, PersonalityService.GetArchetype(profile));
        }
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_ArchetypeCheckOrderKeepsExistingDefinitionsFirst()
    {
        var captainAndBigGame = CreateNeutralProfile();
        captainAndBigGame.Leadership = 16;
        captainAndBigGame.Resilience = 13;
        captainAndBigGame.BigMatchTemperament = 15;
        Assert.Equal("Captain Material", PersonalityService.GetArchetype(captainAndBigGame));

        var hotheadAndEnforcer = CreateNeutralProfile();
        hotheadAndEnforcer.Temperament = 6;
        hotheadAndEnforcer.Sportsmanship = 8;
        Assert.Equal("Hothead", PersonalityService.GetArchetype(hotheadAndEnforcer));

        var quietAndBigGame = CreateNeutralProfile();
        quietAndBigGame.Professionalism = 15;
        quietAndBigGame.Temperament = 13;
        quietAndBigGame.BigMatchTemperament = 15;
        quietAndBigGame.Resilience = 11;
        Assert.Equal("Quiet Professional", PersonalityService.GetArchetype(quietAndBigGame));
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_FallbackUsesExtremeTraitAndReservesBalancedForTightProfiles()
    {
        Assert.Equal("Balanced Character", PersonalityService.GetArchetype(CreateNeutralProfile()));

        var resilient = CreateNeutralProfile();
        resilient.Resilience = 20;
        Assert.Equal("Resilient Type", PersonalityService.GetArchetype(resilient));

        var lowEgo = CreateNeutralProfile();
        lowEgo.Ego = 1;
        Assert.Equal("Low Ego Type", PersonalityService.GetArchetype(lowEgo));
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_ArchetypeDistributionMeetsDiversityTargets()
    {
        var seedData = new SeedDataService().CreateDemoTeams();
        foreach (var player in seedData.HomeTeam.AllPlayers)
            PersonalityService.EnsureInitialized(player, seedData.HomeTeam.Name);
        foreach (var player in seedData.AwayTeam.AllPlayers)
            PersonalityService.EnsureInitialized(player, seedData.AwayTeam.Name);

        var writer = new StringWriter();
        var report = ArchetypeDistributionReport.Generate(
            seedData.HomeTeam.AllPlayers.Concat(seedData.AwayTeam.AllPlayers),
            output: writer);
        _output.WriteLine(writer.ToString());

        var balanced = report.RandomSamplePercentages.GetValueOrDefault("Balanced Character");
        Assert.True(balanced < 20, $"Balanced Character was {balanced:0.00}%");
        Assert.All(report.RandomSamplePercentages.Where(item => item.Key != "Balanced Character"), item =>
            Assert.True(item.Value < 15, $"{item.Key} was {item.Value:0.00}%"));
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_EmotionDecayMovesTowardBaselineAndCapsEffects()
    {
        var player = CreatePlayer("p-2", "Sam Example", "France");
        PersonalityService.EnsureInitialized(player, "Test FC");
        player.MentalState.Emotions.Confidence = 100;
        player.MentalState.Emotions.Frustration = 100;
        player.MentalState.MatchEffectsEnabled = true;

        EmotionService.DecayAfterCalendarSlot(player, 2);
        var modifier = EmotionService.GetActionModifier(player, MatchActionType.Passing);

        Assert.True(player.MentalState.Emotions.Confidence < 100);
        Assert.True(player.MentalState.Emotions.Frustration < 100);
        Assert.InRange(modifier, -MentalSystemConstants.MaximumGeneralActionModifier, MentalSystemConstants.MaximumGeneralActionModifier);
    }

    [Fact]
    [Trait("Category", "MentalSystem")]
    public void MentalSystem_ChemistryUsesOnlyAdjacentLinksAndIsCapped()
    {
        var team = CreateTeam("Chemistry FC");
        DressingRoomService.EnsureTeam(team);
        var service = new FormationChemistryService();
        var links = service.GetAdjacentLinks(team.Players);
        foreach (var link in links)
        {
            var relationship = new RelationshipService().GetOrCreate(team, link.First, link.Second);
            relationship.Friendship = 100;
            relationship.Respect = 100;
            relationship.MatchesPlayedTogether = 100;
        }

        service.ApplyMatchSnapshot(team);

        Assert.NotEmpty(links);
        Assert.All(team.Players, player => Assert.InRange(player.MentalState.MatchChemistryModifier,
            -MentalSystemConstants.MaximumChemistryModifier, MentalSystemConstants.MaximumChemistryModifier));
    }

    [Fact]
    [Trait("Category", "MentalSave")]
    public void MentalSave_RoundTripsAndOldVersionGetsDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"football-mental-{Guid.NewGuid():N}");
        var service = new SaveGameService(directory);
        var team = CreateTeam("Save FC");
        var opponent = CreateTeam("Other FC");
        var league = new League { Name = "Test League", Season = "2026-27", Teams = [team, opponent] };
        PersonalityService.EnsureInitialized(team.Players[0], team.Name);
        team.Players[0].MentalState.Emotions.Confidence = 73;

        try
        {
            service.SaveGame(1, SaveGameService.CreateSaveData(league, team));
            var loaded = service.LoadGame(1)!;
            Assert.Equal(SaveGameService.CurrentSaveVersion, loaded.SaveVersion);
            Assert.Equal(73, loaded.Teams[0].Players[0].MentalState.Emotions.Confidence);

            service.SaveGame(2, loaded);
            var slotPath = service.GetSlotPath(2);
            var oldJson = JsonNode.Parse(File.ReadAllText(slotPath))!;
            RemoveMentalProperties(oldJson);
            oldJson["SaveVersion"] = 8;
            File.WriteAllText(slotPath, oldJson.ToJsonString());
            var oldCompatible = service.LoadGame(2)!;
            Assert.NotNull(oldCompatible.Teams[0].DressingRoom);
            Assert.NotNull(oldCompatible.Teams[0].Players[0].MentalState);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    [Trait("Category", "MentalBenchmark")]
    public void MentalBenchmark_FiveHundredMatchesRemainWithinTolerance()
    {
        VerifyDisabledEquivalence(500);
        var baseline = RunBenchmark(false, 500);
        var enabled = RunBenchmark(true, 500);
        _output.WriteLine($"OFF {baseline}");
        _output.WriteLine($"ON  {enabled}");

        AssertWithinFivePercent(baseline.GoalsPerMatch, enabled.GoalsPerMatch, 0.05, "goals per match");
        AssertWithinFivePercent(baseline.FoulsPerMatch, enabled.FoulsPerMatch, 0.05, "fouls per match");
        AssertWithinFivePercent(baseline.CardsPerMatch, enabled.CardsPerMatch, 0.05, "cards per match");
        AssertWithinFivePercent(baseline.PassAccuracy, enabled.PassAccuracy, 0.05, "pass accuracy");
        AssertWithinFivePercent(baseline.HomeWinRate, enabled.HomeWinRate, 0.05, "home win rate");
        AssertWithinFivePercent(baseline.DrawRate, enabled.DrawRate, 0.05, "draw rate");
        AssertWithinFivePercent(baseline.AwayWinRate, enabled.AwayWinRate, 0.05, "away win rate");
        Assert.True(enabled.ElapsedMilliseconds <= baseline.ElapsedMilliseconds * 1.25,
            $"simulation time: baseline {baseline.ElapsedMilliseconds}ms, enabled {enabled.ElapsedMilliseconds}ms");
    }

    private static void VerifyDisabledEquivalence(int count)
    {
        for (var seed = 1; seed <= count; seed++)
        {
            var seedData = new SeedDataService();
            var baselineTeams = seedData.CreateDemoTeams();
            var alteredTeams = seedData.CreateDemoTeams();
            SetExtremeMentalState(alteredTeams.HomeTeam, alteredTeams.AwayTeam);
            var baseline = new MatchEngine().SimulateMatch(baselineTeams.HomeTeam, baselineTeams.AwayTeam, seed,
                options: new MatchSimulationOptions { EnableMentalSystem = false });
            var altered = new MatchEngine().SimulateMatch(alteredTeams.HomeTeam, alteredTeams.AwayTeam, seed,
                options: new MatchSimulationOptions { EnableMentalSystem = false });
            Assert.Equal((baseline.HomeScore, baseline.AwayScore, baseline.HomeStats.Fouls, baseline.AwayStats.Fouls,
                baseline.HomeStats.YellowCards, baseline.AwayStats.YellowCards),
                (altered.HomeScore, altered.AwayScore, altered.HomeStats.Fouls, altered.AwayStats.Fouls,
                altered.HomeStats.YellowCards, altered.AwayStats.YellowCards));
            Assert.Equal(baseline.Events.Select(e => (e.Minute, e.EventType, e.Description)),
                altered.Events.Select(e => (e.Minute, e.EventType, e.Description)));
        }
    }

    private static void SetExtremeMentalState(Team homeTeam, Team awayTeam)
    {
        foreach (var player in homeTeam.AllPlayers.Concat(awayTeam.AllPlayers))
        {
            player.MentalState.Emotions.Confidence = 100;
            player.MentalState.Emotions.Frustration = 100;
            player.MentalState.Emotions.Anxiety = 100;
            player.MentalState.MatchChemistryModifier = MentalSystemConstants.MaximumChemistryModifier;
        }
    }

    private static BenchmarkResult RunBenchmark(bool enabled, int count)
    {
        var stopwatch = Stopwatch.StartNew();
        double goals = 0, fouls = 0, cards = 0, passing = 0;
        var home = 0; var draws = 0; var away = 0;
        for (var seed = 1; seed <= count; seed++)
        {
            var teams = new SeedDataService().CreateDemoTeams();
            var match = new MatchEngine().SimulateMatch(teams.HomeTeam, teams.AwayTeam, seed,
                options: new MatchSimulationOptions { EnableMentalSystem = enabled });
            goals += match.HomeScore + match.AwayScore;
            fouls += match.HomeStats.Fouls + match.AwayStats.Fouls;
            cards += match.HomeStats.YellowCards + match.AwayStats.YellowCards + match.HomeStats.RedCards + match.AwayStats.RedCards;
            passing += (match.HomeStats.PassAccuracyPercentage + match.AwayStats.PassAccuracyPercentage) / 2;
            if (match.HomeScore > match.AwayScore) home++; else if (match.HomeScore == match.AwayScore) draws++; else away++;
        }
        stopwatch.Stop();
        return new BenchmarkResult(goals / count, fouls / count, cards / count, passing / count,
            home / (double)count, draws / (double)count, away / (double)count, stopwatch.ElapsedMilliseconds);
    }

    private static void AssertWithinFivePercent(double baseline, double actual, double tolerance, string label)
    {
        var relative = baseline == 0 ? Math.Abs(actual) : Math.Abs(actual - baseline) / baseline;
        Assert.True(relative <= tolerance, $"{label}: baseline {baseline:0.###}, enabled {actual:0.###}, difference {relative:P2}");
    }

    private static void RemoveMentalProperties(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("MentalState");
            obj.Remove("DressingRoom");
            foreach (var child in obj.Select(item => item.Value).Where(value => value is not null).ToList())
                RemoveMentalProperties(child!);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(value => value is not null).ToList()) RemoveMentalProperties(child!);
        }
    }

    private static Team CreateTeam(string name)
    {
        var slots = new[] { "GK", "LB", "CB", "CB", "RB", "CDM", "CM", "CAM", "LW", "RW", "ST" };
        return new Team
        {
            Name = name,
            Formation = "4-3-3",
            Players = slots.Select((slot, index) => new Player
            {
                PlayerId = $"{name}-{index}", Name = $"{name} Player {index + 1}", SquadNumber = index + 1,
                Position = slot == "GK" ? Position.Goalkeeper : slot is "LB" or "CB" or "RB" ? Position.Defender : slot is "CDM" or "CM" or "CAM" ? Position.Midfielder : Position.Forward,
                PreferredPosition = slot, AssignedPosition = slot, Nationality = index % 2 == 0 ? "England" : "France",
                OverallRating = 75, BaseOverallRating = 75, Pace = 75, Shooting = 75, Dribbling = 75,
                Defending = 75, Physical = 75, Attack = 75, Defense = 75, Passing = 75, Finishing = 75,
                DisciplineRating = 60, IsStarter = true, IsOnPitch = true, Morale = 50, CurrentForm = 50
            }).ToList()
        };
    }

    private static Player CreatePlayer(string id, string name, string nationality) => new()
    {
        PlayerId = id, Name = name, Nationality = nationality, OverallRating = 75, Passing = 75,
        Shooting = 75, Finishing = 75, Attack = 75, Defense = 75, Morale = 50, CurrentForm = 50
    };

    private static PersonalityProfile CreateNeutralProfile() => new()
    {
        Professionalism = 10,
        Temperament = 10,
        Leadership = 10,
        Resilience = 10,
        BigMatchTemperament = 10,
        Ambition = 10,
        Loyalty = 10,
        Sportsmanship = 10,
        Ego = 10,
        Adaptability = 10,
        Sociability = 10,
        InitializationVersion = 1
    };

    private sealed record BenchmarkResult(double GoalsPerMatch, double FoulsPerMatch, double CardsPerMatch,
        double PassAccuracy, double HomeWinRate, double DrawRate, double AwayWinRate, long ElapsedMilliseconds);
}
