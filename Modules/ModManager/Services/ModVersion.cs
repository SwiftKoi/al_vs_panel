using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AlegacyWebPanel.Modules.ModManager.Services;

/// <summary>
/// A mod or game version such as <c>1.22.7</c>, <c>v4.0.0-rc.10</c> or <c>1.0.0-pre.2</c>.
/// Numeric parts compare numerically (missing parts are zero), a pre-release sorts before its
/// release, and pre-release labels compare part by part (<c>rc.9</c> &lt; <c>rc.10</c>).
/// Build metadata after <c>+</c> is ignored.
/// </summary>
public sealed partial class ModVersion : IComparable<ModVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _prerelease;

    private ModVersion(string text, int[] numbers, string[] prerelease)
    {
        Text = text;
        _numbers = numbers;
        _prerelease = prerelease;
    }

    public string Text { get; }
    public int Major => _numbers[0];
    public int Minor => _numbers.Length > 1 ? _numbers[1] : 0;
    public bool IsPrerelease => _prerelease.Length > 0;

    public static bool TryParse(string? value, [NotNullWhen(true)] out ModVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var numbers = match.Groups["numbers"].Value.Split('.').Select(int.Parse).ToArray();
        var prerelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries)
            : [];
        version = new ModVersion(value.Trim(), numbers, prerelease);
        return true;
    }

    public static int Compare(string? left, string? right)
    {
        var hasLeft = TryParse(left, out var a);
        var hasRight = TryParse(right, out var b);
        return (hasLeft, hasRight) switch
        {
            (true, true) => a!.CompareTo(b),
            (true, false) => 1,
            (false, true) => -1,
            _ => string.Compare(left, right, StringComparison.OrdinalIgnoreCase)
        };
    }

    public int CompareTo(ModVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var length = Math.Max(_numbers.Length, other._numbers.Length);
        for (var index = 0; index < length; index++)
        {
            var result = Part(_numbers, index).CompareTo(Part(other._numbers, index));
            if (result != 0)
            {
                return result;
            }
        }

        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        for (var index = 0; index < Math.Max(_prerelease.Length, other._prerelease.Length); index++)
        {
            if (index >= _prerelease.Length) return -1;
            if (index >= other._prerelease.Length) return 1;
            var left = _prerelease[index];
            var right = other._prerelease[index];
            var leftNumeric = int.TryParse(left, out var leftNumber);
            var rightNumeric = int.TryParse(right, out var rightNumber);
            var result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.Compare(left, right, StringComparison.OrdinalIgnoreCase)
            };
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public override string ToString() => Text;

    private static int Part(int[] numbers, int index) => index < numbers.Length ? numbers[index] : 0;

    [GeneratedRegex(@"^[vV]?(?<numbers>\d{1,9}(?:\.\d{1,9}){0,3})(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]*)?$")]
    private static partial Regex VersionPattern();
}
