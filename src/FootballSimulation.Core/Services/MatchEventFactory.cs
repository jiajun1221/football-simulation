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

    public MatchEvent CreateAttackBuildUp(
        int minute,
        Team attackingTeam,
        Player playmaker,
        Player target,
        Random random,
        PlayerTrait? triggeredTrait = null)
    {
        var safeTarget = ResolveDistinctTeammate(attackingTeam, playmaker, target, random);
        var playmakerName = GetDisplayName(playmaker.Name);
        var description = triggeredTrait is not null
            ? CreateTraitBuildUpDescription(attackingTeam, playmaker, safeTarget, triggeredTrait.Value, random)
            : safeTarget is null
            ? Pick(random,
                $"{attackingTeam.Name} build quickly and {playmakerName} finds space on the wing.",
                $"{attackingTeam.Name} move the ball with intent through {playmakerName}.",
                $"{attackingTeam.Name} progress from midfield with {playmakerName} in control.")
            : Pick(random,
                $"{attackingTeam.Name} build quickly as {playmakerName} picks out {GetDisplayName(safeTarget.Name)}.",
                $"{attackingTeam.Name} keep it moving and {playmakerName} links with {GetDisplayName(safeTarget.Name)}.",
                $"{attackingTeam.Name} combine neatly, with {playmakerName} releasing {GetDisplayName(safeTarget.Name)}.");

        return CreateEvent(minute, EventType.Attack, description, playmaker.Name, safeTarget?.Name, triggeredTrait: triggeredTrait);
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

    public MatchEvent CreatePossessionLossReason(
        int minute,
        Team attackingTeam,
        Team defendingTeam,
        Player attacker,
        Player? defender,
        EventType reasonType,
        Random random,
        PlayerTrait? triggeredTrait = null)
    {
        var attackerName = GetDisplayName(attacker.Name);
        var defenderName = defender is null ? defendingTeam.Name : GetDisplayName(defender.Name);

        var description = triggeredTrait is not null && defender is not null
            ? CreateTraitPossessionWinDescription(attackerName, defenderName, defendingTeam, triggeredTrait.Value, reasonType, random)
            : reasonType switch
        {
            EventType.BadPass => Pick(random,
                $"{attackerName}'s pass is loose for {attackingTeam.Name}.",
                $"{attackerName} tries a risky pass and {defendingTeam.Name} are alert.",
                $"{attackingTeam.Name} rush the pass through {attackerName} and the ball breaks away."),
            EventType.Miscontrol => Pick(random,
                $"{attackerName} miscontrols for {attackingTeam.Name}.",
                $"{attackerName}'s first touch gets away from him under pressure.",
                $"{attackerName} cannot bring the pass under control for {attackingTeam.Name}."),
            EventType.Tackle => Pick(random,
                $"{defenderName} times the tackle well on {attackerName} for {defendingTeam.Name}.",
                $"{defenderName} steps in strongly and takes the ball from {attackerName}."),
            EventType.Interception => Pick(random,
                $"{defenderName} reads {attackerName}'s pass well and steps in for {defendingTeam.Name}.",
                $"{defenderName} intercepts {attackerName}'s pass for {defendingTeam.Name}."),
            EventType.Pressure => Pick(random,
                $"{defenderName} presses quickly and forces {attackerName} backward.",
                $"{defendingTeam.Name} squeeze the space as {defenderName} puts pressure on {attackerName}."),
            EventType.BlockedPass => Pick(random,
                $"{defenderName} blocks {attackerName}'s pass for {defendingTeam.Name}.",
                $"{defenderName} gets in the lane and stops {attackerName}'s pass."),
            _ => $"{defendingTeam.Name} disrupt {attackingTeam.Name}'s attack."
        };

        var primaryPlayerName = IsDefenderPossessionWin(reasonType) ? defender?.Name : attacker.Name;
        var secondaryPlayerName = IsDefenderPossessionWin(reasonType) ? attacker.Name : defender?.Name;

        return CreateEvent(minute, reasonType, description, primaryPlayerName, secondaryPlayerName, triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateTurnover(int minute, Team possessionTeam, Player possessionPlayer, Random random)
    {
        var description = Pick(random,
            $"{possessionPlayer.Name} collects the ball for {possessionTeam.Name} and they look to build.",
            $"{possessionPlayer.Name} takes possession for {possessionTeam.Name} and settles on the ball.",
            $"{possessionPlayer.Name} comes away with it for {possessionTeam.Name} and looks forward.");

        return CreateEvent(minute, EventType.Turnover, description, possessionPlayer.Name);
    }

    public MatchEvent CreateFoul(int minute, Team defendingTeam, Player defender, Player attacker, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait == PlayerTrait.DivesIntoTackles
            ? Pick(
                new Random(minute + defender.Name.Length + attacker.Name.Length),
                $"{defender.Name} flies into an aggressive challenge on {attacker.Name} for {defendingTeam.Name}.",
                $"A crunching tackle from {defender.Name} stops {attacker.Name}, but the referee gives the foul.")
            : $"{defender.Name} fouls {attacker.Name} for {defendingTeam.Name}.";

        return CreateEvent(minute, EventType.Foul, description, defender.Name, attacker.Name, triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateShot(int minute, Team attackingTeam, Player attacker, Player? playmaker = null, PlayerTrait? triggeredTrait = null)
    {
        var description = playmaker is not null && playmaker != attacker
            ? $"{attacker.Name} takes a shot for {attackingTeam.Name} after a pass from {playmaker.Name}."
            : $"{attacker.Name} takes a shot for {attackingTeam.Name}.";

        return CreateEvent(minute, EventType.Shot, description, attacker.Name, playmaker?.Name, triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateShot(
        int minute,
        Team attackingTeam,
        Player attacker,
        Player? playmaker,
        string chanceType,
        Random random,
        PlayerTrait? triggeredTrait = null)
    {
        var safePlaymaker = ResolveDistinctTeammate(attackingTeam, attacker, playmaker, random);
        var attackerName = GetDisplayName(attacker.Name);
        var playmakerName = safePlaymaker is null ? string.Empty : GetDisplayName(safePlaymaker.Name);

        string description = triggeredTrait is not null
            ? CreateTraitShotDescription(attackingTeam, attacker, safePlaymaker, chanceType, triggeredTrait.Value, random)
            : chanceType switch
        {
            "long-range attempt" => Pick(random,
                attacker.Traits.Contains(PlayerTrait.LongShotTaker)
                    ? $"{attackerName} spots the keeper and lets fly early from distance for {attackingTeam.Name}."
                    : $"{attackerName} tries a long-range effort for {attackingTeam.Name}.",
                attacker.Traits.Contains(PlayerTrait.FinesseShot)
                    ? $"{attackerName} shapes a curled effort from range for {attackingTeam.Name}."
                    : $"{attackerName} lets fly from distance for {attackingTeam.Name}."),
            "cross into box" => Pick(random,
                safePlaymaker?.Traits.Contains(PlayerTrait.EarlyCrosser) == true
                    ? $"{playmakerName} sends an early cross in and {attackerName} attacks it for {attackingTeam.Name}."
                    : $"{attackingTeam.Name} whip a cross in and {attackerName} attacks it.",
                $"{attackerName} meets the delivery for {attackingTeam.Name} inside the area."),
            "through ball attempt" => safePlaymaker is not null
                ? safePlaymaker.Traits.Contains(PlayerTrait.LongPasser)
                    ? $"{playmakerName} opens the pitch with a long pass and {attackerName} gets the shot away for {attackingTeam.Name}."
                    : $"{playmakerName} slips a through ball and {attackerName} gets the shot away for {attackingTeam.Name}."
                : $"{attackingTeam.Name} find a lane through the middle and {attackerName} pulls the trigger.",
            "dribble run" => Pick(random,
                attacker.Traits.Contains(PlayerTrait.Rapid) || attacker.Traits.Contains(PlayerTrait.SpeedDribbler)
                    ? $"{attackerName} bursts past a marker at pace and shoots for {attackingTeam.Name}."
                    : attacker.Traits.Contains(PlayerTrait.TechnicalDribbler) || attacker.Traits.Contains(PlayerTrait.Flair)
                        ? $"{attackerName} uses quick feet to beat a marker and shoot for {attackingTeam.Name}."
                        : $"{attackerName} beats a marker and shoots for {attackingTeam.Name}.",
                $"{attackerName} dribbles into space and strikes for {attackingTeam.Name}."),
            _ => safePlaymaker is not null
                ? safePlaymaker.Traits.Contains(PlayerTrait.Playmaker)
                    ? $"{playmakerName} dictates the move and tees up {attackerName} for {attackingTeam.Name}."
                    : $"{attackerName} shoots for {attackingTeam.Name} after a pass from {playmakerName}."
                : $"{attackerName} takes a shot for {attackingTeam.Name}."
        };

        return CreateEvent(minute, EventType.Shot, description, attacker.Name, safePlaymaker?.Name, triggeredTrait: triggeredTrait);
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
        Player? assister = null,
        PlayerTrait? triggeredTrait = null)
    {
        var safeAssister = ResolveDistinctTeammate(attackingTeam, scorer, assister, random);
        var scorerName = GetDisplayName(scorer.Name);
        var baseLine = triggeredTrait is not null
            ? CreateTraitGoalLine(attackingTeam, scorer, safeAssister, goalTypeDescription, triggeredTrait.Value, random)
            : safeAssister is not null
            ? Pick(random,
                $"GOAL! {scorerName} finishes with a {goalTypeDescription} for {attackingTeam.Name}, set up by {GetDisplayName(safeAssister.Name)}.",
                $"GOAL! {scorerName} scores for {attackingTeam.Name} with a {goalTypeDescription}. Assist from {GetDisplayName(safeAssister.Name)}.")
            : Pick(random,
                $"GOAL! {scorerName} scores a {goalTypeDescription} for {attackingTeam.Name}.",
                $"GOAL! {scorerName} finds the net for {attackingTeam.Name} with a {goalTypeDescription}.");

        var description = $"{baseLine} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}";
        return CreateEvent(minute, EventType.Goal, description, scorer.Name, safeAssister?.Name, match, triggeredTrait);
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

    public MatchEvent CreateSave(
        int minute,
        Team defendingTeam,
        Player attacker,
        Player goalkeeper,
        string saveType,
        Random random,
        PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait is not null
            ? CreateTraitSaveDescription(defendingTeam, attacker, goalkeeper, triggeredTrait.Value, random)
            : Pick(random,
            $"{goalkeeper.Name} makes a {saveType} to deny {attacker.Name} for {defendingTeam.Name}.",
            $"{attacker.Name} is stopped by a {saveType} from {goalkeeper.Name} of {defendingTeam.Name}.");

        return CreateEvent(minute, EventType.Save, description, attacker.Name, goalkeeper.Name, triggeredTrait: triggeredTrait);
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

    public MatchEvent CreateInjury(int minute, Team team, Player player, string injuryCause = "")
    {
        var playerName = GetDisplayName(player.Name);
        var causeText = injuryCause switch
        {
            "dangerous tackle" => $"{playerName} looks in pain after a dangerous tackle.",
            "goalkeeper collision" => $"{playerName} stays down after a heavy goalkeeper collision.",
            "aerial duel impact" => $"{playerName} lands awkwardly after an aerial duel.",
            "sprint muscle pull" => $"{playerName} pulls up suddenly after a sprint.",
            "over exhaustion" => $"{playerName} drops to the turf after pushing through exhaustion.",
            "awkward landing" => $"{playerName} goes down after an awkward landing.",
            _ => $"{playerName} goes down after a heavy collision."
        };
        var treatmentText = Pick(
            new Random(minute + player.Name.Length + team.Name.Length),
            "Medical staff are called onto the pitch.",
            "The referee waves the medical team on.",
            $"{team.Name} look worried as treatment begins.");

        return CreateEvent(minute, EventType.Injury, $"{causeText} {treatmentText}", player.Name);
    }

    public MatchEvent CreatePenalty(int minute, Team team, Player player, bool converted, Match match)
    {
        var outcome = converted
            ? $"{player.Name} scores from the penalty spot for {team.Name}."
            : $"{player.Name}'s penalty for {team.Name} is saved.";

        return CreateEvent(
            minute,
            converted ? EventType.Goal : EventType.Save,
            $"{outcome} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            player.Name,
            match: match);
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

    public MatchEvent CreatePenaltyTaker(int minute, Team attackingTeam, Player taker, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait == PlayerTrait.PenaltySpecialist
            ? Pick(
                new Random(minute + taker.Name.Length),
                $"{taker.Name} steps up calmly for {attackingTeam.Name}.",
                $"{taker.Name} prepares for the penalty with total composure.")
            : $"{taker.Name} steps up for {attackingTeam.Name}.";

        return CreateEvent(
            minute,
            EventType.PenaltyTaker,
            description,
            taker.Name,
            triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreatePenaltyResult(
        int minute,
        Team attackingTeam,
        Player taker,
        bool converted,
        bool saved,
        Match match,
        PlayerTrait? triggeredTrait = null,
        Team? defendingTeam = null,
        Player? goalkeeper = null)
    {
        var outcome = converted && triggeredTrait is not null
            ? CreateTraitPenaltyScoreLine(attackingTeam, taker, triggeredTrait.Value, random: new Random(minute + taker.Name.Length))
            : converted
            ? $"{taker.Name} scores from the penalty spot for {attackingTeam.Name}."
            : saved
                ? CreatePenaltySaveLine(attackingTeam, defendingTeam, taker, goalkeeper)
                : CreatePenaltyMissLine(attackingTeam, taker, new Random(minute + taker.Name.Length));
        var eventType = converted
            ? EventType.Goal
            : saved
                ? EventType.Save
                : EventType.Miss;

        return CreateEvent(
            minute,
            eventType,
            $"{outcome} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            taker.Name,
            goalkeeper?.Name,
            match: match,
            triggeredTrait: triggeredTrait);
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

    public MatchEvent CreateOffsideRestart(int minute, Team team, Random random)
    {
        var description = Pick(random,
            $"{team.Name} restart play.",
            $"{team.Name} build from the back.",
            $"{team.Name} regain possession.",
            $"{team.Name} look to settle on the ball.");

        return CreateEvent(minute, EventType.Attack, description);
    }

    public MatchEvent CreateDefensiveError(int minute, Team defendingTeam, Player player)
    {
        return CreateEvent(minute, EventType.DefensiveError, $"{player.Name} makes a defensive error for {defendingTeam.Name}. Pressure builds immediately.", player.Name);
    }

    public MatchEvent CreateWonderGoal(int minute, Team team, Player player, Match match, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait is not null
            ? $"{CreateTraitWonderGoalLine(team, player, triggeredTrait.Value, new Random(minute + player.Name.Length))} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}"
            : $"{player.Name} produces a wonder goal for {team.Name}. Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}";

        return CreateEvent(
            minute,
            EventType.WonderGoal,
            description,
            player.Name,
            match: match,
            triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateGoalkeeperHeroics(int minute, Team team, Player goalkeeper)
    {
        return CreateEvent(minute, EventType.GoalkeeperHeroics, $"{goalkeeper.Name} keeps {team.Name} alive with a huge save.", goalkeeper.Name);
    }

    public MatchEvent CreateSetPieceDanger(int minute, Team team, Player player, PlayerTrait? triggeredTrait = null)
    {
        return CreateEvent(
            minute,
            EventType.SetPieceDanger,
            triggeredTrait == PlayerTrait.DeadBallSpecialist
                ? Pick(new Random(minute + player.Name.Length), $"{player.Name} whips a superb free kick delivery into danger for {team.Name}.", $"{team.Name} lean on {player.Name}'s dead-ball quality and the delivery is dangerous.")
                : $"{team.Name} create danger from a set piece through {player.Name}.",
            player.Name,
            triggeredTrait: triggeredTrait ?? (player.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? PlayerTrait.DeadBallSpecialist : null));
    }

    public MatchEvent CreateSetPieceThreat(int minute, Team team, Player primaryTaker, Player secondaryTaker)
    {
        var secondaryText = string.Equals(primaryTaker.Name, secondaryTaker.Name, StringComparison.OrdinalIgnoreCase)
            ? "a teammate"
            : secondaryTaker.Name;
        var triggeredTrait = primaryTaker.Traits.Contains(PlayerTrait.DeadBallSpecialist) ? PlayerTrait.DeadBallSpecialist : (PlayerTrait?)null;
        var description = triggeredTrait == PlayerTrait.DeadBallSpecialist
            ? Pick(new Random(minute + primaryTaker.Name.Length),
                $"{team.Name} win a dangerous free kick. {primaryTaker.Name}'s set-piece reputation has everyone alert.",
                $"{primaryTaker.Name} stands over it for {team.Name}, ready to bend a dangerous delivery.")
            : $"{team.Name} win a dangerous free kick. {primaryTaker.Name} and {secondaryText} stand over it.";

        return CreateEvent(
            minute,
            EventType.SetPieceDanger,
            description,
            primaryTaker.Name,
            secondaryTaker.Name,
            triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateSetPieceShot(int minute, Team team, Player taker, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait == PlayerTrait.DeadBallSpecialist
            ? Pick(new Random(minute + taker.Name.Length), $"{taker.Name} curls the free kick with real dead-ball quality for {team.Name}.", $"{taker.Name} strikes the set piece cleanly for {team.Name}.")
            : $"{taker.Name} takes the free kick for {team.Name}.";

        return CreateEvent(minute, EventType.Shot, description, taker.Name, triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateCornerKick(int minute, Team team, Player taker, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait switch
        {
            PlayerTrait.DeadBallSpecialist => Pick(new Random(minute + taker.Name.Length), $"{team.Name} win a corner and {taker.Name} shapes a dangerous dead-ball delivery.", $"{taker.Name}'s corner delivery looks dangerous for {team.Name}."),
            PlayerTrait.EarlyCrosser => $"{taker.Name} takes it quickly, whipping an early corner into the danger area for {team.Name}.",
            _ => $"{team.Name} win a corner. {taker.Name} goes across to take it."
        };

        return CreateEvent(
            minute,
            EventType.CornerKick,
            description,
            taker.Name,
            triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateConfrontation(int minute, Team team, Player player)
    {
        return CreateEvent(
            minute,
            EventType.Confrontation,
            Pick(
                new Random(minute + player.Name.Length + team.Name.Length),
                $"Tempers flare as {player.Name} gets involved for {team.Name}.",
                $"Players surround the referee and {player.Name} is right in the middle of it.",
                $"The match is getting heated; {player.Name} has to be pulled away for {team.Name}."),
            player.Name);
    }

    public MatchEvent CreateCrowdMomentum(int minute, Team team)
    {
        return CreateEvent(
            minute,
            EventType.CrowdMomentum,
            Pick(
                new Random(minute + team.Name.Length),
                $"The crowd erupts and the atmosphere lifts {team.Name}.",
                $"Stadium noise surges. Momentum swings toward {team.Name}.",
                $"{team.Name} feed off the noise as pressure builds around the stadium."));
    }

    public MatchEvent CreateExhaustion(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Exhaustion, $"{player.Name} looks exhausted for {team.Name}. Their intensity drops.", player.Name);
    }

    public MatchEvent CreateWeatherAnnouncement(int minute, WeatherCondition weatherCondition)
    {
        var description = weatherCondition switch
        {
            WeatherCondition.Rainy => "Rain is falling and the surface is getting slick.",
            WeatherCondition.HeavyRain => "Heavy rain makes conditions difficult. Players may struggle to keep their footing.",
            WeatherCondition.Windy => "A swirling wind is affecting long balls and crosses.",
            WeatherCondition.Foggy => "Fog rolls across the pitch and reactions could be a split second slower.",
            WeatherCondition.Snow => "Snow is falling. The cold surface could drain legs quickly.",
            _ => "Clear conditions tonight. The pitch looks quick and clean."
        };

        return CreateEvent(minute, EventType.Weather, description);
    }

    public MatchEvent CreateRivalryAtmosphere(int minute, Team homeTeam, Team awayTeam)
    {
        return CreateEvent(
            minute,
            EventType.RivalryAtmosphere,
            Pick(
                new Random(minute + homeTeam.Name.Length + awayTeam.Name.Length),
                $"{homeTeam.Name} against {awayTeam.Name} has a derby edge already. Neither side is backing down.",
                $"The atmosphere is fierce for {homeTeam.Name} versus {awayTeam.Name}. Every challenge is being cheered.",
                $"This rivalry feels loud from the first whistle. The tempo is already rising."));
    }

    public MatchEvent CreateVarCheck(int minute, string reason)
    {
        var description = reason switch
        {
            "goal" => "VAR checking possible offside in the build-up...",
            "penalty" => "VAR checking the penalty decision...",
            "red card" => "VAR checking the red card decision...",
            "offside" => "VAR checking a tight offside call...",
            _ => "VAR review underway. The stadium waits."
        };

        return CreateEvent(minute, EventType.VarCheck, description);
    }

    public MatchEvent CreateVarDecision(int minute, Team team, string outcome, Match? match = null)
    {
        var description = outcome switch
        {
            "goal disallowed" => $"Goal disallowed after VAR review. {team.Name} are pulled back.",
            "goal stands" => $"VAR confirms the goal for {team.Name}. The celebrations restart.",
            "penalty overturned" => $"Penalty overturned after VAR review. No foul by {team.Name}.",
            "red card cancelled" => $"Red card cancelled after VAR review. {team.Name} survive a huge moment.",
            "offside confirmed" => "Tight offside confirmed by VAR. The restart stands.",
            _ => "VAR review complete. Decision stands."
        };

        return CreateEvent(minute, EventType.VarDecision, description, match: match);
    }

    public MatchEvent CreateRefereeControversy(int minute, Team team, Player? player, Random random)
    {
        var description = Pick(random,
            $"Players furious with the referee's decision. {team.Name} feel that looked harsh.",
            $"Replay suggests there was very little contact. {team.Name} cannot believe the whistle.",
            $"{team.Name} surround the referee as frustration spills over.");

        return CreateEvent(minute, EventType.RefereeControversy, description, player?.Name);
    }

    public MatchEvent CreateWoodwork(int minute, Team attackingTeam, Player attacker, string reboundOutcome, Random random)
    {
        var description = Pick(random,
            $"{attacker.Name} rattles the crossbar for {attackingTeam.Name}! {reboundOutcome}",
            $"Shot crashes off the post from {attacker.Name}! {reboundOutcome}",
            $"Woodwork denies {attacker.Name} for {attackingTeam.Name}. {reboundOutcome}");

        return CreateEvent(minute, EventType.Woodwork, description, attacker.Name);
    }

    public MatchEvent CreateGoalkeeperMistake(int minute, Team defendingTeam, Player goalkeeper, Player attacker, Random random)
    {
        var description = Pick(random,
            $"{goalkeeper.Name} spills the ball under pressure for {defendingTeam.Name}. {attacker.Name} reacts first.",
            $"Terrible moment at the back as {goalkeeper.Name} cannot hold {attacker.Name}'s effort.",
            $"{goalkeeper.Name} misjudges it and the ball breaks dangerously inside the box.");

        return CreateEvent(minute, EventType.GoalkeeperMistake, description, goalkeeper.Name, attacker.Name);
    }

    public MatchEvent CreateLateDrama(int minute, Team team, Team opponentTeam, Match match)
    {
        var isLosing = team == match.HomeTeam ? match.HomeScore < match.AwayScore : match.AwayScore < match.HomeScore;
        var description = isLosing
            ? Pick(new Random(minute + team.Name.Length),
                $"{team.Name} are throwing everyone forward. Late drama is unfolding.",
                $"{team.Name} pile bodies into attack while {opponentTeam.Name} try to hang on.",
                $"Urgency everywhere now. {team.Name} chase the match with the crowd roaring.")
            : Pick(new Random(minute + team.Name.Length + opponentTeam.Name.Length),
                $"{team.Name} sense a late chance to decide this.",
                $"The final minutes are opening up. {team.Name} push for one more moment.",
                $"Tension rises as {team.Name} look for a late breakthrough.");

        return CreateEvent(minute, EventType.LateDrama, description);
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

    private static string CreateTraitBuildUpDescription(
        Team team,
        Player playmaker,
        Player? target,
        PlayerTrait trait,
        Random random)
    {
        var playerName = GetDisplayName(playmaker.Name);
        var targetText = target is null ? "a teammate" : GetDisplayName(target.Name);

        return trait switch
        {
            PlayerTrait.Playmaker => Pick(random,
                $"{playerName} spots the run perfectly and unlocks space for {team.Name}.",
                $"{playerName} shapes a defense-splitting pass toward {targetText} for {team.Name}."),
            PlayerTrait.LongPasser => Pick(random,
                $"{playerName} clips a superb long diagonal and switches the play for {team.Name}.",
                $"Pinpoint long ball from {playerName} finds {targetText} instantly for {team.Name}."),
            PlayerTrait.TeamPlayer => Pick(random,
                $"{playerName} chooses the unselfish pass and {team.Name} work a better angle.",
                $"Excellent teamwork from {playerName} keeps {team.Name} moving in the final third."),
            PlayerTrait.PressResistant => Pick(random,
                $"{playerName} stays calm under pressure and helps {team.Name} escape the press.",
                $"Excellent composure from {playerName}; {team.Name} build cleanly through midfield."),
            PlayerTrait.Leadership => Pick(random,
                $"{playerName} rallies {team.Name} forward and lifts the squad intensity.",
                $"Leadership from {playerName} settles {team.Name} on the ball."),
            PlayerTrait.Engine => Pick(random,
                $"{playerName} is still covering every blade of grass as {team.Name} push forward.",
                $"Relentless energy from {playerName} keeps {team.Name}'s move alive."),
            PlayerTrait.BoxToBox => Pick(random,
                $"{playerName} wins it and drives forward immediately for {team.Name}.",
                $"End-to-end contribution from {playerName} gives {team.Name} momentum."),
            PlayerTrait.LongThrower => Pick(random,
                $"{playerName} launches a quick long throw and {team.Name} break forward.",
                $"A massive throw from {playerName} finds {targetText} and starts a counter for {team.Name}."),
            _ => target is null
                ? $"{team.Name} move the ball with intent through {playerName}."
                : $"{team.Name} build quickly as {playerName} picks out {targetText}."
        };
    }

    private static string CreateTraitPossessionWinDescription(
        string attackerName,
        string defenderName,
        Team defendingTeam,
        PlayerTrait trait,
        EventType reasonType,
        Random random)
    {
        return trait switch
        {
            PlayerTrait.Interceptor => Pick(random,
                $"{defenderName} reads the pass brilliantly and cuts out the danger for {defendingTeam.Name}.",
                $"Quick anticipation from {defenderName} turns {attackerName}'s pass into a {defendingTeam.Name} turnover."),
            PlayerTrait.DivesIntoTackles => Pick(random,
                $"{defenderName} launches into a crunching tackle and wins it from {attackerName}.",
                $"Aggressive challenge from {defenderName} stops {attackerName}'s attack for {defendingTeam.Name}."),
            PlayerTrait.BoxToBox => Pick(random,
                $"{defenderName} tracks back, wins it, and immediately drives {defendingTeam.Name} forward.",
                $"End-to-end work from {defenderName} breaks up {attackerName}'s move."),
            _ => reasonType == EventType.Interception
                ? $"{defenderName} intercepts {attackerName}'s pass for {defendingTeam.Name}."
                : $"{defenderName} steps in strongly and takes the ball from {attackerName}."
        };
    }

    private static string CreateTraitShotDescription(
        Team team,
        Player attacker,
        Player? playmaker,
        string chanceType,
        PlayerTrait trait,
        Random random)
    {
        var attackerName = GetDisplayName(attacker.Name);
        var playmakerName = playmaker is null ? "a teammate" : GetDisplayName(playmaker.Name);

        return trait switch
        {
            PlayerTrait.FinesseShot => Pick(random,
                $"{attackerName} opens his body and bends a finesse effort for {team.Name}.",
                $"{attackerName} shapes a curling shot toward the far corner for {team.Name}."),
            PlayerTrait.LongShotTaker => Pick(random,
                $"{attackerName} tries one from distance for {team.Name}.",
                $"{attackerName} unleashes a rocket from outside the box for {team.Name}."),
            PlayerTrait.OutsideFootShot => Pick(random,
                $"{attackerName} goes outside-of-the-boot and surprises the defense for {team.Name}.",
                $"{attackerName} tries a trivela-style effort for {team.Name}."),
            PlayerTrait.PowerHeader => Pick(random,
                $"{attackerName} rises highest and powers a header toward goal for {team.Name}.",
                $"A thunderous header from {attackerName} beats everyone in the air."),
            PlayerTrait.AerialThreat => Pick(random,
                $"{attackerName} dominates in the air and heads at goal for {team.Name}.",
                $"{attackerName}'s aerial presence causes chaos in the box."),
            PlayerTrait.Flair => Pick(random,
                $"A moment of magic from {attackerName} opens space and he shoots for {team.Name}.",
                $"{attackerName} uses a clever flick to create the shooting lane for {team.Name}."),
            PlayerTrait.SpeedDribbler => Pick(random,
                $"{attackerName} bursts past defenders with raw pace and shoots for {team.Name}.",
                $"Rapid acceleration from {attackerName} creates separation before the strike."),
            PlayerTrait.Rapid => Pick(random,
                $"{attackerName} explodes down the flank and gets the shot away for {team.Name}.",
                $"{attackerName} is too quick to catch as he breaks into shooting range."),
            PlayerTrait.TechnicalDribbler => Pick(random,
                $"{attackerName} shows tight control through traffic before shooting for {team.Name}.",
                $"Elegant dribbling from {attackerName} breaks the line and creates the chance."),
            PlayerTrait.Playmaker => Pick(random,
                $"{playmakerName} spots the run perfectly and {attackerName} gets the shot away for {team.Name}.",
                $"A defense-splitting pass from {playmakerName} unlocks the backline for {attackerName}."),
            PlayerTrait.LongPasser => Pick(random,
                $"{playmakerName} picks a superb long diagonal and {attackerName} attacks the space for {team.Name}.",
                $"Pinpoint long ball from {playmakerName} finds {attackerName} instantly."),
            PlayerTrait.EarlyCrosser => Pick(random,
                $"{playmakerName} delivers early and catches the defense sleeping for {team.Name}.",
                $"Quick delivery from {playmakerName} flashes into the danger area for {attackerName}."),
            PlayerTrait.TeamPlayer => Pick(random,
                $"{playmakerName} chooses the unselfish option and {attackerName} has the better chance.",
                $"Excellent teamwork in the final third sets up {attackerName} for {team.Name}."),
            PlayerTrait.TriesToBeatOffsideTrap => Pick(random,
                $"{attackerName} times the run perfectly, beats the line, and shoots for {team.Name}.",
                $"{attackerName} bends the run cleverly before pulling the trigger for {team.Name}."),
            _ => playmaker is null
                ? $"{attackerName} takes a shot for {team.Name}."
                : $"{attackerName} shoots for {team.Name} after a pass from {playmakerName}."
        };
    }

    private static string CreateTraitGoalLine(
        Team team,
        Player scorer,
        Player? assister,
        string goalTypeDescription,
        PlayerTrait trait,
        Random random)
    {
        var scorerName = GetDisplayName(scorer.Name);
        var assisterName = assister is null ? "a teammate" : GetDisplayName(assister.Name);

        return trait switch
        {
            PlayerTrait.FinesseShot => Pick(random,
                $"GOAL! {scorerName} bends a beautiful finesse effort into the corner for {team.Name}.",
                $"GOAL! A curling finish from {scorerName} leaves the keeper stranded."),
            PlayerTrait.PowerHeader => Pick(random,
                $"GOAL! {scorerName} rises highest and powers the header home for {team.Name}.",
                $"GOAL! A thunderous header from {scorerName} beats everyone in the air."),
            PlayerTrait.AerialThreat => Pick(random,
                $"GOAL! {scorerName} dominates in the air and heads {team.Name} in.",
                $"GOAL! {scorerName}'s aerial presence causes chaos and he finishes the chance."),
            PlayerTrait.Playmaker => Pick(random,
                $"GOAL! {assisterName} spots the run perfectly and {scorerName} finishes for {team.Name}.",
                $"GOAL! A defense-splitting pass from {assisterName} unlocks the backline for {scorerName}."),
            PlayerTrait.TeamPlayer => Pick(random,
                $"GOAL! Unselfish play from {assisterName} creates the finish for {scorerName}.",
                $"GOAL! Excellent teamwork from {team.Name}, finished by {scorerName}."),
            PlayerTrait.LongPasser => Pick(random,
                $"GOAL! {assisterName}'s pinpoint long ball finds {scorerName}, who finishes for {team.Name}.",
                $"GOAL! A superb long diagonal from {assisterName} opens the pitch for {scorerName}."),
            PlayerTrait.EarlyCrosser => Pick(random,
                $"GOAL! An early cross from {assisterName} catches the defense sleeping and {scorerName} finishes.",
                $"GOAL! Quick delivery into the danger area, and {scorerName} converts for {team.Name}."),
            PlayerTrait.OutsideFootShot => Pick(random,
                $"GOAL! {scorerName}'s outside-of-the-boot finish curls beautifully for {team.Name}.",
                $"GOAL! A trivela-style finish from {scorerName} surprises the keeper."),
            PlayerTrait.ClinicalFinisher => Pick(random,
                $"GOAL! One chance. One goal. {scorerName} is deadly for {team.Name}.",
                $"GOAL! Calm, clinical finishing from {scorerName} inside the box."),
            PlayerTrait.SpeedDribbler => Pick(random,
                $"GOAL! {scorerName} bursts clear with raw pace and finishes for {team.Name}.",
                $"GOAL! Rapid acceleration creates the gap and {scorerName} takes full advantage."),
            PlayerTrait.Rapid => Pick(random,
                $"GOAL! {scorerName} is too quick to catch and finishes the break for {team.Name}.",
                $"GOAL! Explosive pace from {scorerName} turns the attack into a finish."),
            PlayerTrait.TechnicalDribbler => Pick(random,
                $"GOAL! Tight control through traffic from {scorerName}, then a composed finish.",
                $"GOAL! Elegant dribbling from {scorerName} breaks the line for {team.Name}."),
            PlayerTrait.Flair => Pick(random,
                $"GOAL! A moment of magic from {scorerName} for {team.Name}.",
                $"GOAL! {scorerName}'s flair opens the finish in style."),
            PlayerTrait.DeadBallSpecialist => Pick(random,
                $"GOAL! {scorerName} curls a superb dead-ball strike for {team.Name}.",
                $"GOAL! Dangerous set-piece quality from {scorerName} finds the net."),
            PlayerTrait.TriesToBeatOffsideTrap => Pick(random,
                $"GOAL! {scorerName} times the run perfectly and finishes for {team.Name}.",
                $"GOAL! {scorerName} bends the run cleverly, beats the line, and scores."),
            _ => assister is not null
                ? $"GOAL! {scorerName} finishes with a {goalTypeDescription} for {team.Name}, set up by {assisterName}."
                : $"GOAL! {scorerName} scores a {goalTypeDescription} for {team.Name}."
        };
    }

    private static string CreateTraitSaveDescription(
        Team team,
        Player attacker,
        Player goalkeeper,
        PlayerTrait trait,
        Random random)
    {
        return trait switch
        {
            PlayerTrait.OneOnOnes => Pick(random,
                $"Huge one-on-one save from {goalkeeper.Name} to deny {attacker.Name} for {team.Name}.",
                $"{goalkeeper.Name} stays big and denies {attacker.Name} in a one-on-one."),
            PlayerTrait.RushesOutOfGoal => Pick(random,
                $"{goalkeeper.Name} rushes out aggressively and smothers {attacker.Name}'s chance for {team.Name}.",
                $"Sweeper-keeper action from {goalkeeper.Name} prevents danger for {team.Name}."),
            PlayerTrait.Puncher => Pick(random,
                $"{goalkeeper.Name} punches clear under pressure for {team.Name}.",
                $"Strong punch from {goalkeeper.Name} removes the aerial danger."),
            PlayerTrait.LongThrower => Pick(random,
                $"{goalkeeper.Name} saves and immediately launches a quick long throw for {team.Name}.",
                $"Massive throw from {goalkeeper.Name} starts a counter after denying {attacker.Name}."),
            _ => $"{goalkeeper.Name} makes a strong save to deny {attacker.Name} for {team.Name}."
        };
    }

    private static string CreateTraitPenaltyScoreLine(Team team, Player taker, PlayerTrait trait, Random random)
    {
        return trait switch
        {
            PlayerTrait.DeadBallSpecialist => Pick(random,
                $"{taker.Name} sends the keeper the wrong way with dead-ball precision for {team.Name}.",
                $"{taker.Name}'s set-piece quality shows from the spot for {team.Name}."),
            PlayerTrait.PenaltySpecialist => Pick(random,
                $"{taker.Name} buries it from the spot for {team.Name}.",
                $"{taker.Name} sends the keeper the wrong way for {team.Name}.",
                $"{taker.Name} stays calm and converts the penalty for {team.Name}."),
            PlayerTrait.ClinicalFinisher => Pick(random,
                $"One chance. One goal. {taker.Name} stays clinical from the spot for {team.Name}.",
                $"{taker.Name} finishes calmly from the penalty spot for {team.Name}."),
            PlayerTrait.FinesseShot => $"{taker.Name} strokes a curled penalty beyond the keeper for {team.Name}.",
            _ => $"{taker.Name} scores from the penalty spot for {team.Name}."
        };
    }

    private static string CreatePenaltySaveLine(Team attackingTeam, Team? defendingTeam, Player taker, Player? goalkeeper)
    {
        if (goalkeeper is not null)
        {
            var teamText = defendingTeam is null ? string.Empty : $" for {defendingTeam.Name}";
            return $"{goalkeeper.Name} saves the penalty from {taker.Name}{teamText}. Huge stop from the keeper.";
        }

        return $"{taker.Name}'s penalty for {attackingTeam.Name} is saved. Huge stop from the keeper.";
    }

    private static string CreatePenaltyMissLine(Team attackingTeam, Player taker, Random random)
    {
        return Pick(random,
            $"{taker.Name} misses the penalty for {attackingTeam.Name}. The effort goes wide.",
            $"{taker.Name} misses from the spot for {attackingTeam.Name}. Huge miss.",
            $"{taker.Name}'s penalty for {attackingTeam.Name} clips the woodwork and stays out.");
    }

    private static string CreateTraitWonderGoalLine(Team team, Player player, PlayerTrait trait, Random random)
    {
        return trait switch
        {
            PlayerTrait.FinesseShot => Pick(random,
                $"{player.Name} bends a stunning finesse effort into the top corner for {team.Name}.",
                $"A curling wonder finish from {player.Name} leaves the keeper stranded."),
            PlayerTrait.LongShotTaker => Pick(random,
                $"{player.Name} launches a rocket from outside the box for {team.Name}.",
                $"{player.Name} tries one from distance and it flies in for {team.Name}."),
            PlayerTrait.OutsideFootShot => Pick(random,
                $"{player.Name} curls a sensational outside-of-the-boot finish for {team.Name}.",
                $"A trivela-style wonder goal from {player.Name} lights up the match."),
            PlayerTrait.Flair => Pick(random,
                $"A moment of magic from {player.Name} produces a wonder goal for {team.Name}.",
                $"{player.Name} creates space with a brilliant flick and finishes spectacularly."),
            PlayerTrait.SpeedDribbler => Pick(random,
                $"{player.Name} bursts past defenders with raw pace and scores a wonder goal for {team.Name}.",
                $"Rapid acceleration from {player.Name} creates the space for a brilliant finish."),
            PlayerTrait.TechnicalDribbler => Pick(random,
                $"{player.Name} glides through traffic with tight control and scores for {team.Name}.",
                $"Elegant dribbling from {player.Name} breaks the line before a stunning finish."),
            PlayerTrait.ClinicalFinisher => $"Deadly finishing from {player.Name}; the chance becomes a wonder goal for {team.Name}.",
            _ => $"{player.Name} produces a wonder goal for {team.Name}."
        };
    }

    private static MatchEvent CreateEvent(
        int minute,
        EventType eventType,
        string description,
        string? primaryPlayerName = null,
        string? secondaryPlayerName = null,
        Match? match = null,
        PlayerTrait? triggeredTrait = null)
    {
        return new MatchEvent
        {
            Minute = minute,
            EventType = eventType,
            HomeScore = match?.HomeScore,
            AwayScore = match?.AwayScore,
            PrimaryPlayerName = primaryPlayerName,
            SecondaryPlayerName = secondaryPlayerName,
            TriggeredTrait = triggeredTrait,
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

    private static bool IsDefenderPossessionWin(EventType eventType)
    {
        return eventType is EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass;
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
