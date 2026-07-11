namespace Grimoire.Library.Services;

/// <summary>
/// The outcome of trying to resolve a curated composer name to a single MusicBrainz entity.
/// </summary>
public enum ComposerMatchStatus
{
    /// <summary>Exactly one unambiguous Person matched by name.</summary>
    Resolved,

    /// <summary>No Person matched the name in any of its forms.</summary>
    NotFound,

    /// <summary>More than one distinct Person matched the name: ambiguous, never guessed.</summary>
    Ambiguous,
}

/// <summary>
/// One MusicBrainz artist candidate, reduced to the fields the resolver needs: the id, the primary
/// name, the sort-name, the type, and the recorded aliases (transliterations, name orders, historical
/// spellings). MusicBrainz stores many composers under their native spelling — Chopin as "Fryderyk
/// Chopin", Bartók as "Bartók Béla", Stravinsky in Cyrillic — with the common form only as an alias,
/// so the alias list is what lets an entity be recognised without guessing.
/// </summary>
public readonly record struct ComposerCandidate(
    string Id,
    string Name,
    string? SortName,
    string? Type,
    IReadOnlyList<string> Aliases);

/// <summary>The resolved match: a status plus the MBID when (and only when) it is unambiguous.</summary>
public readonly record struct ComposerMatch(ComposerMatchStatus Status, string? Mbid);

/// <summary>
/// Pure logic that picks a single composer MBID from a list of MusicBrainz search candidates, or
/// refuses (movement VII, D11; same discipline as the D23 anchors: a name that does not resolve to
/// exactly one Person is logged and skipped, never guessed). A candidate matches when the queried
/// name equals — after case-folding and diacritic-stripping (<see cref="NameMatch.Normalize"/>) —
/// any of its recorded name forms: primary name, sort-name, or an alias. Exactly one distinct Person
/// MBID across all candidates → Resolved; zero → NotFound; more → Ambiguous. Kept free of HTTP types
/// so it can be unit-tested directly.
/// </summary>
public static class ComposerResolver
{
    public static ComposerMatch Resolve(string name, IEnumerable<ComposerCandidate> candidates)
    {
        string wanted = NameMatch.Normalize(name);

        if (wanted.Length == 0)
        {
            return new ComposerMatch(ComposerMatchStatus.NotFound, null);
        }

        List<string> matchedIds = candidates
            .Where(c => IsPerson(c.Type))
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .Where(c => NameForms(c).Any(form => NameMatch.Normalize(form) == wanted))
            .Select(c => c.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matchedIds.Count switch
        {
            0 => new ComposerMatch(ComposerMatchStatus.NotFound, null),
            1 => new ComposerMatch(ComposerMatchStatus.Resolved, matchedIds[0]),
            _ => new ComposerMatch(ComposerMatchStatus.Ambiguous, null),
        };
    }

    private static IEnumerable<string> NameForms(ComposerCandidate c)
    {
        yield return c.Name;

        if (!string.IsNullOrWhiteSpace(c.SortName))
        {
            yield return c.SortName;
        }

        foreach (string alias in c.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return alias;
            }
        }
    }

    private static bool IsPerson(string? type)
    {
        return string.Equals(type, "Person", StringComparison.OrdinalIgnoreCase);
    }
}
