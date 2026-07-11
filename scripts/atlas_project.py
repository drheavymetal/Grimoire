#!/usr/bin/env python3
"""Project high-dimensional artist embeddings to 2D for the Atlas (C18/B22).

Reads an input JSON file {"ids": [...], "vectors": [[...], ...]} and writes an output
JSON file {"<id>": [x, y], ...}.

Zero-cost, offline, local (DECISIONS D6). The brief asked for umap-learn, falling back to
sklearn PCA, then a hand-rolled numpy PCA. In this environment none of umap-learn, scikit-learn
or even numpy is installed and there is no pip, so this is a hand-rolled PCA in *pure Python* —
power iteration for the top two principal components, no third-party dependency. It is
deterministic (fixed initialisation, no randomness), so re-running yields the same projection.
"""

import json
import math
import sys


def _dot(a, b):
    return math.fsum(a[i] * b[i] for i in range(len(a)))


def _normalise(v):
    norm = math.sqrt(math.fsum(x * x for x in v))
    if norm == 0.0:
        return v
    return [x / norm for x in v]


def _column_means(rows, dim):
    n = len(rows)
    means = [0.0] * dim
    for row in rows:
        for j in range(dim):
            means[j] += row[j]
    return [m / n for m in means]


def _centre(rows, means, dim):
    return [[row[j] - means[j] for j in range(dim)] for row in rows]


def _top_component(rows, dim, iterations=200, tol=1e-9):
    """Dominant eigenvector of the covariance via power iteration.

    Never forms the dim x dim covariance matrix: it applies C v = X^T (X v) directly,
    which is O(n * dim) per iteration.
    """
    # Deterministic, non-degenerate initialisation (avoids being orthogonal to the
    # dominant eigenvector for real data).
    v = _normalise([math.sin(j + 1.0) for j in range(dim)])

    for _ in range(iterations):
        # projections = X v  (one scalar per row)
        projections = [_dot(row, v) for row in rows]
        # cv = X^T projections
        cv = [0.0] * dim
        for row, p in zip(rows, projections):
            for j in range(dim):
                cv[j] += row[j] * p
        cv = _normalise(cv)
        # Converged when the direction stops moving.
        if 1.0 - abs(_dot(cv, v)) < tol:
            v = cv
            break
        v = cv
    return v


def _deflate(rows, component, dim):
    """Remove the variance along `component` so the next power iteration finds the second axis."""
    out = []
    for row in rows:
        p = _dot(row, component)
        out.append([row[j] - p * component[j] for j in range(dim)])
    return out


def project(ids, vectors):
    if not vectors:
        return {}
    dim = len(vectors[0])

    means = _column_means(vectors, dim)
    centred = _centre(vectors, means, dim)

    pc1 = _top_component(centred, dim)
    residual = _deflate(centred, pc1, dim)
    pc2 = _top_component(residual, dim)

    result = {}
    for artist_id, row in zip(ids, centred):
        result[artist_id] = [_dot(row, pc1), _dot(row, pc2)]
    return result


def main(argv):
    if len(argv) != 3:
        print("usage: atlas_project.py <input.json> <output.json>", file=sys.stderr)
        return 2

    with open(argv[1], "r", encoding="utf-8") as fh:
        payload = json.load(fh)

    ids = payload["ids"]
    vectors = payload["vectors"]

    if len(ids) != len(vectors):
        print("ids and vectors length mismatch", file=sys.stderr)
        return 1

    result = project(ids, vectors)

    with open(argv[2], "w", encoding="utf-8") as fh:
        json.dump(result, fh)

    print(f"projected {len(result)} embeddings to 2D (pure-python PCA)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
