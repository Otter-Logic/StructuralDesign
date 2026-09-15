using System.Globalization;
using OtterLogic.MachineLearning.Graphs;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Discovers the structural intent hidden in a model's geometry: the natural
/// groups its elements fall into, and the places it is not joined the way it looks
/// meant to be.
/// <para>
/// Lines, surfaces and supports in — nothing else, and nothing about what kind of
/// structure it is. There is no rule here that knows a column from a beam, a truss
/// from a shell, or a building from a bridge. Frames, shells, bridges, stadium bowls,
/// gridshells and parametric forms all go through the same six stages, and what
/// comes out is groups described by what was measured. Primary and secondary
/// framing, bracing systems, diaphragm and shell zones, stiff and flexible regions,
/// repeated modules, load-path communities — those are what the groups tend to be,
/// and saying which is which is left to the engineer, downstream, in their own
/// definition.
/// </para>
/// <list type="number">
/// <item><b>Input</b> — lines as start and end points, surfaces as boundary corners,
/// supports as points. Checked and refused with a reason, never repaired.</item>
/// <item><b>Graph construction</b> — points within the join distance weld into
/// joints, and a joint resting along an element joins it there. The elements become
/// the nodes of a graph, joined where they meet; the joints become the nodes of a
/// second, joined along the elements, to measure distances over.
/// See <see cref="StructureGraph"/>.</item>
/// <item><b>Feature extraction</b> — a row per element: centroid, size, extent along
/// each axis, connections, route distance to a support, betweenness centrality,
/// surface or line, aspect ratio. See <see cref="InsightFeatures"/>.</item>
/// <item><b>Unsupervised learning</b> — three views of the same elements from
/// <see cref="MultiViewClustering"/>: spectral clustering of the element graph,
/// cutting where connected elements stop being alike; hierarchical clustering of
/// what elements are like wherever they are; HDBSCAN over everything, whose outliers
/// are the elements that fit nowhere.</item>
/// <item><b>Fusion</b> — the views fused by <see cref="ConsensusClustering"/>: views
/// weighted by how far the others agree with them, a co-association between every
/// pair of elements, and the count the views support over the widest range of
/// thresholds. Each element's agreement between views comes back with it.</item>
/// <item><b>Output</b> — the natural groups and their descriptions, and the QA flags:
/// duplicates, degenerate elements, isolated and disconnected pieces, elements with
/// no route to a support, free ends, single elements the model hangs on, near
/// misses, outliers, and elements the views could not agree on.</item>
/// </list>
/// </summary>
public static class StructuralInsightEngine
{
    /// <summary>
    /// Reads a structural model.
    /// </summary>
    /// <param name="lineStarts">n x 3, the start of each line element. Null when there are only surfaces.</param>
    /// <param name="lineEnds">n x 3, the end of each line element, in the same order.</param>
    /// <param name="surfaces">
    /// Each surface element's boundary corners in order, k x 3 with at least three.
    /// Surfaces are numbered after every line. Null when there are only lines.
    /// </param>
    /// <param name="supports">m x 3 support points. Null or empty skips everything that needs a support.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public static StructuralInsightResult Analyse(
        double[,]? lineStarts,
        double[,]? lineEnds,
        IReadOnlyList<double[,]>? surfaces = null,
        double[,]? supports = null,
        StructuralInsightOptions? options = null)
    {
        options ??= new StructuralInsightOptions();
        var (starts, ends) = CheckLines(lineStarts, lineEnds);
        surfaces = CheckSurfaces(surfaces);
        if (supports is not null)
            CheckPoints(supports, nameof(supports));

        int lines = starts.GetLength(0);
        int n = lines + surfaces.Count;
        if (n < 3)
            throw new ArgumentException($"Need at least three elements to find groups in; got {n}.", nameof(lineStarts));

        options.Validate(n);

        // Graph construction.
        var structure = StructureGraph.Build(starts, ends, surfaces, supports, options.Join);
        var geometry = ElementGeometry.Measure(starts, ends, surfaces);
        double diagonal = Math.Max(Diagonal(structure.Joints), options.Tolerance);

        // Feature extraction.
        var supportDistance = SupportDistances(structure);
        var centrality = Centrality.Betweenness(structure.Elements);
        var features = InsightFeatures.Raw(geometry, structure, supportDistance, centrality);
        var (affinity, hierarchy, density) = InsightFeatures.Views(features, diagonal);

        // Unsupervised learning, and fusion.
        var clustering = MultiViewClustering.Fit(structure.Elements, affinity, hierarchy, density, options.ToClustering());

        // Output.
        var (flags, issues, componentCount) = Inspect(structure, geometry, supportDistance, clustering, options, n);
        var groups = Describe(clustering, structure, geometry, supportDistance, centrality);

        var notes = new List<string>();
        if (!structure.HasSupports)
            notes.Add("No supports given, so support distances and the no-path-to-support check were skipped, and every "
                + "unsupported base reads as a free end. Wire the supports in to read the model the way it stands.");
        if (structure.StrandedSupports.Length > 0)
            notes.Add($"{structure.StrandedSupports.Length} support(s) sit at no joint, so they hold nothing up: "
                + string.Join(", ", structure.StrandedSupports) + ".");
        notes.AddRange(clustering.Notes);

        var joints = new double[structure.Joints.Length, 3];
        for (int j = 0; j < structure.Joints.Length; j++)
            (joints[j, 0], joints[j, 1], joints[j, 2]) = (structure.Joints[j].X, structure.Joints[j].Y, structure.Joints[j].Z);

        return new StructuralInsightResult
        {
            Options = options,
            LineCount = lines,
            SurfaceCount = surfaces.Count,
            Clustering = clustering,
            GroupSummaries = groups,
            Features = features,
            FeatureNames = (string[])InsightFeatures.Names.Clone(),
            Connectivity = structure.Elements,
            Joints = joints,
            ElementJoints = structure.ElementJoints,
            SupportDistance = supportDistance,
            SupportedJoints = Enumerable.Range(0, structure.Joints.Length).Count(j => structure.Supported[j]),
            HasSupports = structure.HasSupports,
            Flags = flags,
            Issues = issues,
            StrandedSupports = structure.StrandedSupports,
            ComponentCount = componentCount,
            Notes = notes,
        };
    }

    /// <summary>
    /// Route length along the elements from each element to the nearest support:
    /// the nearest of its joints. Positive infinity with no route, NaN for every
    /// element when no supports were given.
    /// </summary>
    private static double[] SupportDistances(StructureGraph structure)
    {
        int n = structure.ElementCount;
        var distance = new double[n];

        if (!structure.HasSupports)
        {
            Array.Fill(distance, double.NaN);
            return distance;
        }

        var sources = Enumerable.Range(0, structure.Joints.Length).Where(j => structure.Supported[j]).ToArray();
        if (sources.Length == 0)
        {
            Array.Fill(distance, double.PositiveInfinity);
            return distance;
        }

        var routes = ShortestPaths.From(structure.Routes, sources);
        for (int e = 0; e < n; e++)
            distance[e] = structure.ElementJoints[e].Select(j => routes.Cost[j]).DefaultIfEmpty(double.PositiveInfinity).Min();

        return distance;
    }

    private static (InsightFlag[] Flags, List<InsightIssue> Issues, int Components) Inspect(
        StructureGraph structure, ElementGeometry geometry, double[] supportDistance,
        MultiViewClusteringResult clustering, StructuralInsightOptions options, int n)
    {
        var flags = new InsightFlag[n];
        var issues = new List<InsightIssue>();
        string Number(double value) => value.ToString("G3", CultureInfo.InvariantCulture);

        void Add(int element, InsightFlag flag, Vec at, string reason)
        {
            flags[element] |= flag;
            issues.Add(new InsightIssue(element, flag, at.X, at.Y, at.Z, reason));
        }

        for (int e = 0; e < n; e++)
        {
            var centre = geometry.Centroid[e];

            if (structure.DuplicateOf[e] >= 0)
                Add(e, InsightFlag.Duplicate, centre, $"element {e} is drawn between the same joints as element {structure.DuplicateOf[e]}");

            if (structure.Degenerate[e])
                Add(e, InsightFlag.Degenerate, centre, structure.IsSurface(e)
                    ? $"surface {e} has no area once its corners are joined"
                    : $"line {e} is shorter than the join distance, so its ends are one joint");
        }

        var component = structure.Elements.ConnectedComponents(out int components);
        var sizes = new int[components];
        foreach (int c in component)
            sizes[c]++;
        int largest = Array.IndexOf(sizes, sizes.Max());

        for (int e = 0; e < n; e++)
        {
            if (structure.Elements.Neighbours(e).Length == 0)
                Add(e, InsightFlag.Isolated, geometry.Centroid[e], $"element {e} meets no other element");
            else if (component[e] != largest)
                Add(e, InsightFlag.Disconnected, geometry.Centroid[e],
                    $"element {e} is part of a piece of {sizes[component[e]]} element(s) not connected to the rest of the model");

            if (structure.HasSupports && double.IsPositiveInfinity(supportDistance[e]))
                Add(e, InsightFlag.NoPathToSupport, geometry.Centroid[e], $"no route along the elements takes element {e} to a support");
        }

        for (int e = 0; e < structure.LineCount; e++)
        {
            if (structure.Degenerate[e])
                continue;

            foreach (int joint in new[] { structure.Outline[e][0], structure.Outline[e][^1] })
                if (structure.Valence[joint] == 1 && !structure.Supported[joint])
                    Add(e, InsightFlag.FreeEnd, structure.Joints[joint],
                        $"an end of line {e} meets nothing and no support holds it — a cantilever tip, or a connection that was missed");
        }

        var stranded = CutVertices.Stranded(structure.Elements);
        int threshold = Math.Max(2, (int)Math.Ceiling(options.WeakConnectionShare * n));
        for (int e = 0; e < n; e++)
            if (stranded[e] >= threshold)
                Add(e, InsightFlag.WeakConnection, geometry.Centroid[e],
                    $"removing element {e} alone would cut {stranded[e]} element(s) off from the rest of the model");

        var touching = new List<int>[structure.Joints.Length];
        for (int j = 0; j < touching.Length; j++)
            touching[j] = new List<int>();
        for (int e = 0; e < n; e++)
            foreach (int j in structure.ElementJoints[e])
                touching[j].Add(e);

        for (int j = 0; j < structure.Joints.Length; j++)
            if (structure.Spread[j] > options.Tolerance)
                foreach (int e in touching[j])
                    Add(e, InsightFlag.NearMiss, structure.Joints[j],
                        $"element {e} is joined here to points up to {Number(structure.Spread[j])} apart — beyond the "
                        + "tolerance, so the analysis may not join them");

        foreach (int e in clustering.Outliers())
            Add(e, InsightFlag.Outlier, geometry.Centroid[e], $"element {e} fits no dense group of alike elements");

        for (int e = 0; e < n; e++)
            if (clustering.Agreement[e] < options.LowAgreement)
                Add(e, InsightFlag.LowAgreement, geometry.Centroid[e],
                    $"the clustering views split on which elements {e} belongs with (agreement {clustering.Agreement[e]:0.00})");

        return (flags, issues, components);
    }

    private static IReadOnlyList<InsightGroup> Describe(
        MultiViewClusteringResult clustering, StructureGraph structure, ElementGeometry geometry,
        double[] supportDistance, double[] centrality)
    {
        var members = clustering.Consensus.Members();
        var labels = clustering.Labels;
        var groups = new List<InsightGroup>(members.Length);

        for (int g = 0; g < members.Length; g++)
        {
            var elements = members[g];
            var reachable = elements.Select(e => supportDistance[e]).Where(double.IsFinite).ToArray();

            groups.Add(new InsightGroup(
                g,
                elements,
                elements.Count(e => !structure.IsSurface(e)),
                elements.Count(structure.IsSurface),
                elements.Average(e => geometry.Size[e]),
                elements.Average(e => geometry.Extent[e].X),
                elements.Average(e => geometry.Extent[e].Y),
                elements.Average(e => geometry.Extent[e].Z),
                elements.Average(e => (double)structure.Elements.Neighbours(e).Length),
                reachable.Length > 0 ? reachable.Average() : double.NaN,
                elements.Average(e => centrality[e]),
                Pieces(structure.Elements, elements, labels, g),
                elements.Average(e => clustering.Agreement[e])));
        }

        return groups;
    }

    /// <summary>Connected pieces of the element graph restricted to one group.</summary>
    private static int Pieces(WeightedGraph graph, int[] elements, int[] labels, int group)
    {
        var seen = new HashSet<int>();
        int pieces = 0;
        var stack = new Stack<int>();

        foreach (int start in elements)
        {
            if (!seen.Add(start))
                continue;

            pieces++;
            stack.Push(start);
            while (stack.Count > 0)
                foreach (int next in graph.Neighbours(stack.Pop()))
                    if (labels[next] == group && seen.Add(next))
                        stack.Push(next);
        }

        return pieces;
    }

    private static double Diagonal(Vec[] joints)
    {
        if (joints.Length == 0)
            return 0.0;

        var low = new Vec(joints.Min(j => j.X), joints.Min(j => j.Y), joints.Min(j => j.Z));
        var high = new Vec(joints.Max(j => j.X), joints.Max(j => j.Y), joints.Max(j => j.Z));
        return low.DistanceTo(high);
    }

    private static (double[,] Starts, double[,] Ends) CheckLines(double[,]? starts, double[,]? ends)
    {
        if (starts is null && ends is null)
            return (new double[0, 3], new double[0, 3]);
        if (starts is null || ends is null)
            throw new ArgumentException("Give both the starts and the ends of the lines, or neither.");

        CheckPoints(starts, "lineStarts");
        CheckPoints(ends, "lineEnds");
        if (starts.GetLength(0) != ends.GetLength(0))
            throw new ArgumentException(
                $"{starts.GetLength(0)} line starts but {ends.GetLength(0)} line ends. Every line needs both, in the same order.");

        return (starts, ends);
    }

    private static IReadOnlyList<double[,]> CheckSurfaces(IReadOnlyList<double[,]>? surfaces)
    {
        if (surfaces is null)
            return Array.Empty<double[,]>();

        for (int k = 0; k < surfaces.Count; k++)
        {
            if (surfaces[k] is null)
                throw new ArgumentNullException(nameof(surfaces), $"Surface {k} is missing.");
            CheckPoints(surfaces[k], $"surface {k}");
            if (surfaces[k].GetLength(0) < 3)
                throw new ArgumentException(
                    $"Surface {k} has {surfaces[k].GetLength(0)} corner(s); a surface needs at least three corners.", nameof(surfaces));
        }

        return surfaces;
    }

    private static void CheckPoints(double[,] points, string name)
    {
        if (points.GetLength(1) != 3)
            throw new ArgumentException($"{name} needs three columns, x, y and z; it has {points.GetLength(1)}.", name);

        for (int i = 0; i < points.GetLength(0); i++)
            for (int c = 0; c < 3; c++)
                if (!double.IsFinite(points[i, c]))
                    throw new ArgumentException($"{name} point {i} has {points[i, c]} in it; coordinates must be finite.", name);
    }
}
