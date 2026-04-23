using FootballSimulation.Models;

namespace FootballSimulation.Data;

public static class DemoPlayerFactory
{
    public static Player CreateGoalkeeper(string name, int defense, int passing, int stamina)
    {
        return new Player
        {
            Name = name,
            Position = Position.Goalkeeper,
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
        return new Player
        {
            Name = name,
            Position = Position.Defender,
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
        return new Player
        {
            Name = name,
            Position = Position.Midfielder,
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
        return new Player
        {
            Name = name,
            Position = Position.Forward,
            Attack = attack,
            Defense = defense,
            Passing = passing,
            Stamina = stamina,
            CurrentStamina = stamina,
            Finishing = finishing
        };
    }
}
