using OtterLogic.MachineLearning.Decomposition;
using OtterLogic.MachineLearning.Preprocessing;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Groups elements by how they behave, from six degrees of freedom of data on
/// each one — whatever those six values are.
/// <para>
/// Multipurpose by design. Member end forces, support reactions, connection
/// demands, nodal displacements: the classifier does not know which it has been
/// given, and makes no decision that depends on it. Everything that does — which
/// load combination governs, whether a force counts by its size or with its sign,
/// which elements may never share a group — is the user's to decide upstream, in
/// their own definition, before the six lists arrive. That is the point of it: a
/// tool that reads foundations one way and end plates another serves those two
/// jobs and no third, where this serves any job whose preparation somebody can
/// wire.
/// </para>
/// <para>
/// What it does own is what is true of any six-degree-of-freedom data and would
/// otherwise have to be rediscovered by every user: forces and moments sit side by
/// side with no shared scale, so every column is standardised; they are strongly
/// correlated, so the data is projected onto the few directions it varies along;
/// and a value's size carries meaning, so nothing is logged. Fitting k-means, a
/// Gaussian mixture and HDBSCAN and choosing between them is
/// <see cref="ClusterSelector"/>'s job one layer down.
/// </para>
/// </summary>
public static class SixDofBehaviourClassifier
{
    /// <summary>Fewest elements a classification can be run on.</summary>
    internal const int MinimumMembers = 4;

    /// <summary>
    /// Classifies elements from their six degrees of freedom as six named lists,
    /// one value per element in each.
    /// </summary>
    /// <exception cref="ArgumentException">The lists are not all the same length, or hold a value that is not finite.</exception>
    public static SixDofClassificationResult Classify(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, IReadOnlyList<double> fz,
        IReadOnlyList<double> mx, IReadOnlyList<double> my, IReadOnlyList<double> mz,
        SixDofClassificationOptions? options = null)
        => Classify(SixDof.Read(fx, fy, fz, mx, my, mz), options, SixDof.Names);

    /// <summary>
    /// Classifies n elements described by d values each.
    /// </summary>
    /// <param name="demands">
    /// n x d, one row per element. Six columns is the intended case — Fx, Fy, Fz,
    /// Mx, My, Mz — but any consistent set of columns works.
    /// </param>
    /// <param name="options">Settings. The intended call passes none.</param>
    public static SixDofClassificationResult Classify(double[,] demands, SixDofClassificationOptions? options = null)
        => Classify(demands, options, columnNames: null);

    private static SixDofClassificationResult Classify(
        double[,] demands, SixDofClassificationOptions? options, string[]? columnNames)
    {
        ArgumentNullException.ThrowIfNull(demands);
        options ??= new SixDofClassificationOptions();

        int n = demands.GetLength(0);
        int d = demands.GetLength(1);

        if (n < MinimumMembers)
            throw new ArgumentException(
                $"Need at least four members to compare clusterings; got {n}.", nameof(demands));

        options.Validate(n);

        // Standardise every column, then project.
        //
        // No log transform. These are signed or unsigned values on an interval
        // scale where the gap between two values is what carries the meaning,
        // and a log would compress the large end and inflate the small one,
        // changing which elements look alike for no physical reason.
        var pipeline = FeaturePipeline.Fit(demands, logTransform: false, normaliseRows: false, weights: null);
        var standardised = pipeline.Transform(demands);

        var pca = PrincipalComponents.FitCount(standardised, options.Components, whiten: true);
        var reduced = pca.Transform(standardised);

        // Everything from here is generic: fit all three, score them, choose.
        var selection = ClusterSelector.Select(reduced, options.Selection);

        // The unplaced are dealt with in the space the groups were found in, so
        // "nearest" means nearest in behaviour rather than in whichever raw column
        // happens to be largest.
        var labels = ClusterLabels.ResolveUnplaced(selection.Labels, reduced, options.Unplaced);
        int groups = labels.Length == 0 ? 0 : labels.Max() + 1;

        // Summaries from the raw rows rather than mapped back through the
        // projection: exact, in the units that arrived, and they include whatever
        // the dropped components carried.
        var (centres, minimum, maximum) = Summaries(demands, labels, groups);

        return new SixDofClassificationResult(
            selection, labels, groups, centres, minimum, maximum,
            pca.ExplainedVarianceRatio, pipeline.KeptColumns, d, columnNames, options.Unplaced);
    }

    /// <summary>Mean, smallest and largest value of every column within every group.</summary>
    private static (double[,] Centres, double[,] Minimum, double[,] Maximum) Summaries(double[,] data, int[] labels, int groups)
    {
        int d = data.GetLength(1);
        var centres = ClusterLabels.Means(data, labels, groups);
        var minimum = new double[groups, d];
        var maximum = new double[groups, d];

        for (int g = 0; g < groups; g++)
            for (int j = 0; j < d; j++)
                (minimum[g, j], maximum[g, j]) = (double.PositiveInfinity, double.NegativeInfinity);

        for (int i = 0; i < labels.Length; i++)
        {
            int g = labels[i];
            if (g < 0)
                continue;

            for (int j = 0; j < d; j++)
            {
                minimum[g, j] = Math.Min(minimum[g, j], data[i, j]);
                maximum[g, j] = Math.Max(maximum[g, j], data[i, j]);
            }
        }

        return (centres, minimum, maximum);
    }
}
