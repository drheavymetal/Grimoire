namespace Grimoire.Library.Services;

/// <summary>
/// Pure vector arithmetic for the centred-embedding pipeline (DECISIONS D26). The corpus
/// mean is subtracted from every embedding before indexing, which triples the separation
/// between a near and a far neighbour; the same mean is later subtracted from the query
/// vector so distances stay comparable. All operations here are allocation-light and
/// deterministic so they can be unit-tested without a database or Ollama.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Computes the element-wise mean of a non-empty set of equal-length vectors.
    /// </summary>
    /// <exception cref="ArgumentException">If the set is empty or vectors differ in length.</exception>
    public static float[] Mean(IReadOnlyList<float[]> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);

        if (vectors.Count == 0)
        {
            throw new ArgumentException("Cannot take the mean of an empty set.", nameof(vectors));
        }

        int dim = vectors[0].Length;
        double[] sums = new double[dim];

        foreach (float[] v in vectors)
        {
            if (v.Length != dim)
            {
                throw new ArgumentException("All vectors must share the same dimension.", nameof(vectors));
            }

            for (int i = 0; i < dim; i++)
            {
                sums[i] += v[i];
            }
        }

        float[] mean = new float[dim];

        for (int i = 0; i < dim; i++)
        {
            mean[i] = (float)(sums[i] / vectors.Count);
        }

        return mean;
    }

    /// <summary>
    /// Returns <paramref name="vector"/> minus <paramref name="mean"/>, element-wise. Does
    /// not mutate its inputs.
    /// </summary>
    /// <exception cref="ArgumentException">If the lengths differ.</exception>
    public static float[] Subtract(float[] vector, float[] mean)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(mean);

        if (vector.Length != mean.Length)
        {
            throw new ArgumentException("Vector and mean must share the same dimension.");
        }

        float[] result = new float[vector.Length];

        for (int i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] - mean[i];
        }

        return result;
    }

    /// <summary>
    /// Cosine distance (1 − cosine similarity) between two equal-length vectors, matching
    /// pgvector's <c>vector_cosine_ops</c> so in-process stats agree with the HNSW index.
    /// A zero-magnitude vector yields distance 1 (maximally dissimilar) rather than NaN.
    /// </summary>
    /// <exception cref="ArgumentException">If the lengths differ.</exception>
    public static double CosineDistance(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must share the same dimension.");
        }

        double dot = 0;
        double magA = 0;
        double magB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            magA += (double)a[i] * a[i];
            magB += (double)b[i] * b[i];
        }

        if (magA <= 0 || magB <= 0)
        {
            return 1.0;
        }

        double similarity = dot / (Math.Sqrt(magA) * Math.Sqrt(magB));

        // Guard against floating-point drift just past ±1.
        similarity = Math.Clamp(similarity, -1.0, 1.0);

        return 1.0 - similarity;
    }
}
