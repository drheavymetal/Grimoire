using Grimoire.Library.Data;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Rare instruments (feature C15): the folk and orchestral colour outside the standard rock kit —
/// violin, bagpipe, hurdy gurdy, uilleann pipes, shawm, mandolin — read straight off the real
/// <c>credits.instrument</c> column. <see cref="InstrumentClassifier"/> decides what counts as rare;
/// the endpoint groups the credits by instrument and lists who plays each, on which band. A thin
/// corpus yields a short list and the front renders a designed empty state, never a fabricated one.
/// </summary>
[ApiController]
[Route("api/instruments")]
public class InstrumentsController : ControllerBase
{
    private const int MaxPlayersPerInstrument = 24;

    private readonly GrimoireDbContext _db;

    public InstrumentsController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The rare instruments in the catalogue, most-played first, each with the musicians credited
    /// with it and the band the credit is on. Only performer credits with an instrument are read,
    /// and only those the classifier deems rare survive — nothing here is invented.
    /// </summary>
    [HttpGet("rare")]
    public async Task<ActionResult<IReadOnlyList<RareInstrumentDto>>> Rare(CancellationToken ct = default)
    {
        // Every performer credit that names an instrument, joined to the performer and to the band
        // the release belongs to. Distinct on the four columns so one player on many recordings of
        // the same band is not counted twice for the same instrument.
        var rows = await _db.Credits
            .AsNoTracking()
            .Where(c => c.Role == "performer" && c.Instrument != null && c.ReleaseId != null)
            .Join(
                _db.Releases.AsNoTracking(),
                c => c.ReleaseId,
                r => r.Id,
                (c, r) => new { c.Instrument, PlayerId = c.ArtistId, BandId = r.ArtistId })
            .Join(
                _db.Artists.AsNoTracking(),
                cr => cr.PlayerId,
                a => a.Id,
                (cr, a) => new { cr.Instrument, cr.PlayerId, PlayerName = a.Name, cr.BandId })
            .Join(
                _db.Artists.AsNoTracking(),
                cr => cr.BandId,
                b => b.Id,
                (cr, b) => new
                {
                    cr.Instrument,
                    cr.PlayerId,
                    cr.PlayerName,
                    cr.BandId,
                    BandName = b.Name,
                    BandRank = b.Rank,
                })
            .Distinct()
            .ToListAsync(ct);

        List<RareInstrumentDto> result = rows
            .Where(r => InstrumentClassifier.IsRare(r.Instrument))
            .GroupBy(r => r.Instrument!.Trim().ToLowerInvariant())
            .Select(g =>
            {
                List<RareInstrumentPlayerDto> players = g
                    .GroupBy(r => r.PlayerId)
                    .Select(pg =>
                    {
                        var first = pg.First();
                        return new RareInstrumentPlayerDto(
                            first.PlayerId, first.PlayerName, first.BandId, first.BandName, first.BandRank);
                    })
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new RareInstrumentDto(g.Key, players.Count, players.Take(MaxPlayersPerInstrument).ToList());
            })
            .OrderByDescending(i => i.PlayerCount)
            .ThenBy(i => i.Instrument, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(result);
    }
}
