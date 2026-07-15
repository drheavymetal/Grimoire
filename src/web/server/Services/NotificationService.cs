using Grimoire.Library.Data;
using Grimoire.Library.Models;

namespace Grimoire.Server.Services;

/// <summary>
/// Writes in-app notifications (the NOTIFICATIONS wave). One entry point, <see cref="CreateAsync"/>,
/// serialises the type-specific payload and drops a row into the recipient's inbox, so the event
/// wiring in the controllers stays a single call. Scoped: it shares the request's DbContext.
/// </summary>
public class NotificationService
{
    private readonly GrimoireDbContext _db;

    public NotificationService(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates a notification for <paramref name="recipientId"/>, serialising
    /// <paramref name="payload"/> to JSON (null when the type carries none), and saves it.
    /// </summary>
    public async Task CreateAsync(
        Guid recipientId,
        NotificationType type,
        Guid? actorId,
        object? payload,
        CancellationToken ct)
    {
        Notification notification = new()
        {
            Id = Guid.NewGuid(),
            UserId = recipientId,
            Type = type,
            ActorId = actorId,
            PayloadJson = NotificationPayload.Serialize(payload),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);
    }
}
