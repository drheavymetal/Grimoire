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
}
