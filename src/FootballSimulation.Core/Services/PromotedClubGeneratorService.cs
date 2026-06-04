using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PromotedClubGeneratorService
{
    private static readonly string[] ClubNamePool =
    [
        "Leicester City",
        "Southampton",
        "Ipswich Town",
        "Sheffield United",
        "Norwich City",
        "West Bromwich Albion",
        "Leeds United",
        "Burnley",
        "Middlesbrough",
        "Sunderland",
        "Coventry City",
        "Blackburn Rovers"
    ];

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
        ("RB", Position.Defender, 22),
        ("LB", Position.Defender, 23),
        ("CM", Position.Midfielder, 16),
        ("CAM", Position.Midfielder, 18),
        ("CDM", Position.Midfielder, 20),
        ("RW", Position.Forward, 17),
        ("ST", Position.Forward, 19),
        ("LW", Position.Forward, 21),
        ("ST", Position.Forward, 24)
    ];

    private static readonly string[] FirstNames =
    [
        "Jack",
        "Oliver",
        "Harry",
        "George",
        "Charlie",
        "Thomas",
        "Daniel",
        "Lewis",
        "Ethan",
        "Callum",
        "Ryan",
        "Mason",
        "Noah",
        "Jacob",
        "Samuel",
        "Adam"
    ];

    private static readonly string[] LastNames =
    [
        "Bennett",
        "Cooper",
        "Reed",
        "Parker",
        "Hughes",
        "Morris",
        "Watson",
        "Bailey",
        "Foster",
        "Ward",
        "Turner",
        "Brooks",
        "Kelly",
        "Wright",
        "Price",
        "Wood"
    ];

    public List<Team> GeneratePromotedClubs(int count, IEnumerable<string> excludedClubNames, string season)
    {
        var excluded = excludedClubNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clubs = ClubNamePool
            .Where(name => !excluded.Contains(name))
            .Take(count)
            .Select((name, index) => CreateClub(name, season, index))
            .ToList();

        var fallbackIndex = 1;
        while (clubs.Count < count)
        {
            var name = $"Promoted City {fallbackIndex}";
            fallbackIndex++;
            if (excluded.Contains(name))
            {
                continue;
            }

            clubs.Add(CreateClub(name, season, clubs.Count));
        }

        return clubs;
    }

    private static Team CreateClub(string clubName, string season, int clubIndex)
    {
        var players = SquadTemplate
            .Select((slot, index) => CreatePlayer(clubName, season, clubIndex, index, slot))
            .ToList();

        return new Team
        {
            Name = clubName,
            Venue = $"{clubName} Stadium",
            StadiumName = $"{clubName} Stadium",
            Formation = "4-3-3",
            Players = players.Take(11).ToList(),
            Substitutes = players.Skip(11).ToList(),
            Tactics = new TeamTactics
            {
                Mentality = Mentality.Balanced,
                PressingIntensity = 48 + clubIndex * 2,
                Width = 50,
                Tempo = 50,
                DefensiveLine = 46 + clubIndex * 2
            }
        };
    }

    private static Player CreatePlayer(
        string clubName,
        string season,
        int clubIndex,
        int playerIndex,
        (string ExactPosition, Position Position, int SquadNumber) slot)
    {
        var seed = Math.Abs(HashCode.Combine(clubName, season, clubIndex, playerIndex));
        var random = new Random(seed);
        var isStandout = playerIndex is 0 or 6 or 9;
        var overall = Math.Clamp(random.Next(isStandout ? 72 : 66, isStandout ? 79 : 75), 62, 80);
        var player = new Player
        {
            PlayerId = $"generated-{NormalizeId(clubName)}-{NormalizeId(season)}-{playerIndex + 1:00}",
            Name = $"{FirstNames[(seed + playerIndex) % FirstNames.Length]} {LastNames[(seed / 3 + playerIndex) % LastNames.Length]}",
            SquadNumber = slot.SquadNumber,
            Position = slot.Position,
            PreferredPosition = slot.ExactPosition,
            AssignedPosition = slot.ExactPosition,
            SecondaryPositions = GetSecondaryPositions(slot.ExactPosition),
            PreferredFoot = slot.ExactPosition is "LB" or "LW" ? "Left" : "Right",
            Nationality = "England",
            NationalityCode = "ENG",
            NationalityName = "England",
            FlagImagePath = "Assets/Flags/england.png",
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = random.Next(19, 33),
            PotentialOverall = Math.Clamp(overall + random.Next(2, 8), overall, 82),
            Role = playerIndex < 11 ? PlayerRole.Starter : PlayerRole.Rotation,
            Form = "Average",
            CurrentForm = 50,
            Morale = 50,
            Stamina = random.Next(82, 99),
            DisciplineRating = random.Next(45, 78),
            WeeklyWage = Math.Round((overall * 950m) + random.Next(2_000, 12_000), 0),
            ReleaseClause = Math.Round((decimal)Math.Pow(overall, 3) * 900m, 0),
            ContractEndYear = 2028 + random.Next(0, 4),
            Traits = GetTraits(slot.ExactPosition, random)
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

    private static List<string> GetSecondaryPositions(string exactPosition)
    {
        return exactPosition switch
        {
            "RB" => ["LB", "CB"],
            "LB" => ["RB", "CB"],
            "CB" => ["RB", "LB", "CDM"],
            "CDM" => ["CM", "CB"],
            "CM" => ["CDM", "CAM"],
            "CAM" => ["CM", "LW", "RW"],
            "RW" => ["LW", "ST"],
            "LW" => ["RW", "ST"],
            "ST" => ["RW", "LW"],
            _ => []
        };
    }

    private static List<PlayerTrait> GetTraits(string exactPosition, Random random)
    {
        var traits = exactPosition switch
        {
            "GK" => new[] { PlayerTrait.OneOnOnes, PlayerTrait.RushesOutOfGoal },
            "CB" => new[] { PlayerTrait.Interceptor, PlayerTrait.AerialThreat },
            "LB" or "RB" => new[] { PlayerTrait.Engine, PlayerTrait.EarlyCrosser },
            "CDM" => new[] { PlayerTrait.DivesIntoTackles, PlayerTrait.TeamPlayer },
            "CM" => new[] { PlayerTrait.BoxToBox, PlayerTrait.Playmaker },
            "CAM" => new[] { PlayerTrait.Playmaker, PlayerTrait.PressResistant },
            "LW" or "RW" => new[] { PlayerTrait.Rapid, PlayerTrait.TechnicalDribbler },
            "ST" => new[] { PlayerTrait.ClinicalFinisher, PlayerTrait.TriesToBeatOffsideTrap },
            _ => Array.Empty<PlayerTrait>()
        };

        return traits
            .Where(_ => random.NextDouble() < 0.45)
            .Take(1)
            .ToList();
    }

    private static string NormalizeId(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }
}
