using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class MatchEventFactoryTests
{
    [Theory]
    [InlineData("free-kick header")]
    [InlineData("corner header")]
    public void CreateShot_ClassifiesHeaderAttemptsWithoutChangingShotEventType(string chanceType)
    {
        var team = new Team { Name = "Chelsea" };
        var taker = new Player { Name = "Enzo Fernandez", Position = Position.Midfielder, PreferredPosition = "CM" };
        var target = new Player { Name = "Liam Delap", Position = Position.Forward, PreferredPosition = "ST" };

        var matchEvent = new MatchEventFactory().CreateShot(2, team, target, taker, chanceType, new Random(4));

        Assert.Equal(EventType.Shot, matchEvent.EventType);
        Assert.Equal(ShotClassification.Header, matchEvent.ShotClassification);
        Assert.Equal(target.Name, matchEvent.PrimaryPlayerName);
        Assert.DoesNotContain("takes a shot", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateMiss_KeepsHeaderContextForOutcomeTextOnly()
    {
        var team = new Team { Name = "Chelsea" };
        var target = new Player { Name = "Liam Delap", Position = Position.Forward, PreferredPosition = "ST" };

        var matchEvent = new MatchEventFactory().CreateMiss(2, team, target, "free-kick header", new Random(2));

        Assert.Equal(EventType.Miss, matchEvent.EventType);
        Assert.Equal(ShotClassification.Header, matchEvent.ShotClassification);
    }

    [Fact]
    public void CreateShot_CrossProducesAVisibleHeaderOrVolleyAction()
    {
        var team = new Team { Name = "Chelsea" };
        var creator = new Player { Name = "Reece James", Position = Position.Defender, PreferredPosition = "RB" };
        var attacker = new Player { Name = "Liam Delap", Position = Position.Forward, PreferredPosition = "ST" };

        var matchEvent = new MatchEventFactory().CreateShot(
            36,
            team,
            attacker,
            creator,
            "cross into box",
            new Random(3));

        Assert.Equal(EventType.Shot, matchEvent.EventType);
        Assert.Contains(matchEvent.ShotClassification, new[] { ShotClassification.Header, ShotClassification.Volley });
        Assert.True(
            matchEvent.Description.Contains("heads", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains("volley", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateLateDrama_PreservesCatalystAndTriggeredTrait()
    {
        var team = new Team { Name = "Chelsea" };
        var opponent = new Team { Name = "Arsenal" };
        var catalyst = new Player { Name = "Cole Palmer", Position = Position.Midfielder, PreferredPosition = "CAM" };
        var match = new Match { HomeTeam = team, AwayTeam = opponent, HomeScore = 1, AwayScore = 2 };

        var matchEvent = new MatchEventFactory().CreateLateDrama(88, team, opponent, match, catalyst, PlayerTrait.BigMatchPlayer);

        Assert.Equal(EventType.LateDrama, matchEvent.EventType);
        Assert.Equal(catalyst.Name, matchEvent.PrimaryPlayerName);
        Assert.Equal(PlayerTrait.BigMatchPlayer, matchEvent.TriggeredTrait);
    }

    [Fact]
    public void CreateTimeWasting_PreservesPlayerAndLeadershipTrait()
    {
        var team = new Team { Name = "Chelsea" };
        var leader = new Player { Name = "Reece James", Position = Position.Defender, PreferredPosition = "RB" };

        var matchEvent = new MatchEventFactory().CreateTimeWasting(84, team, new Random(1), leader, PlayerTrait.Leadership);

        Assert.Equal(EventType.TimeWasting, matchEvent.EventType);
        Assert.Equal(leader.Name, matchEvent.PrimaryPlayerName);
        Assert.Equal(PlayerTrait.Leadership, matchEvent.TriggeredTrait);
    }

    [Fact]
    public void CreateGoalkeeperMistake_UsesSlipperyConditionContext()
    {
        var team = new Team { Name = "Chelsea" };
        var goalkeeper = new Player { Name = "Robert Sanchez", Position = Position.Goalkeeper, PreferredPosition = "GK" };
        var attacker = new Player { Name = "Bukayo Saka", Position = Position.Forward, PreferredPosition = "RW" };

        var matchEvent = new MatchEventFactory().CreateGoalkeeperMistake(63, team, goalkeeper, attacker, new Random(3), "slippery conditions");

        Assert.Equal(EventType.GoalkeeperMistake, matchEvent.EventType);
        Assert.Equal(goalkeeper.Name, matchEvent.PrimaryPlayerName);
        Assert.Equal(attacker.Name, matchEvent.SecondaryPlayerName);
        Assert.Contains("ball", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDive_RecordsSimulationAndAttacker()
    {
        var team = new Team { Name = "Chelsea" };
        var attacker = new Player { Name = "Cole Palmer", Position = Position.Midfielder, PreferredPosition = "CAM" };

        var matchEvent = new MatchEventFactory().CreateDive(72, team, attacker);

        Assert.Equal(EventType.Dive, matchEvent.EventType);
        Assert.Equal(attacker.Name, matchEvent.PrimaryPlayerName);
        Assert.Contains("simulation", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No penalty", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePersistentFoulWarning_ExplainsTeamEscalation()
    {
        var team = new Team { Name = "Chelsea" };
        var defender = new Player { Name = "Reece James", Position = Position.Defender, PreferredPosition = "RB" };

        var matchEvent = new MatchEventFactory().CreatePersistentFoulWarning(54, team, defender, isTeamWarning: true);

        Assert.Equal(EventType.RefereeControversy, matchEvent.EventType);
        Assert.Contains("persistent fouling", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(defender.Name, matchEvent.PrimaryPlayerName);
    }

    [Fact]
    public void AttackNarrative_ConnectsBuildUpAndChanceAcrossFeedEvents()
    {
        var team = new Team { Name = "Chelsea" };
        var creator = new Player { Name = "Cole Palmer", Position = Position.Midfielder, PreferredPosition = "CAM" };
        var shooter = new Player { Name = "Jamie Gittens", Position = Position.Forward, PreferredPosition = "LW" };
        var support = new Player { Name = "Moises Caicedo", Position = Position.Midfielder, PreferredPosition = "CM" };
        team.Players.AddRange([creator, shooter, support]);
        var narrative = new AttackNarrativeContext(
            "attack-1",
            team.Name,
            "turnover",
            "left flank",
            "quick",
            creator.Name,
            shooter.Name,
            "high press",
            IsLateUrgency: true);
        var factory = new MatchEventFactory();

        var buildUp = factory.CreateAttackBuildUp(82, team, creator, shooter, new Random(4), narrative: narrative);
        var progression = factory.CreateAttackProgression(82, team, creator, shooter, narrative, new Random(4));
        var continuation = factory.CreateAttackContinuation(82, team, shooter, support, narrative, new Random(6));
        var chance = factory.CreateChanceCreated(83, team, creator, shooter, "through ball attempt", new Random(5), narrative: narrative);

        Assert.Equal(narrative.Id, buildUp.AttackNarrativeId);
        Assert.Equal(narrative.Id, progression.AttackNarrativeId);
        Assert.Equal(narrative.Id, continuation.AttackNarrativeId);
        Assert.Equal(narrative.Id, chance.AttackNarrativeId);
        Assert.Equal(EventType.AttackProgression, progression.EventType);
        Assert.False(string.IsNullOrWhiteSpace(progression.AttackAction));
        Assert.False(string.IsNullOrWhiteSpace(continuation.AttackAction));
        Assert.NotEqual(progression.AttackAction, continuation.AttackAction);
        Assert.Equal("left flank", chance.AttackRoute);
        Assert.Contains(team.Name, buildUp.Description);
        Assert.Contains("Palmer", progression.Description);
        Assert.Contains("Gittens", progression.Description);
        Assert.True(
            progression.Description.Contains("pass", StringComparison.OrdinalIgnoreCase) ||
            progression.Description.Contains("dribbl", StringComparison.OrdinalIgnoreCase) ||
            progression.Description.Contains("carries", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Gittens", chance.Description);
        Assert.Contains("Gittens", continuation.Description);
        Assert.Contains("Caicedo", continuation.Description);
    }

    [Fact]
    public void AttackNarrative_SoloChanceDoesNotInventSecondPlayer()
    {
        var team = new Team { Name = "Chelsea" };
        var attacker = new Player { Name = "Pedro Neto", Position = Position.Forward, PreferredPosition = "RW" };
        team.Players.Add(attacker);
        var narrative = new AttackNarrativeContext(
            "attack-2",
            team.Name,
            "midfield possession",
            "right flank",
            "balanced",
            attacker.Name,
            attacker.Name,
            "balanced shape",
            IsLateUrgency: false);

        var chance = new MatchEventFactory().CreateChanceCreated(
            31,
            team,
            attacker,
            attacker,
            "dribble run",
            new Random(2),
            narrative: narrative);

        Assert.Null(chance.SecondaryPlayerName);
        Assert.Contains("Neto", chance.Description);
    }

    [Fact]
    public void AttackProgression_UsesPlayerTraitsInActionFeed()
    {
        var team = new Team { Name = "Chelsea" };
        var passer = new Player
        {
            Name = "Enzo Fernandez",
            Position = Position.Midfielder,
            Traits = [PlayerTrait.LongPasser]
        };
        var receiver = new Player
        {
            Name = "Jamie Gittens",
            Position = Position.Forward,
            Traits = [PlayerTrait.TechnicalDribbler]
        };
        var support = new Player { Name = "Cole Palmer", Position = Position.Midfielder };
        team.Players.AddRange([passer, receiver, support]);
        var narrative = new AttackNarrativeContext(
            "trait-attack",
            team.Name,
            "midfield possession",
            "switch of play",
            "balanced",
            passer.Name,
            receiver.Name,
            "balanced shape",
            IsLateUrgency: false);
        var factory = new MatchEventFactory();

        var switchEvent = factory.CreateAttackProgression(24, team, passer, receiver, narrative, new Random(1));
        var controlEvent = factory.CreateAttackContinuation(24, team, receiver, support, narrative, new Random(1));

        Assert.Equal("Switch", switchEvent.AttackAction);
        Assert.Equal(PlayerTrait.LongPasser, switchEvent.TriggeredTrait);
        Assert.Equal(2, switchEvent.AttackSequenceStep);
        Assert.Equal(3, controlEvent.AttackSequenceStep);
    }

    [Fact]
    public void SidelineClearance_CreatesConnectedClearanceAndThrowInFeeds()
    {
        var defendingTeam = new Team { Name = "Arsenal" };
        var attackingTeam = new Team { Name = "Chelsea" };
        var defender = new Player { Name = "William Saliba", Position = Position.Defender };
        var attacker = new Player { Name = "Cole Palmer", Position = Position.Midfielder };
        var thrower = new Player
        {
            Name = "Reece James",
            Position = Position.Defender,
            Traits = [PlayerTrait.LongThrower]
        };
        var receiver = new Player { Name = "Jamie Gittens", Position = Position.Forward };
        var factory = new MatchEventFactory();

        var clearance = factory.CreateClearanceToTouch(31, defendingTeam, defender, attacker, new Random(2));
        var throwIn = factory.CreateThrowIn(31, attackingTeam, thrower, receiver, new Random(2));

        Assert.Equal(EventType.Clearance, clearance.EventType);
        Assert.Equal(EventType.ThrowIn, throwIn.EventType);
        Assert.Equal(defender.Name, clearance.PrimaryPlayerName);
        Assert.Equal(thrower.Name, throwIn.PrimaryPlayerName);
        Assert.Equal(receiver.Name, throwIn.SecondaryPlayerName);
        Assert.Equal(PlayerTrait.LongThrower, throwIn.TriggeredTrait);
    }

    [Fact]
    public void DecisiveWideAction_CreatesAnEarlyCrossFeed()
    {
        var team = new Team { Name = "Chelsea" };
        var creator = new Player
        {
            Name = "Reece James",
            Position = Position.Defender,
            PreferredPosition = "RB",
            Traits = [PlayerTrait.EarlyCrosser]
        };
        var receiver = new Player { Name = "Liam Delap", Position = Position.Forward };
        var narrative = new AttackNarrativeContext(
            "cross-attack",
            team.Name,
            "midfield possession",
            "right flank",
            "quick",
            creator.Name,
            receiver.Name,
            "balanced shape",
            false);

        var matchEvent = new MatchEventFactory().CreateDecisiveAttackAction(
            44,
            team,
            creator,
            receiver,
            "cross into box",
            narrative,
            new Random(2));

        Assert.Equal(EventType.AttackProgression, matchEvent.EventType);
        Assert.Equal("Cross", matchEvent.AttackAction);
        Assert.Equal(PlayerTrait.EarlyCrosser, matchEvent.TriggeredTrait);
        Assert.Contains("cross", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }
}
