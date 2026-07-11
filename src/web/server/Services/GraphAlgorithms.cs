namespace Grimoire.Server.Services;

/// <summary>
/// Pure graph algorithms over the artist graph (movement IV — Lineage). They operate on plain
/// adjacency maps (<c>id → neighbour ids</c>) so they can be unit-tested without a database:
/// the controller builds the adjacency from <c>artist_edges</c> and hands it in.
///
/// <para>
/// The underlying graph is undirected: an edge is a real relation between two artists
/// (<c>MemberOf</c> links a person to a band; <c>InfluencedBy</c> links two bands). Six Degrees
/// (B19) walks it band → person → band; Bloodline (B16) grows an ego neighbourhood out to N hops;
/// the Rabbit Hole (C8) takes a non-repeating walk through it.
/// </para>
/// </summary>
public static class GraphAlgorithms
{
    /// <summary>
    /// Shortest path between two nodes by breadth-first search, or an empty list when the two are
    /// disconnected. The path includes both endpoints; a node to itself yields a single-element path.
    /// BFS over an unweighted graph returns a path with the fewest edges — exactly "six degrees".
    /// </summary>
    public static IReadOnlyList<Guid> ShortestPath(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> adjacency,
        Guid from,
        Guid to)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        if (from == to)
        {
            return adjacency.ContainsKey(from) ? [from] : [];
        }

        if (!adjacency.ContainsKey(from) || !adjacency.ContainsKey(to))
        {
            return [];
        }

        Dictionary<Guid, Guid> cameFrom = new() { [from] = from };
        Queue<Guid> queue = new();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();

            if (!adjacency.TryGetValue(current, out IReadOnlyList<Guid>? neighbours))
            {
                continue;
            }

            foreach (Guid next in neighbours)
            {
                if (cameFrom.ContainsKey(next))
                {
                    continue;
                }

                cameFrom[next] = current;

                if (next == to)
                {
                    return Reconstruct(cameFrom, from, to);
                }

                queue.Enqueue(next);
            }
        }

        return [];
    }

    /// <summary>
    /// The set of nodes within <paramref name="hops"/> edges of <paramref name="ego"/>, the ego
    /// itself included. This is the node set of the ego graph (B16 Bloodline); the caller keeps only
    /// the edges whose endpoints both land in this set.
    /// </summary>
    public static IReadOnlySet<Guid> Neighbourhood(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> adjacency,
        Guid ego,
        int hops)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        HashSet<Guid> seen = new() { ego };

        if (hops <= 0 || !adjacency.ContainsKey(ego))
        {
            return seen;
        }

        Queue<(Guid Node, int Depth)> queue = new();
        queue.Enqueue((ego, 0));

        while (queue.Count > 0)
        {
            (Guid node, int depth) = queue.Dequeue();

            if (depth == hops || !adjacency.TryGetValue(node, out IReadOnlyList<Guid>? neighbours))
            {
                continue;
            }

            foreach (Guid next in neighbours)
            {
                if (seen.Add(next))
                {
                    queue.Enqueue((next, depth + 1));
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// A non-repeating walk of at most <paramref name="length"/> nodes starting at
    /// <paramref name="start"/> (feature C8, Rabbit Hole). At each step it moves to the first
    /// not-yet-visited neighbour that <paramref name="chooseNext"/> selects; the walk stops early at
    /// a dead end (no unvisited neighbour), which is honest — a small graph runs out. The
    /// <paramref name="chooseNext"/> delegate picks among the candidate neighbours (e.g. randomly, or
    /// by a fallback to embedding nearness) so the walk stays deterministic in tests.
    /// </summary>
    public static IReadOnlyList<Guid> Walk(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> adjacency,
        Guid start,
        int length,
        Func<IReadOnlyList<Guid>, Guid> chooseNext)
    {
        ArgumentNullException.ThrowIfNull(adjacency);
        ArgumentNullException.ThrowIfNull(chooseNext);

        List<Guid> path = [start];

        if (length <= 1)
        {
            return path;
        }

        HashSet<Guid> visited = [start];
        Guid current = start;

        while (path.Count < length)
        {
            if (!adjacency.TryGetValue(current, out IReadOnlyList<Guid>? neighbours))
            {
                break;
            }

            List<Guid> candidates = neighbours.Where(n => !visited.Contains(n)).ToList();

            if (candidates.Count == 0)
            {
                break;
            }

            Guid next = chooseNext(candidates);
            path.Add(next);
            visited.Add(next);
            current = next;
        }

        return path;
    }

    private static IReadOnlyList<Guid> Reconstruct(IReadOnlyDictionary<Guid, Guid> cameFrom, Guid from, Guid to)
    {
        List<Guid> path = [to];
        Guid step = to;

        while (step != from)
        {
            step = cameFrom[step];
            path.Add(step);
        }

        path.Reverse();
        return path;
    }
}
