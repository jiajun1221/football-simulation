using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public class TeamStrengthCalculator
{
    private readonly TacticalImpactCalculator _tacticalImpactCalculator;

    public TeamStrengthCalculator()
        : this(new TacticalImpactCalculator())
    {
    }

    public TeamStrengthCalculator(TacticalImpactCalculator tacticalImpactCalculator)
    {
        _tacticalImpactCalculator = tacticalImpactCalculator;
    }

    public double CalculateAttackStrength(Team team, Team? opponent = null)
    {
        var availablePlayers = GetAvailablePlayers(team).ToList();
        var baseAttackStrength = availablePlayers.Average(player => player.Position switch
        {
            Position.Forward => (GetEffectiveAttack(player) + GetEffectiveFinishing(player)) / 2.0 * 1.30,
            Position.Midfielder => (GetEffectiveAttack(player) + GetEffectivePassing(player)) / 2.0 * 1.10,
            Position.Defender => GetEffectiveAttack(player) * 0.70,
            Position.Goalkeeper => GetEffectiveAttack(player) * 0.30,
            _ => GetEffectiveAttack(player)
        });

        return baseAttackStrength
            * FormationModifiers.GetAttackModifier(team.Formation)
            * _tacticalImpactCalculator.GetAttackModifier(team, opponent)
            * GetShortHandedModifier(availablePlayers.Count);
    }

    public double CalculateDefenseStrength(Team team, Team? opponent = null)
    {
        var availablePlayers = GetAvailablePlayers(team).ToList();
        var baseDefenseStrength = availablePlayers.Average(player => player.Position switch
        {
            Position.Goalkeeper => GetEffectiveDefense(player) * 1.40,
            Position.Defender => GetEffectiveDefense(player) * 1.20,
            Position.Midfielder => GetEffectiveDefense(player) * 0.90,
            Position.Forward => GetEffectiveDefense(player) * 0.40,
            _ => GetEffectiveDefense(player)
        });

        return baseDefenseStrength
            * FormationModifiers.GetDefenseModifier(team.Formation)
            * _tacticalImpactCalculator.GetDefenseModifier(team, opponent)
            * GetShortHandedModifier(availablePlayers.Count);
    }

    public IEnumerable<Player> GetAvailablePlayers(Team team)
    {
        var availablePlayers = team.Players.Where(player => !player.IsSentOff && !player.IsSuspended).ToList();
        return availablePlayers.Count > 0 ? availablePlayers : team.Players;
    }

    public double GetEffectiveAttack(Player player)
    {
        var traitModifier = GetTraitModifier(player, PlayerTrait.PaceMerchant, PlayerTrait.BigMatchPlayer);
        return player.Attack * GetStaminaModifier(player) * GetStatusModifier(player) * traitModifier;
    }

    public double GetEffectiveDefense(Player player)
    {
        var traitModifier = GetTraitModifier(player, PlayerTrait.AggressiveTackler, PlayerTrait.AerialThreat);
        return player.Defense * GetStaminaModifier(player) * GetStatusModifier(player) * traitModifier;
    }

    public double GetEffectivePassing(Player player)
    {
        var traitModifier = GetTraitModifier(player, PlayerTrait.Playmaker, PlayerTrait.PressResistant, PlayerTrait.SetPieceSpecialist);
        return player.Passing * GetStaminaModifier(player) * GetStatusModifier(player) * traitModifier;
    }

    public double GetEffectiveFinishing(Player player)
    {
        var traitModifier = GetTraitModifier(player, PlayerTrait.ClinicalFinisher, PlayerTrait.LongShotTaker, PlayerTrait.AerialThreat);
        return player.Finishing * GetStaminaModifier(player) * GetStatusModifier(player) * traitModifier;
    }

    public double GetPlaymakerWeight(Player player)
    {
        var positionModifier = player.Position switch
        {
            Position.Midfielder => 1.20,
            Position.Forward => 1.00,
            Position.Defender => 0.60,
            Position.Goalkeeper => 0.20,
            _ => 1.00
        };

        return (GetEffectivePassing(player) * 0.60 + GetEffectiveAttack(player) * 0.40) * positionModifier;
    }

    public double GetShooterWeight(Player player)
    {
        var positionModifier = player.Position switch
        {
            Position.Forward => 1.25,
            Position.Midfielder => 1.00,
            Position.Defender => 0.50,
            Position.Goalkeeper => 0.10,
            _ => 1.00
        };

        return (GetEffectiveAttack(player) * 0.45 + GetEffectiveFinishing(player) * 0.55) * positionModifier;
    }

    private static double GetStaminaModifier(Player player)
    {
        if (player.Stamina <= 0)
        {
            return MatchConstants.MinimumStaminaModifier;
        }

        var staminaRatio = player.CurrentStamina / player.Stamina;

        const double fatigueModifier = 1.0;

        return Math.Clamp(
            staminaRatio,
            MatchConstants.MinimumStaminaModifier,
            MatchConstants.MaximumStaminaModifier) * fatigueModifier;
    }

    private static double GetStatusModifier(Player player)
    {
        var fatigueModifier = GetFatiguePerformanceModifier(player.Fatigue);
        var formModifier = Math.Clamp(0.80 + (player.CurrentForm / 250.0), 0.80, 1.20);
        var moraleModifier = Math.Clamp(0.85 + (player.Morale / 300.0), 0.85, 1.15);
        var injuryModifier = player.IsInjured ? 0.55 : 1.00;
        var suspensionModifier = player.IsSuspended ? 0.40 : 1.00;

        return fatigueModifier * formModifier * moraleModifier * injuryModifier * suspensionModifier;
    }

    private static double GetFatiguePerformanceModifier(int fatigue)
    {
        return fatigue switch
        {
            > 95 => 0.55,
            > 85 => 0.68,
            > 70 => 0.78,
            > 50 => 0.90,
            _ => 1.00
        };
    }

    private static double GetTraitModifier(Player player, params PlayerTrait[] matchingTraits)
    {
        var traitCount = matchingTraits.Count(player.Traits.Contains);
        return 1.0 + traitCount * 0.05;
    }

    private static double GetShortHandedModifier(int availablePlayerCount)
    {
        return Math.Clamp(availablePlayerCount / 11.0, 0.60, 1.00);
    }
}
