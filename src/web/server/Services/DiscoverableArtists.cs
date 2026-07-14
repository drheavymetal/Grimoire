using Grimoire.Library.Models;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// What may be handed to a user as an artist to discover.
///
/// <para>
/// Two conditions, and the second one is the one that bites. <b>An embedding</b>, because without a
/// vector an artist has no place on the map. And <b>a discography</b>, because an artist with no
/// release of its own is not an act at all.
/// </para>
///
/// <para>
/// The catalogue holds 66 554 people, because the Bloodline is built by expanding the corpus through
/// members (D23) — every session drummer and touring bassist gets a row so an edge can point at it.
/// 49 534 of them have an embedding and <b>not one release</b>. They were in every pool: the ring
/// (D4/D26/D31), the Weekly Rite, the Dark Twin, semantic search. The Rite served them as bands.
/// </para>
///
/// <para>
/// And it was worse than an odd name on screen. The preview resolver matches iTunes <b>by name</b>
/// (D40/D25), so a bassist called Lee Freeman — no records, no band of his own — was served with the
/// audio of a <em>different</em> Lee Freeman who happens to be on iTunes. Blind. As a discovery. The
/// Rite was handing out a stranger's music and calling it a find.
/// </para>
///
/// <para>
/// Note what this is NOT: a "no people" rule. Burzum is a Person. So is every composer in movement VII.
/// Filtering by <see cref="ArtistKind"/> would throw them out with the drummers. A discography keeps
/// them and drops only what was never an act: 175 230 embedded → <b>100 915 discoverable</b>.
/// </para>
/// </summary>
public static class DiscoverableArtists
{
    /// <summary>The artists that may be served, recommended, searched or linked to. See the class remarks.</summary>
    public static IQueryable<Artist> Discoverable(this IQueryable<Artist> artists)
    {
        return artists.Where(a => a.Embedding != null && a.Releases.Any());
    }
}
