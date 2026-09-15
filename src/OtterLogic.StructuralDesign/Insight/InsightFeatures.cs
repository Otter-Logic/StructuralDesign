using OtterLogic.MachineLearning.Preprocessing;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// One row of features per element, and which of them each clustering view reads.
/// <para>
/// This is where the engine's judgement lives, and it is a judgement about
/// structural models in general, never about one kind of structure. Every column is
/// something measurable on any stick or surface model — where an element is, how
/// big, which way it extends, how many others it meets, how far it is from the
/// ground it stands on along the elements, how much of the model's traffic passes
/// through it. None of them says what an element is, so none of them can be wrong
/// about a structure nobody anticipated.
/// </para>
/// </summary>
internal static class InsightFeatures
{
    public static readonly string[] Names =
    {
        "Centroid X", "Centroid Y", "Centroid Z", "Size", "Extent X", "Extent Y", "Extent Z",
        "Connections", "Support Distance", "Centrality", "Surface", "Aspect Ratio",
    };

    private const int Size = 3;
    private const int Connections = 7;
    private const int SupportDistance = 8;
    private const int Surface = 10;
    private const int Aspect = 11;

    /// <summary>
    /// What makes two <em>connected</em> elements belong together in the connectivity
    /// view: what each element is like on its own. Position is left out, because
    /// connected elements are already near each other, and so is anything read off
    /// the graph, because the graph is already what the view cuts.
    /// </summary>
    private static readonly int[] AffinityColumns = { Size, 4, 5, 6, Surface, Aspect };

    /// <summary>
    /// What the geometry view groups on: what each element is like and how many
    /// others it meets, and not where it is — so the same element recurring across
    /// the model forms one group, which is how repeated modules show up.
    /// </summary>
    private static readonly int[] HierarchyColumns = { Size, 4, 5, 6, Connections, Surface, Aspect };

    /// <summary>
    /// The raw features, in model units, ready to hand to a user's own pipeline.
    /// Support Distance is the route length along the elements to the nearest
    /// support, and -1 where there is no such route or no supports were given.
    /// </summary>
    public static double[,] Raw(ElementGeometry geometry, StructureGraph structure, double[] supportDistance, double[] centrality)
    {
        int n = structure.ElementCount;
        var features = new double[n, Names.Length];

        for (int e = 0; e < n; e++)
        {
            var centroid = geometry.Centroid[e];
            var extent = geometry.Extent[e];

            features[e, 0] = centroid.X;
            features[e, 1] = centroid.Y;
            features[e, 2] = centroid.Z;
            features[e, Size] = geometry.Size[e];
            features[e, 4] = extent.X;
            features[e, 5] = extent.Y;
            features[e, 6] = extent.Z;
            features[e, Connections] = structure.Elements.Neighbours(e).Length;
            features[e, SupportDistance] = double.IsFinite(supportDistance[e]) ? supportDistance[e] : -1.0;
            features[e, 9] = centrality[e];
            features[e, Surface] = structure.IsSurface(e) ? 1.0 : 0.0;
            features[e, Aspect] = geometry.Aspect[e];
        }

        return features;
    }

    /// <summary>
    /// The three views' matrices, each transformed and standardised.
    /// <para>
    /// Size and aspect are logged, because a structure mixes a one-metre purlin
    /// with a sixty-metre chord and what matters is the ratio, not the difference.
    /// Connections are logged for the same reason — a hub meeting thirty elements
    /// is not thirty times a joint meeting one. Support distance is scaled by the
    /// model's size, and an element with no route to a support reads as further than
    /// any that has one.
    /// </para>
    /// </summary>
    public static (double[,] Affinity, double[,] Hierarchy, double[,] Density) Views(double[,] raw, double diagonal)
    {
        int n = raw.GetLength(0);
        int d = raw.GetLength(1);
        var prepared = (double[,])raw.Clone();

        double furthest = 0.0;
        for (int e = 0; e < n; e++)
            if (raw[e, SupportDistance] >= 0.0)
                furthest = Math.Max(furthest, raw[e, SupportDistance] / diagonal);

        for (int e = 0; e < n; e++)
        {
            prepared[e, Size] = Math.Log(Math.Max(raw[e, Size], diagonal * 1e-6));
            prepared[e, Connections] = Math.Log(1.0 + raw[e, Connections]);
            prepared[e, SupportDistance] = raw[e, SupportDistance] >= 0.0
                ? raw[e, SupportDistance] / diagonal
                : 1.5 * Math.Max(furthest, 1.0);
            prepared[e, Aspect] = Math.Log(raw[e, Aspect]);
        }

        return (
            Standardised(prepared, AffinityColumns),
            Standardised(prepared, HierarchyColumns),
            Standardised(prepared, Enumerable.Range(0, d).ToArray()));
    }

    /// <summary>
    /// The chosen columns, standardised, constant ones dropped. When every one is
    /// constant — a model of identical elements — a single column of zeros: every
    /// element alike, which is the truth.
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
