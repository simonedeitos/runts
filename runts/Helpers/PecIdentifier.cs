namespace runts.Helpers;

public static class PecIdentifier
{
    private static readonly string[] Keywords =
    [
        "pec",
        "postacert",
        "legalmail",
        "pec.it",
        "register.it",
        "aruba.it"
    ];

    public static bool IsPec(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return Keywords.Any(k => email.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
