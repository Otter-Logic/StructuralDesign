using OtterLogic.MachineLearning.Preprocessing;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// One row of features per element to hand on, one row per member to cluster, and
/// which of them each clustering view reads.
/// <para>
/// This is where the engine's judgement lives, and it is a judgement about
/// structural models in general, never about one kind of structure. Every column is
/// something measurable on any stick or surface model, and none of them says what an
/// element is, so none of them can be wrong about a structure nobody anticipated.
/// </para>
/// <para>
/// What is clustered is the <b>member</b>, described the way an engineer sizes one up:
/// how long it is as a whole, how upright, how straight, what frames into it and what
/// it frames into, how much of the model's weight passes along it and how many
/// hand-overs from the ground it sits, and where it lies within the assembly it is part
/// of. Nothing clustered refers to the world's x and y, or to where in plan the member
/// is. Gravity is the only direction that means the same on every structure; on a bowl
/// or a fan the rafter at three o'clock and the one at six differ in nothing but
/// heading, and a feature that can tell them apart can only do harm.
/// </para>
/// </summary>
internal static class InsightFeatures
{
    /// <summary>
    /// The columns of the raw element features. The first twelve are as they have
    /// always been, in the same places, so a definition reading them by position still
    /// reads what it did; what the members, assemblies and load paths add comes after.
    /// </summary>
    public static readonly string[] Names =
    {
        "Centroid X", "Centroid Y", "Centroid Z", "Size", "Extent X", "Extent Y", "Extent Z",
        "Connections", "Support Distance", "Centrality", "Surface", "Aspect Ratio",
        "Member Length", "Member Straightness", "Member Connections", "Ends Bearing", "Members Carried",
        "Flow", "Level", "Assembly Members", "Depth Position", "Along Span",
    };

    private const int ExtentZ = 6;
    private const int SupportDistance = 8;
    private const int Centrality = 9;
    private const int Surface = 10;
    private const int Aspect = 11;

    /// <summary>The member rows' columns, in order.</summary>
    private enum Column
    {
        Length, Upright, Straightness, Connections, EndsBearing, Carried, Flow, Level,
        AssemblyMembers, DepthPosition, AlongSpan, Surface, Aspect, SupportDistance, Centrality,
    }

    /// <summary>
    /// What makes two <em>connected</em> members belong together in the connectivity
    /// view: what each is like on its own, and its place in the load path — so the view
    /// cuts between a chord and the purlin resting on it, alike or not. Nothing counted
    /// off the graph, because the graph is already what the view cuts.
    /// </summary>
    private static readonly Column[] AffinityColumns =
    {
        Column.Length, Column.Upright, Column.Straightness, Column.Flow, Column.Level, Column.Surface, Column.Aspect,
    };

    /// <summary>
    /// What the geometry and role views group on: everything that says what part a
    /// member plays, and nothing that says where it is — so the same member recurring
    /// across the model forms one group, which is how repeated modules show up.
    /// </summary>
    private static readonly Column[] HierarchyColumns =
    {
        Column.Length, Column.Upright, Column.Straightness, Column.Connections, Column.EndsBearing, Column.Carried,
        Column.Flow, Column.Level, Column.AssemblyMembers, Column.DepthPosition, Column.AlongSpan, Column.Surface, Column.Aspect,
    };

    /// <summary>
    /// The raw features, per element, in model units, ready to hand to a user's own
    /// pipeline. Support Distance is the route length along the elements to the nearest
    /// support, and -1 where there is no such route or no supports were given; Level is
    /// -1 likewise. The member and assembly columns repeat down every element of the member.
    /// </summary>
    public static double[,] Raw(
        ElementGeometry geometry, StructureGraph structure, double[] supportDistance, double[] centrality,
        PhysicalMembers members, Assemblies assemblies, LoadPaths paths)
    {
        int n = structure.ElementCount;
        var features = new double[n, Names.Length];

        for (int e = 0; e < n; e++)
        {
            var centroid = geometry.Centroid[e];
            var extent = geometry.Extent[e];
            int member = members.Of[e];
            int assembly = assemblies.Of[member];

            features[e, 0] = centroid.X;
            features[e, 1] = centroid.Y;
            features[e, 2] = centroid.Z;
            features[e, 3] = geometry.Size[e];
            features[e, 4] = extent.X;
            features[e, 5] = extent.Y;
            features[e, ExtentZ] = extent.Z;
            features[e, 7] = structure.Elements.Neighbours(e).Length;
            features[e, SupportDistance] = double.IsFinite(supportDistance[e]) ? supportDistance[e] : -1.0;
            features[e, Centrality] = centrality[e];
            features[e, Surface] = structure.IsSurface(e) ? 1.0 : 0.0;
            features[e, Aspect] = geometry.Aspect[e];
            features[e, 12] = members.Length[member];
            features[e, 13] = members.Straightness[member];
            features[e, 14] = members.Meets[member];
            features[e, 15] = members.EndsBearing[member];
            features[e, 16] = members.Carried[member];
            features[e, 17] = paths.ElementFlow[e];
            features[e, 18] = paths.Level[assembly];
            features[e, 19] = assemblies.Members[assembly].Length;
            features[e, 20] = assemblies.DepthPosition[member];
            features[e, 21] = assemblies.AlongSpan[member];
        }

        return features;
    }

    /// <summary>
    /// The views' matrices, one row per member, each transformed and standardised.
    /// <para>
    /// Lengths, counts and flow are logged, because a structure mixes a one-metre
    /// purlin with a sixty-metre chord, a joint of two with a hub of thirty, a column
    /// carrying a third of the model with a brace carrying a thousandth, and what
    /// matters in each is the ratio, not the difference. Support distance is scaled by
    /// the model's size and level by the number of levels, and a member with no route
    /// to a support reads as further and higher than any that has one.
    /// </para>
    /// </summary>
    public static (double[,] Affinity, double[,] Hierarchy, double[,] Density) Views(
        double[,] raw, double diagonal, PhysicalMembers members, Assemblies assemblies, LoadPaths paths)
    {
        int count = members.Count;
        int width = Enum.GetValues<Column>().Length;
        var rows = new double[count, width];

        double furthest = 0.0;
        for (int e = 0; e < raw.GetLength(0); e++)
            if (raw[e, SupportDistance] >= 0.0)
                furthest = Math.Max(furthest, raw[e, SupportDistance] / diagonal);

        double levels = Math.Max(1, paths.Levels);

        for (int m = 0; m < count; m++)
        {
            var elements = members.Elements[m];
            double weight = elements.Sum(e => raw[e, 3]);
            double Mean(int column) => weight > 0.0
                ? elements.Sum(e => raw[e, column] * raw[e, 3]) / weight
                : elements.Average(e => raw[e, column]);

            var reachable = elements.Where(e => raw[e, SupportDistance] >= 0.0).Select(e => raw[e, SupportDistance]).ToArray();
            int level = paths.Level[assemblies.Of[m]];

            rows[m, (int)Column.Length] = Math.Log(Math.Max(members.Length[m], diagonal * 1e-6));
            rows[m, (int)Column.Upright] = Mean(ExtentZ);
            rows[m, (int)Column.Straightness] = members.Straightness[m];
            rows[m, (int)Column.Connections] = Math.Log(1.0 + members.Meets[m]);
            rows[m, (int)Column.EndsBearing] = members.EndsBearing[m];
            rows[m, (int)Column.Carried] = Math.Log(1.0 + members.Carried[m]);
            rows[m, (int)Column.Flow] = Math.Log(1e-4 + paths.MemberFlow[m]);
            rows[m, (int)Column.Level] = !paths.Traced ? 0.0 : level >= 0 ? level / levels : 1.5;
            rows[m, (int)Column.AssemblyMembers] = Math.Log(assemblies.Members[assemblies.Of[m]].Length);
            rows[m, (int)Column.DepthPosition] = assemblies.DepthPosition[m];
            rows[m, (int)Column.AlongSpan] = assemblies.AlongSpan[m];
            rows[m, (int)Column.Surface] = raw[elements[0], Surface];
            rows[m, (int)Column.Aspect] = Math.Log(Mean(Aspect));
            rows[m, (int)Column.SupportDistance] = reachable.Length > 0 ? reachable.Min() / diagonal : 1.5 * Math.Max(furthest, 1.0);
            rows[m, (int)Column.Centrality] = Mean(Centrality);
        }

        return (
            Standardised(rows, AffinityColumns.Select(c => (int)c).ToArray()),
            Standardised(rows, HierarchyColumns.Select(c => (int)c).ToArray()),
            Standardised(rows, Enumerable.Range(0, width).ToArray()));
    }

    /// <summary>
    /// The chosen columns, standardised, constant ones dropped. When every one is
    /// constant — a model of identical members — a single column of zeros: every
    /// member alike, which is the truth.
    /// </summary>
    private static double[,] Standardised(double[,] data, int[] columns)
    {
        int n = data.GetLength(0);
        var subset = new double[n, columns.Length];
        for (int e = 0; e < n; e++)
            for (int c = 0; c < columns.Length; c++)
                subset[e, c] = data[e, columns[c]];

        try
        {
            return FeaturePipeline.Fit(subset).Transform(subset);
        }
        catch (InvalidOperationException)
        {
            return new double[n, 1];
        }
    }
}
