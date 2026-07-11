using Grimoire.Server.Dtos;

namespace Grimoire.Server.Services;

/// <summary>
/// Pure grouping for a composer's works (movement VII, D11). Turns the flat <c>works</c> rows into
/// the grouped shape the composer page renders as its hero: works gathered by <c>kind</c>, with the
/// untyped ones (MusicBrainz gave no type — 1879 of 2291 rows) kept in their own "unclassified"
/// group rather than hidden.
///
/// <para>
/// Kept database-free so the one decision that matters — that a null kind is a real group, not a
/// dropped row — is unit-tested directly. Nothing is invented: a work's kind is exactly what
/// MusicBrainz said, or null.
/// </para>
/// </summary>
public static class WorkGrouping
{
    /// <summary>One flat work row, kind null when MusicBrainz assigned none.</summary>
    public sealed record WorkRow(Guid Id, Guid Mbid, string Title, string? Kind);

    /// <summary>
    /// Groups the rows by kind. Named kinds come first, ordered case-insensitively; the
    /// unclassified group (kind null) comes last so it never leads the page. Works inside a group
    /// are ordered by title. A kind that is present but blank/whitespace is treated as unclassified
    /// (it is not a real type). Grouping is case-insensitive so "Sonata"/"sonata" do not split.
    /// </summary>
    public static IReadOnlyList<WorkGroupDto> Group(IEnumerable<WorkRow> works)
    {
        ArgumentNullException.ThrowIfNull(works);

        // Normalised kind used for grouping (null or blank => unclassified). Keeps the first
        // non-blank spelling seen for the display label so the heading reads naturally.
        var groups = new Dictionary<string, (string? Label, List<WorkRow> Rows)>(StringComparer.OrdinalIgnoreCase);

        foreach (WorkRow work in works)
        {
            bool unclassified = string.IsNullOrWhiteSpace(work.Kind);
            string key = unclassified ? UnclassifiedKey : work.Kind!.Trim();

            if (!groups.TryGetValue(key, out (string? Label, List<WorkRow> Rows) group))
            {
                group = (unclassified ? null : key, []);
                groups[key] = group;
            }

            group.Rows.Add(work);
        }

        return groups
            .Select(kvp => new WorkGroupDto(
                kvp.Value.Label,
                kvp.Value.Rows
                    .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(w => w.Id)
                    .Select(w => new WorkDto(w.Id, w.Mbid, w.Title, w.Kind))
                    .ToList()))
            // Named kinds first (alphabetical), the unclassified group always last.
            .OrderBy(g => g.Kind is null)
            .ThenBy(g => g.Kind, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Dictionary key for the untyped group (a real title can never collide with it).</summary>
    private const string UnclassifiedKey = "\0unclassified";
}
