using System.Text.RegularExpressions;

namespace EasySearch.Helpers;

public static partial class PhoneExtractor
{
    [GeneratedRegex("(?:(?:\\+|00)39)?\\s?3\\d{2}[\\s.-]?\\d{6,7}|(?:(?:\\+|00)39)?\\s?0\\d{1,3}[\\s.-]?\\d{5,8}", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    public static IReadOnlyCollection<string> Extract(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return PhoneRegex()
            .Matches(content)
            .Select(m => Regex.Replace(m.Value, "\\s+", " ").Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
