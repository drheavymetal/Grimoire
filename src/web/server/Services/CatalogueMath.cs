namespace Grimoire.Server.Services;

/// <summary>
/// Pure classifiers for the catalogue curiosities (C24, C25). Kept database-free so the
/// boundary cases — exactly one album, one more release than years alive — are unit-tested
/// directly. Nothing here invents a number: a band with no formation year is simply not
/// eligible for the hyperprolific measure, and the caller passes a real "current year".
/// </summary>
public static class CatalogueMath
{
    /// <summary>
    /// A one-album band (C24): exactly one main release and no other main releases, where a
    /// "main" release is an album, EP or demo (live records and compilations are posthumous
    /// repackaging and do not disqualify the one-and-done story). Returns true only for a
    /// single main release.
    /// </summary>
    public static bool IsOneAlbumBand(int albums, int eps, int demos)
    {
        int mains = albums + eps + demos;
        return albums == 1 && mains == 1;
    }

    /// <summary>
    /// The prolificacy ratio (C25): releases per year of existence. A band is hyperprolific when
    /// it has put out more releases than it has been alive years — ratio &gt; 1. Years alive is
    /// <paramref name="currentYear"/> − <paramref name="formedYear"/>, floored at 1 so a band
    /// formed this year does not divide by zero and does not report an infinite rate.
    /// </summary>
    public static double ProlificacyRatio(int releaseCount, int formedYear, int currentYear)
    {
        int years = Math.Max(1, currentYear - formedYear);
        return (double)releaseCount / years;
    }

    /// <summary>True when the band released more than it has lived (C25): ratio strictly above 1.</summary>
    public static bool IsHyperprolific(int releaseCount, int formedYear, int currentYear)
    {
        return ProlificacyRatio(releaseCount, formedYear, currentYear) > 1.0;
    }
}
