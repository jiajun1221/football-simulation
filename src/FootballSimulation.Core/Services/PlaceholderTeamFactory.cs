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

    private static readonly Dictionary<string, NameProfile> NameProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Austria"] = new(["Lukas", "Marco", "Florian", "David", "Maximilian", "Niklas", "Tobias", "Matthias"], ["Gruber", "Schmid", "Weber", "Bauer", "Hofer", "Wagner", "Steiner", "Fuchs"], "AT", "Austria", "/Assets/Flags/austria.png"),
        ["Belgium"] = new(["Arthur", "Elias", "Noah", "Milan", "Jules", "Liam", "Thibaut", "Yari"], ["Peeters", "Janssens", "Claes", "Dubois", "Lambert", "Vermeulen", "De Smet", "Maes"], "BE", "Belgium", "/Assets/Flags/belgium.png"),
        ["Croatia"] = new(["Luka", "Ivan", "Marko", "Ante", "Dino", "Niko", "Mateo", "Josip"], ["Horvat", "Kovac", "Maric", "Novak", "Peric", "Vidic", "Jankovic", "Babic"], "HR", "Croatia", "/Assets/Flags/croatia.png"),
        ["Czechia"] = new(["Tomas", "Jan", "Jakub", "Adam", "Petr", "Matej", "Lukas", "Ondrej"], ["Novak", "Svoboda", "Dvorak", "Prochazka", "Cerny", "Kucera", "Vesely", "Horak"], "CZ", "Czechia", "/Assets/Flags/czechia.png"),
        ["Czech Republic"] = new(["Tomas", "Jan", "Jakub", "Adam", "Petr", "Matej", "Lukas", "Ondrej"], ["Novak", "Svoboda", "Dvorak", "Prochazka", "Cerny", "Kucera", "Vesely", "Horak"], "CZ", "Czechia", "/Assets/Flags/czechia.png"),
        ["Denmark"] = new(["Mikkel", "Noah", "Oscar", "Viktor", "Emil", "Frederik", "Magnus", "Jonas"], ["Nielsen", "Jensen", "Larsen", "Madsen", "Pedersen", "Poulsen", "Christensen", "Andersen"], "DK", "Denmark", "/Assets/Flags/denmark.png"),
        ["England"] = new(["Oliver", "Harry", "George", "Jack", "Alfie", "Ethan", "Leo", "Theo"], ["Smith", "Jones", "Taylor", "Brown", "Wilson", "Walker", "Hall", "Bennett"], "GB-ENG", "England", "/Assets/Flags/england.png"),
        ["France"] = new(["Hugo", "Lucas", "Theo", "Enzo", "Mathis", "Noah", "Jules", "Rayan"], ["Bernard", "Dubois", "Moreau", "Roux", "Lefevre", "Girard", "Fontaine", "Lemoine"], "FR", "France", "/Assets/Flags/france.png"),
        ["Germany"] = new(["Ben", "Emil", "Finn", "Jonas", "Leon", "Lukas", "Noah", "Paul"], ["Bauer", "Fischer", "Klein", "Meyer", "Schulz", "Weber", "Hoffmann", "Wagner"], "DE", "Germany", "/Assets/Flags/germany.png"),
        ["Greece"] = new(["Nikos", "Giorgos", "Dimitris", "Kostas", "Petros", "Andreas", "Alexis", "Stavros"], ["Papadopoulos", "Nikolaou", "Georgiou", "Dimitriou", "Kostas", "Vasileiou", "Ioannou", "Pappas"], "GR", "Greece", "/Assets/Flags/greece.png"),
        ["Italy"] = new(["Luca", "Matteo", "Andrea", "Marco", "Nico", "Gabriele", "Federico", "Alessandro"], ["Rossi", "Bianchi", "Romano", "Ricci", "Conti", "Ferrari", "Greco", "Marino"], "IT", "Italy", "/Assets/Flags/italy.png"),
        ["Netherlands"] = new(["Daan", "Finn", "Jens", "Lars", "Milan", "Sem", "Thijs", "Noah"], ["Bakker", "Jansen", "Meijer", "Smit", "Visser", "Vos", "De Jong", "Van Dijk"], "NL", "Netherlands", "/Assets/Flags/netherlands.png"),
        ["Portugal"] = new(["Diogo", "Tomas", "Ruben", "Goncalo", "Tiago", "Andre", "Joao", "Nuno"], ["Costa", "Fernandes", "Mendes", "Neves", "Ramos", "Silva", "Pereira", "Sousa"], "PT", "Portugal", "/Assets/Flags/portugal.png"),
        ["Scotland"] = new(["Lewis", "Callum", "Ryan", "Scott", "Jamie", "Connor", "Euan", "Liam"], ["MacDonald", "Campbell", "Stewart", "Robertson", "McKenzie", "Murray", "Fraser", "Reid"], "GB-SCT", "Scotland", "/Assets/Flags/scotland.png"),
        ["Serbia"] = new(["Luka", "Nikola", "Stefan", "Marko", "Filip", "Milos", "Dusan", "Aleksa"], ["Jovanovic", "Petrovic", "Nikolic", "Ilic", "Stankovic", "Pavlovic", "Milosevic", "Savic"], "RS", "Serbia", "/Assets/Flags/serbia.png"),
        ["Slovakia"] = new(["Jakub", "Adam", "Lukas", "Martin", "Tomas", "Samuel", "David", "Marek"], ["Novak", "Kovac", "Horvath", "Varga", "Nagy", "Balaz", "Kral", "Mikus"], "SK", "Slovakia", "/Assets/Flags/slovakia.png"),
        ["Spain"] = new(["Diego", "Hugo", "Iker", "Mateo", "Nico", "Pablo", "Sergio", "Javi"], ["Garcia", "Alonso", "Ramos", "Santos", "Vega", "Torres", "Moreno", "Navarro"], "ES", "Spain", "/Assets/Flags/spain.png"),
        ["Switzerland"] = new(["Noah", "Luca", "Leon", "Nico", "Jonas", "David", "Elia", "Mauro"], ["Muller", "Meier", "Schmid", "Keller", "Weber", "Huber", "Frei", "Baumann"], "CH", "Switzerland", "/Assets/Flags/switzerland.png"),
        ["Turkey"] = new(["Emir", "Arda", "Kerem", "Yusuf", "Mert", "Eren", "Can", "Ozan"], ["Yilmaz", "Kaya", "Demir", "Celik", "Sahin", "Yildiz", "Aydin", "Arslan"], "TR", "Turkey", "/Assets/Flags/turkey.png"),
        ["Ukraine"] = new(["Andriy", "Oleksandr", "Mykola", "Danylo", "Taras", "Viktor", "Roman", "Bohdan"], ["Shevchenko", "Kovalenko", "Bondarenko", "Tkachenko", "Melnyk", "Kravchenko", "Boyko", "Savchuk"], "UA", "Ukraine", "/Assets/Flags/ukraine.png")
    };

    public static Team Create(string name, int baseOverall, string venueSuffix = "Stadium", string country = "England")
    {
        var players = SquadTemplate
            .Select((slot, index) => CreatePlayer(name, baseOverall, index, slot, country))
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

    public static bool HasPlaceholderNames(Team team)
    {
        return team.Players.Concat(team.Substitutes).Any(IsPlaceholderPlayer);
    }

    public static bool RepairPlaceholderNames(Team team, Team? sourceTeam = null, string country = "England")
    {
        if (!HasPlaceholderNames(team))
        {
            return false;
        }

        if (sourceTeam is not null)
        {
            ApplySourceTeamSquad(team, sourceTeam);
            return true;
        }

        var targetPlayers = team.Players.Concat(team.Substitutes).ToList();
        for (var index = 0; index < targetPlayers.Count; index++)
        {
            var player = targetPlayers[index];
            if (!IsPlaceholderPlayer(player))
            {
                continue;
            }

            ApplyGeneratedIdentity(player, team.Name, index, country);
        }

        return true;
    }

    private static Player CreatePlayer(
        string teamName,
        int baseOverall,
        int index,
        (string ExactPosition, Position Position, int SquadNumber) slot,
        string country)
    {
        var seed = StableHash($"{teamName}|{index}|{slot.ExactPosition}");
        var overall = Math.Clamp(baseOverall + (seed % 7) - 3, 58, 90);
        var player = new Player
        {
            PlayerId = $"placeholder-{NormalizeId(teamName)}-{index + 1:00}",
            SquadNumber = slot.SquadNumber,
            Position = slot.Position,
            PreferredPosition = slot.ExactPosition,
            AssignedPosition = slot.ExactPosition,
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = 22 + seed % 12,
            PotentialOverall = Math.Clamp(overall + 3, overall, 92),
            Stamina = 88 + seed % 10,
            WeeklyWage = Math.Round(overall * 900m, 0),
            Role = index < 11 ? PlayerRole.Starter : PlayerRole.Rotation
        };
        ApplyGeneratedIdentity(player, teamName, index, country);

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

    private static void ApplySourceTeamSquad(Team team, Team sourceTeam)
    {
        team.Venue = string.IsNullOrWhiteSpace(sourceTeam.Venue) ? team.Venue : sourceTeam.Venue;
        team.StadiumName = string.IsNullOrWhiteSpace(sourceTeam.StadiumName) ? team.StadiumName : sourceTeam.StadiumName;
        team.Formation = string.IsNullOrWhiteSpace(sourceTeam.Formation) ? team.Formation : sourceTeam.Formation;
        team.Players = sourceTeam.Players.Select(player => CloneSourcePlayer(player, isStarter: true)).ToList();
        team.Substitutes = sourceTeam.Substitutes.Select(player => CloneSourcePlayer(player, isStarter: false)).ToList();
    }

    private static Player CloneSourcePlayer(Player sourcePlayer, bool isStarter)
    {
        var clone = new Player
        {
            PlayerId = sourcePlayer.PlayerId,
            Name = sourcePlayer.Name,
            SquadNumber = sourcePlayer.SquadNumber,
            Position = sourcePlayer.Position,
            PreferredPosition = sourcePlayer.PreferredPosition,
            SecondaryPositions = sourcePlayer.SecondaryPositions.ToList(),
            AssignedPosition = sourcePlayer.PreferredPosition,
            PreferredFoot = sourcePlayer.PreferredFoot,
            Nationality = sourcePlayer.Nationality,
            NationalityCode = sourcePlayer.NationalityCode,
            NationalityName = sourcePlayer.NationalityName,
            FlagEmoji = sourcePlayer.FlagEmoji,
            FlagImagePath = sourcePlayer.FlagImagePath,
            DisciplineRating = sourcePlayer.DisciplineRating,
            OverallRating = sourcePlayer.OverallRating,
            BaseOverallRating = sourcePlayer.BaseOverallRating,
            GrowthPoints = sourcePlayer.GrowthPoints,
            LastMatchGrowthPoints = sourcePlayer.LastMatchGrowthPoints,
            LastMatchOverallIncrease = sourcePlayer.LastMatchOverallIncrease,
            Age = sourcePlayer.Age,
            PotentialOverall = sourcePlayer.PotentialOverall,
            TransferStatus = sourcePlayer.TransferStatus,
            ContractEndYear = sourcePlayer.ContractEndYear,
            WeeklyWage = sourcePlayer.WeeklyWage,
            ReleaseClause = sourcePlayer.ReleaseClause,
            ContractStatus = sourcePlayer.ContractStatus,
            Role = sourcePlayer.Role,
            RejectTransferOffers = sourcePlayer.RejectTransferOffers,
            Form = sourcePlayer.Form,
            IsStarter = isStarter,
            IsCaptain = sourcePlayer.IsCaptain,
            CurrentForm = sourcePlayer.CurrentForm,
            FormStatus = sourcePlayer.FormStatus,
            Morale = sourcePlayer.Morale,
            Traits = sourcePlayer.Traits.ToList(),
            Pace = sourcePlayer.Pace,
            Shooting = sourcePlayer.Shooting,
            Dribbling = sourcePlayer.Dribbling,
            Defending = sourcePlayer.Defending,
            Physical = sourcePlayer.Physical,
            Attack = sourcePlayer.Attack,
            Defense = sourcePlayer.Defense,
            Passing = sourcePlayer.Passing,
            Stamina = sourcePlayer.Stamina,
            CurrentStamina = sourcePlayer.CurrentStamina,
            LiveMatchModifier = sourcePlayer.LiveMatchModifier,
            IsInjured = sourcePlayer.IsInjured,
            InjuryType = sourcePlayer.InjuryType,
            InjurySeverity = sourcePlayer.InjurySeverity,
            InjuryRecoveryMatches = sourcePlayer.InjuryRecoveryMatches,
            IsSeasonEndingInjury = sourcePlayer.IsSeasonEndingInjury,
            SuspendedMatches = sourcePlayer.SuspendedMatches,
            MatchesPlayedRecently = sourcePlayer.MatchesPlayedRecently,
            SeasonFatigue = sourcePlayer.SeasonFatigue,
            ConsecutiveStarts = sourcePlayer.ConsecutiveStarts,
            Finishing = sourcePlayer.Finishing,
            YellowCards = sourcePlayer.YellowCards,
            IsSentOff = sourcePlayer.IsSentOff,
            RedCardMinute = sourcePlayer.RedCardMinute,
            IsOnPitch = isStarter
        };
        PositionSuitabilityService.EnsurePositionMetadata(clone, sourcePlayer.PreferredPosition);
        return clone;
    }

    private static void ApplyGeneratedIdentity(Player player, string teamName, int index, string country)
    {
        var profile = NameProfiles.GetValueOrDefault(country) ?? NameProfiles["England"];
        var seed = Math.Abs(StableHash($"{teamName}|{index}|{player.PreferredPosition}|{country}"));
        var firstName = profile.FirstNames[seed % profile.FirstNames.Length];
        var lastName = profile.LastNames[(seed / Math.Max(1, profile.FirstNames.Length)) % profile.LastNames.Length];
        player.Name = $"{firstName} {lastName}";
        player.Nationality = profile.NationalityName;
        player.NationalityName = profile.NationalityName;
        player.NationalityCode = profile.NationalityCode;
        player.FlagImagePath = profile.FlagImagePath;
    }

    private static bool IsPlaceholderPlayer(Player player)
    {
        return player.PlayerId.StartsWith("placeholder-", StringComparison.OrdinalIgnoreCase) ||
            player.Name.Contains(" Player ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeId(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var character in value)
            {
                hash = hash * 31 + char.ToUpperInvariant(character);
            }

            return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
        }
    }

    private sealed record NameProfile(
        string[] FirstNames,
        string[] LastNames,
        string NationalityCode,
        string NationalityName,
        string FlagImagePath);
}
