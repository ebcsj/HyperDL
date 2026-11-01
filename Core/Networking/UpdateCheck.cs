namespace HyperDL.Core;

/// <summary>
/// Pure version-comparison logic for the startup update check. Accepts tags in
/// the release style (v1.3.0, v1.3.0-beta.1); anything unparseable is ignored.
/// </summary>
public static class UpdateCheck
{
    public static bool IsNewerVersion(string? currentVersion, string? latestTag)
    {
        var current = Parse(currentVersion);
        var latest = Parse(latestTag);
        if (current is null || latest is null)
        {
            return false;
        }

        var numeric = Compare(current.Value.Numeric, latest.Value.Numeric);
        if (numeric != 0)
        {
            return numeric < 0;
        }

        // Same numbers: the plain (stable) release is newer than a prerelease,
        // and two prereleases compare by their identifier text.
        if (current.Value.IsPrerelease && latest.Value.IsPrerelease)
        {
            return string.CompareOrdinal(latest.Value.Prerelease, current.Value.Prerelease) > 0;
        }

        return current.Value.IsPrerelease && !latest.Value.IsPrerelease;
    }

    private static int Compare(int[] left, int[] right)
    {
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var l = index < left.Length ? left[index] : 0;
            var r = index < right.Length ? right[index] : 0;
            if (l != r)
            {
                return l.CompareTo(r);
            }
        }

        return 0;
    }

    private static (int[] Numeric, bool IsPrerelease, string Prerelease)? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimStart('v', 'V');
        var dash = trimmed.IndexOf('-');
        var isPrerelease = dash >= 0;
        var numericText = isPrerelease ? trimmed[..dash] : trimmed;
        var parts = numericText.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return null;
        }

        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var number) || number < 0)
            {
                return null;
            }

            numbers[index] = number;
        }

        return (numbers, isPrerelease, isPrerelease ? trimmed[(dash + 1)..] : string.Empty);
    }
}
