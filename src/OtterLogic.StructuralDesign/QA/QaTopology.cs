using OtterLogic.MachineLearning.Graphs;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// A model read the way an analysis will read it: points joined only where they
/// coincide at the document's tolerance, and elements connected only through the
/// nodes they share.
/// <para>
/// Deliberately not <see cref="StructureGraph"/>, which joins ends within ten times
/// the tolerance and reads an end resting along a member as connected to it — the
/// generous reading the Insight Engine wants for finding groups. A solver is not
/// generous: ends 5 mm apart are two nodes, and a beam end on a column face is not
/// on the column unless the column has a node there. Checking a model for analysis
/// means reading it as strictly as the solver will.
/// </para>
/// </summary>
internal sealed class QaTopology
{
    /// <summary>Most grid cells a segment is indexed under before it is kept on a list every query checks.</summary>
    private const long MaximumCellsPerSegment = 50_000;

    private readonly Dictionary<(long, long, long), List<int>> _nodeCells = new();
    private readonly Dictionary<(long, long, long), List<int>> _segmentCells = new();
    private readonly List<int> _largeSegments = new();
    private double _cell;

    private QaTopology()
    {
    }

    public Vec[] Nodes { get; private init; } = null!;
    public int LineCount { get; private init; }
    public int ElementCount => Corners.Length;

    /// <summary>Per element, its nodes: a line's two ends, a surface's corner ring.</summary>
    public int[][] Corners { get; private init; } = null!;

    /// <summary>A line whose ends are one node, or a surface with fewer than three distinct corners.</summary>
    public bool[] Collapsed { get; private init; } = null!;

    /// <summary>Every line and every surface edge that has length, with the element it belongs to.</summary>
    public (int Element, int A, int B)[] Segments { get; private init; } = null!;

    /// <summary>Per node, the elements with it as a corner.</summary>
    public int[][] ElementsAt { get; private init; } = null!;

    /// <summary>Nodes joined along every segment, weighted by its length — for route distances.</summary>
    public WeightedGraph Routes { get; private init; } = null!;

    /// <summary>The same joins with unit weights — for anything that should not care about units.</summary>
    public WeightedGraph Links { get; private init; } = null!;

    /// <summary>The longest segment in the model.</summary>
    public double Longest { get; private init; }

    public bool IsSurface(int element) => element >= LineCount;

    public static QaTopology Build(double[,]? starts, double[,]? ends, IReadOnlyList<double[,]> surfaces, double tolerance)
    {
        int lines = starts?.GetLength(0) ?? 0;
        var positions = new List<Vec>();
        var weld = new Dictionary<(long, long, long), List<int>>();

        int Weld(Vec p)
        {
            var home = Cell(p, tolerance);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                        if (weld.TryGetValue((home.Item1 + dx, home.Item2 + dy, home.Item3 + dz), out var here))
                            foreach (int node in here)
                                if (positions[node].DistanceTo(p) <= tolerance)
                                    return node;

            positions.Add(p);
            if (!weld.TryGetValue(home, out var list))
                weld[home] = list = new List<int>();
            list.Add(positions.Count - 1);
            return positions.Count - 1;
        }

        int count = lines + surfaces.Count;
        var corners = new int[count][];
        var collapsed = new bool[count];

        for (int i = 0; i < lines; i++)
        {
            int a = Weld(Vec.Row(starts!, i));
            int b = Weld(Vec.Row(ends!, i));
            corners[i] = new[] { a, b };
            collapsed[i] = a == b;
        }

        for (int k = 0; k < surfaces.Count; k++)
        {
            var ring = new List<int>();
            for (int v = 0; v < surfaces[k].GetLength(0); v++)
            {
                int node = Weld(Vec.Row(surfaces[k], v));
                if (ring.Count == 0 || ring[^1] != node)
                    ring.Add(node);
            }

            if (ring.Count > 1 && ring[^1] == ring[0])
                ring.RemoveAt(ring.Count - 1);

            corners[lines + k] = ring.ToArray();
            collapsed[lines + k] = ring.Distinct().Count() < 3;
        }

        var nodes = positions.ToArray();
        var segments = new List<(int, int, int)>();
        for (int e = 0; e < count; e++)
        {
            if (collapsed[e])
                continue;

            if (e < lines)
            {
                segments.Add((e, corners[e][0], corners[e][1]));
                continue;
            }

            var ring = corners[e];
            for (int v = 0; v < ring.Length; v++)
                if (ring[v] != ring[(v + 1) % ring.Length])
                    segments.Add((e, ring[v], ring[(v + 1) % ring.Length]));
        }

        var elementsAt = new List<int>[nodes.Length];
        for (int j = 0; j < nodes.Length; j++)
            elementsAt[j] = new List<int>();
        for (int e = 0; e < count; e++)
            foreach (int j in corners[e].Distinct())
                elementsAt[j].Add(e);

        var lengths = segments.Select(s => nodes[s.Item2].DistanceTo(nodes[s.Item3])).ToArray();
        int size = Math.Max(nodes.Length, 1);

        var topology = new QaTopology
        {
            Nodes = nodes,
            LineCount = lines,
            Corners = corners,
            Collapsed = collapsed,
            Segments = segments.ToArray(),
            ElementsAt = elementsAt.Select(list => list.ToArray()).ToArray(),
            Routes = WeightedGraph.FromEdges(size, segments.Select((s, i) => (s.Item2, s.Item3, lengths[i]))),
            Links = WeightedGraph.FromEdges(size, segments.Select(s => (s.Item2, s.Item3))),
            Longest = lengths.Length > 0 ? lengths.Max() : 0.0,
        };

        topology.Index(lengths, tolerance);
        return topology;
    }

    /// <summary>
    /// Buckets nodes and segments on a grid sized to the model's median segment, so
    /// "what is near here" costs a few cells rather than the whole model. The cell
    /// size changes only how fast a query is, never what it finds.
    /// </summary>
    private void Index(double[] lengths, double tolerance)
    {
        var sorted = lengths.Where(l => l > 0.0).OrderBy(l => l).ToArray();
        _cell = sorted.Length > 0 ? Math.Max(sorted[sorted.Length / 2], tolerance) : Math.Max(tolerance, 1.0);

        for (int j = 0; j < Nodes.Length; j++)
        {
            var key = Cell(Nodes[j], _cell);
            if (!_nodeCells.TryGetValue(key, out var list))
                _nodeCells[key] = list = new List<int>();
            list.Add(j);
        }

        for (int s = 0; s < Segments.Length; s++)
        {
            var (_, a, b) = Segments[s];
            var low = Cell(Min(Nodes[a], Nodes[b]), _cell);
            var high = Cell(Max(Nodes[a], Nodes[b]), _cell);
            long cells = (high.Item1 - low.Item1 + 1) * (high.Item2 - low.Item2 + 1) * (high.Item3 - low.Item3 + 1);

            if (cells > MaximumCellsPerSegment)
            {
                _largeSegments.Add(s);
                continue;
            }

            for (long x = low.Item1; x <= high.Item1; x++)
                for (long y = low.Item2; y <= high.Item2; y++)
                    for (long z = low.Item3; z <= high.Item3; z++)
                    {
                        if (!_segmentCells.TryGetValue((x, y, z), out var list))
                            _segmentCells[(x, y, z)] = list = new List<int>();
                        list.Add(s);
                    }
        }
    }

    /// <summary>The <paramref name="k"/> nodes nearest node <paramref name="node"/>, nearest first, with their distances.</summary>
    public List<(int Node, double Distance)> Nearest(int node, int k)
    {
        var p = Nodes[node];
        var home = Cell(p, _cell);
        var found = new List<(int Node, double Distance)>();
        long maximumRing = (long)Math.Ceiling(Extent() / _cell) + 1;

        for (long ring = 0; ring <= maximumRing; ring++)
        {
            for (long x = -ring; x <= ring; x++)
                for (long y = -ring; y <= ring; y++)
                    for (long z = -ring; z <= ring; z++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != ring)
                            continue;
                        if (!_nodeCells.TryGetValue((home.Item1 + x, home.Item2 + y, home.Item3 + z), out var here))
                            continue;
                        foreach (int other in here)
                            if (other != node)
                                found.Add((other, p.DistanceTo(Nodes[other])));
                    }

            // Everything within ring cells of the home cell is now in hand, so any
            // node closer than ring x cell has been seen.
            if (found.Count >= k)
            {
                found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                if (found[k - 1].Distance <= ring * _cell)
                    return found.Take(k).ToList();
            }
        }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return found.Take(k).ToList();
    }

    /// <summary>Segments with any part in the cells within <paramref name="radius"/> of a point.</summary>
    public IEnumerable<int> SegmentsNear(Vec p, double radius)
    {
        var low = Cell(p - new Vec(radius, radius, radius), _cell);
        var high = Cell(p + new Vec(radius, radius, radius), _cell);
        var seen = new HashSet<int>(_largeSegments);

        long cells = (high.Item1 - low.Item1 + 1) * (high.Item2 - low.Item2 + 1) * (high.Item3 - low.Item3 + 1);
        if (cells > MaximumCellsPerSegment)
            return Enumerable.Range(0, Segments.Length);

        for (long x = low.Item1; x <= high.Item1; x++)
            for (long y = low.Item2; y <= high.Item2; y++)
                for (long z = low.Item3; z <= high.Item3; z++)
                    if (_segmentCells.TryGetValue((x, y, z), out var here))
                        seen.UnionWith(here);

        return seen;
    }

    /// <summary>Segments sharing a grid cell with segment <paramref name="segment"/>'s bounding box.</summary>
    public IEnumerable<int> SegmentsNearSegment(int segment, double reach)
    {
        var (_, a, b) = Segments[segment];
        var low = Min(Nodes[a], Nodes[b]) - new Vec(reach, reach, reach);
        var high = Max(Nodes[a], Nodes[b]) + new Vec(reach, reach, reach);
        var centre = 0.5 * (low + high);
        double radius = 0.5 * (high - low).Length;
        return SegmentsNear(centre, radius);
    }

    /// <summary>
    /// Route length along the structure from <paramref name="source"/> to every node it
    /// reaches within <paramref name="cap"/>; nodes not reached are absent.
    /// </summary>
    public Dictionary<int, double> RoutesFrom(int source, double cap)
    {
        var best = new Dictionary<int, double> { [source] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(source, 0.0);
        var settled = new HashSet<int>();

        while (queue.TryDequeue(out int node, out double cost))
        {
            if (!settled.Add(node) || cost > best[node])
                continue;

            var neighbours = Routes.Neighbours(node);
            var weights = Routes.EdgeWeights(node);
            for (int e = 0; e < neighbours.Length; e++)
            {
                double through = cost + weights[e];
                if (through > cap)
                    continue;
                if (!best.TryGetValue(neighbours[e], out double known) || through < known)
                {
                    best[neighbours[e]] = through;
                    queue.Enqueue(neighbours[e], through);
                }
            }
        }

        return best;
    }

    /// <summary>Closest point on a segment to a point: distance, and how far along from its start, 0 to 1.</summary>
    public static (double Distance, double Along) PointToSegment(Vec p, Vec a, Vec b)
    {
        var ab = b - a;
        double lengthSquared = ab.Dot(ab);
        double t = lengthSquared > 0.0 ? Math.Clamp((p - a).Dot(ab) / lengthSquared, 0.0, 1.0) : 0.0;
        return (p.DistanceTo(a + t * ab), t);
    }

    /// <summary>Closest approach of two segments: distance, and the parameters on each, 0 to 1.</summary>
    public static (double Distance, double S, double T) SegmentToSegment(Vec p1, Vec q1, Vec p2, Vec q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
        double s, t;

        if (a <= 0.0 && e <= 0.0)
            return (p1.DistanceTo(p2), 0.0, 0.0);

        if (a <= 0.0)
        {
            s = 0.0;
            t = Math.Clamp(f / e, 0.0, 1.0);
        }
        else
        {
            double c = d1.Dot(r);
            if (e <= 0.0)
            {
                t = 0.0;
                s = Math.Clamp(-c / a, 0.0, 1.0);
            }
            else
            {
                double b = d1.Dot(d2);
                double denominator = a * e - b * b;
                s = denominator > 0.0 ? Math.Clamp((b * f - c * e) / denominator, 0.0, 1.0) : 0.0;
                t = (b * s + f) / e;
                if (t < 0.0)
                {
                    t = 0.0;
                    s = Math.Clamp(-c / a, 0.0, 1.0);
                }
                else if (t > 1.0)
                {
                    t = 1.0;
                    s = Math.Clamp((b - c) / a, 0.0, 1.0);
                }
            }
        }

        return ((p1 + s * d1).DistanceTo(p2 + t * d2), s, t);
    }

    private double Extent()
    {
        if (Nodes.Length == 0)
            return 0.0;
        var low = Nodes.Aggregate(Min);
        var high = Nodes.Aggregate(Max);
        return (high - low).Length;
    }

    private static Vec Min(Vec a, Vec b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Vec Max(Vec a, Vec b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private static (long, long, long) Cell(Vec p, double size)
        => ((long)Math.Floor(p.X / size), (long)Math.Floor(p.Y / size), (long)Math.Floor(p.Z / size));
}
