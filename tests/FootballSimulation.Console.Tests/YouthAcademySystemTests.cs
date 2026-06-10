using FootballSimulation.Engine;
using FootballSimulation.Data;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class YouthAcademySystemTests
{
    [Fact]
    public void CreateLeague_GeneratesYouthAcademyForEveryClub()
    {
        var league = CreateLeague();

        Assert.Equal(league.Teams.Count, league.YouthAcademies.Count);
        Assert.All(league.YouthAcademies, academy =>
        {
            Assert.InRange(academy.YouthPlayers.Count, 4, 8);
            Assert.Equal(3, academy.ScoutAssignments.Count);
            Assert.All(academy.YouthPlayers, player =>
            {
                Assert.InRange(player.Age, 15, 18);
                Assert.InRange(player.CurrentOVR, 45, 68);
                Assert.InRange(player.PotentialMin, 60, 99);
                Assert.InRange(player.PotentialMax, player.PotentialMin, 99);
                Assert.False(string.IsNullOrWhiteSpace(player.PreferredPosition));
                Assert.False(string.IsNullOrWhiteSpace(player.ScoutReport));
            });
        });
    }

    [Fact]
    public void EliteAcademy_HasChanceToProduceEliteProspects()
    {
        var league = CreateLeague();
        var eliteAcademy = league.YouthAcademies.First(academy => academy.AcademyLevel == AcademyLevel.Elite);

        Assert.Equal(AcademyLevel.Elite, eliteAcademy.AcademyLevel);
        Assert.True(eliteAcademy.Reputation >= 85);
        Assert.Contains(league.YouthAcademies.SelectMany(academy => academy.YouthPlayers), player => player.PotentialMax >= 86);
    }

    [Fact]
    public void ApplyDevelopment_GrowsFastHighPotentialProspect()
    {
        var league = CreateLeague();
        var academy = league.YouthAcademies[0];
        var prospect = academy.YouthPlayers[0];
        prospect.CurrentOVR = 58;
        prospect.HiddenTruePotential = 94;
        prospect.PotentialMin = 88;
        prospect.PotentialMax = 96;
        prospect.PotentialTier = YouthPotentialTier.GenerationalTalent;
        prospect.DevelopmentRate = YouthDevelopmentRate.Explosive;
        prospect.Personality = YouthPersonality.Determined;
        var startingOverall = prospect.CurrentOVR;

        new YouthAcademyService().ApplyDevelopment(league, months: 6);

        Assert.True(prospect.CurrentOVR > startingOverall);
    }

    [Fact]
    public void ApplyDevelopment_UsesDevelopmentRateForOvrGrowth()
    {
        var academy = new YouthAcademy
        {
            ClubName = "Chelsea",
            AcademyLevel = AcademyLevel.Silver,
            TrainingFocus = YouthTrainingFocus.Balanced
        };
        var slowProspect = CreateComparableProspect("Slow Prospect", YouthDevelopmentRate.Slow);
        var explosiveProspect = CreateComparableProspect("Explosive Prospect", YouthDevelopmentRate.Explosive);
        var service = new YouthDevelopmentService();

        service.ApplyDevelopment(slowProspect, academy, months: 6);
        service.ApplyDevelopment(explosiveProspect, academy, months: 6);

        Assert.True(explosiveProspect.CurrentOVR > slowProspect.CurrentOVR);
        Assert.True(explosiveProspect.DevelopmentProgress > slowProspect.DevelopmentProgress ||
            explosiveProspect.CurrentOVR - slowProspect.CurrentOVR > 1);
    }

    [Fact]
    public void PromoteYouthPlayer_AddsProspectToSeniorSubstitutes()
    {
        var league = CreateLeague();
        var team = league.Teams[0];
        var academy = new YouthAcademyService().GetAcademy(league, team.Name);
        var prospect = academy.YouthPlayers[0];
        prospect.Age = 16;
        prospect.CurrentOVR = 58;
        prospect.HiddenTruePotential = 86;

        var result = new YouthAcademyService().PromoteYouthPlayer(league, team, prospect.PlayerId);

        Assert.True(result.Success, result.Message);
        Assert.Contains(team.Substitutes, player => player.PlayerId == prospect.PlayerId);
        Assert.Contains(team.Substitutes, player => player.Role == PlayerRole.Prospect);
        Assert.DoesNotContain(academy.YouthPlayers, player => player.PlayerId == prospect.PlayerId);
    }

    [Fact]
    public void YouthScout_GeneratesCountryReportAfterThreeClubMatches()
    {
        var league = CreateLeague();
        var selectedTeam = league.Teams[0];
        var service = new YouthScoutService();
        var academy = new YouthAcademyService().GetAcademy(league, selectedTeam.Name);
        service.EnsureScoutNetwork(academy);
        var assignment = academy.ScoutAssignments.First();
        var assignResult = service.AssignScoutingPlan(
            academy,
            assignment.ScoutId,
            "Brazil",
            YouthScoutPositionFocus.CB);
        Assert.True(assignResult.Success, assignResult.Message);

        service.AdvanceScoutingAfterClubMatch(league, selectedTeam, currentRound: 1);
        service.AdvanceScoutingAfterClubMatch(league, selectedTeam, currentRound: 2);
        var reports = service.AdvanceScoutingAfterClubMatch(league, selectedTeam, currentRound: 3);
        var focusedReport = reports.Single(report => report.ScoutId == assignment.ScoutId);
        var focusedCount = focusedReport.Prospects.Count(prospect => prospect.PreferredPosition == "CB");

        Assert.Equal(3, academy.ScoutAssignments.Count);
        Assert.NotEmpty(reports);
        Assert.True(focusedCount >= (int)Math.Ceiling(focusedReport.Prospects.Count * 0.70), "Focused reports should mostly match the selected position.");
        Assert.All(reports, report =>
        {
            Assert.InRange(report.Prospects.Count, 3, 8);
            Assert.All(report.Prospects, prospect =>
            {
                Assert.False(string.IsNullOrWhiteSpace(prospect.NationalityName));
                Assert.False(string.IsNullOrWhiteSpace(prospect.PreferredPosition));
                Assert.InRange(prospect.SigningCost, 500_000m, 8_000_000m);
            });
        });
    }

    [Fact]
    public void YouthScout_AssignmentUsesSingleReadableFocus()
    {
        var league = CreateLeague();
        var service = new YouthScoutService();
        var academy = league.YouthAcademies.First(item => item.ScoutAssignments.Any(assignment => assignment.Rating == YouthScoutRating.EliteScout));
        var eliteScout = academy.ScoutAssignments.First(assignment => assignment.Rating == YouthScoutRating.EliteScout);

        var assignResult = service.AssignScoutingPlan(
            academy,
            eliteScout.ScoutId,
            "France",
            YouthScoutPositionFocus.CB);
        service.AdvanceScoutingAfterClubMatch(academy, league.Season, currentRound: 1);
        service.AdvanceScoutingAfterClubMatch(academy, league.Season, currentRound: 2);
        var reports = service.AdvanceScoutingAfterClubMatch(academy, league.Season, currentRound: 3);
        var report = reports.Single(item => item.ScoutId == eliteScout.ScoutId);
        var focusedCount = report.Prospects.Count(prospect => prospect.PreferredPosition == "CB");

        Assert.True(assignResult.Success, assignResult.Message);
        Assert.Equal(YouthScoutPositionFocus.CB, eliteScout.PrimaryFocus);
        Assert.Equal(YouthScoutPositionFocus.AnyPosition, eliteScout.SecondaryFocus);
        Assert.True(focusedCount >= (int)Math.Ceiling(report.Prospects.Count * 0.70));
    }

    [Fact]
    public void YouthScout_SignProspectAddsPlayerToAcademy()
    {
        var league = CreateLeague();
        var selectedTeam = league.Teams[0];
        var transferState = new TransferMarketService().CreateInitialState(league);
        var service = new YouthScoutService();
        var academy = new YouthAcademyService().GetAcademy(league, selectedTeam.Name);
        service.AdvanceScoutingAfterClubMatch(league, selectedTeam, currentRound: 1);
        service.AdvanceScoutingAfterClubMatch(league, selectedTeam, currentRound: 2);
        service.AdvanceScoutingAfterClubMatch(league, selectedTeam, currentRound: 3);
        var report = academy.ScoutReports.First();
        var prospect = report.Prospects.First();
        var startingCount = academy.YouthPlayers.Count;

        var result = service.SignProspect(
            league,
            transferState,
            selectedTeam,
            report.ReportId,
            prospect.ProspectId,
            currentRound: 3);

        Assert.True(result.Success, result.Message);
        Assert.True(prospect.IsSigned);
        Assert.Equal(startingCount + 1, academy.YouthPlayers.Count);
        Assert.Contains(academy.YouthPlayers, player => player.Name == prospect.Name);
    }

    [Fact]
    public void SaveLoad_RestoresYouthAcademiesExactly()
    {
        var saveDirectory = Path.Combine(Path.GetTempPath(), $"football-youth-save-tests-{Guid.NewGuid():N}");
        var saveGameService = new SaveGameService(saveDirectory);
        var league = CreateLeague();
        var selectedTeam = league.Teams[0];
        var selectedAcademy = new YouthAcademyService().GetAcademy(league, selectedTeam.Name);
        selectedAcademy.ScoutFocus = YouthScoutFocus.Winger;
        selectedAcademy.TrainingFocus = YouthTrainingFocus.Technical;
        var expectedPlayerId = selectedAcademy.YouthPlayers[0].PlayerId;
        var expectedPotential = selectedAcademy.YouthPlayers[0].HiddenTruePotential;

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, selectedTeam));

            var loadedData = saveGameService.LoadGame(1);
            var loadedLeague = SaveGameService.CreateLeague(loadedData!);
            var loadedAcademy = new YouthAcademyService().GetAcademy(loadedLeague, selectedTeam.Name);
            var loadedPlayer = loadedAcademy.YouthPlayers.Single(player => player.PlayerId == expectedPlayerId);

            Assert.Equal(SaveGameService.CurrentSaveVersion, loadedData!.SaveVersion);
            Assert.Equal(YouthScoutFocus.Winger, loadedAcademy.ScoutFocus);
            Assert.Equal(YouthTrainingFocus.Technical, loadedAcademy.TrainingFocus);
            Assert.Equal(expectedPotential, loadedPlayer.HiddenTruePotential);
        }
        finally
        {
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartNextSeason_RegeneratesAcademiesForPromotedClubs()
    {
        var leagueEngine = new LeagueEngine();
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(6).ToList();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
        SimulateAllFixtures(leagueEngine, league);
        var transferMarket = new TransferMarketService().CreateInitialState(league);

        var result = new SeasonRolloverService().StartNextSeason(league, selectedTeam, transferMarket);

        Assert.Equal(result.League.Teams.Count, result.League.YouthAcademies.Count);
        Assert.All(result.PromotedClubs, club =>
        {
            var academy = result.League.YouthAcademies.Single(item => item.ClubName == club.Name);
            Assert.NotEmpty(academy.YouthPlayers);
        });
    }

    private static League CreateLeague()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(6).ToList();
        return new LeagueEngine().CreateLeague(definition.LeagueId, definition.Name, definition.Season, teams);
    }

    private static void SimulateAllFixtures(LeagueEngine leagueEngine, League league)
    {
        var safety = 0;
        while (league.Fixtures.Any(fixture => !fixture.IsPlayed))
        {
            var fixture = league.Fixtures
                .Where(item => !item.IsPlayed)
                .OrderBy(item => item.CalendarRound > 0 ? item.CalendarRound : item.RoundNumber)
                .ThenBy(item => item.Competition)
                .First();
            leagueEngine.SimulateFixture(league, fixture);
            safety++;
            if (safety > 500)
            {
                throw new InvalidOperationException("Fixture simulation did not complete.");
            }
        }
    }

    private static YouthPlayer CreateComparableProspect(string name, YouthDevelopmentRate developmentRate)
    {
        return new YouthPlayer
        {
            PlayerId = Guid.NewGuid().ToString("N"),
            Name = name,
            Age = 17,
            Position = Position.Midfielder,
            PreferredPosition = "CM",
            CurrentOVR = 58,
            PotentialMin = 84,
            PotentialMax = 92,
            HiddenTruePotential = 92,
            PotentialTier = YouthPotentialTier.ExcitingProspect,
            Personality = YouthPersonality.Professional,
            DevelopmentRate = developmentRate
        };
    }
}
