namespace Grimoire.Worker.Credits;

/// <summary>
/// Batch limits for the credits / labels ETL. At MusicBrainz's strict 1 req/s, a full pass over
/// thousands of release-groups takes hours, so each pass processes a bounded batch and declares
/// how many remain (the run is resumable). Overridable with <c>GRIMOIRE_CREDITS_LIMIT</c> and
/// <c>GRIMOIRE_LABEL_COUNTRY_LIMIT</c>.
/// </summary>
public sealed class CreditsOptions
{
    /// <summary>Max release-groups to fetch from MusicBrainz in one pass.</summary>
    public int Limit { get; init; } = 300;

    /// <summary>Editions to request per release-group so a good (official, early) one can be chosen.</summary>
    public int ReleasesPerGroup { get; init; } = 5;

    /// <summary>Max distinct labels to look up for their country in one labels pass.</summary>
    public int LabelCountryLimit { get; init; } = 200;
}
