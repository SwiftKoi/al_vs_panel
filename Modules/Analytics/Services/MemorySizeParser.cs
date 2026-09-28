using System.Globalization;

namespace AlegacyWebPanel.Modules.Analytics.Services;

// Parses Docker-style sizes such as "7.605GiB", "512MiB", "1.2GB", or "900kB".
public static class MemorySizeParser
{
    private static readonly (string Suffix, double Factor)[] Units =
    [
        ("TiB", Math.Pow(1024, 4)), ("GiB", Math.Pow(1024, 3)), ("MiB", Math.Pow(1024, 2)), ("KiB", 1024),
        ("TB", 1e12), ("GB", 1e9), ("MB", 1e6), ("kB", 1e3), ("KB", 1e3), ("B", 1)
    ];

    public static long ToBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var trimmed = value.Trim();
        foreach (var (suffix, factor) in Units)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal) &&
                double.TryParse(trimmed[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                return (long)(amount * factor);
            }
        }

        return 0;
    }
}
