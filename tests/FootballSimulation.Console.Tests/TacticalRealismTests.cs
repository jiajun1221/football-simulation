using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class TacticalRealismTests
{
    [Fact]
    public void ClinicalFinisherTrait_IncreasesEffectiveFinishing()
    {
        var calculator = new TeamStrengthCalculator();
        var player = new Player
        {
            Name = "Test Forward",
            Position = Position.Forward,
            Attack = 75,
            Defense = 35,
            Passing = 65,
            Stamina = 80,
            CurrentStamina = 80,
            Finishing = 78,
            CurrentForm = 60,
            Morale = 60
        };

        var baseFinishing = calculator.GetEffectiveFinishing(player);

        player.Traits.Add(PlayerTrait.ClinicalFinisher);
        var traitFinishing = calculator.GetEffectiveFinishing(player);

        Assert.True(traitFinishing > baseFinishing);
    }

    [Fact]
    public void TacticalInsightService_ReturnsOpponentThreatsAndRecommendations()
    {
        var seedDataService = new SeedDataService();
        var selectedTeam = seedDataService.CreateHomeTeam();
        var opponent = seedDataService.CreateAwayTeam();
        opponent.Formation = "4-3-3";
        opponent.Tactics = new TeamTactics
        {
            Mentality = Mentality.Attacking,
            PressingIntensity = 78,
            Width = 65,
            Tempo = 72,
            DefensiveLine = 76
        };
        opponent.Players.First(player => player.Position == Position.Forward).Traits.Add(PlayerTrait.PaceMerchant);

        var service = new TacticalInsightService();

        var insight = service.GenerateInsight(selectedTeam, opponent);

        Assert.NotEmpty(insight.OpponentThreats);
        Assert.NotEmpty(insight.LikelyTactics);
        Assert.NotEmpty(insight.Recommendations);
    }

    [Fact]
    public void TacticalInsightService_DoesNotMarkFullStaminaPlayerAsTired()
    {
        var player = new Player
        {
            Name = "Rested Midfielder",
            Position = Position.Midfielder,
            Stamina = 100,
            SeasonFatigue = 70,
            MatchesPlayedRecently = 4
        };
        var selectedTeam = new Team
        {
            Name = "Selected FC",
            Players = [player],
            Tactics = new TeamTactics()
        };
        var opponent = new Team
        {
            Name = "Opponent FC",
            Players = [],
            Tactics = new TeamTactics()
        };

        var insight = new TacticalInsightService().GenerateInsight(selectedTeam, opponent);

        Assert.DoesNotContain(insight.Warnings, warning => warning.Contains($"{player.Name} is showing signs of fatigue", StringComparison.Ordinal));
    }

    [Fact]
    public void TacticalPresets_WriteExpectedIdentityValues()
    {
        var gegenpress = TacticalProfileService.GetPresets().First(preset => preset.Key == "gegenpress");
        var parkTheBus = TacticalProfileService.GetPresets().First(preset => preset.Key == "park-the-bus");

        Assert.Equal(Mentality.Attacking, gegenpress.Tactics.Mentality);
        Assert.True(gegenpress.Tactics.PressingIntensity >= 85);
        Assert.True(gegenpress.Tactics.DefensiveLine >= 80);
        Assert.Equal(Mentality.UltraDefensive, parkTheBus.Tactics.Mentality);
        Assert.True(parkTheBus.Tactics.DefensiveLine <= 25);
    }

    [Fact]
    public void TacticalImpact_HighPressRaisesTurnoverAndFatigueRisk()
    {
        var calculator = new TacticalImpactCalculator();
        var attackingTeam = new Team
        {
            Name = "Build Up FC",
            Formation = "4-3-3",
            Tactics = new TeamTactics { Tempo = 82, Width = 40, PressingIntensity = 50, DefensiveLine = 50 }
        };
        var passiveTeam = new Team
        {
            Name = "Passive FC",
            Formation = "4-3-3",
            Tactics = new TeamTactics { PressingIntensity = 20, Tempo = 40, DefensiveLine = 35 }
        };
        var pressingTeam = new Team
        {
            Name = "Press FC",
            Formation = "4-3-3",
            Tactics = new TeamTactics { PressingIntensity = 90, Tempo = 82, DefensiveLine = 82 }
        };

        var passiveTurnoverRisk = calculator.GetTurnoverRiskModifier(attackingTeam, passiveTeam);
        var highPressTurnoverRisk = calculator.GetTurnoverRiskModifier(attackingTeam, pressingTeam);

        Assert.True(highPressTurnoverRisk > passiveTurnoverRisk);
    }

    [Fact]
    public void TacticalImpact_TenManAllOutAttackHasHigherTurnoverRisk()
    {
        var calculator = new TacticalImpactCalculator();
        var attackingTeam = CreateActiveTeam("Attack FC");
        var defendingTeam = CreateActiveTeam("Defense FC");
        attackingTeam.Tactics = new TeamTactics
        {
            Mentality = Mentality.AllOutAttack,
            Tempo = 90,
            PressingIntensity = 90,
            Width = 80
        };

        var fullStrengthRisk = calculator.GetTurnoverRiskModifier(attackingTeam, defendingTeam);
        attackingTeam.Players.First(player => player.Position != Position.Goalkeeper).IsSentOff = true;
        var tenManRisk = calculator.GetTurnoverRiskModifier(attackingTeam, defendingTeam);

        Assert.True(tenManRisk > fullStrengthRisk * 1.25);
    }

    [Fact]
    public void TacticalImpact_AllOutAttackBoostsAttackButWeakensDefense()
    {
        var calculator = new TacticalImpactCalculator();
        var balanced = new Team
        {
            Name = "Balanced FC",
            Formation = "4-3-3",
            Tactics = new TeamTactics { Mentality = Mentality.Balanced }
        };
        var allOut = new Team
        {
            Name = "All Out FC",
            Formation = "4-3-3",
            Tactics = new TeamTactics { Mentality = Mentality.AllOutAttack, Tempo = 90, PressingIntensity = 90, DefensiveLine = 88 }
        };

        Assert.True(calculator.GetAttackModifier(allOut) > calculator.GetAttackModifier(balanced));
        Assert.True(calculator.GetDefenseModifier(allOut) < calculator.GetDefenseModifier(balanced));
    }

    [Fact]
    public void ContextualPerformance_OverallRatingStronglyAffectsSameAction()
    {
        var calculator = new ContextualPlayerPerformanceCalculator();
        var elite = CreateRatedPlayer("Elite", 90);
        var average = CreateRatedPlayer("Average", 75);

        var elitePassing = calculator.Calculate(elite, MatchActionType.Passing).Score;
        var averagePassing = calculator.Calculate(average, MatchActionType.Passing).Score;

        Assert.True(elitePassing >= averagePassing + 5);
        Assert.True(calculator.GetContestProbability(elite, MatchActionType.Passing, average, MatchActionType.Interception) > 0.55);
    }

    [Fact]
    public void ContextualPerformance_UsesNoMoreThanTwoRelevantTraits()
    {
        var calculator = new ContextualPlayerPerformanceCalculator();
        var player = CreateRatedPlayer("Creator", 84);
        player.Traits =
        [
            PlayerTrait.Playmaker,
            PlayerTrait.LongPasser,
            PlayerTrait.TeamPlayer,
            PlayerTrait.PressResistant,
            PlayerTrait.ClinicalFinisher
        ];

        var result = calculator.Calculate(player, MatchActionType.Passing);

        Assert.Equal(2, result.AppliedTraits.Count);
        Assert.DoesNotContain(PlayerTrait.ClinicalFinisher, result.AppliedTraits);
    }

    [Fact]
    public void TacticalProfile_SlowTempoSlowsAttacksAndExtendsBuildup()
    {
        var calculator = new TacticalImpactCalculator();
        var slow = CreateActiveTeam("Slow FC");
        var fast = CreateActiveTeam("Fast FC");
        slow.Tactics.Tempo = 20;
        fast.Tactics.Tempo = 90;

        var slowProfile = calculator.GetEventProfile(slow);
        var fastProfile = calculator.GetEventProfile(fast);

        Assert.True(slowProfile.AttackFrequency < fastProfile.AttackFrequency);
        Assert.True(slowProfile.BuildupLength > fastProfile.BuildupLength);
        Assert.True(slowProfile.FatigueLoad < fastProfile.FatigueLoad);
    }

    [Fact]
    public void TacticalProfile_ConstantPressureHasRecoveryFatigueAndExposureTradeoffs()
    {
        var calculator = new TacticalImpactCalculator();
        var balanced = CreateActiveTeam("Balanced FC");
        var constantPressure = CreateActiveTeam("Press FC");
        balanced.Tactics.PressingIntensity = 50;
        constantPressure.Tactics.PressingIntensity = 95;

        var balancedProfile = calculator.GetEventProfile(balanced);
        var pressProfile = calculator.GetEventProfile(constantPressure);

        Assert.True(pressProfile.PressingRecovery > balancedProfile.PressingRecovery);
        Assert.True(pressProfile.ExposureAfterPress > balancedProfile.ExposureAfterPress);
        Assert.True(pressProfile.FatigueLoad >= balancedProfile.FatigueLoad * 1.30);
    }

    [Fact]
    public void TacticalProfile_WideAndHighLineSettingsHaveDistinctConsequences()
    {
        var calculator = new TacticalImpactCalculator();
        var wide = CreateActiveTeam("Wide FC");
        var narrow = CreateActiveTeam("Narrow FC");
        wide.Tactics.Width = 90;
        narrow.Tactics.Width = 20;
        wide.Tactics.DefensiveLine = 90;
        narrow.Tactics.DefensiveLine = 25;

        var wideProfile = calculator.GetEventProfile(wide);
        var narrowProfile = calculator.GetEventProfile(narrow);

        Assert.True(wideProfile.WideRoutePreference > narrowProfile.WideRoutePreference);
        Assert.True(narrowProfile.CentralRoutePreference > wideProfile.CentralRoutePreference);
        Assert.True(wideProfile.SpaceBehindLine > narrowProfile.SpaceBehindLine);
        Assert.True(wideProfile.OffsidePressure > narrowProfile.OffsidePressure);
    }

    [Fact]
    public void HomeAwayAdvantage_AppliesHomeBoostsAndAwayPressure()
    {
        var homeTeam = new Team { Name = "Home FC", Tactics = new TeamTactics() };
        var awayTeam = new Team { Name = "Away FC", Tactics = new TeamTactics() };
        var match = new Match { HomeTeam = homeTeam, AwayTeam = awayTeam };

        var homeModifier = HomeAwayAdvantageService.GetModifier(match, homeTeam);
        var awayModifier = HomeAwayAdvantageService.GetModifier(match, awayTeam);

        Assert.True(homeModifier.AttackModifier > 1.0);
        Assert.True(homeModifier.DefenseModifier > 1.0);
        Assert.True(homeModifier.PassingModifier > 1.0);
        Assert.True(awayModifier.AttackModifier < 1.0);
        Assert.True(awayModifier.FinishingModifier < 1.0);
        Assert.True(awayModifier.TurnoverRiskModifier > 1.0);
        Assert.True(awayModifier.FoulRiskModifier > 1.0);
    }

    [Fact]
    public void HomeAwayAdvantage_DefensiveAwayTacticsReduceAwayPenalty()
    {
        var homeTeam = new Team { Name = "Home FC", Tactics = new TeamTactics() };
        var defensiveAwayTeam = new Team
        {
            Name = "Defensive Away FC",
            Tactics = new TeamTactics { Mentality = Mentality.Defensive, DefensiveLine = 30, Tempo = 42 }
        };
        var aggressiveAwayTeam = new Team
        {
            Name = "Aggressive Away FC",
            Tactics = new TeamTactics { Mentality = Mentality.AllOutAttack, DefensiveLine = 75, Tempo = 78, PressingIntensity = 88 }
        };

        var defensiveMatch = new Match { HomeTeam = homeTeam, AwayTeam = defensiveAwayTeam };
        var aggressiveMatch = new Match { HomeTeam = homeTeam, AwayTeam = aggressiveAwayTeam };

        var defensiveModifier = HomeAwayAdvantageService.GetModifier(defensiveMatch, defensiveAwayTeam);
        var aggressiveModifier = HomeAwayAdvantageService.GetModifier(aggressiveMatch, aggressiveAwayTeam);

        Assert.True(defensiveModifier.AttackModifier > aggressiveModifier.AttackModifier);
        Assert.True(defensiveModifier.TurnoverRiskModifier < aggressiveModifier.TurnoverRiskModifier);
        Assert.True(defensiveModifier.FatigueLossModifier < aggressiveModifier.FatigueLossModifier);
    }

    private static Team CreateActiveTeam(string name)
    {
        var players = Enumerable.Range(0, 11)
            .Select(index => new Player
            {
                Name = $"{name} Player {index + 1}",
                Position = index == 0 ? Position.Goalkeeper : index <= 4 ? Position.Defender : index <= 8 ? Position.Midfielder : Position.Forward,
                IsOnPitch = true,
                Stamina = 90,
                CurrentStamina = 90,
                Attack = 75,
                Defense = 75,
                Passing = 75,
                Finishing = 75,
                OverallRating = 75
            })
            .ToList();

        return new Team
        {
            Name = name,
            Formation = "4-3-3",
            Players = players,
            Tactics = new TeamTactics()
        };
    }

    private static Player CreateRatedPlayer(string name, int rating)
    {
        return new Player
        {
            Name = name,
            Position = Position.Midfielder,
            PreferredPosition = "CM",
            AssignedPosition = "CM",
            OverallRating = rating,
            Attack = rating,
            Defense = rating,
            Passing = rating,
            Finishing = rating,
            Pace = rating,
            Shooting = rating,
            Dribbling = rating,
            Defending = rating,
            Physical = rating,
            Stamina = 100,
            CurrentForm = 50,
            Morale = 50
        };
    }
}
