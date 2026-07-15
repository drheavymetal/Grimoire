using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The notification payload round trip (NOTIFICATIONS wave): each type serialises to JSON and
/// flattens back to exactly the fields it carries, and a null/malformed body degrades to empty
/// rather than throwing — the inbox must never fall over on a stray row.
/// </summary>
public class NotificationPayloadTests
{
    [Fact]
    public void FriendRequest_RoundTripsTheFriendshipId()
    {
        Guid friendship = Guid.NewGuid();

        string? json = NotificationPayload.Serialize(new NotificationPayload.FriendRequest(friendship));
        NotificationPayload.Flattened flat = NotificationPayload.Flatten(NotificationType.FriendRequest, json);

        Assert.Equal(friendship, flat.FriendshipId);
        Assert.Null(flat.GiftToken);
        Assert.Null(flat.ArtistName);
    }

    [Fact]
    public void GiftReceived_RoundTripsTheTokenAndName()
    {
        string? json = NotificationPayload.Serialize(
            new NotificationPayload.GiftReceived("opaque-token", "Darkthrone"));
        NotificationPayload.Flattened flat = NotificationPayload.Flatten(NotificationType.GiftReceived, json);

        Assert.Equal("opaque-token", flat.GiftToken);
        Assert.Equal("Darkthrone", flat.ArtistName);
        Assert.Null(flat.FriendshipId);
    }

    [Fact]
    public void FriendAccepted_CarriesNoPayload()
    {
        // An accept has no payload object: serialising null yields null, and flattening is empty.
        string? json = NotificationPayload.Serialize(null);

        Assert.Null(json);
        NotificationPayload.Flattened flat = NotificationPayload.Flatten(NotificationType.FriendAccepted, json);

        Assert.Equal(NotificationPayload.Flattened.Empty, flat);
    }

    [Fact]
    public void Flatten_ToleratesNullAndGarbage()
    {
        Assert.Equal(NotificationPayload.Flattened.Empty,
            NotificationPayload.Flatten(NotificationType.FriendRequest, null));
        Assert.Equal(NotificationPayload.Flattened.Empty,
            NotificationPayload.Flatten(NotificationType.GiftReceived, "not json"));
    }
}
