namespace FootballSimulation.Models;

public class FormationPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Formation { get; set; } = "4-3-3";
    public List<FormationPresetSlot> StartingXI { get; set; } = [];
    public List<FormationPresetPlayerRef> Bench { get; set; } = [];
    public TeamTactics Tactics { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Saved Setup" : Name;
}

public class FormationPresetSlot
{
    public string Slot { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}

public class FormationPresetPlayerRef
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}

public sealed record FormationPresetOperationResult(bool Success, string Message, FormationPreset? Preset = null)
{
    public static FormationPresetOperationResult Succeeded(string message, FormationPreset? preset = null) => new(true, message, preset);

    public static FormationPresetOperationResult Failed(string message) => new(false, message);
}

public sealed record FormationPresetApplyResult(bool Success, string Message, IReadOnlyList<string> Warnings)
{
    public static FormationPresetApplyResult Succeeded(IReadOnlyList<string> warnings) => new(true, string.Empty, warnings);

    public static FormationPresetApplyResult Failed(string message, IReadOnlyList<string>? warnings = null) => new(false, message, warnings ?? []);
}
