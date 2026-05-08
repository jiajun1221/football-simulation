using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public class MatchDramaService
{
    public MatchDramaResult? TryCreateDramaEvent(MatchEventContext context)
    {
        var chance = CalculateDramaChance(context);
        if (context.Random.NextDouble() > chance)
        {
            return null;
        }

        var tiredPlayer = FindTiredPlayer(context);
        if (tiredPlayer is not null && context.Random.NextDouble() < 0.24)
        {
            tiredPlayer.Stamina = Math.Max(0, tiredPlayer.Stamina - 18);

            return new MatchDramaResult
            {
                EventType = EventType.Exhaustion,
                Team = GetTeamForPlayer(context, tiredPlayer),
                Player = tiredPlayer,
                HomeAttackModifier = tiredPlayer.TeamIs(context.HomeTeam) ? 0.92 : 1.0,
                AwayAttackModifier = tiredPlayer.TeamIs(context.AwayTeam) ? 0.92 : 1.0
            };
        }

        var injuryCandidate = FindInjuryCandidate(context);
        if (injuryCandidate is not null && context.Random.NextDouble() < GetInjuryProbability(injuryCandidate))
        {
            var injuryCause = ChooseInjuryCause(context, injuryCandidate);
            ApplyMatchInjury(injuryCandidate, injuryCause, context.Random);

            return new MatchDramaResult
            {
                EventType = EventType.Injury,
                Team = GetTeamForPlayer(context, injuryCandidate),
                Player = injuryCandidate,
                InjuryCause = injuryCause,
                HomeAttackModifier = injuryCandidate.TeamIs(context.HomeTeam) ? 0.78 : 1.0,
                AwayAttackModifier = injuryCandidate.TeamIs(context.AwayTeam) ? 0.78 : 1.0,
                HomeDefenseModifier = injuryCandidate.TeamIs(context.HomeTeam) ? 0.80 : 1.0,
                AwayDefenseModifier = injuryCandidate.TeamIs(context.AwayTeam) ? 0.80 : 1.0
            };
        }

        if (ShouldCreateDefensiveError(context))
        {
            var defendingTeam = ChooseWeakerDefense(context);
            var isHomeError = defendingTeam == context.HomeTeam;
            return new MatchDramaResult
            {
                EventType = EventType.DefensiveError,
                Team = defendingTeam,
                Player = ChooseDefensivePlayer(defendingTeam, context.Random),
                HomeDefenseModifier = isHomeError ? 0.85 : 1.0,
                AwayDefenseModifier = isHomeError ? 1.0 : 0.85
            };
        }

        return CreateAtmosphereEvent(context);
    }

    private static double CalculateDramaChance(MatchEventContext context)
    {
        var activePlayers = GetActivePlayers(context.HomeTeam)
            .Concat(GetActivePlayers(context.AwayTeam))
            .ToList();
        var averageFatigue = activePlayers.Count == 0
            ? 0
            : activePlayers.Average(player => 100 - player.Stamina);
        var averagePressing = (context.HomeTeam.Tactics.PressingIntensity + context.AwayTeam.Tactics.PressingIntensity) / 2.0;
        var lateGameBonus = context.Minute >= 70 ? 0.03 : 0.0;

        return Math.Clamp(0.035 + averageFatigue / 900.0 + Math.Max(0, averagePressing - 60) / 700.0 + lateGameBonus, 0.02, 0.18);
    }

    private static Player? FindTiredPlayer(MatchEventContext context)
    {
        return context.HomeTeam.Players
            .Concat(context.AwayTeam.Players)
            .Where(IsActivePlayer)
            .Where(player => player.Stamina < 45)
            .OrderBy(player => player.Stamina)
            .FirstOrDefault();
    }

    private static Player? FindInjuryCandidate(MatchEventContext context)
    {
        var pressingRisk = Math.Max(context.HomeTeam.Tactics.PressingIntensity, context.AwayTeam.Tactics.PressingIntensity);
        return context.HomeTeam.Players
            .Concat(context.AwayTeam.Players)
            .Where(player => IsActivePlayer(player) && !player.IsInjured)
            .Select(player => new
            {
                Player = player,
                Risk = GetPlayerInjuryRisk(player) + Math.Max(0, pressingRisk - 62) * 0.18
            })
            .Where(candidate => candidate.Risk >= 18)
            .OrderByDescending(candidate => candidate.Risk)
            .Select(candidate => candidate.Player)
            .FirstOrDefault();
    }

    private static double GetInjuryProbability(Player player)
    {
        var traitModifier = player.Traits.Contains(PlayerTrait.InjuryProne)
            ? 1.45
            : player.Traits.Contains(PlayerTrait.Engine)
                ? 0.55
                : 1.0;
        var lowStaminaBonus = GetStaminaRatio(player) switch
        {
            < 0.18 => 0.08,
            < 0.30 => 0.04,
            _ => 0.0
        };

        var fatigueBonus = player.Stamina switch
        {
            <= 5 => 0.08,
            <= 15 => 0.04,
            _ => 0.0
        };

        var repeatedLoadBonus = player.MatchesPlayedRecently >= 4 ? 0.010 : 0.0;
        return Math.Clamp((0.010 + lowStaminaBonus + fatigueBonus + repeatedLoadBonus) * traitModifier, 0.004, 0.085);
    }

    private static double GetPlayerInjuryRisk(Player player)
    {
        var lowStaminaRisk = Math.Max(0.0, 55.0 - player.Stamina) * 0.9;
        var traitRisk = player.Traits.Contains(PlayerTrait.InjuryProne) ? 26.0 : 0.0;
        var workloadRisk = Math.Max(0, player.MatchesPlayedRecently - 2) * 8.0;
        var duelRisk =
            (player.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 5.0 : 0.0) +
            (player.Traits.Contains(PlayerTrait.Rapid) || player.Traits.Contains(PlayerTrait.SpeedDribbler) ? 4.0 : 0.0) +
            (player.Traits.Contains(PlayerTrait.PowerHeader) || player.Traits.Contains(PlayerTrait.AerialThreat) ? 3.0 : 0.0);

        return lowStaminaRisk + traitRisk + workloadRisk + duelRisk;
    }

    private static string ChooseInjuryCause(MatchEventContext context, Player player)
    {
        if (player.Position == Position.Goalkeeper)
        {
            return "goalkeeper collision";
        }

        if (player.Stamina <= 20)
        {
            return context.Random.NextDouble() < 0.55 ? "over exhaustion" : "sprint muscle pull";
        }

        var opposingTeam = GetOpposingTeam(context, player);
        var dangerousTackleRisk = opposingTeam.Players
            .Count(candidate => IsActivePlayer(candidate) && candidate.Traits.Contains(PlayerTrait.DivesIntoTackles));

        if (dangerousTackleRisk > 0 && context.Random.NextDouble() < 0.28)
        {
            return "dangerous tackle";
        }

        if (player.Traits.Contains(PlayerTrait.PowerHeader) || player.Traits.Contains(PlayerTrait.AerialThreat))
        {
            return "aerial duel impact";
        }

        return context.Random.NextDouble() switch
        {
            < 0.32 => "heavy collision",
            < 0.54 => "awkward landing",
            < 0.76 => "sprint muscle pull",
            _ => "aerial duel impact"
        };
    }

    private static Team GetOpposingTeam(MatchEventContext context, Player player)
    {
        return player.TeamIs(context.HomeTeam) ? context.AwayTeam : context.HomeTeam;
    }

    private static void ApplyMatchInjury(Player player, string injuryCause, Random random)
    {
        var severity = ChooseInjurySeverity(player, injuryCause, random);
        player.IsInjured = true;
        player.NewlyInjuredThisMatch = true;
        player.InjurySeverity = severity;
        player.InjuryType = ChooseInjuryType(injuryCause, severity, random);
        player.IsSeasonEndingInjury = severity == InjurySeverity.SeasonEnding;
        player.InjuryRecoveryMatches = severity switch
        {
            InjurySeverity.Minor => random.Next(1, 4),
            InjurySeverity.Moderate => random.Next(4, 11),
            InjurySeverity.Serious => random.Next(15, 31),
            InjurySeverity.SeasonEnding => 99,
            _ => 1
        };
        player.Stamina = 0;
        player.LiveMatchModifier = 0.25;
    }

    private static InjurySeverity ChooseInjurySeverity(Player player, string injuryCause, Random random)
    {
        var seriousBonus =
            (injuryCause is "dangerous tackle" or "goalkeeper collision" or "aerial duel impact" ? 0.06 : 0.0) +
            (player.Traits.Contains(PlayerTrait.InjuryProne) ? 0.04 : 0.0);
        var roll = random.NextDouble();

        if (roll < 0.004 + seriousBonus / 10.0)
        {
            return InjurySeverity.SeasonEnding;
        }

        if (roll < 0.08 + seriousBonus)
        {
            return InjurySeverity.Serious;
        }

        if (roll < 0.34 + seriousBonus)
        {
            return InjurySeverity.Moderate;
        }

        return InjurySeverity.Minor;
    }

    private static string ChooseInjuryType(string injuryCause, InjurySeverity severity, Random random)
    {
        if (severity == InjurySeverity.SeasonEnding)
        {
            return random.NextDouble() < 0.5 ? "ACL Injury" : "Fracture";
        }

        return injuryCause switch
        {
            "dangerous tackle" => random.NextDouble() < 0.55 ? "Ankle Injury" : "Knee Injury",
            "goalkeeper collision" => random.NextDouble() < 0.5 ? "Shoulder Injury" : "Head Injury",
            "aerial duel impact" => random.NextDouble() < 0.5 ? "Head Injury" : "Back Injury",
            "sprint muscle pull" => random.NextDouble() < 0.65 ? "Hamstring Injury" : "Calf Strain",
            "over exhaustion" => random.NextDouble() < 0.5 ? "Muscle Fatigue" : "Groin Strain",
            "awkward landing" => random.NextDouble() < 0.5 ? "Ankle Injury" : "Knee Injury",
            _ => random.NextDouble() < 0.5 ? "Impact Injury" : "Knock"
        };
    }

    private static double GetStaminaRatio(Player player)
    {
        if (player.Stamina <= 0)
        {
            return 0;
        }

        return Math.Clamp(player.Stamina / 100.0, 0.0, 1.0);
    }

    private static bool ShouldCreateDefensiveError(MatchEventContext context)
    {
        var highLineRisk = Math.Max(context.HomeTeam.Tactics.DefensiveLine, context.AwayTeam.Tactics.DefensiveLine);
        return context.Random.NextDouble() < 0.18 + Math.Max(0, highLineRisk - 65) / 300.0;
    }

    private static MatchDramaResult CreateAtmosphereEvent(MatchEventContext context)
    {
        var roll = context.Random.NextDouble();
        if (roll < 0.28)
        {
            var defendingTeam = ChooseWeakerDefense(context);
            return new MatchDramaResult { EventType = EventType.GoalkeeperHeroics, Team = defendingTeam, Player = ChooseGoalkeeper(defendingTeam) };
        }

        if (roll < 0.58)
        {
            var team = context.HomeTeam.Tactics.PressingIntensity >= context.AwayTeam.Tactics.PressingIntensity ? context.HomeTeam : context.AwayTeam;
            return new MatchDramaResult { EventType = EventType.Confrontation, Team = team, Player = ChooseDefensivePlayer(team, context.Random) };
        }

        return new MatchDramaResult { EventType = EventType.CrowdMomentum, Team = context.HomeTeam, HomeAttackModifier = 1.08 };
    }

    private static Team ChooseStrongerAttack(MatchEventContext context)
    {
        var total = context.HomeAttackStrength + context.AwayAttackStrength;
        if (total <= 0)
        {
            return context.Random.Next(0, 2) == 0 ? context.HomeTeam : context.AwayTeam;
        }

        return context.Random.NextDouble() < context.HomeAttackStrength / total ? context.HomeTeam : context.AwayTeam;
    }

    private static Team ChooseWeakerDefense(MatchEventContext context)
    {
        return context.HomeDefenseStrength <= context.AwayDefenseStrength ? context.HomeTeam : context.AwayTeam;
    }

    private static Player ChooseAttackingPlayer(Team team, Random random)
    {
        var players = GetActivePlayers(team)
            .Where(player => player.Position is Position.Forward or Position.Midfielder)
            .OrderByDescending(player => player.Attack + player.Finishing + player.CurrentForm)
            .Take(4)
            .ToList();

        var activePlayers = GetActivePlayers(team);
        return players.Count == 0 ? activePlayers[random.Next(activePlayers.Count)] : players[random.Next(players.Count)];
    }

    private static Player ChooseDefensivePlayer(Team team, Random random)
    {
        var players = GetActivePlayers(team)
            .Where(player => player.Position is Position.Defender or Position.Midfielder)
            .OrderByDescending(player => player.Defense)
            .Take(5)
            .ToList();

        var activePlayers = GetActivePlayers(team);
        return players.Count == 0 ? activePlayers[random.Next(activePlayers.Count)] : players[random.Next(players.Count)];
    }

    private static Player ChooseSetPiecePlayer(Team team, Random random)
    {
        var player = GetActivePlayers(team)
            .Where(candidate => candidate.Traits.Contains(PlayerTrait.DeadBallSpecialist) || candidate.Traits.Contains(PlayerTrait.AerialThreat))
            .OrderByDescending(candidate => candidate.Passing + candidate.Finishing)
            .FirstOrDefault();

        return player ?? ChooseAttackingPlayer(team, random);
    }

    private static Player ChooseGoalkeeper(Team team)
    {
        var activePlayers = GetActivePlayers(team);
        return activePlayers.FirstOrDefault(player => player.Position == Position.Goalkeeper) ??
            activePlayers.FirstOrDefault() ??
            team.Players.First();
    }

    private static Team GetTeamForPlayer(MatchEventContext context, Player player)
    {
        return context.HomeTeam.Players.Contains(player) ? context.HomeTeam : context.AwayTeam;
    }

    private static List<Player> GetActivePlayers(Team team)
    {
        var activePlayers = team.Players
            .Where(IsActivePlayer)
            .ToList();

        return activePlayers.Count > 0
            ? activePlayers
            : team.Players.Where(player => !player.IsSentOff).ToList();
    }

    private static bool IsActivePlayer(Player player)
    {
        return player.IsOnPitch && !player.IsSentOff;
    }
}

internal static class PlayerTeamExtensions
{
    public static bool TeamIs(this Player player, Team team)
    {
        return team.Players.Contains(player);
    }
}
