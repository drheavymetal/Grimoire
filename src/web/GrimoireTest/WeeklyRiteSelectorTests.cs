using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The Weekly Rite selection (feature B17). These bite on the one property the whole feature rests
/// on: the same ISO week yields the same seven, and the choice does not leak in from pool order.
/// </summary>
public class WeeklyRiteSelectorTests
{
    private static List<Guid> Pool(int n)
    {
        // Deterministic distinct guids so the test itself is reproducible.
        List<Guid> pool = [];
        for (int i = 0; i < n; i++)
        {
            byte[] bytes = new byte[16];
            bytes[0] = (byte)(i & 0xFF);
            bytes[1] = (byte)((i >> 8) & 0xFF);
            pool.Add(new Guid(bytes));
        }

        return pool;
    }

    [Fact]
    public void Select_SameWeekSamePool_IsIdentical()
    {
        List<Guid> pool = Pool(200);

        IReadOnlyList<Guid> a = WeeklyRiteSelector.Select(pool, "2026-W28");
        IReadOnlyList<Guid> b = WeeklyRiteSelector.Select(pool, "2026-W28");

        Assert.Equal(a, b); // same week -> same seven, byte for byte and in the same order
    }

    [Fact]
    public void Select_IsIndependentOfPoolOrder()
    {
        List<Guid> pool = Pool(200);
        List<Guid> shuffled = pool.OrderByDescending(g => g).ToList();

        IReadOnlyList<Guid> a = WeeklyRiteSelector.Select(pool, "2026-W28");
        IReadOnlyList<Guid> b = WeeklyRiteSelector.Select(shuffled, "2026-W28");

        // The pool is sorted by id before shuffling, so input order cannot perturb the outcome.
        Assert.Equal(a, b);
    }

    [Fact]
    public void Select_DifferentWeeks_Differ()
    {
        List<Guid> pool = Pool(200);

        IReadOnlyList<Guid> w28 = WeeklyRiteSelector.Select(pool, "2026-W28");
        IReadOnlyList<Guid> w29 = WeeklyRiteSelector.Select(pool, "2026-W29");

        // Over a 200-band pool two different week keys must not produce the identical seven.
        Assert.NotEqual(w28, w29);
    }

    [Fact]
    public void Select_ReturnsSevenDistinctFromThePool()
    {
        List<Guid> pool = Pool(200);

        IReadOnlyList<Guid> seven = WeeklyRiteSelector.Select(pool, "2026-W28");

        Assert.Equal(WeeklyRiteSelector.WeeklyCount, seven.Count);
        Assert.Equal(seven.Count, seven.Distinct().Count());
        Assert.All(seven, id => Assert.Contains(id, pool));
    }

    [Fact]
    public void Select_SmallPool_ReturnsWholePool()
    {
        List<Guid> pool = Pool(3);

        IReadOnlyList<Guid> chosen = WeeklyRiteSelector.Select(pool, "2026-W28");

        Assert.Equal(3, chosen.Count);
        Assert.Equal(pool.OrderBy(g => g), chosen.OrderBy(g => g));
    }

    [Fact]
    public void Select_EmptyPool_IsEmpty()
    {
        Assert.Empty(WeeklyRiteSelector.Select([], "2026-W28"));
    }

    [Fact]
    public void IsoWeekKey_IsMondayAligned()
    {
        // 2026-07-06 is a Monday, 2026-07-12 the Sunday of the same ISO week: same key.
        var monday = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 7, 12, 23, 0, 0, TimeSpan.Zero);
        var nextMonday = new DateTimeOffset(2026, 7, 13, 0, 30, 0, TimeSpan.Zero);

        Assert.Equal(WeeklyRiteSelector.IsoWeekKey(monday), WeeklyRiteSelector.IsoWeekKey(sunday));
        Assert.NotEqual(WeeklyRiteSelector.IsoWeekKey(sunday), WeeklyRiteSelector.IsoWeekKey(nextMonday));
        Assert.Equal("2026-W28", WeeklyRiteSelector.IsoWeekKey(monday));
    }
}
