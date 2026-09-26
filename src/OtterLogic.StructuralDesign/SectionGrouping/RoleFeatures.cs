using OtterLogic.MachineLearning.Preprocessing;
using OtterLogic.StructuralEngine;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// One row per run, for the views that sort runs into role families, and which of
/// them each view reads.
/// <para>
/// A role here is how a run carries load, and nothing else: along its axis or across
/// it, how many hand-overs from the ground, whether it is part of a triangulated body
/// and where in it, and whether other members hand their load onto it part of the way
/// along. How long it is and how much it carries are left to the size step, so a
/// girder line of three bays and one of four are one family, and a light and a heavy
/// column are one family split into two sections by flow.
/// </para>
/// <para>
/// The Structural Insight Engine this came from also read run length, connection and
/// carried-member counts, centrality and support distance. On a six-storey frame of
/// identical 6 m bays those split the beams six ways — four-bay lines from three-bay
/// lines, floor beams landing partway up a column stack from roof beams landing on
/// its top — into groups with the same design length and the same flow: position,
/// not role. None of them is here. Nothing refers to the world's x and y either:
/// gravity is the only direction that means the same on every frame.
/// </para>
/// </summary>
internal static class RoleFeatures
{
    /// <summary>
    /// A joint counts as one the run receives load at when what it receives there is
    /// at least this share of the most it receives at any joint — the same measure
    /// the load path uses for what rests on what.
    /// </summary>
    private const double SubstantialShare = 0.1;

    /// <summary>The run rows' columns, in order.</summary>
    private enum Column
    {
        Upright, Straightness, Level, InBody, DepthPosition, AlongSpan, Receiving,
    }

    /// <summary>
    /// What makes two <em>connected</em> runs belong together in the connectivity view,
    /// which is off by default: how each stands, and its place in the load path.
    /// </summary>
    private static readonly Column[] AffinityColumns = { Column.Upright, Column.Straightness, Column.Level };

    /// <summary>The run rows, standardised: the same matrix feeds the connectivity, geometry, role and density views.</summary>
    public static (double[,] Affinity, double[,] Role, double[,] Density) Views(ModelReading reading)
    {
        var raw = reading.ElementFeatures();
        var members = reading.Members;
        var assemblies = reading.Assemblies;
        var paths = reading.Paths;

        int count = members.Count;
        int width = Enum.GetValues<Column>().Length;
        var rows = new double[count, width];

        const int size = 3;
        double levels = Math.Max(1, paths.Levels);
        var receiving = ReceivingShare(members, paths);

        for (int m = 0; m < count; m++)
        {
            var elements = members.Elements[m];
            double weight = elements.Sum(e => raw[e, size]);
            double upright = weight > 0.0
                ? elements.Sum(e => raw[e, ElementFeatures.ExtentZ] * raw[e, size]) / weight
                : elements.Average(e => raw[e, ElementFeatures.ExtentZ]);
            int level = paths.Level[assemblies.Of[m]];

            rows[m, (int)Column.Upright] = upright;
            rows[m, (int)Column.Straightness] = members.Straightness[m];
            rows[m, (int)Column.Level] = !paths.Traced ? 0.0 : level >= 0 ? level / levels : 1.5;
            rows[m, (int)Column.InBody] = assemblies.Members[assemblies.Of[m]].Length > 1 ? 1.0 : 0.0;
            rows[m, (int)Column.DepthPosition] = assemblies.DepthPosition[m];
            rows[m, (int)Column.AlongSpan] = assemblies.AlongSpan[m];
            rows[m, (int)Column.Receiving] = receiving[m];
        }

        var all = Enumerable.Range(0, width).ToArray();
        var role = Standardised(rows, all);
        return (Standardised(rows, AffinityColumns.Select(c => (int)c).ToArray()), role, role);
    }

    /// <summary>
    /// Per run, the share of the joints part of the way along it at which other members
    /// hand it their load. A column stack takes load at every floor, a girder at every
    /// secondary landing on it, a beam line cut at its columns at none — and none of
    /// it grows with how many bays or storeys the run happens to span.
    /// </summary>
    private static double[] ReceivingShare(PhysicalMembers members, LoadPaths paths)
    {
        var received = new Dictionary<(int Member, int Joint), double>();
        foreach (var h in paths.JointHandOvers)
            if (h.To >= 0)
                received[(h.To, h.Joint)] = received.GetValueOrDefault((h.To, h.Joint)) + h.Share;

        var most = new double[members.Count];
        foreach (var ((m, _), share) in received)
            most[m] = Math.Max(most[m], share);

        var shares = new double[members.Count];
        for (int m = 0; m < members.Count; m++)
        {
            var run = members.Run[m];
            var along = Enumerable.Range(0, run.Length)
                .Where(p => members.Closed[m] || (p > 0 && p < run.Length - 1))
                .Select(p => run[p])
                .ToArray();
            if (along.Length == 0 || most[m] <= 0.0)
                continue;

            shares[m] = along.Count(j => received.GetValueOrDefault((m, j)) >= SubstantialShare * most[m]) / (double)along.Length;
        }

        return shares;
    }

    /// <summary>
    /// The chosen columns, standardised, constant ones dropped. When every one is
    /// constant — a model of identical runs — a single column of zeros: every run
    /// alike, which is the truth.
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
