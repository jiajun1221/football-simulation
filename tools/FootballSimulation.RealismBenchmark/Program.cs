using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;

var matchCount = args.Length > 0 && int.TryParse(args[0], out var requestedCount)
    ? requestedCount
    : 500;

if (matchCount < 500)
{
    throw new ArgumentOutOfRangeException(nameof(args), "The realism benchmark requires at least 500 matches.");
}

var aggregate = new BenchmarkAggregate();
for (var seed = 1; seed <= matchCount; seed++)
{
    var (homeTeam, awayTeam) = new SeedDataService().CreateDemoTeams();
    var match = new MatchEngine().SimulateMatch(homeTeam, awayTeam, seed);
    aggregate.Add(match);
}

aggregate.Print(matchCount);

internal sealed class BenchmarkAggregate
{
    private int _goals;
    private int _shots;
    private int _shotsOnTarget;
    private int _woodwork;
    private int _homeWins;
    private int _draws;
    private int _awayWins;
    private int _firstHalfGoals;
    private int _secondHalfGoals;
    private int _lateFirstHalfGoals;
    private int _lateSecondHalfGoals;
    private double _expectedGoals;
    private double _passAccuracy;
    private double _passLength;
    private double _passSpeed;
    private double _shotDistance;
    private double _shotAngle;
    private double _shotSpeed;
    private int _setPieceGoals;
    private readonly List<double> _shotExpectedGoals = [];

    public void Add(Match match)
    {
        var goals = match.HomeScore + match.AwayScore;
        _goals += goals;
        _shots += match.HomeStats.TotalShots + match.AwayStats.TotalShots;
        _shotsOnTarget += match.HomeStats.ShotsOnTarget + match.AwayStats.ShotsOnTarget;
        _expectedGoals += match.HomeStats.ExpectedGoals + match.AwayStats.ExpectedGoals;
        _passAccuracy += match.HomeStats.PassAccuracyPercentage + match.AwayStats.PassAccuracyPercentage;
        _passLength += match.HomeStats.AveragePassLengthMeters + match.AwayStats.AveragePassLengthMeters;
        _passSpeed += match.HomeStats.AveragePassSpeedMetersPerSecond + match.AwayStats.AveragePassSpeedMetersPerSecond;
        _shotDistance += match.HomeStats.AverageShotDistanceMeters + match.AwayStats.AverageShotDistanceMeters;
        _shotAngle += match.HomeStats.AverageShotAngleDegrees + match.AwayStats.AverageShotAngleDegrees;
        _shotSpeed += match.HomeStats.AverageShotSpeedMetersPerSecond + match.AwayStats.AverageShotSpeedMetersPerSecond;
        _woodwork += match.Events.Count(matchEvent => matchEvent.EventType == EventType.Woodwork);

        if (match.HomeScore > match.AwayScore) _homeWins++;
        else if (match.HomeScore == match.AwayScore) _draws++;
        else _awayWins++;

        foreach (var goal in match.Events.Where(IsCountedGoal))
        {
            if (goal.Minute <= 45 + match.FirstHalfAddedMinutes)
            {
                _firstHalfGoals++;
                if (goal.Minute >= 31) _lateFirstHalfGoals++;
            }
            else
            {
                _secondHalfGoals++;
                if (goal.Minute >= 76 + match.FirstHalfAddedMinutes) _lateSecondHalfGoals++;
            }
        }

        for (var index = 0; index < match.Events.Count; index++)
        {
            var matchEvent = match.Events[index];
            if (matchEvent.EventType == EventType.Shot && matchEvent.ExpectedGoals.HasValue)
            {
                _shotExpectedGoals.Add(matchEvent.ExpectedGoals.Value);
            }

            if (IsCountedGoal(matchEvent) && IsSetPieceSequence(match.Events, index))
            {
                _setPieceGoals++;
            }
        }
    }

    public void Print(int matchCount)
    {
        PrintMetric("matches", matchCount);
        PrintMetric("goals_per_match", Divide(_goals, matchCount));
        PrintMetric("shots_per_match", Divide(_shots, matchCount));
        PrintMetric("shots_on_target_per_match", Divide(_shotsOnTarget, matchCount));
        PrintMetric("shot_conversion_percent", Percent(_goals, _shots));
        PrintMetric("xg_per_shot", Divide(_expectedGoals, _shots));
        PrintMetric("xg_per_shot_p50", Percentile(_shotExpectedGoals, 0.50));
        PrintMetric("xg_per_shot_p90", Percentile(_shotExpectedGoals, 0.90));
        PrintMetric("pass_completion_percent", _passAccuracy / (matchCount * 2.0));
        PrintMetric("average_pass_length_m", Divide(_passLength, matchCount * 2.0));
        PrintMetric("average_pass_speed_mps", Divide(_passSpeed, matchCount * 2.0));
        PrintMetric("average_shot_distance_m", Divide(_shotDistance, matchCount * 2.0));
        PrintMetric("average_shot_angle_degrees", Divide(_shotAngle, matchCount * 2.0));
        PrintMetric("average_shot_speed_mps", Divide(_shotSpeed, matchCount * 2.0));
        PrintMetric("woodwork_per_match", Divide(_woodwork, matchCount));
        PrintMetric("set_piece_goal_percent_proxy", Percent(_setPieceGoals, _goals));
        PrintMetric("home_win_percent", Percent(_homeWins, matchCount));
        PrintMetric("draw_percent", Percent(_draws, matchCount));
        PrintMetric("away_win_percent", Percent(_awayWins, matchCount));
        PrintMetric("first_half_goal_percent", Percent(_firstHalfGoals, _firstHalfGoals + _secondHalfGoals));
        PrintMetric("late_half_goal_percent", Percent(_lateFirstHalfGoals + _lateSecondHalfGoals, _firstHalfGoals + _secondHalfGoals));
    }

    private static bool IsCountedGoal(MatchEvent matchEvent) =>
        matchEvent.EventType is EventType.Goal or EventType.WonderGoal ||
        matchEvent.EventType == EventType.Penalty && matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase);

    private static bool IsSetPieceSequence(IReadOnlyList<MatchEvent> events, int goalIndex)
    {
        var goal = events[goalIndex];
        if (goal.EventType == EventType.Penalty)
        {
            return true;
        }

        return events
            .Take(goalIndex)
            .LastOrDefault(matchEvent => matchEvent.Minute == goal.Minute && matchEvent.EventType == EventType.Shot)
            ?.IsSetPieceShot == true;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var index = (int)Math.Round((values.Count - 1) * percentile);
        return Math.Round(values[index], 3);
    }

    private static double Divide(double numerator, double denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / denominator, 3);

    private static double Percent(double numerator, double denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator * 100.0 / denominator, 2);

    private static void PrintMetric(string name, object value) => Console.WriteLine($"{name}={value}");
}
