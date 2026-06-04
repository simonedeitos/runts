using System.Text.RegularExpressions;

namespace runts.Helpers;

public static partial class EmailExtractor
{
    [GeneratedRegex("[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    public static IReadOnlyCollection<string> Extract(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return EmailRegex()
            .Matches(content)
            .Select(m => m.Value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
