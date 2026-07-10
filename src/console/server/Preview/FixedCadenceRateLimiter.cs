namespace Grimoire.Worker.Preview;

/// <summary>
/// Serialises callers to at most one request per fixed interval, the same single-threaded
/// pacing as <see cref="MusicBrainz.MusicBrainzRateLimiter"/> but with a configurable cadence.
/// iTunes tolerates ~20 req/min in practice (DECISIONS D25), so it is paced at 3 s; Deezer,
/// which is only a complement, is paced more gently. Correctness over throughput.
/// </summary>
public sealed class FixedCadenceRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PeriodicTimer _timer;

    public FixedCadenceRateLimiter(TimeSpan interval)
    {
        _timer = new PeriodicTimer(interval);
    }

    /// <summary>Blocks until the next slot is due. Await immediately before each HTTP call.</summary>
    public async Task WaitTurnAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await _timer.WaitForNextTickAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _gate.Dispose();
    }
}
