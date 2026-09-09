using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="SixDofBehaviourClassifier"/>. Every one has a default
/// tuned for six-degree-of-freedom results out of a structural analysis, and the
/// intended use is to pass none of them.
/// <para>
/// They exist so the behaviour is inspectable and testable, not because a user
/// is expected to reach for them. Anyone who wants to drive the individual
/// models is better served by the raw K-Means, Gaussian Mixture and HDBSCAN
/// components, which expose everything.
/// </para>
/// <para>
/// The split below is the same one the repos are arranged on:
/// <see cref="Components"/> is a claim about structural demand data, so it lives
/// here; <see cref="Selection"/> holds the thresholds that judge the shape of a
/// point cloud, which is not a structural question and lives in the layer that
/// owns the algorithms.
/// </para>
/// </summary>
public sealed record SixDofClassificationOptions
{
    /// <summary>
    /// Principal components to project onto before clustering.
    /// <para>
    /// Three, because demand across six degrees of freedom is strongly
    /// correlated — axial with major-axis moment, the two shears with their
    /// matching moments — so the members of a real structure lie close to a
    /// low-dimensional surface inside the six. Three keeps that surface and
    /// discards the rest, which is mostly noise, and it makes the clustering
    /// tractable and the result plottable.
    /// </para>
    /// <para>
    /// Clamped to the columns that survive the constant-column check, so a
    /// planar frame with three identically-zero degrees of freedom keeps what it
    /// has instead of failing.
    /// </para>
    /// </summary>
    public int Components { get; init; } = 3;

    /// <summary>
    /// How the three models are compared and chosen between. Defaulted; every
    /// setting on it is about cluster shape rather than about structures.
    /// </summary>
    public ClusterSelectorOptions Selection { get; init; } = new();

    internal void Validate(int memberCount)
    {
        if (Components < 1)
            throw new ArgumentOutOfRangeException(nameof(Components), Components,
                "Need at least one principal component.");

        Selection.Validate(memberCount);
    }
}
