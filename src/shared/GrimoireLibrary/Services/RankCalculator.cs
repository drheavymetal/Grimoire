using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// Derives a rarity <see cref="Rank"/> from a Last.fm listener count. Rarity is
/// inverse to popularity: the fewer listeners, the rarer the find. Thresholds come
/// from SPEC section 6 / DECISIONS D3.
/// </summary>
public static class RankCalculator
{
    /// <summary>
    /// Maps a listener count to a rarity tier. Returns <c>null</c> when the listener
    /// count is unknown, so callers never invent a rank from missing data.
    /// </summary>
    /// <param name="listeners">Last.fm listener count, or <c>null</c> if unknown.</param>
    public static Rank? FromListeners(int? listeners)
    {
        if (listeners is null)
        {
            return null;
        }

        int count = listeners.Value;

        if (count < 500)
        {
            return Rank.Nameless;
        }

        if (count < 5_000)
        {
            return Rank.Forgotten;
        }

        if (count < 50_000)
        {
            return Rank.Hidden;
        }

        // SPEC: "> 500 000 → Known" is strict, so 500 000 itself is Obscure.
        if (count <= 500_000)
        {
            return Rank.Obscure;
        }

        return Rank.Known;
    }
}
