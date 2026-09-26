using OtterLogic.StructuralEngine;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="SectionGrouping"/>. Every one has a default the grouping is
/// meant to run on; <see cref="Groups"/> is the one a user reaches for.
/// </summary>
public sealed record SectionGroupingOptions
{
    /// <summary>Points closer than this are the same point. The document's absolute tolerance is the right value from Rhino.</summary>
    public double Tolerance { get; init; } = 0.001;

    /// <summary>
    /// Points closer than this are read as meant to meet, and joined. Null for ten
    /// times <see cref="Tolerance"/>. Deliberately forgiving: grouping should read a
    /// model the way it was meant to be drawn. Finding where it was not is Geometry QA's job.
    /// </summary>
    public double? JoinDistance { get; init; }

    /// <summary>
    /// A fixed number of sections for the whole frame. Null groups each role family
    /// into the fewest sections that keep every group within <see cref="MaximumLengthRatio"/>
    /// and <see cref="MaximumFlowRatio"/>. Fewer than the families found asks for that
    /// many families, and none of them is split by size.
    /// </summary>
    public int? Groups { get; init; }

    /// <summary>
    /// The most a group's longest design length may be over its shortest before the
    /// group is split. A section is sized for the longest piece in its group, and
    /// bending grows with the square of the span, so 1.3 in length is about 1.7 in
    /// moment — two or three sizes in a serial range, which is where engineers
    /// usually start a new group. Ignored when <see cref="Groups"/> is set.
    /// </summary>
    public double MaximumLengthRatio { get; init; } = 1.3;

    /// <summary>
    /// The most a group's heaviest flow may be over its lightest before the group is
    /// split. Flow is the share of the frame's weight passing along a piece — a proxy
    /// for demand before any analysis, not a force — and a piece carrying twice what
    /// its neighbour does is usually a size or two up. Ignored when <see cref="Groups"/>
    /// is set, and when no supports were given.
    /// </summary>
    public double MaximumFlowRatio { get; init; } = 2.0;

    /// <summary>Most role families the views and the consensus consider.</summary>
    public int MaximumFamilies { get; init; } = 10;

    /// <summary>Families of fewer runs than this merge into the connected family their runs agree with most. One merges nothing.</summary>
    public int MinimumFamilySize { get; init; } = 1;

    /// <summary>
    /// Vote of the connectivity view: spectral clustering of the run graph, cutting
    /// where connected runs stop being alike. Off by default, because it finds
    /// regions and a section group is scattered by nature — every girder of a kind,
    /// wherever it stands. Worth a vote when groups should also be zones.
    /// </summary>
    public double ConnectivityWeight { get; init; }

    /// <summary>Vote of the geometry view: hierarchical clustering of what each run is like, wherever it is. Zero skips it.</summary>
    public double GeometryWeight { get; init; } = 1.0;

    /// <summary>Vote of the density view: HDBSCAN over every feature, whose outliers are runs that fit no family well. Zero skips it.</summary>
    public double DensityWeight { get; init; } = 1.0;

    /// <summary>
    /// Vote of the role view: hierarchical clustering of what each run is like and
    /// what it is attached to, out to <see cref="RoleHops"/> runs away — so a girder
    /// groups with girders because each has secondaries on it and columns under it.
    /// Zero skips it.
    /// </summary>
    public double RoleWeight { get; init; } = 1.0;

    /// <summary>How many runs away the role view looks when it reads what a run is attached to.</summary>
    public int RoleHops { get; init; } = 2;

    /// <summary>Scale each view's vote by how far the other views agree with it.</summary>
    public bool WeightViewsByAgreement { get; init; } = true;

    /// <summary>
    /// Read lines that carry straight on through a joint as one run. On by default;
    /// off reads every line as its own run, for a model drawn with deliberate breaks.
    /// Pieces are cut at bearings either way.
    /// </summary>
    public bool ChainMembers { get; init; } = true;

    /// <summary>Smallest dense family the density view accepts. Null derives it from the run count.</summary>
    public int? MinimumClusterSize { get; init; }

    /// <summary>Seed for the connectivity view. Fixed, so a Grasshopper re-solve returns the same groups.</summary>
    public int Seed { get; init; } = 1;

    internal double Join => JoinDistance ?? 10.0 * Tolerance;

    internal ModelReadingOptions ToReading(bool chain) => new()
    {
        Tolerance = Tolerance,
        JoinDistance = Join,
        ChainMembers = chain,
    };

    internal void Validate()
    {
        if (!double.IsFinite(Tolerance) || Tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Tolerance), Tolerance, "Tolerance must be above zero.");
        if (JoinDistance is { } join && (!double.IsFinite(join) || join < Tolerance))
            throw new ArgumentOutOfRangeException(nameof(JoinDistance), join, $"The join distance must be at least the tolerance, {Tolerance}.");
        if (Groups is { } groups && groups < 1)
            throw new ArgumentOutOfRangeException(nameof(Groups), groups, "Ask for at least one section, or leave Groups unset to let the frame decide.");
        if (!double.IsFinite(MaximumLengthRatio) || MaximumLengthRatio <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(MaximumLengthRatio), MaximumLengthRatio,
                "The length ratio must be above 1: a group's longest piece over its shortest.");
        if (!double.IsFinite(MaximumFlowRatio) || MaximumFlowRatio <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(MaximumFlowRatio), MaximumFlowRatio,
                "The flow ratio must be above 1: a group's heaviest piece over its lightest.");
        if (MaximumFamilies < 2)
            throw new ArgumentOutOfRangeException(nameof(MaximumFamilies), MaximumFamilies, "Allow at least two families.");
        if (!double.IsFinite(ConnectivityWeight) || ConnectivityWeight < 0.0)
            throw new ArgumentOutOfRangeException(nameof(ConnectivityWeight), ConnectivityWeight, "A view's weight must be finite and not negative.");
    }

    internal MultiViewClusteringOptions ToClustering(int? families) => new()
    {
        MinimumGroups = 2,
        MaximumGroups = MaximumFamilies,
        MinimumClusterSize = MinimumClusterSize,
        SpectralWeight = ConnectivityWeight,
        HierarchicalWeight = GeometryWeight,
        DensityWeight = DensityWeight,
        ProfileWeight = RoleWeight,
        ProfileHops = RoleHops,
        Seed = Seed,
        Consensus = new ConsensusOptions
        {
            MinimumGroups = Math.Min(2, families ?? 2),
            MaximumGroups = MaximumFamilies,
            Groups = families,
            MinimumGroupSize = MinimumFamilySize,
            WeightByAgreement = WeightViewsByAgreement,
        },
    };
}
