using Grimoire.Library.Models;

namespace Grimoire.Library.Enrichment;

/// <summary>
/// An optional, feature-flagged source of extra data about an artist (Invariant 5 /
/// DECISIONS D9). Every external data source hides behind this contract so that no
/// view breaks when a source is missing — and sources <em>will</em> be missing, because
/// coverage is worst exactly for the obscure bands the app exists to serve. A disabled
/// source is simply skipped; it never throws and never invents data.
/// </summary>
public interface IEnrichmentSource
{
    /// <summary>Stable, human-readable name used in logs and progress reports.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this source is turned on. Driven by configuration so a source can be cut
    /// without code changes. When false the orchestrator must not call it.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Fetches enrichment for one artist, or <c>null</c> when the source has nothing for
    /// it. A null result is a legitimate gap, never an error to mask.
    /// </summary>
    Task<ArtistEnrichment?> FetchAsync(Artist artist, CancellationToken ct);
}
