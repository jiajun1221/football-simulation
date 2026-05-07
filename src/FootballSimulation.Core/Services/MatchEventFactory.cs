using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class MatchEventFactory
{
    public MatchEvent CreateKickoff(int minute, Team homeTeam)
    {
        return CreateEvent(minute, EventType.Kickoff, $"{homeTeam.Name} kick off the match.");
    }

    public MatchEvent CreateAttack(int minute, Team attackingTeam, Player playmaker, Player shooter)
    {
        var safeShooter = !string.Equals(playmaker.Name, shooter.Name, StringComparison.OrdinalIgnoreCase)
            ? shooter
            : attackingTeam.Players.FirstOrDefault(candidate =>
                !string.Equals(candidate.Name, playmaker.Name, StringComparison.OrdinalIgnoreCase));

        var description = safeShooter is null
            ? $"{attackingTeam.Name} build an attack through {GetDisplayName(playmaker.Name)}."
            : $"{attackingTeam.Name} build through {GetDisplayName(playmaker.Name)} and look for {GetDisplayName(safeShooter.Name)}.";

        return CreateEvent(minute, EventType.Attack, description, playmaker.Name, safeShooter?.Name);
    }

    public MatchEvent CreateAttackBuildUp(int minute, Team attackingTeam, Player playmaker, Player target, Random random)
    {
        var safeTarget = ResolveDistinctTeammate(attackingTeam, playmaker, target, random);
        var playmakerName = GetDisplayName(playmaker.Name);
        var description = safeTarget is null
            ? Pick(random,
                $"{attackingTeam.Name} build quickly and {playmakerName} finds space on the wing.",
                $"{attackingTeam.Name} move the ball with intent through {playmakerName}.",
                $"{attackingTeam.Name} progress from midfield with {playmakerName} in control.")
            : Pick(random,
                $"{attackingTeam.Name} build quickly as {playmakerName} picks out {GetDisplayName(safeTarget.Name)}.",
                $"{attackingTeam.Name} keep it moving and {playmakerName} links with {GetDisplayName(safeTarget.Name)}.",
                $"{attackingTeam.Name} combine neatly, with {playmakerName} releasing {GetDisplayName(safeTarget.Name)}.");

        return CreateEvent(minute, EventType.Attack, description, playmaker.Name, safeTarget?.Name);
    }

    public MatchEvent CreateAttackReset(int minute, Team attackingTeam, Team defendingTeam, Player defender, Random random)
    {
        var description = Pick(random,
            $"{defendingTeam.Name} stay compact and force {attackingTeam.Name} to reset through {defender.Name}.",
            $"{attackingTeam.Name} are pushed backward by disciplined pressure from {defendingTeam.Name}.",
            $"{defendingTeam.Name} squeeze space and {attackingTeam.Name} recycle possession under pressure.",
            $"{defendingTeam.Name} close lanes quickly and {attackingTeam.Name} must start over.");

        return CreateEvent(minute, EventType.DefensiveStop, description, defender.Name);
    }

    public MatchEvent CreateDefensiveStop(int minute, Team defendingTeam, Player defender, Player attacker, Random random)
    {
        var description = Pick(random,
            $"Brilliant tackle by {defender.Name} for {defendingTeam.Name} on {attacker.Name}. D+1.",
            $"Interception cuts out the attack as {defender.Name} steps in for {defendingTeam.Name}. D+1.",
            $"Strong clearance under pressure by {defender.Name} for {defendingTeam.Name}. D+1.");

        return CreateEvent(minute, EventType.DefensiveStop, description, defender.Name, attacker.Name);
    }

    public MatchEvent CreateAttackMistake(int minute, Team attackingTeam, Team defendingTeam, Player player, Random random)
    {
        var description = Pick(random,
            $"Bad pass from {player.Name} for {attackingTeam.Name} and {defendingTeam.Name} win it back.",
            $"{player.Name} miscontrols for {attackingTeam.Name}; possession turns over to {defendingTeam.Name}.",
            $"{attackingTeam.Name} show poor positioning and {player.Name} is dispossessed by {defendingTeam.Name}.");

        return CreateEvent(minute, EventType.Turnover, description, player.Name);
    }

    public MatchEvent CreateFoul(int minute, Team defendingTeam, Player defender, Player attacker)
    {
        return CreateEvent(minute, EventType.Foul, $"{defender.Name} fouls {attacker.Name} for {defendingTeam.Name}.", defender.Name, attacker.Name);
    }

    public MatchEvent CreateShot(int minute, Team attackingTeam, Player attacker, Player? playmaker = null)
    {
        var description = playmaker is not null && playmaker != attacker
            ? $"{attacker.Name} takes a shot for {attackingTeam.Name} after a pass from {playmaker.Name}."
            : $"{attacker.Name} takes a shot for {attackingTeam.Name}.";

        return CreateEvent(minute, EventType.Shot, description, attacker.Name, playmaker?.Name);
    }

    public MatchEvent CreateShot(int minute, Team attackingTeam, Player attacker, Player? playmaker, string chanceType, Random random)
    {
        var safePlaymaker = ResolveDistinctTeammate(attackingTeam, attacker, playmaker, random);
        var attackerName = GetDisplayName(attacker.Name);
        var playmakerName = safePlaymaker is null ? string.Empty : GetDisplayName(safePlaymaker.Name);

        string description = chanceType switch
        {
            "long-range attempt" => Pick(random,
                $"{attackerName} tries a long-range effort for {attackingTeam.Name}.",
                $"{attackerName} lets fly from distance for {attackingTeam.Name}."),
            "cross into box" => Pick(random,
                $"{attackingTeam.Name} whip a cross in and {attackerName} attacks it.",
                $"{attackerName} meets the delivery for {attackingTeam.Name} inside the area."),
            "through ball attempt" => safePlaymaker is not null
                ? $"{playmakerName} slips a through ball and {attackerName} gets the shot away for {attackingTeam.Name}."
                : $"{attackingTeam.Name} find a lane through the middle and {attackerName} pulls the trigger.",
            "dribble run" => Pick(random,
                $"{attackerName} beats a marker and shoots for {attackingTeam.Name}.",
                $"{attackerName} dribbles into space and strikes for {attackingTeam.Name}."),
            _ => safePlaymaker is not null
                ? $"{attackerName} shoots for {attackingTeam.Name} after a pass from {playmakerName}."
                : $"{attackerName} takes a shot for {attackingTeam.Name}."
        };

        return CreateEvent(minute, EventType.Shot, description, attacker.Name, safePlaymaker?.Name);
    }

    public MatchEvent CreateGoal(int minute, Team attackingTeam, Player scorer, Match match, Player? assister = null)
    {
        var assistText = assister is not null && assister != scorer
            ? $" Assisted by {assister.Name}."
            : string.Empty;

        return CreateEvent(
            minute,
            EventType.Goal,
            $"{scorer.Name} scores for {attackingTeam.Name}.{assistText} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            scorer.Name,
            assister?.Name,
            match);
    }

    public MatchEvent CreateGoal(
        int minute,
        Team attackingTeam,
        Player scorer,
        Match match,
        string goalTypeDescription,
        Random random,
        Player? assister = null)
    {
        var safeAssister = ResolveDistinctTeammate(attackingTeam, scorer, assister, random);
        var scorerName = GetDisplayName(scorer.Name);
        var baseLine = safeAssister is not null
            ? Pick(random,
                $"GOAL! {scorerName} finishes with a {goalTypeDescription} for {attackingTeam.Name}, set up by {GetDisplayName(safeAssister.Name)}.",
                $"GOAL! {scorerName} scores for {attackingTeam.Name} with a {goalTypeDescription}. Assist from {GetDisplayName(safeAssister.Name)}.")
            : Pick(random,
                $"GOAL! {scorerName} scores a {goalTypeDescription} for {attackingTeam.Name}.",
                $"GOAL! {scorerName} finds the net for {attackingTeam.Name} with a {goalTypeDescription}.");

        var description = $"{baseLine} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}";
        return CreateEvent(minute, EventType.Goal, description, scorer.Name, safeAssister?.Name, match);
    }

    public MatchEvent CreateOwnGoal(int minute, Team benefitingTeam, Team concedingTeam, Player ownGoalPlayer, Match match)
    {
        var description =
            $"GOAL! Own goal from {ownGoalPlayer.Name} for {benefitingTeam.Name}, a nightmare moment for {concedingTeam.Name}. " +
            $"Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}";

        return CreateEvent(minute, EventType.Goal, description, ownGoalPlayer.Name, match: match);
    }

    public MatchEvent CreateMiss(int minute, Team attackingTeam, Player attacker)
    {
        return CreateEvent(minute, EventType.Miss, $"{attacker.Name} misses the target for {attackingTeam.Name}.", attacker.Name);
    }

    public MatchEvent CreateMiss(int minute, Team attackingTeam, Player attacker, string shotStyle, Random random)
    {
        var description = Pick(random,
            $"{attacker.Name} goes for a {shotStyle} for {attackingTeam.Name}, but drags it wide.",
            $"{attacker.Name} attempts a {shotStyle} for {attackingTeam.Name} and misses the target.");

        return CreateEvent(minute, EventType.Miss, description, attacker.Name);
    }

    public MatchEvent CreateSave(int minute, Team defendingTeam, Player attacker)
    {
        return CreateEvent(minute, EventType.Save, $"{attacker.Name}'s shot is saved by {defendingTeam.Name}.", attacker.Name);
    }

    public MatchEvent CreateSave(int minute, Team defendingTeam, Player attacker, Player goalkeeper, string saveType, Random random)
    {
        var description = Pick(random,
            $"{goalkeeper.Name} makes a {saveType} to deny {attacker.Name} for {defendingTeam.Name}.",
            $"{attacker.Name} is stopped by a {saveType} from {goalkeeper.Name} of {defendingTeam.Name}.");

        return CreateEvent(minute, EventType.Save, description, attacker.Name, goalkeeper.Name);
    }

    public MatchEvent CreateYellowCard(int minute, Player player)
    {
        return CreateEvent(minute, EventType.YellowCard, $"Yellow card for {player.Name}.", player.Name);
    }

    public MatchEvent CreateRedCard(int minute, Player player)
    {
        return CreateEvent(minute, EventType.RedCard, $"Red card for {player.Name}. {player.Name} is sent off.", player.Name);
    }

    public MatchEvent CreateRedCard(int minute, Player player, string reason)
    {
        return CreateEvent(
            minute,
            EventType.RedCard,
            $"Red card for {player.Name}: {reason}. {player.Name} is sent off.",
            player.Name);
    }

    public MatchEvent CreateSubstitution(int minute, Team team, Player playerOff, Player playerOn)
    {
        return CreateEvent(
            minute,
            EventType.Substitution,
            $"{playerOn.Name} replaces {playerOff.Name} for {team.Name}.",
            playerOn.Name,
            playerOff.Name);
    }

    public MatchEvent CreateInjury(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Injury, $"{player.Name} goes down injured for {team.Name}.", player.Name);
    }

    public MatchEvent CreatePenalty(int minute, Team team, Player player, bool converted, Match match)
    {
        var outcome = converted
            ? $"{player.Name} scores from the penalty spot for {team.Name}."
            : $"{player.Name}'s penalty for {team.Name} is saved.";

        return CreateEvent(minute, EventType.Penalty, $"{outcome} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}", player.Name, match: match);
    }

    public MatchEvent CreatePenaltyDecision(int minute, Team defendingTeam, Player defender, Player attacker, string reason)
    {
        return CreateEvent(
            minute,
            EventType.PenaltyDecision,
            $"{defender.Name} {reason} {attacker.Name} inside the box. Penalty awarded against {defendingTeam.Name}.",
            defender.Name,
            attacker.Name);
    }

    public MatchEvent CreatePenaltyTaker(int minute, Team attackingTeam, Player taker)
    {
        return CreateEvent(
            minute,
            EventType.PenaltyTaker,
            $"{taker.Name} steps up for {attackingTeam.Name}.",
            taker.Name);
    }

    public MatchEvent CreatePenaltyResult(int minute, Team attackingTeam, Player taker, bool converted, bool saved, Match match)
    {
        var outcome = converted
            ? $"{taker.Name} scores from the penalty spot for {attackingTeam.Name}."
            : saved
                ? $"{taker.Name}'s penalty for {attackingTeam.Name} is saved."
                : $"{taker.Name} misses the penalty for {attackingTeam.Name}.";

        return CreateEvent(
            minute,
            EventType.Penalty,
            $"{outcome} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            taker.Name,
            match: match);
    }

    public MatchEvent CreateOffside(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Offside, $"{player.Name} breaks behind the line for {team.Name}, but the flag goes up.", player.Name);
    }

    public MatchEvent CreateOffside(int minute, Team team, Player player, string attackRoute, Random random)
    {
        var description = Pick(random,
            $"{player.Name} times the {attackRoute} run poorly for {team.Name} and is flagged offside.",
            $"The {attackRoute} move from {team.Name} is halted as {player.Name} strays offside.");

        return CreateEvent(minute, EventType.Offside, description, player.Name);
    }

    public MatchEvent CreateDefensiveError(int minute, Team defendingTeam, Player player)
    {
        return CreateEvent(minute, EventType.DefensiveError, $"{player.Name} makes a defensive error for {defendingTeam.Name}. Pressure builds immediately.", player.Name);
    }

    public MatchEvent CreateWonderGoal(int minute, Team team, Player player, Match match)
    {
        return CreateEvent(
            minute,
            EventType.WonderGoal,
            $"{player.Name} produces a wonder goal for {team.Name}. Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            player.Name,
            match: match);
    }

    public MatchEvent CreateGoalkeeperHeroics(int minute, Team team, Player goalkeeper)
    {
        return CreateEvent(minute, EventType.GoalkeeperHeroics, $"{goalkeeper.Name} keeps {team.Name} alive with a huge save.", goalkeeper.Name);
    }

    public MatchEvent CreateSetPieceDanger(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.SetPieceDanger, $"{team.Name} create danger from a set piece through {player.Name}.", player.Name);
    }

    public MatchEvent CreateSetPieceThreat(int minute, Team team, Player primaryTaker, Player secondaryTaker)
    {
        var secondaryText = string.Equals(primaryTaker.Name, secondaryTaker.Name, StringComparison.OrdinalIgnoreCase)
            ? "a teammate"
            : secondaryTaker.Name;
        return CreateEvent(
            minute,
            EventType.SetPieceDanger,
            $"{team.Name} win a dangerous free kick. {primaryTaker.Name} and {secondaryText} stand over it.",
            primaryTaker.Name,
            secondaryTaker.Name);
    }

    public MatchEvent CreateSetPieceShot(int minute, Team team, Player taker)
    {
        return CreateEvent(minute, EventType.Shot, $"{taker.Name} takes the free kick for {team.Name}.", taker.Name);
    }

    public MatchEvent CreateCornerKick(int minute, Team team, Player taker)
    {
        return CreateEvent(
            minute,
            EventType.CornerKick,
            $"{team.Name} win a corner. {taker.Name} goes across to take it.",
            taker.Name);
    }

    public MatchEvent CreateConfrontation(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Confrontation, $"Tempers flare as {player.Name} gets involved for {team.Name}.", player.Name);
    }

    public MatchEvent CreateCrowdMomentum(int minute, Team team)
    {
        return CreateEvent(minute, EventType.CrowdMomentum, $"The home crowd lifts {team.Name}. Momentum swings their way.");
    }

    public MatchEvent CreateExhaustion(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Exhaustion, $"{player.Name} looks exhausted for {team.Name}. Their intensity drops.", player.Name);
    }

    public MatchEvent CreateHalftime(int minute, Match match)
    {
        return CreateEvent(
            minute,
            EventType.Halftime,
            $"Halftime score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            match: match);
    }

    public MatchEvent CreateFulltime(int minute, Match match)
    {
        return CreateEvent(
            minute,
            EventType.Fulltime,
            $"Full time. Final score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            match: match);
    }

    private static MatchEvent CreateEvent(
        int minute,
        EventType eventType,
        string description,
        string? primaryPlayerName = null,
        string? secondaryPlayerName = null,
        Match? match = null)
    {
        return new MatchEvent
        {
            Minute = minute,
            EventType = eventType,
            HomeScore = match?.HomeScore,
            AwayScore = match?.AwayScore,
            PrimaryPlayerName = primaryPlayerName,
            SecondaryPlayerName = secondaryPlayerName,
            Description = description
        };
    }

    private static string Pick(Random random, params string[] options)
    {
        if (options.Length == 0)
        {
            return string.Empty;
        }

        return options[random.Next(options.Length)];
    }

    private static Player? ResolveDistinctTeammate(Team team, Player actor, Player? preferred, Random random)
    {
        if (preferred is not null &&
            !string.Equals(preferred.Name, actor.Name, StringComparison.OrdinalIgnoreCase))
        {
            return preferred;
        }

        var alternatives = team.Players
            .Where(candidate => !string.Equals(candidate.Name, actor.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (alternatives.Count == 0)
        {
            return null;
        }

        return alternatives[random.Next(alternatives.Count)];
    }

    private static string GetDisplayName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : parts[0];
    }
}
