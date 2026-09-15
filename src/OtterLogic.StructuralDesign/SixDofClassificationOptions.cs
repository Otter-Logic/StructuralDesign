using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="SixDofBehaviourClassifier"/>. Every one has a default
/// tuned for six-degree-of-freedom data, and the intended use is to pass none of
/// them.
/// <para>
/// The split below is the same one the repos are arranged on:
/// <see cref="Components"/> is a claim about six-degree-of-freedom data, so it
/// lives here; <see cref="Selection"/> holds the thresholds that judge the shape of
/// a point cloud, which is not a structural question and lives in the layer that
/// owns the algorithms.
/// </para>
/// </summary>
public sealed record SixDofClassificationOptions
{
    /// <summary>
    /// Principal components to project onto before clustering.
    /// <para>
    /// Three, because six degrees of freedom out of a structure are strongly
    /// correlated — axial with major-axis moment, the two shears with their
    /// matching moments — so the elements of a real structure lie close to a
    /// low-dimensional surface inside the six. Three keeps that surface and
    /// discards the rest, which is mostly noise, and it makes the clustering
    /// tractable and the projection plottable as points.
    /// </para>
    /// <para>
    /// Clamped to the columns that survive the constant-column check, so a
    /// planar frame with three identically-zero degrees of freedom keeps what it
    /// has instead of failing.
    /// </para>
    /// </summary>
    public int Components { get; init; } = 3;

    /// <summary>
    /// What happens to an element HDBSCAN finds fits no group. Left unassigned by
    /// default — for reading a structure, that an element fits nowhere is the
    /// finding.
    /// <para>
    /// For acting on the groups, every element needs one. <see cref="UnplacedPolicy.OwnGroup"/>
    /// is the cautious choice: an element that fits no family is usually the
    /// unusual one, and filed with its nearest family it either drags that
    /// family's governing values up for every element in it or is itself
    /// under-represented. <see cref="UnplacedPolicy.Nearest"/> is for when being
    /// unusual is no reason to treat it apart.
    /// </para>
    /// </summary>
    public UnplacedPolicy Unplaced { get; init; } = UnplacedPolicy.Leave;

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
        if (!Enum.IsDefined(Unplaced))
            throw new ArgumentOutOfRangeException(nameof(Unplaced), Unplaced, "Not a policy for unassigned elements.");

        Selection.Validate(memberCount);
    }
}
