using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Grimoire.Server.Services;

/// <summary>
/// Seals and opens a gift's capability token (C22). The band id and the signed note are serialised
/// and encrypted with ASP.NET Data Protection, so the token is opaque (the recipient cannot read
/// the band from the link) and tamper-evident (a forged token fails to open). No database row is
/// needed — the whole gift lives inside the token. Isolated here so the round trip is unit-tested
/// with an ephemeral protector, including the tamper case.
/// </summary>
public static class GiftToken
{
    /// <summary>The purpose string that scopes the protector; changing it invalidates old tokens.</summary>
    public const string Purpose = "Grimoire.Gift.v1";

    /// <summary>What a gift carries: the band being gifted and an optional signed note.</summary>
    public record Payload(Guid ArtistId, string? Note);

    /// <summary>Seals a payload into an opaque, URL-safe token.</summary>
    public static string Wrap(IDataProtector protector, Payload payload)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(payload);

        return protector.Protect(JsonSerializer.Serialize(payload));
    }

    /// <summary>Opens a token back into its payload, or null when it is invalid, forged or malformed.</summary>
    public static Payload? Unwrap(IDataProtector protector, string token)
    {
        ArgumentNullException.ThrowIfNull(protector);

        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Payload>(protector.Unprotect(token));
        }
        catch (CryptographicException)
        {
            // Tampered, forged or expired: an honest miss, never a leak.
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
