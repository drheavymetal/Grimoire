using Microsoft.AspNetCore.Identity;

namespace Grimoire.Library.Models;

/// <summary>
/// Application user. Backed by ASP.NET Identity with a Guid primary key.
/// </summary>
public class GrimoireUser : IdentityUser<Guid>
{
}
