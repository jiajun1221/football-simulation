using FootballSimulation.Services;

namespace FootballSimulation.Wpf.Services;

public class FormationLayoutService
{
    public IReadOnlyList<PitchPosition> GetPositions(string formation)
    {
        return FormationCatalogService.NormalizeFormationName(formation) switch
        {
            "4-3-3 Attack" => FourThreeThreeAttack,
            "4-3-3 Holding" => FourThreeThreeHolding,
            "4-3-3 Flat" => FourThreeThreeFlat,
            "4-2-3-1 Wide" => FourTwoThreeOneWide,
            "4-2-3-1 Narrow" => FourTwoThreeOneNarrow,
            "4-4-2" => FourFourTwo,
            "4-2-4" => FourTwoFour,
            "3-5-2" => ThreeFiveTwo,
            "3-5-2 Attack" => ThreeFiveTwoAttack,
            "3-4-3" => ThreeFourThree,
            "4-1-2-1-2 Narrow" => FourOneTwoOneTwoNarrow,
            "4-2-2-2" => FourTwoTwoTwo,
            "4-1-4-1" => FourOneFourOne,
            "5-2-3" => FiveTwoThree,
            "4-5-1" => FourFiveOne,
            "5-3-2" => FiveThreeTwo,
            "5-4-1" => FiveFourOne,
            "5-2-2-1" => FiveTwoTwoOne,
            "4-1-4-1 Defensive" => FourOneFourOneDefensive,
            "3-4-2-1" => ThreeFourTwoOne,
            _ => FourThreeThreeHolding
        };
    }

    private static IReadOnlyList<PitchPosition> Create(params (double X, double Y, string Slot)[] positions)
    {
        return positions.Select(position => new PitchPosition(position.X, position.Y, position.Slot)).ToList();
    }

    private static readonly IReadOnlyList<PitchPosition> FourThreeThreeAttack = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.32, 0.44, "CM"), (0.68, 0.44, "CM"), (0.50, 0.31, "CAM"),
        (0.16, 0.16, "LW"), (0.50, 0.09, "ST"), (0.84, 0.16, "RW"));

    private static readonly IReadOnlyList<PitchPosition> FourThreeThreeHolding = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.50, 0.53, "CDM"), (0.32, 0.42, "CM"), (0.68, 0.42, "CM"),
        (0.16, 0.16, "LW"), (0.50, 0.09, "ST"), (0.84, 0.16, "RW"));

    private static readonly IReadOnlyList<PitchPosition> FourThreeThreeFlat = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.26, 0.43, "CM"), (0.50, 0.43, "CM"), (0.74, 0.43, "CM"),
        (0.16, 0.16, "LW"), (0.50, 0.09, "ST"), (0.84, 0.16, "RW"));

    private static readonly IReadOnlyList<PitchPosition> FourTwoThreeOneWide = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.36, 0.50, "CDM"), (0.64, 0.50, "CDM"),
        (0.16, 0.30, "LW"), (0.50, 0.25, "CAM"), (0.84, 0.30, "RW"),
        (0.50, 0.09, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FourTwoThreeOneNarrow = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.36, 0.50, "CDM"), (0.64, 0.50, "CDM"),
        (0.30, 0.30, "CAM"), (0.50, 0.25, "CAM"), (0.70, 0.30, "CAM"),
        (0.50, 0.09, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FourFourTwo = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.14, 0.43, "LM"), (0.38, 0.43, "CM"), (0.62, 0.43, "CM"), (0.86, 0.43, "RM"),
        (0.36, 0.12, "ST"), (0.64, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FourTwoFour = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.36, 0.45, "CM"), (0.64, 0.45, "CM"),
        (0.13, 0.15, "LW"), (0.39, 0.10, "ST"), (0.61, 0.10, "ST"), (0.87, 0.15, "RW"));

    private static readonly IReadOnlyList<PitchPosition> ThreeFiveTwo = Create(
        (0.50, 0.93, "GK"),
        (0.24, 0.69, "CB"), (0.50, 0.72, "CB"), (0.76, 0.69, "CB"),
        (0.08, 0.43, "LWB"), (0.30, 0.49, "CDM"), (0.50, 0.42, "CM"), (0.70, 0.49, "CDM"), (0.92, 0.43, "RWB"),
        (0.36, 0.12, "ST"), (0.64, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> ThreeFiveTwoAttack = Create(
        (0.50, 0.93, "GK"),
        (0.24, 0.69, "CB"), (0.50, 0.72, "CB"), (0.76, 0.69, "CB"),
        (0.08, 0.43, "LWB"), (0.32, 0.45, "CM"), (0.50, 0.30, "CAM"), (0.68, 0.45, "CM"), (0.92, 0.43, "RWB"),
        (0.36, 0.12, "ST"), (0.64, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> ThreeFourThree = Create(
        (0.50, 0.93, "GK"),
        (0.24, 0.69, "CB"), (0.50, 0.72, "CB"), (0.76, 0.69, "CB"),
        (0.12, 0.43, "LM"), (0.38, 0.45, "CM"), (0.62, 0.45, "CM"), (0.88, 0.43, "RM"),
        (0.16, 0.16, "LW"), (0.50, 0.09, "ST"), (0.84, 0.16, "RW"));

    private static readonly IReadOnlyList<PitchPosition> FourOneTwoOneTwoNarrow = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.50, 0.53, "CDM"), (0.34, 0.39, "CM"), (0.66, 0.39, "CM"), (0.50, 0.27, "CAM"),
        (0.36, 0.12, "ST"), (0.64, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FourTwoTwoTwo = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.36, 0.50, "CDM"), (0.64, 0.50, "CDM"), (0.32, 0.30, "CAM"), (0.68, 0.30, "CAM"),
        (0.36, 0.12, "ST"), (0.64, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FourOneFourOne = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.50, 0.53, "CDM"), (0.14, 0.39, "LM"), (0.38, 0.39, "CM"), (0.62, 0.39, "CM"), (0.86, 0.39, "RM"),
        (0.50, 0.10, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FiveTwoThree = Create(
        (0.50, 0.93, "GK"),
        (0.08, 0.64, "LWB"), (0.29, 0.70, "CB"), (0.50, 0.73, "CB"), (0.71, 0.70, "CB"), (0.92, 0.64, "RWB"),
        (0.38, 0.43, "CM"), (0.62, 0.43, "CM"),
        (0.16, 0.16, "LW"), (0.50, 0.09, "ST"), (0.84, 0.16, "RW"));

    private static readonly IReadOnlyList<PitchPosition> FourFiveOne = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.67, "LB"), (0.37, 0.70, "CB"), (0.63, 0.70, "CB"), (0.88, 0.67, "RB"),
        (0.12, 0.43, "LM"), (0.34, 0.45, "CM"), (0.50, 0.52, "CDM"), (0.66, 0.45, "CM"), (0.88, 0.43, "RM"),
        (0.50, 0.11, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FiveThreeTwo = Create(
        (0.50, 0.93, "GK"),
        (0.08, 0.64, "LWB"), (0.29, 0.70, "CB"), (0.50, 0.73, "CB"), (0.71, 0.70, "CB"), (0.92, 0.64, "RWB"),
        (0.30, 0.43, "CM"), (0.50, 0.43, "CM"), (0.70, 0.43, "CM"),
        (0.36, 0.12, "ST"), (0.64, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FiveFourOne = Create(
        (0.50, 0.93, "GK"),
        (0.08, 0.64, "LWB"), (0.29, 0.70, "CB"), (0.50, 0.73, "CB"), (0.71, 0.70, "CB"), (0.92, 0.64, "RWB"),
        (0.14, 0.42, "LM"), (0.38, 0.43, "CM"), (0.62, 0.43, "CM"), (0.86, 0.42, "RM"),
        (0.50, 0.11, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FiveTwoTwoOne = Create(
        (0.50, 0.93, "GK"),
        (0.08, 0.64, "LWB"), (0.29, 0.70, "CB"), (0.50, 0.73, "CB"), (0.71, 0.70, "CB"), (0.92, 0.64, "RWB"),
        (0.36, 0.50, "CDM"), (0.64, 0.50, "CDM"), (0.34, 0.30, "CAM"), (0.66, 0.30, "CAM"),
        (0.50, 0.11, "ST"));

    private static readonly IReadOnlyList<PitchPosition> FourOneFourOneDefensive = Create(
        (0.50, 0.93, "GK"),
        (0.12, 0.68, "LB"), (0.37, 0.71, "CB"), (0.63, 0.71, "CB"), (0.88, 0.68, "RB"),
        (0.50, 0.56, "CDM"), (0.14, 0.42, "LM"), (0.38, 0.44, "CM"), (0.62, 0.44, "CM"), (0.86, 0.42, "RM"),
        (0.50, 0.12, "ST"));

    private static readonly IReadOnlyList<PitchPosition> ThreeFourTwoOne = Create(
        (0.50, 0.93, "GK"),
        (0.24, 0.69, "CB"), (0.50, 0.72, "CB"), (0.76, 0.69, "CB"),
        (0.12, 0.43, "LM"), (0.38, 0.46, "CM"), (0.62, 0.46, "CM"), (0.88, 0.43, "RM"),
        (0.34, 0.28, "CAM"), (0.66, 0.28, "CAM"),
        (0.50, 0.10, "ST"));
}

public sealed record PitchPosition(double X, double Y, string ExactPosition = "");
