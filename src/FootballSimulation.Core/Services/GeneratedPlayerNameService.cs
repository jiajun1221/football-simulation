namespace FootballSimulation.Services;

/// <summary>
/// Provides a shared, country-aware name pool for every generated player.
/// Keeping this in one place prevents academy, scouting, regen and market
/// generators from repeatedly producing the same small group of names.
/// </summary>
public static class GeneratedPlayerNameService
{
    private static readonly NameProfile FallbackProfile = new(
        ["Alex", "Adrian", "Daniel", "Elias", "Gabriel", "Isaac", "Julian", "Kai", "Luca", "Milan", "Nathan", "Noah", "Oliver", "Samuel", "Theo", "Victor"],
        ["Bennett", "Costa", "Fischer", "Garcia", "Martin", "Moreau", "Novak", "Parker", "Rossi", "Santos", "Silva", "Torres", "Vega", "Weber", "Wilson", "Young"]);

    private static readonly Dictionary<string, NameProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["England"] = new(
            ["Alfie", "Archie", "Callum", "Charlie", "Elliot", "Ethan", "Finley", "Freddie", "George", "Harvey", "Isaac", "Jack", "Leo", "Mason", "Oliver", "Oscar", "Reuben", "Theo", "Toby", "William"],
            ["Bennett", "Brooks", "Clarke", "Cole", "Davies", "Foster", "Grant", "Harrison", "Hayes", "Hughes", "Marshall", "Palmer", "Parker", "Price", "Reed", "Shaw", "Taylor", "Walker", "Ward", "Wilson"]),
        ["Spain"] = new(
            ["Alejandro", "Alvaro", "Carlos", "Daniel", "Diego", "Hector", "Hugo", "Iker", "Javier", "Jorge", "Marco", "Mateo", "Nico", "Pablo", "Rodrigo", "Sergio", "Unai", "Victor"],
            ["Alonso", "Blanco", "Castro", "Dominguez", "Garcia", "Gil", "Herrera", "Iglesias", "Lopez", "Moreno", "Navarro", "Ortega", "Ramos", "Ruiz", "Santos", "Serrano", "Torres", "Vega"]),
        ["France"] = new(
            ["Adrien", "Antoine", "Bastien", "Clement", "Enzo", "Etienne", "Hugo", "Jules", "Kylian", "Leo", "Lucas", "Mathis", "Maxime", "Noah", "Raphael", "Theo", "Thomas", "Yanis"],
            ["Bernard", "Blanc", "Chevalier", "Dubois", "Dupont", "Faure", "Fontaine", "Fournier", "Girard", "Lambert", "Laurent", "Lefevre", "Marchand", "Martin", "Moreau", "Roux", "Simon", "Vincent"]),
        ["Germany"] = new(
            ["Anton", "Ben", "David", "Elias", "Emil", "Felix", "Finn", "Florian", "Jonas", "Julian", "Leon", "Lukas", "Mats", "Max", "Moritz", "Niklas", "Paul", "Tim"],
            ["Bauer", "Becker", "Braun", "Fischer", "Hoffmann", "Klein", "Koch", "Kruger", "Lang", "Meyer", "Richter", "Schmidt", "Schneider", "Schulz", "Vogel", "Wagner", "Weber", "Wolf"]),
        ["Brazil"] = new(
            ["Andre", "Bruno", "Caio", "Danilo", "Davi", "Enzo", "Felipe", "Gabriel", "Guilherme", "Joao", "Lucas", "Matheus", "Murilo", "Pedro", "Rafael", "Renan", "Thiago", "Vinicius"],
            ["Almeida", "Alves", "Barbosa", "Cardoso", "Carvalho", "Costa", "Ferreira", "Gomes", "Lima", "Martins", "Mendes", "Nascimento", "Oliveira", "Pereira", "Ribeiro", "Rocha", "Santos", "Silva"]),
        ["Netherlands"] = new(
            ["Bram", "Daan", "Finn", "Gijs", "Jens", "Jesse", "Lars", "Luuk", "Mees", "Milan", "Noud", "Pim", "Sem", "Stijn", "Teun", "Thijs", "Timo", "Wout"],
            ["Bakker", "Bos", "De Boer", "De Jong", "De Vries", "Dekker", "Dijkstra", "Jansen", "Kok", "Meijer", "Mulder", "Smit", "Van Dijk", "Van Leeuwen", "Van den Berg", "Visser", "Vos", "Willems"]),
        ["Argentina"] = new(
            ["Agustin", "Alejo", "Bautista", "Benjamin", "Emiliano", "Facundo", "Franco", "Ignacio", "Joaquin", "Julian", "Lautaro", "Mateo", "Nicolas", "Santiago", "Thiago", "Tomas", "Valentin", "Vicente"],
            ["Acuna", "Alvarez", "Benitez", "Cabrera", "Diaz", "Fernandez", "Gimenez", "Gomez", "Lopez", "Martinez", "Molina", "Paredes", "Pereyra", "Romero", "Sosa", "Suarez", "Vega", "Vera"]),
        ["Portugal"] = new(
            ["Afonso", "Andre", "Bernardo", "Diogo", "Duarte", "Francisco", "Goncalo", "Henrique", "Joao", "Martim", "Miguel", "Nuno", "Pedro", "Ricardo", "Ruben", "Tiago", "Tomas", "Vasco"],
            ["Almeida", "Carvalho", "Coelho", "Costa", "Dias", "Fernandes", "Ferreira", "Gomes", "Lopes", "Martins", "Mendes", "Monteiro", "Neves", "Oliveira", "Pereira", "Ramos", "Rocha", "Silva"]),
        ["Belgium"] = new(
            ["Arthur", "Baptiste", "Elias", "Emile", "Jules", "Laurent", "Liam", "Louis", "Lucas", "Mathis", "Milan", "Nathan", "Noah", "Rayan", "Robbe", "Seppe", "Thomas", "Victor"],
            ["Claes", "De Smet", "De Vos", "Dubois", "Jacobs", "Janssens", "Lambert", "Leclercq", "Maes", "Mertens", "Peeters", "Renard", "Simon", "Van Damme", "Vandenberg", "Vermeulen", "Willems", "Wouters"]),
        ["Italy"] = new(
            ["Alessandro", "Andrea", "Davide", "Edoardo", "Elia", "Federico", "Francesco", "Gabriele", "Giacomo", "Leonardo", "Lorenzo", "Luca", "Marco", "Matteo", "Nicolo", "Riccardo", "Simone", "Tommaso"],
            ["Bianchi", "Bruno", "Colombo", "Conti", "De Luca", "Esposito", "Ferrari", "Gallo", "Giordano", "Greco", "Lombardi", "Mancini", "Marino", "Moretti", "Ricci", "Romano", "Rossi", "Villa"]),
        ["Croatia"] = new(
            ["Ante", "Dino", "Domagoj", "Filip", "Ivan", "Josip", "Karlo", "Kristijan", "Lovro", "Luka", "Marin", "Marko", "Mateo", "Niko", "Petar", "Stipe", "Tin", "Tomislav"],
            ["Babic", "Barisic", "Horvat", "Jukic", "Kovac", "Kovacevic", "Maric", "Markovic", "Novak", "Pavic", "Peric", "Petrovic", "Simic", "Tomic", "Vidak", "Vidic", "Vukovic", "Zoric"]),
        ["Uruguay"] = new(
            ["Agustin", "Bruno", "Emiliano", "Facundo", "Federico", "Franco", "Ignacio", "Joaquin", "Juan", "Lautaro", "Lucas", "Martin", "Mateo", "Nicolas", "Rodrigo", "Santiago", "Sebastian", "Thiago"],
            ["Bentancur", "Cabrera", "Diaz", "Fernandez", "Garcia", "Gimenez", "Gonzalez", "Mendez", "Nunez", "Olivera", "Pereira", "Rodriguez", "Rojas", "Silva", "Suarez", "Torres", "Varela", "Vina"]),
        ["Colombia"] = new(
            ["Andres", "Camilo", "Carlos", "Cristian", "Daniel", "David", "Diego", "Felipe", "Juan", "Kevin", "Luis", "Mateo", "Miguel", "Nicolas", "Samuel", "Santiago", "Sebastian", "Tomas"],
            ["Aguilar", "Castro", "Diaz", "Gomez", "Gutierrez", "Herrera", "Martinez", "Medina", "Moreno", "Murillo", "Ortiz", "Quintero", "Ramirez", "Restrepo", "Rios", "Sanchez", "Torres", "Vargas"]),
        ["Norway"] = new(
            ["Aksel", "Andreas", "Eirik", "Elias", "Emil", "Henrik", "Isak", "Jakob", "Jonas", "Kristoffer", "Magnus", "Marius", "Noah", "Oskar", "Sander", "Sebastian", "Sindre", "Tobias"],
            ["Andersen", "Berg", "Dahl", "Eriksen", "Hagen", "Hansen", "Haugen", "Johansen", "Karlsen", "Kristiansen", "Larsen", "Lunde", "Nilsen", "Olsen", "Solberg", "Strand", "Svendsen", "Vik"]),
        ["Denmark"] = new(
            ["Anders", "Anton", "Christian", "Elias", "Emil", "Frederik", "Jacob", "Kasper", "Lasse", "Lucas", "Magnus", "Mikkel", "Nikolaj", "Noah", "Oliver", "Oscar", "Rasmus", "Viktor"],
            ["Andersen", "Christensen", "Eriksen", "Hansen", "Jensen", "Jorgensen", "Kristensen", "Larsen", "Madsen", "Mikkelsen", "Nielsen", "Olsen", "Pedersen", "Poulsen", "Rasmussen", "Sorensen", "Thomsen", "Vestergaard"]),
        ["Japan"] = new(
            ["Ao", "Daichi", "Haruto", "Hayato", "Hinata", "Kaito", "Keita", "Kota", "Minato", "Ren", "Riku", "Ryota", "Sora", "Sota", "Takumi", "Yuki", "Yuma", "Yuto"],
            ["Abe", "Endo", "Hayashi", "Ito", "Kato", "Kobayashi", "Maeda", "Matsuda", "Mori", "Nakamura", "Saito", "Sato", "Suzuki", "Takahashi", "Tanaka", "Watanabe", "Yamada", "Yamamoto"]),
        ["South Korea"] = new(
            ["Do-yun", "Dong-hyun", "Ha-jun", "Hyun-woo", "Ji-ho", "Ji-hoon", "Joon", "Jun-seo", "Min-jae", "Min-jun", "Seung-ho", "Seo-jun", "Si-woo", "Tae-hyun", "Woo-jin", "Ye-jun", "Young-ho", "Yu-jun"],
            ["Bae", "Cho", "Choi", "Han", "Hwang", "Jeong", "Jung", "Kang", "Kim", "Kwon", "Lee", "Lim", "Moon", "Park", "Seo", "Shin", "Song", "Yoon"])
    };

    public static string CreateUniqueName(
        string nationality,
        Random random,
        ISet<string>? usedNames = null,
        string? preferredLastName = null)
    {
        ArgumentNullException.ThrowIfNull(random);

        var profile = FindProfile(nationality);
        var firstStart = random.Next(profile.FirstNames.Length);
        var lastStart = random.Next(profile.LastNames.Length);
        var lastNames = string.IsNullOrWhiteSpace(preferredLastName)
            ? profile.LastNames
            : [preferredLastName.Trim()];

        for (var lastOffset = 0; lastOffset < lastNames.Length; lastOffset++)
        {
            var lastName = lastNames[(lastStart + lastOffset) % lastNames.Length];
            for (var firstOffset = 0; firstOffset < profile.FirstNames.Length; firstOffset++)
            {
                var firstName = profile.FirstNames[(firstStart + firstOffset) % profile.FirstNames.Length];
                var candidate = $"{firstName} {lastName}";
                if (usedNames is null || usedNames.Add(candidate))
                {
                    return candidate;
                }
            }
        }

        // A preserved regen surname can run out of first-name combinations in a
        // very long save. Fall back to the full country pool before using a suffix.
        if (!string.IsNullOrWhiteSpace(preferredLastName))
        {
            return CreateUniqueName(nationality, random, usedNames);
        }

        var baseName = $"{profile.FirstNames[firstStart]} {profile.LastNames[lastStart]}";
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (usedNames is null || usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    public static HashSet<string> CreateUsedNameSet(IEnumerable<string> names)
    {
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static NameProfile FindProfile(string nationality)
    {
        if (Profiles.TryGetValue(nationality ?? string.Empty, out var exactProfile))
        {
            return exactProfile;
        }

        return Profiles.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(nationality) &&
                nationality.Contains(item.Key, StringComparison.OrdinalIgnoreCase))
            .Value ?? FallbackProfile;
    }

    private sealed record NameProfile(string[] FirstNames, string[] LastNames);
}
