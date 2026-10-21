using System.Globalization;
using System.Text.RegularExpressions;

namespace VineMix.UpdateCore;

public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly string[] _prerelease;
    private readonly string _text;
    private SemanticVersion(string text, int major, int minor, int patch, string[] prerelease)
    { _text = text; Major = major; Minor = minor; Patch = patch; _prerelease = prerelease; }
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public bool IsPrerelease => _prerelease.Length != 0;
    public static SemanticVersion Parse(string value) => TryParse(value, out var version) ? version! : throw new FormatException("The release version is not valid semantic versioning.");
    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
        var match = Pattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch)) return false;
        string[] prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (prerelease.Any(part => IsNumeric(part) && part.Length > 1 && part[0] == '0')) return false;
        version = new SemanticVersion(value, major, minor, patch, prerelease); return true;
    }
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int compared = Major.CompareTo(other.Major);
        if (compared == 0) compared = Minor.CompareTo(other.Minor);
        if (compared == 0) compared = Patch.CompareTo(other.Patch);
        if (compared != 0) return compared;
        if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease ? -1 : other.IsPrerelease ? 1 : 0;
        for (int i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            string left = _prerelease[i], right = other._prerelease[i];
            bool leftNumeric = IsNumeric(left), rightNumeric = IsNumeric(right);
            compared = leftNumeric && rightNumeric ? left.Length.CompareTo(right.Length)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : 0;
            if (compared == 0) compared = StringComparer.Ordinal.Compare(left, right);
            if (compared != 0) return compared;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }
    private static bool IsNumeric(string value) => value.All(character => character is >= '0' and <= '9');
    public override string ToString() => _text;
}
