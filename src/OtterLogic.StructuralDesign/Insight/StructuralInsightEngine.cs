using System.Globalization;
using OtterLogic.Graphs;
using OtterLogic.Unsupervised.Clustering;
using OtterLogic.StructuralEngine;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Discovers the structural intent hidden in a model's geometry: the natural
/// groups its elements fall into, and the places it is not joined the way it looks
/// meant to be.
/// <para>
/// Lines, surfaces and supports in — nothing else, and nothing about what kind of
/// structure it is. There is no rule here that knows a column from a beam, a truss
/// from a shell, or a building from a bridge. Frames, shells, bridges, stadium bowls,
/// gridshells and parametric forms all go through the same stages, and what
/// comes out is groups described by what was measured. Primary and secondary
/// framing, chords and webs, bracing systems, diaphragm and shell zones, repeated
/// modules — those are what the groups tend to be, and saying which is which is left
/// to the engineer, downstream, in their own definition.
/// </para>
/// <para>
/// What the engine does take as given is what is true of every structure: gravity
/// points down, weight ends at the supports, and a line that carries straight on
/// through a joint is one member. From those it reads the model the way it is read by
/// eye — members first, then what is triangulated into one body, then from the
/// supports up, what rests on what — and only then asks the clustering which members
/// play the same part.
/// </para>
/// <list type="number">
/// <item><b>Input</b> — lines as start and end points, surfaces as boundary corners,
/// supports as points. Checked and refused with a reason, never repaired.</item>
/// <item><b>Graph construction</b> — points within the join distance weld into
/// joints, and a joint resting along an element joins it there. The elements become
/// the nodes of a graph, joined where they meet; the joints become the nodes of a
/// second, joined along the elements, to measure distances over.
/// See <see cref="StructureGraph"/>.</item>
/// <item><b>Members and assemblies</b> — lines that carry on through their joints
/// chained into the members they are pieces of, and members triangulated together in
/// one plane read as one body. See <see cref="PhysicalMembers"/> and <see cref="Assemblies"/>.</item>
/// <item><b>Load paths</b> — every element's weight drained to the supports, giving
/// how much passes along each element and how many hand-overs stand between each
/// assembly and the ground. See <see cref="LoadPaths"/>.</item>
/// <item><b>Feature extraction</b> — a row per member: length, uprightness,
/// straightness, what frames into it and what it frames into, flow, level, its place
/// in its assembly; and a row per element to hand on. See <see cref="InsightFeatures"/>.</item>
/// <item><b>Unsupervised learning</b> — four views of the same members from
/// <see cref="MultiViewClustering"/>: spectral clustering of the member graph,
/// cutting where connected members stop being alike; hierarchical clustering of
/// what members are like wherever they are; the same over what each member is like
/// and what it is attached to, which groups by role; HDBSCAN over everything, whose
/// outliers are the members that fit nowhere.</item>
/// <item><b>Fusion</b> — the views fused by <see cref="ConsensusClustering"/>: views
/// weighted by how far the others agree with them, a co-association between every
/// pair of members, and the count the views support over the widest range of
/// thresholds. Each element's agreement between views comes back with it.</item>
/// <item><b>Output</b> — the natural groups and their descriptions, the level of every
/// element and so the hierarchy of groups within levels, and the QA flags:
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
        var (starts, ends) = ModelInput.CheckLines(lineStarts, lineEnds);
        surfaces = ModelInput.CheckSurfaces(surfaces);
        if (supports is not null)
            ModelInput.CheckPoints(supports, nameof(supports));

        int lines = starts.GetLength(0);
        int n = lines + surfaces.Count;
        if (n < 3)
            throw new ArgumentException($"Need at least three elements to find groups in; got {n}.", nameof(lineStarts));

        options.Validate(n);

        // Graph construction.
        var structure = StructureGraph.Build(starts, ends, surfaces, supports, options.Join);
        var geometry = ElementGeometry.Measure(starts, ends, surfaces);
        double diagonal = Math.Max(Diagonal(structure.Joints), options.Tolerance);

        // Members and assemblies. Fewer than three members is too few to find groups
        // among, and then every element stands for itself, as it would have anyway.
        var members = PhysicalMembers.Read(structure, geometry, chain: !options.ElementsAsMembers);
        if (members.Count < 3)
            members = PhysicalMembers.Read(structure, geometry, chain: false);

        if (options.Groups is { } fixedGroups && fixedGroups > members.Count)
            throw new ArgumentException(
                $"The model reads as {members.Count} physical members, so it cannot be put into {fixedGroups} groups. "
                + $"Ask for {members.Count} or fewer, or read every element as its own member.", nameof(options));

        var assemblies = Assemblies.Read(structure, members);

        // Load paths.
        var paths = LoadPaths.Trace(structure, geometry, members, assemblies);

        // Feature extraction.
        var supportDistance = SupportDistances(structure);
        var centrality = Centrality.Betweenness(structure.Elements);
        var features = InsightFeatures.Raw(geometry, structure, supportDistance, centrality, members, assemblies, paths);
        var (affinity, hierarchy, density) = InsightFeatures.Views(features, diagonal, members, assemblies, paths);

        // Unsupervised learning, and fusion — of members, handed back to their elements.
        var clustering = MultiViewClustering.Fit(members.Graph, affinity, hierarchy, density, options.ToClustering());
        var labels = members.Of.Select(m => clustering.Labels[m]).ToArray();
        var agreement = members.Of.Select(m => clustering.Agreement[m]).ToArray();
        var level = members.Of.Select(m => paths.Level[assemblies.Of[m]]).ToArray();

        // Output.
        var (flags, issues, componentCount) = Inspect(structure, geometry, supportDistance, clustering, members, options, n);
        var groups = Describe(clustering, labels, agreement, level, structure, geometry, supportDistance, centrality, members, paths);

        var notes = new List<string>();
        if (!structure.HasSupports)
            notes.Add("No supports given, so support distances, load paths, levels and the no-path-to-support check were "
                + "skipped, and every unsupported base reads as a free end. Wire the supports in to read the model the way it stands.");
        if (!paths.Converged)
            notes.Add("The load-path flow stopped short of its tolerance, so Flow and Level are approximate. A model with "
                + "members many orders of magnitude apart in length is the usual cause.");
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
            Member = members.Of,
            MemberCount = members.Count,
            TurnLimit = members.TurnLimit,
            Assembly = members.Of.Select(m => assemblies.Of[m]).ToArray(),
            AssemblyCount = assemblies.Count,
            TriangulatedAssemblies = assemblies.Members.Count(a => a.Length > 1),
            Level = level,
            Levels = paths.Levels,
            Flow = paths.ElementFlow,
            Labels = labels,
            Agreement = agreement,
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

        var routes = Dijkstra.From(structure.Routes, sources);
        for (int e = 0; e < n; e++)
            distance[e] = structure.ElementJoints[e].Select(j => routes.Cost[j]).DefaultIfEmpty(double.PositiveInfinity).Min();

        return distance;
    }

    private static (InsightFlag[] Flags, List<InsightIssue> Issues, int Components) Inspect(
        StructureGraph structure, ElementGeometry geometry, double[] supportDistance,
        MultiViewClusteringResult clustering, PhysicalMembers members, StructuralInsightOptions options, int n)
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

        foreach (int m in clustering.Outliers())
            foreach (int e in members.Elements[m])
                Add(e, InsightFlag.Outlier, geometry.Centroid[e], members.Elements[m].Length > 1
                    ? $"element {e} is part of a member of {members.Elements[m].Length} elements that fits no dense group of alike members"
                    : $"element {e} fits no dense group of alike members");

        for (int e = 0; e < n; e++)
            if (clustering.Agreement[members.Of[e]] < options.LowAgreement)
                Add(e, InsightFlag.LowAgreement, geometry.Centroid[e],
                    $"the clustering views split on which members element {e}'s belongs with (agreement {clustering.Agreement[members.Of[e]]:0.00})");

        return (flags, issues, components);
    }

    private static IReadOnlyList<InsightGroup> Describe(
        MultiViewClusteringResult clustering, int[] labels, double[] agreement, int[] level,
        StructureGraph structure, ElementGeometry geometry, double[] supportDistance, double[] centrality,
        PhysicalMembers members, LoadPaths paths)
    {
        var grouped = clustering.Consensus.Members();
        var groups = new List<InsightGroup>(grouped.Length);

        for (int g = 0; g < grouped.Length; g++)
        {
            var elements = grouped[g].SelectMany(m => members.Elements[m]).OrderBy(e => e).ToArray();
            var reachable = elements.Select(e => supportDistance[e]).Where(double.IsFinite).ToArray();
            var placed = elements.Where(e => level[e] >= 0).ToArray();

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
                elements.Average(e => agreement[e]),
                grouped[g].Length,
                grouped[g].Average(m => members.Length[m]),
                placed.Length > 0 ? placed.Average(e => (double)level[e]) : double.NaN,
                elements.Average(e => paths.ElementFlow[e])));
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

}
