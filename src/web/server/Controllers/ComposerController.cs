using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The composer body (movement VII, D11): a composer's works grouped by kind and their two
/// lineages (teacher/student and influence). This is NOT the band ficha — no Gantt, no members,
/// no rank. Identity is served by <c>ArtistsController</c>; the artist page decides which body to
/// render from <c>ArtistDetail.HasWorks</c> and calls this only for composers.
/// </summary>
[ApiController]
[Route("api/composers")]
public class ComposerController : ControllerBase
{
    private readonly ComposerDetailBuilder _builder;

    public ComposerController(ComposerDetailBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>A composer's works and lineage. 404 when the id is unknown.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ComposerDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        ComposerDetailDto? dto = await _builder.BuildAsync(id, ct);

        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }
}
