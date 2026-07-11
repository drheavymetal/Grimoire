using System.Text.Json;
using Grimoire.Worker.MusicBrainz;

namespace Grimoire.Worker.Credits;

/// <summary>
/// A disk cache of the chosen MusicBrainz release JSON, keyed by release-group MBID, plus a
/// per-verb progress ledger. Both make the credits/labels passes <b>resumable</b> across
/// interruptions and <b>polite</b> to MusicBrainz: the single release fetch is shared, so once
/// the credits pass has cached a release-group the labels pass reads it without a second request.
/// The cache lives outside the repository (a temp directory by default), overridable with
/// <c>GRIMOIRE_CACHE_DIR</c>.
/// </summary>
public sealed class EtlCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _releasesDir;

    public EtlCache(string root)
    {
        Root = root;
        _releasesDir = Path.Combine(root, "releases");
        Directory.CreateDirectory(_releasesDir);
    }

    public string Root { get; }

    /// <summary>Resolves the cache root from the environment, falling back to a temp directory.</summary>
    public static string ResolveRoot()
    {
        string? env = Environment.GetEnvironmentVariable("GRIMOIRE_CACHE_DIR");

        return string.IsNullOrWhiteSpace(env)
            ? Path.Combine(Path.GetTempPath(), "grimoire-cache")
            : env;
    }

    public bool HasRelease(Guid releaseGroupMbid)
    {
        return File.Exists(ReleasePath(releaseGroupMbid));
    }

    /// <summary>
    /// Loads the cached release for a release-group. Returns null both when nothing is cached and
    /// when the cache holds the "no release found" marker; callers that need to tell them apart
    /// use <see cref="HasRelease"/> first.
    /// </summary>
    public async Task<MbRelease?> LoadReleaseAsync(Guid releaseGroupMbid, CancellationToken ct)
    {
        string path = ReleasePath(releaseGroupMbid);

        if (!File.Exists(path))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(path, ct);

        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return null;
        }

        return JsonSerializer.Deserialize<MbRelease>(json, JsonOptions);
    }

    /// <summary>Persists the chosen release (or a null marker) for a release-group.</summary>
    public async Task SaveReleaseAsync(Guid releaseGroupMbid, MbRelease? release, CancellationToken ct)
    {
        string json = release is null ? "null" : JsonSerializer.Serialize(release, JsonOptions);
        await File.WriteAllTextAsync(ReleasePath(releaseGroupMbid), json, ct);
    }

    public ProgressLedger Ledger(string verb)
    {
        return new ProgressLedger(Path.Combine(Root, $"{verb}.done"));
    }

    private string ReleasePath(Guid releaseGroupMbid)
    {
        return Path.Combine(_releasesDir, $"{releaseGroupMbid}.json");
    }
}

/// <summary>
/// An append-only ledger of completed ids for a verb, so a re-run skips what a previous run
/// already finished. Distinct from cache presence, which only means "the release JSON is on
/// disk", not "this verb wrote its rows for it".
/// </summary>
public sealed class ProgressLedger
{
    private readonly string _path;
    private readonly HashSet<Guid> _done = [];

    public ProgressLedger(string path)
    {
        _path = path;

        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (Guid.TryParse(line.Trim(), out Guid id))
            {
                _done.Add(id);
            }
        }
    }

    public int Count => _done.Count;

    public bool Contains(Guid id)
    {
        return _done.Contains(id);
    }

    public async Task MarkAsync(Guid id, CancellationToken ct)
    {
        if (_done.Add(id))
        {
            await File.AppendAllTextAsync(_path, id + Environment.NewLine, ct);
        }
    }
}
