using Grimoire.Library.Data;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — gift a discovery (C22). You do not send a Spotify link. You send the band <b>face
/// down and signed</b>: the recipient hears forty-five seconds blind, not knowing whether it is a
/// gift or a trap, and it is revealed only if they choose to turn it over.
///
/// <para>
/// The gift carries no database row and no name in its link: the band id and the note are sealed
/// inside an encrypted capability token (ASP.NET Data Protection), so the token is unguessable and
/// unreadable, and the audio is streamed through the same anti-leak proxy the Rite uses (D32).
/// Only a servable band — one with a real preview — can be gifted, since a gift that cannot sound
/// blind is no gift at all.
/// </para>
/// </summary>
[ApiController]
[Route("api/gift")]
public class GiftController : ControllerBase
{
    private const int MaxNoteLength = 280;

    private readonly GrimoireDbContext _db;
    private readonly PreviewAudioProxy _audio;
    private readonly ArtistDetailBuilder _details;
    private readonly IDataProtector _protector;

    public GiftController(
        GrimoireDbContext db,
        PreviewAudioProxy audio,
        ArtistDetailBuilder details,
        IDataProtectionProvider protection)
    {
        _db = db;
        _audio = audio;
        _details = details;
        _protector = protection.CreateProtector(GiftToken.Purpose);
    }

    /// <summary>
    /// Wraps a band as a gift (C22). Requires a signed-in giver. 422 when the band cannot sound blind
    /// (no preview) — the gift mechanic depends on the audio.
    /// </summary>
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<GiftDto>> Create(CreateGiftRequest request, CancellationToken ct)
    {
        var artist = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Id == request.ArtistId)
            .Select(a => new { a.Id, a.PreviewUrl })
            .FirstOrDefaultAsync(ct);

        if (artist is null)
        {
            return NotFound(new { message = "That band is not in the grimoire." });
        }

        if (string.IsNullOrEmpty(artist.PreviewUrl))
        {
            return UnprocessableEntity(new
            {
                message = "That band has no preview, so it cannot be sent blind. A gift has to be able to sound.",
            });
        }

        string? note = string.IsNullOrWhiteSpace(request.Note)
            ? null
            : request.Note.Trim()[..Math.Min(request.Note.Trim().Length, MaxNoteLength)];

        string token = GiftToken.Wrap(_protector, new GiftToken.Payload(artist.Id, note));

        return Ok(new GiftDto(token, note));
    }

    /// <summary>
    /// What the recipient sees before deciding (C22): the note and the audio URL, never the band.
    /// 404 when the token is invalid or tampered with.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{token}")]
    public ActionResult<GiftBlindDto> Peek(string token)
    {
        GiftToken.Payload? payload = GiftToken.Unwrap(_protector, token);

        if (payload is null)
        {
            return NotFound();
        }

        string audioUrl = $"{Request.Scheme}://{Request.Host}/api/gift/{Uri.EscapeDataString(token)}/audio";
        return Ok(new GiftBlindDto(payload.Note, audioUrl));
    }

    /// <summary>
    /// Streams the gifted band's preview blind, through the server (C22, same anti-leak proxy as the
    /// Rite — D32). Anonymous: the token is the capability. 404 on an invalid token or missing audio.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{token}/audio")]
    public async Task<IActionResult> Audio(string token, CancellationToken ct)
    {
        GiftToken.Payload? payload = GiftToken.Unwrap(_protector, token);

        if (payload is null)
        {
            return NotFound();
        }

        string? previewUrl = await _db.Artists
            .Where(a => a.Id == payload.ArtistId)
            .Select(a => a.PreviewUrl)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(previewUrl))
        {
            return NotFound();
        }

        HttpResponseMessage? upstream = await _audio.OpenAsync(previewUrl, ct);

        if (upstream is null)
        {
            return NotFound();
        }

        HttpContext.Response.RegisterForDispose(upstream);

        Stream body = await upstream.Content.ReadAsStreamAsync(ct);
        string contentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";

        return File(body, contentType);
    }

    /// <summary>
    /// Turns the gift over (C22): reveals the full band. Anonymous — anyone holding the link may
    /// reveal, once they have decided they like it. 404 on an invalid token or unknown band.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{token}/reveal")]
    public async Task<ActionResult<ArtistDetailDto>> Reveal(string token, CancellationToken ct)
    {
        GiftToken.Payload? payload = GiftToken.Unwrap(_protector, token);

        if (payload is null)
        {
            return NotFound();
        }

        ArtistDetailDto? artist = await _details.BuildAsync(payload.ArtistId, ct);

        if (artist is null)
        {
            return NotFound();
        }

        return Ok(artist);
    }
}
