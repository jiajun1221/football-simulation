using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlaceholderTeamFactory
{
    private static readonly (string ExactPosition, Position Position, int SquadNumber)[] SquadTemplate =
    [
        ("GK", Position.Goalkeeper, 1),
        ("RB", Position.Defender, 2),
        ("CB", Position.Defender, 4),
        ("CB", Position.Defender, 5),
        ("LB", Position.Defender, 3),
        ("CDM", Position.Midfielder, 6),
        ("CM", Position.Midfielder, 8),
        ("CAM", Position.Midfielder, 10),
        ("RW", Position.Forward, 7),
        ("ST", Position.Forward, 9),
        ("LW", Position.Forward, 11),
        ("GK", Position.Goalkeeper, 13),
        ("CB", Position.Defender, 15),
        ("CM", Position.Midfielder, 16),
        ("ST", Position.Forward, 18)
    ];

    public static Team Create(string name, int baseOverall, string venueSuffix = "Stadium")
    {
        var players = SquadTemplate
            .Select((slot, index) => CreatePlayer(name, baseOverall, index, slot))
            .ToList();

        return new Team
        {
            Name = name,
            Venue = $"{name} {venueSuffix}",
            StadiumName = $"{name} {venueSuffix}",
            Formation = "4-3-3",
            Players = players.Take(11).ToList(),
            Substitutes = players.Skip(11).ToList(),
            Tactics = new TeamTactics
            {
                Mentality = Mentality.Balanced,
                PressingIntensity = 50,
                Width = 50,
                Tempo = 50,
                DefensiveLine = 50
            }
        };
    }

    private static Player CreatePlayer(
        string teamName,
        int baseOverall,
        int index,
        (string ExactPosition, Position Position, int SquadNumber) slot)
    {
        var seed = Math.Abs(HashCode.Combine(teamName, index, slot.ExactPosition));
        var overall = Math.Clamp(baseOverall + (seed % 7) - 3, 58, 90);
        var player = new Player
        {
            PlayerId = $"placeholder-{NormalizeId(teamName)}-{index + 1:00}",
            Name = $"{teamName} Player {index + 1}",
            SquadNumber = slot.SquadNumber,
            Position = slot.Position,
            PreferredPosition = slot.ExactPosition,
            AssignedPosition = slot.ExactPosition,
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = 22 + seed % 12,
            PotentialOverall = Math.Clamp(overall + 3, overall, 92),
            Nationality = "England",
            NationalityName = "England",
            NationalityCode = "ENG",
            FlagImagePath = "Assets/Flags/england.png",
            Stamina = 88 + seed % 10,
            WeeklyWage = Math.Round(overall * 900m, 0),
            Role = index < 11 ? PlayerRole.Starter : PlayerRole.Rotation
        };

        var attributes = PlayerAttributeService.DeriveAttributes(
            player.Position,
            player.PreferredPosition,
            player.OverallRating,
            player.Traits,
            (int)Math.Round(player.Stamina));
        player.Pace = attributes.Pace;
        player.Shooting = attributes.Shooting;
        player.Passing = attributes.Passing;
        player.Dribbling = attributes.Dribbling;
        player.Defending = attributes.Defending;
        player.Physical = attributes.Physical;

        return player;
    }

    private static string NormalizeId(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
