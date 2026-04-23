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
        var description = playmaker == shooter
            ? $"{attackingTeam.Name} build an attack through {playmaker.Name}."
            : $"{attackingTeam.Name} build an attack through {playmaker.Name}, looking for {shooter.Name}.";

        return CreateEvent(minute, EventType.Attack, description, playmaker.Name, shooter.Name);
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

    public MatchEvent CreateMiss(int minute, Team attackingTeam, Player attacker)
    {
        return CreateEvent(minute, EventType.Miss, $"{attacker.Name} misses the target for {attackingTeam.Name}.", attacker.Name);
    }

    public MatchEvent CreateSave(int minute, Team defendingTeam, Player attacker)
    {
        return CreateEvent(minute, EventType.Save, $"{attacker.Name}'s shot is saved by {defendingTeam.Name}.", attacker.Name);
    }

    public MatchEvent CreateYellowCard(int minute, Player player)
    {
        return CreateEvent(minute, EventType.YellowCard, $"Yellow card for {player.Name}.", player.Name);
    }

    public MatchEvent CreateRedCard(int minute, Player player)
    {
        return CreateEvent(minute, EventType.RedCard, $"Red card for {player.Name}. {player.Name} is sent off.", player.Name);
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

    public MatchEvent CreateOffside(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Offside, $"{player.Name} breaks behind the line for {team.Name}, but the flag goes up.", player.Name);
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
}
