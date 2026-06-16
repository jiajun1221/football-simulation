using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class MatchEventFactory
{
    public MatchEvent CreateKickoff(int minute, Team homeTeam)
    {
        return CreateEvent(minute, EventType.Kickoff, $"{homeTeam.Name} kick off the match.");
    }

    public MatchEvent CreateSecondHalfKickoff(int minute, Team team)
    {
        return CreateEvent(minute, EventType.Kickoff, $"{team.Name} get the second half underway.");
    }

    public MatchEvent CreateAttack(int minute, Team attackingTeam, Player playmaker, Player shooter)
    {
        var safeShooter = !string.Equals(playmaker.Name, shooter.Name, StringComparison.OrdinalIgnoreCase)
            ? shooter
            : ResolveFirstDistinctOutfieldTeammate(attackingTeam, playmaker);

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
        var tacticalDescription = triggeredTrait is null
            ? CreateTacticalBuildUpDescription(attackingTeam, playmaker, safeTarget, random)
            : null;
        var description = triggeredTrait is not null
            ? CreateTraitBuildUpDescription(attackingTeam, playmaker, safeTarget, triggeredTrait.Value, random)
            : tacticalDescription ?? (safeTarget is null
            ? Pick(random,
                $"{attackingTeam.Name} build quickly and {playmakerName} finds space on the wing.",
                $"{attackingTeam.Name} move the ball with intent through {playmakerName}.",
                $"{attackingTeam.Name} progress from midfield with {playmakerName} in control.")
            : Pick(random,
                $"{attackingTeam.Name} build quickly as {playmakerName} picks out {GetDisplayName(safeTarget.Name)}.",
                $"{attackingTeam.Name} keep it moving and {playmakerName} links with {GetDisplayName(safeTarget.Name)}.",
                $"{attackingTeam.Name} combine neatly, with {playmakerName} releasing {GetDisplayName(safeTarget.Name)}."));

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
            $"{defender.Name} reads the danger for {defendingTeam.Name} and stops {attacker.Name}.",
            $"{defender.Name} blocks the shot for {defendingTeam.Name}.",
            $"{defender.Name} breaks up the attack before {attacker.Name} can finish.",
            $"{defender.Name} clears under pressure for {defendingTeam.Name}.",
            $"Brilliant tackle by {defender.Name} for {defendingTeam.Name} on {attacker.Name}.",
            $"Interception cuts out the attack as {defender.Name} steps in for {defendingTeam.Name}.");

        return CreateEvent(minute, EventType.DefensiveStop, description, defender.Name, attacker.Name);
    }

    public MatchEvent CreateBlockedShot(int minute, Team defendingTeam, Player defender, Player attacker, Random random)
    {
        var description = Pick(random,
            $"{defender.Name} blocks {attacker.Name}'s shot for {defendingTeam.Name}.",
            $"{defender.Name} throws himself in front of {attacker.Name}'s effort.",
            $"{attacker.Name} gets the shot away, but {defender.Name} blocks it for {defendingTeam.Name}.");

        return CreateEvent(minute, EventType.DefensiveStop, description, defender.Name, attacker.Name);
    }

    public MatchEvent CreateReboundClearance(int minute, Team defendingTeam, Player defender, Player attacker, Random random)
    {
        var description = Pick(random,
            $"{defender.Name} clears the rebound for {defendingTeam.Name} after {attacker.Name}'s effort.",
            $"{attacker.Name}'s effort comes back out and {defender.Name} sweeps it clear.",
            $"{defendingTeam.Name} survive as {defender.Name} clears the loose ball.");

        return CreateEvent(minute, EventType.DefensiveStop, description, defender.Name, attacker.Name);
    }

    public MatchEvent CreateGoalLineClearance(int minute, Team defendingTeam, Player defender, Player attacker, Random random)
    {
        var description = Pick(random,
            $"{defender.Name} makes a stunning goal-line clearance for {defendingTeam.Name} to deny {attacker.Name}.",
            $"{defender.Name} somehow clears off the line for {defendingTeam.Name}.",
            $"{defender.Name} reads the danger and hooks it off the line for {defendingTeam.Name}.");

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

        var tacticalDescription = triggeredTrait is null
            ? CreateTacticalPossessionLossDescription(attackingTeam, defendingTeam, attackerName, defenderName, reasonType, random)
            : null;
        var description = triggeredTrait is not null && defender is not null
            ? CreateTraitPossessionWinDescription(attackerName, defenderName, defendingTeam, triggeredTrait.Value, reasonType, random)
            : tacticalDescription ?? (reasonType switch
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
                $"{defenderName} steps in strongly and takes the ball from {attackerName}.",
                $"{defenderName} breaks up the attack with a clean challenge."),
            EventType.Interception => Pick(random,
                $"{defenderName} reads {attackerName}'s pass well and steps in for {defendingTeam.Name}.",
                $"{defenderName} intercepts {attackerName}'s pass for {defendingTeam.Name}.",
                $"{defenderName} reads the danger and cuts out the pass."),
            EventType.Pressure => Pick(random,
                $"{defenderName} presses quickly and forces {attackerName} backward.",
                $"{defendingTeam.Name} squeeze the space as {defenderName} puts pressure on {attackerName}.",
                $"{defenderName} breaks up the attack with sharp pressure."),
            EventType.BlockedPass => Pick(random,
                $"{defenderName} blocks {attackerName}'s pass for {defendingTeam.Name}.",
                $"{defenderName} gets in the lane and stops {attackerName}'s pass.",
                $"{defenderName} steps across and shuts down the passing lane."),
            _ => $"{defendingTeam.Name} disrupt {attackingTeam.Name}'s attack."
        });

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

    public MatchEvent CreateFoul(
        int minute,
        Team defendingTeam,
        Team attackingTeam,
        Player defender,
        Player attacker,
        FoulLocation foulLocation = FoulLocation.OpenPlay,
        bool isPenaltyFoul = false,
        PlayerTrait? triggeredTrait = null)
    {
        var description = isPenaltyFoul || foulLocation == FoulLocation.PenaltyBox
            ? CreatePenaltyBoxFoulDescription(defendingTeam, attackingTeam, defender, attacker, triggeredTrait)
            : triggeredTrait == PlayerTrait.DivesIntoTackles
            ? Pick(
                new Random(minute + defender.Name.Length + attacker.Name.Length),
                $"{defender.Name} flies into an aggressive challenge on {attacker.Name} for {defendingTeam.Name}.",
                $"A crunching tackle from {defender.Name} stops {attacker.Name}, but the referee gives the foul.")
            : $"{defender.Name} fouls {attacker.Name} for {defendingTeam.Name}.";

        var matchEvent = CreateEvent(minute, EventType.Foul, description, defender.Name, attacker.Name, triggeredTrait: triggeredTrait);
        matchEvent.FoulLocation = foulLocation;
        matchEvent.IsPenaltyFoul = isPenaltyFoul;
        matchEvent.FoulingPlayer = defender.Name;
        matchEvent.FouledPlayer = attacker.Name;
        matchEvent.FoulingTeam = defendingTeam.Name;
        matchEvent.AttackingTeam = attackingTeam.Name;
        return matchEvent;
    }

    private static string CreatePenaltyBoxFoulDescription(
        Team defendingTeam,
        Team attackingTeam,
        Player defender,
        Player attacker,
        PlayerTrait? triggeredTrait)
    {
        if (triggeredTrait == PlayerTrait.DivesIntoTackles)
        {
            return $"{defender.Name} dives in and brings down {attacker.Name} inside the penalty area for {defendingTeam.Name}. {attackingTeam.Name} appeal immediately as the referee stops play.";
        }

        return $"{defender.Name} brings down {attacker.Name} inside the penalty area for {defendingTeam.Name}. The referee stops play immediately.";
    }

    public MatchEvent CreateShot(int minute, Team attackingTeam, Player attacker, Player? playmaker = null, PlayerTrait? triggeredTrait = null)
    {
        var description = playmaker is not null && !string.Equals(playmaker.Name, attacker.Name, StringComparison.OrdinalIgnoreCase)
            ? $"{attacker.Name} takes a shot for {attackingTeam.Name} after a pass from {playmaker.Name}."
            : $"{attacker.Name} takes a shot for {attackingTeam.Name}.";

        var matchEvent = CreateEvent(minute, EventType.Shot, description, attacker.Name, GetAssistCandidateName(attacker, playmaker), triggeredTrait: triggeredTrait);
        matchEvent.ShotClassification = ClassifyShot(string.Empty, description);
        return matchEvent;
    }

    public MatchEvent CreateChanceCreated(
        int minute,
        Team attackingTeam,
        Player chanceCreator,
        Player shooter,
        string chanceType,
        Random random,
        PlayerTrait? triggeredTrait = null)
    {
        var creatorName = GetDisplayName(chanceCreator.Name);
        var shooterName = GetDisplayName(shooter.Name);
        var hasSeparateShooter = !string.Equals(chanceCreator.Name, shooter.Name, StringComparison.OrdinalIgnoreCase);

        string description = triggeredTrait is not null
            ? CreateTraitChanceDescription(attackingTeam, chanceCreator, shooter, chanceType, triggeredTrait.Value, random)
            : chanceType switch
        {
            "long-range attempt" => Pick(random,
                $"{creatorName} finds space outside the box for {attackingTeam.Name}.",
                $"{creatorName} opens the angle from range for {attackingTeam.Name}."),
            "cross into box" => Pick(random,
                chanceCreator.Traits.Contains(PlayerTrait.EarlyCrosser)
                    ? $"{creatorName} delivers early and catches the defense sleeping for {attackingTeam.Name}."
                    : $"{creatorName} whips a cross into the area for {attackingTeam.Name}.",
                hasSeparateShooter
                    ? $"{creatorName} picks out {shooterName} with a dangerous delivery."
                    : $"{creatorName} attacks the box and creates the opening."),
            "through ball attempt" => Pick(random,
                chanceCreator.Traits.Contains(PlayerTrait.LongPasser)
                    ? $"{creatorName} opens the pitch with a long pass for {attackingTeam.Name}."
                    : $"{creatorName} slips a through ball behind the line for {attackingTeam.Name}.",
                hasSeparateShooter
                    ? $"{creatorName} releases {shooterName} into space."
                    : $"{creatorName} times the run and breaks the line."),
            "dribble run" => Pick(random,
                $"{creatorName} beats a marker and creates space for {attackingTeam.Name}.",
                $"{creatorName} drives into space and opens up the chance."),
            "quick combination" => Pick(random,
                hasSeparateShooter
                    ? $"{creatorName} combines neatly and tees up {shooterName} for {attackingTeam.Name}."
                    : $"{creatorName} combines quickly and finds room to shoot.",
                $"{creatorName} unlocks the defense with sharp final-third play."),
            _ => hasSeparateShooter
                ? $"{creatorName} creates the chance for {shooterName} and {attackingTeam.Name}."
                : $"{creatorName} creates a shooting chance for {attackingTeam.Name}."
        };

        return CreateEvent(minute, EventType.ChanceCreated, description, chanceCreator.Name, hasSeparateShooter ? shooter.Name : null, triggeredTrait: triggeredTrait);
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
        var attackerName = GetDisplayName(attacker.Name);
        var playmakerName = playmaker is null || string.Equals(playmaker.Name, attacker.Name, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : GetDisplayName(playmaker.Name);

        string description = chanceType switch
        {
            "corner header" => Pick(random,
                $"{attackerName} heads the corner goalward for {attackingTeam.Name}.",
                $"{attackerName} rises to the corner and directs a header toward goal.",
                $"{attackerName} attacks the corner delivery and heads at goal for {attackingTeam.Name}."),
            "free-kick header" => Pick(random,
                string.IsNullOrWhiteSpace(playmakerName)
                    ? $"{attackerName} heads the free kick goalward for {attackingTeam.Name}."
                    : $"{attackerName} heads {playmakerName}'s free kick goalward for {attackingTeam.Name}.",
                $"{attackerName} meets the free-kick delivery and heads at goal for {attackingTeam.Name}.",
                $"{attackerName} goes for a free-kick header for {attackingTeam.Name}."),
            "corner delivery" => Pick(random,
                $"{attackerName} attacks the corner delivery for {attackingTeam.Name}.",
                $"{attackerName} meets the corner and sends it goalward for {attackingTeam.Name}.",
                $"{attackerName} rises to the delivery and directs the chance toward goal."),
            "free-kick delivery" => Pick(random,
                $"{attackerName} attacks the free-kick delivery for {attackingTeam.Name}.",
                $"{attackerName} meets {playmakerName}'s free kick and heads goalward for {attackingTeam.Name}.",
                $"{attackerName} gets on the end of the set-piece delivery for {attackingTeam.Name}."),
            "long-range attempt" => Pick(random,
                $"{attackerName} shoots from distance for {attackingTeam.Name}.",
                $"{attackerName} lets fly from range for {attackingTeam.Name}."),
            "cross into box" => Pick(random,
                string.IsNullOrWhiteSpace(playmakerName)
                    ? $"{attackerName} meets the delivery and gets the shot away for {attackingTeam.Name}."
                    : $"{attackerName} meets {playmakerName}'s delivery and gets the shot away for {attackingTeam.Name}.",
                $"{attackerName} meets the delivery for {attackingTeam.Name} inside the area."),
            "through ball attempt" => !string.IsNullOrWhiteSpace(playmakerName)
                ? $"{attackerName} runs onto {playmakerName}'s pass and gets the shot away for {attackingTeam.Name}."
                : $"{attackingTeam.Name} find a lane through the middle and {attackerName} pulls the trigger.",
            "dribble run" => Pick(random,
                $"{attackerName} gets the shot away after the dribble for {attackingTeam.Name}.",
                $"{attackerName} drives into shooting range and strikes for {attackingTeam.Name}."),
            _ => !string.IsNullOrWhiteSpace(playmakerName)
                ? $"{attackerName} shoots for {attackingTeam.Name} after {playmakerName}'s pass."
                : $"{attackerName} takes a shot for {attackingTeam.Name}."
        };

        var matchEvent = CreateEvent(minute, EventType.Shot, description, attacker.Name, GetAssistCandidateName(attacker, playmaker), triggeredTrait: triggeredTrait);
        matchEvent.ShotClassification = ClassifyShot(chanceType, description);
        return matchEvent;
    }

    public MatchEvent CreateGoal(int minute, Team attackingTeam, Player scorer, Match match, Player? assister = null, int scorerMatchGoals = 0)
    {
        var assistText = assister is not null && assister != scorer
            ? $" Assisted by {assister.Name}."
            : string.Empty;
        var milestoneText = CreateGoalMilestoneText(scorer, attackingTeam, scorerMatchGoals, new Random(minute + scorer.Name.Length));

        return CreateEvent(
            minute,
            EventType.Goal,
            $"{scorer.Name} scores for {attackingTeam.Name}.{assistText}{milestoneText} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
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
        PlayerTrait? triggeredTrait = null,
        int scorerMatchGoals = 0)
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

        var milestoneText = CreateGoalMilestoneText(scorer, attackingTeam, scorerMatchGoals, random);
        var description = $"{baseLine}{milestoneText} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}";
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

        var matchEvent = CreateEvent(minute, EventType.Miss, description, attacker.Name);
        matchEvent.ShotClassification = ClassifyShot(shotStyle, description);
        return matchEvent;
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

    public MatchEvent CreateYellowCard(int minute, Player player, string? reason = null)
    {
        var description = string.IsNullOrWhiteSpace(reason)
            ? $"Yellow card for {player.Name}."
            : $"The referee steps in and books {player.Name} {reason}.";

        return CreateEvent(minute, EventType.YellowCard, description, player.Name);
    }

    public MatchEvent CreateRedCard(int minute, Player player)
    {
        return CreateEvent(
            minute,
            EventType.RedCard,
            $"Red card for {player.Name}. {player.Name} is sent off. {player.Name} will miss the next match due to suspension.",
            player.Name);
    }

    public MatchEvent CreateRedCard(int minute, Player player, string reason)
    {
        if (reason.Equals("second yellow", StringComparison.OrdinalIgnoreCase))
        {
            return CreateEvent(
                minute,
                EventType.RedCard,
                $"Second yellow! {player.Name} is sent off. That dismissal means {player.Name} is suspended for the next fixture.",
                player.Name);
        }

        return CreateEvent(
            minute,
            EventType.RedCard,
            $"Straight red card for {player.Name}: {reason}. {player.Name} is sent off and will miss the next match due to suspension.",
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

    public MatchEvent CreateStoppageSubstitution(int minute, Team team, Player playerOff, Player playerOn, EventType stoppageType)
    {
        var stoppageText = stoppageType switch
        {
            EventType.Goal or EventType.WonderGoal or EventType.Penalty => "before the restart",
            EventType.Injury => "during the injury stoppage",
            EventType.Foul or EventType.SetPieceDanger or EventType.CornerKick or EventType.Offside => "during the stoppage",
            EventType.VarCheck or EventType.VarDecision => "during the VAR stoppage",
            EventType.YellowCard or EventType.RedCard or EventType.RefereeControversy or EventType.Confrontation => "after the stoppage",
            _ => "during the stoppage"
        };

        return CreateEvent(
            minute,
            EventType.Substitution,
            $"{team.Name} make a change {stoppageText}. {playerOn.Name} replaces {playerOff.Name}.",
            playerOn.Name,
            playerOff.Name);
    }

    public MatchEvent CreateHalftimeSubstitution(int minute, Team team, MatchSubstitution substitution)
    {
        return CreateEvent(
            minute,
            EventType.Substitution,
            $"{team.Name} make a halftime change. {substitution.PlayerOnName} replaces {substitution.PlayerOffName}.",
            substitution.PlayerOnName,
            substitution.PlayerOffName);
    }

    public MatchEvent CreateGroupedHalftimeSubstitution(int minute, Team team, IReadOnlyList<MatchSubstitution> substitutions)
    {
        var count = substitutions.Count;
        var description = count switch
        {
            0 => $"{team.Name} make a halftime change.",
            1 => $"{team.Name} make a halftime change. {substitutions[0].PlayerOnName} replaces {substitutions[0].PlayerOffName}.",
            _ => $"{team.Name} make {count} changes at halftime. Fresh legs introduced at the break."
        };

        return CreateEvent(
            minute,
            EventType.Substitution,
            description,
            substitutions.FirstOrDefault()?.PlayerOnName,
            substitutions.FirstOrDefault()?.PlayerOffName);
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
            "training overload" => $"{playerName} is ruled out after an overloaded training week.",
            "training muscle strain" => $"{playerName} suffers a muscle strain in preparation.",
            "training knock" => $"{playerName} picks up a knock during training.",
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

    public MatchEvent CreatePenalty(int minute, Team team, Player player, bool converted, Match match, int scorerMatchGoals = 0)
    {
        var milestoneText = converted
            ? CreateGoalMilestoneText(player, team, scorerMatchGoals, new Random(minute + player.Name.Length))
            : string.Empty;
        var outcome = converted
            ? $"{player.Name} scores from the penalty spot for {team.Name}.{milestoneText}"
            : $"{player.Name}'s penalty for {team.Name} is saved.";

        var matchEvent = CreateEvent(
            minute,
            converted ? EventType.Goal : EventType.Save,
            $"{outcome} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            player.Name,
            match: match);
        matchEvent.ShotClassification = ShotClassification.Penalty;
        return matchEvent;
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
        Player? goalkeeper = null,
        int scorerMatchGoals = 0)
    {
        var milestoneText = converted
            ? CreateGoalMilestoneText(taker, attackingTeam, scorerMatchGoals, new Random(minute + taker.Name.Length))
            : string.Empty;
        var outcome = converted && triggeredTrait is not null
            ? $"{CreateTraitPenaltyScoreLine(attackingTeam, taker, triggeredTrait.Value, random: new Random(minute + taker.Name.Length))}{milestoneText}"
            : converted
            ? $"{taker.Name} scores from the penalty spot for {attackingTeam.Name}.{milestoneText}"
            : saved
                ? CreatePenaltySaveLine(attackingTeam, defendingTeam, taker, goalkeeper)
                : CreatePenaltyMissLine(attackingTeam, taker, new Random(minute + taker.Name.Length));
        var eventType = converted
            ? EventType.Goal
            : saved
                ? EventType.Save
                : EventType.Miss;

        var matchEvent = CreateEvent(
            minute,
            eventType,
            $"{outcome} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}",
            taker.Name,
            goalkeeper?.Name,
            match: match,
            triggeredTrait: triggeredTrait);
        matchEvent.ShotClassification = ShotClassification.Penalty;
        return matchEvent;
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

    public MatchEvent CreateDisallowedGoalOffside(int minute, Team team, Player scorer, Player? assister, Match match, Random random)
    {
        var description = Pick(random,
            $"Goal ruled out. {scorer.Name} was offside and the score returns to {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}.",
            $"OFFSIDE! {scorer.Name}'s goal is ruled out. The score returns to {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}.");

        return CreateEvent(minute, EventType.Offside, description, scorer.Name, assister?.Name, match);
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

    public MatchEvent CreateMistakeChance(
        int minute,
        Team attackingTeam,
        Team defendingTeam,
        Player attacker,
        Player mistakePlayer,
        string mistakeType,
        Random random)
    {
        var description = mistakeType == "keeper"
            ? Pick(random,
                $"{attacker.Name} reacts first to the loose ball for {attackingTeam.Name}.",
                $"{attackingTeam.Name} smell danger as {attacker.Name} pounces on the keeper's mistake.",
                $"The ball breaks loose in the box and {attacker.Name} is there for {attackingTeam.Name}.")
            : Pick(random,
                $"BIG CHANCE! {attacker.Name} pounces on {mistakePlayer.Name}'s mistake for {attackingTeam.Name}.",
                $"{attackingTeam.Name} break immediately after the error and {attacker.Name} has space.",
                $"{defendingTeam.Name} are exposed as {attacker.Name} seizes on the loose ball.");

        return CreateEvent(minute, EventType.Attack, description, attacker.Name, mistakePlayer.Name);
    }

    public MatchEvent CreateMistakePunishmentShot(
        int minute,
        Team attackingTeam,
        Player attacker,
        Player mistakePlayer,
        string mistakeType,
        Random random)
    {
        var description = mistakeType == "keeper"
            ? Pick(random,
                $"REBOUND SHOT! {attacker.Name} reacts first and fires at goal for {attackingTeam.Name}.",
                $"{attacker.Name} snaps at the loose ball after the keeper error for {attackingTeam.Name}.",
                $"{attacker.Name} stabs a quick effort through the scramble for {attackingTeam.Name}.")
            : Pick(random,
                $"{attacker.Name} turns the defensive error into a quick shot for {attackingTeam.Name}.",
                $"{attacker.Name} drives into the gap and shoots after the mistake for {attackingTeam.Name}.",
                $"{attackingTeam.Name} punish the loose touch as {attacker.Name} gets the shot away.");

        return CreateEvent(minute, EventType.Shot, description, attacker.Name, mistakePlayer.Name);
    }

    public MatchEvent CreateWonderGoal(int minute, Team team, Player player, Match match, PlayerTrait? triggeredTrait = null, int scorerMatchGoals = 0)
    {
        var random = new Random(minute + player.Name.Length);
        var description = triggeredTrait is not null
            ? $"{CreateTraitWonderGoalLine(team, player, triggeredTrait.Value, random)}{CreateGoalMilestoneText(player, team, scorerMatchGoals, random)} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}"
            : $"{player.Name} produces a wonder goal for {team.Name}.{CreateGoalMilestoneText(player, team, scorerMatchGoals, random)} Score: {match.HomeTeam.Name} {match.HomeScore} - {match.AwayScore} {match.AwayTeam.Name}";

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

    public MatchEvent CreateSetPieceShot(int minute, Team team, Player taker, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait == PlayerTrait.DeadBallSpecialist
            ? Pick(new Random(minute + taker.Name.Length), $"{taker.Name} curls the free kick with real dead-ball quality for {team.Name}.", $"{taker.Name} strikes the set piece cleanly for {team.Name}.")
            : $"{taker.Name} takes the free kick for {team.Name}.";

        var matchEvent = CreateEvent(minute, EventType.Shot, description, taker.Name, triggeredTrait: triggeredTrait);
        matchEvent.ShotClassification = ShotClassification.FreeKick;
        return matchEvent;
    }

    public MatchEvent CreateSetPieceDelivery(int minute, Team team, Player taker, Player target, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait == PlayerTrait.DeadBallSpecialist
            ? Pick(new Random(minute + taker.Name.Length + target.Name.Length),
                $"{taker.Name} whips a dangerous free kick toward {target.Name} for {team.Name}.",
                $"{taker.Name}'s delivery bends into {target.Name}'s path for {team.Name}.")
            : $"{taker.Name} delivers the free kick toward {target.Name} for {team.Name}.";

        return CreateEvent(minute, EventType.SetPieceDanger, description, taker.Name, target.Name, triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateCornerKick(int minute, Team team, Player taker, PlayerTrait? triggeredTrait = null)
    {
        var description = triggeredTrait switch
        {
            PlayerTrait.DeadBallSpecialist => Pick(new Random(minute + taker.Name.Length), $"{taker.Name} swings in a dangerous corner for {team.Name}.", $"{taker.Name}'s corner delivery drops into a threatening area for {team.Name}."),
            PlayerTrait.EarlyCrosser => $"{taker.Name} whips an early corner into the danger area for {team.Name}.",
            _ => $"{taker.Name} delivers the corner for {team.Name}."
        };

        return CreateEvent(
            minute,
            EventType.CornerKick,
            description,
            taker.Name,
            triggeredTrait: triggeredTrait);
    }

    public MatchEvent CreateConfrontation(int minute, Team team, Player player, Player? opponent = null, string reason = "")
    {
        var playerText = opponent is null
            ? player.Name
            : $"{player.Name} and {opponent.Name}";
        var reasonText = reason switch
        {
            "late tackle" => "after a late challenge",
            "dangerous foul" => "after a dangerous foul",
            "penalty incident" => "after the penalty incident",
            "rivalry tension" => "as the rivalry tension boils over",
            "aggressive player" => "after an aggressive challenge",
            "frustration" => "after frustration spills over",
            _ => "after a heated moment"
        };

        return CreateEvent(
            minute,
            EventType.Confrontation,
            Pick(
                new Random(minute + player.Name.Length + team.Name.Length),
                $"Players clash {reasonText}. {playerText} have to be separated.",
                $"Tempers flare {reasonText}; {playerText} are right in the middle of it.",
                $"The match is getting heated as {playerText} square up {reasonText}."),
            player.Name,
            opponent?.Name);
    }

    public MatchEvent CreateDoubleBooking(int minute, Player firstPlayer, Player secondPlayer)
    {
        return CreateEvent(
            minute,
            EventType.YellowCard,
            $"Both players go into the book after the confrontation: {firstPlayer.Name} and {secondPlayer.Name}.",
            firstPlayer.Name,
            secondPlayer.Name);
    }

    public MatchEvent CreateRefereeWarning(int minute, Player firstPlayer, Player? secondPlayer = null)
    {
        var playerText = secondPlayer is null
            ? firstPlayer.Name
            : $"{firstPlayer.Name} and {secondPlayer.Name}";

        return CreateEvent(
            minute,
            EventType.RefereeControversy,
            $"The referee warns {playerText} after the confrontation. No cards this time.",
            firstPlayer.Name,
            secondPlayer?.Name);
    }

    public MatchEvent CreateCrowdMomentum(int minute, Team team)
    {
        return CreateEvent(
            minute,
            EventType.CrowdMomentum,
            Pick(
                new Random(minute + team.Name.Length),
                $"The crowd are roaring now as {team.Name} try to ride the surge.",
                $"Noise levels rise after that moment. {team.Name} have the stadium behind them.",
                $"The stadium is fully behind {team.Name} after that swing in momentum.",
                $"{team.Name} feed off the noise as pressure builds around the stadium."));
    }

    public MatchEvent CreateHomeCrowdPressure(int minute, Team homeTeam, Team awayTeam, Random random)
    {
        var description = minute switch
        {
            <= 15 => Pick(random,
                $"The home crowd are roaring early. {awayTeam.Name} are trying to settle away from home.",
                $"Hostile away atmosphere tonight as {homeTeam.Name}'s support drives the opening pressure."),
            >= 80 => Pick(random,
                $"The home support are driving {homeTeam.Name} forward late on.",
                $"The home support sense a comeback as {homeTeam.Name} push again.",
                $"The stadium is fully behind {homeTeam.Name} in this late spell."),
            _ => Pick(random,
                $"The crowd are roaring now and {homeTeam.Name} respond.",
                $"Noise levels rise after that chance for {homeTeam.Name}.",
                $"{awayTeam.Name} are being made to work through a hostile spell.")
        };

        return CreateEvent(minute, EventType.CrowdMomentum, description);
    }

    public MatchEvent CreateExhaustion(int minute, Team team, Player player)
    {
        return CreateEvent(minute, EventType.Exhaustion, $"{player.Name} looks exhausted for {team.Name}. Their intensity drops.", player.Name);
    }

    public MatchEvent CreateAddedTimeAnnouncement(int minute, int addedMinutes, bool isSecondHalf)
    {
        var description = addedMinutes <= 0
            ? "No added time at the end of the half."
            : addedMinutes == 1
                ? "Minimum of 1 minute added."
                : isSecondHalf && addedMinutes >= 6
                    ? $"{NumberWord(addedMinutes)} minutes of added time to play."
                    : $"Minimum of {addedMinutes} minutes added.";

        return CreateEvent(minute, EventType.AddedTime, description);
    }

    public MatchEvent CreateTimeWasting(int minute, Team team, Random random)
    {
        return CreateEvent(
            minute,
            EventType.TimeWasting,
            Pick(
                random,
                $"The keeper takes his time with the restart for {team.Name}.",
                $"{team.Name} are trying to slow the game down.",
                $"{team.Name} take their time over the restart as the clock ticks.",
                $"A few extra seconds disappear as {team.Name} manage the tempo."));
    }

    public MatchEvent CreateWeatherAnnouncement(int minute, WeatherCondition weatherCondition)
    {
        var description = weatherCondition switch
        {
            WeatherCondition.Clear => "Clear skies tonight. Perfect football conditions.",
            WeatherCondition.Rainy => "Rain is falling and the surface is getting slick.",
            WeatherCondition.HeavyRain => "Heavy rain makes conditions difficult. Players may struggle to keep their footing.",
            WeatherCondition.Storm => "Thunderstorms around the stadium are disrupting visibility and long passing.",
            WeatherCondition.Windy => "A swirling wind is affecting long balls and crosses.",
            WeatherCondition.Foggy => "Fog rolls across the pitch and reactions could be a split second slower.",
            WeatherCondition.Snow => "Snow is falling. The cold surface could drain legs quickly.",
            WeatherCondition.Hot => "Hot and humid conditions are draining energy early.",
            WeatherCondition.Cold => "Cold air bites tonight. Players may need extra time to react sharply.",
            _ => "Clear skies tonight. Perfect football conditions."
        };

        return CreateEvent(minute, EventType.Weather, description, weatherCondition: weatherCondition);
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
            "foul buildup" => "VAR checking a possible foul in the build-up...",
            "handball buildup" => "VAR checking a possible handball in the build-up...",
            "handball penalty" => "VAR checking possible handball for a penalty...",
            "penalty" => "VAR checking the penalty decision...",
            "red card" => "VAR checking possible violent conduct...",
            "offside" => "VAR checking a tight offside call...",
            _ => "VAR review underway. The stadium waits."
        };

        return CreateEvent(minute, EventType.VarCheck, description);
    }

    public MatchEvent CreateVarDecision(
        int minute,
        Team team,
        string outcome,
        Match? match = null,
        Player? primaryPlayer = null,
        Player? secondaryPlayer = null)
    {
        var description = outcome switch
        {
            "goal disallowed offside" => $"Goal ruled out for offside. The score returns to {match?.HomeTeam.Name} {match?.HomeScore} - {match?.AwayScore} {match?.AwayTeam.Name}.",
            "goal disallowed foul" => $"Goal ruled out. Foul in the build-up by {team.Name}; the score returns to {match?.HomeTeam.Name} {match?.HomeScore} - {match?.AwayScore} {match?.AwayTeam.Name}.",
            "goal disallowed handball" => $"Goal ruled out. Handball in the build-up by {team.Name}; the score returns to {match?.HomeTeam.Name} {match?.HomeScore} - {match?.AwayScore} {match?.AwayTeam.Name}.",
            "goal disallowed" => $"Goal disallowed after VAR review. {team.Name} are pulled back.",
            "goal stands" => $"VAR confirms the goal for {team.Name}. The celebrations restart.",
            "penalty stands" => "Penalty decision stands after VAR review.",
            "no penalty" => "No penalty after review. Play continues.",
            "penalty overturned" => $"Penalty overturned after VAR review. No foul by {team.Name}.",
            "red card upgraded" => "Referee upgrades yellow to red after VAR review.",
            "red card cancelled" => $"Red card cancelled after VAR review. {team.Name} survive a huge moment.",
            "no red card" => "No red card. Play continues after VAR review.",
            "offside confirmed" => "Tight offside confirmed by VAR. The restart stands.",
            _ => "VAR review complete. Decision stands."
        };

        return CreateEvent(minute, EventType.VarDecision, description, primaryPlayer?.Name, secondaryPlayer?.Name, match);
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
            PlayerTrait.BigMatchPlayer => Pick(random,
                $"{playerName} embraces the big moment and drives {team.Name} forward.",
                $"{playerName} raises the tempo for {team.Name} when the pressure is highest."),
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

    private static string CreateTraitChanceDescription(
        Team team,
        Player chanceCreator,
        Player shooter,
        string chanceType,
        PlayerTrait trait,
        Random random)
    {
        var creatorName = GetDisplayName(chanceCreator.Name);
        var shooterName = GetDisplayName(shooter.Name);

        return trait switch
        {
            PlayerTrait.FinesseShot => Pick(random,
                $"{creatorName} opens his body and finds room for a finesse effort.",
                $"{creatorName} shapes the angle for a curling shot for {team.Name}."),
            PlayerTrait.LongShotTaker => Pick(random,
                $"{creatorName} spots space from distance for {team.Name}.",
                $"{creatorName} opens up a shooting lane outside the box for {team.Name}."),
            PlayerTrait.OutsideFootShot => Pick(random,
                $"{creatorName} shapes an outside-of-the-boot angle for {team.Name}.",
                $"{creatorName} creates room for a trivela-style effort."),
            PlayerTrait.PowerHeader => Pick(random,
                $"{creatorName} attacks the delivery and creates an aerial chance.",
                $"{creatorName} rises highest as {team.Name} threaten in the air."),
            PlayerTrait.AerialThreat => Pick(random,
                $"{creatorName}'s aerial presence causes chaos in the box.",
                $"{creatorName} dominates the air and creates danger for {team.Name}."),
            PlayerTrait.Flair => Pick(random,
                $"A moment of magic from {creatorName} opens space for {team.Name}.",
                $"{creatorName} uses a clever flick to create the shooting lane."),
            PlayerTrait.SpeedDribbler => Pick(random,
                $"{creatorName} bursts past defenders with raw pace for {team.Name}.",
                $"Rapid acceleration from {creatorName} creates separation before the strike."),
            PlayerTrait.Rapid => Pick(random,
                $"{creatorName} explodes into space and creates the chance for {team.Name}.",
                $"{creatorName} is too quick to catch as he breaks into shooting range."),
            PlayerTrait.TechnicalDribbler => Pick(random,
                $"{creatorName} shows tight control through traffic for {team.Name}.",
                $"Elegant dribbling from {creatorName} breaks the line and creates the chance."),
            PlayerTrait.Playmaker => Pick(random,
                $"{creatorName} spots {shooterName}'s run perfectly for {team.Name}.",
                $"A defense-splitting pass from {creatorName} unlocks the backline for {shooterName}."),
            PlayerTrait.LongPasser => Pick(random,
                $"{creatorName} picks a superb long diagonal and {shooterName} attacks the space.",
                $"Pinpoint long ball from {creatorName} finds {shooterName} instantly."),
            PlayerTrait.EarlyCrosser => Pick(random,
                $"{creatorName} delivers early and catches the defense sleeping for {team.Name}.",
                $"Quick delivery from {creatorName} flashes into the danger area for {shooterName}."),
            PlayerTrait.TeamPlayer => Pick(random,
                $"{creatorName} chooses the unselfish option and {shooterName} has the better chance.",
                $"Excellent teamwork in the final third sets up {shooterName} for {team.Name}."),
            PlayerTrait.TriesToBeatOffsideTrap => Pick(random,
                $"{creatorName} times the run perfectly and beats the line for {team.Name}.",
                $"{creatorName} bends the run cleverly to create the opening."),
            _ => string.Equals(chanceCreator.Name, shooter.Name, StringComparison.OrdinalIgnoreCase)
                ? $"{creatorName} creates a shooting chance for {team.Name}."
                : $"{creatorName} creates the chance for {shooterName} and {team.Name}."
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

    private static string? CreateTacticalBuildUpDescription(Team team, Player playmaker, Player? target, Random random)
    {
        var playmakerName = GetDisplayName(playmaker.Name);
        var targetName = target is null ? "the front line" : GetDisplayName(target.Name);

        if (team.Tactics.PressingIntensity >= 80 && random.NextDouble() < 0.45)
        {
            return Pick(random,
                $"High pressing from {team.Name} wins territory and {playmakerName} immediately looks for {targetName}.",
                $"{team.Name} counter-press aggressively, turning pressure into a quick attack through {playmakerName}.");
        }

        if (team.Tactics.Tempo >= 78 && random.NextDouble() < 0.45)
        {
            return Pick(random,
                $"{team.Name} attack at a furious tempo as {playmakerName} releases {targetName} early.",
                $"{team.Name} go direct, moving the ball forward before the defense can settle.");
        }

        if (team.Tactics.Width >= 75 && random.NextDouble() < 0.45)
        {
            return Pick(random,
                $"{team.Name} stretch the pitch wide and {playmakerName} switches play toward {targetName}.",
                $"{team.Name}'s wide shape opens the flank for {targetName}.");
        }

        if (team.Tactics.Width <= 30 && random.NextDouble() < 0.45)
        {
            return Pick(random,
                $"{team.Name} overload the middle with narrow combinations around {playmakerName}.",
                $"{team.Name} keep the shape compact and combine through central lanes.");
        }

        if (team.Tactics.Mentality == Mentality.AllOutAttack && random.NextDouble() < 0.50)
        {
            return $"{team.Name} flood players forward, chasing the next chance with all-out attacking intent.";
        }

        if (team.Tactics.Mentality == Mentality.UltraDefensive && random.NextDouble() < 0.45)
        {
            return $"{team.Name} break carefully from a deep block, with {playmakerName} choosing the safest forward pass.";
        }

        return null;
    }

    private static string? CreateTacticalPossessionLossDescription(
        Team attackingTeam,
        Team defendingTeam,
        string attackerName,
        string defenderName,
        EventType reasonType,
        Random random)
    {
        if (defendingTeam.Tactics.PressingIntensity >= 80 && reasonType is EventType.Pressure or EventType.Tackle or EventType.Interception)
        {
            return Pick(random,
                $"{defendingTeam.Name}'s constant pressure traps {attackerName} and forces the turnover.",
                $"{defendingTeam.Name} swarm the ball, with {defenderName} turning the press into possession.");
        }

        if (defendingTeam.Tactics.DefensiveLine <= 30 && reasonType is EventType.BlockedPass or EventType.Interception)
        {
            return Pick(random,
                $"{defendingTeam.Name}'s deep block closes the lane and frustrates {attackingTeam.Name}.",
                $"{defendingTeam.Name} stay compact, blocking {attackerName}'s route through.");
        }

        if (attackingTeam.Tactics.Tempo >= 80 && reasonType is EventType.BadPass or EventType.Miscontrol)
        {
            return Pick(random,
                $"{attackingTeam.Name}'s very fast tempo becomes rushed as {attackerName} gives it away.",
                $"{attackerName} cannot execute the high-speed move and {defendingTeam.Name} pounce.");
        }

        return null;
    }

    private static MatchEvent CreateEvent(
        int minute,
        EventType eventType,
        string description,
        string? primaryPlayerName = null,
        string? secondaryPlayerName = null,
        Match? match = null,
        PlayerTrait? triggeredTrait = null,
        WeatherCondition? weatherCondition = null)
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
            WeatherCondition = weatherCondition,
            Description = description
        };
    }

    private static ShotClassification ClassifyShot(string shotContext, string description)
    {
        var text = $"{shotContext} {description}";
        if (ContainsAny(text, "penalty"))
        {
            return ShotClassification.Penalty;
        }

        if (ContainsAny(text, "heads", "header", "nods", "glances", "powers a header", "flicks on", "headed"))
        {
            return ShotClassification.Header;
        }

        if (ContainsAny(text, "volley", "volleys", "half-volley"))
        {
            return ShotClassification.Volley;
        }

        if (ContainsAny(text, "free kick", "free-kick", "set piece", "set-piece"))
        {
            return ShotClassification.FreeKick;
        }

        if (ContainsAny(text, "long-range", "long range", "from distance", "from range"))
        {
            return ShotClassification.LongShot;
        }

        return ShotClassification.Standard;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string Pick(Random random, params string[] options)
    {
        if (options.Length == 0)
        {
            return string.Empty;
        }

        return options[random.Next(options.Length)];
    }

    private static string NumberWord(int number)
    {
        return number switch
        {
            1 => "One",
            2 => "Two",
            3 => "Three",
            4 => "Four",
            5 => "Five",
            6 => "Six",
            7 => "Seven",
            8 => "Eight",
            9 => "Nine",
            10 => "Ten",
            11 => "Eleven",
            12 => "Twelve",
            _ => number.ToString()
        };
    }

    private static string CreateGoalMilestoneText(Player scorer, Team team, int scorerMatchGoals, Random random)
    {
        if (scorerMatchGoals == 3)
        {
            return Pick(random,
                $" HAT-TRICK! {GetDisplayName(scorer.Name)} completes his hat-trick for {team.Name}. That is his third goal of the match.",
                $" That's three for {GetDisplayName(scorer.Name)}! A clinical hat-trick. That is his third goal of the match.",
                $" The match ball is his - {GetDisplayName(scorer.Name)} has a hat-trick. That is his third goal of the match.");
        }

        if (scorerMatchGoals >= 4)
        {
            return $" {scorerMatchGoals} goals for {GetDisplayName(scorer.Name)}. What an extraordinary scoring display.";
        }

        if (scorerMatchGoals == 2 && random.NextDouble() < 0.55)
        {
            return $" That's a brace for {GetDisplayName(scorer.Name)}.";
        }

        return string.Empty;
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
            !string.Equals(preferred.Name, actor.Name, StringComparison.OrdinalIgnoreCase) &&
            IsOutfieldPlayer(preferred))
        {
            return preferred;
        }

        var alternatives = team.Players
            .Where(candidate =>
                !string.Equals(candidate.Name, actor.Name, StringComparison.OrdinalIgnoreCase) &&
                IsOutfieldPlayer(candidate))
            .ToList();

        if (alternatives.Count == 0)
        {
            alternatives = team.Players
                .Where(candidate => !string.Equals(candidate.Name, actor.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (alternatives.Count == 0)
        {
            return null;
        }

        return alternatives[random.Next(alternatives.Count)];
    }

    private static Player? ResolveFirstDistinctOutfieldTeammate(Team team, Player actor)
    {
        return team.Players.FirstOrDefault(candidate =>
                !string.Equals(candidate.Name, actor.Name, StringComparison.OrdinalIgnoreCase) &&
                IsOutfieldPlayer(candidate)) ??
            team.Players.FirstOrDefault(candidate =>
                !string.Equals(candidate.Name, actor.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOutfieldPlayer(Player player)
    {
        return !PositionSuitabilityService.IsGoalkeeperCapable(player);
    }

    private static string? GetAssistCandidateName(Player shooter, Player? chanceCreator)
    {
        return chanceCreator is not null &&
            !string.Equals(chanceCreator.Name, shooter.Name, StringComparison.OrdinalIgnoreCase)
                ? chanceCreator.Name
                : null;
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
