namespace Grimoire.Worker.MusicBrainz;

/// <summary>
/// Enforces MusicBrainz's strict "one request per second" policy across the whole
/// process. A single <see cref="SemaphoreSlim"/> serialises callers into one queue,
/// and a <see cref="PeriodicTimer"/> paces the queue to at most one release per
/// second. This is deliberately single-threaded: correctness beats throughput.
/// </summary>
public sealed class MusicBrainzRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));

    /// <summary>
    /// Blocks until it is safe to issue the next request, honouring the 1 req/s cadence.
    /// Callers must await this immediately before each HTTP call.
    /// </summary>
    public async Task WaitTurnAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            // Waits for the next 1-second tick, guaranteeing spacing between requests.
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
