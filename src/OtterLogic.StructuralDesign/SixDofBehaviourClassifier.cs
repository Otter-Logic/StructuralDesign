using OtterLogic.MachineLearning.Decomposition;
using OtterLogic.MachineLearning.Preprocessing;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Groups structural members by how they behave, from the six-degree-of-freedom
/// demand on each one.
/// <para>
/// The end product this repo exists for. A caller supplies the numbers and
/// nothing else.
/// </para>
/// <para>
/// Deliberately thin. Fitting three models, scoring them and choosing between
/// them is <see cref="ClusterSelector"/>'s job, and none of that is structural —
/// a fabrication toolkit grouping panels wants exactly the same mechanism. What
/// is here is the part that is <em>only</em> true of six-degree-of-freedom
/// results out of an analysis, and would otherwise have to be rediscovered by
/// every user: that these columns are forces beside moments, that demand must
/// not be logged, how many directions such data really varies along, and what
/// the answer means once it comes back.
/// </para>
/// </summary>
public static class SixDofBehaviourClassifier
{
    /// <summary>
    /// Classifies n members described by their degree-of-freedom demands.
    /// </summary>
    /// <param name="demands">
    /// n x d, one row per member. Six columns is the intended case — Fx, Fy, Fz,
    /// Mx, My, Mz — but any consistent set of demand columns works.
    /// </param>
    /// <param name="options">Settings. The intended call passes none.</param>
    public static SixDofClassificationResult Classify(
        double[,] demands, SixDofClassificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(demands);
        options ??= new SixDofClassificationOptions();

        int n = demands.GetLength(0);
        int d = demands.GetLength(1);

        if (n < 4)
            throw new ArgumentException(
                $"Need at least four members to compare clusterings; got {n}.", nameof(demands));

        options.Validate(n);

        // Standardise every degree of freedom, then project.
        //
        // No log transform. These are signed or unsigned demands on an interval
        // scale where the gap between two values is what carries the meaning,
        // and a log would compress the large end and inflate the small one,
        // changing which members look alike for no physical reason.
        var pipeline = FeaturePipeline.Fit(demands, logTransform: false, normaliseRows: false, weights: null);
        var standardised = pipeline.Transform(demands);

        var pca = PrincipalComponents.FitCount(standardised, options.Components, whiten: true);
        var reduced = pca.Transform(standardised);

        // Everything from here is generic: fit all three, score them, choose.
        var selection = ClusterSelector.Select(reduced, options.Selection);

        // And back into the units the analysis produced, which is the step that
        // makes a group nameable.
        var centres = pipeline.InverseTransform(pca.InverseTransform(selection.GroupCentres()));

        return new SixDofClassificationResult(
            selection, centres, pca.ExplainedVarianceRatio, pipeline.KeptColumns, d);
    }
}
