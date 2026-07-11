namespace Grimoire.Server.Services;

/// <summary>
/// Recovers the linear PCA map the offline Atlas pass used (<c>scripts/atlas_project.py</c>), so a
/// live vector — the signed-in user's taste — can be placed on the very same 2D map as the stars,
/// without re-running the projection and without inventing coordinates.
///
/// <para>
/// The Atlas job centres the embeddings by their column mean and projects onto the top two
/// principal components <c>pc1</c>, <c>pc2</c> (orthonormal eigenvectors of the centred covariance):
/// <c>xy_i = ((emb_i − μ)·pc1, (emb_i − μ)·pc2)</c>. Those eigenvectors are not persisted, but they
/// can be reconstructed EXACTLY from the stored pairs. Writing the centred matrix as <c>C</c> and the
/// x-scores as <c>s = C·pc1</c> (i.e. the stored <c>xy_x</c> column), then
/// <c>Cᵀs = CᵀC·pc1 = λ1·pc1</c> and <c>‖s‖² = pc1ᵀCᵀC·pc1 = λ1</c>, hence
/// <c>pc1 = (Cᵀs) / ‖s‖²</c> — a plain weighted sum of the centred embeddings. Same for <c>pc2</c>
/// with the <c>xy_y</c> column. This reproduces the stored coordinates for every training star and,
/// because the PCA map is linear, applies unchanged to the taste vector (which lives in the same
/// centred-embedding space, D26/D31 — never re-centred by the corpus mean here).
/// </para>
///
/// <para>Pure and allocation-light so it is unit-tested without a database.</para>
/// </summary>
public static class AtlasProjection
{
    /// <summary>The reconstructed linear projection basis: the centring mean and the two axes.</summary>
    public sealed record Basis(float[] Mean, float[] Axis1, float[] Axis2);

    /// <summary>
    /// Reconstructs the projection basis from the stored (embedding, xy) pairs. Returns null when the
    /// inputs cannot define a projection: fewer than two stars, a dimension mismatch, or a degenerate
    /// axis with zero score variance (nothing to project onto) — the caller then omits the taste
    /// marker rather than placing it at a made-up point.
    /// </summary>
    public static Basis? Reconstruct(IReadOnlyList<float[]> embeddings, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);

        if (embeddings.Count < 2 || embeddings.Count != xs.Count || embeddings.Count != ys.Count)
        {
            return null;
        }

        int dim = embeddings[0].Length;

        foreach (float[] e in embeddings)
        {
            if (e.Length != dim)
            {
                return null;
            }
        }

        // Column mean μ over the stored embeddings (the same centring the Atlas pass applies).
        double[] mean = new double[dim];
        foreach (float[] e in embeddings)
        {
            for (int j = 0; j < dim; j++)
            {
                mean[j] += e[j];
            }
        }
        for (int j = 0; j < dim; j++)
        {
            mean[j] /= embeddings.Count;
        }

        // Axis_k = (Σ_i score_k,i · (emb_i − μ)) / Σ_i score_k,i², with the scores being the stored
        // xy columns. This is Cᵀs / ‖s‖², the exact eigenvector up to its (matching) sign.
        double[] axis1 = new double[dim];
        double[] axis2 = new double[dim];
        double sumSq1 = 0;
        double sumSq2 = 0;

        for (int i = 0; i < embeddings.Count; i++)
        {
            float[] e = embeddings[i];
            double sx = xs[i];
            double sy = ys[i];
            sumSq1 += sx * sx;
            sumSq2 += sy * sy;

            for (int j = 0; j < dim; j++)
            {
                double centred = e[j] - mean[j];
                axis1[j] += sx * centred;
                axis2[j] += sy * centred;
            }
        }

        if (sumSq1 <= 0 || sumSq2 <= 0)
        {
            return null;
        }

        float[] a1 = new float[dim];
        float[] a2 = new float[dim];
        for (int j = 0; j < dim; j++)
        {
            a1[j] = (float)(axis1[j] / sumSq1);
            a2[j] = (float)(axis2[j] / sumSq2);
        }

        return new Basis([.. mean.Select(m => (float)m)], a1, a2);
    }

    /// <summary>
    /// Projects a vector onto the reconstructed basis: <c>((v − μ)·axis1, (v − μ)·axis2)</c>. The
    /// vector must share the basis dimension.
    /// </summary>
    public static (double X, double Y) Project(Basis basis, float[] vector)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length != basis.Mean.Length)
        {
            throw new ArgumentException("Vector and basis must share the same dimension.", nameof(vector));
        }

        double x = 0;
        double y = 0;
        for (int j = 0; j < vector.Length; j++)
        {
            double centred = vector[j] - basis.Mean[j];
            x += centred * basis.Axis1[j];
            y += centred * basis.Axis2[j];
        }

        return (x, y);
    }
}
