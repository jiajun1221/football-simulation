using System.Diagnostics;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

public class MatchEngine
{
    private readonly TeamStrengthCalculator _teamStrengthCalculator;
    private readonly DisciplinaryService _disciplinaryService;
    private readonly TacticalImpactCalculator _tacticalImpactCalculator = new();
    private readonly MatchDramaService _matchDramaService = new();
    private readonly FatigueService _fatigueService = new();
    private readonly AiManagerService _aiManagerService = new();
    private readonly Random _random;

    public MatchEngine()
        : this(new TeamStrengthCalculator(), new DisciplinaryService(), Random.Shared)
    {
    }

    public MatchEngine(TeamStrengthCalculator teamStrengthCalculator, DisciplinaryService disciplinaryService)
        : this(teamStrengthCalculator, disciplinaryService, Random.Shared)
    {
    }

    public MatchEngine(TeamStrengthCalculator teamStrengthCalculator, DisciplinaryService disciplinaryService, Random random)
    {
        _teamStrengthCalculator = teamStrengthCalculator;
        _disciplinaryService = disciplinaryService;
        _random = random;
    }

    public Match SimulateMatch(
        Team homeTeam,
        Team awayTeam,
        int? seed = null,
        int totalMinutes = MatchConstants.DefaultMatchDurationMinutes,
        MatchSimulationOptions? options = null)
    {
        if (totalMinutes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(totalMinutes), "Match length must be at least 1 minute.");
        }

        if (totalMinutes == MatchConstants.DefaultMatchDurationMinutes)
        {
            var match = SimulateFirstHalf(homeTeam, awayTeam, seed, options);
            return SimulateSecondHalf(match, seed.HasValue ? seed.Value + 100 : null, options);
        }

        return SimulateMatchSegment(homeTeam, awayTeam, seed, 1, totalMinutes, resetPlayers: true, includeFulltime: true, options);
    }

    public Match SimulateFirstHalf(Team homeTeam, Team awayTeam, int? seed = null, MatchSimulationOptions? options = null)
    {
        return SimulateMatchSegment(
            homeTeam,
            awayTeam,
            seed,
            startMinute: 1,
            endMinute: MatchConstants.HalftimeMinute,
            resetPlayers: true,
            includeFulltime: false,
            options: options);
    }

    public Match SimulateSecondHalf(Match match, int? seed = null, MatchSimulationOptions? options = null)
    {
        ValidateTeam(match.HomeTeam, nameof(match.HomeTeam));
        ValidateTeam(match.AwayTeam, nameof(match.AwayTeam));

        var homePossessionMoments = EstimatePossessionMoments(match.HomeStats.PossessionPercentage, MatchConstants.HalftimeMinute);
        var awayPossessionMoments = MatchConstants.HalftimeMinute - homePossessionMoments;
        var simulationState = new MatchSimulationState(
            match,
            CreateMatchLog(match),
            new MatchEventFactory(),
            homePossessionMoments,
            awayPossessionMoments,
            CreateOptions(options));
        EnsurePlayerPerformances(match);

        RunMatchMinutes(
            simulationState,
            CreateRandom(seed),
            startMinute: MatchConstants.HalftimeMinute + 1,
            endMinute: MatchConstants.DefaultMatchDurationMinutes,
            includeFulltime: true);

        FinalizeSimulationState(simulationState);
        return match;
    }

    private Match SimulateMatchSegment(
        Team homeTeam,
        Team awayTeam,
        int? seed,
        int startMinute,
        int endMinute,
        bool resetPlayers,
        bool includeFulltime,
        MatchSimulationOptions? options = null)
    {
        ValidateTeam(homeTeam, nameof(homeTeam));
        ValidateTeam(awayTeam, nameof(awayTeam));

        if (resetPlayers)
        {
            ResetTeamStamina(homeTeam);
            ResetTeamStamina(awayTeam);
        }

        var match = CreateMatch(homeTeam, awayTeam);
        var simulationState = new MatchSimulationState(match, new MatchLogService(), new MatchEventFactory(), 0, 0, CreateOptions(options));

        RunMatchMinutes(simulationState, CreateRandom(seed), startMinute, endMinute, includeFulltime);
        FinalizeSimulationState(simulationState);

        return match;
    }

    private Random CreateRandom(int? seed)
    {
        return seed.HasValue ? new Random(seed.Value) : _random;
    }

    private static MatchSimulationOptions CreateOptions(MatchSimulationOptions? options)
    {
        return options ?? new MatchSimulationOptions
        {
            EnableAiSubstitutions = false,
            EnableDynamicFatigue = false
        };
    }

    private void RunMatchMinutes(
        MatchSimulationState simulationState,
        Random random,
        int startMinute,
        int endMinute,
        bool includeFulltime)
    {
        var match = simulationState.Match;
        var homeTeam = match.HomeTeam;
        var awayTeam = match.AwayTeam;

        for (var minute = startMinute; minute <= endMinute; minute++)
        {
            UpdateMatchPhase(match, minute);
            if (minute >= 60)
            {
                TryApplyAiManagerDecisions(simulationState, random, minute);
            }

            var strengthSnapshot = CreateStrengthSnapshot(homeTeam, awayTeam, random);
            WriteStrengthDiagnostics(match, minute, strengthSnapshot);
            strengthSnapshot = ApplyDramaEventIfAny(simulationState, random, strengthSnapshot, minute);

            if (minute == 1)
            {
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateKickoff(minute, homeTeam));
            }

            var attackingTeam = ChooseAttackingTeam(
                homeTeam,
                awayTeam,
                strengthSnapshot.HomeAttackStrength,
                strengthSnapshot.AwayAttackStrength,
                strengthSnapshot.HomeDefenseStrength,
                strengthSnapshot.AwayDefenseStrength,
                random);

            if (attackingTeam == homeTeam)
            {
                simulationState.HomePossessionMoments++;
            }
            else
            {
                simulationState.AwayPossessionMoments++;
            }

            ProcessAttack(
                minute,
                simulationState,
                attackingTeam,
                random,
                strengthSnapshot);

            if (minute == MatchConstants.HalftimeMinute)
            {
                match.CurrentPhase = MatchPhase.Halftime;
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateHalftime(minute, match));
                TryApplyAiManagerDecisions(simulationState, random, minute);
            }

            if (includeFulltime && minute == endMinute)
            {
                match.CurrentPhase = MatchPhase.Fulltime;
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateFulltime(minute, match));
            }

            if (minute < endMinute)
            {
                ApplyMinuteFatigue(homeTeam, simulationState.Options);
                ApplyMinuteFatigue(awayTeam, simulationState.Options);
            }
        }
    }

    private void TryApplyAiManagerDecisions(MatchSimulationState simulationState, Random random, int minute)
    {
        TryApplyAiManagerDecision(simulationState, simulationState.Match.HomeTeam, random, minute);
        TryApplyAiManagerDecision(simulationState, simulationState.Match.AwayTeam, random, minute);
    }

    private void TryApplyAiManagerDecision(MatchSimulationState simulationState, Team team, Random random, int minute)
    {
        var decision = _aiManagerService.TryMakeSubstitution(
            simulationState.Match,
            team,
            minute,
            simulationState.Options,
            random);

        if (decision is null)
        {
            return;
        }

        simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateSubstitution(
            minute,
            decision.Team,
            decision.PlayerOff,
            decision.PlayerOn));
        simulationState.SuperSubBoosts[decision.PlayerOn.Name] = minute + 10;
    }

    private static bool IsSuperSubBoostActive(MatchSimulationState simulationState, Player player, int minute)
    {
        return simulationState.SuperSubBoosts.TryGetValue(player.Name, out var boostEndMinute) &&
            minute <= boostEndMinute;
    }

    private static MatchLogService CreateMatchLog(Match match)
    {
        var matchLog = new MatchLogService();
        matchLog.AddEvents(match.Events);
        return matchLog;
    }

    private static void FinalizeSimulationState(MatchSimulationState simulationState)
    {
        SetPossessionStats(
            simulationState.Match,
            simulationState.HomePossessionMoments,
            simulationState.AwayPossessionMoments);
        SetPassingStats(simulationState.Match);

        simulationState.Match.Events = simulationState.MatchLog.GetEvents();
        UpdateFinalPlayerFatigue(simulationState.Match);
        NormalizePlayerRatings(simulationState.Match);
    }

    private static int EstimatePossessionMoments(double possessionPercentage, int totalMinutes)
    {
        return (int)Math.Round(possessionPercentage * totalMinutes / 100.0);
    }

    private static Match CreateMatch(Team homeTeam, Team awayTeam)
    {
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            HomeStats = new MatchTeamStats(),
            AwayStats = new MatchTeamStats(),
            CurrentPhase = MatchPhase.NotStarted
        };

        EnsurePlayerPerformances(match);
        return match;
    }

    private static void UpdateMatchPhase(Match match, int minute)
    {
        match.CurrentMinute = minute;
        match.CurrentPhase = minute <= MatchConstants.HalftimeMinute
            ? MatchPhase.FirstHalf
            : MatchPhase.SecondHalf;
    }

    private TeamStrengthSnapshot CreateStrengthSnapshot(Team homeTeam, Team awayTeam, Random random)
    {
        var homeAttackSwing = GetRandomStrengthSwing(random);
        var awayAttackSwing = GetRandomStrengthSwing(random);
        var homeDefenseSwing = GetRandomStrengthSwing(random);
        var awayDefenseSwing = GetRandomStrengthSwing(random);

        var homeTacticalAttack = _tacticalImpactCalculator.GetAttackModifier(homeTeam, awayTeam);
        var awayTacticalAttack = _tacticalImpactCalculator.GetAttackModifier(awayTeam, homeTeam);
        var homeTacticalDefense = _tacticalImpactCalculator.GetDefenseModifier(homeTeam, awayTeam);
        var awayTacticalDefense = _tacticalImpactCalculator.GetDefenseModifier(awayTeam, homeTeam);

        return new TeamStrengthSnapshot(
            _teamStrengthCalculator.CalculateAttackStrength(homeTeam, awayTeam) * homeAttackSwing,
            _teamStrengthCalculator.CalculateAttackStrength(awayTeam, homeTeam) * awayAttackSwing,
            _teamStrengthCalculator.CalculateDefenseStrength(homeTeam, awayTeam) * homeDefenseSwing,
            _teamStrengthCalculator.CalculateDefenseStrength(awayTeam, homeTeam) * awayDefenseSwing,
            new TeamDiagnosticSnapshot(
                CalculateBaseAttack(homeTeam),
                homeTacticalAttack,
                CalculateFatiguePenalty(homeTeam),
                homeAttackSwing,
                CalculateBaseDefense(homeTeam),
                homeTacticalDefense,
                CalculateFatiguePenalty(homeTeam),
                homeDefenseSwing),
            new TeamDiagnosticSnapshot(
                CalculateBaseAttack(awayTeam),
                awayTacticalAttack,
                CalculateFatiguePenalty(awayTeam),
                awayAttackSwing,
                CalculateBaseDefense(awayTeam),
                awayTacticalDefense,
                CalculateFatiguePenalty(awayTeam),
                awayDefenseSwing));
    }

    private static double GetRandomStrengthSwing(Random random)
    {
        return 0.88 + (random.NextDouble() * 0.24);
    }

    private static double CalculateBaseAttack(Team team)
    {
        return team.Players.Average(player => player.Position switch
        {
            Position.Forward => (player.Attack + player.Finishing) / 2.0 * 1.30,
            Position.Midfielder => (player.Attack + player.Passing) / 2.0 * 1.10,
            Position.Defender => player.Attack * 0.70,
            Position.Goalkeeper => player.Attack * 0.30,
            _ => player.Attack
        });
    }

    private static double CalculateBaseDefense(Team team)
    {
        return team.Players.Average(player => player.Position switch
        {
            Position.Goalkeeper => player.Defense * 1.40,
            Position.Defender => player.Defense * 1.20,
            Position.Midfielder => player.Defense * 0.90,
            Position.Forward => player.Defense * 0.40,
            _ => player.Defense
        });
    }

    private static double CalculateFatiguePenalty(Team team)
    {
        var averageStaminaRatio = team.Players.Average(player =>
        {
            if (player.Stamina <= 0)
            {
                return 0.0;
            }

            return Math.Clamp(player.CurrentStamina / player.Stamina, 0.0, 1.0);
        });

        var averageFatiguePenalty = team.Players.Average(player => player.Fatigue / 125.0);

        return Math.Clamp(1.0 - averageStaminaRatio + averageFatiguePenalty, 0.0, 0.75);
    }

    private static void WriteStrengthDiagnostics(Match match, int minute, TeamStrengthSnapshot snapshot)
    {
        if (minute is not (1 or 15 or 30 or 45 or 46 or 60 or 75 or 90))
        {
            return;
        }

        Debug.WriteLine(
            $"[MatchEngine] {minute}' {match.HomeTeam.Name} ATK base={snapshot.HomeDiagnostics.BaseAttack:0.0}, tactical={snapshot.HomeDiagnostics.AttackTacticalModifier:0.00}, fatiguePenalty={snapshot.HomeDiagnostics.AttackFatiguePenalty:0.00}, random={snapshot.HomeDiagnostics.AttackRandomSwing:0.00}, final={snapshot.HomeAttackStrength:0.0}; " +
            $"DEF base={snapshot.HomeDiagnostics.BaseDefense:0.0}, tactical={snapshot.HomeDiagnostics.DefenseTacticalModifier:0.00}, fatiguePenalty={snapshot.HomeDiagnostics.DefenseFatiguePenalty:0.00}, random={snapshot.HomeDiagnostics.DefenseRandomSwing:0.00}, final={snapshot.HomeDefenseStrength:0.0}");

        Debug.WriteLine(
            $"[MatchEngine] {minute}' {match.AwayTeam.Name} ATK base={snapshot.AwayDiagnostics.BaseAttack:0.0}, tactical={snapshot.AwayDiagnostics.AttackTacticalModifier:0.00}, fatiguePenalty={snapshot.AwayDiagnostics.AttackFatiguePenalty:0.00}, random={snapshot.AwayDiagnostics.AttackRandomSwing:0.00}, final={snapshot.AwayAttackStrength:0.0}; " +
            $"DEF base={snapshot.AwayDiagnostics.BaseDefense:0.0}, tactical={snapshot.AwayDiagnostics.DefenseTacticalModifier:0.00}, fatiguePenalty={snapshot.AwayDiagnostics.DefenseFatiguePenalty:0.00}, random={snapshot.AwayDiagnostics.DefenseRandomSwing:0.00}, final={snapshot.AwayDefenseStrength:0.0}");
    }

    private TeamStrengthSnapshot ApplyDramaEventIfAny(
        MatchSimulationState simulationState,
        Random random,
        TeamStrengthSnapshot strengthSnapshot,
        int minute)
    {
        var match = simulationState.Match;
        var dramaContext = new MatchEventContext
        {
            Match = match,
            HomeTeam = match.HomeTeam,
            AwayTeam = match.AwayTeam,
            Random = random,
            Minute = minute,
            HomeAttackStrength = strengthSnapshot.HomeAttackStrength,
            AwayAttackStrength = strengthSnapshot.AwayAttackStrength,
            HomeDefenseStrength = strengthSnapshot.HomeDefenseStrength,
            AwayDefenseStrength = strengthSnapshot.AwayDefenseStrength
        };

        var dramaResult = _matchDramaService.TryCreateDramaEvent(dramaContext);
        if (dramaResult is null)
        {
            return strengthSnapshot;
        }

        if (dramaResult.ScoresGoal)
        {
            UpdateScore(match, dramaResult.Team);
        }

        TrackDramaPerformance(match, dramaResult);
        simulationState.MatchLog.AddEvent(CreateDramaMatchEvent(minute, match, dramaResult, simulationState.EventFactory));

        return strengthSnapshot with
        {
            HomeAttackStrength = strengthSnapshot.HomeAttackStrength * dramaResult.HomeAttackModifier,
            AwayAttackStrength = strengthSnapshot.AwayAttackStrength * dramaResult.AwayAttackModifier,
            HomeDefenseStrength = strengthSnapshot.HomeDefenseStrength * dramaResult.HomeDefenseModifier,
            AwayDefenseStrength = strengthSnapshot.AwayDefenseStrength * dramaResult.AwayDefenseModifier
        };
    }

    private static MatchEvent CreateDramaMatchEvent(
        int minute,
        Match match,
        MatchDramaResult dramaResult,
        MatchEventFactory eventFactory)
    {
        return dramaResult.EventType switch
        {
            EventType.Injury => eventFactory.CreateInjury(minute, dramaResult.Team, dramaResult.Player!),
            EventType.Penalty => eventFactory.CreatePenalty(minute, dramaResult.Team, dramaResult.Player!, dramaResult.ScoresGoal, match),
            EventType.Offside => eventFactory.CreateOffside(minute, dramaResult.Team, dramaResult.Player!),
            EventType.DefensiveError => eventFactory.CreateDefensiveError(minute, dramaResult.Team, dramaResult.Player!),
            EventType.WonderGoal => eventFactory.CreateWonderGoal(minute, dramaResult.Team, dramaResult.Player!, match),
            EventType.GoalkeeperHeroics => eventFactory.CreateGoalkeeperHeroics(minute, dramaResult.Team, dramaResult.Player!),
            EventType.SetPieceDanger => eventFactory.CreateSetPieceDanger(minute, dramaResult.Team, dramaResult.Player!),
            EventType.Confrontation => eventFactory.CreateConfrontation(minute, dramaResult.Team, dramaResult.Player!),
            EventType.CrowdMomentum => eventFactory.CreateCrowdMomentum(minute, dramaResult.Team),
            EventType.Exhaustion => eventFactory.CreateExhaustion(minute, dramaResult.Team, dramaResult.Player!),
            _ => eventFactory.CreateSetPieceDanger(minute, dramaResult.Team, dramaResult.Player ?? dramaResult.Team.Players[0])
        };
    }

    private void ProcessAttack(
        int minute,
        MatchSimulationState simulationState,
        Team attackingTeam,
        Random random,
        TeamStrengthSnapshot strengthSnapshot)
    {
        var match = simulationState.Match;
        var matchLog = simulationState.MatchLog;
        var eventFactory = simulationState.EventFactory;
        var defendingTeam = attackingTeam == match.HomeTeam ? match.AwayTeam : match.HomeTeam;
        var attackingTeamStrength = attackingTeam == match.HomeTeam
            ? strengthSnapshot.HomeAttackStrength
            : strengthSnapshot.AwayAttackStrength;
        var defendingTeamStrength = defendingTeam == match.HomeTeam
            ? strengthSnapshot.HomeDefenseStrength
            : strengthSnapshot.AwayDefenseStrength;

        var playmaker = ChoosePlaymaker(attackingTeam, random);
        var shooter = ChooseShooter(attackingTeam, playmaker, random);
        var shooterHasSuperSubBoost = IsSuperSubBoostActive(simulationState, shooter, minute);
        var attackingStats = GetTeamStats(match, attackingTeam);
        var defendingStats = GetTeamStats(match, defendingTeam);

        if (!ShouldCreateAttack(attackingTeamStrength, defendingTeamStrength, random))
        {
            return;
        }

        matchLog.AddEvent(eventFactory.CreateAttack(minute, attackingTeam, playmaker, shooter));

        if (ShouldCommitFoul(defendingTeam, attackingTeamStrength, defendingTeamStrength, random))
        {
            HandleFoul(minute, match, defendingTeam, shooter, random, matchLog, eventFactory, defendingStats);
            return;
        }

        if (!ShouldCreateShot(shooter, playmaker, attackingTeamStrength, defendingTeamStrength, random, shooterHasSuperSubBoost))
        {
            return;
        }

        attackingStats.TotalShots++;
        matchLog.AddEvent(eventFactory.CreateShot(minute, attackingTeam, shooter, playmaker));
        HandleShotOutcome(
            minute,
            match,
            attackingTeam,
            defendingTeam,
            shooter,
            playmaker,
            random,
            matchLog,
            eventFactory,
            attackingStats,
            attackingTeamStrength,
            defendingTeamStrength,
            shooterHasSuperSubBoost);
    }

    private void HandleFoul(
        int minute,
        Match match,
        Team defendingTeam,
        Player fouledPlayer,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        MatchTeamStats defendingStats)
    {
        var defender = ChooseDefendingPlayer(defendingTeam, random);
        matchLog.AddEvent(eventFactory.CreateFoul(minute, defendingTeam, defender, fouledPlayer));
        defendingStats.Fouls++;
        var defenderPerformance = GetOrCreatePerformance(match, defendingTeam, defender);
        defenderPerformance.Fouls++;
        defenderPerformance.Rating -= 0.15;
        GetOrCreatePerformance(match, GetOpposingTeam(match, defendingTeam), fouledPlayer).Rating += 0.05;

        if (!ShouldGiveYellowCard(defender, random))
        {
            return;
        }

        matchLog.AddEvent(eventFactory.CreateYellowCard(minute, defender));
        defenderPerformance.YellowCards++;
        defenderPerformance.Rating -= 0.35;

        if (_disciplinaryService.ApplyYellowCard(defender, defendingStats))
        {
            matchLog.AddEvent(eventFactory.CreateRedCard(minute, defender));
            defenderPerformance.RedCards++;
            defenderPerformance.Rating -= 1.0;
        }
    }

    private void HandleShotOutcome(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player shooter,
        Player playmaker,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        MatchTeamStats attackingStats,
        double attackingTeamStrength,
        double defendingTeamStrength,
        bool shooterHasSuperSubBoost)
    {
        var goalProbability = CalculateGoalProbability(shooter, attackingTeamStrength, defendingTeamStrength, random, shooterHasSuperSubBoost);
        attackingStats.ExpectedGoals += goalProbability;

        var shooterPerformance = GetOrCreatePerformance(match, attackingTeam, shooter);
        shooterPerformance.Shots++;

        if (playmaker != shooter)
        {
            var playmakerPerformance = GetOrCreatePerformance(match, attackingTeam, playmaker);
            playmakerPerformance.KeyPasses++;
            playmakerPerformance.Rating += goalProbability >= 0.18 ? 0.20 : 0.08;
        }

        if (IsGoal(goalProbability, random))
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            shooterPerformance.ShotsOnTarget++;
            shooterPerformance.Goals++;
            shooterPerformance.Rating += 1.0 + goalProbability;

            var assister = playmaker != shooter ? playmaker : null;
            if (assister is not null)
            {
                var assisterPerformance = GetOrCreatePerformance(match, attackingTeam, assister);
                assisterPerformance.Assists++;
                assisterPerformance.Rating += 0.75;
            }

            matchLog.AddEvent(eventFactory.CreateGoal(minute, attackingTeam, shooter, match, assister));
            return;
        }

        var wasSaved = IsSaved(defendingTeam, random);
        if (wasSaved)
        {
            attackingStats.ShotsOnTarget++;
            shooterPerformance.ShotsOnTarget++;
            shooterPerformance.Rating += 0.08;
            var goalkeeper = GetGoalkeeper(defendingTeam);
            if (goalkeeper is not null)
            {
                var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                goalkeeperPerformance.Saves++;
                goalkeeperPerformance.Rating += 0.20 + goalProbability;
            }

            if (random.NextDouble() < 0.20)
            {
                attackingStats.Corners++;
            }

            matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, shooter));
            return;
        }

        shooterPerformance.Rating -= goalProbability >= 0.20 ? 0.12 : 0.04;
        if (random.NextDouble() < 0.12)
        {
            attackingStats.Corners++;
        }

        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, shooter));
    }

    private static void UpdateScore(Match match, Team scoringTeam)
    {
        if (scoringTeam == match.HomeTeam)
        {
            match.HomeScore++;
        }
        else
        {
            match.AwayScore++;
        }
    }

    private static void TrackDramaPerformance(Match match, MatchDramaResult dramaResult)
    {
        var stats = GetTeamStats(match, dramaResult.Team);
        var player = dramaResult.Player;

        switch (dramaResult.EventType)
        {
            case EventType.Penalty:
                stats.TotalShots++;
                stats.ShotsOnTarget++;
                stats.ExpectedGoals += 0.76;
                if (player is not null)
                {
                    var performance = GetOrCreatePerformance(match, dramaResult.Team, player);
                    performance.Shots++;
                    performance.ShotsOnTarget++;
                    if (dramaResult.ScoresGoal)
                    {
                        performance.Goals++;
                        performance.Rating += 1.05;
                    }
                    else
                    {
                        performance.Rating -= 0.35;
                    }
                }

                break;

            case EventType.WonderGoal:
                stats.ExpectedGoals += 0.08;
                if (player is not null)
                {
                    var performance = GetOrCreatePerformance(match, dramaResult.Team, player);
                    performance.Shots++;
                    performance.ShotsOnTarget++;
                    performance.Goals++;
                    performance.Rating += 1.35;
                }

                break;

            case EventType.Offside:
                stats.Offsides++;
                if (player is not null)
                {
                    var performance = GetOrCreatePerformance(match, dramaResult.Team, player);
                    performance.Offsides++;
                    performance.Rating -= 0.08;
                }

                break;

            case EventType.GoalkeeperHeroics:
                if (player is not null)
                {
                    var performance = GetOrCreatePerformance(match, dramaResult.Team, player);
                    performance.Saves++;
                    performance.Rating += 0.50;
                }

                break;

            case EventType.SetPieceDanger:
                stats.Corners++;
                if (player is not null)
                {
                    var performance = GetOrCreatePerformance(match, dramaResult.Team, player);
                    performance.KeyPasses++;
                    performance.Rating += 0.25;
                }

                break;

            case EventType.DefensiveError:
                if (player is not null)
                {
                    GetOrCreatePerformance(match, dramaResult.Team, player).Rating -= 0.45;
                }

                break;

            case EventType.Injury:
                if (player is not null)
                {
                    var performance = GetOrCreatePerformance(match, dramaResult.Team, player);
                    performance.Injuries++;
                    performance.Rating -= 0.30;
                }

                break;

            case EventType.Exhaustion:
                if (player is not null)
                {
                    GetOrCreatePerformance(match, dramaResult.Team, player).Rating -= 0.20;
                }

                break;

            case EventType.Confrontation:
                if (player is not null)
                {
                    GetOrCreatePerformance(match, dramaResult.Team, player).Rating -= 0.10;
                }

                break;
        }
    }

    private static void EnsurePlayerPerformances(Match match)
    {
        foreach (var player in match.HomeTeam.Players)
        {
            GetOrCreatePerformance(match, match.HomeTeam, player);
        }

        foreach (var player in match.AwayTeam.Players)
        {
            GetOrCreatePerformance(match, match.AwayTeam, player);
        }
    }

    private static PlayerMatchPerformance GetOrCreatePerformance(Match match, Team team, Player player)
    {
        var performance = match.PlayerPerformances.FirstOrDefault(existing =>
            existing.PlayerName == player.Name && existing.TeamName == team.Name);

        if (performance is not null)
        {
            return performance;
        }

        performance = new PlayerMatchPerformance
        {
            PlayerName = player.Name,
            TeamName = team.Name,
            Position = player.Position,
            FatigueAtStart = GetFatiguePercentage(player),
            FatigueAtEnd = GetFatiguePercentage(player),
            WasSubstitute = team.Substitutes.Contains(player)
        };

        match.PlayerPerformances.Add(performance);
        return performance;
    }

    private static void UpdateFinalPlayerFatigue(Match match)
    {
        foreach (var performance in match.PlayerPerformances)
        {
            var player = match.HomeTeam.Players.Concat(match.AwayTeam.Players)
                .Concat(match.HomeTeam.Substitutes)
                .Concat(match.AwayTeam.Substitutes)
                .FirstOrDefault(candidate => candidate.Name == performance.PlayerName);

            if (player is not null)
            {
                performance.FatigueAtEnd = GetFatiguePercentage(player);
            }
        }
    }

    private static void NormalizePlayerRatings(Match match)
    {
        foreach (var performance in match.PlayerPerformances)
        {
            performance.Rating = Math.Round(Math.Clamp(performance.Rating, 1.0, 10.0), 1);
        }
    }

    private static int GetFatiguePercentage(Player player)
    {
        if (player.Fatigue > 0)
        {
            return Math.Clamp(player.Fatigue, 0, 100);
        }

        if (player.Stamina <= 0)
        {
            return 100;
        }

        var staminaRatio = Math.Clamp(player.CurrentStamina / player.Stamina, 0.0, 1.0);
        return (int)Math.Round((1.0 - staminaRatio) * 100);
    }

    private static Team GetOpposingTeam(Match match, Team team)
    {
        return team == match.HomeTeam ? match.AwayTeam : match.HomeTeam;
    }

    private static Player? GetGoalkeeper(Team team)
    {
        return team.Players.FirstOrDefault(player => player.Position == Position.Goalkeeper);
    }

    private static void ValidateTeam(Team team, string parameterName)
    {
        if (team is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (team.Players.Count != 11)
        {
            throw new ArgumentException("Each team must have exactly 11 players.", parameterName);
        }
    }

    private static bool ShouldCreateAttack(
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random)
    {
        var attackPressure = attackingTeamStrength - (defendingTeamStrength * 0.60);
        var attackProbability = Math.Clamp(
            0.35 + (attackPressure / 180.0) + GetProbabilitySwing(random, 0.08),
            0.15,
            0.85);

        return random.NextDouble() < attackProbability;
    }

    private static bool ShouldCommitFoul(
        Team defendingTeam,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random)
    {
        var foulPressure = defendingTeamStrength - (attackingTeamStrength * 0.20);
        var aggressionBonus = defendingTeam.Players.Count(player => player.Traits.Contains(PlayerTrait.AggressiveTackler)) * 0.015;
        var foulProbability = Math.Clamp(
            MatchConstants.BaseFoulChancePerAttack + aggressionBonus + (foulPressure / 400.0) + GetProbabilitySwing(random, 0.03),
            0.05,
            0.32);

        return random.NextDouble() < foulProbability;
    }

    private static bool ShouldGiveYellowCard(Player defender, Random random)
    {
        var traitBonus = defender.Traits.Contains(PlayerTrait.AggressiveTackler) ? 0.18 : 0.0;
        return random.NextDouble() < MatchConstants.YellowCardChancePerFoul + traitBonus;
    }

    private static Team ChooseAttackingTeam(
        Team homeTeam,
        Team awayTeam,
        double homeAttackStrength,
        double awayAttackStrength,
        double homeDefenseStrength,
        double awayDefenseStrength,
        Random random)
    {
        var homePressure = homeAttackStrength / Math.Max(1.0, awayDefenseStrength);
        var awayPressure = awayAttackStrength / Math.Max(1.0, homeDefenseStrength);
        var totalPressure = homePressure + awayPressure;

        if (totalPressure <= 0)
        {
            return random.Next(0, 2) == 0 ? homeTeam : awayTeam;
        }

        return random.NextDouble() < homePressure / totalPressure ? homeTeam : awayTeam;
    }

    private bool ShouldCreateShot(
        Player shooter,
        Player playmaker,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random,
        bool hasSuperSubBoost)
    {
        var boost = hasSuperSubBoost ? 1.05 : 1.0;
        var shotScore =
            _teamStrengthCalculator.GetEffectiveAttack(shooter) * 0.35 * boost +
            _teamStrengthCalculator.GetEffectivePassing(playmaker) * 0.25 +
            attackingTeamStrength * 0.40 -
            defendingTeamStrength * 0.45;

        var shotProbability = Math.Clamp(
            MatchConstants.BaseShotChancePerMinute + (shotScore / 170.0) + GetProbabilitySwing(random, 0.06),
            MatchConstants.MinimumShotChancePerMinute,
            0.55);

        return random.NextDouble() < shotProbability;
    }

    private Player ChoosePlaymaker(Team team, Random random)
    {
        var candidates = GetAvailablePlayers(team)
            .Where(player => player.Position is Position.Midfielder or Position.Forward)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = GetAvailablePlayers(team).ToList();
        }

        var totalWeight = candidates.Sum(_teamStrengthCalculator.GetPlaymakerWeight);
        var roll = random.NextDouble() * totalWeight;
        double runningWeight = 0;

        foreach (var candidate in candidates)
        {
            runningWeight += _teamStrengthCalculator.GetPlaymakerWeight(candidate);
            if (roll <= runningWeight)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private Player ChooseShooter(Team team, Player playmaker, Random random)
    {
        var candidates = GetAvailablePlayers(team)
            .Where(player => player.Position is Position.Forward or Position.Midfielder)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = GetAvailablePlayers(team).ToList();
        }

        var candidateWeights = candidates.ToDictionary(
            candidate => candidate,
            candidate =>
            {
                var weight = _teamStrengthCalculator.GetShooterWeight(candidate);
                return candidate == playmaker && candidates.Count > 1 ? weight * 0.75 : weight;
            });

        var totalWeight = candidateWeights.Values.Sum();
        var roll = random.NextDouble() * totalWeight;
        double runningWeight = 0;

        foreach (var candidate in candidates)
        {
            runningWeight += candidateWeights[candidate];
            if (roll <= runningWeight)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private Player ChooseDefendingPlayer(Team team, Random random)
    {
        var candidates = GetAvailablePlayers(team)
            .Where(player => player.Position is Position.Defender or Position.Midfielder)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = GetAvailablePlayers(team).ToList();
        }

        var totalWeight = candidates.Sum(_teamStrengthCalculator.GetEffectiveDefense);
        var roll = random.NextDouble() * totalWeight;
        double runningWeight = 0;

        foreach (var candidate in candidates)
        {
            runningWeight += _teamStrengthCalculator.GetEffectiveDefense(candidate);
            if (roll <= runningWeight)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private bool IsGoal(double goalProbability, Random random)
    {
        return random.NextDouble() < goalProbability;
    }

    private double CalculateGoalProbability(
        Player attacker,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random,
        bool hasSuperSubBoost)
    {
        var boost = hasSuperSubBoost ? 1.05 : 1.0;
        var chanceScore =
            _teamStrengthCalculator.GetEffectiveFinishing(attacker) * 0.50 * boost +
            _teamStrengthCalculator.GetEffectiveAttack(attacker) * 0.20 * boost +
            attackingTeamStrength * 0.20 -
            defendingTeamStrength * 0.45;

        return Math.Clamp(
            MatchConstants.GoalProbabilityBase + (chanceScore / 130.0) + GetProbabilitySwing(random, 0.05),
            MatchConstants.MinimumGoalProbability,
            0.55);
    }

    private bool IsSaved(Team defendingTeam, Random random)
    {
        var goalkeeper = GetAvailablePlayers(defendingTeam)
            .FirstOrDefault(player => player.Position == Position.Goalkeeper);

        if (goalkeeper is null)
        {
            return false;
        }

        var saveProbability = Math.Clamp(_teamStrengthCalculator.GetEffectiveDefense(goalkeeper) / 100.0, 0.20, 0.85);
        return random.NextDouble() < saveProbability;
    }

    private static double GetProbabilitySwing(Random random, double range)
    {
        return (random.NextDouble() * range) - (range / 2.0);
    }

    private static void ResetTeamStamina(Team team)
    {
        foreach (var player in team.Players)
        {
            player.Fatigue = Math.Clamp(player.Fatigue, 0, 100);
            player.CurrentStamina = Math.Clamp(player.Stamina * ((100 - player.Fatigue) / 100.0), 0, player.Stamina);
            player.YellowCards = 0;
            player.IsSentOff = false;
        }
    }

    private void ApplyMinuteFatigue(Team team, MatchSimulationOptions options)
    {
        if (options.EnableDynamicFatigue)
        {
            _fatigueService.ApplyMinuteFatigue(team);
            return;
        }

        var tacticalLoad = (team.Tactics.PressingIntensity + team.Tactics.Tempo + team.Tactics.DefensiveLine) / 3.0;
        var loadModifier = 1.0 + (Math.Max(0.0, tacticalLoad - 50.0) / 100.0);
        var staminaLoss = MatchConstants.StaminaLossPerMinute * loadModifier;

        foreach (var player in team.Players)
        {
            player.CurrentStamina = Math.Max(0.0, player.CurrentStamina - staminaLoss);
        }
    }

    private IEnumerable<Player> GetAvailablePlayers(Team team)
    {
        return _teamStrengthCalculator.GetAvailablePlayers(team);
    }

    private static MatchTeamStats GetTeamStats(Match match, Team team)
    {
        return team == match.HomeTeam ? match.HomeStats : match.AwayStats;
    }

    private static void SetPossessionStats(Match match, int homePossessionMoments, int awayPossessionMoments)
    {
        var totalPossessionMoments = homePossessionMoments + awayPossessionMoments;
        if (totalPossessionMoments == 0)
        {
            match.HomeStats.PossessionPercentage = 50.0;
            match.AwayStats.PossessionPercentage = 50.0;
            return;
        }

        match.HomeStats.PossessionPercentage = Math.Round(homePossessionMoments * 100.0 / totalPossessionMoments, 1);
        match.AwayStats.PossessionPercentage = Math.Round(100.0 - match.HomeStats.PossessionPercentage, 1);
    }

    private static void SetPassingStats(Match match)
    {
        match.HomeStats.Passes = EstimatePasses(match.HomeTeam, match.HomeStats.PossessionPercentage);
        match.AwayStats.Passes = EstimatePasses(match.AwayTeam, match.AwayStats.PossessionPercentage);
        match.HomeStats.PassAccuracyPercentage = EstimatePassAccuracy(match.HomeTeam);
        match.AwayStats.PassAccuracyPercentage = EstimatePassAccuracy(match.AwayTeam);
    }

    private static int EstimatePasses(Team team, double possessionPercentage)
    {
        var tempoModifier = 0.80 + team.Tactics.Tempo / 160.0;
        return (int)Math.Round((170 + possessionPercentage * 4.2) * tempoModifier);
    }

    private static double EstimatePassAccuracy(Team team)
    {
        var averagePassing = team.Players.Average(player => player.Passing);
        var tempoPenalty = Math.Max(0, team.Tactics.Tempo - 65) * 0.08;
        return Math.Round(Math.Clamp(65 + averagePassing * 0.24 - tempoPenalty, 62, 92), 1);
    }

    private sealed record TeamStrengthSnapshot(
        double HomeAttackStrength,
        double AwayAttackStrength,
        double HomeDefenseStrength,
        double AwayDefenseStrength,
        TeamDiagnosticSnapshot HomeDiagnostics,
        TeamDiagnosticSnapshot AwayDiagnostics);

    private sealed record TeamDiagnosticSnapshot(
        double BaseAttack,
        double AttackTacticalModifier,
        double AttackFatiguePenalty,
        double AttackRandomSwing,
        double BaseDefense,
        double DefenseTacticalModifier,
        double DefenseFatiguePenalty,
        double DefenseRandomSwing);

    private sealed class MatchSimulationState(
        Match match,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        int homePossessionMoments,
        int awayPossessionMoments,
        MatchSimulationOptions options)
    {
        public Match Match { get; } = match;
        public MatchLogService MatchLog { get; } = matchLog;
        public MatchEventFactory EventFactory { get; } = eventFactory;
        public MatchSimulationOptions Options { get; } = options;
        public Dictionary<string, int> SuperSubBoosts { get; } = [];
        public int HomePossessionMoments { get; set; } = homePossessionMoments;
        public int AwayPossessionMoments { get; set; } = awayPossessionMoments;
    }
}
