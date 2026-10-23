namespace FootballSimulation.Services;

public static class MentalSystemSettings
{
    public const string EnvironmentVariableName = "FOOTBALLSIM_MENTAL_SYSTEM";

    private static bool? _override;

    public static bool IsEnabled
    {
        get => _override ?? ParseEnabled(Environment.GetEnvironmentVariable(EnvironmentVariableName));
        set => _override = value;
    }

    public static void ClearOverride() => _override = null;

    private static bool ParseEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }
}
