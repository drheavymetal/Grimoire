namespace Grimoire.Server.Services;

/// <summary>
/// Pure duration helpers for the recording features (B5 tracklist, C7 the duration axis). Kept
/// database-free so the boundary cases — a null length that must never count toward an average,
/// the mm:ss rollover, the band with no timed track at all — are unit-tested directly. Nothing
/// here invents a number: a missing length stays missing (C7 degrades honestly, never a zero).
/// </summary>
public static class DurationMath
{
    /// <summary>
    /// Formats a track length in milliseconds as <c>m:ss</c> (or <c>h:mm:ss</c> past an hour).
    /// A null length — MusicBrainz had none — renders as an em dash, never a fabricated 0:00.
    /// </summary>
    public static string FormatLength(int? lengthMs)
    {
        if (lengthMs is null || lengthMs.Value < 0)
        {
            return "—";
        }

        int totalSeconds = lengthMs.Value / 1000;
        int hours = totalSeconds / 3600;
        int minutes = totalSeconds % 3600 / 60;
        int seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return $"{hours}:{minutes:D2}:{seconds:D2}";
        }

        return $"{minutes}:{seconds:D2}";
    }

    /// <summary>
    /// The mean track length in milliseconds over a set of recordings, <b>excluding</b> the ones
    /// MusicBrainz never timed (C7: the null lengths are not zeros, they are absences). Returns null
    /// when nothing is timed — the honest "cannot say", which the caller renders as an empty state
    /// rather than a misleading average of the few tracks that happen to carry a length.
    /// </summary>
    public static double? AverageMs(IEnumerable<int?> lengths)
    {
        long sum = 0;
        int count = 0;

        foreach (int? length in lengths)
        {
            if (length is null || length.Value < 0)
            {
                continue;
            }

            sum += length.Value;
            count++;
        }

        if (count == 0)
        {
            return null;
        }

        return (double)sum / count;
    }
}
