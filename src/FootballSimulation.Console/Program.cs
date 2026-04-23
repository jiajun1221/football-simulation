using FootballSimulation.Services;

namespace FootballSimulation;

public static class Program
{
    public static void Main()
    {
        var gameFlowService = new GameFlowService();
        gameFlowService.Run();
    }
}
