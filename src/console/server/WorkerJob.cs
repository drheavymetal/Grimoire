using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker;

/// <summary>
/// Base for the worker's one-shot command jobs (seed, edges, previews, embeddings, stats).
/// Each runs its work once on a background task, then stops the host. Failures are logged,
/// never swallowed silently, and the host still stops so the process exits with the run over.
/// </summary>
public abstract class WorkerJob : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger _logger;
    private Task? _task;

    protected WorkerJob(IHostApplicationLifetime lifetime, ILogger logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>Human-readable name of the command, for log lines.</summary>
    protected abstract string CommandName { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _task = Task.Run(() => RunGuardedAsync(_lifetime.ApplicationStopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_task is not null)
        {
            await _task;
        }
    }

    /// <summary>Does the actual work. Implementations own their own database scope.</summary>
    protected abstract Task ExecuteAsync(CancellationToken ct);

    private async Task RunGuardedAsync(CancellationToken ct)
    {
        try
        {
            await ExecuteAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("{Command} cancelled.", CommandName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Command} failed.", CommandName);
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
