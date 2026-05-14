using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class TacticalProfileService
{
    private static readonly IReadOnlyList<TacticalOption> MentalityOptions =
    [
        new(TacticalDimension.Mentality, "ultra-defensive", "Ultra Defensive", "🧠", "Deep defensive block with very few risks going forward.", 10, Mentality.UltraDefensive),
        new(TacticalDimension.Mentality, "defensive", "Defensive", "🧠", "Protect space first and attack with controlled numbers.", 30, Mentality.Defensive),
        new(TacticalDimension.Mentality, "balanced", "Balanced", "🧠", "Balanced approach between attack and defense.", 50, Mentality.Balanced),
        new(TacticalDimension.Mentality, "attacking", "Attacking", "🧠", "Commit extra players forward and look for regular chances.", 70, Mentality.Attacking),
        new(TacticalDimension.Mentality, "all-out-attack", "All Out Attack", "🧠", "Relentless forward runs, high risk, and heavy counter-attack exposure.", 90, Mentality.AllOutAttack)
    ];

    private static readonly IReadOnlyList<TacticalOption> PressingOptions =
    [
        new(TacticalDimension.PressingIntensity, "low-block", "Low Block", "⚔", "Defend deep, conserve energy, and wait for loose passes.", 10),
        new(TacticalDimension.PressingIntensity, "conservative", "Conservative", "⚔", "Press selectively without opening too much space.", 30),
        new(TacticalDimension.PressingIntensity, "normal", "Normal", "⚔", "Press in a balanced shape across midfield.", 50),
        new(TacticalDimension.PressingIntensity, "aggressive", "Aggressive", "⚔", "Aggressive pressing high up the pitch.", 70),
        new(TacticalDimension.PressingIntensity, "constant-pressure", "Constant Pressure", "⚔", "Suffocate the opponent, force mistakes, and accept huge fatigue cost.", 90)
    ];

    private static readonly IReadOnlyList<TacticalOption> WidthOptions =
    [
        new(TacticalDimension.Width, "very-narrow", "Very Narrow", "⛶", "Crowd central areas and overload short passing lanes.", 10),
        new(TacticalDimension.Width, "narrow", "Narrow", "⛶", "Play compact combinations through midfield.", 30),
        new(TacticalDimension.Width, "balanced", "Balanced", "⛶", "Keep balanced spacing across the pitch.", 50),
        new(TacticalDimension.Width, "wide", "Wide", "⛶", "Stretch play and create crossing opportunities.", 70),
        new(TacticalDimension.Width, "very-wide", "Very Wide", "⛶", "Isolate wingers, attack the byline, and flood the box.", 90)
    ];

    private static readonly IReadOnlyList<TacticalOption> TempoOptions =
    [
        new(TacticalDimension.Tempo, "very-slow", "Very Slow", "⚡", "Control possession patiently and reduce mistakes.", 10),
        new(TacticalDimension.Tempo, "slow", "Slow", "⚡", "Slow buildup with careful passing choices.", 30),
        new(TacticalDimension.Tempo, "balanced", "Balanced", "⚡", "Balanced tempo with controlled transitions.", 50),
        new(TacticalDimension.Tempo, "fast", "Fast", "⚡", "Move the ball quickly and attack directly.", 70),
        new(TacticalDimension.Tempo, "very-fast", "Very Fast", "⚡", "Chaotic transitions, direct attacks, and higher passing risk.", 90)
    ];

    private static readonly IReadOnlyList<TacticalOption> DefensiveLineOptions =
    [
        new(TacticalDimension.DefensiveLine, "deep", "Deep", "🛡", "Sit deeper, protect space behind, and invite longer shots.", 20),
        new(TacticalDimension.DefensiveLine, "standard", "Standard", "🛡", "Hold a balanced line with moderate space behind.", 45),
        new(TacticalDimension.DefensiveLine, "higher", "Higher", "🛡", "Push defenders higher to compress space.", 70),
        new(TacticalDimension.DefensiveLine, "high-press-line", "High Press Line", "🛡", "Very high line, aggressive offside trap, and major counter risk.", 90)
    ];

    private static readonly IReadOnlyList<TacticalPreset> Presets =
    [
        new("park-the-bus", "Park The Bus", "Ultra compact, deep, and hard to break down.", new TeamTactics
        {
            Mentality = Mentality.UltraDefensive,
            PressingIntensity = 18,
            Width = 24,
            Tempo = 24,
            DefensiveLine = 18
        }),
        new("tiki-taka", "Tiki-Taka", "Patient narrow buildup with high possession control.", new TeamTactics
        {
            Mentality = Mentality.Balanced,
            PressingIntensity = 58,
            Width = 28,
            Tempo = 28,
            DefensiveLine = 58
        }),
        new("gegenpress", "Gegenpress", "Win the ball back quickly with relentless pressure.", new TeamTactics
        {
            Mentality = Mentality.Attacking,
            PressingIntensity = 90,
            Width = 55,
            Tempo = 82,
            DefensiveLine = 82
        }),
        new("wing-play", "Wing Play", "Stretch the pitch and create crossing volume.", new TeamTactics
        {
            Mentality = Mentality.Attacking,
            PressingIntensity = 55,
            Width = 88,
            Tempo = 66,
            DefensiveLine = 55
        }),
        new("counter-attack", "Counter Attack", "Compact defending with fast direct transitions.", new TeamTactics
        {
            Mentality = Mentality.Defensive,
            PressingIntensity = 35,
            Width = 46,
            Tempo = 84,
            DefensiveLine = 28
        }),
        new("balanced", "Balanced", "Stable shape with no extreme risk.", new TeamTactics
        {
            Mentality = Mentality.Balanced,
            PressingIntensity = 50,
            Width = 50,
            Tempo = 50,
            DefensiveLine = 50
        })
    ];

    public static IReadOnlyList<TacticalOption> GetOptions(TacticalDimension dimension)
    {
        return dimension switch
        {
            TacticalDimension.Mentality => MentalityOptions,
            TacticalDimension.PressingIntensity => PressingOptions,
            TacticalDimension.Width => WidthOptions,
            TacticalDimension.Tempo => TempoOptions,
            TacticalDimension.DefensiveLine => DefensiveLineOptions,
            _ => []
        };
    }

    public static IReadOnlyList<TacticalPreset> GetPresets()
    {
        return Presets;
    }

    public static TacticalOption GetSelectedOption(TeamTactics tactics, TacticalDimension dimension)
    {
        var options = GetOptions(dimension);
        if (dimension == TacticalDimension.Mentality)
        {
            return options.First(option => option.MentalityValue == tactics.Mentality);
        }

        var value = GetValue(tactics, dimension);
        return options.MinBy(option => Math.Abs(option.Value - value)) ?? options[0];
    }

    public static void ApplyOption(TeamTactics tactics, TacticalOption option)
    {
        if (option.Dimension == TacticalDimension.Mentality && option.MentalityValue is not null)
        {
            tactics.Mentality = option.MentalityValue.Value;
            return;
        }

        SetValue(tactics, option.Dimension, option.Value);
    }

    public static TeamTactics Clone(TeamTactics tactics)
    {
        return new TeamTactics
        {
            Mentality = tactics.Mentality,
            PressingIntensity = tactics.PressingIntensity,
            Width = tactics.Width,
            Tempo = tactics.Tempo,
            DefensiveLine = tactics.DefensiveLine
        };
    }

    public static void CopyTo(TeamTactics source, TeamTactics target)
    {
        target.Mentality = source.Mentality;
        target.PressingIntensity = Clamp(source.PressingIntensity);
        target.Width = Clamp(source.Width);
        target.Tempo = Clamp(source.Tempo);
        target.DefensiveLine = Clamp(source.DefensiveLine);
    }

    public static string CreateSummary(TeamTactics tactics)
    {
        var mentality = GetSelectedOption(tactics, TacticalDimension.Mentality).Label.ToLowerInvariant();
        var pressing = GetSelectedOption(tactics, TacticalDimension.PressingIntensity).Label.ToLowerInvariant();
        var tempo = GetSelectedOption(tactics, TacticalDimension.Tempo).Label.ToLowerInvariant();
        var width = GetSelectedOption(tactics, TacticalDimension.Width).Label.ToLowerInvariant();
        var line = GetSelectedOption(tactics, TacticalDimension.DefensiveLine).Label.ToLowerInvariant();

        return $"Your team will play {mentality} football with {tempo} tempo, {width} width, {pressing} pressing intensity, and a {line} defensive line.";
    }

    public static int GetValue(TeamTactics tactics, TacticalDimension dimension)
    {
        return dimension switch
        {
            TacticalDimension.PressingIntensity => tactics.PressingIntensity,
            TacticalDimension.Width => tactics.Width,
            TacticalDimension.Tempo => tactics.Tempo,
            TacticalDimension.DefensiveLine => tactics.DefensiveLine,
            TacticalDimension.Mentality => GetSelectedOption(tactics, TacticalDimension.Mentality).Value,
            _ => 50
        };
    }

    public static void SetValue(TeamTactics tactics, TacticalDimension dimension, int value)
    {
        var clamped = Clamp(value);
        switch (dimension)
        {
            case TacticalDimension.PressingIntensity:
                tactics.PressingIntensity = clamped;
                break;
            case TacticalDimension.Width:
                tactics.Width = clamped;
                break;
            case TacticalDimension.Tempo:
                tactics.Tempo = clamped;
                break;
            case TacticalDimension.DefensiveLine:
                tactics.DefensiveLine = clamped;
                break;
        }
    }

    private static int Clamp(int value)
    {
        return Math.Clamp(value, 1, 100);
    }
}
