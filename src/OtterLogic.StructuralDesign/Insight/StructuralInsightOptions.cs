using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="StructuralInsightEngine"/>. Every one has a default and
/// none is about a type of structure — there is no setting for frames, shells or
/// bridges, because the engine treats them all the same way.
/// </summary>
public sealed record StructuralInsightOptions
{
    /// <summary>
    /// Points closer than this are the same point in the model. The document's
    /// absolute tolerance is the right value from Rhino.
    /// </summary>
    public double Tolerance { get; init; } = 0.001;

    /// <summary>
    /// Points closer than this are read as meant to meet, and joined. Null for ten
    /// times <see cref="Tolerance"/>. Anything joined from further apart than the
    /// tolerance is reported as a near miss, because the analysis will not make the
    /// same correction.
    /// </summary>
    public double? JoinDistance { get; init; }

    /// <summary>Fewest groups each view and the consensus consider.</summary>
    public int MinimumGroups { get; init; } = 2;

    /// <summary>Most groups each view and the consensus consider.</summary>
    public int MaximumGroups { get; init; } = 10;

    /// <summary>A fixed number of natural groups, instead of letting the views' agreement decide. Null decides.</summary>
    public int? Groups { get; init; }

    /// <summary>Groups smaller than this merge into the connected group their elements agree with most. One merges nothing.</summary>
    public int MinimumGroupSize { get; init; } = 1;

    /// <summary>
    /// Vote of the connectivity view: spectral clustering of the member graph,
    /// cutting where connected members stop being alike. Zero skips it.
    /// </summary>
    public double ConnectivityWeight { get; init; } = 1.0;

    /// <summary>
    /// Vote of the geometry view: hierarchical clustering of what each member is
    /// like, wherever it is — so repeated members group across the model. Zero
    /// skips it.
    /// </summary>
    public double GeometryWeight { get; init; } = 1.0;

    /// <summary>
    /// Vote of the density view: HDBSCAN over every feature, whose outliers are the
    /// QA findings. Zero skips it, and with it the outlier flags.
    /// </summary>
    public double DensityWeight { get; init; } = 1.0;

    /// <summary>
    /// Vote of the role view: hierarchical clustering of what each member is like
    /// <em>and what it is attached to</em>, out to <see cref="RoleHops"/> members away —
    /// so members playing the same part group wherever they are, a chord with every
    /// other chord because each has webs on one side and purlins on the other. Zero
    /// skips it.
    /// </summary>
    public double RoleWeight { get; init; } = 1.0;

    /// <summary>How many members away the role view looks when it reads what a member is attached to.</summary>
    public int RoleHops { get; init; } = 2;

    /// <summary>
    /// Read every line as its own member instead of chaining lines that carry straight
    /// on into one. Off by default; worth switching on when a model was drawn with
    /// deliberate breaks that ought to stay breaks.
    /// </summary>
    public bool ElementsAsMembers { get; init; }

    /// <summary>
    /// Scale each view's vote by how far the other views agree with it. On by default.
    /// <para>
    /// Worth switching off when the views are answering deliberately different
    /// questions. The connectivity view finds <em>where</em> — contiguous regions —
    /// and the geometry view finds <em>what</em> — kinds of element wherever they
    /// are — so on a model whose regions and kinds cut across each other the two
    /// agree at chance, and weighting by agreement hands the vote to whichever
    /// views happen to side together. Measured on a regular ten-by-ten-bay frame,
    /// the connectivity view kept 13% of the vote and the consensus was the
    /// geometry view's grouping exactly.
    /// </para>
    /// </summary>
    public bool WeightViewsByAgreement { get; init; } = true;

    /// <summary>Smallest dense group the density view accepts. Null derives it from the element count.</summary>
    public int? MinimumClusterSize { get; init; }

    /// <summary>
    /// Share of the model an element must hold on by itself to be flagged a weak
    /// connection — five per cent, and never fewer than two elements. Below that,
    /// every short spur's root would be flagged.
    /// </summary>
    public double WeakConnectionShare { get; init; } = 0.05;

    /// <summary>Agreement between views below which an element is flagged. The views split evenly at a half.</summary>
    public double LowAgreement { get; init; } = 0.6;

    /// <summary>Seed for the spectral view. Fixed, so a Grasshopper re-solve returns the same groups.</summary>
    public int Seed { get; init; } = 1;

    internal double Join => JoinDistance ?? 10.0 * Tolerance;

    internal void Validate(int elementCount)
    {
        if (!double.IsFinite(Tolerance) || Tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Tolerance), Tolerance, "Tolerance must be above zero.");
        if (JoinDistance is { } join && (!double.IsFinite(join) || join < Tolerance))
            throw new ArgumentOutOfRangeException(nameof(JoinDistance), join,
                $"The join distance must be at least the tolerance, {Tolerance}.");
        if (!double.IsFinite(WeakConnectionShare) || WeakConnectionShare <= 0.0 || WeakConnectionShare >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(WeakConnectionShare), WeakConnectionShare, "Must be between 0 and 1.");
        if (!double.IsFinite(LowAgreement) || LowAgreement < 0.0 || LowAgreement > 1.0)
            throw new ArgumentOutOfRangeException(nameof(LowAgreement), LowAgreement, "Must be between 0 and 1.");

        if (!double.IsFinite(RoleWeight) || RoleWeight < 0.0)
            throw new ArgumentOutOfRangeException(nameof(RoleWeight), RoleWeight, "A view's weight must be finite and not negative.");
        if (RoleHops < 1)
            throw new ArgumentOutOfRangeException(nameof(RoleHops), RoleHops, "The role view needs to look at least one member away.");

        ToClustering().Validate(elementCount);
    }

    internal MultiViewClusteringOptions ToClustering() => new()
    {
        MinimumGroups = MinimumGroups,
        MaximumGroups = MaximumGroups,
        MinimumClusterSize = MinimumClusterSize,
        SpectralWeight = ConnectivityWeight,
        HierarchicalWeight = GeometryWeight,
        DensityWeight = DensityWeight,
        ProfileWeight = RoleWeight,
        ProfileHops = RoleHops,
        Seed = Seed,
        Consensus = new ConsensusOptions
        {
            MinimumGroups = MinimumGroups,
            MaximumGroups = MaximumGroups,
            Groups = Groups,
            MinimumGroupSize = MinimumGroupSize,
            WeightByAgreement = WeightViewsByAgreement,
        },
    };
}
