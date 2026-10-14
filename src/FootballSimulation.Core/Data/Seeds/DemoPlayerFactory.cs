using FootballSimulation.Models;

namespace FootballSimulation.Data;

public static class DemoPlayerFactory
{
    public static Player CreateGoalkeeper(string name, int defense, int passing, int stamina)
    {
        var overall = CalculateOverall(18, defense, passing, stamina, 12);
        return new Player
        {
            Name = name,
            Position = Position.Goalkeeper,
            Age = EstimateAge(Position.Goalkeeper, overall),
            OverallRating = overall,
            BaseOverallRating = overall,
            Attack = 18,
            Defense = defense,
            Passing = passing,
            Stamina = stamina,
            CurrentStamina = stamina,
            Finishing = 12
        };
    }

    public static Player CreateDefender(string name, int attack, int defense, int passing, int stamina, int finishing)
    {
        var overall = CalculateOverall(attack, defense, passing, stamina, finishing);
        return new Player
        {
            Name = name,
            Position = Position.Defender,
            Age = EstimateAge(Position.Defender, overall),
            OverallRating = overall,
            BaseOverallRating = overall,
            Attack = attack,
            Defense = defense,
            Passing = passing,
            Stamina = stamina,
            CurrentStamina = stamina,
            Finishing = finishing
        };
    }

    public static Player CreateMidfielder(string name, int attack, int defense, int passing, int stamina, int finishing)
    {
        var overall = CalculateOverall(attack, defense, passing, stamina, finishing);
        return new Player
        {
            Name = name,
            Position = Position.Midfielder,
            Age = EstimateAge(Position.Midfielder, overall),
            OverallRating = overall,
            BaseOverallRating = overall,
            Attack = attack,
            Defense = defense,
            Passing = passing,
            Stamina = stamina,
            CurrentStamina = stamina,
            Finishing = finishing
        };
    }

    public static Player CreateForward(string name, int attack, int defense, int passing, int stamina, int finishing)
    {
        var overall = CalculateOverall(attack, defense, passing, stamina, finishing);
        return new Player
        {
            Name = name,
            Position = Position.Forward,
            Age = EstimateAge(Position.Forward, overall),
            OverallRating = overall,
            BaseOverallRating = overall,
            Attack = attack,
            Defense = defense,
            Passing = passing,
            Stamina = stamina,
            CurrentStamina = stamina,
            Finishing = finishing
        };
    }

    private static int CalculateOverall(int attack, int defense, int passing, int stamina, int finishing)
    {
        return (int)Math.Round((attack + defense + passing + stamina + finishing) / 5.0);
    }

    private static int EstimateAge(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => overall >= 80 ? 29 : 25,
            Position.Defender => overall >= 78 ? 27 : 24,
            Position.Midfielder => overall >= 78 ? 26 : 23,
            Position.Forward => overall >= 80 ? 25 : 22,
            _ => 24
        };
    }
}
