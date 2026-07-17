using Microsoft.AspNetCore.Identity;

namespace Grimoire.Library.Models;

/// <summary>
/// Application user. Backed by ASP.NET Identity with a Guid primary key.
/// </summary>
public class GrimoireUser : IdentityUser<Guid>
{
    /// <summary>
    /// A public, human-friendly identifier a friend types to send a friend request (the FRIENDS
    /// wave). Null until the user claims one. When set it is lower-cased, 3-30 chars of
    /// <c>[a-z0-9_]</c>, and unique across all users (a filtered unique index skips the nulls).
    /// </summary>
    public string? Handle { get; set; }

    /// <summary>
    /// Whether this user lets accepted friends play the verdict game against their grimoire (the
    /// GAMES wave). That game necessarily reveals that they BANISHED a given band — a negative
    /// judgement no endpoint has ever exposed to anyone but its author (today only the Mirror, C20,
    /// reads a user's banishments, and only for themselves). The social block's guardrail is opt-in
    /// always, so this gates it.
    ///
    /// Deliberately NULLABLE, and null is not the same as false: null means "never asked", false
    /// means "asked and declined". Both refuse the game, but only one of them is a decision — the
    /// same distinction DECISIONS D61 had to retrofit onto the crawls after null had been overloaded
    /// to mean two things at once.
    /// </summary>
    public bool? VerdictGameOptIn { get; set; }
}
