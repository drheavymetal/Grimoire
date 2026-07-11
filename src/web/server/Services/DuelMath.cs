namespace Grimoire.Server.Services;

/// <summary>
/// The pairwise-preference taste update behind the blind duel (feature C2, DECISIONS D16). A duel
/// serves two bands blind and the user picks one; that choice is a <b>Bradley-Terry</b> preference
/// (winner &gt; loser), which teaches the taste vector far more than a lone like: a like says "this
/// is good", a preference says "this, <em>rather than that</em>", pinning the vector on both sides.
///
/// <para>
/// The update pulls the taste <b>toward the winner</b> and pushes it <b>away from the loser</b>:
/// <code>
///   toward = (1 − wWin)·taste + wWin·winner          // strong pull toward the winner (an EMA, like a summon)
///   result = toward + wLose·(toward − loser)          // gentler push away from the loser
/// </code>
/// The winner counts as the strong signal (its weight matches a summon's decay); the loser only
/// nudges. Together the two moves separate winner-from-loser more than summoning the winner alone
/// would — which is exactly why a duel is worth more than a single like.
/// </para>
///
/// <para>
/// CRITICAL INVARIANT — the double-centring trap (CLAUDE.md, DECISIONS D26). Every vector here is
/// <b>already centred</b> (the ETL subtracted the corpus mean before indexing, and the taste is a
/// mean of centred embeddings). This class only blends centred vectors, so the result stays centred;
/// it never subtracts the corpus mean again — see <see cref="Grimoire.Library.Services.TasteMath"/>.
/// Pure and deterministic, so it is unit-tested without a database.
/// </para>
/// </summary>
public static class DuelMath
{
    /// <summary>How strongly the winner pulls the taste — the strong signal, matching a summon's decay.</summary>
    public const double DefaultWinnerWeight = 0.25;

    /// <summary>How strongly the loser pushes the taste away — gentler than the winner's pull.</summary>
    public const double DefaultLoserWeight = 0.10;

    /// <summary>
    /// Applies a duel outcome to the taste vector: toward <paramref name="winner"/>, away from
    /// <paramref name="loser"/>. A null current taste starts from the winner (first duel with no
    /// seed) and is then pushed off the loser.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If a weight is outside (0, 1].</exception>
    /// <exception cref="ArgumentException">If the vector dimensions differ.</exception>
    public static float[] ApplyDuel(
        float[]? taste,
        float[] winner,
        float[] loser,
        double winnerWeight = DefaultWinnerWeight,
        double loserWeight = DefaultLoserWeight)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(loser);

        if (winnerWeight is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(winnerWeight), winnerWeight, "Winner weight must be in (0, 1].");
        }

        if (loserWeight is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(loserWeight), loserWeight, "Loser weight must be in (0, 1].");
        }

        if (winner.Length != loser.Length)
        {
            throw new ArgumentException("Winner and loser vectors must share the same dimension.");
        }

        // Pull toward the winner (an exponential moving average). A null taste means the winner
        // becomes the starting point.
        float[] toward;
        if (taste is null)
        {
            toward = (float[])winner.Clone();
        }
        else
        {
            if (taste.Length != winner.Length)
            {
                throw new ArgumentException("Taste and duel vectors must share the same dimension.");
            }

            toward = new float[taste.Length];
            for (int i = 0; i < taste.Length; i++)
            {
                toward[i] = (float)(((1.0 - winnerWeight) * taste[i]) + (winnerWeight * winner[i]));
            }
        }

        // Push away from the loser: extrapolate along (toward − loser).
        float[] result = new float[toward.Length];
        for (int i = 0; i < toward.Length; i++)
        {
            result[i] = (float)(toward[i] + (loserWeight * (toward[i] - loser[i])));
        }

        return result;
    }
}
