using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// A membership resolved from a single MusicBrainz "member of band" relation, expressed
/// as a directed <c>member_of</c> edge (member → band) with dates and instruments. This
/// is the raw material of the lineup timeline (B7/B8) and of the D23 admission criterion
/// (Bloodline as the corpus-expansion rule).
/// </summary>
public sealed record ResolvedMembership(
    Guid MemberMbid,
    string MemberName,
    string? MemberSortName,
    ArtistKind MemberKind,
    Guid BandMbid,
    string BandName,
    DateOnly? Begin,
    DateOnly? End,
    string[] Instruments);

/// <summary>
/// Pure logic that turns one MusicBrainz artist-relation into a <see cref="ResolvedMembership"/>,
/// independent of the JSON DTOs. It resolves which endpoint is the member and which is the
/// band from the relation's <c>direction</c>, filters out non-membership and guest relations
/// (official member ≠ guest — SPEC section 4), and normalises attributes into instruments.
/// Kept in the shared library, free of HTTP types, so it can be unit-tested directly.
/// </summary>
public static class MembershipResolver
{
    /// <summary>The only MusicBrainz relation type that denotes official band membership.</summary>
    public const string MemberOfBandType = "member of band";

    // Attributes on a "member of band" relation that qualify the membership rather than
    // name an instrument. "guest" additionally demotes the relation from official.
    private static readonly HashSet<string> NonInstrumentAttributes =
        new(StringComparer.OrdinalIgnoreCase) { "original", "additional", "guest" };

    /// <summary>
    /// Resolves one relation fetched while querying <paramref name="queriedMbid"/>. Returns
    /// null when the relation is not an official band membership (wrong type, guest attribute,
    /// or unparseable ids), so the caller never fabricates an edge.
    /// </summary>
    /// <param name="relationType">The MusicBrainz relation type (e.g. "member of band").</param>
    /// <param name="direction">"backward" or "forward", relative to the queried artist.</param>
    /// <param name="queriedMbid">MBID of the artist whose relations were fetched.</param>
    /// <param name="queriedName">Name of the queried artist.</param>
    /// <param name="queriedSortName">Sort-name of the queried artist, if known.</param>
    /// <param name="queriedKind">Kind of the queried artist.</param>
    /// <param name="targetMbid">MBID of the artist on the other end of the relation.</param>
    /// <param name="targetName">Name of the target artist.</param>
    /// <param name="targetSortName">Sort-name of the target artist, if known.</param>
    /// <param name="targetKind">Kind of the target artist.</param>
    /// <param name="begin">Raw begin value (year, year-month or full date), may be null.</param>
    /// <param name="end">Raw end value, may be null.</param>
    /// <param name="attributes">Relation attributes (instruments plus qualifiers), may be null.</param>
    public static ResolvedMembership? Resolve(
        string? relationType,
        string? direction,
        Guid queriedMbid,
        string queriedName,
        string? queriedSortName,
        ArtistKind queriedKind,
        Guid targetMbid,
        string targetName,
        string? targetSortName,
        ArtistKind targetKind,
        string? begin,
        string? end,
        IReadOnlyList<string>? attributes)
    {
        if (!string.Equals(relationType, MemberOfBandType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (attributes is not null && attributes.Any(a => string.Equals(a, "guest", StringComparison.OrdinalIgnoreCase)))
        {
            // A guest is not an official member; guests live in recording credits, not here.
            return null;
        }

        // "member of band" is directed member → band. When we queried the band the member
        // sits on the far ("backward") end; when we queried the person the band does.
        bool queriedIsBand = string.Equals(direction, "backward", StringComparison.OrdinalIgnoreCase);

        Guid memberMbid = queriedIsBand ? targetMbid : queriedMbid;
        string memberName = queriedIsBand ? targetName : queriedName;
        string? memberSortName = queriedIsBand ? targetSortName : queriedSortName;
        ArtistKind memberKind = queriedIsBand ? targetKind : queriedKind;

        Guid bandMbid = queriedIsBand ? queriedMbid : targetMbid;
        string bandName = queriedIsBand ? queriedName : targetName;

        if (memberMbid == Guid.Empty || bandMbid == Guid.Empty || memberMbid == bandMbid)
        {
            return null;
        }

        string[] instruments = ExtractInstruments(attributes);

        return new ResolvedMembership(
            memberMbid,
            memberName,
            memberSortName,
            memberKind,
            bandMbid,
            bandName,
            ParseDate(begin),
            ParseDate(end),
            instruments);
    }

    /// <summary>
    /// Merges two membership records for the same (member, band) pair into one edge, as the
    /// schema keeps a single edge per pair. Takes the earliest begin and the latest end, but
    /// an open end (still active) wins over any closed end, and unions the instruments.
    /// </summary>
    public static ResolvedMembership Merge(ResolvedMembership a, ResolvedMembership b)
    {
        DateOnly? begin = MinDate(a.Begin, b.Begin);

        // If either stint is open-ended (member still active), the merged edge is open.
        DateOnly? end = (a.End is null || b.End is null) ? null : MaxDate(a.End, b.End);

        string[] instruments = a.Instruments
            .Concat(b.Instruments)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return a with { Begin = begin, End = end, Instruments = instruments };
    }

    private static string[] ExtractInstruments(IReadOnlyList<string>? attributes)
    {
        if (attributes is null)
        {
            return [];
        }

        return attributes
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Where(a => !NonInstrumentAttributes.Contains(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateOnly? MinDate(DateOnly? a, DateOnly? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a.Value <= b.Value ? a : b;
    }

    private static DateOnly? MaxDate(DateOnly? a, DateOnly? b)
    {
        if (a is null || b is null)
        {
            return null;
        }

        return a.Value >= b.Value ? a : b;
    }

    /// <summary>
    /// Parses a MusicBrainz partial date ("1991", "1991-03", "1991-03-15") into a
    /// <see cref="DateOnly"/>, filling missing parts with the first of the period. Returns
    /// null for empty or unparseable input rather than guessing.
    /// </summary>
    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0 || !int.TryParse(parts[0], out int year) || year < 1 || year > 9999)
        {
            return null;
        }

        int month = 1;
        int day = 1;

        if (parts.Length >= 2 && int.TryParse(parts[1], out int m) && m is >= 1 and <= 12)
        {
            month = m;
        }

        if (parts.Length >= 3 && int.TryParse(parts[2], out int d) && d is >= 1 and <= 31)
        {
            day = d;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Day-of-month out of range for the month (e.g. "1991-02-30"): fall back to the month.
            return new DateOnly(year, month, 1);
        }
    }
}
