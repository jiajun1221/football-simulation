namespace FootballSimulation.Wpf.Services;

public class FormationLayoutService
{
    public IReadOnlyList<PitchPosition> GetPositions(string formation)
    {
        return formation switch
        {
            "4-2-3-1" => FourTwoThreeOne,
            "4-4-2" => FourFourTwo,
            "3-5-2" => ThreeFiveTwo,
            _ => FourThreeThree
        };
    }

    private static readonly IReadOnlyList<PitchPosition> FourThreeThree =
    [
        new(0.50, 0.93, "GK"),
        new(0.12, 0.67, "LB"), new(0.37, 0.70, "CB"), new(0.63, 0.70, "CB"), new(0.88, 0.67, "RB"),
        new(0.23, 0.43, "CM"), new(0.50, 0.36, "CAM"), new(0.77, 0.43, "CM"),
        new(0.16, 0.16, "LW"), new(0.50, 0.09, "ST"), new(0.84, 0.16, "RW")
    ];

    private static readonly IReadOnlyList<PitchPosition> FourTwoThreeOne =
    [
        new(0.50, 0.93, "GK"),
        new(0.12, 0.67, "LB"), new(0.37, 0.70, "CB"), new(0.63, 0.70, "CB"), new(0.88, 0.67, "RB"),
        new(0.36, 0.50, "CDM"), new(0.64, 0.50, "CDM"),
        new(0.16, 0.30, "LW"), new(0.84, 0.30, "RW"), new(0.50, 0.25, "CAM"),
        new(0.50, 0.09, "ST")
    ];

    private static readonly IReadOnlyList<PitchPosition> FourFourTwo =
    [
        new(0.50, 0.93, "GK"),
        new(0.12, 0.67, "LB"), new(0.37, 0.70, "CB"), new(0.63, 0.70, "CB"), new(0.88, 0.67, "RB"),
        new(0.14, 0.43, "CM"), new(0.38, 0.43, "CM"), new(0.62, 0.43, "CM"), new(0.86, 0.43, "CM"),
        new(0.36, 0.12, "ST"), new(0.64, 0.12, "ST")
    ];

    private static readonly IReadOnlyList<PitchPosition> ThreeFiveTwo =
    [
        new(0.50, 0.93, "GK"),
        new(0.24, 0.69, "CB"), new(0.50, 0.72, "CB"), new(0.76, 0.69, "CB"),
        new(0.08, 0.43, "LB"), new(0.29, 0.49, "CM"), new(0.50, 0.42, "CDM"), new(0.71, 0.49, "CM"), new(0.92, 0.43, "RB"),
        new(0.36, 0.12, "ST"), new(0.64, 0.12, "ST")
    ];
}

public sealed record PitchPosition(double X, double Y, string ExactPosition = "");
