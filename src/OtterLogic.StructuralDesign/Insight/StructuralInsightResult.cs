using System.Globalization;
using System.Text;
using OtterLogic.Core;
using OtterLogic.MachineLearning.Graphs;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// What the Structural Insight Engine read from a model: the natural groups and what
/// each is like, every view behind them, the features and graph they came from, and
/// everything worth a look.
/// <para>
/// Elements are numbered lines first, then surfaces, in the order given — every
/// per-element array here uses that numbering. The clustering itself runs on physical
/// members, and everything it found is handed back per element: every element of a
/// member shares its member's group, agreement and level.
/// </para>
/// </summary>
public sealed class StructuralInsightResult
{
    internal StructuralInsightResult()
    {
    }

    /// <summary>The settings the engine ran with.</summary>
    public StructuralInsightOptions Options { get; internal init; } = null!;

    /// <summary>Number of line elements, numbered first.</summary>
    public int LineCount { get; internal init; }

    /// <summary>Number of surface elements, numbered after the lines.</summary>
    public int SurfaceCount { get; internal init; }

    /// <summary>Number of elements.</summary>
    public int ElementCount => LineCount + SurfaceCount;

    /// <summary>
    /// Every clustering view, and their consensus — over the physical members, one
    /// sample each; <see cref="Member"/> says which sample an element is part of.
    /// </summary>
    public MultiViewClusteringResult Clustering { get; internal init; } = null!;

    /// <summary>
    /// The physical member each element is part of: lines that carry straight on
    /// through their joints are one member, however many pieces they were drawn in.
    /// Numbered by their lowest element.
    /// </summary>
    public int[] Member { get; internal init; } = null!;

    /// <summary>Number of physical members.</summary>
    public int MemberCount { get; internal init; }

    /// <summary>The turn, in degrees, up to which a line was read as carrying on into the next. NaN when nothing was chained.</summary>
    public double TurnLimit { get; internal init; }

    /// <summary>
    /// The assembly each element is part of: members triangulated together in one
    /// plane — a truss, a braced bay — are one assembly, and any other member is an
    /// assembly by itself. Numbered by their lowest member.
    /// </summary>
    public int[] Assembly { get; internal init; } = null!;

    /// <summary>Number of assemblies, those of a single member included.</summary>
    public int AssemblyCount { get; internal init; }

    /// <summary>Assemblies of more than one member.</summary>
    public int TriangulatedAssemblies { get; internal init; }

    /// <summary>
    /// Per element, how many hand-overs stand between its assembly and the ground: 0
    /// rests on the supports, 1 rests on something that does, and so on up. Assemblies
    /// that lean on each other share a level. -1 without supports, or with no route to one.
    /// </summary>
    public int[] Level { get; internal init; } = null!;

    /// <summary>The highest level found; -1 when no load path was traced.</summary>
    public int Levels { get; internal init; }

    /// <summary>Per element, the share of the whole model's weight passing along it on its way to the supports, 0 to 1.</summary>
    public double[] Flow { get; internal init; } = null!;

    /// <summary>Natural group per element, largest group first. Every element is placed.</summary>
    public int[] Labels { get; internal init; } = null!;

    /// <summary>Number of natural groups.</summary>
    public int Groups => Clustering.Groups;

    /// <summary>Per element, how far the views agreed about the members its own belongs with, 0 to 1.</summary>
    public double[] Agreement { get; internal init; } = null!;

    /// <summary>Each natural group, described by what was measured.</summary>
    public IReadOnlyList<InsightGroup> GroupSummaries { get; internal init; } = null!;

    /// <summary>Per element, its connectivity-view group, or -1 where the view skipped it or did not run.</summary>
    public int[] ConnectivityLabels => PerElement(Clustering.Spectral?.Labels);

    /// <summary>Per element, its geometry-view group, or -1 when the view did not run.</summary>
    public int[] GeometryLabels => PerElement(Clustering.HierarchicalLabels);

    /// <summary>Per element, its density-view group, or -1 for an outlier or when the view did not run.</summary>
    public int[] DensityLabels => PerElement(Clustering.Density?.Labels);

    /// <summary>Per element, its role-view group, or -1 when the view did not run.</summary>
    public int[] RoleLabels => PerElement(Clustering.ProfileLabels);

    /// <summary>
    /// The raw features, one row per element, in model units — see <see cref="FeatureNames"/>.
    /// Ready for a user's own clustering or training data.
    /// </summary>
    public double[,] Features { get; internal init; } = null!;

    /// <summary>What each column of <see cref="Features"/> is.</summary>
    public string[] FeatureNames { get; internal init; } = null!;

    /// <summary>The element graph: a node per element, an edge where two meet.</summary>
    public WeightedGraph Connectivity { get; internal init; } = null!;

    /// <summary>Joint positions after joining, one row of x, y, z per joint.</summary>
    public double[,] Joints { get; internal init; } = null!;

    /// <summary>Each element's joints, ascending.</summary>
    public int[][] ElementJoints { get; internal init; } = null!;

    /// <summary>
    /// Route length along the elements from each element to the nearest support.
    /// Positive infinity with no route; NaN when no supports were given.
    /// </summary>
    public double[] SupportDistance { get; internal init; } = null!;

    /// <summary>Whether any supports were given.</summary>
    public bool HasSupports { get; internal init; }

    /// <summary>Joints holding a support.</summary>
    public int SupportedJoints { get; internal init; }

    /// <summary>Supports at no joint, by their index among the supports given.</summary>
    public int[] StrandedSupports { get; internal init; } = null!;

    /// <summary>Everything worth a look at each element, combined.</summary>
    public InsightFlag[] Flags { get; internal init; } = null!;

    /// <summary>Every finding, one per flag per place, with where to look and why.</summary>
    public IReadOnlyList<InsightIssue> Issues { get; internal init; } = null!;

    /// <summary>Connected pieces of the element graph. One means the model holds together.</summary>
    public int ComponentCount { get; internal init; }

    /// <summary>How weakly the model holds together as a whole, 0 to 2 — close to zero is weak. NaN without the connectivity view.</summary>
    public double AlgebraicConnectivity => Clustering.AlgebraicConnectivity;

    /// <summary>Anything that changed what ran, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; internal init; } = null!;

    /// <summary>Element indices bucketed by natural group.</summary>
    public int[][] Members() => Enumerable.Range(0, Groups)
        .Select(g => Enumerable.Range(0, ElementCount).Where(e => Labels[e] == g).ToArray())
        .ToArray();

    /// <summary>
    /// The model as an engineer would schedule it: element indices bucketed by level,
    /// then by natural group within the level — what rests on the supports sorted into
    /// its kinds, then what rests on that, and so on up. Only the pairs that occur,
    /// level ascending and group ascending within it; level -1 first when there is one.
    /// </summary>
    public IReadOnlyList<(int Level, int Group, int[] Elements)> Hierarchy() => Enumerable.Range(0, ElementCount)
        .GroupBy(e => (Level: Level[e], Group: Labels[e]))
        .OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Group)
        .Select(g => (g.Key.Level, g.Key.Group, g.ToArray()))
        .ToList();

    /// <summary>What was read, how the views voted, what each group is like, and what is worth a look.</summary>
    public string Report()
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string G(double value) => double.IsNaN(value) ? "-" : value.ToString("G3", inv);

        text.AppendLine($"Elements     {ElementCount} ({LineCount} lines, {SurfaceCount} surfaces), {Joints.GetLength(0)} joints");
        text.AppendLine($"Joined       points within {Options.Join.ToString("G4", inv)} read as one joint");
        text.AppendLine(HasSupports
            ? $"Supports     {SupportedJoints} joint(s) supported"
              + (StrandedSupports.Length > 0 ? $", {StrandedSupports.Length} support(s) at no joint" : string.Empty)
            : "Supports     none given");
        text.AppendLine($"Members      {MemberCount} physical member(s)"
            + (double.IsNaN(TurnLimit) ? string.Empty : $", lines turning up to {G(TurnLimit)} degrees read as carrying on"));
        text.AppendLine($"Assemblies   {TriangulatedAssemblies} triangulated together, {AssemblyCount - TriangulatedAssemblies} member(s) standing alone");
        text.AppendLine(Levels >= 0
            ? $"Load path    {Levels + 1} level(s), level 0 resting on the supports"
            : "Load path    not traced");
        text.AppendLine($"Connected    {ComponentCount} piece(s)"
            + (double.IsNaN(AlgebraicConnectivity) ? string.Empty : $", algebraic connectivity {G(AlgebraicConnectivity)}"));
        text.AppendLine();

        var spectral = Clustering.Spectral;
        text.AppendLine(spectral is null
            ? "Connectivity skipped"
            : $"Connectivity {spectral.ClusterCount} groups, eigengap {G(spectral.EigenGap)}");
        text.AppendLine(Clustering.HierarchicalLabels is null
            ? "Geometry     skipped"
            : $"Geometry     {Clustering.HierarchicalLabels.Max() + 1} groups, silhouette {G(Clustering.HierarchicalSilhouette)}");
        text.AppendLine(Clustering.Density is null
            ? "Density      skipped"
            : $"Density      {Clustering.Density.ClusterCount} dense groups, {Clustering.Density.NoiseCount} outlier(s)");
        text.AppendLine(Clustering.ProfileLabels is null
            ? "Role         skipped"
            : $"Role         {Clustering.ProfileLabels.Max() + 1} groups");

        var consensus = Clustering.Consensus;
        text.AppendLine($"Consensus    {Groups} natural groups"
            + (consensus.MergedGroups > 0 ? $", {consensus.MergedGroups} small group(s) merged" : string.Empty));
        text.AppendLine("Weights      " + string.Join(", ",
            consensus.Views.Select((view, v) => $"{view.Name} {consensus.Weights[v].ToString("0.00", inv)}")));
        text.AppendLine("Agreement    " + string.Join(", ",
            consensus.Views.Select((view, v) => $"{view.Name} {consensus.ViewAgreement[v].ToString("0.00", inv)}"))
            + " (each view against the consensus, 1 is identical)");
        text.AppendLine();

        text.AppendLine("Group  Elements  Members  Lines  Surfaces  Member length  Extent x/y/z    Level  Flow    To support  Pieces  Agreement");
        foreach (var group in GroupSummaries)
        {
            string extent = $"{group.MeanExtentX:0.00}/{group.MeanExtentY:0.00}/{group.MeanExtentZ:0.00}";
            text.AppendLine(
                $"{group.Index,5}  {group.Elements.Length,8}  {group.Members,7}  {group.Lines,5}  {group.Surfaces,8}  "
                + $"{G(group.MeanMemberLength),13}  {extent,-14}  {G(group.MeanLevel),5}  {G(group.MeanFlow),-6}  "
                + $"{G(group.MeanSupportDistance),10}  {group.Pieces,6}  {group.MeanAgreement.ToString("0.00", inv),9}");
        }

        if (Levels >= 0)
        {
            text.AppendLine();
            text.AppendLine("Level  Group  Elements");
            foreach (var (level, group, elements) in Hierarchy())
                text.AppendLine($"{level,5}  {group,5}  {elements.Length,8}");
        }

        text.AppendLine();
        text.AppendLine($"Issues: {Issues.Count}");
        foreach (var kind in Issues.GroupBy(issue => issue.Flag).OrderBy(g => g.Key))
            text.AppendLine($"  {kind.Count(),5}  {Naming.Humanise(kind.Key).ToLowerInvariant()}");

        foreach (string note in Notes)
        {
            text.AppendLine();
            text.AppendLine(note);
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>A labelling of the members, handed out to their elements; -1 throughout when there is none.</summary>
    private int[] PerElement(int[]? perMember)
        => Member.Select(m => perMember is null ? -1 : perMember[m]).ToArray();
}
