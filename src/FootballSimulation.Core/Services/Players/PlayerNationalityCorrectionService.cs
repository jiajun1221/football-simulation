using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerNationalityCorrectionService
{
    private const string NetherlandsCode = "NL";
    private const string NetherlandsFlagPath = "/Assets/Flags/netherlands.png";

    public static void ApplyKnownCorrections(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var isNetherlandsPlayer = player.Nationality.Equals("Netherlands", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityName.Equals("Netherlands", StringComparison.OrdinalIgnoreCase);
        var hasGenericEnglandIdentity = player.NationalityCode.Equals("GB-ENG", StringComparison.OrdinalIgnoreCase) ||
            player.FlagImagePath.EndsWith("/england.png", StringComparison.OrdinalIgnoreCase);

        if (!isNetherlandsPlayer || !hasGenericEnglandIdentity)
        {
            return;
        }

        player.Nationality = "Netherlands";
        player.NationalityName = "Netherlands";
        player.NationalityCode = NetherlandsCode;
        player.FlagImagePath = NetherlandsFlagPath;
    }
}
