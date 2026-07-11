namespace Grimoire.Worker.PersonLinks;

/// <summary>
/// Batch limit for the person-links pass. At 1 req/s a full pass over ~2000 people takes over
/// half an hour, so each pass processes a bounded batch and declares the remainder. Overridable
/// with <c>GRIMOIRE_PERSONLINKS_LIMIT</c>.
/// </summary>
public sealed class PersonLinksOptions
{
    public int Limit { get; init; } = 300;
}
