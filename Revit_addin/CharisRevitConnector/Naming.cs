using System.Globalization;

namespace CharisRevitConnector;

/// <summary>
/// Naming convention for Revit types/symbols created from Firebase. Every such
/// type uses the universal "Test" prefix and encodes its defining size so the
/// name always reflects the geometry. Types are shared by size (+ material),
/// created on demand, e.g.:
///   "Test - Floor - 1 - concrete", "Test - Wall - 0.5 - concrete",
///   "Test - Beam - 1x2", "Test - Column - 1x1".
/// </summary>
internal static class Naming
{
    public const string Prefix = "Test";

    /// <summary>Format a length (feet) compactly: 1, 0.5, 8, 1.25 ...</summary>
    public static string Num(double feet) => feet.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FloorTypeName(double thicknessFeet, string? material) =>
        Compose("Floor", Num(thicknessFeet), material);

    public static string WallTypeName(double thicknessFeet, string? material) =>
        Compose("Wall", Num(thicknessFeet), material);

    public static string BeamTypeName(double b, double h) => $"{Prefix} - Beam - {Num(b)}x{Num(h)}";

    public static string ColumnTypeName(double b, double h) => $"{Prefix} - Column - {Num(b)}x{Num(h)}";

    public static string FloorPrefix { get; } = $"{Prefix} - Floor - ";
    public static string WallPrefix { get; } = $"{Prefix} - Wall - ";
    public static string BeamPrefix { get; } = $"{Prefix} - Beam - ";
    public static string ColumnPrefix { get; } = $"{Prefix} - Column - ";

    private static string Compose(string family, string size, string? material) =>
        string.IsNullOrWhiteSpace(material)
            ? $"{Prefix} - {family} - {size}"
            : $"{Prefix} - {family} - {size} - {material}";
}
