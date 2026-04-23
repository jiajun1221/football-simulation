namespace FootballSimulation.Wpf.Services;

public class FormationLayoutService
{
    public IReadOnlyList<PitchPosition> GetPositions(string formation)
    {
        return formation switch
        {
            "4-2-3-1" => Build([1, 4, 2, 3, 1]),
            "4-4-2" => Build([1, 4, 4, 2]),
            "3-5-2" => Build([1, 3, 5, 2]),
            _ => Build([1, 4, 3, 3])
        };
    }

    private static IReadOnlyList<PitchPosition> Build(IReadOnlyList<int> lines)
    {
        var positions = new List<PitchPosition>();
        var yValues = GetLineYValues(lines.Count);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var playersInLine = lines[lineIndex];
            var y = yValues[lineIndex];

            for (var playerIndex = 0; playerIndex < playersInLine; playerIndex++)
            {
                var x = playersInLine == 1
                    ? 0.50
                    : 0.16 + (playerIndex * (0.68 / (playersInLine - 1)));

                positions.Add(new PitchPosition(x, y));
            }
        }

        return positions;
    }

    private static double[] GetLineYValues(int lineCount)
    {
        return lineCount switch
        {
            4 => [0.88, 0.66, 0.42, 0.18],
            5 => [0.88, 0.68, 0.50, 0.32, 0.16],
            _ => [0.88, 0.66, 0.42, 0.18]
        };
    }
}

public sealed record PitchPosition(double X, double Y);
