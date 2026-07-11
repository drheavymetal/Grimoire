namespace Grimoire.Library.Services;

/// <summary>
/// A record label resolved from a MusicBrainz release's label-info, ready to persist into the
/// <c>labels</c> table (SPEC §10). The MBID is the identity; country may be null until a label
/// lookup fills it in.
/// </summary>
public sealed record ResolvedLabel(Guid Mbid, string Name, string? Country);

/// <summary>
/// Pure logic that validates a MusicBrainz label reference into a <see cref="ResolvedLabel"/>.
/// A reference without a well-formed MBID or a name is dropped (never invented). Kept in the
/// shared library so the label parsing is unit-testable without HTTP or the database.
/// </summary>
public static class LabelResolver
{
    /// <summary>
    /// Validates one label reference. Returns null when the id is not a well-formed non-empty
    /// GUID or the name is blank — the release's <c>label_id</c> then stays null, a real gap.
    /// </summary>
    public static ResolvedLabel? Resolve(string? mbid, string? name, string? country)
    {
        if (!Guid.TryParse(mbid, out Guid id) || id == Guid.Empty || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new ResolvedLabel(
            id,
            name.Trim(),
            string.IsNullOrWhiteSpace(country) ? null : country.Trim());
    }

    /// <summary>
    /// Picks the first valid label from a release's label-info list. A release with several
    /// labels (co-releases) is attributed to the first that resolves; the rest are ignored,
    /// which keeps <c>releases.label_id</c> single-valued as the schema requires.
    /// </summary>
    public static ResolvedLabel? First(IEnumerable<(string? Id, string? Name, string? Country)> labelInfos)
    {
        foreach ((string? id, string? name, string? country) in labelInfos)
        {
            ResolvedLabel? label = Resolve(id, name, country);

            if (label is not null)
            {
                return label;
            }
        }

        return null;
    }
}
