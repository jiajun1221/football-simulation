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

        var simulationState = new MatchSimulationState(
            match,
            CreateMatchLog(match),
            new MatchEventFactory(),
            match.HomePossessionMoments > 0 || match.AwayPossessionMoments > 0
                ? match.HomePossessionMoments
                : EstimatePossessionMoments(match.HomeStats.PossessionPercentage, MatchConstants.HalftimeMinute),
            match.HomePossessionMoments > 0 || match.AwayPossessionMoments > 0
                ? match.AwayPossessionMoments
                : MatchConstants.HalftimeMinute - EstimatePossessionMoments(match.HomeStats.PossessionPercentage, MatchConstants.HalftimeMinute),
            CreateOptions(options));
        InitializeAttackFlowState(simulationState);
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

    public Match CreateLiveMatch(Team homeTeam, Team awayTeam, MatchSimulationOptions? options = null)
    {
        ValidateTeam(homeTeam, nameof(homeTeam));
        ValidateTeam(awayTeam, nameof(awayTeam));

        ResetTeamStamina(homeTeam);
        ResetTeamStamina(awayTeam);

        var match = CreateMatch(homeTeam, awayTeam);
        match.CurrentPhase = MatchPhase.NotStarted;
        match.CurrentMinute = 0;
        return match;
    }

    public Match AdvanceMatch(
        Match match,
        int startMinute,
        int endMinute,
        bool includeFulltime,
        int? seed = null,
        MatchSimulationOptions? options = null)
    {
        ValidateTeam(match.HomeTeam, nameof(match.HomeTeam));
        ValidateTeam(match.AwayTeam, nameof(match.AwayTeam));

        if (startMinute < 1 || endMinute < startMinute)
        {
            throw new ArgumentOutOfRangeException(nameof(startMinute), "Minute range is invalid.");
        }

        var simulationState = new MatchSimulationState(
            match,
            CreateMatchLog(match),
            new MatchEventFactory(),
            match.HomePossessionMoments,
            match.AwayPossessionMoments,
            CreateOptions(options));
        InitializeAttackFlowState(simulationState);

        foreach (var boost in match.SuperSubBoosts)
        {
            simulationState.SuperSubBoosts[boost.Key] = boost.Value;
        }

        EnsurePlayerPerformances(match);
        RunMatchMinutes(simulationState, CreateRandom(seed), startMinute, endMinute, includeFulltime);
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
        InitializeAttackFlowState(simulationState);

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

            if (minute == 1)
            {
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateKickoff(minute, homeTeam));
                SetPossession(simulationState, homeTeam, BallState.Kickoff, EventType.Kickoff);
            }

            var strengthSnapshot = CreateStrengthSnapshot(homeTeam, awayTeam, random);
            WriteStrengthDiagnostics(match, minute, strengthSnapshot);
            if (minute > 1)
            {
                strengthSnapshot = ApplyDramaEventIfAny(simulationState, random, strengthSnapshot, minute);
            }

            var attackingTeam = GetPossessionTeam(simulationState) ?? ChooseAttackingTeam(
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

            var nextPossessionTeam = ProcessAttack(
                minute,
                simulationState,
                attackingTeam,
                random,
                strengthSnapshot);

            if (nextPossessionTeam is not null)
            {
                SetPossession(simulationState, nextPossessionTeam, simulationState.BallState, simulationState.LastFeedEventType);
            }

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

            if (match.CurrentPhase != MatchPhase.Fulltime)
            {
                ApplyMinuteFatigue(homeTeam, simulationState.Match, simulationState.Options);
                ApplyMinuteFatigue(awayTeam, simulationState.Match, simulationState.Options);
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

    private static void InitializeAttackFlowState(MatchSimulationState simulationState)
    {
        var lastEvent = simulationState.Match.Events.LastOrDefault(IsPossessionStateEvent);
        if (lastEvent is null)
        {
            SetPossession(simulationState, simulationState.Match.HomeTeam, BallState.Kickoff, null);
            return;
        }

        simulationState.LastFeedEventType = lastEvent.EventType;
        simulationState.LastAttackingTeamName = lastEvent.EventType == EventType.Attack
            ? FindEventTeamName(lastEvent, simulationState.Match)
            : null;
        var possessionTeam = InferPossessionAfterEvent(lastEvent, simulationState.Match);
        SetPossession(simulationState, possessionTeam, GetBallStateAfterEvent(lastEvent.EventType), lastEvent.EventType);
    }

    private static bool IsPossessionStateEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType is EventType.Kickoff
            or EventType.Attack
            or EventType.Foul
            or EventType.Shot
            or EventType.Goal
            or EventType.Miss
            or EventType.Save
            or EventType.PenaltyDecision
            or EventType.PenaltyTaker
            or EventType.Penalty
            or EventType.Offside
            or EventType.BadPass
            or EventType.Miscontrol
            or EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass
            or EventType.Turnover
            or EventType.DefensiveStop
            or EventType.WonderGoal
            or EventType.CornerKick
            or EventType.SetPieceDanger;
    }

    private static Team? GetPossessionTeam(MatchSimulationState simulationState)
    {
        return simulationState.PossessionTeamName == simulationState.Match.HomeTeam.Name
            ? simulationState.Match.HomeTeam
            : simulationState.PossessionTeamName == simulationState.Match.AwayTeam.Name
                ? simulationState.Match.AwayTeam
                : null;
    }

    private static bool HasUnresolvedAttack(MatchSimulationState simulationState, Team attackingTeam)
    {
        return simulationState.LastFeedEventType == EventType.Attack &&
            string.Equals(simulationState.LastAttackingTeamName, attackingTeam.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkAttackStarted(MatchSimulationState simulationState, Team attackingTeam)
    {
        simulationState.LastAttackingTeamName = attackingTeam.Name;
        SetPossession(simulationState, attackingTeam, BallState.Attack, EventType.Attack);
    }

    private static void MarkAttackResolved(MatchSimulationState simulationState, Team possessionTeam, BallState ballState, EventType outcomeType)
    {
        simulationState.LastAttackingTeamName = null;
        SetPossession(simulationState, possessionTeam, ballState, outcomeType);
    }

    private static void SetPossession(MatchSimulationState simulationState, Team team, BallState ballState, EventType? eventType)
    {
        simulationState.PossessionTeamName = team.Name;
        simulationState.BallState = ballState;
        simulationState.LastActingTeamName = team.Name;
        simulationState.LastFeedEventType = eventType;
    }

    private static Team InferPossessionAfterEvent(MatchEvent matchEvent, Match match)
    {
        var actingTeam = FindEventTeamName(matchEvent, match);
        var actingTeamModel = actingTeam == match.AwayTeam.Name ? match.AwayTeam : match.HomeTeam;
        var opposingTeam = actingTeamModel == match.HomeTeam ? match.AwayTeam : match.HomeTeam;

        return matchEvent.EventType switch
        {
            EventType.Attack => actingTeamModel,
            EventType.Foul => opposingTeam,
            EventType.PenaltyDecision => opposingTeam,
            EventType.Turnover => actingTeamModel,
            EventType.BadPass or EventType.Miscontrol => actingTeamModel,
            EventType.Tackle or EventType.Interception or EventType.Pressure or EventType.BlockedPass => opposingTeam,
            EventType.DefensiveStop => actingTeamModel,
            EventType.Save => actingTeamModel,
            EventType.Goal => opposingTeam,
            EventType.WonderGoal => opposingTeam,
            EventType.Shot => opposingTeam,
            EventType.Miss => opposingTeam,
            EventType.Offside => opposingTeam,
            EventType.CornerKick => actingTeamModel,
            EventType.SetPieceDanger => actingTeamModel,
            EventType.Kickoff => match.HomeTeam,
            _ => actingTeamModel
        };
    }

    private static BallState GetBallStateAfterEvent(EventType eventType)
    {
        return eventType switch
        {
            EventType.Kickoff => BallState.Kickoff,
            EventType.Attack => BallState.AttackPending,
            EventType.Shot or EventType.Save or EventType.Goal or EventType.Miss or EventType.WonderGoal => BallState.Chance,
            EventType.CornerKick => BallState.CornerPending,
            EventType.SetPieceDanger => BallState.SetPiecePending,
            EventType.Foul or EventType.Offside or EventType.PenaltyDecision or EventType.PenaltyTaker or EventType.Penalty => BallState.SetPiece,
            EventType.Turnover => BallState.Turnover,
            EventType.BadPass or EventType.Miscontrol or EventType.Tackle or EventType.Interception or EventType.Pressure or EventType.BlockedPass => BallState.Turnover,
            EventType.DefensiveStop => BallState.Defending,
            _ => BallState.BuildUp
        };
    }

    private static string? FindEventTeamName(MatchEvent matchEvent, Match match)
    {
        var primaryPlayerTeam = FindPlayerTeamName(matchEvent.PrimaryPlayerName, match);
        var secondaryPlayerTeam = FindPlayerTeamName(matchEvent.SecondaryPlayerName, match);

        switch (matchEvent.EventType)
        {
            case EventType.Turnover:
            case EventType.Attack:
            case EventType.Shot:
            case EventType.Miss:
            case EventType.Goal:
            case EventType.WonderGoal:
            case EventType.Offside:
            case EventType.BadPass:
            case EventType.Miscontrol:
            case EventType.CornerKick:
            case EventType.SetPieceDanger:
            case EventType.Penalty:
            case EventType.PenaltyTaker:
                return primaryPlayerTeam ?? FindMentionedTeamName(matchEvent, match);

            case EventType.Save:
                return secondaryPlayerTeam ?? FindSavingTeamName(matchEvent, match) ?? FindMentionedTeamName(matchEvent, match);

            case EventType.Foul:
            case EventType.PenaltyDecision:
            case EventType.YellowCard:
            case EventType.RedCard:
            case EventType.DefensiveStop:
            case EventType.DefensiveError:
            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
                return primaryPlayerTeam ?? FindMentionedTeamName(matchEvent, match);
        }

        return FindMentionedTeamName(matchEvent, match);
    }

    private static string? FindMentionedTeamName(MatchEvent matchEvent, Match match)
    {
        if (EventMentionsTeam(matchEvent.Description, match.HomeTeam.Name))
        {
            return match.HomeTeam.Name;
        }

        if (EventMentionsTeam(matchEvent.Description, match.AwayTeam.Name))
        {
            return match.AwayTeam.Name;
        }

        return null;
    }

    private static string? FindSavingTeamName(MatchEvent matchEvent, Match match)
    {
        if (matchEvent.Description.Contains($"by {match.HomeTeam.Name}", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains($"for {match.HomeTeam.Name}", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains($"of {match.HomeTeam.Name}", StringComparison.OrdinalIgnoreCase))
        {
            return match.HomeTeam.Name;
        }

        if (matchEvent.Description.Contains($"by {match.AwayTeam.Name}", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains($"for {match.AwayTeam.Name}", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains($"of {match.AwayTeam.Name}", StringComparison.OrdinalIgnoreCase))
        {
            return match.AwayTeam.Name;
        }

        return null;
    }

    private static string? FindPlayerTeamName(string? playerName, Match match)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        if (match.HomeTeam.Players.Concat(match.HomeTeam.Substitutes).Any(player =>
            string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return match.HomeTeam.Name;
        }

        if (match.AwayTeam.Players.Concat(match.AwayTeam.Substitutes).Any(player =>
            string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return match.AwayTeam.Name;
        }

        return null;
    }

    private static bool EventMentionsTeam(string description, string teamName)
    {
        return description.StartsWith($"{teamName} ", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" for {teamName}", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" by {teamName}", StringComparison.OrdinalIgnoreCase);
    }

    private static void FinalizeSimulationState(MatchSimulationState simulationState)
    {
        SetPossessionStats(
            simulationState.Match,
            simulationState.HomePossessionMoments,
            simulationState.AwayPossessionMoments);
        SetPassingStats(simulationState.Match);

        simulationState.Match.HomePossessionMoments = simulationState.HomePossessionMoments;
        simulationState.Match.AwayPossessionMoments = simulationState.AwayPossessionMoments;
        simulationState.Match.SuperSubBoosts = simulationState.SuperSubBoosts.ToDictionary(
            entry => entry.Key,
            entry => entry.Value);
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
            CurrentPhase = MatchPhase.NotStarted,
            HomePossessionMoments = 0,
            AwayPossessionMoments = 0,
            SuperSubBoosts = []
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
        var activePlayers = GetActivePitchPlayers(team).ToList();
        if (activePlayers.Count == 0)
        {
            return 0;
        }

        return activePlayers.Average(player => player.Position switch
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
        var activePlayers = GetActivePitchPlayers(team).ToList();
        if (activePlayers.Count == 0)
        {
            return 0;
        }

        return activePlayers.Average(player => player.Position switch
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
        var activePlayers = GetActivePitchPlayers(team).ToList();
        if (activePlayers.Count == 0)
        {
            return 0.75;
        }

        var averageStaminaRatio = activePlayers.Average(player => Math.Clamp(player.Stamina / 100.0, 0.0, 1.0));

        var averageFatiguePenalty = activePlayers.Average(player => (100.0 - player.Stamina) / 125.0);

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
        if (simulationState.LastFeedEventType == EventType.Turnover)
        {
            return strengthSnapshot;
        }

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

    private Team? ProcessAttack(
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
        var attackOutcome = DetermineAttackOutcome(attackingTeamStrength, defendingTeamStrength, random);
        var isResolvingPreviousAttack = HasUnresolvedAttack(simulationState, attackingTeam);
        var isResolvingTurnover = simulationState.LastFeedEventType == EventType.Turnover;

        if (isResolvingTurnover)
        {
            attackOutcome = AttackFlowOutcome.BuildUp;
        }

        if (attackOutcome == AttackFlowOutcome.BuildUp)
        {
            if (isResolvingPreviousAttack)
            {
                attackOutcome = random.NextDouble() < 0.65
                    ? AttackFlowOutcome.CreateChance
                    : AttackFlowOutcome.LosePossession;
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateAttackBuildUp(minute, attackingTeam, playmaker, shooter, random));
                MarkAttackStarted(simulationState, attackingTeam);
                GetOrCreatePerformance(match, attackingTeam, playmaker).Rating += 0.04;
                return attackingTeam;
            }
        }

        if (attackOutcome == AttackFlowOutcome.LosePossession)
        {
            if (!isResolvingPreviousAttack)
            {
                matchLog.AddEvent(eventFactory.CreateAttackBuildUp(minute, attackingTeam, playmaker, shooter, random));
                MarkAttackStarted(simulationState, attackingTeam);
                GetOrCreatePerformance(match, attackingTeam, playmaker).Rating += 0.04;
            }

            HandlePossessionLoss(minute, match, attackingTeam, defendingTeam, playmaker, shooter, random, matchLog, eventFactory);
            MarkAttackResolved(simulationState, defendingTeam, BallState.Turnover, EventType.Turnover);
            return defendingTeam;
        }

        if (attackOutcome == AttackFlowOutcome.ForcedReset)
        {
            var defender = ApplyDefensiveContribution(match, defendingTeam, random, ratingBoostOverride: 0.05);
            matchLog.AddEvent(eventFactory.CreateAttackReset(minute, attackingTeam, defendingTeam, defender, random));
            MarkAttackResolved(simulationState, defendingTeam, BallState.Defending, EventType.DefensiveStop);
            return defendingTeam;
        }

        var chanceType = ChooseChanceType(random);
        if (!isResolvingPreviousAttack)
        {
            matchLog.AddEvent(eventFactory.CreateAttackBuildUp(minute, attackingTeam, playmaker, shooter, random));
            MarkAttackStarted(simulationState, attackingTeam);
        }

        if (chanceType == "through ball attempt" && random.NextDouble() < 0.18)
        {
            attackingStats.Offsides++;
            var shooterPerformance = GetOrCreatePerformance(match, attackingTeam, shooter);
            shooterPerformance.Offsides++;
            shooterPerformance.Rating -= 0.08;
            matchLog.AddEvent(eventFactory.CreateOffside(minute, attackingTeam, shooter, chanceType, random));
            MarkAttackResolved(simulationState, defendingTeam, BallState.SetPiece, EventType.Offside);
            return defendingTeam;
        }

        if (ShouldCommitFoul(defendingTeam, attackingTeamStrength, defendingTeamStrength, random))
        {
            var foulContext = CreateFoulContext(chanceType, attackingTeamStrength, defendingTeamStrength, random);
            var possessionAfterFoul = HandleFoul(minute, match, attackingTeam, defendingTeam, shooter, random, matchLog, eventFactory, defendingStats, foulContext);
            var finalEventType = GetLastEventType(matchLog, foulContext.IsPenalty
                ? EventType.Penalty
                : foulContext.CreatesDangerousSetPiece
                    ? EventType.SetPieceDanger
                    : EventType.Foul);
            MarkAttackResolved(
                simulationState,
                possessionAfterFoul,
                GetBallStateAfterEvent(finalEventType),
                finalEventType);
            return possessionAfterFoul;
        }

        var defensiveErrorBoost = 0.0;
        if (random.NextDouble() < 0.12)
        {
            var errorPlayer = ChooseDefendingPlayer(defendingTeam, random);
            matchLog.AddEvent(eventFactory.CreateDefensiveError(minute, defendingTeam, errorPlayer));
            GetOrCreatePerformance(match, defendingTeam, errorPlayer).Rating -= 0.30;
            defensiveErrorBoost = 0.06;
        }

        if (random.NextDouble() < 0.14)
        {
            HandlePossessionLoss(minute, match, attackingTeam, defendingTeam, playmaker, shooter, random, matchLog, eventFactory);
            MarkAttackResolved(simulationState, defendingTeam, BallState.Turnover, EventType.Turnover);
            return defendingTeam;
        }

        attackingStats.TotalShots++;
        matchLog.AddEvent(eventFactory.CreateShot(minute, attackingTeam, shooter, playmaker, chanceType, random));
        var shotPossessionTeam = HandleShotOutcome(
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
            shooterHasSuperSubBoost,
            chanceType,
            defensiveErrorBoost);
        var shotFinalEventType = GetLastEventType(matchLog, EventType.Shot);
        MarkAttackResolved(simulationState, shotPossessionTeam, GetBallStateAfterEvent(shotFinalEventType), shotFinalEventType);
        return shotPossessionTeam;
    }

    private void HandlePossessionLoss(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player playmaker,
        Player shooter,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        var reasonType = ChoosePossessionLossReason(random);
        var attacker = random.NextDouble() < 0.65 ? playmaker : shooter;
        Player? defender = null;

        if (IsDefenderPossessionWin(reasonType))
        {
            defender = ChooseDefendingPlayer(defendingTeam, random);
            ApplyPossessionWinContribution(match, defendingTeam, defender, reasonType);
        }
        else
        {
            GetOrCreatePerformance(match, attackingTeam, attacker).Rating -= 0.08;
        }

        matchLog.AddEvent(eventFactory.CreatePossessionLossReason(
            minute,
            attackingTeam,
            defendingTeam,
            attacker,
            defender,
            reasonType,
            random));

        var possessionPlayer = defender ?? ChooseDefendingPlayer(defendingTeam, random);
        matchLog.AddEvent(eventFactory.CreateTurnover(minute, defendingTeam, possessionPlayer, random));
    }

    private static EventType ChoosePossessionLossReason(Random random)
    {
        var roll = random.NextDouble();
        return roll switch
        {
            < 0.22 => EventType.BadPass,
            < 0.40 => EventType.Miscontrol,
            < 0.58 => EventType.Tackle,
            < 0.76 => EventType.Interception,
            < 0.90 => EventType.Pressure,
            _ => EventType.BlockedPass
        };
    }

    private static bool IsDefenderPossessionWin(EventType eventType)
    {
        return eventType is EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass;
    }

    private static void ApplyPossessionWinContribution(Match match, Team defendingTeam, Player defender, EventType reasonType)
    {
        var performance = GetOrCreatePerformance(match, defendingTeam, defender);
        switch (reasonType)
        {
            case EventType.Tackle:
                performance.Tackles++;
                performance.Rating += 0.08;
                break;

            case EventType.Interception:
                performance.Interceptions++;
                performance.Rating += 0.08;
                break;

            case EventType.BlockedPass:
                performance.Blocks++;
                performance.Rating += 0.09;
                break;

            case EventType.Pressure:
                performance.Tackles++;
                performance.Rating += 0.06;
                break;
        }
    }

    private Team HandleFoul(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player fouledPlayer,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        MatchTeamStats defendingStats,
        FoulContext foulContext)
    {
        var defender = ChooseDefendingPlayer(defendingTeam, random);
        matchLog.AddEvent(eventFactory.CreateFoul(minute, defendingTeam, defender, fouledPlayer));
        defendingStats.Fouls++;
        var defenderPerformance = GetOrCreatePerformance(match, defendingTeam, defender);
        defenderPerformance.Fouls++;
        defenderPerformance.Rating -= 0.15;
        GetOrCreatePerformance(match, GetOpposingTeam(match, defendingTeam), fouledPlayer).Rating += 0.05;

        var redCardReason = GetStraightRedReason(foulContext, defender, random);
        if (redCardReason is not null)
        {
            ApplyRedCard(minute, match, defendingTeam, attackingTeam, defender, defenderPerformance, defendingStats, matchLog, eventFactory, redCardReason);
        }
        else if (ShouldGiveYellowCard(defender, random))
        {
            matchLog.AddEvent(eventFactory.CreateYellowCard(minute, defender));
            defenderPerformance.YellowCards++;
            defenderPerformance.Rating -= 0.35;

            if (_disciplinaryService.ApplyYellowCard(defender, defendingStats))
            {
                ApplyRedCard(minute, match, defendingTeam, attackingTeam, defender, defenderPerformance, defendingStats, matchLog, eventFactory, "second yellow");
            }
        }

        if (foulContext.IsPenalty)
        {
            return HandlePenaltySequence(minute, match, attackingTeam, defendingTeam, defender, fouledPlayer, random, matchLog, eventFactory, foulContext);
        }

        if (foulContext.CreatesDangerousSetPiece)
        {
            return HandleDangerousSetPieceSequence(minute, match, attackingTeam, defendingTeam, random, matchLog, eventFactory);
        }

        return attackingTeam;
    }

    private Team HandleDangerousSetPieceSequence(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        var (primaryTaker, secondaryTaker) = ChooseSetPieceTakers(attackingTeam);
        matchLog.AddEvent(eventFactory.CreateSetPieceThreat(minute, attackingTeam, primaryTaker, secondaryTaker));
        matchLog.AddEvent(eventFactory.CreateSetPieceShot(minute, attackingTeam, primaryTaker));

        var attackingStats = GetTeamStats(match, attackingTeam);
        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += 0.09;
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, primaryTaker);
        takerPerformance.Shots++;
        takerPerformance.KeyPasses++;
        takerPerformance.Rating += 0.12;

        var goalProbability = Math.Clamp(0.04 + primaryTaker.Finishing / 950.0 +
            (primaryTaker.Traits.Contains(PlayerTrait.SetPieceSpecialist) ? 0.04 : 0.0), 0.05, 0.16);
        var roll = random.NextDouble();
        if (roll < goalProbability)
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            takerPerformance.ShotsOnTarget++;
            takerPerformance.Goals++;
            takerPerformance.Rating += 1.15;
            matchLog.AddEvent(eventFactory.CreateGoal(minute, attackingTeam, primaryTaker, match, "direct free-kick strike", random));
            return defendingTeam;
        }

        if (roll < goalProbability + 0.28)
        {
            attackingStats.ShotsOnTarget++;
            takerPerformance.ShotsOnTarget++;
            var goalkeeper = GetGoalkeeper(defendingTeam);
            if (goalkeeper is not null)
            {
                var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                goalkeeperPerformance.Saves++;
                goalkeeperPerformance.Rating += 0.35;
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, primaryTaker, goalkeeper, ChooseSaveType(random), random));
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, primaryTaker));
            }

            return defendingTeam;
        }

        if (roll < goalProbability + 0.55)
        {
            var defender = ApplyDefensiveContribution(match, defendingTeam, random, ratingBoostOverride: 0.08, allowBlocks: true);
            matchLog.AddEvent(eventFactory.CreateDefensiveStop(minute, defendingTeam, defender, primaryTaker, random));
            return random.NextDouble() < 0.25
                ? HandleCornerSequence(minute, match, attackingTeam, defendingTeam, primaryTaker, random, matchLog, eventFactory)
                : defendingTeam;
        }

        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, primaryTaker, "free-kick", random));
        takerPerformance.Rating -= 0.04;
        return defendingTeam;
    }

    private Team HandlePenaltySequence(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player defender,
        Player fouledPlayer,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        FoulContext foulContext)
    {
        var taker = ChoosePenaltyTaker(attackingTeam);
        var converted = random.NextDouble() < GetPenaltyConversionChance(taker);
        var saved = !converted && random.NextDouble() < 0.62;

        matchLog.AddEvent(eventFactory.CreatePenaltyDecision(minute, defendingTeam, defender, fouledPlayer, foulContext.PenaltyReason));
        matchLog.AddEvent(eventFactory.CreatePenaltyTaker(minute, attackingTeam, taker));

        var attackingStats = GetTeamStats(match, attackingTeam);
        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += 0.76;
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, taker);
        takerPerformance.Shots++;

        if (converted)
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            takerPerformance.ShotsOnTarget++;
            takerPerformance.Goals++;
            takerPerformance.Rating += 1.05;
        }
        else
        {
            takerPerformance.Rating -= 0.35;
            if (saved)
            {
                attackingStats.ShotsOnTarget++;
                var goalkeeper = GetGoalkeeper(defendingTeam);
                if (goalkeeper is not null)
                {
                    var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                    goalkeeperPerformance.Saves++;
                    goalkeeperPerformance.Rating += 0.80;
                }
            }
        }

        matchLog.AddEvent(eventFactory.CreatePenaltyResult(minute, attackingTeam, taker, converted, saved, match));
        return defendingTeam;
    }

    private Team HandleShotOutcome(
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
        bool shooterHasSuperSubBoost,
        string chanceType,
        double defensiveErrorBoost)
    {
        var goalProbability = Math.Clamp(
            CalculateGoalProbability(shooter, attackingTeamStrength, defendingTeamStrength, random, shooterHasSuperSubBoost) + defensiveErrorBoost,
            MatchConstants.MinimumGoalProbability,
            0.72);
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

            var isOwnGoal = random.NextDouble() < 0.03;
            if (isOwnGoal)
            {
                var ownGoalPlayer = ChooseDefendingPlayer(defendingTeam, random);
                var ownGoalPerformance = GetOrCreatePerformance(match, defendingTeam, ownGoalPlayer);
                ownGoalPerformance.Rating -= 0.80;
                ownGoalPerformance.Clearances++;
                shooterPerformance.Rating += 0.25;
                matchLog.AddEvent(eventFactory.CreateOwnGoal(minute, attackingTeam, defendingTeam, ownGoalPlayer, match));
                return defendingTeam;
            }

            shooterPerformance.Goals++;
            shooterPerformance.Rating += 1.0 + goalProbability;

            var assister = playmaker != shooter ? playmaker : null;
            if (assister is not null)
            {
                var assisterPerformance = GetOrCreatePerformance(match, attackingTeam, assister);
                assisterPerformance.Assists++;
                assisterPerformance.Rating += 0.75;
            }

            var goalTypeDescription = ChooseGoalTypeDescription(random, chanceType, chanceType.Contains("corner", StringComparison.OrdinalIgnoreCase));
            matchLog.AddEvent(eventFactory.CreateGoal(minute, attackingTeam, shooter, match, goalTypeDescription, random, assister));
            return defendingTeam;
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
                var saveType = ChooseSaveType(random);
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, shooter, goalkeeper, saveType, random));
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, shooter));
            }

            if (random.NextDouble() < 0.20)
            {
                attackingStats.Corners++;
                return HandleCornerSequence(minute, match, attackingTeam, defendingTeam, playmaker, random, matchLog, eventFactory);
            }
            return defendingTeam;
        }

        shooterPerformance.Rating -= goalProbability >= 0.20 ? 0.12 : 0.04;
        var defender = ApplyDefensiveContribution(match, defendingTeam, random, ratingBoostOverride: 0.06, allowBlocks: true);
        matchLog.AddEvent(eventFactory.CreateDefensiveStop(minute, defendingTeam, defender, shooter, random));
        if (random.NextDouble() < 0.12)
        {
            attackingStats.Corners++;
            return HandleCornerSequence(minute, match, attackingTeam, defendingTeam, playmaker, random, matchLog, eventFactory);
        }

        var shotStyle = ChooseShotStyle(random, chanceType);
        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, shooter, shotStyle, random));
        return defendingTeam;
    }

    private Team HandleCornerSequence(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player preferredTaker,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        var taker = ChooseCornerTaker(attackingTeam, preferredTaker);
        matchLog.AddEvent(eventFactory.CreateCornerKick(minute, attackingTeam, taker));
        var target = ChooseShooter(attackingTeam, taker, random);
        var attackingStats = GetTeamStats(match, attackingTeam);
        var targetPerformance = GetOrCreatePerformance(match, attackingTeam, target);
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, taker);

        takerPerformance.KeyPasses++;
        takerPerformance.Rating += 0.08;

        var roll = random.NextDouble();
        if (roll < 0.12)
        {
            attackingStats.TotalShots++;
            attackingStats.ShotsOnTarget++;
            attackingStats.ExpectedGoals += 0.12;
            UpdateScore(match, attackingTeam);
            targetPerformance.Shots++;
            targetPerformance.ShotsOnTarget++;
            targetPerformance.Goals++;
            targetPerformance.Rating += 1.05;
            if (target != taker)
            {
                takerPerformance.Assists++;
                takerPerformance.Rating += 0.60;
            }

            matchLog.AddEvent(eventFactory.CreateGoal(minute, attackingTeam, target, match, "header from a corner", random, target == taker ? null : taker));
            return defendingTeam;
        }

        if (roll < 0.34)
        {
            attackingStats.TotalShots++;
            attackingStats.ShotsOnTarget++;
            attackingStats.ExpectedGoals += 0.08;
            targetPerformance.Shots++;
            targetPerformance.ShotsOnTarget++;
            matchLog.AddEvent(eventFactory.CreateShot(minute, attackingTeam, target, taker, "corner delivery", random));
            var goalkeeper = GetGoalkeeper(defendingTeam);
            if (goalkeeper is not null)
            {
                var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                goalkeeperPerformance.Saves++;
                goalkeeperPerformance.Rating += 0.30;
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, target, goalkeeper, ChooseSaveType(random), random));
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, target));
            }

            return defendingTeam;
        }

        if (roll < 0.72)
        {
            var defender = ApplyDefensiveContribution(match, defendingTeam, random, ratingBoostOverride: 0.08, allowBlocks: false);
            matchLog.AddEvent(eventFactory.CreateDefensiveStop(minute, defendingTeam, defender, target, random));
            return defendingTeam;
        }

        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += 0.05;
        targetPerformance.Shots++;
        matchLog.AddEvent(eventFactory.CreateShot(minute, attackingTeam, target, taker, "corner delivery", random));
        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, target, "corner chance", random));
        targetPerformance.Rating -= 0.04;
        return defendingTeam;
    }

    private static EventType GetLastEventType(MatchLogService matchLog, EventType fallback)
    {
        return matchLog.GetEvents().LastOrDefault()?.EventType ?? fallback;
    }

    private static AttackFlowOutcome DetermineAttackOutcome(
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random)
    {
        var attackBalance = attackingTeamStrength / Math.Max(1.0, attackingTeamStrength + defendingTeamStrength);
        var chanceProbability = Math.Clamp(0.20 + attackBalance * 0.40 + GetProbabilitySwing(random, 0.06), 0.18, 0.62);
        var buildupProbability = Math.Clamp(0.20 + (1.0 - attackBalance) * 0.18 + GetProbabilitySwing(random, 0.06), 0.14, 0.40);
        var turnoverProbability = Math.Clamp(0.30 + (0.55 - attackBalance) * 0.25 + GetProbabilitySwing(random, 0.05), 0.16, 0.50);

        var roll = random.NextDouble();
        if (roll < chanceProbability)
        {
            return AttackFlowOutcome.CreateChance;
        }

        if (roll < chanceProbability + buildupProbability)
        {
            return AttackFlowOutcome.BuildUp;
        }

        if (roll < chanceProbability + buildupProbability + turnoverProbability)
        {
            return AttackFlowOutcome.LosePossession;
        }

        return AttackFlowOutcome.ForcedReset;
    }

    private static string ChooseChanceType(Random random)
    {
        return random.Next(5) switch
        {
            0 => "long-range attempt",
            1 => "cross into box",
            2 => "through ball attempt",
            3 => "dribble run",
            _ => "quick combination"
        };
    }

    private static string ChooseSaveType(Random random)
    {
        return random.Next(3) switch
        {
            0 => "diving save",
            1 => "reflex save",
            _ => "parry"
        };
    }

    private static string ChooseShotStyle(Random random, string chanceType)
    {
        var styles = new List<string>
        {
            "finesse shot",
            "power shot",
            "low driven effort",
            "outside-foot strike"
        };

        if (chanceType == "cross into box")
        {
            styles.Add("header");
            styles.Add("volley");
            styles.Add("tap-in");
        }
        else if (chanceType == "through ball attempt")
        {
            styles.Add("chip shot");
        }
        else if (chanceType == "dribble run")
        {
            styles.Add("solo run finish");
        }

        if (random.NextDouble() < 0.08)
        {
            styles.Add("acrobatic attempt");
        }

        return styles[random.Next(styles.Count)];
    }

    private static string ChooseGoalTypeDescription(Random random, string chanceType, bool hasCornerPressure)
    {
        var options = new List<string>
        {
            "finesse shot from outside the box",
            "power shot into the top corner",
            "low driven finish across goal",
            "outside-foot trivela finish",
            "counterattack goal after a rapid break",
            "solo run finish through the middle",
            "scrappy rebound finish in the six-yard box"
        };

        if (chanceType == "cross into box")
        {
            options.Add("towering header from a cross");
            options.Add("half-volley from a floated delivery");
            options.Add("tap-in at the far post");
        }

        if (chanceType == "through ball attempt")
        {
            options.Add("chip shot over the goalkeeper");
        }

        if (hasCornerPressure || random.NextDouble() < 0.10)
        {
            options.Add("header from a corner");
        }

        if (random.NextDouble() < 0.06)
        {
            options.Add("direct free-kick strike");
        }

        if (random.NextDouble() < 0.03)
        {
            options.Add("acrobatic overhead effort");
        }

        return options[random.Next(options.Count)];
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
                    performance.Blocks++;
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

    private void TrackDefensiveStop(Match match, Team defendingTeam, Random random)
    {
        _ = ApplyDefensiveContribution(match, defendingTeam, random);
    }

    private Player ApplyDefensiveContribution(
        Match match,
        Team defendingTeam,
        Random random,
        double? ratingBoostOverride = null,
        bool allowBlocks = false)
    {
        var defender = ChooseDefendingPlayer(defendingTeam, random);
        var performance = GetOrCreatePerformance(match, defendingTeam, defender);
        var ratingBoost = ratingBoostOverride ?? 0.07;

        var roll = random.NextDouble();
        if (allowBlocks && roll < 0.22)
        {
            performance.Blocks++;
            performance.Rating += ratingBoost + 0.03;
            return defender;
        }

        if (roll < 0.52)
        {
            performance.Interceptions++;
            performance.Rating += ratingBoost;
            return defender;
        }

        if (roll < 0.82)
        {
            performance.Tackles++;
            performance.Rating += ratingBoost + 0.01;
            return defender;
        }

        performance.Clearances++;
        performance.Rating += Math.Max(0.05, ratingBoost - 0.01);
        return defender;
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
        return 100 - Math.Clamp((int)Math.Round(player.Stamina), 0, 100);
    }

    private static Team GetOpposingTeam(Match match, Team team)
    {
        return team == match.HomeTeam ? match.AwayTeam : match.HomeTeam;
    }

    private static Player? GetGoalkeeper(Team team)
    {
        return GetActivePitchPlayers(team).FirstOrDefault(player => player.Position == Position.Goalkeeper);
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
        var aggressionBonus = GetActivePitchPlayers(defendingTeam)
            .Count(player => player.Traits.Contains(PlayerTrait.AggressiveTackler)) * 0.015;
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

    private static FoulContext CreateFoulContext(
        string chanceType,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random)
    {
        var isBoxThreat = chanceType is "cross into box" or "through ball attempt" or "dribble run" or "quick combination";
        var attackPressure = attackingTeamStrength / Math.Max(1.0, attackingTeamStrength + defendingTeamStrength);
        var penaltyProbability = isBoxThreat
            ? Math.Clamp(0.16 + attackPressure * 0.18, 0.12, 0.34)
            : 0.0;
        var handball = isBoxThreat && random.NextDouble() < 0.06;
        var isPenalty = handball || random.NextDouble() < penaltyProbability;
        var deniesClearChance = isBoxThreat && attackPressure > 0.52 && random.NextDouble() < 0.30;
        var violentFoul = random.NextDouble() < 0.035;
        var createsDangerousSetPiece = !isPenalty &&
            (isBoxThreat || chanceType is "long-range effort" or "wide overload") &&
            random.NextDouble() < 0.72;
        var reason = handball ? "handles the ball near" : "fouls";

        return new FoulContext(isPenalty, reason, deniesClearChance, violentFoul, createsDangerousSetPiece);
    }

    private static string? GetStraightRedReason(FoulContext foulContext, Player defender, Random random)
    {
        if (foulContext.ViolentFoul)
        {
            return "violent foul";
        }

        if (foulContext.DeniesClearChance && random.NextDouble() < (foulContext.IsPenalty ? 0.55 : 0.70))
        {
            return "denying a clear scoring chance";
        }

        return null;
    }

    private static void ApplyRedCard(
        int minute,
        Match match,
        Team sentOffTeam,
        Team opponentTeam,
        Player player,
        PlayerMatchPerformance performance,
        MatchTeamStats sentOffTeamStats,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        string reason)
    {
        if (!player.IsSentOff)
        {
            player.IsSentOff = true;
            sentOffTeamStats.RedCards++;
        }

        player.IsOnPitch = false;
        player.RedCardMinute ??= minute;
        matchLog.AddEvent(eventFactory.CreateRedCard(minute, player, reason));
        performance.RedCards = Math.Max(performance.RedCards, 1);
        performance.Rating -= reason == "second yellow" ? 1.0 : 1.35;
        ApplyRedCardTacticalReaction(sentOffTeam, opponentTeam);
    }

    private static void ApplyRedCardTacticalReaction(Team sentOffTeam, Team opponentTeam)
    {
        sentOffTeam.Tactics.Mentality = Mentality.Defensive;
        sentOffTeam.Tactics.PressingIntensity = Math.Max(25, sentOffTeam.Tactics.PressingIntensity - 15);
        sentOffTeam.Tactics.Tempo = Math.Max(30, sentOffTeam.Tactics.Tempo - 12);
        sentOffTeam.Tactics.DefensiveLine = Math.Max(25, sentOffTeam.Tactics.DefensiveLine - 12);

        opponentTeam.Tactics.Mentality = Mentality.Attacking;
        opponentTeam.Tactics.PressingIntensity = Math.Min(90, opponentTeam.Tactics.PressingIntensity + 12);
        opponentTeam.Tactics.Tempo = Math.Min(90, opponentTeam.Tactics.Tempo + 10);
    }

    private static Player ChoosePenaltyTaker(Team team)
    {
        return GetActivePitchPlayers(team)
            .Where(player => !player.IsInjured)
            .OrderByDescending(player =>
                player.Finishing +
                player.CurrentForm * 0.25 +
                (player.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 8 : 0) +
                (player.Traits.Contains(PlayerTrait.SetPieceSpecialist) ? 6 : 0))
            .FirstOrDefault() ?? GetActivePitchPlayers(team).FirstOrDefault() ?? team.Players[0];
    }

    private static (Player Primary, Player Secondary) ChooseSetPieceTakers(Team team)
    {
        var activePlayers = GetActivePitchPlayers(team).ToList();
        if (activePlayers.Count == 0)
        {
            return (team.Players[0], team.Players[0]);
        }

        var takers = activePlayers
            .Where(player => !player.IsInjured)
            .OrderByDescending(player =>
                player.Passing * 1.10 +
                player.Finishing * 0.85 +
                player.CurrentForm * 0.25 +
                (player.Traits.Contains(PlayerTrait.SetPieceSpecialist) ? 14 : 0) +
                (player.Traits.Contains(PlayerTrait.LongShotTaker) ? 8 : 0))
            .Take(2)
            .ToList();

        if (takers.Count == 0)
        {
            return (activePlayers[0], activePlayers[0]);
        }

        return (takers[0], takers.Count > 1 ? takers[1] : takers[0]);
    }

    private static Player ChooseCornerTaker(Team team, Player preferredTaker)
    {
        return GetActivePitchPlayers(team)
            .Where(player => !player.IsInjured && player.Position is Position.Midfielder or Position.Forward)
            .OrderByDescending(player =>
                player.Passing * 1.15 +
                player.CurrentForm * 0.25 +
                (player.Traits.Contains(PlayerTrait.SetPieceSpecialist) ? 12 : 0))
            .FirstOrDefault() ?? preferredTaker;
    }

    private static double GetPenaltyConversionChance(Player player)
    {
        var traitBonus = player.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 0.08 : 0.0;
        var setPieceBonus = player.Traits.Contains(PlayerTrait.SetPieceSpecialist) ? 0.04 : 0.0;
        return Math.Clamp(0.66 + player.Finishing / 450.0 + traitBonus + setPieceBonus, 0.58, 0.90);
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

    private void ResetTeamStamina(Team team)
    {
        _fatigueService.RecoverTeamForNewMatch(team);
        var starters = team.Players.ToHashSet();

        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            player.Stamina = Math.Clamp(player.Stamina, 0, 100);
            player.LiveMatchModifier = 1.0;
            player.YellowCards = 0;
            player.IsSentOff = false;
            player.RedCardMinute = null;
            player.IsOnPitch = starters.Contains(player);
        }
    }

    private void ApplyMinuteFatigue(Team team, Match match, MatchSimulationOptions options)
    {
        if (options.EnableDynamicFatigue)
        {
            _fatigueService.ApplyMinuteFatigue(team, match);
            return;
        }

        var tacticalLoad = (team.Tactics.PressingIntensity + team.Tactics.Tempo + team.Tactics.DefensiveLine) / 3.0;
        var loadModifier = 1.0 + (Math.Max(0.0, tacticalLoad - 50.0) / 100.0);
        var staminaLoss = MatchConstants.StaminaLossPerMinute * loadModifier;

        foreach (var player in GetActivePitchPlayers(team))
        {
            player.Stamina = Math.Max(0.0, player.Stamina - staminaLoss);
        }
    }

    private IEnumerable<Player> GetAvailablePlayers(Team team)
    {
        return _teamStrengthCalculator.GetAvailablePlayers(team);
    }

    private static IEnumerable<Player> GetActivePitchPlayers(Team team)
    {
        return team.Players.Where(player => player.IsOnPitch && !player.IsSentOff);
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

    private enum AttackFlowOutcome
    {
        BuildUp,
        CreateChance,
        LosePossession,
        ForcedReset
    }

    private enum BallState
    {
        Kickoff,
        BuildUp,
        Attack,
        AttackPending,
        Chance,
        Defending,
        SetPiece,
        SetPiecePending,
        CornerPending,
        Turnover
    }

    private sealed record FoulContext(
        bool IsPenalty,
        string PenaltyReason,
        bool DeniesClearChance,
        bool ViolentFoul,
        bool CreatesDangerousSetPiece);

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
        public string? PossessionTeamName { get; set; }
        public BallState BallState { get; set; } = BallState.Kickoff;
        public EventType? LastFeedEventType { get; set; }
        public string? LastAttackingTeamName { get; set; }
        public string? LastActingTeamName { get; set; }
    }
}
