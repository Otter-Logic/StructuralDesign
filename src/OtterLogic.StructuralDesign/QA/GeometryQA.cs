using OtterLogic.MachineLearning.Decomposition;
using OtterLogic.Unsupervised.Clustering;
using OtterLogic.StructuralEngine;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// A first-pass health check of a model before it goes to analysis: the places its
/// connectivity will stop a solver or give answers nobody meant.
/// <para>
/// One idea runs through it. An analysis model is a graph — nodes, and the elements
/// joining them — and the geometry is what the graph is drawn in. Connectivity
/// problems are where the two disagree: points close in space that the graph keeps
/// far apart, regions the graph barely holds, members the geometry lines up that
/// the graph never joins. And every "unusual" is judged against the model's own
/// habits, never a rulebook: what counts as a gap, a sliver or a skew is read from
/// the model, so the same check means the same thing on a house in millimetres and
/// a stadium in metres.
/// </para>
/// <list type="bullet">
/// <item><b>Near misses</b> — for nearby pairs the structure connects, the <em>detour
/// ratio</em>: distance along the structure over distance in space. Connected neighbours
/// sit near one; a pair 12 mm apart and 16 m round is in the thousands. The ratios on a
/// different scale from the model's ordinary detours are found by
/// <see cref="ScaleSeparation"/>, and units cancel. Pairs it does not connect nearby have
/// no ratio, so they are judged by their gap against the elements around them, on the
/// same principle.</item>
/// <item><b>Separate parts</b> — exactly, from the graph's connected components (the
/// zero eigenvalues of its Laplacian, counted directly).</item>
/// <item><b>Weak attachments</b> — spectrally: the lowest eigenvectors of the
/// normalised graph Laplacian concentrate on regions the graph barely holds. A sweep
/// grows a region from each end of each ordering and keeps the least-connected one that
/// is a closed sub-structure held by fewer elements than meet at its own typical node —
/// the model compared with itself.</item>
/// <item><b>Short elements</b> — each element's length against the elements it meets,
/// separated by scale.</item>
/// <item><b>Alignment</b> — <see cref="GridLevelInference"/>'s levels and grid, with the
/// lines that belong to one but are not quite on it.</item>
/// </list>
/// <para>
/// A few findings are facts about how a solver reads a model rather than judgements,
/// and are reported whenever found: an end resting on a member with no node there,
/// lines crossing with none, an element drawn twice, one with no length, a support at
/// no node. The document's tolerance is the only distance given.
/// </para>
/// </summary>
public static class GeometryQA
{
    /// <summary>
    /// How far along the structure a route is followed, in multiples of the longest
    /// element, before a pair is taken as not connected nearby. A search reach, not a
    /// verdict: a pair beyond it is judged by its gap rather than its detour, and every
    /// ordinary connection is found well within it.
    /// </summary>
    private const double RouteReach = 4.0;

    /// <summary>Checks a model.</summary>
    /// <param name="starts">n x 3 line start points, or null for none.</param>
    /// <param name="ends">n x 3 line end points, or null for none.</param>
    /// <param name="surfaces">Surface boundaries, each k x 3 corners in order; null for none.</param>
    /// <param name="supports">Optional s x 3 supported points. Without them, support checks are skipped.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public static GeometryQAResult Check(
        double[,]? starts,
        double[,]? ends,
        IReadOnlyList<double[,]>? surfaces = null,
        double[,]? supports = null,
        GeometryQAOptions? options = null)
    {
        options ??= new GeometryQAOptions();
        options.Validate();
        surfaces ??= Array.Empty<double[,]>();

        int lines = starts?.GetLength(0) ?? 0;
        if ((starts is null) != (ends is null))
            throw new ArgumentException("Give both line starts and ends, or neither.", nameof(ends));
        if (starts is not null && (starts.GetLength(1) != 3 || ends!.GetLength(1) != 3 || ends.GetLength(0) != lines))
            throw new ArgumentException("Line starts and ends must both be n x 3, one row per line.", nameof(ends));
        if (lines + surfaces.Count == 0)
            throw new ArgumentException("Need at least one line or surface to check.", nameof(starts));
        if (supports is not null && supports.GetLength(1) != 3)
            throw new ArgumentException("Supports must be s x 3.", nameof(supports));

        foreach (var boundary in surfaces)
            if (boundary is null || boundary.GetLength(1) != 3)
                throw new ArgumentException("Every surface boundary must be k x 3 corners.", nameof(surfaces));

        double tolerance = options.Tolerance;
        var t = QaTopology.Build(starts, ends, surfaces, tolerance);
        var issues = new List<GeometryIssue>();
        var notes = new List<string>();

        var orientation = lines > 0
            ? LineOrientations.Classify(
                Enumerable.Range(0, lines).Select(i => Vec.Row(starts!, i)).ToArray(),
                Enumerable.Range(0, lines).Select(i => Vec.Row(ends!, i)).ToArray(),
                tolerance, options.Banding, new List<string>())
            : Array.Empty<LineOrientation>();

        notes.Add($"Read as {t.Nodes.Length} nodes: points joined only where they coincide within {GeometryQAResult.Length(tolerance)}, "
            + "as a solver will join them.");

        Collapsed(t, issues);
        Duplicates(t, issues);
        var collinear = Overlaps(t, tolerance, issues);
        Bearings(t, tolerance, collinear, issues);
        Crossings(t, tolerance, orientation, collinear, issues);
        NearMisses(t, tolerance, options, issues, notes);
        var (part, partCount) = Parts(t, supports, tolerance, options, issues, notes);
        WeakAttachments(t, part, options, issues);
        if (lines >= 2)
            Alignment(starts!, ends!, t, options, issues, notes);
        ShortElements(t, issues);

        var nodes = new double[t.Nodes.Length, 3];
        for (int j = 0; j < t.Nodes.Length; j++)
            (nodes[j, 0], nodes[j, 1], nodes[j, 2]) = (t.Nodes[j].X, t.Nodes[j].Y, t.Nodes[j].Z);

        return new GeometryQAResult(nodes, lines, t.ElementCount, part, partCount,
            issues.OrderBy(i => i.Kind).ToList(), notes);
    }

    private static string Name(QaTopology t, int element)
        => t.IsSurface(element) ? $"surface {element - t.LineCount}" : $"line {element}";

    private static double[] At(Vec p) => new[] { p.X, p.Y, p.Z };

    /// <summary>What meets at a node, in words: "the end of line 30", "a corner of surfaces 3 and 4".</summary>
    private static string Ends(QaTopology t, int[] elements)
    {
        var lines = elements.Where(e => !t.IsSurface(e)).ToArray();
        var surfaces = elements.Where(t.IsSurface).Select(e => e - t.LineCount).ToArray();
        var parts = new List<string>();

        if (lines.Length == 1)
            parts.Add($"the end of line {lines[0]}");
        else if (lines.Length > 1)
            parts.Add($"the ends of lines {And(lines)}");

        if (surfaces.Length == 1)
            parts.Add($"a corner of surface {surfaces[0]}");
        else if (surfaces.Length > 1)
            parts.Add($"a corner of surfaces {And(surfaces)}");

        return parts.Count > 0 ? string.Join(" and ", parts) : "on no element";
    }

    private static string And(int[] items)
        => items.Length == 1 ? items[0].ToString() : string.Join(", ", items[..^1]) + " and " + items[^1];

    private static void Collapsed(QaTopology t, List<GeometryIssue> issues)
    {
        for (int e = 0; e < t.ElementCount; e++)
        {
            if (!t.Collapsed[e])
                continue;

            string what = t.IsSurface(e) ? "has fewer than three distinct corners" : "has no length — both ends are one node";
            issues.Add(new GeometryIssue(GeometryIssueKind.ZeroLength, new[] { e }, t.Corners[e].Distinct().ToArray(),
                At(t.Nodes[t.Corners[e][0]]), 0.0, $"{Capital(Name(t, e))} {what}."));
        }
    }

    private static void Duplicates(QaTopology t, List<GeometryIssue> issues)
    {
        var first = new Dictionary<string, int>();
        for (int e = 0; e < t.ElementCount; e++)
        {
            if (t.Collapsed[e])
                continue;

            string key = (t.IsSurface(e) ? "S" : "L") + string.Join(",", t.Corners[e].Distinct().OrderBy(j => j));
            if (!first.TryGetValue(key, out int earlier))
            {
                first[key] = e;
                continue;
            }

            var centre = Centre(t, e);
            issues.Add(new GeometryIssue(GeometryIssueKind.Duplicate, new[] { earlier, e }, t.Corners[e].Distinct().ToArray(),
                At(centre), 0.0, $"{Capital(Name(t, e))} is drawn on top of {Name(t, earlier)} — the solver will count it twice."));
        }
    }

    /// <summary>Line pairs lying along each other for more than the tolerance; returned so later checks do not report them again.</summary>
    private static HashSet<(int, int)> Overlaps(QaTopology t, double tolerance, List<GeometryIssue> issues)
    {
        var collinear = new HashSet<(int, int)>();

        for (int s = 0; s < t.Segments.Length; s++)
        {
            var (e, a, b) = t.Segments[s];
            if (t.IsSurface(e))
                continue;

            var pa = t.Nodes[a];
            var pb = t.Nodes[b];
            double length = pa.DistanceTo(pb);
            var direction = (pb - pa) / length;

            foreach (int other in t.SegmentsNearSegment(s, tolerance))
            {
                var (f, c, d) = t.Segments[other];
                if (f <= e || t.IsSurface(f) || collinear.Contains((e, f)))
                    continue;

                // Collinear at the tolerance: both ends of the other on this line's axis.
                double offC = (t.Nodes[c] - pa).Cross(direction).Length;
                double offD = (t.Nodes[d] - pa).Cross(direction).Length;
                if (offC > tolerance || offD > tolerance)
                    continue;

                double tc = (t.Nodes[c] - pa).Dot(direction);
                double td = (t.Nodes[d] - pa).Dot(direction);
                double shared = Math.Min(length, Math.Max(tc, td)) - Math.Max(0.0, Math.Min(tc, td));
                if (shared <= tolerance)
                    continue;

                bool same = new[] { a, b }.OrderBy(j => j).SequenceEqual(new[] { c, d }.OrderBy(j => j));
                collinear.Add((e, f));
                if (same)
                    continue;

                double from = Math.Max(0.0, Math.Min(tc, td));
                var middle = pa + (from + 0.5 * shared) * direction;
                issues.Add(new GeometryIssue(GeometryIssueKind.Overlap, new[] { e, f }, new[] { a, b, c, d }.Distinct().ToArray(),
                    At(middle), shared,
                    $"Line {e} and line {f} lie along each other for {GeometryQAResult.Length(shared)} — one member drawn as two "
                    + "that overlap, which a solver reads as two members side by side."));
            }
        }

        return collinear;
    }

    private static void Bearings(QaTopology t, double tolerance, HashSet<(int, int)> collinear, List<GeometryIssue> issues)
    {
        var reported = new HashSet<(int Node, int Element)>();

        for (int j = 0; j < t.Nodes.Length; j++)
        {
            var p = t.Nodes[j];
            var own = t.ElementsAt[j];

            foreach (int s in t.SegmentsNear(p, tolerance))
            {
                var (e, a, b) = t.Segments[s];
                if (own.Contains(e) || reported.Contains((j, e)))
                    continue;
                if (own.Any(o => collinear.Contains((Math.Min(o, e), Math.Max(o, e)))))
                    continue;

                double length = t.Nodes[a].DistanceTo(t.Nodes[b]);
                var (distance, along) = QaTopology.PointToSegment(p, t.Nodes[a], t.Nodes[b]);
                if (distance > tolerance || along * length <= tolerance || (1.0 - along) * length <= tolerance)
                    continue;

                reported.Add((j, e));
                string on = t.IsSurface(e) ? $"an edge of {Name(t, e)}" : Name(t, e);
                issues.Add(new GeometryIssue(GeometryIssueKind.UnnodedBearing, own.Append(e).ToArray(), new[] { j }, At(p), 0.0,
                    $"Node {j}, {Ends(t, own)}, rests on {on} with no node there, so a solver will not connect them. "
                    + $"Split {Name(t, e)} at node {j}."));
            }
        }
    }

    private static void Crossings(
        QaTopology t, double tolerance, LineOrientation[] orientation, HashSet<(int, int)> collinear, List<GeometryIssue> issues)
    {
        var found = new List<(int E, int F, Vec Point, string Kind)>();

        for (int s = 0; s < t.Segments.Length; s++)
        {
            var (e, a, b) = t.Segments[s];
            if (t.IsSurface(e))
                continue;

            foreach (int other in t.SegmentsNearSegment(s, tolerance))
            {
                var (f, c, d) = t.Segments[other];
                if (f <= e || t.IsSurface(f) || collinear.Contains((e, f)))
                    continue;
                if (a == c || a == d || b == c || b == d)
                    continue;

                var (distance, u, v) = QaTopology.SegmentToSegment(t.Nodes[a], t.Nodes[b], t.Nodes[c], t.Nodes[d]);
                double le = t.Nodes[a].DistanceTo(t.Nodes[b]);
                double lf = t.Nodes[c].DistanceTo(t.Nodes[d]);
                if (distance > tolerance || u * le <= tolerance || (1 - u) * le <= tolerance || v * lf <= tolerance || (1 - v) * lf <= tolerance)
                    continue;

                var kinds = new[] { orientation[e].ToString(), orientation[f].ToString() }.OrderBy(k => k);
                found.Add((e, f, t.Nodes[a] + u * (t.Nodes[b] - t.Nodes[a]), string.Join("–", kinds)));
            }
        }

        // Whether a crossing is one of a repeated pattern or the only one of its kind is
        // said, not judged: twenty-four brace-to-brace crossings are usually X-bracing
        // meant to pass; the single beam-to-column crossing usually is not.
        var counts = found.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (e, f, point, kind) in found.OrderBy(c => counts[c.Kind]))
        {
            int count = counts[kind];
            string pattern = count == 1
                ? $"the only unjoined {kind} crossing in the model"
                : $"one of {count} unjoined {kind} crossings — a repeated pattern, often deliberate";
            issues.Add(new GeometryIssue(GeometryIssueKind.UnjoinedCrossing, new[] { e, f }, Array.Empty<int>(), At(point), count,
                $"Line {e} and line {f} cross with no node: {pattern}."));
        }
    }

    private static void NearMisses(QaTopology t, double tolerance, GeometryQAOptions options, List<GeometryIssue> issues, List<string> notes)
    {
        if (t.Longest <= 0.0 || t.Nodes.Length < 2)
            return;

        double cap = RouteReach * t.Longest;
        var pairs = new List<(int Node, int Other, int Segment, Vec Target, double Distance, double Route, bool Reached)>();
        var seen = new HashSet<(int, int, int)>();

        for (int i = 0; i < t.Nodes.Length; i++)
        {
            var p = t.Nodes[i];
            var own = t.ElementsAt[i];
            var nearest = t.Nearest(i, options.Neighbours);
            if (nearest.Count == 0)
                continue;

            var routes = t.RoutesFrom(i, cap);
            double Route(int node, out bool reached)
            {
                reached = routes.TryGetValue(node, out double cost);
                return reached ? cost : cap;
            }

            foreach (var (j, distance) in nearest)
            {
                if (own.Intersect(t.ElementsAt[j]).Any())
                    continue;
                if (!seen.Add((Math.Min(i, j), Math.Max(i, j), -1)))
                    continue;

                double route = Route(j, out bool reached);
                pairs.Add((i, j, -1, t.Nodes[j], distance, route, reached));
            }

            double radius = nearest[^1].Distance;
            foreach (int s in t.SegmentsNear(p, radius))
            {
                var (e, a, b) = t.Segments[s];
                if (own.Contains(e) || !seen.Add((i, -1, s)))
                    continue;

                double length = t.Nodes[a].DistanceTo(t.Nodes[b]);
                var (distance, along) = QaTopology.PointToSegment(p, t.Nodes[a], t.Nodes[b]);
                if (distance <= tolerance || distance > radius || along * length <= tolerance || (1 - along) * length <= tolerance)
                    continue;

                double viaA = Route(a, out bool reachedA) + along * length;
                double viaB = Route(b, out bool reachedB) + (1 - along) * length;
                pairs.Add((i, -1, s, t.Nodes[a] + along * (t.Nodes[b] - t.Nodes[a]), distance,
                    Math.Min(viaA, viaB), reachedA || reachedB));
            }
        }

        if (pairs.Count == 0)
            return;

        // A near miss is two things at once: close in space for the elements around it,
        // and far apart along the structure — or not connected nearby at all. Either
        // alone is ordinary. Two column bases 6 m apart with no ground beam are 14 m
        // round, a detour of 2.3, but 6 m is no gap beside 4 and 6 m members; two
        // nodes 50 mm apart on a gusset are close, but joined through a third node a
        // few millimetres round. Both measures are separated by scale against the
        // model's own population, so neither needs a number.
        var spacing = Enumerable.Range(0, pairs.Count).Select(k =>
        {
            var (i, j, segment, _, distance, _, _) = pairs[k];
            var around = t.ElementsAt[i].Concat(j >= 0 ? t.ElementsAt[j] : new[] { t.Segments[segment].Element })
                .Select(e => ElementSize(t, e)).Where(v => v > 0.0).DefaultIfEmpty(distance).OrderBy(v => v).ToArray();
            return Math.Log10(distance / around[around.Length / 2]);
        }).ToArray();
        var close = ScaleSeparation.Below(spacing).ToHashSet();

        // The detour is only defined for pairs the structure connects within reach;
        // reading one off the search reach for the rest would invent a spread the real
        // ratios then have to beat.
        var reachable = Enumerable.Range(0, pairs.Count).Where(k => pairs[k].Reached).ToArray();
        var detours = reachable.Select(k => Math.Log10(Math.Max(pairs[k].Route, pairs[k].Distance) / pairs[k].Distance)).ToArray();
        var farRound = ScaleSeparation.Above(detours).Select(i => reachable[i]).ToHashSet();

        var byDetour = farRound.Where(close.Contains).ToHashSet();
        var byGap = close.Where(k => !pairs[k].Reached).ToHashSet();

        var flagged = byDetour.Concat(byGap).Distinct().ToList();
        if (byDetour.Count > 0)
        {
            double ordinary = Enumerable.Range(0, reachable.Length).Where(i => !byDetour.Contains(reachable[i]))
                .Select(i => detours[i]).DefaultIfEmpty(0.0).Max();
            notes.Add($"Nearby points connected as drawn detour up to {Math.Pow(10, ordinary):0.#}x their distance apart along "
                + $"the structure; {byDetour.Count} pair(s) detour on another scale — near misses.");
        }

        if (byGap.Count > 0)
            notes.Add($"{byGap.Count} pair(s) not connected nearby at all sit closer than the model's ordinary spacing by a "
                + "scale of their own — near misses between parts.");

        foreach (int k in flagged.OrderBy(k => pairs[k].Distance))
        {
            var (i, j, segment, target, distance, route, reached) = pairs[k];

            // The elements to point at are the dangling side's, not everything at both
            // nodes: a column foot 12 mm off a floor node is one element against the six
            // meeting at the floor, and it is the column that is wrong. Fewer elements
            // meeting is the dangling side; equal, and neither can be told, so both. A
            // node near the middle of a member names both it and the member.
            int[] elements;
            if (j < 0)
                elements = t.ElementsAt[i].Append(t.Segments[segment].Element).Distinct().ToArray();
            else if (t.ElementsAt[i].Length != t.ElementsAt[j].Length)
                elements = t.ElementsAt[i].Length < t.ElementsAt[j].Length ? t.ElementsAt[i] : t.ElementsAt[j];
            else
                elements = t.ElementsAt[i].Concat(t.ElementsAt[j]).Distinct().ToArray();

            int onto = j >= 0 ? -1 : t.Segments[segment].Element;
            string there = j >= 0 ? $"node {j}" : (t.IsSurface(onto) ? $"an edge of {Name(t, onto)}" : Name(t, onto));
            string round = reached
                ? $"but {GeometryQAResult.Length(route)} apart along the structure ({route / distance:0}x further)"
                : "but not connected to it anywhere nearby along the structure";

            issues.Add(new GeometryIssue(GeometryIssueKind.NearMiss, elements, j >= 0 ? new[] { i, j } : new[] { i },
                At(0.5 * (t.Nodes[i] + target)), distance,
                $"Node {i} is {GeometryQAResult.Length(distance)} from {there} {round} — probably meant to meet."));
        }
    }

    /// <summary>A line's length, or a surface's shortest edge.</summary>
    private static double ElementSize(QaTopology t, int e)
    {
        var ring = t.Corners[e];
        if (!t.IsSurface(e))
            return t.Nodes[ring[0]].DistanceTo(t.Nodes[ring[1]]);

        double shortest = double.PositiveInfinity;
        for (int v = 0; v < ring.Length; v++)
        {
            double edge = t.Nodes[ring[v]].DistanceTo(t.Nodes[ring[(v + 1) % ring.Length]]);
            if (edge > 0.0)
                shortest = Math.Min(shortest, edge);
        }

        return double.IsInfinity(shortest) ? 0.0 : shortest;
    }

    private static (int[] Part, int Count) Parts(
        QaTopology t, double[,]? supports, double tolerance, GeometryQAOptions options, List<GeometryIssue> issues, List<string> notes)
    {
        var component = t.Links.ConnectedComponents(out int count);
        if (t.Nodes.Length == 0)
            return (Array.Empty<int>(), 0);

        // Largest first, so part 0 is the main structure.
        var order = Enumerable.Range(0, count)
            .OrderByDescending(c => component.Count(x => x == c)).ThenBy(c => Array.IndexOf(component, c)).ToArray();
        var rank = new int[count];
        for (int r = 0; r < count; r++)
            rank[order[r]] = r;
        var part = component.Take(t.Nodes.Length).Select(c => rank[c]).ToArray();

        var supported = new bool[count];
        bool haveSupports = supports is not null && supports.GetLength(0) > 0;
        if (haveSupports)
        {
            for (int s = 0; s < supports!.GetLength(0); s++)
            {
                var p = Vec.Row(supports, s);
                int node = Enumerable.Range(0, t.Nodes.Length).FirstOrDefault(j => t.Nodes[j].DistanceTo(p) <= tolerance, -1);
                if (node >= 0)
                {
                    supported[part[node]] = true;
                    continue;
                }

                issues.Add(new GeometryIssue(GeometryIssueKind.StrandedSupport, Array.Empty<int>(), Array.Empty<int>(), At(p), 0.0,
                    $"Support {s} is at no node, so it holds nothing."));
            }
        }
        else
        {
            notes.Add("No supports given, so whether each part is held was not checked.");
        }

        int realParts = 0;
        for (int r = 0; r < count; r++)
        {
            var nodes = Enumerable.Range(0, t.Nodes.Length).Where(j => part[j] == r).ToArray();
            var elements = Enumerable.Range(0, t.ElementCount)
                .Where(e => !t.Collapsed[e] && t.Corners[e].All(j => part[j] == r)).ToArray();
            if (elements.Length == 0)
                continue;

            realParts++;
            if (r == 0)
            {
                if (haveSupports && !supported[0])
                    issues.Add(new GeometryIssue(GeometryIssueKind.Unsupported, elements, Array.Empty<int>(), At(Centroid(t, nodes)), 0.0,
                        "The main structure has none of the given supports at any of its nodes."));
                continue;
            }

            double gap = double.PositiveInfinity;
            foreach (int j in nodes)
                foreach (var (other, distance) in t.Nearest(j, options.Neighbours))
                    if (part[other] == 0)
                        gap = Math.Min(gap, distance);

            string distanceText = double.IsInfinity(gap) ? "" : $", {GeometryQAResult.Length(gap)} from it at the closest";
            string held = !haveSupports ? "" : supported[r] ? " It has a support of its own." : " It has no support, so on its own it is a mechanism.";
            issues.Add(new GeometryIssue(GeometryIssueKind.SeparatePart, elements, nodes, At(Centroid(t, nodes)),
                double.IsInfinity(gap) ? 0.0 : gap,
                $"Part {r}: {elements.Length} element(s) not connected to the main structure at all{distanceText}.{held}"));
        }

        notes.Add(realParts <= 1 ? "The model is one connected structure." : $"The model is in {realParts} separate parts.");
        return (part, realParts);
    }

    /// <summary>
    /// Spectral sweep over the main structure: each of the lowest eigenvectors of the
    /// normalised Laplacian orders the nodes, and the prefix with the least conductance
    /// — fewest links out for the links within — is the cut that eigenvector points at.
    /// </summary>
    private static void WeakAttachments(QaTopology t, int[] part, GeometryQAOptions options, List<GeometryIssue> issues)
    {
        var main = Enumerable.Range(0, t.Nodes.Length).Where(j => part[j] == 0 && t.Links.Degree(j) > 0.0).ToArray();
        int s = main.Length;
        if (s < 4)
            return;

        var local = new Dictionary<int, int>(s);
        for (int k = 0; k < s; k++)
            local[main[k]] = k;

        var neighbours = new int[s][];
        var degree = new double[s];
        for (int k = 0; k < s; k++)
        {
            neighbours[k] = t.Links.Neighbours(main[k]).ToArray().Where(local.ContainsKey).Select(n => local[n]).ToArray();
            degree[k] = neighbours[k].Length;
        }

        // (I + D^-1/2 A D^-1/2) / 2: positive semi-definite, with the Laplacian's
        // lowest eigenvalues as its highest, which is what the solver finds.
        double[,] Multiply(double[,] block)
        {
            int p = block.GetLength(1);
            var image = new double[s, p];
            for (int i = 0; i < s; i++)
                for (int c = 0; c < p; c++)
                {
                    double sum = 0.0;
                    foreach (int j in neighbours[i])
                        sum += block[j, c] / Math.Sqrt(degree[i] * degree[j]);
                    image[i, c] = 0.5 * (block[i, c] + sum);
                }
            return image;
        }

        int count = Math.Min(options.SpectralProbes + 1, s);
        var (_, vectors, _, _) = LeadingEigen.Solve(s, Multiply, count, 1, 1e-8);
        double totalVolume = degree.Sum();
        var reported = new HashSet<string>();

        var valence = main.Select(j => t.ElementsAt[j].Length).ToArray();

        for (int c = 1; c < count; c++)
        {
            // Both ends of the ordering: a region can sit at either extreme.
            foreach (int sign in new[] { 1, -1 })
            {
                var order = Enumerable.Range(0, s).OrderBy(i => sign * vectors[i, c] / Math.Sqrt(degree[i])).ThenBy(i => i).ToArray();
                var inside = new bool[s];
                var sortedValence = new List<int>();
                double volume = 0.0, cut = 0.0, best = double.PositiveInfinity;
                int bestSize = 0;

                // Grow the region from the extreme, keeping the least-connected prefix
                // that is a closed sub-structure held by fewer links than meet at its
                // own typical node. Not the sweep's global minimum, which on any real
                // building is a whole half of it: a small appendage is weak for its
                // size, not weak in absolute terms.
                for (int k = 0; k < s / 2; k++)
                {
                    int node = order[k];
                    inside[node] = true;
                    volume += degree[node];
                    foreach (int j in neighbours[node])
                        cut += inside[j] ? -1.0 : 1.0;

                    int position = sortedValence.BinarySearch(valence[node]);
                    sortedValence.Insert(position < 0 ? ~position : position, valence[node]);

                    int size = k + 1;
                    double internalLinks = (volume - cut) / 2.0;
                    double typical = size % 2 == 1
                        ? sortedValence[size / 2]
                        : 0.5 * (sortedValence[size / 2 - 1] + sortedValence[size / 2]);

                    if (size < 3 || internalLinks < size || cut >= typical)
                        continue;

                    double conductance = cut / Math.Min(volume, totalVolume - volume);
                    if (conductance < best)
                    {
                        best = conductance;
                        bestSize = size;
                    }
                }

                if (bestSize == 0)
                    continue;

                ReportRegion(t, order.Take(bestSize).Select(i => main[i]).OrderBy(j => j).ToArray(), reported, issues);
            }
        }
    }

    /// <summary>Reports a weakly attached region, confirmed on elements rather than graph links.</summary>
    private static void ReportRegion(QaTopology t, int[] region, HashSet<string> reported, List<GeometryIssue> issues)
    {
        if (!reported.Add(string.Join(",", region)))
            return;

        var regionSet = region.ToHashSet();
        var within = Enumerable.Range(0, t.ElementCount).Where(e => !t.Collapsed[e] && t.Corners[e].All(regionSet.Contains)).ToArray();
        var across = Enumerable.Range(0, t.ElementCount)
            .Where(e => !t.Collapsed[e] && t.Corners[e].Any(regionSet.Contains) && !t.Corners[e].All(regionSet.Contains)).ToArray();

        var valences = region.Select(j => (double)t.ElementsAt[j].Length).OrderBy(v => v).ToArray();
        double typical = valences.Length % 2 == 1
            ? valences[valences.Length / 2]
            : 0.5 * (valences[valences.Length / 2 - 1] + valences[valences.Length / 2]);

        if (across.Length >= typical)
            return;

        issues.Add(new GeometryIssue(GeometryIssueKind.WeaklyAttached, within.Concat(across).ToArray(), region,
            At(Centroid(t, region)), across.Length,
            $"A region of {region.Length} nodes and {within.Length} element(s) is held to the rest by {across.Length} "
            + $"element(s) — fewer than the {typical:0.#} that meet at its own typical node: "
            + $"{string.Join(", ", across.Select(e => Name(t, e)))}."));
    }

    private static void Alignment(
        double[,] starts, double[,] ends, QaTopology t, GeometryQAOptions options, List<GeometryIssue> issues, List<string> notes)
    {
        GridLevelResult grid;
        try
        {
            grid = GridLevelInference.Infer(starts, ends, new GridLevelOptions { Tolerance = options.Tolerance, Banding = options.Banding });
        }
        catch (ArgumentException ex)
        {
            notes.Add("Alignment was not checked: " + ex.Message);
            return;
        }

        var levelNames = grid.Levels.Select(l => l.Name).ToHashSet();
        foreach (var issue in grid.Issues)
        {
            if (double.IsNaN(issue.Deviation))
                continue;

            int line = issue.Element;
            var middle = 0.5 * (Vec.Row(starts, line) + Vec.Row(ends, line));
            var kind = levelNames.Contains(issue.Reference) ? GeometryIssueKind.OffLevel : GeometryIssueKind.OffGrid;
            issues.Add(new GeometryIssue(kind, new[] { line }, t.Corners[line].Distinct().ToArray(), At(middle),
                Math.Abs(issue.Deviation), issue.Message));
        }
    }

    private static void ShortElements(QaTopology t, List<GeometryIssue> issues)
    {
        double Size(int e) => ElementSize(t, e);

        var measured = new List<(int Element, double Size, double Around)>();
        for (int e = 0; e < t.ElementCount; e++)
        {
            if (t.Collapsed[e])
                continue;

            var others = t.Corners[e].SelectMany(j => t.ElementsAt[j]).Distinct().Where(o => o != e && !t.Collapsed[o])
                .Select(Size).OrderBy(v => v).ToArray();
            if (others.Length == 0)
                continue;

            double around = others.Length % 2 == 1 ? others[others.Length / 2] : 0.5 * (others[others.Length / 2 - 1] + others[others.Length / 2]);
            measured.Add((e, Size(e), around));
        }

        if (measured.Count == 0)
            return;

        var ratios = measured.Select(m => Math.Log10(m.Size / m.Around)).ToArray();
        foreach (int k in ScaleSeparation.Below(ratios).OrderBy(k => ratios[k]))
        {
            var (e, size, around) = measured[k];
            issues.Add(new GeometryIssue(GeometryIssueKind.ShortElement, new[] { e }, t.Corners[e].Distinct().ToArray(),
                At(Centre(t, e)), around / size,
                $"{Capital(Name(t, e))} is {GeometryQAResult.Length(size)} long — {around / size:0}x shorter than the elements it "
                + $"meets ({GeometryQAResult.Length(around)}), on a scale of its own. An element that much smaller than its "
                + "neighbours can make a solve unstable."));
        }
    }

    private static Vec Centre(QaTopology t, int e) => Centroid(t, t.Corners[e].Distinct().ToArray());

    private static Vec Centroid(QaTopology t, int[] nodes)
    {
        var sum = new Vec(0, 0, 0);
        foreach (int j in nodes)
            sum = sum + t.Nodes[j];
        return nodes.Length > 0 ? sum / nodes.Length : sum;
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
