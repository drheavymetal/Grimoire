using System.Globalization;
using System.Text;

namespace Grimoire.Library.Services;

/// <summary>
/// Deterministic selection of the seven blind bands for the Weekly Rite (feature B17). The same
/// ISO week always yields the same seven from the same pool: the choice is a seeded shuffle whose
/// seed is derived only from the ISO-week key, so it is stable within a week and reproducible on
/// any machine. Pure and deterministic — unit-tested without a database or a clock.
///
/// <para>
/// The guarantee is precise: given an identical servable pool and week key, the output is byte-for-byte
/// identical. The pool is static between ETL runs (DECISIONS D5), so in practice the seven are fixed
/// for the whole week. The pool is sorted by id before shuffling so the input order cannot perturb it.
/// </para>
/// </summary>
public static class WeeklyRiteSelector
{
    /// <summary>How many bands the Weekly Rite serves each week (feature B17).</summary>
    public const int WeeklyCount = 7;

    /// <summary>
    /// The ISO-8601 week key for a moment in time, e.g. <c>2026-W28</c>. ISO weeks start on Monday,
    /// so any day of a week maps to the same key — "the same seven every week" (B17), Monday-aligned.
    /// </summary>
    public static string IsoWeekKey(DateTimeOffset instant)
    {
        DateTime utc = instant.UtcDateTime;
        int year = ISOWeek.GetYear(utc);
        int week = ISOWeek.GetWeekOfYear(utc);
        return string.Create(CultureInfo.InvariantCulture, $"{year:D4}-W{week:D2}");
    }

    /// <summary>
    /// Picks up to <paramref name="count"/> ids from <paramref name="pool"/>, deterministically for
    /// the given <paramref name="weekKey"/>. Returns fewer only when the pool is smaller than the
    /// count. The result order is itself deterministic (the shuffle order), not the pool order.
    /// </summary>
    public static IReadOnlyList<Guid> Select(
        IReadOnlyList<Guid> pool,
        string weekKey,
        int count = WeeklyCount)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(weekKey);

        if (pool.Count == 0 || count <= 0)
        {
            return [];
        }

        // Sort by id so the input order cannot change the outcome, then Fisher-Yates shuffle with a
        // PRNG seeded solely from the week key. Same key + same set -> identical permutation.
        Guid[] shuffled = pool.OrderBy(id => id).ToArray();
        ulong state = SeedFrom(weekKey);

        int take = Math.Min(count, shuffled.Length);
        for (int i = 0; i < take; i++)
        {
            int j = i + (int)(NextUInt64(ref state) % (ulong)(shuffled.Length - i));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled.Take(take).ToList();
    }

    /// <summary>FNV-1a 64-bit hash of the week key, used as the PRNG seed (never zero).</summary>
    private static ulong SeedFrom(string weekKey)
    {
        const ulong FnvOffset = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        ulong hash = FnvOffset;
        foreach (byte b in Encoding.UTF8.GetBytes(weekKey))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash == 0 ? FnvOffset : hash;
    }

    /// <summary>SplitMix64: a small, fast, well-distributed deterministic PRNG.</summary>
    private static ulong NextUInt64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
