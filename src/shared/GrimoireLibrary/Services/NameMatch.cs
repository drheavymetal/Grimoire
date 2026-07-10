using System.Globalization;
using System.Text;

namespace Grimoire.Library.Services;

/// <summary>
/// Conservative name matching for streaming lookups. The preview spikes (DECISIONS D22/D25)
/// were poisoned by loose matching — "Toto" and the wrong "Death" crept in — so a candidate
/// counts only when its name equals the query after case-folding, diacritic-stripping and
/// whitespace collapse. This deliberately undercounts (the 52 % coverage is a lower bound),
/// which is the honest failure: a missing preview, never a wrong band's audio.
/// </summary>
public static class NameMatch
{
    /// <summary>True when two names are equal after normalisation.</summary>
    public static bool Matches(string a, string b)
    {
        return Normalize(a) == Normalize(b);
    }

    /// <summary>Lower-cases, strips diacritics and collapses whitespace.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new(decomposed.Length);
        bool lastWasSpace = false;

        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);

            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }
}
