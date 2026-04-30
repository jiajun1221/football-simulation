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
            injuryCandidate.IsInjured = true;
            injuryCandidate.Stamina = 0;

            return new MatchDramaResult
            {
                EventType = EventType.Injury,
                Team = GetTeamForPlayer(context, injuryCandidate),
                Player = injuryCandidate,
                HomeAttackModifier = injuryCandidate.TeamIs(context.HomeTeam) ? 0.88 : 1.0,
                AwayAttackModifier = injuryCandidate.TeamIs(context.AwayTeam) ? 0.88 : 1.0,
                HomeDefenseModifier = injuryCandidate.TeamIs(context.HomeTeam) ? 0.90 : 1.0,
                AwayDefenseModifier = injuryCandidate.TeamIs(context.AwayTeam) ? 0.90 : 1.0
            };
        }

        if (ShouldCreateWonderGoal(context))
        {
            var team = ChooseStrongerAttack(context);
            return new MatchDramaResult
            {
                EventType = EventType.WonderGoal,
                Team = team,
                Player = ChooseAttackingPlayer(team, context.Random),
                ScoresGoal = true
            };
        }

        if (ShouldCreatePenalty(context))
        {
            var team = ChooseStrongerAttack(context);
            var taker = ChooseAttackingPlayer(team, context.Random);
            var converted = context.Random.NextDouble() < GetPenaltyConversionChance(taker);

            return new MatchDramaResult
            {
                EventType = EventType.Penalty,
                Team = team,
                Player = taker,
                ScoresGoal = converted,
                PenaltyConverted = converted
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
        var averageFatigue = context.HomeTeam.Players.Concat(context.AwayTeam.Players).Average(player => 100 - player.Stamina);
        var averagePressing = (context.HomeTeam.Tactics.PressingIntensity + context.AwayTeam.Tactics.PressingIntensity) / 2.0;
        var lateGameBonus = context.Minute >= 70 ? 0.03 : 0.0;

        return Math.Clamp(0.035 + averageFatigue / 900.0 + Math.Max(0, averagePressing - 60) / 700.0 + lateGameBonus, 0.02, 0.18);
    }

    private static Player? FindTiredPlayer(MatchEventContext context)
    {
        return context.HomeTeam.Players
            .Concat(context.AwayTeam.Players)
            .Where(player => player.Stamina < 45)
            .OrderBy(player => player.Stamina)
            .FirstOrDefault();
    }

    private static Player? FindInjuryCandidate(MatchEventContext context)
    {
        return context.HomeTeam.Players
            .Concat(context.AwayTeam.Players)
            .Where(player => !player.IsInjured &&
                (player.Traits.Contains(PlayerTrait.InjuryProne) ||
                player.Stamina <= 25 ||
                GetStaminaRatio(player) < 0.30 ||
                player.MatchesPlayedRecently >= 4))
            .OrderByDescending(player => (100.0 - player.Stamina) + (1.0 - GetStaminaRatio(player)) * 30 + player.MatchesPlayedRecently * 8)
            .FirstOrDefault();
    }

    private static double GetInjuryProbability(Player player)
    {
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

        return Math.Clamp(0.16 + lowStaminaBonus + fatigueBonus, 0.10, 0.32);
    }

    private static double GetStaminaRatio(Player player)
    {
        if (player.Stamina <= 0)
        {
            return 0;
        }

        return Math.Clamp(player.Stamina / 100.0, 0.0, 1.0);
    }

    private static bool ShouldCreateWonderGoal(MatchEventContext context)
    {
        var players = context.HomeTeam.Players.Concat(context.AwayTeam.Players);
        return players.Any(player => player.Traits.Contains(PlayerTrait.LongShotTaker) || player.Traits.Contains(PlayerTrait.BigMatchPlayer))
            && context.Random.NextDouble() < 0.22;
    }

    private static bool ShouldCreatePenalty(MatchEventContext context)
    {
        var aggressiveDefenders = context.HomeTeam.Players.Concat(context.AwayTeam.Players)
            .Count(player => player.Traits.Contains(PlayerTrait.AggressiveTackler));

        return context.Random.NextDouble() < 0.12 + aggressiveDefenders * 0.015;
    }

    private static bool ShouldCreateDefensiveError(MatchEventContext context)
    {
        var highLineRisk = Math.Max(context.HomeTeam.Tactics.DefensiveLine, context.AwayTeam.Tactics.DefensiveLine);
        return context.Random.NextDouble() < 0.18 + Math.Max(0, highLineRisk - 65) / 300.0;
    }

    private static MatchDramaResult CreateAtmosphereEvent(MatchEventContext context)
    {
        var roll = context.Random.NextDouble();
        if (roll < 0.18)
        {
            var attackingTeam = ChooseStrongerAttack(context);
            return new MatchDramaResult { EventType = EventType.Offside, Team = attackingTeam, Player = ChooseAttackingPlayer(attackingTeam, context.Random) };
        }

        if (roll < 0.34)
        {
            var defendingTeam = ChooseWeakerDefense(context);
            return new MatchDramaResult { EventType = EventType.GoalkeeperHeroics, Team = defendingTeam, Player = ChooseGoalkeeper(defendingTeam) };
        }

        if (roll < 0.52)
        {
            var attackingTeam = ChooseStrongerAttack(context);
            return new MatchDramaResult { EventType = EventType.SetPieceDanger, Team = attackingTeam, Player = ChooseSetPiecePlayer(attackingTeam, context.Random), HomeAttackModifier = 1.06, AwayAttackModifier = 1.06 };
        }

        if (roll < 0.70)
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
        var players = team.Players
            .Where(player => player.Position is Position.Forward or Position.Midfielder)
            .OrderByDescending(player => player.Attack + player.Finishing + player.CurrentForm)
            .Take(4)
            .ToList();

        return players.Count == 0 ? team.Players[random.Next(team.Players.Count)] : players[random.Next(players.Count)];
    }

    private static Player ChooseDefensivePlayer(Team team, Random random)
    {
        var players = team.Players
            .Where(player => player.Position is Position.Defender or Position.Midfielder)
            .OrderByDescending(player => player.Defense)
            .Take(5)
            .ToList();

        return players.Count == 0 ? team.Players[random.Next(team.Players.Count)] : players[random.Next(players.Count)];
    }

    private static Player ChooseSetPiecePlayer(Team team, Random random)
    {
        var player = team.Players
            .Where(candidate => candidate.Traits.Contains(PlayerTrait.SetPieceSpecialist) || candidate.Traits.Contains(PlayerTrait.AerialThreat))
            .OrderByDescending(candidate => candidate.Passing + candidate.Finishing)
            .FirstOrDefault();

        return player ?? ChooseAttackingPlayer(team, random);
    }

    private static Player ChooseGoalkeeper(Team team)
    {
        return team.Players.FirstOrDefault(player => player.Position == Position.Goalkeeper) ?? team.Players[0];
    }

    private static double GetPenaltyConversionChance(Player player)
    {
        var traitBonus = player.Traits.Contains(PlayerTrait.ClinicalFinisher) ? 0.08 : 0.0;
        return Math.Clamp(0.68 + player.Finishing / 450.0 + traitBonus, 0.60, 0.88);
    }

    private static Team GetTeamForPlayer(MatchEventContext context, Player player)
    {
        return context.HomeTeam.Players.Contains(player) ? context.HomeTeam : context.AwayTeam;
    }
}

internal static class PlayerTeamExtensions
{
    public static bool TeamIs(this Player player, Team team)
    {
        return team.Players.Contains(player);
    }
}
