namespace OtterLogic.StructuralDesign;

/// <summary>
/// What the Structural Insight Engine found worth a look at an element. Flags
/// combine: a stray line can be isolated, unsupported and an outlier at once.
/// <para>
/// Every flag is a fact about the geometry or the grouping, not a verdict. A free
/// end is a cantilever tip as often as a missed connection; a weak connection is
/// sometimes exactly the release the engineer intended. The flag says where to look.
/// </para>
/// </summary>
[Flags]
public enum InsightFlag
{
    /// <summary>Nothing to report.</summary>
    None = 0,

    /// <summary>Drawn between exactly the same joints as an earlier element.</summary>
    Duplicate = 1 << 0,

    /// <summary>A line whose ends meet, or a surface with no area.</summary>
    Degenerate = 1 << 1,

    /// <summary>Meets no other element at all.</summary>
    Isolated = 1 << 2,

    /// <summary>Part of a piece of the model not connected to its largest piece.</summary>
    Disconnected = 1 << 3,

    /// <summary>No route along the elements reaches a support. Only checked when supports are given.</summary>
    NoPathToSupport = 1 << 4,

    /// <summary>A line end nothing else meets and no support holds — a cantilever tip, or a connection that was missed.</summary>
    FreeEnd = 1 << 5,

    /// <summary>Removing this one element would cut a substantial part of the model off from the rest.</summary>
    WeakConnection = 1 << 6,

    /// <summary>Touches a joint its ends were welded into from further apart than the tolerance — joined here, maybe not in the model.</summary>
    NearMiss = 1 << 7,

    /// <summary>Fits no dense group of alike elements.</summary>
    Outlier = 1 << 8,

    /// <summary>The clustering views disagree about which elements this one belongs with.</summary>
    LowAgreement = 1 << 9,
}

/// <summary>One thing worth a look, at one element.</summary>
/// <param name="Element">The element, lines numbered first and surfaces after.</param>
/// <param name="Flag">What was found.</param>
/// <param name="X">Where to look: the joint concerned, or the element's centroid.</param>
/// <param name="Y">Where to look.</param>
/// <param name="Z">Where to look.</param>
/// <param name="Reason">What was found, in words.</param>
public sealed record InsightIssue(int Element, InsightFlag Flag, double X, double Y, double Z, string Reason);

/// <summary>
/// One natural group, described in the terms the engine measured — never named.
/// Whether it is a floor's secondary framing or a shell's stiff edge zone is the
/// engineer's reading.
/// </summary>
/// <param name="Index">The group number, largest group first.</param>
/// <param name="Elements">Its elements, ascending.</param>
/// <param name="Lines">How many are lines.</param>
/// <param name="Surfaces">How many are surfaces.</param>
/// <param name="MeanSize">Mean length, or square root of area, in model units.</param>
/// <param name="MeanExtentX">Mean share of each element along x.</param>
/// <param name="MeanExtentY">Mean share of each element along y.</param>
/// <param name="MeanExtentZ">Mean share of each element along z.</param>
/// <param name="MeanConnections">Mean number of other elements each meets.</param>
/// <param name="MeanSupportDistance">Mean route length along the elements to the nearest support; NaN when none is reachable or none was given.</param>
/// <param name="MeanCentrality">Mean betweenness — how much of the model's traffic passes through its elements.</param>
/// <param name="Pieces">
/// Connected pieces the group falls into. One is a contiguous zone; several is the
/// same kind of element recurring apart — a repeated module.
/// </param>
/// <param name="MeanAgreement">Mean agreement between the clustering views about its elements, 0 to 1.</param>
/// <param name="Members">Physical members the group holds — runs of elements that carry straight on, read as one.</param>
/// <param name="MeanMemberLength">Mean length of those members, in model units.</param>
/// <param name="MeanLevel">Mean hand-overs between its elements and the ground; NaN when no load path was traced.</param>
/// <param name="MeanFlow">Mean share of the model's weight passing along its elements, 0 to 1.</param>
public sealed record InsightGroup(
    int Index,
    int[] Elements,
    int Lines,
    int Surfaces,
    double MeanSize,
    double MeanExtentX,
    double MeanExtentY,
    double MeanExtentZ,
    double MeanConnections,
    double MeanSupportDistance,
    double MeanCentrality,
    int Pieces,
    double MeanAgreement,
    int Members,
    double MeanMemberLength,
    double MeanLevel,
    double MeanFlow);
