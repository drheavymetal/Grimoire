namespace Grimoire.Library.Services;

/// <summary>
/// One facet of a MusicBrainz artist relation, mapped onto the domain's credit vocabulary
/// (SPEC §4/§10): a <see cref="Role"/> (performer | producer | engineer | mix | master), an
/// optional <see cref="Instrument"/>, and whether the performance was as a guest rather than
/// an official member. A single relation can yield several facets (one performer plays two
/// instruments), so mapping returns a list.
/// </summary>
public sealed record CreditFacet(string Role, string? Instrument, bool IsGuest);

/// <summary>
/// A resolved credit, ready to persist: the corpus artist's MusicBrainz id, the recording it
/// is on (null for a release-level credit), and the facet fields. The worker turns each of
/// these into a <c>credits</c> row, mapping <see cref="ArtistMbid"/> to our artist id.
/// </summary>
public sealed record ResolvedCredit(Guid ArtistMbid, Guid? RecordingMbid, string Role, string? Instrument, bool IsGuest);

/// <summary>
/// Pure logic that turns a MusicBrainz artist-artist / artist-recording relation into credit
/// facets, independent of the JSON DTOs and of the database. It maps the relation type to a
/// role, extracts instruments from the relation attributes, and flags guests (official member
/// ≠ guest — SPEC §4). Kept in the shared library, free of HTTP types, so it can be unit-tested
/// directly. Anything it cannot map — an unknown relation type, an artist outside our corpus —
/// yields no credit; nothing is invented (autonomous-mode rule / REVIEW.md).
/// </summary>
public static class CreditResolver
{
    public const string RolePerformer = "performer";
    public const string RoleProducer = "producer";
    public const string RoleEngineer = "engineer";
    public const string RoleMix = "mix";
    public const string RoleMaster = "master";

    /// <summary>The default vocal descriptor when a "vocal" relation names no specific kind.</summary>
    public const string Vocals = "vocals";

    // Attributes that qualify a performance rather than name an instrument. "guest" additionally
    // demotes the credit from official (SPEC §4). These are stripped before an attribute is read
    // as an instrument, matching the MembershipResolver's treatment of the same vocabulary.
    private static readonly HashSet<string> QualifierAttributes =
        new(StringComparer.OrdinalIgnoreCase) { "guest", "additional", "original", "minor", "solo" };

    /// <summary>
    /// Resolves one relation into persistable credits, keeping it only when the artist is in our
    /// corpus (<paramref name="corpusMbids"/>) — the MBID is the join key, and an artist we do
    /// not already carry is discarded, never created. Returns an empty list for an unmapped
    /// relation type or an out-of-corpus artist.
    /// </summary>
    public static IReadOnlyList<ResolvedCredit> Resolve(
        string? relationType,
        IReadOnlyList<string>? attributes,
        Guid artistMbid,
        Guid? recordingMbid,
        IReadOnlySet<Guid> corpusMbids)
    {
        if (artistMbid == Guid.Empty || !corpusMbids.Contains(artistMbid))
        {
            return [];
        }

        IReadOnlyList<CreditFacet> facets = Facets(relationType, attributes);

        if (facets.Count == 0)
        {
            return [];
        }

        List<ResolvedCredit> credits = new(facets.Count);

        foreach (CreditFacet facet in facets)
        {
            credits.Add(new ResolvedCredit(artistMbid, recordingMbid, facet.Role, facet.Instrument, facet.IsGuest));
        }

        return credits;
    }

    /// <summary>
    /// Maps a MusicBrainz relation type plus its attributes to zero or more credit facets.
    /// The heart of the parser, pure over primitives so it is trivially testable.
    /// </summary>
    public static IReadOnlyList<CreditFacet> Facets(string? relationType, IReadOnlyList<string>? attributes)
    {
        if (string.IsNullOrWhiteSpace(relationType))
        {
            return [];
        }

        bool isGuest = attributes is not null
            && attributes.Any(a => string.Equals(a, "guest", StringComparison.OrdinalIgnoreCase));

        switch (relationType.Trim().ToLowerInvariant())
        {
            case "instrument":
            {
                string[] instruments = Instruments(attributes);

                if (instruments.Length == 0)
                {
                    // A bare "instrument" relation with only qualifiers: still a performer.
                    return [new CreditFacet(RolePerformer, null, isGuest)];
                }

                return instruments.Select(i => new CreditFacet(RolePerformer, i, isGuest)).ToList();
            }

            case "vocal":
            case "vocals":
                return [new CreditFacet(RolePerformer, Vocal(attributes), isGuest)];

            case "performer":
            case "performing orchestra":
                return [new CreditFacet(RolePerformer, null, isGuest)];

            case "producer":
                return [new CreditFacet(RoleProducer, null, isGuest)];

            case "engineer":
            case "recording":
            case "audio":
                return [new CreditFacet(RoleEngineer, null, isGuest)];

            case "mix":
                return [new CreditFacet(RoleMix, null, isGuest)];

            case "mastering":
                return [new CreditFacet(RoleMaster, null, isGuest)];

            default:
                // Any other artist relation (composer, lyricist, artwork, …) is not a
                // performance/production credit in SPEC's vocabulary: dropped here.
                return [];
        }
    }

    private static string[] Instruments(IReadOnlyList<string>? attributes)
    {
        if (attributes is null)
        {
            return [];
        }

        return attributes
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Where(a => !QualifierAttributes.Contains(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Vocal(IReadOnlyList<string>? attributes)
    {
        // A "vocal" relation may name the kind ("lead vocals", "choir vocals"); if it names only
        // qualifiers, or nothing, fall back to the generic "vocals".
        string? descriptor = attributes?
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .FirstOrDefault(a => !QualifierAttributes.Contains(a));

        return string.IsNullOrWhiteSpace(descriptor) ? Vocals : descriptor;
    }
}
