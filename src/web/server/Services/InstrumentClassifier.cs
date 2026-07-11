namespace Grimoire.Server.Services;

/// <summary>
/// Decides whether a credited instrument is <b>rare</b> — outside the standard rock kit
/// (feature C15). The standard kit is guitar, bass, drums, vocals and keys; on top of that,
/// generic studio percussion and effects are common furniture, not a discovery. Everything
/// else that carries a real instrument name — violin, bagpipe, hurdy gurdy, uilleann pipes,
/// nyckelharpa/talharpa, shawm, mandolin, accordion — is what C15 exists to surface, the folk
/// and orchestral colour the folk corpus (D23) brings in.
///
/// <para>
/// Pure and database-free so the boundary cases are unit-tested directly. It classifies by the
/// standard kit rather than enumerating every rare instrument, because an allowlist of rare
/// names would silently drop anything nobody thought to list — and the underground is exactly
/// where the odd instrument shows up. Nothing here invents an instrument: a null or blank
/// credit is not rare, it is simply absent.
/// </para>
/// </summary>
public static class InstrumentClassifier
{
    /// <summary>
    /// Substrings that mark an instrument as part of the standard rock kit. Matched anywhere in
    /// the lowercased name, so "electric bass guitar", "12 string guitar" and "lead vocals" all
    /// fold in. Order does not matter here — any hit means "standard".
    /// </summary>
    private static readonly string[] StandardKit =
    [
        "guitar", "bass", "drum", "vocal", "vox", "choir",
        "keyboard", "piano", "synth", "organ", "mellotron", "clavinet",
        "rhodes", "wurlitzer", "moog", "hammond", "harpsichord", "continuum",
    ];

    /// <summary>
    /// Exact names that are common studio percussion or effects — furniture on many records,
    /// not the folk/orchestral colour C15 is looking for. Matched on the whole trimmed name so
    /// that, for example, "whistling" (a vocal effect) is excluded while "tin whistle" (a folk
    /// instrument) is not.
    /// </summary>
    private static readonly HashSet<string> CommonStudio = new(StringComparer.OrdinalIgnoreCase)
    {
        "percussion", "membranophone", "handclaps", "hand claps", "finger snaps", "foot stomps",
        "sampler", "effects", "electronic instruments", "cowbell", "tambourine", "triangle",
        "wood block", "maracas", "gong", "timpani", "marimba", "crotales", "temple blocks",
        "timbales", "vibraslap", "bell tree", "wind chime", "tubular bells", "bicycle bell",
        "bones", "whistling", "vocoder",
    };

    /// <summary>
    /// True when the instrument sits outside the standard rock kit and the common studio set —
    /// the rare, discovery-worthy colour of feature C15. False for a null, blank, standard-kit
    /// or common-studio credit.
    /// </summary>
    public static bool IsRare(string? instrument)
    {
        if (string.IsNullOrWhiteSpace(instrument))
        {
            return false;
        }

        string name = instrument.Trim();
        string lower = name.ToLowerInvariant();

        foreach (string kit in StandardKit)
        {
            if (lower.Contains(kit, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (CommonStudio.Contains(name))
        {
            return false;
        }

        return true;
    }
}
