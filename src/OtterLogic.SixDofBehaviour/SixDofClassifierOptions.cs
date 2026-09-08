namespace OtterLogic.SixDofBehaviour;

/// <summary>
/// Settings for the classifier. Every one has a default tuned for
/// six-degree-of-freedom results out of a structural analysis, and the intended
/// use is to pass none of them.
/// <para>
/// They exist so the behaviour is inspectable and testable, not because a user
/// is expected to reach for them. Anyone who wants to drive the individual
/// models is better served by the raw K-Means, Gaussian Mixture and HDBSCAN
/// components, which expose everything.
/// </para>
/// </summary>
public sealed record SixDofClassifierOptions
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

    /// <summary>Lowest number of behaviour groups to consider.</summary>
    public int MinimumGroups { get; init; } = 2;

    /// <summary>
    /// Highest number of behaviour groups to consider.
    /// <para>
    /// Ten, because every group is a behaviour somebody has to interpret and act
    /// on. A structure that genuinely wants more than ten families is one where
    /// the grouping is not the useful abstraction.
    /// </para>
    /// </summary>
    public int MaximumGroups { get; init; } = 10;

    /// <summary>
    /// Smallest group HDBSCAN will call a behaviour. Null derives it from the
    /// member count — see <c>HdbscanOptions.DefaultMinimumClusterSize</c>.
    /// </summary>
    public int? MinimumClusterSize { get; init; }

    /// <summary>
    /// Forces a model instead of choosing one. Null runs the comparison, which
    /// is the point of the tool.
    /// </summary>
    public BehaviourModel? Model { get; init; }

    /// <summary>
    /// Seed for every model that starts randomly. Fixed, so a Grasshopper
    /// re-solve returns the same behaviours.
    /// </summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// Noise fraction at or above which the data is treated as messy enough to
    /// want HDBSCAN, provided it found groups at all.
    /// </summary>
    public double MessyNoiseFloor { get; init; } = 0.05;

    /// <summary>
    /// Noise fraction above which HDBSCAN is judged to have found nothing rather
    /// than to have found outliers, and is passed over.
    /// <para>
    /// A third is already generous. Genuine one-off members are a minority of
    /// any real structure; when a density model cannot place more than that, the
    /// honest reading is that the data has no density structure to find, not
    /// that most of the building is exceptional. Measured on families overlapping
    /// so heavily they are one diffuse cloud, HDBSCAN left 47 to 77 per cent
    /// unplaced — which is the failure this ceiling exists to catch.
    /// </para>
    /// </summary>
    public double MessyNoiseCeiling { get; init; } = 0.35;

    /// <summary>
    /// Silhouette below which no round partition of this data is any good, which
    /// is itself evidence the behaviours are not clean.
    /// </summary>
    public double CleanSilhouetteFloor { get; init; } = 0.25;

    /// <summary>
    /// Share of members sitting between two behaviours, above which the
    /// behaviours are judged to overlap and the mixture wins.
    /// <para>
    /// One member in ten being a genuine boundary case is enough to matter: a
    /// hard partition would file all of them silently, and the whole value of a
    /// mixture is that it says which ones they are. See
    /// <c>BehaviourCandidate.AmbiguousFraction</c> for why this is a tail
    /// measure rather than a mean.
    /// </para>
    /// </summary>
    public double OverlapAmbiguousShare { get; init; } = 0.10;

    internal void Validate(int sampleCount)
    {
        if (Components < 1)
            throw new ArgumentOutOfRangeException(nameof(Components), Components,
                "Need at least one principal component.");
        if (MinimumGroups < 2)
            throw new ArgumentOutOfRangeException(nameof(MinimumGroups), MinimumGroups,
                "Comparing partitions needs at least two groups.");
        if (MaximumGroups < MinimumGroups)
            throw new ArgumentOutOfRangeException(nameof(MaximumGroups), MaximumGroups,
                $"Maximum groups ({MaximumGroups}) is below minimum ({MinimumGroups}).");
        if (MinimumGroups > sampleCount)
            throw new ArgumentOutOfRangeException(nameof(MinimumGroups), MinimumGroups,
                $"Cannot ask for {MinimumGroups} groups from {sampleCount} members.");
    }
}
