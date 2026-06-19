using System.Diagnostics;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

public class MatchEngine
{
    private const int CrowdMomentumCooldownMinutes = 12;
    private const int MaxCrowdMomentumEventsPerTeam = 2;
    private const int MaxCrowdMomentumEventsPerMatch = 3;
    private const int RedCardCooldownMinutes = 20;
    private const int EarlyRedCardProtectionMinute = 15;
    private const int FirstHalfMaximumAddedMinutes = 6;
    private const int SecondHalfMaximumAddedMinutes = 10;
    private const int ExtremeMaximumAddedMinutes = 12;

    private readonly TeamStrengthCalculator _teamStrengthCalculator;
    private readonly DisciplinaryService _disciplinaryService;
    private readonly TacticalImpactCalculator _tacticalImpactCalculator = new();
    private readonly MatchDramaService _matchDramaService = new();
    private readonly InjuryRiskService _injuryRiskService = new();
    private readonly FatigueService _fatigueService = new();
    private readonly AiManagerService _aiManagerService = new();
    private readonly HashSet<string> _secondWindAppliedPlayerKeys = [];
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
            startMinute: GetSecondHalfStartMinute(match),
            endMinute: GetSecondHalfRegulationEndMinute(match),
            includeFulltime: true);

        FinalizeSimulationState(simulationState);
        return match;
    }

    public Match CreateLiveMatch(Team homeTeam, Team awayTeam, MatchSimulationOptions? options = null)
    {
        ValidateTeam(homeTeam, nameof(homeTeam));
        ValidateTeam(awayTeam, nameof(awayTeam));

        var simulationOptions = CreateOptions(options);
        ResetTeamStamina(homeTeam, simulationOptions.PreserveMatchStartStamina);
        ResetTeamStamina(awayTeam, simulationOptions.PreserveMatchStartStamina);

        var match = CreateMatch(homeTeam, awayTeam);
        AssignWeather(match, _random);
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
            var simulationOptions = CreateOptions(options);
            ResetTeamStamina(homeTeam, simulationOptions.PreserveMatchStartStamina);
            ResetTeamStamina(awayTeam, simulationOptions.PreserveMatchStartStamina);
        }

        var random = CreateRandom(seed);
        var match = CreateMatch(homeTeam, awayTeam);
        AssignWeather(match, random);
        var simulationState = new MatchSimulationState(match, new MatchLogService(), new MatchEventFactory(), 0, 0, CreateOptions(options));
        InitializeAttackFlowState(simulationState);

        RunMatchMinutes(simulationState, random, startMinute, endMinute, includeFulltime);
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

    private static void AssignWeather(Match match, Random random)
    {
        match.WeatherCondition = ChooseWeather(random);
    }

    private static WeatherCondition ChooseWeather(Random random)
    {
        var roll = random.NextDouble();
        return roll switch
        {
            < 0.42 => WeatherCondition.Clear,
            < 0.60 => WeatherCondition.Rainy,
            < 0.72 => WeatherCondition.Windy,
            < 0.81 => WeatherCondition.Foggy,
            < 0.89 => WeatherCondition.HeavyRain,
            < 0.93 => WeatherCondition.Snow,
            < 0.96 => WeatherCondition.Storm,
            < 0.98 => WeatherCondition.Hot,
            _ => WeatherCondition.Cold
        };
    }

    private static void EnsureMatchWeather(MatchSimulationState simulationState, int startMinute)
    {
        var match = simulationState.Match;
        if (startMinute == 1 && !simulationState.MatchLog.GetEvents().Any(matchEvent => matchEvent.EventType == EventType.Weather))
        {
            simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateWeatherAnnouncement(0, match.WeatherCondition));
            if (match.IsRivalryMatch)
            {
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateRivalryAtmosphere(0, match.HomeTeam, match.AwayTeam));
            }
        }
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
        EnsureMatchWeather(simulationState, startMinute);
        var isSingleMinuteAdvance = startMinute == endMinute;
        var dynamicEndMinute = endMinute;

        for (var minute = startMinute; minute <= dynamicEndMinute; minute++)
        {
            UpdateMatchPhase(match, minute);
            if (minute == GetSecondHalfStartMinute(match))
            {
                AddSecondHalfRestartEvents(simulationState, minute);
            }

            if (minute == 1)
            {
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateKickoff(minute, homeTeam));
                SetPossession(simulationState, homeTeam, BallState.Kickoff, EventType.Kickoff);
            }

            AnnounceAddedTimeIfNeeded(simulationState, minute);
            var eventsBeforeMinute = simulationState.MatchLog.GetEvents().Count;

            TryAddHomeCrowdPressureEvent(simulationState, random, minute);
            TryAddTimeWastingEvent(simulationState, random, minute);

            var strengthSnapshot = CreateStrengthSnapshot(match, random);
            strengthSnapshot = ApplyWeatherModifiers(strengthSnapshot, match.WeatherCondition);
            strengthSnapshot = ApplyHomeCrowdMomentumModifiers(match, strengthSnapshot, minute);
            strengthSnapshot = ApplyAddedTimePressureModifiers(match, strengthSnapshot, minute);
            if (match.WeatherCondition == WeatherCondition.Cold && minute <= 20)
            {
                strengthSnapshot = strengthSnapshot with
                {
                    HomeAttackStrength = strengthSnapshot.HomeAttackStrength * 0.975,
                    AwayAttackStrength = strengthSnapshot.AwayAttackStrength * 0.975
                };
            }
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

            TryAddMinuteInjuryEvent(simulationState, random, minute);

            if ((minute >= 60 || simulationState.LastFeedEventType == EventType.Injury) &&
                IsSubstitutionStoppageEvent(simulationState.LastFeedEventType))
            {
                TryApplyAiManagerDecisions(simulationState, random, minute, stoppageType: simulationState.LastFeedEventType);
            }

            AddStoppageForNewEvents(simulationState, eventsBeforeMinute, random, minute);

            if (minute == GetFirstHalfEndMinute(match))
            {
                match.CurrentPhase = MatchPhase.Halftime;
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateHalftime(minute, match));
                TryApplyAiManagerDecisions(simulationState, random, minute, suppressFeedEvent: true);
            }

            if (includeFulltime && minute == GetSecondHalfEndMinute(match))
            {
                match.CurrentPhase = MatchPhase.Fulltime;
                simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateFulltime(minute, match));
            }

            if (!isSingleMinuteAdvance)
            {
                dynamicEndMinute = Math.Max(dynamicEndMinute, GetPhaseEndMinute(match, includeFulltime));
            }

            if (match.CurrentPhase != MatchPhase.Fulltime)
            {
                ApplyMinuteFatigue(homeTeam, simulationState.Match, simulationState.Options);
                ApplyMinuteFatigue(awayTeam, simulationState.Match, simulationState.Options);
            }
        }
    }

    private void TryAddMinuteInjuryEvent(MatchSimulationState simulationState, Random random, int minute)
    {
        if (!simulationState.Options.EnableInjuries)
        {
            return;
        }

        var injury = _injuryRiskService.TryCreateMatchInjury(
            simulationState.Match,
            minute,
            simulationState.LastFeedEventType,
            random);
        if (injury is null)
        {
            return;
        }

        simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateInjury(minute, injury.Team, injury.Player, injury.Cause));
        var performance = GetOrCreatePerformance(simulationState.Match, injury.Team, injury.Player);
        performance.Injuries++;
        performance.Rating -= 1.40;
        SetPossession(simulationState, injury.Team, BallState.SetPiece, EventType.Injury);
    }

    private static void AnnounceAddedTimeIfNeeded(MatchSimulationState simulationState, int minute)
    {
        var match = simulationState.Match;
        if (minute == MatchConstants.HalftimeMinute && !match.FirstHalfAddedTimeAnnounced)
        {
            match.FirstHalfAddedTimeAnnounced = true;
            match.FirstHalfAddedMinutes = CalculateAddedMinutes(match.FirstHalfStoppageSeconds, isSecondHalf: false);
            simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateAddedTimeAnnouncement(minute, match.FirstHalfAddedMinutes, isSecondHalf: false));
        }

        var secondHalfRegulationEnd = GetSecondHalfRegulationEndMinute(match);
        if (minute == secondHalfRegulationEnd && !match.SecondHalfAddedTimeAnnounced)
        {
            match.SecondHalfAddedTimeAnnounced = true;
            match.SecondHalfAddedMinutes = CalculateAddedMinutes(match.SecondHalfStoppageSeconds, isSecondHalf: true);
            simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateAddedTimeAnnouncement(minute, match.SecondHalfAddedMinutes, isSecondHalf: true));
        }
    }

    private static void AddStoppageForNewEvents(MatchSimulationState simulationState, int eventsBeforeMinute, Random random, int minute)
    {
        var newEvents = simulationState.MatchLog.GetEvents().Skip(eventsBeforeMinute).ToList();
        foreach (var matchEvent in newEvents)
        {
            var seconds = GetStoppageSecondsForEvent(matchEvent, random);
            if (seconds > 0)
            {
                AddStoppageSeconds(simulationState.Match, minute, seconds);
            }
        }
    }

    private static int GetStoppageSecondsForEvent(MatchEvent matchEvent, Random random)
    {
        return matchEvent.EventType switch
        {
            EventType.Goal or EventType.WonderGoal => random.Next(30, 61),
            EventType.Substitution => random.Next(20, 41),
            EventType.Injury => IsMajorInjury(matchEvent) ? random.Next(60, 181) : random.Next(30, 61),
            EventType.VarCheck => random.Next(60, 181),
            EventType.YellowCard or EventType.RedCard or EventType.Confrontation or EventType.RefereeControversy => random.Next(20, 61),
            EventType.PenaltyDecision => random.Next(45, 91),
            EventType.TimeWasting => random.Next(20, 91),
            EventType.GoalkeeperHeroics when matchEvent.Description.Contains("treatment", StringComparison.OrdinalIgnoreCase) => random.Next(30, 91),
            _ => 0
        };
    }

    private static bool IsMajorInjury(MatchEvent matchEvent)
    {
        return matchEvent.Description.Contains("serious", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains("moderate", StringComparison.OrdinalIgnoreCase) ||
            matchEvent.Description.Contains("medical", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddStoppageSeconds(Match match, int minute, int seconds)
    {
        if (IsFirstHalfMinute(match, minute))
        {
            match.FirstHalfStoppageSeconds += seconds;
            if (match.FirstHalfAddedTimeAnnounced)
            {
                match.FirstHalfAddedMinutes = Math.Max(match.FirstHalfAddedMinutes, CalculateAddedMinutes(match.FirstHalfStoppageSeconds, isSecondHalf: false));
            }

            return;
        }

        match.SecondHalfStoppageSeconds += seconds;
        if (match.SecondHalfAddedTimeAnnounced)
        {
            match.SecondHalfAddedMinutes = Math.Max(match.SecondHalfAddedMinutes, CalculateAddedMinutes(match.SecondHalfStoppageSeconds, isSecondHalf: true));
        }
    }

    private static int CalculateAddedMinutes(int stoppageSeconds, bool isSecondHalf)
    {
        var addedMinutes = (int)Math.Ceiling(stoppageSeconds / 60.0);
        var minimum = isSecondHalf ? 1 : 0;
        var usualMaximum = isSecondHalf ? SecondHalfMaximumAddedMinutes : FirstHalfMaximumAddedMinutes;
        var extremeThresholdSeconds = isSecondHalf ? 11 * 60 : 7 * 60;
        var maximum = addedMinutes > usualMaximum && stoppageSeconds >= extremeThresholdSeconds
            ? ExtremeMaximumAddedMinutes
            : usualMaximum;

        return Math.Clamp(addedMinutes, minimum, maximum);
    }

    private static int GetFirstHalfEndMinute(Match match)
    {
        return MatchConstants.HalftimeMinute + match.FirstHalfAddedMinutes;
    }

    private static int GetSecondHalfStartMinute(Match match)
    {
        return GetFirstHalfEndMinute(match) + 1;
    }

    private static int GetSecondHalfRegulationEndMinute(Match match)
    {
        return MatchConstants.DefaultMatchDurationMinutes + match.FirstHalfAddedMinutes;
    }

    private static int GetSecondHalfEndMinute(Match match)
    {
        return GetSecondHalfRegulationEndMinute(match) + match.SecondHalfAddedMinutes;
    }

    private static int GetPhaseEndMinute(Match match, bool includeFulltime)
    {
        return includeFulltime ? GetSecondHalfEndMinute(match) : GetFirstHalfEndMinute(match);
    }

    private static bool IsFirstHalfMinute(Match match, int minute)
    {
        return minute <= GetFirstHalfEndMinute(match);
    }

    private static int GetRegulationMinute(Match match, int minute)
    {
        return minute <= GetFirstHalfEndMinute(match)
            ? minute
            : minute - match.FirstHalfAddedMinutes;
    }

    private static void AddSecondHalfRestartEvents(MatchSimulationState simulationState, int minute)
    {
        if (simulationState.MatchLog.GetEvents().Any(matchEvent =>
            matchEvent.Minute == minute &&
            matchEvent.EventType == EventType.Kickoff &&
            matchEvent.Description.Contains("second half", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var kickoffTeam = GetPossessionTeam(simulationState) ?? simulationState.Match.HomeTeam;
        simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateSecondHalfKickoff(minute, kickoffTeam));
        SetPossession(simulationState, kickoffTeam, BallState.Kickoff, EventType.Kickoff);
        AddPendingHalftimeSubstitutionEvents(simulationState, minute);
    }

    private static void TryAddHomeCrowdPressureEvent(MatchSimulationState simulationState, Random random, int minute)
    {
        var match = simulationState.Match;
        if (simulationState.HomeCrowdPressureMinutes.Contains(minute))
        {
            return;
        }

        var shouldAdd =
            (match.IsRivalryMatch && minute == 12) ||
            (minute >= 82 && match.HomeScore <= match.AwayScore && !simulationState.HomeCrowdPressureMinutes.Any(existing => existing >= 80));
        if (!shouldAdd)
        {
            return;
        }

        if (TryAddCrowdMomentum(
            simulationState,
            minute,
            match.HomeTeam,
            () => simulationState.EventFactory.CreateHomeCrowdPressure(minute, match.HomeTeam, match.AwayTeam, random)))
        {
            simulationState.HomeCrowdPressureMinutes.Add(minute);
        }
    }

    private static void TryAddTimeWastingEvent(MatchSimulationState simulationState, Random random, int minute)
    {
        var match = simulationState.Match;
        var regulationMinute = GetRegulationMinute(match, minute);
        if (regulationMinute < 75 || minute - simulationState.LastTimeWastingMinute < 8)
        {
            return;
        }

        var leadingTeam = match.HomeScore > match.AwayScore
            ? match.HomeTeam
            : match.AwayScore > match.HomeScore
                ? match.AwayTeam
                : null;
        if (leadingTeam is null)
        {
            return;
        }

        var possessionTeam = GetPossessionTeam(simulationState);
        if (possessionTeam is null || !string.Equals(possessionTeam.Name, leadingTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isAwayLead = leadingTeam == match.AwayTeam;
        var isDefensiveShape = leadingTeam.Tactics.Mentality is Mentality.Defensive or Mentality.UltraDefensive;
        var addedTimePressure = regulationMinute > MatchConstants.DefaultMatchDurationMinutes ? 0.05 : 0.0;
        var chance = 0.035 + addedTimePressure + (isAwayLead ? 0.025 : 0.0) + (isDefensiveShape ? 0.025 : 0.0);
        if (random.NextDouble() > chance)
        {
            return;
        }

        simulationState.MatchLog.AddEvent(simulationState.EventFactory.CreateTimeWasting(minute, leadingTeam, random));
        simulationState.LastTimeWastingMinute = minute;
        SetPossession(simulationState, leadingTeam, BallState.SetPiece, EventType.TimeWasting);
    }

    private static void TryAddHomeGoalCrowdSurge(MatchSimulationState simulationState, int minute, Match match, Team scoringTeam)
    {
        if (!HomeAwayAdvantageService.IsHomeTeam(match, scoringTeam) || simulationState.HomeCrowdPressureMinutes.Contains(minute))
        {
            return;
        }

        if (TryAddCrowdMomentum(
            simulationState,
            minute,
            match.HomeTeam,
            () => simulationState.EventFactory.CreateHomeCrowdPressure(minute, match.HomeTeam, match.AwayTeam, new Random(minute + scoringTeam.Name.Length))))
        {
            simulationState.HomeCrowdPressureMinutes.Add(minute);
        }
    }

    private static bool TryAddCrowdMomentum(
        MatchSimulationState simulationState,
        int minute,
        Team team,
        Func<MatchEvent> createEvent)
    {
        if (!CanAddCrowdMomentum(simulationState, minute, team))
        {
            return false;
        }

        simulationState.MatchLog.AddEvent(createEvent());
        RegisterCrowdMomentum(simulationState, minute, team);
        return true;
    }

    private static bool CanAddCrowdMomentum(MatchSimulationState simulationState, int minute, Team team)
    {
        var previousEventType = simulationState.MatchLog.GetEvents().LastOrDefault()?.EventType ?? simulationState.LastFeedEventType;
        if (previousEventType == EventType.CrowdMomentum)
        {
            return false;
        }

        if (simulationState.TotalCrowdMomentumCount >= MaxCrowdMomentumEventsPerMatch)
        {
            return false;
        }

        if (minute - simulationState.LastCrowdMomentumMinute < CrowdMomentumCooldownMinutes)
        {
            return false;
        }

        var teamCount = GetCount(simulationState.CrowdMomentumCountByTeam, team.Name);
        return teamCount < MaxCrowdMomentumEventsPerTeam;
    }

    private static void RegisterCrowdMomentum(MatchSimulationState simulationState, int minute, Team team)
    {
        simulationState.LastCrowdMomentumMinute = minute;
        simulationState.TotalCrowdMomentumCount++;
        IncrementCount(simulationState.CrowdMomentumCountByTeam, team.Name);
    }

    private static void AddPendingHalftimeSubstitutionEvents(MatchSimulationState simulationState, int minute)
    {
        var match = simulationState.Match;
        var halftimeSubstitutions = match.Substitutions
            .Where(substitution => substitution.Minute == MatchConstants.HalftimeMinute)
            .Where(substitution => !HasVisibleSubstitutionEvent(match, substitution))
            .GroupBy(substitution => substitution.TeamName)
            .ToList();

        foreach (var group in halftimeSubstitutions)
        {
            var team = string.Equals(group.Key, match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)
                ? match.HomeTeam
                : match.AwayTeam;
            var substitutions = group.ToList();
            var matchEvent = substitutions.Count > 1
                ? simulationState.EventFactory.CreateGroupedHalftimeSubstitution(minute, team, substitutions)
                : simulationState.EventFactory.CreateHalftimeSubstitution(minute, team, substitutions[0]);
            simulationState.MatchLog.AddEvent(matchEvent);
        }
    }

    private static bool HasVisibleSubstitutionEvent(Match match, MatchSubstitution substitution)
    {
        return match.Events.Any(matchEvent =>
            matchEvent.EventType == EventType.Substitution &&
            string.Equals(matchEvent.PrimaryPlayerName, substitution.PlayerOnName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(matchEvent.SecondaryPlayerName, substitution.PlayerOffName, StringComparison.OrdinalIgnoreCase));
    }

    private void TryApplyAiManagerDecisions(
        MatchSimulationState simulationState,
        Random random,
        int minute,
        bool suppressFeedEvent = false,
        EventType? stoppageType = null)
    {
        TryApplyAiManagerDecision(simulationState, simulationState.Match.HomeTeam, random, minute, suppressFeedEvent, stoppageType);
        TryApplyAiManagerDecision(simulationState, simulationState.Match.AwayTeam, random, minute, suppressFeedEvent, stoppageType);
    }

    private void TryApplyAiManagerDecision(
        MatchSimulationState simulationState,
        Team team,
        Random random,
        int minute,
        bool suppressFeedEvent = false,
        EventType? stoppageType = null)
    {
        var previousFormation = FormationCatalogService.NormalizeFormationName(team.Formation);
        var decision = _aiManagerService.TryMakeSubstitution(
            simulationState.Match,
            team,
            minute,
            simulationState.Options,
            random);
        var currentFormation = FormationCatalogService.NormalizeFormationName(team.Formation);

        if (!suppressFeedEvent &&
            !string.Equals(previousFormation, currentFormation, StringComparison.OrdinalIgnoreCase))
        {
            simulationState.MatchLog.AddEvent(CreateTacticalChangeEvent(
                minute,
                simulationState.Match,
                team,
                currentFormation));
        }

        if (decision is null)
        {
            return;
        }

        if (!suppressFeedEvent)
        {
            var substitutionEvent = stoppageType.HasValue
                ? simulationState.EventFactory.CreateStoppageSubstitution(minute, decision.Team, decision.PlayerOff, decision.PlayerOn, stoppageType.Value)
                : simulationState.EventFactory.CreateSubstitution(minute, decision.Team, decision.PlayerOff, decision.PlayerOn);
            simulationState.MatchLog.AddEvent(substitutionEvent);
        }

        simulationState.SuperSubBoosts[decision.PlayerOn.Name] = minute + 10;
    }

    private static MatchEvent CreateTacticalChangeEvent(int minute, Match match, Team team, string formation)
    {
        return new MatchEvent
        {
            Minute = minute,
            EventType = EventType.TacticalChange,
            HomeScore = match.HomeScore,
            AwayScore = match.AwayScore,
            Description = $"{team.Name} switch to {formation}{CreateTacticalChangeReason(match, team, minute)}."
        };
    }

    private static string CreateTacticalChangeReason(Match match, Team team, int minute)
    {
        if (IsTeamWinning(match, team) && minute >= 70)
        {
            return " to protect the lead";
        }

        if (IsTeamLosing(match, team) && minute >= 78)
        {
            return " in search of a late goal";
        }

        if (IsTeamLosing(match, team) && minute >= 60)
        {
            return " to chase the game";
        }

        if (team == match.AwayTeam && minute >= 60)
        {
            return " to absorb pressure away from home";
        }

        return string.Empty;
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
        var latestConfrontation = simulationState.Match.Events.LastOrDefault(matchEvent => matchEvent.EventType == EventType.Confrontation);
        if (latestConfrontation is not null && latestConfrontation.Minute + 8 >= simulationState.Match.CurrentMinute)
        {
            simulationState.TensionUntilMinute = latestConfrontation.Minute + 8;
        }
        simulationState.LastConfrontationMinute = latestConfrontation?.Minute ?? -100;
        simulationState.ConfrontationCount = simulationState.Match.Events.Count(matchEvent => matchEvent.EventType == EventType.Confrontation);

        RestoreErrorStateFromExistingEvents(simulationState);
        var possessionTeam = InferPossessionAfterEvent(lastEvent, simulationState.Match);
        SetPossession(simulationState, possessionTeam, GetBallStateAfterEvent(lastEvent.EventType), lastEvent.EventType);
    }

    private static void RestoreErrorStateFromExistingEvents(MatchSimulationState simulationState)
    {
        foreach (var matchEvent in simulationState.Match.Events)
        {
            if (matchEvent.EventType == EventType.DefensiveError)
            {
                var teamName = FindEventTeamName(matchEvent, simulationState.Match);
                if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(matchEvent.PrimaryPlayerName))
                {
                    continue;
                }

                IncrementCount(simulationState.TeamDefensiveErrorCounts, teamName);
                simulationState.TeamDefensiveErrorCooldownUntil[teamName] = Math.Max(
                    simulationState.TeamDefensiveErrorCooldownUntil.GetValueOrDefault(teamName),
                    matchEvent.Minute + 15);
                var playerKey = $"{teamName}|{matchEvent.PrimaryPlayerName}";
                simulationState.PlayerDefensiveErrorCooldownUntil[playerKey] = Math.Max(
                    simulationState.PlayerDefensiveErrorCooldownUntil.GetValueOrDefault(playerKey),
                    matchEvent.Minute + 25);
            }
            else if (matchEvent.EventType == EventType.GoalkeeperMistake)
            {
                var teamName = FindEventTeamName(matchEvent, simulationState.Match);
                if (!string.IsNullOrWhiteSpace(teamName))
                {
                    IncrementCount(simulationState.TeamGoalkeeperErrorCounts, teamName);
                }
            }
        }
    }

    private static bool IsPossessionStateEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType is EventType.Kickoff
            or EventType.Attack
            or EventType.ChanceCreated
            or EventType.Foul
            or EventType.Shot
            or EventType.Goal
            or EventType.Miss
            or EventType.Woodwork
            or EventType.Save
            or EventType.GoalkeeperMistake
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
            or EventType.GoalkeeperHeroics
            or EventType.Injury
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.LateDrama
            or EventType.CrowdMomentum
            or EventType.TimeWasting;
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
            EventType.ChanceCreated => actingTeamModel,
            EventType.Foul => opposingTeam,
            EventType.PenaltyDecision => opposingTeam,
            EventType.Turnover => actingTeamModel,
            EventType.BadPass or EventType.Miscontrol => actingTeamModel,
            EventType.Tackle or EventType.Interception or EventType.Pressure or EventType.BlockedPass => opposingTeam,
            EventType.DefensiveStop => actingTeamModel,
            EventType.Save => actingTeamModel,
            EventType.GoalkeeperMistake => opposingTeam,
            EventType.Goal => opposingTeam,
            EventType.WonderGoal => opposingTeam,
            EventType.Shot => opposingTeam,
            EventType.Miss => opposingTeam,
            EventType.Woodwork => opposingTeam,
            EventType.LateDrama => actingTeamModel,
            EventType.CrowdMomentum => actingTeamModel,
            EventType.TimeWasting => actingTeamModel,
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
            EventType.ChanceCreated => BallState.Chance,
            EventType.Shot or EventType.Save or EventType.Goal or EventType.Miss or EventType.WonderGoal or EventType.Woodwork or EventType.GoalkeeperHeroics => BallState.Chance,
            EventType.GoalkeeperMistake => BallState.Turnover,
            EventType.CornerKick => BallState.CornerPending,
            EventType.SetPieceDanger => BallState.SetPiecePending,
            EventType.Injury => BallState.SetPiece,
            EventType.Foul or EventType.Offside or EventType.PenaltyDecision or EventType.PenaltyTaker or EventType.Penalty => BallState.SetPiece,
            EventType.TimeWasting => BallState.SetPiece,
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
            case EventType.ChanceCreated:
            case EventType.Shot:
            case EventType.Miss:
            case EventType.Goal:
            case EventType.WonderGoal:
            case EventType.Woodwork:
            case EventType.LateDrama:
            case EventType.CrowdMomentum:
            case EventType.TimeWasting:
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
            case EventType.GoalkeeperHeroics:
            case EventType.GoalkeeperMistake:
            case EventType.Injury:
            case EventType.Tackle:
            case EventType.Interception:
            case EventType.Pressure:
            case EventType.BlockedPass:
            case EventType.RefereeControversy:
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

    private static Team? ResolvePlayerTeam(Match match, Player player)
    {
        if (match.HomeTeam.Players.Concat(match.HomeTeam.Substitutes).Any(candidate =>
            ReferenceEquals(candidate, player) ||
            string.Equals(candidate.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return match.HomeTeam;
        }

        if (match.AwayTeam.Players.Concat(match.AwayTeam.Substitutes).Any(candidate =>
            ReferenceEquals(candidate, player) ||
            string.Equals(candidate.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return match.AwayTeam;
        }

        return null;
    }

    private static bool EventMentionsTeam(string description, string teamName)
    {
        return description.StartsWith($"{teamName} ", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" for {teamName}", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" from {teamName}", StringComparison.OrdinalIgnoreCase) ||
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
        simulationState.Match.Events = simulationState.MatchLog.GetEvents()
            .Select(matchEvent => ApplyDisplayMinute(simulationState.Match, matchEvent))
            .ToList();
        UpdateFinalPlayerFatigue(simulationState.Match);
        NormalizePlayerRatings(simulationState.Match);
    }

    private static MatchEvent ApplyDisplayMinute(Match match, MatchEvent matchEvent)
    {
        matchEvent.DisplayMinuteText = FormatDisplayMinute(match, matchEvent.Minute);
        return matchEvent;
    }

    public static string FormatDisplayMinute(Match match, int minute)
    {
        if (minute <= MatchConstants.HalftimeMinute)
        {
            return $"{minute}'";
        }

        if (minute <= GetFirstHalfEndMinute(match))
        {
            return $"45+{minute - MatchConstants.HalftimeMinute}'";
        }

        var regulationMinute = minute - match.FirstHalfAddedMinutes;
        if (regulationMinute <= MatchConstants.DefaultMatchDurationMinutes)
        {
            return $"{regulationMinute}'";
        }

        return $"90+{regulationMinute - MatchConstants.DefaultMatchDurationMinutes}'";
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
            IsRivalryMatch = IsRivalry(homeTeam.Name, awayTeam.Name),
            SuperSubBoosts = []
        };

        EnsurePlayerPerformances(match);
        return match;
    }

    private static void UpdateMatchPhase(Match match, int minute)
    {
        match.CurrentMinute = minute;
        match.CurrentPhase = minute <= GetFirstHalfEndMinute(match)
            ? MatchPhase.FirstHalf
            : MatchPhase.SecondHalf;
    }

    private TeamStrengthSnapshot CreateStrengthSnapshot(Match match, Random random)
    {
        var homeTeam = match.HomeTeam;
        var awayTeam = match.AwayTeam;
        var homeAttackSwing = GetRandomStrengthSwing(random);
        var awayAttackSwing = GetRandomStrengthSwing(random);
        var homeDefenseSwing = GetRandomStrengthSwing(random);
        var awayDefenseSwing = GetRandomStrengthSwing(random);
        var homeVenue = HomeAwayAdvantageService.GetModifier(match, homeTeam);
        var awayVenue = HomeAwayAdvantageService.GetModifier(match, awayTeam);

        var homeTacticalAttack = _tacticalImpactCalculator.GetAttackModifier(homeTeam, awayTeam);
        var awayTacticalAttack = _tacticalImpactCalculator.GetAttackModifier(awayTeam, homeTeam);
        var homeTacticalDefense = _tacticalImpactCalculator.GetDefenseModifier(homeTeam, awayTeam);
        var awayTacticalDefense = _tacticalImpactCalculator.GetDefenseModifier(awayTeam, homeTeam);

        return new TeamStrengthSnapshot(
            _teamStrengthCalculator.CalculateAttackStrength(homeTeam, awayTeam) * homeAttackSwing * homeVenue.AttackModifier,
            _teamStrengthCalculator.CalculateAttackStrength(awayTeam, homeTeam) * awayAttackSwing * awayVenue.AttackModifier,
            _teamStrengthCalculator.CalculateDefenseStrength(homeTeam, awayTeam) * homeDefenseSwing * homeVenue.DefenseModifier,
            _teamStrengthCalculator.CalculateDefenseStrength(awayTeam, homeTeam) * awayDefenseSwing * awayVenue.DefenseModifier,
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

    private static TeamStrengthSnapshot ApplyWeatherModifiers(TeamStrengthSnapshot snapshot, WeatherCondition weatherCondition)
    {
        var (attackModifier, defenseModifier) = weatherCondition switch
        {
            WeatherCondition.Rainy => (0.972, 0.988),
            WeatherCondition.HeavyRain => (0.935, 0.95),
            WeatherCondition.Storm => (0.915, 0.94),
            WeatherCondition.Windy => (0.958, 0.995),
            WeatherCondition.Foggy => (0.968, 0.962),
            WeatherCondition.Snow => (0.928, 0.95),
            WeatherCondition.Hot => (0.975, 0.985),
            WeatherCondition.Cold => (0.988, 0.994),
            _ => (1.0, 1.0)
        };

        return snapshot with
        {
            HomeAttackStrength = snapshot.HomeAttackStrength * attackModifier,
            AwayAttackStrength = snapshot.AwayAttackStrength * attackModifier,
            HomeDefenseStrength = snapshot.HomeDefenseStrength * defenseModifier,
            AwayDefenseStrength = snapshot.AwayDefenseStrength * defenseModifier
        };
    }

    private static TeamStrengthSnapshot ApplyHomeCrowdMomentumModifiers(Match match, TeamStrengthSnapshot snapshot, int minute)
    {
        var earlySurge = minute <= 15 ? 1.018 : 1.0;
        var latePush = minute >= 80 && match.HomeScore <= match.AwayScore + 1 ? 1.020 : 1.0;
        var rivalrySurge = match.IsRivalryMatch && (minute <= 20 || minute >= 75) ? 1.012 : 1.0;
        var homeMomentum = earlySurge * latePush * rivalrySurge;

        if (Math.Abs(homeMomentum - 1.0) < 0.001)
        {
            return snapshot;
        }

        return snapshot with
        {
            HomeAttackStrength = snapshot.HomeAttackStrength * homeMomentum,
            HomeDefenseStrength = snapshot.HomeDefenseStrength * (1.0 + (homeMomentum - 1.0) * 0.55)
        };
    }

    private static TeamStrengthSnapshot ApplyAddedTimePressureModifiers(Match match, TeamStrengthSnapshot snapshot, int minute)
    {
        if (GetRegulationMinute(match, minute) <= MatchConstants.DefaultMatchDurationMinutes)
        {
            return snapshot;
        }

        var homeTrailingOrLevel = match.HomeScore <= match.AwayScore;
        var awayTrailingOrLevel = match.AwayScore <= match.HomeScore;
        var pressureBoost = 1.035;
        var defensiveClearanceBoost = 1.015;

        return snapshot with
        {
            HomeAttackStrength = snapshot.HomeAttackStrength * (homeTrailingOrLevel ? pressureBoost : 1.0),
            AwayAttackStrength = snapshot.AwayAttackStrength * (awayTrailingOrLevel ? pressureBoost : 1.0),
            HomeDefenseStrength = snapshot.HomeDefenseStrength * (homeTrailingOrLevel ? 1.0 : defensiveClearanceBoost),
            AwayDefenseStrength = snapshot.AwayDefenseStrength * (awayTrailingOrLevel ? 1.0 : defensiveClearanceBoost)
        };
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

        return activePlayers.Average(player =>
        {
            var attributes = PlayerAttributeService.GetAttributes(player);
            var attackingProfile = player.Attack * 0.35 + player.Finishing * 0.25 + attributes.Pace * 0.15 + attributes.Dribbling * 0.15 + attributes.Shooting * 0.10;
            return player.Position switch
            {
                Position.Forward => attackingProfile * 1.30,
                Position.Midfielder => (attackingProfile * 0.55 + attributes.Passing * 0.45) * 1.10,
                Position.Defender => attackingProfile * 0.70,
                Position.Goalkeeper => attackingProfile * 0.30,
                _ => attackingProfile
            };
        });
    }

    private static double CalculateBaseDefense(Team team)
    {
        var activePlayers = GetActivePitchPlayers(team).ToList();
        if (activePlayers.Count == 0)
        {
            return 0;
        }

        return activePlayers.Average(player =>
        {
            var attributes = PlayerAttributeService.GetAttributes(player);
            var defensiveProfile = player.Defense * 0.50 + attributes.Defending * 0.34 + attributes.Physical * 0.16;
            return player.Position switch
            {
                Position.Goalkeeper => defensiveProfile * 1.40,
                Position.Defender => defensiveProfile * 1.20,
                Position.Midfielder => defensiveProfile * 0.90,
                Position.Forward => defensiveProfile * 0.40,
                _ => defensiveProfile
            };
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
        if (simulationState.LastFeedEventType is EventType.Turnover or EventType.Offside)
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
            WeatherCondition = match.WeatherCondition,
            IsRivalryMatch = match.IsRivalryMatch,
            EnableInjuries = simulationState.Options.EnableInjuries,
            PreviousEventType = simulationState.LastFeedEventType,
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

        if (dramaResult.EventType is EventType.Confrontation or EventType.RefereeControversy &&
            !CanCreateContextualConfrontation(simulationState, minute, simulationState.LastFeedEventType))
        {
            return strengthSnapshot;
        }

        if (dramaResult.EventType == EventType.DefensiveError &&
            (dramaResult.Player is null ||
                !TryRegisterDefensiveError(simulationState, dramaResult.Team, dramaResult.Player, minute, random)))
        {
            return strengthSnapshot;
        }

        if (dramaResult.ScoresGoal)
        {
            UpdateScore(match, dramaResult.Team);
        }

        if (dramaResult.EventType == EventType.CrowdMomentum)
        {
            if (!TryAddCrowdMomentum(
                simulationState,
                minute,
                dramaResult.Team,
                () => simulationState.EventFactory.CreateCrowdMomentum(minute, dramaResult.Team)))
            {
                return strengthSnapshot;
            }

            TrackDramaPerformance(match, dramaResult);
            return strengthSnapshot with
            {
                HomeAttackStrength = strengthSnapshot.HomeAttackStrength * dramaResult.HomeAttackModifier,
                AwayAttackStrength = strengthSnapshot.AwayAttackStrength * dramaResult.AwayAttackModifier,
                HomeDefenseStrength = strengthSnapshot.HomeDefenseStrength * dramaResult.HomeDefenseModifier,
                AwayDefenseStrength = strengthSnapshot.AwayDefenseStrength * dramaResult.AwayDefenseModifier
            };
        }

        TrackDramaPerformance(match, dramaResult);
        simulationState.MatchLog.AddEvent(CreateDramaMatchEvent(minute, match, dramaResult, simulationState.EventFactory));
        if (dramaResult.EventType == EventType.Confrontation && dramaResult.Player is not null)
        {
            RegisterConfrontation(simulationState, minute);
            var opponentTeam = GetOpposingTeam(match, dramaResult.Team);
            var opponentPlayer = ChooseConfrontationOpponent(opponentTeam, random);
            ResolveConfrontationOutcome(
                minute,
                simulationState,
                dramaResult.Team,
                opponentTeam,
                dramaResult.Player,
                opponentPlayer,
                random,
                simulationState.MatchLog,
                simulationState.EventFactory);
        }

        if (dramaResult.EventType == EventType.DefensiveError &&
            dramaResult.Player is not null &&
            random.NextDouble() < 0.78)
        {
            var attackingTeam = GetOpposingTeam(match, dramaResult.Team);
            var attackingStats = GetTeamStats(match, attackingTeam);
            var attackingStrength = attackingTeam == match.HomeTeam
                ? strengthSnapshot.HomeAttackStrength
                : strengthSnapshot.AwayAttackStrength;
            var defendingStrength = dramaResult.Team == match.HomeTeam
                ? strengthSnapshot.HomeDefenseStrength
                : strengthSnapshot.AwayDefenseStrength;
            var punishmentPossessionTeam = HandleMistakePunishmentSequence(
                minute,
                simulationState,
                match,
                attackingTeam,
                dramaResult.Team,
                dramaResult.Player,
                preferredAttacker: null,
                random,
                simulationState.MatchLog,
                simulationState.EventFactory,
                attackingStats,
                attackingStrength,
                defendingStrength,
                "defensive");
            var punishmentFinalEventType = GetLastEventType(simulationState.MatchLog, EventType.Shot);
            MarkAttackResolved(
                simulationState,
                punishmentPossessionTeam,
                GetBallStateAfterEvent(punishmentFinalEventType),
                punishmentFinalEventType);
        }

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
            EventType.Injury => eventFactory.CreateInjury(minute, dramaResult.Team, dramaResult.Player!, dramaResult.InjuryCause),
            EventType.Penalty => eventFactory.CreatePenalty(
                minute,
                dramaResult.Team,
                dramaResult.Player!,
                dramaResult.ScoresGoal,
                match,
                dramaResult.ScoresGoal ? GetMatchGoalCount(match, dramaResult.Team, dramaResult.Player!) : 0),
            EventType.Offside => eventFactory.CreateOffside(minute, dramaResult.Team, dramaResult.Player!),
            EventType.DefensiveError => eventFactory.CreateDefensiveError(minute, dramaResult.Team, dramaResult.Player!),
            EventType.WonderGoal => eventFactory.CreateWonderGoal(
                minute,
                dramaResult.Team,
                dramaResult.Player!,
                match,
                scorerMatchGoals: GetMatchGoalCount(match, dramaResult.Team, dramaResult.Player!)),
            EventType.GoalkeeperHeroics => eventFactory.CreateGoalkeeperHeroics(minute, dramaResult.Team, dramaResult.Player!),
            EventType.SetPieceDanger => eventFactory.CreateSetPieceDanger(minute, dramaResult.Team, dramaResult.Player!),
            EventType.RefereeControversy => eventFactory.CreateRefereeControversy(minute, dramaResult.Team, dramaResult.Player, new Random(minute + dramaResult.Team.Name.Length)),
            EventType.LateDrama => eventFactory.CreateLateDrama(minute, dramaResult.Team, dramaResult.OpponentTeam ?? GetOpposingTeam(match, dramaResult.Team), match),
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
        var attackingStats = GetTeamStats(match, attackingTeam);
        var defendingStats = GetTeamStats(match, defendingTeam);
        var attackOutcome = DetermineAttackOutcome(attackingTeam, defendingTeam, attackingTeamStrength, defendingTeamStrength, random);
        var isResolvingPreviousAttack = HasUnresolvedAttack(simulationState, attackingTeam);
        var isResolvingTurnover = simulationState.LastFeedEventType == EventType.Turnover;
        var isRestartingAfterOffside = simulationState.LastFeedEventType == EventType.Offside;

        if (isResolvingTurnover || isRestartingAfterOffside)
        {
            attackOutcome = AttackFlowOutcome.BuildUp;
        }

        if (attackOutcome == AttackFlowOutcome.BuildUp)
        {
            if (isResolvingPreviousAttack)
            {
                attackOutcome = random.NextDouble() < 0.48
                    ? AttackFlowOutcome.CreateChance
                    : AttackFlowOutcome.LosePossession;
            }
            else
            {
                var buildUpEvent = isRestartingAfterOffside
                    ? eventFactory.CreateOffsideRestart(minute, attackingTeam, random)
                    : eventFactory.CreateAttackBuildUp(
                        minute,
                        attackingTeam,
                        playmaker,
                        shooter,
                        random,
                        GetTriggeredBuildUpTrait(match, attackingTeam, playmaker, minute, random));
                matchLog.AddEvent(buildUpEvent);
                MarkAttackStarted(simulationState, attackingTeam);
                GetOrCreatePerformance(match, attackingTeam, playmaker).Rating += 0.04;
                return attackingTeam;
            }
        }

        if (attackOutcome == AttackFlowOutcome.LosePossession)
        {
            if (!isResolvingPreviousAttack)
            {
                matchLog.AddEvent(eventFactory.CreateAttackBuildUp(
                    minute,
                    attackingTeam,
                    playmaker,
                    shooter,
                    random,
                    GetTriggeredBuildUpTrait(match, attackingTeam, playmaker, minute, random)));
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
            MarkAttackResolved(simulationState, attackingTeam, BallState.BuildUp, EventType.DefensiveStop);
            return attackingTeam;
        }

        var chanceType = ChooseChanceType(attackingTeam, defendingTeam, match.WeatherCondition, playmaker, shooter, random);
        var attackSequence = CreateOpenPlayAttackSequence(attackingTeam, playmaker, shooter, chanceType, random);
        playmaker = attackSequence.CreatorPlayer;
        shooter = attackSequence.ShooterPlayer;
        var shooterHasSuperSubBoost = IsSuperSubBoostActive(simulationState, shooter, minute);
        if (!isResolvingPreviousAttack)
        {
            matchLog.AddEvent(eventFactory.CreateAttackBuildUp(
                minute,
                attackingTeam,
                playmaker,
                shooter,
                random,
                GetTriggeredBuildUpTrait(match, attackingTeam, playmaker, minute, random)));
            MarkAttackStarted(simulationState, attackingTeam);
        }

        var offsideChance = Math.Clamp(
            0.13 +
            (defendingTeam.Tactics.DefensiveLine - 50) * 0.0024 +
            (attackingTeam.Tactics.Tempo - 50) * 0.0012 -
            (shooter.Traits.Contains(PlayerTrait.TriesToBeatOffsideTrap) ? 0.07 : 0.0) +
            HomeAwayAdvantageService.GetOffsideAdjustment(match, attackingTeam),
            0.04,
            0.38);
        if (chanceType == "through ball attempt" && random.NextDouble() < offsideChance)
        {
            attackingStats.Offsides++;
            var shooterPerformance = GetOrCreatePerformance(match, attackingTeam, shooter);
            shooterPerformance.Offsides++;
            shooterPerformance.Rating -= 0.08;
            matchLog.AddEvent(eventFactory.CreateOffside(minute, attackingTeam, shooter, chanceType, random));
            MarkAttackResolved(simulationState, defendingTeam, BallState.BuildUp, EventType.Offside);
            return defendingTeam;
        }

        if (ShouldCommitFoul(simulationState, defendingTeam, attackingTeamStrength, defendingTeamStrength, minute, random))
        {
            var foulContext = CreateFoulContext(chanceType, attackingTeamStrength, defendingTeamStrength, random);
            var possessionAfterFoul = HandleFoul(minute, simulationState, attackingTeam, defendingTeam, shooter, random, matchLog, eventFactory, defendingStats, foulContext);
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
        var errorPlayer = ChooseDefendingPlayer(defendingTeam, random);
        var errorPerformance = GetOrCreatePerformance(match, defendingTeam, errorPlayer);
        var defensiveErrorChance = CalculateDefensiveErrorChance(
            simulationState,
            attackingTeam,
            defendingTeam,
            errorPlayer,
            errorPerformance,
            attackingTeamStrength,
            defendingTeamStrength,
            minute);
        if (CanCreateDefensiveError(simulationState, defendingTeam, errorPlayer, minute) &&
            random.NextDouble() < defensiveErrorChance &&
            TryRegisterDefensiveError(simulationState, defendingTeam, errorPlayer, minute, random))
        {
            matchLog.AddEvent(eventFactory.CreateDefensiveError(minute, defendingTeam, errorPlayer));
            errorPerformance.ErrorsLeadingToShot++;
            errorPerformance.Rating -= 0.22;
            if (random.NextDouble() < 0.80)
            {
                var punishmentPossessionTeam = HandleMistakePunishmentSequence(
                    minute,
                    simulationState,
                    match,
                    attackingTeam,
                    defendingTeam,
                    errorPlayer,
                    shooter,
                    random,
                    matchLog,
                    eventFactory,
                    attackingStats,
                    attackingTeamStrength,
                    defendingTeamStrength,
                    "defensive");
                var punishmentFinalEventType = GetLastEventType(matchLog, EventType.Shot);
                MarkAttackResolved(
                    simulationState,
                    punishmentPossessionTeam,
                    GetBallStateAfterEvent(punishmentFinalEventType),
                    punishmentFinalEventType);
                return punishmentPossessionTeam;
            }

            defensiveErrorBoost = 0.10;
        }

        var openPlayTurnoverRisk = 0.14 -
            (playmaker.Traits.Contains(PlayerTrait.PressResistant) ? 0.045 : 0.0) -
            (playmaker.Traits.Contains(PlayerTrait.Playmaker) ? 0.024 : 0.0) -
            (playmaker.Traits.Contains(PlayerTrait.TeamPlayer) ? 0.016 : 0.0) -
            (shooter.Traits.Contains(PlayerTrait.TechnicalDribbler) ? 0.020 : 0.0) -
            (shooter.Traits.Contains(PlayerTrait.Flair) ? 0.012 : 0.0);
        openPlayTurnoverRisk += GetPreShotDisruptionRisk(match, attackingTeam, defendingTeam, chanceType, shooter);
        openPlayTurnoverRisk *= _tacticalImpactCalculator.GetTurnoverRiskModifier(attackingTeam, defendingTeam);
        openPlayTurnoverRisk *= HomeAwayAdvantageService.GetModifier(match, attackingTeam).TurnoverRiskModifier;
        if (random.NextDouble() < Math.Clamp(openPlayTurnoverRisk, 0.035, GetOpenPlayTurnoverRiskCap(attackingTeam, defendingTeam)))
        {
            HandlePossessionLoss(minute, match, attackingTeam, defendingTeam, playmaker, shooter, random, matchLog, eventFactory);
            MarkAttackResolved(simulationState, defendingTeam, BallState.Turnover, EventType.Turnover);
            return defendingTeam;
        }

        attackingStats.TotalShots++;
        var triggeredChanceTrait = GetTriggeredShotCreationTrait(playmaker, shooter, chanceType);
        var chanceCreator = ResolveChanceCreator(playmaker, shooter, chanceType);
        matchLog.AddEvent(eventFactory.CreateChanceCreated(
            minute,
            attackingTeam,
            chanceCreator,
            shooter,
            chanceType,
            random,
            triggeredChanceTrait));
        matchLog.AddEvent(eventFactory.CreateShot(
            minute,
            attackingTeam,
            shooter,
            chanceCreator,
            chanceType,
            random));
        var shotPossessionTeam = HandleShotOutcome(
            minute,
            simulationState,
            match,
            attackingTeam,
            defendingTeam,
            shooter,
            chanceCreator,
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
        var reasonType = ChoosePossessionLossReason(attackingTeam, defendingTeam, match.WeatherCondition, random);
        var attacker = random.NextDouble() < 0.65 ? playmaker : shooter;
        Player? defender = null;

        if (IsDefenderPossessionWin(reasonType))
        {
            defender = ChooseDefendingPlayer(defendingTeam, random);
            reasonType = AdjustPossessionLossReasonForDefenderTraits(reasonType, defender, random);
            ApplyPossessionWinContribution(match, defendingTeam, defender, reasonType, random);
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
            random,
            GetTriggeredPossessionWinTrait(defender, reasonType)));

        var possessionPlayer = defender ?? ChooseDefendingPlayer(defendingTeam, random);
        matchLog.AddEvent(eventFactory.CreateTurnover(minute, defendingTeam, possessionPlayer, random));
    }

    private static EventType ChoosePossessionLossReason(Team attackingTeam, Team defendingTeam, WeatherCondition weatherCondition, Random random)
    {
        var roll = random.NextDouble();
        var pressingBias = Math.Max(0, defendingTeam.Tactics.PressingIntensity - 55) / 100.0;
        var tempoRisk = Math.Max(0, attackingTeam.Tactics.Tempo - 60) / 120.0;
        var narrowBias = Math.Max(0, 45 - attackingTeam.Tactics.Width) / 100.0;
        var passRiskBonus = weatherCondition switch
        {
            WeatherCondition.Rainy => 0.03,
            WeatherCondition.HeavyRain => 0.07,
            WeatherCondition.Storm => 0.09,
            WeatherCondition.Windy => 0.04,
            WeatherCondition.Snow => 0.05,
            WeatherCondition.Foggy => 0.02,
            _ => 0.0
        };
        var controlRiskBonus = weatherCondition switch
        {
            WeatherCondition.Rainy => 0.04,
            WeatherCondition.HeavyRain => 0.08,
            WeatherCondition.Snow => 0.07,
            WeatherCondition.Storm => 0.06,
            WeatherCondition.Cold => 0.03,
            _ => 0.0
        };

        if (roll < 0.18 + tempoRisk + passRiskBonus)
        {
            return EventType.BadPass;
        }

        if (roll < 0.34 + tempoRisk + controlRiskBonus)
        {
            return EventType.Miscontrol;
        }

        if (roll < 0.50 + pressingBias)
        {
            return EventType.Tackle;
        }

        if (roll < 0.68 + pressingBias + narrowBias)
        {
            return EventType.Interception;
        }

        return roll < 0.88 + pressingBias
            ? EventType.Pressure
            : EventType.BlockedPass;
    }

    private static bool IsDefenderPossessionWin(EventType eventType)
    {
        return eventType is EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass;
    }

    private static EventType AdjustPossessionLossReasonForDefenderTraits(EventType reasonType, Player defender, Random random)
    {
        if (defender.Traits.Contains(PlayerTrait.Interceptor) && random.NextDouble() < 0.45)
        {
            return EventType.Interception;
        }

        if (defender.Traits.Contains(PlayerTrait.DivesIntoTackles) && random.NextDouble() < 0.42)
        {
            return EventType.Tackle;
        }

        return reasonType;
    }

    private static PlayerTrait? GetTriggeredPossessionWinTrait(Player? defender, EventType reasonType)
    {
        if (defender is null)
        {
            return null;
        }

        if (reasonType == EventType.Interception && defender.Traits.Contains(PlayerTrait.Interceptor))
        {
            return PlayerTrait.Interceptor;
        }

        if (reasonType == EventType.Tackle && defender.Traits.Contains(PlayerTrait.DivesIntoTackles))
        {
            return PlayerTrait.DivesIntoTackles;
        }

        return null;
    }

    private static void ApplyPossessionWinContribution(Match match, Team defendingTeam, Player defender, EventType reasonType, Random random)
    {
        var performance = GetOrCreatePerformance(match, defendingTeam, defender);
        switch (reasonType)
        {
            case EventType.Tackle:
                performance.Tackles++;
                performance.Rating += GetDefensiveActionRating(defender, 0.20, 0.40, random);
                break;

            case EventType.Interception:
                performance.Interceptions++;
                performance.Rating += GetDefensiveActionRating(defender, 0.20, 0.40, random);
                break;

            case EventType.BlockedPass:
                performance.Blocks++;
                performance.Recoveries++;
                performance.Rating += GetDefensiveActionRating(defender, 0.20, 0.35, random);
                break;

            case EventType.Pressure:
                performance.Recoveries++;
                performance.Rating += GetDefensiveActionRating(defender, 0.20, 0.25, random);
                break;
        }
    }

    private Team HandleFoul(
        int minute,
        MatchSimulationState simulationState,
        Team attackingTeam,
        Team defendingTeam,
        Player fouledPlayer,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        MatchTeamStats defendingStats,
        FoulContext foulContext)
    {
        var match = simulationState.Match;
        var defender = ChooseDefendingPlayer(defendingTeam, random);
        matchLog.AddEvent(eventFactory.CreateFoul(
            minute,
            defendingTeam,
            attackingTeam,
            defender,
            fouledPlayer,
            foulContext.Location,
            foulContext.IsPenalty,
            defender.Traits.Contains(PlayerTrait.DivesIntoTackles) ? PlayerTrait.DivesIntoTackles : null));
        defendingStats.Fouls++;
        var defenderPerformance = GetOrCreatePerformance(match, defendingTeam, defender);
        defenderPerformance.Fouls++;
        defenderPerformance.Rating -= 0.15;
        GetOrCreatePerformance(match, GetOpposingTeam(match, defendingTeam), fouledPlayer).Rating += 0.05;

        var redCardReason = GetStraightRedReason(foulContext, defender, simulationState, defendingTeam, minute, random);
        if (redCardReason is not null)
        {
            if (TryCancelRedCardWithVar(minute, match, defendingTeam, defender, random, matchLog, eventFactory))
            {
                defenderPerformance.Rating += 0.12;
            }
            else
            {
                ApplyRedCard(minute, simulationState, defendingTeam, attackingTeam, defender, defenderPerformance, defendingStats, matchLog, eventFactory, redCardReason);
            }
        }
        else if (ShouldGiveYellowCard(defender, simulationState, foulContext, minute, random))
        {
            ApplyYellowCard(
                minute,
                simulationState,
                defendingTeam,
                attackingTeam,
                defender,
                defenderPerformance,
                defendingStats,
                matchLog,
                eventFactory,
                random);
        }
        else
        {
            TryAddRefereeControversy(minute, simulationState, defendingTeam, attackingTeam, defender, fouledPlayer, foulContext, random, matchLog, eventFactory);
        }

        if (foulContext.IsPenalty)
        {
            defenderPerformance.PenaltiesConceded++;
            defenderPerformance.ErrorsLeadingToShot++;
            defenderPerformance.Rating -= 0.18;
            return HandlePenaltySequence(minute, simulationState, match, attackingTeam, defendingTeam, defender, fouledPlayer, random, matchLog, eventFactory, foulContext);
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
        var triggeredSetPieceTrait = primaryTaker.Traits.Contains(PlayerTrait.DeadBallSpecialist)
            ? PlayerTrait.DeadBallSpecialist
            : (PlayerTrait?)null;

        if (!ShouldTakeDirectFreeKick(primaryTaker, random))
        {
            return HandleIndirectFreeKickSequence(
                minute,
                match,
                attackingTeam,
                defendingTeam,
                primaryTaker,
                random,
                matchLog,
                eventFactory,
                triggeredSetPieceTrait);
        }

        matchLog.AddEvent(eventFactory.CreateSetPieceShot(
            minute,
            attackingTeam,
            primaryTaker,
            triggeredSetPieceTrait));

        var attackingStats = GetTeamStats(match, attackingTeam);
        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += 0.09;
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, primaryTaker);
        takerPerformance.Shots++;
        takerPerformance.KeyPasses++;
        takerPerformance.Rating += 0.12;

        var takerAttributes = PlayerAttributeService.GetAttributes(primaryTaker);
        var goalProbability = Math.Clamp(0.025 + (primaryTaker.Finishing * 0.45 + takerAttributes.Shooting * 0.35 + takerAttributes.Passing * 0.20) / 1700.0 +
            (primaryTaker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 0.040 : 0.0) +
            (primaryTaker.Traits.Contains(PlayerTrait.FinesseShot) ? 0.020 : 0.0), 0.035, 0.14);
        var roll = random.NextDouble();
        if (roll < goalProbability)
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            takerPerformance.ShotsOnTarget++;
            takerPerformance.Goals++;
            takerPerformance.Rating += GetGoalRatingBoost(takerPerformance.Goals, goalProbability);
            matchLog.AddEvent(eventFactory.CreateGoal(
                minute,
                attackingTeam,
                primaryTaker,
                match,
                "direct free-kick strike",
                random,
                triggeredTrait: primaryTaker.Traits.Contains(PlayerTrait.DeadBallSpecialist)
                    ? PlayerTrait.DeadBallSpecialist
                    : primaryTaker.Traits.Contains(PlayerTrait.FinesseShot)
                        ? PlayerTrait.FinesseShot
                        : null,
                scorerMatchGoals: takerPerformance.Goals));
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
                matchLog.AddEvent(eventFactory.CreateSave(
                    minute,
                    defendingTeam,
                    primaryTaker,
                    goalkeeper,
                    ChooseSaveType(goalkeeper, "free-kick", random),
                    random));
                TryAddGoalkeeperHeroicsAfterSave(
                    minute,
                    match,
                    attackingTeam,
                    defendingTeam,
                    goalkeeper,
                    "free-kick",
                    goalProbability,
                    isPenaltySave: false,
                    matchLog,
                    eventFactory);
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, primaryTaker));
            }

            return defendingTeam;
        }

        if (roll < goalProbability + 0.55)
        {
            var defender = ApplyShotBlockContribution(match, defendingTeam, random, ratingBoostOverride: 0.08);
            matchLog.AddEvent(eventFactory.CreateBlockedShot(minute, defendingTeam, defender, primaryTaker, random));
            return random.NextDouble() < 0.25
                ? HandleCornerSequence(minute, match, attackingTeam, defendingTeam, primaryTaker, random, matchLog, eventFactory)
                : defendingTeam;
        }

        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, primaryTaker, "free-kick", random));
        takerPerformance.Rating -= 0.04;
        return defendingTeam;
    }

    private Team HandleIndirectFreeKickSequence(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player taker,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        PlayerTrait? triggeredSetPieceTrait)
    {
        var target = ChooseSetPieceTarget(attackingTeam, taker, random);
        var attackingStats = GetTeamStats(match, attackingTeam);
        var targetPerformance = GetOrCreatePerformance(match, attackingTeam, target);
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, taker);
        var targetAttributes = PlayerAttributeService.GetAttributes(target);
        var takerAttributes = PlayerAttributeService.GetAttributes(taker);
        var goalProbability = Math.Clamp(
            0.045 +
            Math.Max(0, targetAttributes.Physical - 72) * 0.0008 +
            Math.Max(0, takerAttributes.Passing - 72) * 0.0005 +
            (target.Traits.Contains(PlayerTrait.PowerHeader) ? 0.026 : 0.0) +
            (target.Traits.Contains(PlayerTrait.AerialThreat) ? 0.022 : 0.0) +
            (triggeredSetPieceTrait == PlayerTrait.DeadBallSpecialist ? 0.014 : 0.0),
            0.035,
            0.115);

        matchLog.AddEvent(eventFactory.CreateSetPieceDelivery(
            minute,
            attackingTeam,
            taker,
            target,
            triggeredSetPieceTrait));
        matchLog.AddEvent(eventFactory.CreateShot(
            minute,
            attackingTeam,
            target,
            taker,
            "free-kick header",
            random,
            triggeredSetPieceTrait));

        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += goalProbability;
        targetPerformance.Shots++;
        takerPerformance.KeyPasses++;
        takerPerformance.Rating += 0.08;

        var roll = random.NextDouble();
        if (roll < goalProbability)
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            targetPerformance.ShotsOnTarget++;
            targetPerformance.Goals++;
            targetPerformance.Rating += GetGoalRatingBoost(targetPerformance.Goals, goalProbability);
            if (!string.Equals(target.Name, taker.Name, StringComparison.OrdinalIgnoreCase))
            {
                takerPerformance.Assists++;
                takerPerformance.Rating += GetAssistRatingBoost(goalProbability);
            }

            matchLog.AddEvent(eventFactory.CreateGoal(
                minute,
                attackingTeam,
                target,
                match,
                target.Traits.Contains(PlayerTrait.PowerHeader)
                    ? "power header from a free kick"
                    : "header from a free kick",
                random,
                target == taker ? null : taker,
                target.Traits.Contains(PlayerTrait.PowerHeader)
                    ? PlayerTrait.PowerHeader
                    : target.Traits.Contains(PlayerTrait.AerialThreat)
                        ? PlayerTrait.AerialThreat
                        : triggeredSetPieceTrait,
                targetPerformance.Goals));
            return defendingTeam;
        }

        if (roll < goalProbability + 0.30)
        {
            attackingStats.ShotsOnTarget++;
            targetPerformance.ShotsOnTarget++;
            var goalkeeper = GetGoalkeeper(defendingTeam);
            if (goalkeeper is not null)
            {
                var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                goalkeeperPerformance.Saves++;
                goalkeeperPerformance.Rating += 0.26;
                matchLog.AddEvent(eventFactory.CreateSave(
                    minute,
                    defendingTeam,
                    target,
                    goalkeeper,
                    ChooseSaveType(goalkeeper, "free-kick delivery", random),
                    random));
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, target));
            }

            return defendingTeam;
        }

        if (roll < goalProbability + 0.58)
        {
            var defender = ApplyShotBlockContribution(match, defendingTeam, random, ratingBoostOverride: 0.08);
            matchLog.AddEvent(eventFactory.CreateBlockedShot(minute, defendingTeam, defender, target, random));
            return defendingTeam;
        }

        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, target, "free-kick header", random));
        targetPerformance.Rating -= 0.04;
        return defendingTeam;
    }

    private Team HandlePenaltySequence(
        int minute,
        MatchSimulationState simulationState,
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
        var savedChance = taker.Traits.Contains(PlayerTrait.PenaltySpecialist) ? 0.78 : 0.62;
        var saved = !converted && random.NextDouble() < savedChance;
        var goalkeeper = saved ? GetGoalkeeper(defendingTeam) : null;

        matchLog.AddEvent(eventFactory.CreatePenaltyDecision(minute, defendingTeam, defender, fouledPlayer, foulContext.PenaltyReason));
        var penaltyVarReason = foulContext.PenaltyReason.Contains("handle", StringComparison.OrdinalIgnoreCase)
            ? "handball penalty"
            : "penalty";
        if (TryOverturnPenaltyWithVar(minute, simulationState, match, defendingTeam, penaltyVarReason, random, matchLog, eventFactory))
        {
            GetOrCreatePerformance(match, attackingTeam, fouledPlayer).Rating -= 0.05;
            return defendingTeam;
        }

        matchLog.AddEvent(eventFactory.CreatePenaltyTaker(
            minute,
            attackingTeam,
            taker,
            taker.Traits.Contains(PlayerTrait.PenaltySpecialist) ? PlayerTrait.PenaltySpecialist : null));

        var attackingStats = GetTeamStats(match, attackingTeam);
        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += 0.76;
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, taker);
        takerPerformance.Shots++;

        if (converted)
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            GetOrCreatePerformance(match, defendingTeam, defender).ErrorsLeadingToGoal++;
            takerPerformance.ShotsOnTarget++;
            takerPerformance.Goals++;
            takerPerformance.Rating += GetGoalRatingBoost(takerPerformance.Goals, GetPenaltyConversionChance(taker));
        }
        else
        {
            takerPerformance.Rating -= 0.35;
            if (saved)
            {
                attackingStats.ShotsOnTarget++;
                if (goalkeeper is not null)
                {
                    var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                    goalkeeperPerformance.Saves++;
                    goalkeeperPerformance.Rating += 0.80;
                }
            }
        }

        matchLog.AddEvent(eventFactory.CreatePenaltyResult(
            minute,
            attackingTeam,
            taker,
            converted,
            saved,
            match,
            converted ? GetTriggeredPenaltyTrait(taker) : null,
            defendingTeam,
            goalkeeper,
            converted ? takerPerformance.Goals : 0));
        if (saved && goalkeeper is not null)
        {
            TryAddGoalkeeperHeroicsAfterSave(
                minute,
                match,
                attackingTeam,
                defendingTeam,
                goalkeeper,
                "penalty save",
                0.76,
                isPenaltySave: true,
                matchLog,
                eventFactory);
        }

        return defendingTeam;
    }

    private static void TryAddGoalkeeperHeroicsAfterSave(
        int minute,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player goalkeeper,
        string chanceType,
        double goalProbability,
        bool isPenaltySave,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        if (defendingTeam != GetOpposingTeam(match, attackingTeam) ||
            !goalkeeper.TeamIs(defendingTeam) ||
            goalkeeper.TeamIs(attackingTeam))
        {
            return;
        }

        var previousEvent = matchLog.GetEvents().LastOrDefault();
        if (!IsSaveByGoalkeeper(previousEvent, goalkeeper))
        {
            return;
        }

        var heroicsRandom = new Random(
            minute +
            goalkeeper.Name.Length * 17 +
            chanceType.Length * 31 +
            attackingTeam.Name.Length * 43 +
            defendingTeam.Name.Length * 53);
        if (!isPenaltySave && !ShouldCreateGoalkeeperHeroics(minute, match, chanceType, goalProbability, heroicsRandom))
        {
            return;
        }

        matchLog.ReplaceLastEvent(eventFactory.CreateGoalkeeperHeroics(minute, defendingTeam, goalkeeper));
    }

    private static bool IsSaveByGoalkeeper(MatchEvent? matchEvent, Player goalkeeper)
    {
        return matchEvent?.EventType == EventType.Save &&
            (string.Equals(matchEvent.SecondaryPlayerName, goalkeeper.Name, StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains(goalkeeper.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldCreateGoalkeeperHeroics(
        int minute,
        Match match,
        string chanceType,
        double goalProbability,
        Random random)
    {
        var isOneOnOne = chanceType is "through ball attempt" or "dribble run";
        var isBigChance = goalProbability >= 0.20;
        var isLatePressure = minute >= 80 && Math.Abs(match.HomeScore - match.AwayScore) <= 1;

        if (!isOneOnOne && !isBigChance && !isLatePressure)
        {
            return false;
        }

        var probability =
            0.16 +
            (isOneOnOne ? 0.10 : 0.0) +
            (isBigChance ? 0.12 : 0.0) +
            (isLatePressure ? 0.12 : 0.0);

        return random.NextDouble() < Math.Clamp(probability, 0.16, 0.50);
    }

    private Team HandleMistakePunishmentSequence(
        int minute,
        MatchSimulationState simulationState,
        Match match,
        Team attackingTeam,
        Team defendingTeam,
        Player mistakePlayer,
        Player? preferredAttacker,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        MatchTeamStats attackingStats,
        double attackingTeamStrength,
        double defendingTeamStrength,
        string mistakeType)
    {
        var attacker = preferredAttacker ?? ChooseShooter(attackingTeam, ChoosePlaymaker(attackingTeam, random), random);
        var playmaker = ResolveDistinctAttackingPlayer(attackingTeam, attacker, random);
        var chanceType = mistakeType == "keeper" ? "rebound shot" : "one-on-one";
        var errorBoost = mistakeType == "keeper" ? 0.20 : 0.16;

        matchLog.AddEvent(eventFactory.CreateMistakeChance(minute, attackingTeam, defendingTeam, attacker, mistakePlayer, mistakeType, random));
        GetOrCreatePerformance(match, attackingTeam, attacker).Rating += mistakeType == "keeper" ? 0.14 : 0.10;

        attackingStats.TotalShots++;
        matchLog.AddEvent(eventFactory.CreateMistakePunishmentShot(minute, attackingTeam, attacker, mistakePlayer, mistakeType, random));

        return HandleShotOutcome(
            minute,
            simulationState,
            match,
            attackingTeam,
            defendingTeam,
            attacker,
            playmaker,
            random,
            matchLog,
            eventFactory,
            attackingStats,
            attackingTeamStrength,
            defendingTeamStrength,
            shooterHasSuperSubBoost: false,
            chanceType,
            errorBoost);
    }

    private static Player ResolveDistinctAttackingPlayer(Team attackingTeam, Player attacker, Random random)
    {
        var candidates = GetActiveOutfieldPlayers(attackingTeam)
            .Where(player => !string.Equals(player.Name, attacker.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.Count == 0
            ? attacker
            : candidates[random.Next(candidates.Count)];
    }

    private Team HandleShotOutcome(
        int minute,
        MatchSimulationState simulationState,
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
        var shooterPerformance = GetOrCreatePerformance(match, attackingTeam, shooter);
        var scorerKey = CreateErrorPlayerKey(attackingTeam, shooter);
        var scorerCooldownPenalty = GetScorerCooldownPenalty(simulationState, scorerKey, minute);
        var goalProbability = Math.Clamp(
            CalculateGoalProbability(
                shooter,
                match,
                defendingTeam,
                chanceType,
                attackingTeamStrength,
                defendingTeamStrength,
                random,
                shooterHasSuperSubBoost,
                shooterPerformance.Goals,
                scorerCooldownPenalty) + defensiveErrorBoost,
            MatchConstants.MinimumGoalProbability,
            MatchConstants.MaximumGoalProbability);
        attackingStats.ExpectedGoals += goalProbability;

        shooterPerformance.Shots++;

        if (playmaker != shooter)
        {
            var playmakerPerformance = GetOrCreatePerformance(match, attackingTeam, playmaker);
            playmakerPerformance.KeyPasses++;
            playmakerPerformance.Rating += goalProbability >= 0.18 ? 0.16 : 0.07;
        }

        if (ShouldScoreWonderGoal(shooter, chanceType, random))
        {
            attackingStats.ShotsOnTarget++;
            UpdateScore(match, attackingTeam);
            shooterPerformance.ShotsOnTarget++;
            shooterPerformance.Goals++;
            shooterPerformance.Rating += GetGoalRatingBoost(shooterPerformance.Goals, goalProbability, isWonderGoal: true);
            matchLog.AddEvent(eventFactory.CreateWonderGoal(
                minute,
                attackingTeam,
                shooter,
                match,
                GetTriggeredWonderGoalTrait(shooter, chanceType),
                shooterPerformance.Goals));
            ApplyConfidenceBoost(shooter);
            if (TryDisallowGoalWithVar(minute, simulationState, match, attackingTeam, shooter, null, random, matchLog, eventFactory))
            {
                attackingStats.ShotsOnTarget = Math.Max(0, attackingStats.ShotsOnTarget - 1);
                shooterPerformance.ShotsOnTarget = Math.Max(0, shooterPerformance.ShotsOnTarget - 1);
                shooterPerformance.Goals = Math.Max(0, shooterPerformance.Goals - 1);
                shooterPerformance.Rating -= GetGoalRatingBoost(shooterPerformance.Goals + 1, goalProbability, isWonderGoal: true);
            }
            else
            {
                RegisterScorerGoal(simulationState, scorerKey, minute);
                ApplyHomeGoalMomentum(match, attackingTeam);
                TryAddHomeGoalCrowdSurge(simulationState, minute, match, attackingTeam);
            }

            return defendingTeam;
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
                ownGoalPerformance.ErrorsLeadingToGoal++;
                shooterPerformance.Rating += 0.25;
                matchLog.AddEvent(eventFactory.CreateOwnGoal(minute, attackingTeam, defendingTeam, ownGoalPlayer, match));
                return defendingTeam;
            }

            shooterPerformance.Goals++;
            shooterPerformance.Rating += GetGoalRatingBoost(shooterPerformance.Goals, goalProbability);

            var assister = playmaker != shooter ? playmaker : null;
            if (assister is not null)
            {
                var assisterPerformance = GetOrCreatePerformance(match, attackingTeam, assister);
                assisterPerformance.Assists++;
                assisterPerformance.Rating += GetAssistRatingBoost(goalProbability);
            }

            var goalTypeDescription = ChooseGoalTypeDescription(shooter, random, chanceType, chanceType.Contains("corner", StringComparison.OrdinalIgnoreCase));
            matchLog.AddEvent(eventFactory.CreateGoal(
                minute,
                attackingTeam,
                shooter,
                match,
                goalTypeDescription,
                random,
                assister,
                GetTriggeredGoalTrait(shooter, assister, chanceType, goalTypeDescription),
                shooterPerformance.Goals));
            ApplyConfidenceBoost(shooter);
            if (TryDisallowGoalWithVar(minute, simulationState, match, attackingTeam, shooter, assister, random, matchLog, eventFactory))
            {
                attackingStats.ShotsOnTarget = Math.Max(0, attackingStats.ShotsOnTarget - 1);
                shooterPerformance.ShotsOnTarget = Math.Max(0, shooterPerformance.ShotsOnTarget - 1);
                shooterPerformance.Goals = Math.Max(0, shooterPerformance.Goals - 1);
                shooterPerformance.Rating -= GetGoalRatingBoost(shooterPerformance.Goals + 1, goalProbability);
                if (assister is not null)
                {
                    var assisterPerformance = GetOrCreatePerformance(match, attackingTeam, assister);
                    assisterPerformance.Assists = Math.Max(0, assisterPerformance.Assists - 1);
                    assisterPerformance.Rating -= GetAssistRatingBoost(goalProbability) * 0.55;
                }
            }
            else
            {
                RegisterScorerGoal(simulationState, scorerKey, minute);
                ApplyHomeGoalMomentum(match, attackingTeam);
                TryAddHomeGoalCrowdSurge(simulationState, minute, match, attackingTeam);
            }

            return defendingTeam;
        }

        if (ShouldHitWoodwork(goalProbability, match.WeatherCondition, random))
        {
            var reboundOutcome = ChooseWoodworkReboundOutcome(random);
            matchLog.AddEvent(eventFactory.CreateWoodwork(minute, attackingTeam, shooter, reboundOutcome, random));
            ApplyConfidenceDrop(shooter, goalProbability >= 0.20 ? 0.03 : 0.015);

            if (reboundOutcome.Contains("cleared", StringComparison.OrdinalIgnoreCase))
            {
                var reboundDefender = ApplyShotBlockContribution(match, defendingTeam, random, ratingBoostOverride: 0.07);
                matchLog.AddEvent(eventFactory.CreateReboundClearance(minute, defendingTeam, reboundDefender, shooter, random));
            }
            else if (reboundOutcome.Contains("keeper", StringComparison.OrdinalIgnoreCase))
            {
                var goalkeeper = GetGoalkeeper(defendingTeam);
                if (goalkeeper is not null)
                {
                    GetOrCreatePerformance(match, defendingTeam, goalkeeper).Rating += 0.10;
                    matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, shooter, goalkeeper, "follow-up claim", random));
                }
            }
            else if (random.NextDouble() < 0.22)
            {
                attackingStats.Corners++;
                return HandleCornerSequence(minute, match, attackingTeam, defendingTeam, playmaker, random, matchLog, eventFactory);
            }

            return defendingTeam;
        }

        var wasSaved = IsSaved(defendingTeam, shooter, chanceType, random);
        if (wasSaved)
        {
            Player? goalkeeper = null;
            var saveWasClean = true;
            attackingStats.ShotsOnTarget++;
            shooterPerformance.ShotsOnTarget++;
            shooterPerformance.Rating += 0.08;
            goalkeeper = GetGoalkeeper(defendingTeam);
            if (goalkeeper is not null)
            {
                var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                goalkeeperPerformance.Saves++;
                goalkeeperPerformance.Rating += 0.20 + goalProbability;
                var saveType = ChooseSaveType(goalkeeper, chanceType, random);
                matchLog.AddEvent(eventFactory.CreateSave(
                    minute,
                    defendingTeam,
                    shooter,
                    goalkeeper,
                    saveType,
                    random,
                    GetTriggeredSaveTrait(goalkeeper, chanceType)));
                if (ShouldCreateGoalkeeperMistake(simulationState, defendingTeam, goalkeeper, minute, match.WeatherCondition, random))
                {
                    saveWasClean = false;
                    RegisterGoalkeeperMistake(simulationState, defendingTeam);
                    matchLog.AddEvent(eventFactory.CreateGoalkeeperMistake(minute, defendingTeam, goalkeeper, shooter, random));
                    goalkeeperPerformance.Rating -= 0.35;
                    if (random.NextDouble() < 0.90)
                    {
                        return HandleMistakePunishmentSequence(
                            minute,
                            simulationState,
                            match,
                            attackingTeam,
                            defendingTeam,
                            goalkeeper,
                            shooter,
                            random,
                            matchLog,
                            eventFactory,
                            attackingStats,
                            attackingTeamStrength,
                            defendingTeamStrength,
                            "keeper");
                    }
                }
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, shooter));
            }

            if (goalkeeper is not null && saveWasClean)
            {
                TryAddGoalkeeperHeroicsAfterSave(
                    minute,
                    match,
                    attackingTeam,
                    defendingTeam,
                    goalkeeper,
                    chanceType,
                    goalProbability,
                    isPenaltySave: false,
                    matchLog,
                    eventFactory);
            }

            if (random.NextDouble() < 0.20)
            {
                attackingStats.Corners++;
                return HandleCornerSequence(minute, match, attackingTeam, defendingTeam, playmaker, random, matchLog, eventFactory);
            }
            return defendingTeam;
        }

        var blockProbability = Math.Clamp(0.20 + GetDefensiveShotBlockPressure(defendingTeam, defendingTeamStrength, attackingTeamStrength), 0.14, 0.42);
        if (random.NextDouble() < blockProbability)
        {
            if (goalProbability >= 0.28 && random.NextDouble() < 0.18)
            {
                var goalLineDefender = ApplyGoalLineClearance(match, defendingTeam, random);
                matchLog.AddEvent(eventFactory.CreateGoalLineClearance(minute, defendingTeam, goalLineDefender, shooter, random));
            }
            else
            {
                var defender = ApplyShotBlockContribution(match, defendingTeam, random, ratingBoostOverride: 0.06);
                matchLog.AddEvent(eventFactory.CreateBlockedShot(minute, defendingTeam, defender, shooter, random));
            }

            if (random.NextDouble() < 0.12)
            {
                attackingStats.Corners++;
                return HandleCornerSequence(minute, match, attackingTeam, defendingTeam, playmaker, random, matchLog, eventFactory);
            }

            return defendingTeam;
        }

        shooterPerformance.Rating -= GetMissedChancePenalty(goalProbability);
        var shotStyle = ChooseShotStyle(shooter, random, chanceType);
        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, shooter, shotStyle, random));
        ApplyConfidenceDrop(shooter, goalProbability >= 0.20 ? 0.04 : 0.015);
        return defendingTeam;
    }

    private static bool TryDisallowGoalWithVar(
        int minute,
        MatchSimulationState simulationState,
        Match match,
        Team attackingTeam,
        Player scorer,
        Player? assister,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        if (random.NextDouble() < GetDirectOffsideDisallowChance(match))
        {
            UndoScore(match, attackingTeam);
            ApplyDisallowedGoalMoraleDrop(scorer, assister);
            matchLog.AddEvent(eventFactory.CreateDisallowedGoalOffside(minute, attackingTeam, scorer, assister, match, random));
            var defendingTeam = GetOpposingTeam(match, attackingTeam);
            TryAddCrowdMomentum(simulationState, minute, defendingTeam, () => eventFactory.CreateCrowdMomentum(minute, defendingTeam));
            return true;
        }

        var checkChance = match.IsRivalryMatch ? 0.18 : 0.14;
        if (random.NextDouble() > checkChance)
        {
            return false;
        }

        var reviewReason = ChooseGoalVarReviewReason(random);
        matchLog.AddEvent(eventFactory.CreateVarCheck(minute, reviewReason.CheckReason));
        var disallowed = random.NextDouble() < reviewReason.DisallowChance;
        if (!disallowed)
        {
            matchLog.AddEvent(eventFactory.CreateVarDecision(minute, attackingTeam, "goal stands", match, scorer, assister));
            return false;
        }

        UndoScore(match, attackingTeam);
        ApplyDisallowedGoalMoraleDrop(scorer, assister);
        matchLog.AddEvent(eventFactory.CreateVarDecision(minute, attackingTeam, reviewReason.DisallowedOutcome, match, scorer, assister));
        var opponentTeam = GetOpposingTeam(match, attackingTeam);
        TryAddCrowdMomentum(simulationState, minute, opponentTeam, () => eventFactory.CreateCrowdMomentum(minute, opponentTeam));
        return true;
    }

    private static double GetDirectOffsideDisallowChance(Match match)
    {
        return match.IsRivalryMatch ? 0.09 : 0.07;
    }

    private static GoalVarReviewReason ChooseGoalVarReviewReason(Random random)
    {
        return random.NextDouble() switch
        {
            < 0.58 => new GoalVarReviewReason("goal", "goal disallowed offside", 0.30),
            < 0.82 => new GoalVarReviewReason("foul buildup", "goal disallowed foul", 0.24),
            _ => new GoalVarReviewReason("handball buildup", "goal disallowed handball", 0.22)
        };
    }

    private static void ApplyDisallowedGoalMoraleDrop(Player scorer, Player? assister)
    {
        scorer.Morale = Math.Clamp(scorer.Morale - 2, 0, 100);
        if (assister is not null)
        {
            assister.Morale = Math.Clamp(assister.Morale - 1, 0, 100);
        }
    }

    private double CalculateDefensiveErrorChance(
        MatchSimulationState simulationState,
        Team attackingTeam,
        Team defendingTeam,
        Player defender,
        PlayerMatchPerformance defenderPerformance,
        double attackingTeamStrength,
        double defendingTeamStrength,
        int minute)
    {
        var staminaRisk = Math.Max(0, 55 - defender.Stamina) / 2600.0;
        var passingRisk = Math.Max(0, 68 - defender.Passing) / 4500.0;
        var defenseRisk = Math.Max(0, 70 - defender.Defense) / 5200.0;
        var formRisk = Math.Max(0, 6.0 - defenderPerformance.Rating) / 140.0;
        var heavyPressRisk = Math.Max(0, attackingTeam.Tactics.PressingIntensity - 68) / 2400.0;
        var pressureRisk = Math.Max(0, attackingTeamStrength - defendingTeamStrength) / 12000.0;
        var weatherRisk = simulationState.Match.WeatherCondition switch
        {
            WeatherCondition.HeavyRain or WeatherCondition.Storm or WeatherCondition.Snow => 0.007,
            WeatherCondition.Rainy or WeatherCondition.Foggy => 0.004,
            _ => 0.0
        };
        var tacticalRisk =
            Math.Max(0, defendingTeam.Tactics.DefensiveLine - 72) / 4500.0 +
            Math.Max(0, defendingTeam.Tactics.Tempo - 74) / 5500.0;
        var mentalityRisk = defendingTeam.Tactics.Mentality switch
        {
            Mentality.UltraDefensive => 0.004,
            Mentality.AllOutAttack => 0.003,
            _ => 0.0
        };
        var positionModifier = defender.Position switch
        {
            Position.Defender => 0.72,
            Position.Goalkeeper => 0.60,
            Position.Midfielder => 1.05,
            _ => 1.18
        };
        var tacticalModifier = Math.Clamp(_tacticalImpactCalculator.GetDefensiveErrorModifier(defendingTeam), 0.70, 1.35);
        var venueModifier = HomeAwayAdvantageService.GetModifier(simulationState.Match, defendingTeam).DefensiveErrorRiskModifier;
        var tensionRisk = IsMatchTense(simulationState, minute) ? 0.002 : 0.0;

        return Math.Clamp(
            (0.006 + staminaRisk + passingRisk + defenseRisk + formRisk + heavyPressRisk + pressureRisk + weatherRisk + tacticalRisk + mentalityRisk + tensionRisk) *
            positionModifier *
            tacticalModifier *
            venueModifier,
            0.002,
            0.030);
    }

    private static bool TryRegisterDefensiveError(
        MatchSimulationState simulationState,
        Team defendingTeam,
        Player defender,
        int minute,
        Random random)
    {
        if (!CanCreateDefensiveError(simulationState, defendingTeam, defender, minute))
        {
            return false;
        }

        IncrementCount(simulationState.TeamDefensiveErrorCounts, defendingTeam.Name);
        simulationState.TeamDefensiveErrorCooldownUntil[defendingTeam.Name] = minute + random.Next(10, 21);
        simulationState.PlayerDefensiveErrorCooldownUntil[CreateErrorPlayerKey(defendingTeam, defender)] = minute + random.Next(20, 31);
        return true;
    }

    private static bool CanCreateDefensiveError(MatchSimulationState simulationState, Team defendingTeam, Player defender, int minute)
    {
        var teamCount = GetCount(simulationState.TeamDefensiveErrorCounts, defendingTeam.Name);
        var softCap = IsChaoticErrorMatch(simulationState, minute) ? 3 : 2;
        if (teamCount >= softCap)
        {
            return false;
        }

        if (simulationState.TeamDefensiveErrorCooldownUntil.TryGetValue(defendingTeam.Name, out var teamCooldown) &&
            minute <= teamCooldown)
        {
            return false;
        }

        var playerKey = CreateErrorPlayerKey(defendingTeam, defender);
        return !simulationState.PlayerDefensiveErrorCooldownUntil.TryGetValue(playerKey, out var playerCooldown) ||
            minute > playerCooldown;
    }

    private static bool CanCreateGoalkeeperMistake(MatchSimulationState simulationState, Team defendingTeam, int minute)
    {
        var maxKeeperErrors = IsChaoticErrorMatch(simulationState, minute) ? 2 : 1;
        return GetCount(simulationState.TeamGoalkeeperErrorCounts, defendingTeam.Name) < maxKeeperErrors;
    }

    private static void RegisterGoalkeeperMistake(MatchSimulationState simulationState, Team defendingTeam)
    {
        IncrementCount(simulationState.TeamGoalkeeperErrorCounts, defendingTeam.Name);
    }

    private static bool IsChaoticErrorMatch(MatchSimulationState simulationState, int minute)
    {
        return simulationState.Match.IsRivalryMatch ||
            IsMatchTense(simulationState, minute) ||
            simulationState.Match.WeatherCondition is WeatherCondition.HeavyRain or WeatherCondition.Storm or WeatherCondition.Snow;
    }

    private static string CreateErrorPlayerKey(Team team, Player player)
    {
        return $"{team.Name}|{player.Name}";
    }

    private static int GetCount(Dictionary<string, int> counts, string key)
    {
        return counts.TryGetValue(key, out var count) ? count : 0;
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        counts[key] = GetCount(counts, key) + 1;
    }

    private static bool TryOverturnPenaltyWithVar(
        int minute,
        MatchSimulationState simulationState,
        Match match,
        Team defendingTeam,
        string checkReason,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        if (random.NextDouble() > 0.07)
        {
            return false;
        }

        matchLog.AddEvent(eventFactory.CreateVarCheck(minute, checkReason));
        var overturned = random.NextDouble() < 0.28;
        if (!overturned)
        {
            matchLog.AddEvent(eventFactory.CreateVarDecision(minute, defendingTeam, "penalty stands", match));
            return false;
        }

        matchLog.AddEvent(eventFactory.CreateVarDecision(minute, defendingTeam, checkReason == "handball penalty" ? "no penalty" : "penalty overturned", match));
        TryAddCrowdMomentum(simulationState, minute, defendingTeam, () => eventFactory.CreateCrowdMomentum(minute, defendingTeam));
        return true;
    }

    private static bool TryCancelRedCardWithVar(
        int minute,
        Match match,
        Team defendingTeam,
        Player defender,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        if (random.NextDouble() > 0.10)
        {
            return false;
        }

        matchLog.AddEvent(eventFactory.CreateVarCheck(minute, "red card"));
        var cancelled = random.NextDouble() < 0.25;
        if (!cancelled)
        {
            matchLog.AddEvent(eventFactory.CreateVarDecision(minute, defendingTeam, "decision stands", match));
            return false;
        }

        defender.Morale = Math.Clamp(defender.Morale + 2, 0, 100);
        matchLog.AddEvent(eventFactory.CreateVarDecision(minute, defendingTeam, "no red card", match));
        return true;
    }

    private void TryAddRefereeControversy(
        int minute,
        MatchSimulationState simulationState,
        Team defendingTeam,
        Team attackingTeam,
        Player defender,
        Player fouledPlayer,
        FoulContext foulContext,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        var controversyChance = foulContext.IsPenalty || foulContext.DeniesClearChance ? 0.12 : 0.045;
        var shouldAddControversy = random.NextDouble() <= controversyChance;
        var shouldAddConfrontation = CanCreateContextualConfrontation(simulationState, minute, EventType.Foul) &&
            random.NextDouble() < GetFoulConfrontationChance(simulationState.Match, defender, foulContext);
        if (!shouldAddControversy && !shouldAddConfrontation)
        {
            return;
        }

        if (shouldAddControversy)
        {
            matchLog.AddEvent(eventFactory.CreateRefereeControversy(minute, defendingTeam, defender, random));
        }

        if (shouldAddConfrontation)
        {
            var reason = GetConfrontationReason(simulationState.Match, defender, foulContext);
            matchLog.AddEvent(eventFactory.CreateConfrontation(minute, defendingTeam, defender, fouledPlayer, reason));
            RegisterConfrontation(simulationState, minute);
            ResolveConfrontationOutcome(
                minute,
                simulationState,
                defendingTeam,
                attackingTeam,
                defender,
                fouledPlayer,
                random,
                matchLog,
                eventFactory);
        }
    }

    private void ResolveConfrontationOutcome(
        int minute,
        MatchSimulationState simulationState,
        Team firstTeam,
        Team secondTeam,
        Player firstPlayer,
        Player secondPlayer,
        Random random,
        MatchLogService matchLog,
        MatchEventFactory eventFactory)
    {
        IncreaseMatchTension(simulationState, minute, random);
        ApplyConfrontationComposureDrop(firstPlayer, secondPlayer);

        var roll = random.NextDouble();
        if (roll < 0.003)
        {
            var offender = ChooseConfrontationOffender(firstPlayer, secondPlayer, random);
            var offenderTeam = IsPlayerOnTeam(offender, firstTeam) ? firstTeam : secondTeam;
            var opponentTeam = offenderTeam == firstTeam ? secondTeam : firstTeam;
            if (!CanIssueRedCard(simulationState, offenderTeam, minute))
            {
                roll = 0.25;
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateVarCheck(minute, "red card"));
                matchLog.AddEvent(eventFactory.CreateVarDecision(minute, offenderTeam, "red card upgraded", simulationState.Match));
                ApplyRedCard(
                    minute,
                    simulationState,
                    offenderTeam,
                    opponentTeam,
                    offender,
                    GetOrCreatePerformance(simulationState.Match, offenderTeam, offender),
                    GetTeamStats(simulationState.Match, offenderTeam),
                    matchLog,
                    eventFactory,
                    "violent conduct");
                return;
            }
        }

        if (roll < 0.25)
        {
            matchLog.AddEvent(eventFactory.CreateDoubleBooking(minute, firstPlayer, secondPlayer));
            ApplyYellowCard(minute, simulationState, firstTeam, secondTeam, firstPlayer, GetOrCreatePerformance(simulationState.Match, firstTeam, firstPlayer), GetTeamStats(simulationState.Match, firstTeam), matchLog, eventFactory, random, addCardEvent: false);
            ApplyYellowCard(minute, simulationState, secondTeam, firstTeam, secondPlayer, GetOrCreatePerformance(simulationState.Match, secondTeam, secondPlayer), GetTeamStats(simulationState.Match, secondTeam), matchLog, eventFactory, random, addCardEvent: false);
            return;
        }

        if (roll < 0.90)
        {
            var bookedPlayer = ChooseConfrontationOffender(firstPlayer, secondPlayer, random);
            var bookedTeam = IsPlayerOnTeam(bookedPlayer, firstTeam) ? firstTeam : secondTeam;
            var opposingTeam = bookedTeam == firstTeam ? secondTeam : firstTeam;
            ApplyYellowCard(
                minute,
                simulationState,
                bookedTeam,
                opposingTeam,
                bookedPlayer,
                GetOrCreatePerformance(simulationState.Match, bookedTeam, bookedPlayer),
                GetTeamStats(simulationState.Match, bookedTeam),
                matchLog,
                eventFactory,
                random,
                reason: "after the confrontation");
            return;
        }

        matchLog.AddEvent(eventFactory.CreateRefereeWarning(minute, firstPlayer, secondPlayer));
    }

    private static double GetFoulConfrontationChance(Match match, Player defender, FoulContext foulContext)
    {
        var chance = 0.065;
        if (foulContext.ViolentFoul || foulContext.DeniesClearChance)
        {
            chance = 0.18;
        }
        else if (foulContext.IsPenalty)
        {
            chance = 0.13;
        }

        if (defender.Traits.Contains(PlayerTrait.DivesIntoTackles))
        {
            chance += 0.025;
        }

        if (match.IsRivalryMatch)
        {
            chance += 0.035;
        }

        return Math.Clamp(chance, 0.03, 0.25);
    }

    private static bool CanCreateContextualConfrontation(
        MatchSimulationState simulationState,
        int minute,
        EventType? triggerEventType)
    {
        if (!IsConfrontationTriggerEvent(triggerEventType, simulationState.Match.IsRivalryMatch))
        {
            return false;
        }

        var latestEventType = simulationState.MatchLog.GetEvents().LastOrDefault()?.EventType;
        if (latestEventType.HasValue &&
            latestEventType != triggerEventType &&
            !IsConfrontationTriggerEvent(latestEventType, simulationState.Match.IsRivalryMatch))
        {
            return false;
        }

        var cap = simulationState.Match.IsRivalryMatch ? 3 : 2;
        if (simulationState.ConfrontationCount >= cap)
        {
            return false;
        }

        return simulationState.LastConfrontationMinute + 20 <= minute;
    }

    private static bool IsConfrontationTriggerEvent(EventType? eventType, bool isRivalryMatch)
    {
        if (isRivalryMatch && eventType is EventType.RivalryAtmosphere or EventType.RefereeControversy)
        {
            return true;
        }

        return eventType is EventType.Foul
            or EventType.PenaltyDecision
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.VarCheck
            or EventType.VarDecision
            or EventType.RefereeControversy
            or EventType.Offside
            or EventType.CornerKick
            or EventType.SetPieceDanger;
    }

    private static bool IsSubstitutionStoppageEvent(EventType? eventType)
    {
        return eventType is EventType.Halftime
            or EventType.Injury
            or EventType.Foul
            or EventType.PenaltyDecision
            or EventType.PenaltyTaker
            or EventType.Penalty
            or EventType.Offside
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Goal
            or EventType.WonderGoal
            or EventType.VarCheck
            or EventType.VarDecision
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.RefereeControversy
            or EventType.Confrontation
            or EventType.TimeWasting;
    }

    private static void RegisterConfrontation(MatchSimulationState simulationState, int minute)
    {
        simulationState.LastConfrontationMinute = minute;
        simulationState.ConfrontationCount++;
    }

    private void ApplyYellowCard(
        int minute,
        MatchSimulationState simulationState,
        Team bookedTeam,
        Team opposingTeam,
        Player player,
        PlayerMatchPerformance performance,
        MatchTeamStats bookedTeamStats,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        Random random,
        bool addCardEvent = true,
        string? reason = null)
    {
        var match = simulationState.Match;
        if (player.IsSentOff)
        {
            return;
        }

        if (player.YellowCards >= 1 && !CanIssueRedCard(simulationState, bookedTeam, minute))
        {
            return;
        }

        if (addCardEvent && string.IsNullOrWhiteSpace(reason))
        {
            _ = random.NextDouble();
        }

        if (addCardEvent)
        {
            matchLog.AddEvent(eventFactory.CreateYellowCard(minute, player, reason));
        }

        performance.YellowCards++;
        performance.Rating -= 0.35;

        if (_disciplinaryService.ApplyYellowCard(player, bookedTeamStats))
        {
            ApplyRedCard(minute, simulationState, bookedTeam, opposingTeam, player, performance, bookedTeamStats, matchLog, eventFactory, "second yellow");
            return;
        }
    }

    private static void IncreaseMatchTension(MatchSimulationState simulationState, int minute, Random random)
    {
        simulationState.TensionUntilMinute = Math.Max(simulationState.TensionUntilMinute, minute + random.Next(5, 11));
    }

    private static bool IsMatchTense(MatchSimulationState simulationState, int minute)
    {
        return minute <= simulationState.TensionUntilMinute;
    }

    private static void ApplyConfrontationComposureDrop(params Player[] players)
    {
        foreach (var player in players.Where(player => !player.IsSentOff))
        {
            player.Morale = Math.Clamp(player.Morale - 1, 0, 100);
            player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier - 0.015, 0.70, 1.18);
        }
    }

    private static Player ChooseConfrontationOpponent(Team team, Random random)
    {
        var candidates = GetActivePitchPlayers(team)
            .Where(player => !player.IsInjured)
            .ToList();

        if (candidates.Count == 0)
        {
            return team.Players.FirstOrDefault() ?? new Player { Name = team.Name };
        }

        return candidates
            .OrderByDescending(player =>
                (player.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 35 : 0) +
                player.YellowCards * 24 +
                Math.Max(0, 50 - player.Morale) * 0.35 +
                random.NextDouble())
            .First();
    }

    private static Player ChooseConfrontationOffender(Player firstPlayer, Player secondPlayer, Random random)
    {
        var firstWeight = GetConfrontationDisciplineWeight(firstPlayer);
        var secondWeight = GetConfrontationDisciplineWeight(secondPlayer);
        return random.NextDouble() * (firstWeight + secondWeight) <= firstWeight
            ? firstPlayer
            : secondPlayer;
    }

    private static double GetConfrontationDisciplineWeight(Player player)
    {
        return 1.0 +
            (player.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 0.55 : 0.0) +
            player.YellowCards * 0.45 +
            Math.Max(0, 50 - player.Morale) / 120.0;
    }

    private static bool IsPlayerOnTeam(Player player, Team team)
    {
        return team.Players.Concat(team.Substitutes)
            .Any(candidate => string.Equals(candidate.Name, player.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetConfrontationReason(Match match, Player defender, FoulContext foulContext)
    {
        if (foulContext.IsPenalty)
        {
            return "penalty incident";
        }

        if (foulContext.ViolentFoul || foulContext.DeniesClearChance)
        {
            return "dangerous foul";
        }

        if (match.IsRivalryMatch)
        {
            return "rivalry tension";
        }

        if (defender.Traits.Contains(PlayerTrait.DivesIntoTackles))
        {
            return "aggressive player";
        }

        return defender.Morale < 45 ? "frustration" : "late tackle";
    }

    private static bool ShouldHitWoodwork(double goalProbability, WeatherCondition weatherCondition, Random random)
    {
        var weatherBonus = weatherCondition switch
        {
            WeatherCondition.Storm => 0.015,
            WeatherCondition.Windy or WeatherCondition.HeavyRain => 0.01,
            _ => 0.0
        };
        var probability = Math.Clamp(0.035 + goalProbability * 0.20 + weatherBonus, 0.035, 0.10);
        return random.NextDouble() < probability;
    }

    private static string ChooseWoodworkReboundOutcome(Random random)
    {
        return random.NextDouble() switch
        {
            < 0.34 => "The rebound is cleared by the defense.",
            < 0.62 => "The rebound drops safely to the keeper.",
            < 0.82 => "It spins behind for a corner.",
            _ => "It bounces back into traffic and chaos follows."
        };
    }

    private static bool ShouldCreateGoalkeeperMistake(
        MatchSimulationState simulationState,
        Team defendingTeam,
        Player goalkeeper,
        int minute,
        WeatherCondition weatherCondition,
        Random random)
    {
        if (!CanCreateGoalkeeperMistake(simulationState, defendingTeam, minute))
        {
            return false;
        }

        var weatherBonus = weatherCondition switch
        {
            WeatherCondition.HeavyRain or WeatherCondition.Storm => 0.006,
            WeatherCondition.Rainy or WeatherCondition.Snow => 0.004,
            WeatherCondition.Windy or WeatherCondition.Foggy => 0.002,
            _ => 0.0
        };
        var distributionRisk = Math.Max(0, 72 - goalkeeper.Passing) / 2500.0;
        var ratingRisk = Math.Max(0, 76 - goalkeeper.OverallRating) / 3000.0;
        var fatigueRisk = Math.Max(0, 55 - goalkeeper.Stamina) / 3500.0;

        return random.NextDouble() < Math.Clamp(0.004 + weatherBonus + distributionRisk + ratingRisk + fatigueRisk, 0.003, 0.018);
    }

    private static void ApplyConfidenceBoost(Player player)
    {
        player.Morale = Math.Clamp(player.Morale + 2, 0, 100);
        player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier + 0.02, 0.70, 1.18);
    }

    private static void ApplyHomeGoalMomentum(Match match, Team scoringTeam)
    {
        if (!HomeAwayAdvantageService.IsHomeTeam(match, scoringTeam))
        {
            return;
        }

        foreach (var player in GetActivePitchPlayers(scoringTeam))
        {
            player.Morale = Math.Clamp(player.Morale + 1, 0, 100);
            player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier + 0.006, 0.70, 1.18);
        }
    }

    private static void ApplyConfidenceDrop(Player player, double modifierDrop)
    {
        player.Morale = Math.Clamp(player.Morale - 1, 0, 100);
        player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier - modifierDrop, 0.70, 1.18);
    }

    private static double GetGoalRatingBoost(int scorerMatchGoals, double goalProbability, bool isWonderGoal = false)
    {
        var chanceQualityBonus = Math.Min(goalProbability, 0.18);
        var boost = scorerMatchGoals switch
        {
            <= 1 => 0.62 + chanceQualityBonus,
            2 => 0.45 + Math.Min(goalProbability, 0.12),
            3 => 0.50 + Math.Min(goalProbability, 0.12),
            _ => 0.32 + Math.Min(goalProbability, 0.08)
        };

        return isWonderGoal
            ? Math.Min(1.05, boost + 0.18)
            : Math.Min(0.95, boost);
    }

    private static double GetAssistRatingBoost(double goalProbability)
    {
        return Math.Clamp(0.38 + goalProbability * 0.75, 0.40, 0.66);
    }

    private static double GetMissedChancePenalty(double goalProbability)
    {
        return goalProbability switch
        {
            >= 0.30 => 0.44,
            >= 0.22 => 0.34,
            >= 0.15 => 0.20,
            _ => 0.06
        };
    }

    private static double GetScorerCooldownPenalty(MatchSimulationState simulationState, string scorerKey, int minute)
    {
        if (!simulationState.LastGoalMinuteByPlayer.TryGetValue(scorerKey, out var lastGoalMinute))
        {
            return 0.0;
        }

        var minutesSinceGoal = minute - lastGoalMinute;
        if (minutesSinceGoal <= 5)
        {
            return 0.08;
        }

        if (minutesSinceGoal <= 10)
        {
            return 0.04;
        }

        return 0.0;
    }

    private static void RegisterScorerGoal(MatchSimulationState simulationState, string scorerKey, int minute)
    {
        simulationState.LastGoalMinuteByPlayer[scorerKey] = minute;
    }

    private static bool ShouldScoreWonderGoal(Player shooter, string chanceType, Random random)
    {
        var probability = chanceType switch
        {
            "long-range attempt" => 0.010,
            "dribble run" => 0.009,
            "quick combination" => 0.004,
            _ => 0.002
        };

        probability +=
            (shooter.Traits.Contains(PlayerTrait.LongShotTaker) ? 0.010 : 0.0) +
            (shooter.Traits.Contains(PlayerTrait.FinesseShot) ? 0.008 : 0.0) +
            (shooter.Traits.Contains(PlayerTrait.OutsideFootShot) ? 0.006 : 0.0) +
            (shooter.Traits.Contains(PlayerTrait.Flair) ? 0.006 : 0.0) +
            (shooter.Traits.Contains(PlayerTrait.TechnicalDribbler) ? 0.005 : 0.0) +
            (shooter.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 0.004 : 0.0);

        return random.NextDouble() < Math.Clamp(probability, 0.002, 0.035);
    }

    private static PlayerTrait? GetTriggeredBuildUpTrait(Match match, Team attackingTeam, Player playmaker, int minute, Random random)
    {
        if (random.NextDouble() > 0.24)
        {
            return null;
        }

        if (playmaker.Traits.Contains(PlayerTrait.Engine) && minute >= 72)
        {
            return PlayerTrait.Engine;
        }

        if ((playmaker.Traits.Contains(PlayerTrait.Leadership) || playmaker.Traits.Contains(PlayerTrait.BigMatchPlayer)) &&
            (IsTeamLosing(match, attackingTeam) || minute >= 65))
        {
            return playmaker.Traits.Contains(PlayerTrait.BigMatchPlayer)
                ? PlayerTrait.BigMatchPlayer
                : PlayerTrait.Leadership;
        }

        if (playmaker.Traits.Contains(PlayerTrait.PressResistant))
        {
            return PlayerTrait.PressResistant;
        }

        if (playmaker.Traits.Contains(PlayerTrait.TeamPlayer))
        {
            return PlayerTrait.TeamPlayer;
        }

        if (playmaker.Traits.Contains(PlayerTrait.BoxToBox))
        {
            return PlayerTrait.BoxToBox;
        }

        if (playmaker.Traits.Contains(PlayerTrait.LongThrower))
        {
            return PlayerTrait.LongThrower;
        }

        if (playmaker.Traits.Contains(PlayerTrait.LongPasser))
        {
            return PlayerTrait.LongPasser;
        }

        return playmaker.Traits.Contains(PlayerTrait.Playmaker) ? PlayerTrait.Playmaker : null;
    }

    private static PlayerTrait? GetTriggeredShotCreationTrait(Player playmaker, Player shooter, string chanceType)
    {
        if (playmaker.Traits.Contains(PlayerTrait.TeamPlayer) && playmaker != shooter &&
            chanceType is "quick combination" or "cross into box")
        {
            return PlayerTrait.TeamPlayer;
        }

        if (playmaker.Traits.Contains(PlayerTrait.Playmaker) && playmaker != shooter &&
            chanceType is "quick combination" or "through ball attempt")
        {
            return PlayerTrait.Playmaker;
        }

        if (playmaker.Traits.Contains(PlayerTrait.LongPasser) && chanceType == "through ball attempt")
        {
            return PlayerTrait.LongPasser;
        }

        if (playmaker.Traits.Contains(PlayerTrait.EarlyCrosser) && chanceType == "cross into box")
        {
            return PlayerTrait.EarlyCrosser;
        }

        if (shooter.Traits.Contains(PlayerTrait.TriesToBeatOffsideTrap) && chanceType == "through ball attempt")
        {
            return PlayerTrait.TriesToBeatOffsideTrap;
        }

        if ((shooter.Traits.Contains(PlayerTrait.SpeedDribbler) || shooter.Traits.Contains(PlayerTrait.Rapid)) && chanceType == "dribble run")
        {
            return shooter.Traits.Contains(PlayerTrait.SpeedDribbler) ? PlayerTrait.SpeedDribbler : PlayerTrait.Rapid;
        }

        if (shooter.Traits.Contains(PlayerTrait.TechnicalDribbler) && chanceType == "dribble run")
        {
            return PlayerTrait.TechnicalDribbler;
        }

        if (shooter.Traits.Contains(PlayerTrait.Flair) && chanceType == "dribble run")
        {
            return PlayerTrait.Flair;
        }

        if (shooter.Traits.Contains(PlayerTrait.LongShotTaker) && chanceType == "long-range attempt")
        {
            return PlayerTrait.LongShotTaker;
        }

        if (shooter.Traits.Contains(PlayerTrait.FinesseShot) && chanceType == "long-range attempt")
        {
            return PlayerTrait.FinesseShot;
        }

        return null;
    }

    private static Player ResolveChanceCreator(Player playmaker, Player shooter, string chanceType)
    {
        return chanceType is "dribble run" or "long-range attempt" or "rebound shot" or "one-on-one"
            ? shooter
            : playmaker;
    }

    private AttackSequence CreateOpenPlayAttackSequence(
        Team attackingTeam,
        Player creator,
        Player shooter,
        string chanceType,
        Random random)
    {
        var isSoloMove = IsSoloChanceType(chanceType);
        var resolvedShooter = isSoloMove
            ? shooter
            : ResolveDistinctShooter(attackingTeam, creator, shooter, random);

        return new AttackSequence(
            CreatorPlayer: isSoloMove ? resolvedShooter : creator,
            ShooterPlayer: resolvedShooter,
            SetPieceTaker: null,
            IsSoloMove: isSoloMove,
            AttackType: AttackType.OpenPlay);
    }

    private static bool IsSoloChanceType(string chanceType)
    {
        return chanceType is "dribble run" or "long-range attempt" or "rebound shot" or "one-on-one";
    }

    private Player ResolveDistinctShooter(Team attackingTeam, Player creator, Player shooter, Random random)
    {
        if (!string.Equals(creator.Name, shooter.Name, StringComparison.OrdinalIgnoreCase))
        {
            return shooter;
        }

        var candidates = GetAvailablePlayers(attackingTeam)
            .Where(player =>
                !string.Equals(player.Name, creator.Name, StringComparison.OrdinalIgnoreCase) &&
                IsOutfieldPlayer(player) &&
                (player.Position is Position.Forward or Position.Midfielder ||
                    player.Traits.Contains(PlayerTrait.PowerHeader) ||
                    player.Traits.Contains(PlayerTrait.AerialThreat)))
            .ToList();

        if (candidates.Count == 0)
        {
            return shooter;
        }

        return candidates
            .OrderByDescending(candidate => _teamStrengthCalculator.GetShooterWeight(candidate))
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static PlayerTrait? GetTriggeredWonderGoalTrait(Player shooter, string chanceType)
    {
        if (shooter.Traits.Contains(PlayerTrait.FinesseShot) && (chanceType is "long-range attempt" or "quick combination"))
        {
            return PlayerTrait.FinesseShot;
        }

        if (shooter.Traits.Contains(PlayerTrait.LongShotTaker) && chanceType == "long-range attempt")
        {
            return PlayerTrait.LongShotTaker;
        }

        if (shooter.Traits.Contains(PlayerTrait.Flair) && chanceType == "dribble run")
        {
            return PlayerTrait.Flair;
        }

        if (shooter.Traits.Contains(PlayerTrait.SpeedDribbler) && chanceType == "dribble run")
        {
            return PlayerTrait.SpeedDribbler;
        }

        if (shooter.Traits.Contains(PlayerTrait.Rapid) && chanceType == "dribble run")
        {
            return PlayerTrait.Rapid;
        }

        if (shooter.Traits.Contains(PlayerTrait.TechnicalDribbler) && chanceType == "dribble run")
        {
            return PlayerTrait.TechnicalDribbler;
        }

        if (shooter.Traits.Contains(PlayerTrait.OutsideFootShot))
        {
            return PlayerTrait.OutsideFootShot;
        }

        return shooter.Traits.Contains(PlayerTrait.ClinicalFinisher) ? PlayerTrait.ClinicalFinisher : null;
    }

    private static PlayerTrait? GetTriggeredGoalTrait(Player scorer, Player? assister, string chanceType, string goalTypeDescription)
    {
        if (goalTypeDescription.Contains("finesse", StringComparison.OrdinalIgnoreCase) && scorer.Traits.Contains(PlayerTrait.FinesseShot))
        {
            return PlayerTrait.FinesseShot;
        }

        if (goalTypeDescription.Contains("header", StringComparison.OrdinalIgnoreCase))
        {
            if (scorer.Traits.Contains(PlayerTrait.PowerHeader))
            {
                return PlayerTrait.PowerHeader;
            }

            if (scorer.Traits.Contains(PlayerTrait.AerialThreat))
            {
                return PlayerTrait.AerialThreat;
            }
        }

        if (scorer.Traits.Contains(PlayerTrait.OutsideFootShot) &&
            goalTypeDescription.Contains("outside", StringComparison.OrdinalIgnoreCase))
        {
            return PlayerTrait.OutsideFootShot;
        }

        if (assister?.Traits.Contains(PlayerTrait.Playmaker) == true)
        {
            return PlayerTrait.Playmaker;
        }

        if (assister?.Traits.Contains(PlayerTrait.TeamPlayer) == true && chanceType == "quick combination")
        {
            return PlayerTrait.TeamPlayer;
        }

        if (assister?.Traits.Contains(PlayerTrait.LongPasser) == true && chanceType == "through ball attempt")
        {
            return PlayerTrait.LongPasser;
        }

        if (assister?.Traits.Contains(PlayerTrait.EarlyCrosser) == true && chanceType == "cross into box")
        {
            return PlayerTrait.EarlyCrosser;
        }

        if (chanceType == "through ball attempt" && scorer.Traits.Contains(PlayerTrait.TriesToBeatOffsideTrap))
        {
            return PlayerTrait.TriesToBeatOffsideTrap;
        }

        if (chanceType == "dribble run" && scorer.Traits.Contains(PlayerTrait.Flair))
        {
            return PlayerTrait.Flair;
        }

        if (chanceType == "dribble run" && scorer.Traits.Contains(PlayerTrait.SpeedDribbler))
        {
            return PlayerTrait.SpeedDribbler;
        }

        if (chanceType == "dribble run" && scorer.Traits.Contains(PlayerTrait.Rapid))
        {
            return PlayerTrait.Rapid;
        }

        if (chanceType == "dribble run" && scorer.Traits.Contains(PlayerTrait.TechnicalDribbler))
        {
            return PlayerTrait.TechnicalDribbler;
        }

        if (scorer.Traits.Contains(PlayerTrait.ClinicalFinisher))
        {
            return PlayerTrait.ClinicalFinisher;
        }

        return null;
    }

    private static PlayerTrait? GetTriggeredPenaltyTrait(Player taker)
    {
        if (taker.Traits.Contains(PlayerTrait.PenaltySpecialist))
        {
            return PlayerTrait.PenaltySpecialist;
        }

        if (taker.Traits.Contains(PlayerTrait.DeadBallSpecialist))
        {
            return PlayerTrait.DeadBallSpecialist;
        }

        if (taker.Traits.Contains(PlayerTrait.ClinicalFinisher))
        {
            return PlayerTrait.ClinicalFinisher;
        }

        return taker.Traits.Contains(PlayerTrait.FinesseShot) ? PlayerTrait.FinesseShot : null;
    }

    private static PlayerTrait? GetTriggeredSaveTrait(Player goalkeeper, string chanceType)
    {
        if (goalkeeper.Traits.Contains(PlayerTrait.OneOnOnes) && (chanceType is "through ball attempt" or "dribble run"))
        {
            return PlayerTrait.OneOnOnes;
        }

        if (goalkeeper.Traits.Contains(PlayerTrait.RushesOutOfGoal) && chanceType == "through ball attempt")
        {
            return PlayerTrait.RushesOutOfGoal;
        }

        return goalkeeper.Traits.Contains(PlayerTrait.Puncher) && chanceType.Contains("cross", StringComparison.OrdinalIgnoreCase)
            ? PlayerTrait.Puncher
            : null;
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
        matchLog.AddEvent(eventFactory.CreateCornerKick(
            minute,
            attackingTeam,
            taker,
            taker.Traits.Contains(PlayerTrait.DeadBallSpecialist)
                ? PlayerTrait.DeadBallSpecialist
                : taker.Traits.Contains(PlayerTrait.EarlyCrosser)
                    ? PlayerTrait.EarlyCrosser
                    : null));
        var attackSequence = new AttackSequence(
            CreatorPlayer: taker,
            ShooterPlayer: ChooseCornerTarget(attackingTeam, taker, random),
            SetPieceTaker: taker,
            IsSoloMove: false,
            AttackType: AttackType.Corner);
        var target = attackSequence.ShooterPlayer;
        var attackingStats = GetTeamStats(match, attackingTeam);
        var targetPerformance = GetOrCreatePerformance(match, attackingTeam, target);
        var takerPerformance = GetOrCreatePerformance(match, attackingTeam, taker);

        takerPerformance.KeyPasses++;
        takerPerformance.Rating += 0.08;

        var roll = random.NextDouble();
        var goalChance = Math.Clamp(0.065 +
            (target.Traits.Contains(PlayerTrait.PowerHeader) ? 0.040 : 0.0) +
            (target.Traits.Contains(PlayerTrait.AerialThreat) ? 0.028 : 0.0) +
            (taker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 0.016 : 0.0) +
            (taker.Traits.Contains(PlayerTrait.EarlyCrosser) ? 0.012 : 0.0),
            0.045,
            0.145);
        if (roll < goalChance)
        {
            attackingStats.TotalShots++;
            attackingStats.ShotsOnTarget++;
            attackingStats.ExpectedGoals += 0.12;
            UpdateScore(match, attackingTeam);
            targetPerformance.Shots++;
            targetPerformance.ShotsOnTarget++;
            targetPerformance.Goals++;
            targetPerformance.Rating += GetGoalRatingBoost(targetPerformance.Goals, goalChance);
            if (target != taker)
            {
                takerPerformance.Assists++;
                takerPerformance.Rating += GetAssistRatingBoost(goalChance);
            }

            var goalTypeDescription = target.Traits.Contains(PlayerTrait.PowerHeader)
                ? "power header from a corner"
                : "header from a corner";
            matchLog.AddEvent(eventFactory.CreateShot(
                minute,
                attackingTeam,
                target,
                taker,
                "corner header",
                random,
                taker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? PlayerTrait.DeadBallSpecialist : null));
            matchLog.AddEvent(eventFactory.CreateGoal(
                minute,
                attackingTeam,
                target,
                match,
                goalTypeDescription,
                random,
                target == taker ? null : taker,
                target.Traits.Contains(PlayerTrait.PowerHeader)
                    ? PlayerTrait.PowerHeader
                    : target.Traits.Contains(PlayerTrait.AerialThreat)
                        ? PlayerTrait.AerialThreat
                        : taker.Traits.Contains(PlayerTrait.DeadBallSpecialist)
                            ? PlayerTrait.DeadBallSpecialist
                            : null,
                targetPerformance.Goals));
            return defendingTeam;
        }

        var goalkeeper = GetGoalkeeper(defendingTeam);
        var puncherAdjustment = goalkeeper?.Traits.Contains(PlayerTrait.Puncher) == true ? 0.06 : 0.0;
        if (roll < 0.34 + puncherAdjustment)
        {
            attackingStats.TotalShots++;
            attackingStats.ShotsOnTarget++;
            attackingStats.ExpectedGoals += 0.08;
            targetPerformance.Shots++;
            targetPerformance.ShotsOnTarget++;
            matchLog.AddEvent(eventFactory.CreateShot(
                minute,
                attackingTeam,
                target,
                taker,
                "corner header",
                random,
                taker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? PlayerTrait.DeadBallSpecialist : null));
            if (goalkeeper is not null)
            {
                var goalkeeperPerformance = GetOrCreatePerformance(match, defendingTeam, goalkeeper);
                goalkeeperPerformance.Saves++;
                goalkeeperPerformance.Rating += 0.30;
                matchLog.AddEvent(eventFactory.CreateSave(
                    minute,
                    defendingTeam,
                    target,
                    goalkeeper,
                    ChooseSaveType(goalkeeper, "corner delivery", random),
                    random,
                    goalkeeper.Traits.Contains(PlayerTrait.Puncher) ? PlayerTrait.Puncher : null));
                TryAddGoalkeeperHeroicsAfterSave(
                    minute,
                    match,
                    attackingTeam,
                    defendingTeam,
                    goalkeeper,
                    "corner delivery",
                    0.08,
                    isPenaltySave: false,
                    matchLog,
                    eventFactory);
            }
            else
            {
                matchLog.AddEvent(eventFactory.CreateSave(minute, defendingTeam, target));
            }

            return defendingTeam;
        }

        if (roll < 0.72)
        {
            attackingStats.TotalShots++;
            attackingStats.ExpectedGoals += 0.06;
            targetPerformance.Shots++;
            matchLog.AddEvent(eventFactory.CreateShot(
                minute,
                attackingTeam,
                target,
                taker,
                "corner header",
                random,
                taker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? PlayerTrait.DeadBallSpecialist : null));
            var defender = ApplyShotBlockContribution(match, defendingTeam, random, ratingBoostOverride: 0.08);
            matchLog.AddEvent(eventFactory.CreateBlockedShot(minute, defendingTeam, defender, target, random));
            return defendingTeam;
        }

        attackingStats.TotalShots++;
        attackingStats.ExpectedGoals += 0.05;
        targetPerformance.Shots++;
        matchLog.AddEvent(eventFactory.CreateShot(minute, attackingTeam, target, taker, "corner header", random));
        matchLog.AddEvent(eventFactory.CreateMiss(minute, attackingTeam, target, "corner chance", random));
        targetPerformance.Rating -= 0.04;
        return defendingTeam;
    }

    private static EventType GetLastEventType(MatchLogService matchLog, EventType fallback)
    {
        return matchLog.GetEvents().LastOrDefault()?.EventType ?? fallback;
    }

    private AttackFlowOutcome DetermineAttackOutcome(
        Team attackingTeam,
        Team defendingTeam,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random)
    {
        var attackBalance = attackingTeamStrength / Math.Max(1.0, attackingTeamStrength + defendingTeamStrength);
        var attackFlowModifier = _tacticalImpactCalculator.GetAttackFlowModifier(attackingTeam);
        var turnoverModifier = _tacticalImpactCalculator.GetTurnoverRiskModifier(attackingTeam, defendingTeam);
        var slowBuildUpBonus = Math.Max(0, 55 - attackingTeam.Tactics.Tempo) * 0.003;
        var directPlayBonus = Math.Max(0, attackingTeam.Tactics.Tempo - 65) * 0.003;

        var chanceProbability = Math.Clamp(
            (0.15 + attackBalance * 0.30 + directPlayBonus + GetProbabilitySwing(random, 0.05)) * attackFlowModifier,
            0.08,
            0.58);
        var buildupProbability = Math.Clamp(
            0.20 + (1.0 - attackBalance) * 0.18 + slowBuildUpBonus + GetProbabilitySwing(random, 0.06),
            0.08,
            0.48);
        var turnoverProbability = Math.Clamp(
            (0.26 + (0.55 - attackBalance) * 0.25 + GetProbabilitySwing(random, 0.05)) * turnoverModifier,
            0.08,
            0.68);

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

    private static string ChooseChanceType(Team attackingTeam, Team defendingTeam, WeatherCondition weatherCondition, Player playmaker, Player shooter, Random random)
    {
        var width = attackingTeam.Tactics.Width;
        var tempo = attackingTeam.Tactics.Tempo;
        var line = defendingTeam.Tactics.DefensiveLine;
        var playmakerAttributes = PlayerAttributeService.GetAttributes(playmaker);
        var shooterAttributes = PlayerAttributeService.GetAttributes(shooter);
        var passingBonus = Math.Max(0, playmakerAttributes.Passing - 72) * 0.035;
        var attackBonus = Math.Max(0, (shooterAttributes.Pace + shooterAttributes.Dribbling) / 2.0 - 72) * 0.030;
        var finishingBonus = Math.Max(0, shooterAttributes.Shooting - 72) * 0.025;

        var weightedChanceTypes = new List<(string Type, double Weight)>
        {
            ("long-range attempt", 1.0 + Math.Max(0, 40 - line) * 0.035 + finishingBonus + (shooter.Traits.Contains(PlayerTrait.LongShotTaker) ? 1.60 : 0.0) + (shooter.Traits.Contains(PlayerTrait.FinesseShot) ? 0.58 : 0.0) + (shooter.Traits.Contains(PlayerTrait.OutsideFootShot) ? 0.35 : 0.0)),
            ("cross into box", 1.0 + Math.Max(0, width - 50) * 0.045 + passingBonus * 0.65 + Math.Max(0, shooterAttributes.Physical - 72) * 0.020 + (playmaker.Traits.Contains(PlayerTrait.EarlyCrosser) ? 1.55 : 0.0) + (playmaker.Traits.Contains(PlayerTrait.LongPasser) ? 0.55 : 0.0) + (shooter.Traits.Contains(PlayerTrait.PowerHeader) ? 0.40 : 0.0) + (shooter.Traits.Contains(PlayerTrait.AerialThreat) ? 0.34 : 0.0)),
            ("through ball attempt", 1.0 + Math.Max(0, line - 50) * 0.045 + Math.Max(0, tempo - 55) * 0.025 + passingBonus + Math.Max(0, shooterAttributes.Pace - 72) * 0.030 + attackBonus * 0.45 + (playmaker.Traits.Contains(PlayerTrait.LongPasser) ? 1.25 : 0.0) + (playmaker.Traits.Contains(PlayerTrait.Playmaker) ? 0.55 : 0.0) + (shooter.Traits.Contains(PlayerTrait.TriesToBeatOffsideTrap) ? 0.95 : 0.0)),
            ("dribble run", 1.0 + Math.Max(0, width - 60) * 0.025 + Math.Max(0, tempo - 60) * 0.020 + Math.Max(0, shooterAttributes.Dribbling - 72) * 0.036 + Math.Max(0, shooterAttributes.Pace - 72) * 0.018 + (shooter.Traits.Contains(PlayerTrait.Rapid) ? 1.00 : 0.0) + (shooter.Traits.Contains(PlayerTrait.SpeedDribbler) ? 1.45 : 0.0) + (shooter.Traits.Contains(PlayerTrait.TechnicalDribbler) ? 0.85 : 0.0) + (shooter.Traits.Contains(PlayerTrait.Flair) ? 0.70 : 0.0)),
            ("quick combination", 1.0 + Math.Max(0, 50 - width) * 0.045 + Math.Max(0, 55 - tempo) * 0.020 + passingBonus + attackBonus * 0.45 + (playmaker.Traits.Contains(PlayerTrait.Playmaker) ? 1.45 : 0.0) + (playmaker.Traits.Contains(PlayerTrait.PressResistant) ? 0.65 : 0.0) + (playmaker.Traits.Contains(PlayerTrait.TeamPlayer) ? 0.90 : 0.0) + (shooter.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 0.35 : 0.0))
        };

        for (var index = 0; index < weightedChanceTypes.Count; index++)
        {
            var (type, weight) = weightedChanceTypes[index];
            weightedChanceTypes[index] = (type, weight * GetWeatherChanceTypeModifier(weatherCondition, type));
        }

        var totalWeight = weightedChanceTypes.Sum(chance => chance.Weight);
        var roll = random.NextDouble() * totalWeight;
        var runningWeight = 0.0;

        foreach (var chanceType in weightedChanceTypes)
        {
            runningWeight += chanceType.Weight;
            if (roll <= runningWeight)
            {
                return chanceType.Type;
            }
        }

        return "quick combination";
    }

    private static double GetWeatherChanceTypeModifier(WeatherCondition weatherCondition, string chanceType)
    {
        return weatherCondition switch
        {
            WeatherCondition.Storm when chanceType is "cross into box" => 0.74,
            WeatherCondition.Storm when chanceType is "long-range attempt" => 0.78,
            WeatherCondition.Storm when chanceType is "through ball attempt" => 0.88,
            WeatherCondition.Windy when chanceType is "cross into box" => 0.82,
            WeatherCondition.Windy when chanceType is "through ball attempt" => 0.86,
            WeatherCondition.Snow when chanceType is "dribble run" => 0.76,
            WeatherCondition.Snow when chanceType is "through ball attempt" => 0.90,
            WeatherCondition.HeavyRain when chanceType is "through ball attempt" => 0.87,
            WeatherCondition.HeavyRain when chanceType is "dribble run" => 0.84,
            _ => 1.0
        };
    }

    private static double GetPreShotDisruptionRisk(Match match, Team attackingTeam, Team defendingTeam, string chanceType, Player shooter)
    {
        var shooterAttributes = PlayerAttributeService.GetAttributes(shooter);
        var lowBlockBonus = defendingTeam.Tactics.DefensiveLine <= 40 ? 0.045 : 0.0;
        var defensiveMentalityBonus = defendingTeam.Tactics.Mentality is Mentality.Defensive ? 0.035 : 0.0;
        var pressureBonus = Math.Max(0, defendingTeam.Tactics.PressingIntensity - 60) * 0.001;
        var riskyTempoBonus = Math.Max(0, attackingTeam.Tactics.Tempo - 72) * 0.0012;
        var poorTouchRisk = Math.Max(0, 58 - ((shooterAttributes.Passing + shooterAttributes.Dribbling) / 2.0)) * 0.0014;
        var pressResistanceReduction = Math.Max(0, shooterAttributes.Dribbling - 78) * 0.0008;
        var awayComposureRisk = HomeAwayAdvantageService.IsHomeTeam(match, attackingTeam)
            ? -0.010
            : 0.035 * HomeAwayAdvantageService.GetAwayShapeMitigation(attackingTeam);
        var homePressRisk = HomeAwayAdvantageService.IsHomeTeam(match, defendingTeam)
            ? Math.Max(0, defendingTeam.Tactics.PressingIntensity - 55) * 0.0008
            : 0.0;
        var chanceTypeRisk = chanceType switch
        {
            "dribble run" => 0.025,
            "through ball attempt" => 0.018,
            "cross into box" => 0.014,
            _ => 0.0
        };

        return Math.Clamp(lowBlockBonus + defensiveMentalityBonus + pressureBonus + riskyTempoBonus + poorTouchRisk + awayComposureRisk + homePressRisk + chanceTypeRisk - pressResistanceReduction, 0.0, 0.16);
    }

    private static double GetDefensiveShotBlockPressure(Team defendingTeam, double defendingTeamStrength, double attackingTeamStrength)
    {
        var lowBlockBonus = defendingTeam.Tactics.DefensiveLine <= 40 ? 0.07 : 0.0;
        var defensiveMentalityBonus = defendingTeam.Tactics.Mentality is Mentality.Defensive or Mentality.UltraDefensive ? 0.06 : 0.0;
        var compactPressureBonus = Math.Max(0, defendingTeam.Tactics.PressingIntensity - 60) / 1000.0;
        var strengthBonus = Math.Clamp((defendingTeamStrength - attackingTeamStrength) / 900.0, -0.06, 0.08);

        return lowBlockBonus + defensiveMentalityBonus + compactPressureBonus + strengthBonus;
    }

    private static string ChooseSaveType(Player? goalkeeper, string chanceType, Random random)
    {
        if (goalkeeper?.Traits.Contains(PlayerTrait.Puncher) == true &&
            (chanceType.Contains("cross", StringComparison.OrdinalIgnoreCase) ||
             chanceType.Contains("corner", StringComparison.OrdinalIgnoreCase) ||
             random.NextDouble() < 0.35))
        {
            return "punch clear";
        }

        return random.Next(3) switch
        {
            0 => "diving save",
            1 => "reflex save",
            _ => "parry"
        };
    }

    private static string ChooseShotStyle(Player shooter, Random random, string chanceType)
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

        if (shooter.Traits.Contains(PlayerTrait.FinesseShot))
        {
            styles.Add("curling finesse shot");
            styles.Add("finesse shot");
        }

        if (shooter.Traits.Contains(PlayerTrait.OutsideFootShot))
        {
            styles.Add("outside-foot strike");
            styles.Add("trivela attempt");
        }

        if (shooter.Traits.Contains(PlayerTrait.PowerHeader))
        {
            styles.Add("power header");
        }

        if (shooter.Traits.Contains(PlayerTrait.ClinicalFinisher))
        {
            styles.Add("clinical finish");
        }

        if (random.NextDouble() < 0.08)
        {
            styles.Add("acrobatic attempt");
        }

        return styles[random.Next(styles.Count)];
    }

    private static string ChooseGoalTypeDescription(Player scorer, Random random, string chanceType, bool hasCornerPressure)
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

        if (scorer.Traits.Contains(PlayerTrait.FinesseShot))
        {
            options.Add("curling finesse shot");
            options.Add("finesse shot into the far corner");
        }

        if (scorer.Traits.Contains(PlayerTrait.PowerHeader))
        {
            options.Add("power header");
        }

        if (scorer.Traits.Contains(PlayerTrait.OutsideFootShot))
        {
            options.Add("outside-foot trivela finish");
            options.Add("outside-foot strike");
        }

        if (scorer.Traits.Contains(PlayerTrait.SpeedDribbler))
        {
            options.Add("solo run finish after a burst of pace");
        }

        if (scorer.Traits.Contains(PlayerTrait.ClinicalFinisher))
        {
            options.Add("clinical first-time finish");
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
        var concedingTeam = scoringTeam == match.HomeTeam ? match.AwayTeam : match.HomeTeam;
        if (scoringTeam == match.HomeTeam)
        {
            match.HomeScore++;
        }
        else
        {
            match.AwayScore++;
        }

        ApplyGoalMoraleSwing(scoringTeam, concedingTeam);
    }

    private static void UndoScore(Match match, Team scoringTeam)
    {
        if (scoringTeam == match.HomeTeam)
        {
            match.HomeScore = Math.Max(0, match.HomeScore - 1);
        }
        else
        {
            match.AwayScore = Math.Max(0, match.AwayScore - 1);
        }
    }

    private static void ApplyGoalMoraleSwing(Team scoringTeam, Team concedingTeam)
    {
        foreach (var player in GetActivePitchPlayers(scoringTeam))
        {
            player.Morale = Math.Clamp(player.Morale + 2, 0, 100);
        }

        var leadershipCount = GetActivePitchPlayers(concedingTeam)
            .Count(player => player.Traits.Contains(PlayerTrait.Leadership) || player.Traits.Contains(PlayerTrait.TeamPlayer));
        var moraleDrop = Math.Max(1, 4 - leadershipCount);

        foreach (var player in GetActivePitchPlayers(concedingTeam))
        {
            player.Morale = Math.Clamp(player.Morale - moraleDrop, 0, 100);
        }
    }

    private static int GetMatchGoalCount(Match match, Team team, Player player)
    {
        return match.PlayerPerformances.FirstOrDefault(performance =>
            string.Equals(performance.TeamName, team.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(performance.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase))?.Goals ?? 0;
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
                        performance.Rating += GetGoalRatingBoost(performance.Goals, 0.76);
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
                    performance.Rating += GetGoalRatingBoost(performance.Goals, 0.12, isWonderGoal: true);
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
                    performance.Rating -= 1.40;
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

            case EventType.RefereeControversy:
                foreach (var activePlayer in GetActivePitchPlayers(dramaResult.Team))
                {
                    activePlayer.Morale = Math.Clamp(activePlayer.Morale - 1, 0, 100);
                }

                break;

            case EventType.CrowdMomentum:
            case EventType.LateDrama:
                foreach (var activePlayer in GetActivePitchPlayers(dramaResult.Team))
                {
                    activePlayer.Morale = Math.Clamp(activePlayer.Morale + 1, 0, 100);
                    activePlayer.LiveMatchModifier = Math.Clamp(activePlayer.LiveMatchModifier + 0.01, 0.70, 1.16);
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
        var defensiveQuality = Math.Clamp(defender.Defense / 100.0, 0.0, 1.0);

        var roll = random.NextDouble() - ((defensiveQuality - 0.70) * 0.18);
        if (allowBlocks && roll < 0.22)
        {
            performance.Blocks++;
            performance.Rating += GetDefensiveActionRating(defender, 0.30, 0.50, random);
            return defender;
        }

        if (roll < 0.52)
        {
            performance.Interceptions++;
            performance.Rating += GetDefensiveActionRating(defender, 0.20, 0.40, random);
            return defender;
        }

        if (roll < 0.82)
        {
            performance.Tackles++;
            performance.Rating += GetDefensiveActionRating(defender, 0.20, 0.40, random);
            return defender;
        }

        performance.Clearances++;
        performance.Rating += GetDefensiveActionRating(defender, 0.10, 0.30, random);
        if (random.NextDouble() < 0.35)
        {
            performance.AerialDuelsWon++;
            performance.Rating += GetDefensiveActionRating(defender, 0.10, 0.25, random);
        }
        return defender;
    }

    private Player ApplyShotBlockContribution(
        Match match,
        Team defendingTeam,
        Random random,
        double ratingBoostOverride)
    {
        var defender = ChooseDefendingPlayer(defendingTeam, random);
        var performance = GetOrCreatePerformance(match, defendingTeam, defender);
        performance.Blocks++;
        performance.Clearances++;
        performance.Rating += GetDefensiveActionRating(defender, ratingBoostOverride, ratingBoostOverride + 0.18, random);
        return defender;
    }

    private Player ApplyGoalLineClearance(Match match, Team defendingTeam, Random random)
    {
        var defender = ChooseDefendingPlayer(defendingTeam, random);
        var performance = GetOrCreatePerformance(match, defendingTeam, defender);
        performance.Clearances++;
        performance.GoalLineClearances++;
        performance.Rating += GetDefensiveActionRating(defender, 0.80, 1.20, random);
        return defender;
    }

    private static double GetDefensiveActionRating(Player player, double min, double max, Random random)
    {
        var rating = min + random.NextDouble() * (max - min);
        var defenseQuality = Math.Clamp(player.Defense / 100.0, 0.0, 1.0);
        rating += (defenseQuality - 0.65) * 0.08;

        return Math.Round(rating * GetDefensiveActionMultiplier(player), 2);
    }

    private static double GetDefensiveActionMultiplier(Player player)
    {
        if (IsDefensiveSpecialist(player))
        {
            return 1.15;
        }

        return player.Position switch
        {
            Position.Midfielder => 0.85,
            Position.Forward => 0.55,
            Position.Goalkeeper => 0.70,
            _ => 1.0
        };
    }

    private static bool IsDefensiveSpecialist(Player player)
    {
        var assignedPosition = player.AssignedPosition ?? string.Empty;
        return player.Position == Position.Defender ||
            assignedPosition.Contains("CB", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("LB", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("RB", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("LWB", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("RWB", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("CDM", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("DM", StringComparison.OrdinalIgnoreCase);
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
            performance.Rating = match.CurrentPhase == MatchPhase.Fulltime
                ? PlayerMatchRatingService.CalculateContextualRating(match, performance)
                : Math.Round(Math.Clamp(performance.Rating, 1.0, 10.0), 1);
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
        return GetActivePitchPlayers(team).FirstOrDefault(PositionSuitabilityService.IsGoalkeeperCapable);
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

    private bool ShouldCommitFoul(
        MatchSimulationState simulationState,
        Team defendingTeam,
        double attackingTeamStrength,
        double defendingTeamStrength,
        int minute,
        Random random)
    {
        var match = simulationState.Match;
        var foulPressure = defendingTeamStrength - (attackingTeamStrength * 0.20);
        var aggressionBonus = GetActivePitchPlayers(defendingTeam)
            .Count(player => player.Traits.Contains(PlayerTrait.DivesIntoTackles)) * 0.022;
        var rivalryBonus = match.IsRivalryMatch ? 0.035 : 0.0;
        var tensionBonus = IsMatchTense(simulationState, minute) ? 0.045 : 0.0;
        var weatherBonus = match.WeatherCondition switch
        {
            WeatherCondition.Storm => 0.024,
            WeatherCondition.HeavyRain or WeatherCondition.Snow => 0.018,
            _ => 0.0
        };
        var tacticalModifier = _tacticalImpactCalculator.GetFoulModifier(defendingTeam);
        var venueModifier = HomeAwayAdvantageService.GetModifier(match, defendingTeam).FoulRiskModifier;
        var foulProbability = Math.Clamp(
            (MatchConstants.BaseFoulChancePerAttack + aggressionBonus + rivalryBonus + tensionBonus + weatherBonus + (foulPressure / 400.0) + GetProbabilitySwing(random, 0.03)) * tacticalModifier * venueModifier,
            0.05,
            IsMatchTense(simulationState, minute)
                ? 0.46
                : match.IsRivalryMatch ? 0.38 : 0.32);

        return random.NextDouble() < foulProbability;
    }

    private static bool ShouldGiveYellowCard(Player defender, MatchSimulationState simulationState, FoulContext foulContext, int minute, Random random)
    {
        var baseChance = foulContext.Severity switch
        {
            FoulSeverity.Minor => 0.03,
            FoulSeverity.Tactical => 0.18,
            FoulSeverity.Reckless => 0.45,
            FoulSeverity.Dangerous => 0.72,
            FoulSeverity.Dogso => 0.78,
            FoulSeverity.Violent => 0.85,
            _ => MatchConstants.YellowCardChancePerFoul
        };

        if (defender.YellowCards >= 1)
        {
            baseChance = foulContext.Severity switch
            {
                FoulSeverity.Minor => 0.0,
                FoulSeverity.Tactical => 0.03,
                FoulSeverity.Reckless => 0.055,
                FoulSeverity.Dangerous or FoulSeverity.Dogso or FoulSeverity.Violent => 0.08,
                _ => 0.04
            };
        }

        var traitBonus = defender.YellowCards >= 1
            ? 0.0
            : defender.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 0.08 : 0.0;
        var tensionBonus = defender.YellowCards >= 1
            ? 0.0
            : IsMatchTense(simulationState, minute) ? 0.05 : 0.0;
        var defendingTeam = ResolvePlayerTeam(simulationState.Match, defender);
        var venueModifier = defendingTeam is null
            ? 1.0
            : HomeAwayAdvantageService.GetModifier(simulationState.Match, defendingTeam).YellowRiskModifier;
        return random.NextDouble() < Math.Clamp((baseChance + traitBonus + tensionBonus) * venueModifier, 0.0, 0.88);
    }

    private static FoulContext CreateFoulContext(
        string chanceType,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random)
    {
        var isBoxThreat = chanceType is "cross into box" or "through ball attempt" or "dribble run" or "quick combination";
        var isClearThroughThreat = chanceType is "through ball attempt" or "dribble run";
        var attackPressure = attackingTeamStrength / Math.Max(1.0, attackingTeamStrength + defendingTeamStrength);
        var penaltyProbability = isBoxThreat
            ? Math.Clamp(0.16 + attackPressure * 0.18, 0.12, 0.34)
            : 0.0;
        var handball = isBoxThreat && random.NextDouble() < 0.06;
        var isPenalty = handball || random.NextDouble() < penaltyProbability;
        var dogsoChance = isClearThroughThreat
            ? Math.Clamp(0.012 + (attackPressure - 0.54) * 0.16, 0.0, 0.045)
            : chanceType == "quick combination"
                ? Math.Clamp((attackPressure - 0.58) * 0.08, 0.0, 0.018)
                : 0.0;
        var dogsoRoll = dogsoChance > 0.0 ? random.NextDouble() : 1.0;
        var deniesClearChance = !handball && dogsoRoll < dogsoChance;
        var violentRoll = random.NextDouble();
        var violentFoul = violentRoll < 0.002;
        var dangerousFoul = !violentFoul &&
            !deniesClearChance &&
            violentRoll >= 0.002 &&
            violentRoll < 0.002 + GetDangerousFoulChance(chanceType, attackPressure);
        var severity = GetFoulSeverity(chanceType, deniesClearChance, violentFoul, dangerousFoul, attackPressure, dogsoRoll, violentRoll);
        var createsDangerousSetPiece = !isPenalty &&
            (isBoxThreat || chanceType is "long-range attempt" or "long-range effort" or "wide overload") &&
            random.NextDouble() < 0.72;
        var location = isPenalty
            ? FoulLocation.PenaltyBox
            : createsDangerousSetPiece || isBoxThreat
                ? FoulLocation.FinalThird
                : FoulLocation.OpenPlay;
        var reason = handball ? "handles the ball near" : "fouls";

        return new FoulContext(severity, isPenalty, reason, deniesClearChance, violentFoul, createsDangerousSetPiece, location);
    }

    private static double GetDangerousFoulChance(string chanceType, double attackPressure)
    {
        var baseChance = chanceType is "dribble run" or "through ball attempt"
            ? 0.016
            : 0.009;
        return Math.Clamp(baseChance + Math.Max(0.0, attackPressure - 0.55) * 0.035, 0.006, 0.024);
    }

    private static FoulSeverity GetFoulSeverity(
        string chanceType,
        bool deniesClearChance,
        bool violentFoul,
        bool dangerousFoul,
        double attackPressure,
        double dogsoRoll,
        double violentRoll)
    {
        if (violentFoul)
        {
            return FoulSeverity.Violent;
        }

        if (deniesClearChance)
        {
            return FoulSeverity.Dogso;
        }

        if (dangerousFoul)
        {
            return FoulSeverity.Dangerous;
        }

        var recklessChance = chanceType is "dribble run" or "through ball attempt"
            ? 0.20
            : 0.12;
        recklessChance += Math.Max(0.0, attackPressure - 0.50) * 0.18;
        var severityRoll = (violentRoll * 997.0) % 1.0;
        if (severityRoll < Math.Clamp(recklessChance, 0.08, 0.28))
        {
            return FoulSeverity.Reckless;
        }

        var tacticalChance = chanceType is "quick combination" or "through ball attempt"
            ? 0.44
            : 0.28;
        var tacticalRoll = (dogsoRoll * 499.0 + attackPressure) % 1.0;
        if (tacticalRoll < tacticalChance)
        {
            return FoulSeverity.Tactical;
        }

        return FoulSeverity.Minor;
    }

    private static string? GetStraightRedReason(
        FoulContext foulContext,
        Player defender,
        MatchSimulationState simulationState,
        Team defendingTeam,
        int minute,
        Random random)
    {
        if (!CanIssueRedCard(simulationState, defendingTeam, minute))
        {
            return null;
        }

        var earlyMultiplier = minute < EarlyRedCardProtectionMinute ? 0.30 : 1.0;
        if (foulContext.Severity == FoulSeverity.Violent)
        {
            return random.NextDouble() < 0.88 * earlyMultiplier
                ? "violent conduct after an off-ball clash"
                : null;
        }

        if (foulContext.Severity == FoulSeverity.Dogso)
        {
            var dogsoChance = defender.Position == Position.Goalkeeper ? 0.72 : 0.62;
            if (random.NextDouble() >= dogsoChance * earlyMultiplier)
            {
                return null;
            }

            return defender.Position == Position.Goalkeeper
                ? "goalkeeper DOGSO, denying a clear scoring chance"
                : "denying a clear scoring chance as the last defender";
        }

        if (foulContext.Severity == FoulSeverity.Dangerous && minute >= EarlyRedCardProtectionMinute)
        {
            var dangerousRedChance = defender.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 0.16 : 0.11;
            return random.NextDouble() < dangerousRedChance
                ? "serious foul play after a dangerous high-risk challenge"
                : null;
        }

        return null;
    }

    private static void ApplyRedCard(
        int minute,
        MatchSimulationState simulationState,
        Team sentOffTeam,
        Team opponentTeam,
        Player player,
        PlayerMatchPerformance performance,
        MatchTeamStats sentOffTeamStats,
        MatchLogService matchLog,
        MatchEventFactory eventFactory,
        string reason)
    {
        if (reason != "second yellow" && !CanIssueRedCard(simulationState, sentOffTeam, minute))
        {
            return;
        }

        var isFirstRedCardIncident = player.RedCardMinute is null;
        if (!player.IsSentOff)
        {
            player.IsSentOff = true;
            sentOffTeamStats.RedCards++;
        }

        if (isFirstRedCardIncident && !player.NewlySuspendedThisMatch)
        {
            player.SuspendedMatches += 1;
            player.NewlySuspendedThisMatch = true;
        }

        player.IsOnPitch = false;
        player.RedCardMinute ??= minute;
        matchLog.AddEvent(eventFactory.CreateRedCard(minute, player, reason));
        simulationState.LastRedCardMinute = minute;
        performance.RedCards = Math.Max(performance.RedCards, 1);
        performance.Rating -= reason == "second yellow" ? 1.0 : 1.35;
        var redCardFormation = ApplyRedCardTacticalReaction(sentOffTeam, opponentTeam);
        if (!string.IsNullOrWhiteSpace(redCardFormation))
        {
            matchLog.AddEvent(new MatchEvent
            {
                Minute = minute,
                EventType = EventType.TacticalChange,
                HomeScore = simulationState.Match.HomeScore,
                AwayScore = simulationState.Match.AwayScore,
                Description = $"{sentOffTeam.Name} switch to {redCardFormation} after the red card."
            });
        }
    }

    private static bool CanIssueRedCard(MatchSimulationState simulationState, Team sentOffTeam, int minute)
    {
        if (simulationState.LastRedCardMinute + RedCardCooldownMinutes > minute)
        {
            return false;
        }

        var match = simulationState.Match;
        var sentOffTeamStats = GetTeamStats(match, sentOffTeam);
        var chaoticMatch = IsRedCardChaosMatch(simulationState, minute);
        var teamCap = chaoticMatch ? 2 : 1;
        var matchCap = chaoticMatch ? 3 : 2;
        var totalRedCards = match.HomeStats.RedCards + match.AwayStats.RedCards;

        return sentOffTeamStats.RedCards < teamCap && totalRedCards < matchCap;
    }

    private static bool IsRedCardChaosMatch(MatchSimulationState simulationState, int minute)
    {
        return simulationState.Match.IsRivalryMatch ||
            (IsMatchTense(simulationState, minute) &&
                simulationState.Match.WeatherCondition is WeatherCondition.HeavyRain or WeatherCondition.Storm or WeatherCondition.Snow);
    }

    private static string? ApplyRedCardTacticalReaction(Team sentOffTeam, Team opponentTeam)
    {
        sentOffTeam.Tactics.Mentality = Mentality.Defensive;
        sentOffTeam.Tactics.PressingIntensity = Math.Max(25, sentOffTeam.Tactics.PressingIntensity - 15);
        sentOffTeam.Tactics.Tempo = Math.Max(30, sentOffTeam.Tactics.Tempo - 12);
        sentOffTeam.Tactics.DefensiveLine = Math.Max(25, sentOffTeam.Tactics.DefensiveLine - 12);

        opponentTeam.Tactics.Mentality = Mentality.Attacking;
        opponentTeam.Tactics.PressingIntensity = Math.Min(90, opponentTeam.Tactics.PressingIntensity + 12);
        opponentTeam.Tactics.Tempo = Math.Min(90, opponentTeam.Tactics.Tempo + 10);

        var formationService = new InMatchFormationService();
        var formation = formationService.ChooseBestFormation(sentOffTeam, ["4-5-1", "5-4-1", "5-3-2"]);
        if (string.IsNullOrWhiteSpace(formation))
        {
            return null;
        }

        var result = formationService.ApplyFormation(sentOffTeam, formation);
        return result.Success ? result.Formation : null;
    }

    private static Player ChoosePenaltyTaker(Team team)
    {
        return GetActiveOutfieldPlayers(team)
            .Where(player => !player.IsInjured)
            .OrderByDescending(player =>
                player.Finishing +
                player.CurrentForm * 0.25 +
                (player.Traits.Contains(PlayerTrait.PenaltySpecialist) ? 22 : 0) +
                (player.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 10 : 0) +
                (player.Traits.Contains(PlayerTrait.FinesseShot) ? 6 : 0) +
                (player.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 6 : 0))
            .FirstOrDefault() ?? GetActivePitchPlayers(team).FirstOrDefault() ?? team.Players[0];
    }

    private static (Player Primary, Player Secondary) ChooseSetPieceTakers(Team team)
    {
        var activePlayers = GetActiveOutfieldPlayers(team).ToList();
        if (activePlayers.Count == 0)
        {
            var fallback = GetActivePitchPlayers(team).FirstOrDefault() ?? team.Players[0];
            return (fallback, fallback);
        }

        var takers = activePlayers
            .Where(player => !player.IsInjured)
            .OrderByDescending(player =>
                player.Passing * 1.10 +
                player.Finishing * 0.85 +
                player.CurrentForm * 0.25 +
                (player.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 16 : 0) +
                (player.Traits.Contains(PlayerTrait.LongPasser) ? 12 : 0) +
                (player.Traits.Contains(PlayerTrait.EarlyCrosser) ? 8 : 0) +
                (player.Traits.Contains(PlayerTrait.LongShotTaker) ? 8 : 0))
            .Take(2)
            .ToList();

        if (takers.Count == 0)
        {
            return (activePlayers[0], activePlayers[0]);
        }

        return (takers[0], takers.Count > 1 ? takers[1] : takers[0]);
    }

    private static bool ShouldTakeDirectFreeKick(Player taker, Random random)
    {
        var directChance =
            0.46 +
            (taker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 0.12 : 0.0) +
            (taker.Traits.Contains(PlayerTrait.FinesseShot) ? 0.06 : 0.0) +
            (taker.Traits.Contains(PlayerTrait.LongShotTaker) ? 0.05 : 0.0);

        return random.NextDouble() < Math.Clamp(directChance, 0.35, 0.72);
    }

    private static Player ChooseSetPieceTarget(Team team, Player taker, Random random)
    {
        return ChooseAerialTarget(team, taker, random, allowSamePlayerChance: 0.01);
    }

    private static Player ChooseCornerTaker(Team team, Player preferredTaker)
    {
        return GetActiveOutfieldPlayers(team)
            .Where(player => !player.IsInjured && player.Position is Position.Midfielder or Position.Forward)
            .OrderByDescending(player =>
                player.Passing * 1.15 +
                player.CurrentForm * 0.25 +
                (player.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 14 : 0) +
                (player.Traits.Contains(PlayerTrait.EarlyCrosser) ? 12 : 0) +
                (player.Traits.Contains(PlayerTrait.LongPasser) ? 8 : 0))
            .FirstOrDefault() ??
            GetActiveOutfieldPlayers(team).OrderByDescending(player => player.Passing).FirstOrDefault() ??
            preferredTaker;
    }

    private static Player ChooseCornerTarget(Team team, Player taker, Random random)
    {
        return ChooseAerialTarget(team, taker, random, allowSamePlayerChance: 0.0);
    }

    private static Player ChooseAerialTarget(Team team, Player taker, Random random, double allowSamePlayerChance)
    {
        var activePlayers = GetActiveOutfieldPlayers(team)
            .Where(player => !player.IsInjured)
            .ToList();

        if (activePlayers.Count == 0)
        {
            return taker;
        }

        if (random.NextDouble() < allowSamePlayerChance)
        {
            return taker;
        }

        var candidates = activePlayers
            .Where(player => !string.Equals(player.Name, taker.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return taker;
        }

        var weightedCandidates = candidates
            .Select(player => new
            {
                Player = player,
                Weight =
                    GetAerialTargetBaseWeight(player) +
                    player.OverallRating * 0.20 +
                    player.CurrentForm * 0.10 +
                    player.Finishing * 0.18 +
                    (player.Traits.Contains(PlayerTrait.PowerHeader) ? 30 : 0) +
                    (player.Traits.Contains(PlayerTrait.AerialThreat) ? 24 : 0) +
                    (player.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 10 : 0)
            })
            .ToList();

        var totalWeight = weightedCandidates.Sum(candidate => Math.Max(1.0, candidate.Weight));
        var roll = random.NextDouble() * totalWeight;
        double runningWeight = 0;

        foreach (var candidate in weightedCandidates)
        {
            runningWeight += Math.Max(1.0, candidate.Weight);
            if (roll <= runningWeight)
            {
                return candidate.Player;
            }
        }

        return weightedCandidates[^1].Player;
    }

    private static double GetAerialTargetBaseWeight(Player player)
    {
        if (PositionMatches(player, "CB"))
        {
            return 52;
        }

        if (PositionMatches(player, "ST"))
        {
            return 48;
        }

        if (player.Position == Position.Defender)
        {
            return 34;
        }

        if (PositionMatches(player, "CDM") || PositionMatches(player, "CM"))
        {
            return 28;
        }

        return player.Position == Position.Forward ? 26 : 12;
    }

    private static bool PositionMatches(Player player, string exactPosition)
    {
        return string.Equals(player.PreferredPosition, exactPosition, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(player.AssignedPosition, exactPosition, StringComparison.OrdinalIgnoreCase) ||
            player.SecondaryPositions.Any(position => string.Equals(position, exactPosition, StringComparison.OrdinalIgnoreCase));
    }

    private static double GetPenaltyConversionChance(Player player)
    {
        var attributes = PlayerAttributeService.GetAttributes(player);
        var penaltyBonus = player.Traits.Contains(PlayerTrait.PenaltySpecialist) ? 0.10 : 0.0;
        var traitBonus = player.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 0.08 : 0.0;
        var finesseBonus = player.Traits.Contains(PlayerTrait.FinesseShot) ? 0.04 : 0.0;
        var setPieceBonus = player.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? 0.04 : 0.0;
        return Math.Clamp(0.58 + (player.Finishing * 0.45 + attributes.Shooting * 0.55) / 600.0 + penaltyBonus + traitBonus + finesseBonus + setPieceBonus, 0.65, 0.85);
    }

    private Team ChooseAttackingTeam(
        Team homeTeam,
        Team awayTeam,
        double homeAttackStrength,
        double awayAttackStrength,
        double homeDefenseStrength,
        double awayDefenseStrength,
        Random random)
    {
        var homePressure = homeAttackStrength / Math.Max(1.0, awayDefenseStrength) * _tacticalImpactCalculator.GetAttackFlowModifier(homeTeam);
        var awayPressure = awayAttackStrength / Math.Max(1.0, homeDefenseStrength) * _tacticalImpactCalculator.GetAttackFlowModifier(awayTeam);
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

    private static double GetOpenPlayTurnoverRiskCap(Team attackingTeam, Team defendingTeam)
    {
        var playerDeficit = GetActivePitchPlayers(defendingTeam).Count() - GetActivePitchPlayers(attackingTeam).Count();
        var riskCap = 0.34;
        if (attackingTeam.Tactics.Mentality == Mentality.AllOutAttack)
        {
            riskCap += 0.08;
        }

        if (playerDeficit > 0)
        {
            riskCap += Math.Min(0.16, playerDeficit * 0.10);
        }

        return Math.Clamp(riskCap, 0.34, 0.56);
    }

    private Player ChoosePlaymaker(Team team, Random random)
    {
        var candidates = GetAvailablePlayers(team)
            .Where(player =>
                IsOutfieldPlayer(player) &&
                (player.Position is Position.Midfielder or Position.Forward ||
                    player.Traits.Contains(PlayerTrait.LongPasser) ||
                    player.Traits.Contains(PlayerTrait.EarlyCrosser) ||
                    player.Traits.Contains(PlayerTrait.LongThrower) ||
                    player.Traits.Contains(PlayerTrait.Playmaker)))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = GetAvailablePlayers(team).Where(IsOutfieldPlayer).ToList();
        }

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
            .Where(player =>
                IsOutfieldPlayer(player) &&
                (player.Position is Position.Forward or Position.Midfielder ||
                    player.Traits.Contains(PlayerTrait.PowerHeader) ||
                    player.Traits.Contains(PlayerTrait.AerialThreat) ||
                    (player.Position == Position.Defender && random.NextDouble() < 0.18)))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = GetAvailablePlayers(team).Where(IsOutfieldPlayer).ToList();
        }

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

        var totalWeight = candidates.Sum(GetDefendingPlayerWeight);
        var roll = random.NextDouble() * totalWeight;
        double runningWeight = 0;

        foreach (var candidate in candidates)
        {
            runningWeight += GetDefendingPlayerWeight(candidate);
            if (roll <= runningWeight)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private double GetDefendingPlayerWeight(Player player)
    {
        return _teamStrengthCalculator.GetEffectiveDefense(player) * 1.24 +
            _teamStrengthCalculator.GetEffectivePassing(player) * 0.16 +
            (player.Traits.Contains(PlayerTrait.Interceptor) ? 18 : 0) +
            (player.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 14 : 0) +
            (player.Traits.Contains(PlayerTrait.BoxToBox) ? 7 : 0);
    }

    private bool IsGoal(double goalProbability, Random random)
    {
        return random.NextDouble() < goalProbability;
    }

    private double CalculateGoalProbability(
        Player attacker,
        Match match,
        Team defendingTeam,
        string chanceType,
        double attackingTeamStrength,
        double defendingTeamStrength,
        Random random,
        bool hasSuperSubBoost,
        int scorerMatchGoals,
        double scorerCooldownPenalty)
    {
        var boost = hasSuperSubBoost ? 1.05 : 1.0;
        var attributes = PlayerAttributeService.GetAttributes(attacker);
        var (baseProbability, minimumProbability, maximumProbability) = GetChanceGoalProbabilityBand(chanceType);
        var chanceScore =
            (_teamStrengthCalculator.GetEffectiveFinishing(attacker) - 72) * 0.0014 * boost +
            (_teamStrengthCalculator.GetEffectiveAttack(attacker) - 72) * 0.0007 * boost +
            (attributes.Shooting - 72) * 0.00055 * boost +
            (chanceType == "dribble run" ? (attributes.Dribbling - 72) * 0.00035 : 0.0) +
            (chanceType == "through ball attempt" ? (attributes.Pace - 72) * 0.00030 : 0.0) +
            (chanceType.Contains("cross", StringComparison.OrdinalIgnoreCase) ? (attributes.Physical - 72) * 0.00025 : 0.0) +
            (attackingTeamStrength - defendingTeamStrength) / 1050.0;

        var traitBonus =
            (attacker.Traits.Contains(PlayerTrait.FinesseShot) && (chanceType is "long-range attempt" or "quick combination") ? 0.018 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.LongShotTaker) && chanceType == "long-range attempt" ? 0.020 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.PowerHeader) && chanceType.Contains("cross", StringComparison.OrdinalIgnoreCase) ? 0.028 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.AerialThreat) && chanceType.Contains("cross", StringComparison.OrdinalIgnoreCase) ? 0.018 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 0.024 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.OutsideFootShot) ? 0.012 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.SpeedDribbler) && chanceType == "dribble run" ? 0.014 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.Rapid) && chanceType == "dribble run" ? 0.010 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.TechnicalDribbler) && chanceType == "dribble run" ? 0.012 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.Flair) && chanceType is "dribble run" or "quick combination" ? 0.010 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.TriesToBeatOffsideTrap) && chanceType == "through ball attempt" ? 0.010 : 0.0) +
            (attacker.Traits.Contains(PlayerTrait.PressResistant) ? 0.004 : 0.0);
        var diminishingReturnPenalty = scorerMatchGoals switch
        {
            0 => 0.0,
            1 => 0.035,
            2 => 0.060,
            _ => 0.085
        };
        var defensiveResistance = GetDefensiveGoalResistance(defendingTeam, chanceType);
        var attackingTeam = HomeAwayAdvantageService.IsHomeTeam(match, defendingTeam)
            ? match.AwayTeam
            : match.HomeTeam;
        var venueFinishingModifier = HomeAwayAdvantageService.GetModifier(match, attackingTeam).FinishingModifier;

        return Math.Clamp(
            (baseProbability + chanceScore + traitBonus - defensiveResistance - diminishingReturnPenalty - scorerCooldownPenalty + GetProbabilitySwing(random, 0.025)) * venueFinishingModifier,
            minimumProbability,
            maximumProbability);
    }

    private static (double Base, double Minimum, double Maximum) GetChanceGoalProbabilityBand(string chanceType)
    {
        return chanceType switch
        {
            "long-range attempt" => (0.045, 0.025, 0.080),
            "through ball attempt" => (0.300, 0.180, 0.500),
            "dribble run" => (0.240, 0.140, 0.420),
            "cross into box" => (0.100, 0.050, 0.180),
            "quick combination" => (0.110, 0.060, 0.180),
            "one-on-one" => (0.340, 0.220, 0.500),
            "rebound shot" => (0.240, 0.140, 0.380),
            _ => (0.100, MatchConstants.MinimumGoalProbability, 0.180)
        };
    }

    private double GetDefensiveGoalResistance(Team defendingTeam, string chanceType)
    {
        var goalkeeper = GetAvailablePlayers(defendingTeam)
            .FirstOrDefault(PositionSuitabilityService.IsGoalkeeperCapable);
        var defensivePlayers = GetAvailablePlayers(defendingTeam)
            .Where(player => player.Position is Position.Defender or Position.Midfielder)
            .OrderByDescending(_teamStrengthCalculator.GetEffectiveDefense)
            .Take(5)
            .ToList();
        var goalkeeperResistance = goalkeeper is null
            ? 0.0
            : Math.Max(0, _teamStrengthCalculator.GetEffectiveDefense(goalkeeper) - 74) / 650.0;
        var defensiveShapeResistance = defensivePlayers.Count == 0
            ? 0.0
            : Math.Max(0, defensivePlayers.Average(_teamStrengthCalculator.GetEffectiveDefense) - 72) / 900.0;
        var lowBlockResistance = defendingTeam.Tactics.DefensiveLine <= 40 ? 0.015 : 0.0;
        var compactMentalityResistance = defendingTeam.Tactics.Mentality is Mentality.Defensive ? 0.014 : 0.0;
        var pressureResistance = Math.Max(0, defendingTeam.Tactics.PressingIntensity - 62) / 2600.0;
        var chanceResistanceModifier = chanceType switch
        {
            "long-range attempt" => 1.20,
            "cross into box" => 1.05,
            "through ball attempt" => 0.72,
            "one-on-one" => 0.60,
            _ => 1.0
        };

        return Math.Clamp(
            (goalkeeperResistance + defensiveShapeResistance + lowBlockResistance + compactMentalityResistance + pressureResistance) * chanceResistanceModifier,
            0.0,
            0.12);
    }

    private bool IsSaved(Team defendingTeam, Player shooter, string chanceType, Random random)
    {
        var goalkeeper = GetAvailablePlayers(defendingTeam)
            .FirstOrDefault(PositionSuitabilityService.IsGoalkeeperCapable);

        if (goalkeeper is null)
        {
            return false;
        }

        var traitBonus =
            (goalkeeper.Traits.Contains(PlayerTrait.OneOnOnes) && (chanceType is "through ball attempt" or "dribble run") ? 0.12 : 0.0) +
            (goalkeeper.Traits.Contains(PlayerTrait.RushesOutOfGoal) && chanceType == "through ball attempt" ? 0.055 : 0.0) +
            (goalkeeper.Traits.Contains(PlayerTrait.Puncher) && chanceType.Contains("cross", StringComparison.OrdinalIgnoreCase) ? 0.06 : 0.0);
        var shooterPenalty = shooter.Traits.Contains(PlayerTrait.FinesseShot) || shooter.Traits.Contains(PlayerTrait.OutsideFootShot)
            ? 0.025
            : 0.0;
        var saveProbability = Math.Clamp(_teamStrengthCalculator.GetEffectiveDefense(goalkeeper) / 100.0 + traitBonus - shooterPenalty, 0.20, 0.88);
        return random.NextDouble() < saveProbability;
    }

    private static double GetProbabilitySwing(Random random, double range)
    {
        return (random.NextDouble() * range) - (range / 2.0);
    }

    private void ResetTeamStamina(Team team, bool preserveMatchStartStamina)
    {
        var starters = team.Players.ToHashSet();

        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            player.Stamina = preserveMatchStartStamina ? Math.Clamp(player.Stamina, 0, 100) : 100;
            player.LiveMatchModifier = 1.0;
            player.YellowCards = 0;
            player.IsSentOff = false;
            player.RedCardMinute = null;
            player.IsOnPitch = starters.Contains(player) && !player.IsSuspended && !player.IsInjured;
        }
    }

    private void ApplyMinuteFatigue(Team team, Match match, MatchSimulationOptions options)
    {
        if (options.EnableDynamicFatigue)
        {
            _fatigueService.ApplyMinuteFatigue(team, match);
            ApplyVenueFatiguePressure(team, match);
            ApplySecondWindIfNeeded(team, match);
            ApplyExtraTimeFatiguePressure(team, match, options);
            return;
        }

        var tacticalLoad = (team.Tactics.PressingIntensity + team.Tactics.Tempo + team.Tactics.DefensiveLine) / 3.0;
        var loadModifier = 1.0 + (Math.Max(0.0, tacticalLoad - 50.0) / 100.0);
        loadModifier *= match.WeatherCondition switch
        {
            WeatherCondition.HeavyRain => 1.10,
            WeatherCondition.Storm => 1.08,
            WeatherCondition.Snow => 1.14,
            WeatherCondition.Rainy => 1.04,
            WeatherCondition.Hot => 1.12,
            WeatherCondition.Cold => 0.98,
            _ => 1.0
        };
        loadModifier *= HomeAwayAdvantageService.GetModifier(match, team).FatigueLossModifier;
        var staminaLoss = MatchConstants.StaminaLossPerMinute * loadModifier;

        foreach (var player in GetActivePitchPlayers(team))
        {
            player.Stamina = Math.Max(0.0, player.Stamina - staminaLoss * GetPositionFatiguePressureMultiplier(player));
        }

        ApplyVenueFatiguePressure(team, match);
        ApplySecondWindIfNeeded(team, match);
        ApplyExtraTimeFatiguePressure(team, match, options);
    }

    private static void ApplyExtraTimeFatiguePressure(Team team, Match match, MatchSimulationOptions options)
    {
        if (!options.IsExtraTimeSegment && match.CurrentMinute <= MatchConstants.DefaultMatchDurationMinutes)
        {
            return;
        }

        foreach (var player in GetActivePitchPlayers(team))
        {
            var lowStaminaPressure = player.Stamina < 50 ? 0.16 : player.Stamina < 65 ? 0.10 : 0.06;
            player.Stamina = Math.Max(0.0, player.Stamina - lowStaminaPressure * GetPositionFatiguePressureMultiplier(player));
            player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier - 0.0015, 0.70, 1.15);
        }
    }

    private static void ApplyVenueFatiguePressure(Team team, Match match)
    {
        var modifier = HomeAwayAdvantageService.GetModifier(match, team).FatigueLossModifier;
        if (modifier <= 1.0)
        {
            return;
        }

        var extraLoss = MatchConstants.StaminaLossPerMinute * (modifier - 1.0) * 0.35;
        foreach (var player in GetActivePitchPlayers(team))
        {
            player.Stamina = Math.Max(0.0, player.Stamina - extraLoss * GetPositionFatiguePressureMultiplier(player));
        }
    }

    private static double GetPositionFatiguePressureMultiplier(Player player)
    {
        return player.Position == Position.Goalkeeper ? 0.25 : 1.0;
    }

    private void ApplySecondWindIfNeeded(Team team, Match match)
    {
        if (match.CurrentMinute < 75 || !IsTeamLosing(match, team))
        {
            return;
        }

        foreach (var player in GetActivePitchPlayers(team)
                     .Where(player => player.Traits.Contains(PlayerTrait.Engine) && !player.IsInjured))
        {
            var key = $"{match.HomeTeam.Name}|{match.AwayTeam.Name}|{team.Name}|{player.Name}";
            if (!_secondWindAppliedPlayerKeys.Add(key))
            {
                continue;
            }

            player.Stamina = Math.Clamp(player.Stamina + 25, 0, 100);
            player.LiveMatchModifier = Math.Clamp(player.LiveMatchModifier + 0.04, 0.75, 1.15);
        }
    }

    private static bool IsTeamLosing(Match match, Team team)
    {
        return team == match.HomeTeam
            ? match.HomeScore < match.AwayScore
            : match.AwayScore < match.HomeScore;
    }

    private static bool IsTeamWinning(Match match, Team team)
    {
        return team == match.HomeTeam
            ? match.HomeScore > match.AwayScore
            : match.AwayScore > match.HomeScore;
    }

    private static bool IsRivalry(string homeTeamName, string awayTeamName)
    {
        var key = NormalizeRivalryPair(homeTeamName, awayTeamName);
        return key is "arsenal|chelsea"
            or "arsenal|tottenham"
            or "chelsea|tottenham"
            or "everton|liverpool"
            or "liverpool|manchester united"
            or "manchester city|manchester united"
            or "newcastle|sunderland"
            or "aston villa|wolves"
            or "leeds|manchester united";
    }

    private static string NormalizeRivalryPair(string firstTeamName, string secondTeamName)
    {
        var teams = new[] { NormalizeTeamName(firstTeamName), NormalizeTeamName(secondTeamName) };
        Array.Sort(teams, StringComparer.Ordinal);
        return $"{teams[0]}|{teams[1]}";
    }

    private static string NormalizeTeamName(string teamName)
    {
        return teamName.Trim().ToLowerInvariant()
            .Replace("fc", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("afc", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private IEnumerable<Player> GetAvailablePlayers(Team team)
    {
        return _teamStrengthCalculator.GetAvailablePlayers(team);
    }

    private static IEnumerable<Player> GetActivePitchPlayers(Team team)
    {
        return team.Players.Where(player => player.IsOnPitch && !player.IsSentOff && !player.IsInjured && !player.IsSuspended);
    }

    private static IEnumerable<Player> GetActiveOutfieldPlayers(Team team)
    {
        return GetActivePitchPlayers(team).Where(IsOutfieldPlayer);
    }

    private static bool IsOutfieldPlayer(Player player)
    {
        return !PositionSuitabilityService.IsGoalkeeperCapable(player);
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
        match.HomeStats.PassAccuracyPercentage = EstimatePassAccuracy(match, match.HomeTeam);
        match.AwayStats.PassAccuracyPercentage = EstimatePassAccuracy(match, match.AwayTeam);
    }

    private static int EstimatePasses(Team team, double possessionPercentage)
    {
        var tempoModifier = 0.80 + team.Tactics.Tempo / 160.0;
        return (int)Math.Round((170 + possessionPercentage * 4.2) * tempoModifier);
    }

    private static double EstimatePassAccuracy(Match match, Team team)
    {
        var averagePassing = team.Players.Average(player => player.Passing);
        var tempoPenalty = Math.Max(0, team.Tactics.Tempo - 65) * 0.08;
        var venueAdjustment = (HomeAwayAdvantageService.GetModifier(match, team).PassingModifier - 1.0) * 100.0;
        return Math.Round(Math.Clamp(65 + averagePassing * 0.24 - tempoPenalty + venueAdjustment, 58, 94), 1);
    }

    private static int GetLastCrowdMomentumMinute(Match match)
    {
        return match.Events
            .Where(matchEvent => matchEvent.EventType == EventType.CrowdMomentum)
            .Select(matchEvent => matchEvent.Minute)
            .DefaultIfEmpty(-100)
            .Max();
    }

    private static Dictionary<string, int> GetCrowdMomentumCountsByTeam(Match match)
    {
        return match.Events
            .Where(matchEvent => matchEvent.EventType == EventType.CrowdMomentum)
            .Select(matchEvent => FindEventTeamName(matchEvent, match))
            .Where(teamName => !string.IsNullOrWhiteSpace(teamName))
            .GroupBy(teamName => teamName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private enum AttackFlowOutcome
    {
        BuildUp,
        CreateChance,
        LosePossession,
        ForcedReset
    }

    private enum AttackType
    {
        OpenPlay,
        Corner,
        DirectFreeKick,
        IndirectFreeKick
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

    private enum FoulSeverity
    {
        Minor,
        Tactical,
        Reckless,
        Dangerous,
        Dogso,
        Violent
    }

    private sealed record FoulContext(
        FoulSeverity Severity,
        bool IsPenalty,
        string PenaltyReason,
        bool DeniesClearChance,
        bool ViolentFoul,
        bool CreatesDangerousSetPiece,
        FoulLocation Location);

    private sealed record AttackSequence(
        Player CreatorPlayer,
        Player ShooterPlayer,
        Player? SetPieceTaker,
        bool IsSoloMove,
        AttackType AttackType)
    {
        public string CreatorPlayerId => CreatorPlayer.Name;
        public string ShooterPlayerId => ShooterPlayer.Name;
        public string SetPieceTakerId => SetPieceTaker?.Name ?? string.Empty;
    }

    private sealed record GoalVarReviewReason(
        string CheckReason,
        string DisallowedOutcome,
        double DisallowChance);

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
        public int TensionUntilMinute { get; set; }
        public int LastConfrontationMinute { get; set; } = -100;
        public int ConfrontationCount { get; set; }
        public int LastRedCardMinute { get; set; } = match.Events
            .Where(matchEvent => matchEvent.EventType == EventType.RedCard)
            .Select(matchEvent => matchEvent.Minute)
            .DefaultIfEmpty(-100)
            .Max();
        public Dictionary<string, int> TeamDefensiveErrorCooldownUntil { get; } = [];
        public Dictionary<string, int> PlayerDefensiveErrorCooldownUntil { get; } = [];
        public Dictionary<string, int> TeamDefensiveErrorCounts { get; } = [];
        public Dictionary<string, int> TeamGoalkeeperErrorCounts { get; } = [];
        public Dictionary<string, int> LastGoalMinuteByPlayer { get; } = [];
        public HashSet<int> HomeCrowdPressureMinutes { get; } = [];
        public int LastTimeWastingMinute { get; set; } = match.Events
            .Where(matchEvent => matchEvent.EventType == EventType.TimeWasting)
            .Select(matchEvent => matchEvent.Minute)
            .DefaultIfEmpty(-100)
            .Max();
        public int LastCrowdMomentumMinute { get; set; } = GetLastCrowdMomentumMinute(match);
        public int TotalCrowdMomentumCount { get; set; } = match.Events.Count(matchEvent => matchEvent.EventType == EventType.CrowdMomentum);
        public Dictionary<string, int> CrowdMomentumCountByTeam { get; } = GetCrowdMomentumCountsByTeam(match);
    }
}
