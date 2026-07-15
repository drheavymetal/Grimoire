namespace Grimoire.Server.Services;

/// <summary>
/// Validates and normalises a public friend handle (FRIENDS wave). A handle is 3-30 characters of
/// <c>[a-z0-9_]</c>. Validation is case-insensitive and the accepted value is lower-cased, so
/// "Mercyful_Fate" and "mercyful_fate" are the same handle and only one may exist. Pure and
/// deterministic — unit-tested without a database.
/// </summary>
public static class HandleValidator
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    /// <summary>
    /// Returns the normalised (lower-cased, trimmed) handle when the input is well-formed, or null
    /// when it is not — the caller answers a null with a 400 and never persists an invalid handle.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        string trimmed = raw.Trim().ToLowerInvariant();

        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            return null;
        }

        foreach (char c in trimmed)
        {
            bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!allowed)
            {
                return null;
            }
        }

        return trimmed;
    }

    /// <summary>True when the input is a well-formed handle.</summary>
    public static bool IsValid(string? raw)
    {
        return Normalize(raw) is not null;
    }
}
