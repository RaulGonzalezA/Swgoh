namespace Swgoh.Application.Investments;

public enum InventoryResourceKind
{
    Currency = 1,
    RelicMaterial = 2,
    SignalData = 3
}

public sealed record InventoryResourceDefinition(
    string Id,
    string Name,
    InventoryResourceKind Kind,
    int SortOrder);

public sealed record PlayerInventoryResource(
    string Id,
    string Name,
    long Quantity);

public sealed record PlayerInventorySnapshot(
    long AllyCode,
    DateTimeOffset CapturedAtUtc,
    string Source,
    IReadOnlyCollection<PlayerInventoryResource> Resources);

public sealed record PlayerInventoryImport(
    DateTimeOffset? CapturedAtUtc,
    string? Source,
    IReadOnlyCollection<PlayerInventoryResource> Resources);

public static class PlayerInventoryCatalog
{
    public static IReadOnlyCollection<InventoryResourceDefinition> Resources { get; } =
    [
        new("credits", "Créditos", InventoryResourceKind.Currency, 10),
        new("carbonite_circuit_board", "Carbonite Circuit Board", InventoryResourceKind.RelicMaterial, 20),
        new("bronzium_wiring", "Bronzium Wiring", InventoryResourceKind.RelicMaterial, 30),
        new("chromium_transistor", "Chromium Transistor", InventoryResourceKind.RelicMaterial, 40),
        new("aurodium_heatsink", "Aurodium Heatsink", InventoryResourceKind.RelicMaterial, 50),
        new("electrium_conductor", "Electrium Conductor", InventoryResourceKind.RelicMaterial, 60),
        new("zinbiddle_card", "Zinbiddle Card", InventoryResourceKind.RelicMaterial, 70),
        new("impulse_detector", "Impulse Detector", InventoryResourceKind.RelicMaterial, 80),
        new("aeromagnifier", "Aeromagnifier", InventoryResourceKind.RelicMaterial, 90),
        new("gyrda_keypad", "Gyrda Keypad", InventoryResourceKind.RelicMaterial, 100),
        new("droid_brain", "Droid Brain", InventoryResourceKind.RelicMaterial, 110),
        new("coaxial_servomotor", "Coaxial Servomotor", InventoryResourceKind.RelicMaterial, 120),
        new("signal_data_fragmented", "Fragmented Signal Data", InventoryResourceKind.SignalData, 130),
        new("signal_data_incomplete", "Incomplete Signal Data", InventoryResourceKind.SignalData, 140),
        new("signal_data_flawed", "Flawed Signal Data", InventoryResourceKind.SignalData, 150),
        new("signal_data_corrupted", "Corrupted Signal Data", InventoryResourceKind.SignalData, 160)
    ];

    public static bool TryNormalize(string value, out InventoryResourceDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = Normalize(value);
        definition = Resources.FirstOrDefault(resource =>
            string.Equals(Normalize(resource.Id), normalized, StringComparison.Ordinal)
            || string.Equals(Normalize(resource.Name), normalized, StringComparison.Ordinal))!;
        return definition is not null;
    }

    private static string Normalize(string value) => string.Concat(
        value.Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character)));
}
