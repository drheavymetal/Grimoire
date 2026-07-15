namespace Grimoire.Server.Services;

/// <summary>
/// Normalises a raw needle used in an <c>ILIKE</c> lane — the Rite's genre/theme filters and the
/// browse door. Trims, lower-cases, drops empties, and caps the length so a pathological input
/// cannot turn a substring match into a table scan of an enormous pattern. Returns <c>null</c> when
/// there is nothing to match on, which every caller reads as "do not narrow the pool".
/// </summary>
public static class SearchNeedle
{
    /// <summary>The longest needle we honour; anything past it is truncated (an ILIKE guard, not a UX rule).</summary>
    public const int MaxLength = 64;

    /// <summary>The cleaned needle, or <c>null</c> when the input is null, blank or whitespace.</summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim().ToLowerInvariant();

        return trimmed.Length > MaxLength ? trimmed[..MaxLength] : trimmed;
    }
}
