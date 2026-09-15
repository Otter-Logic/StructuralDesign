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
/// per-element array here uses that numbering.
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

    /// <summary>Every clustering view, and their consensus.</summary>
    public MultiViewClusteringResult Clustering { get; internal init; } = null!;

    /// <summary>Natural group per element, largest group first. Every element is placed.</summary>
    public int[] Labels => Clustering.Labels;

    /// <summary>Number of natural groups.</summary>
    public int Groups => Clustering.Groups;

    /// <summary>Per element, how far the views agreed about the elements it belongs with, 0 to 1.</summary>
    public double[] Agreement => Clustering.Agreement;

    /// <summary>Each natural group, described by what was measured.</summary>
    public IReadOnlyList<InsightGroup> GroupSummaries { get; internal init; } = null!;

    /// <summary>Per element, its connectivity-view group, or -1 where the view skipped it or did not run.</summary>
    public int[] ConnectivityLabels => Clustering.Spectral?.Labels ?? Unplaced();

    /// <summary>Per element, its geometry-view group, or -1 when the view did not run.</summary>
    public int[] GeometryLabels => Clustering.HierarchicalLabels ?? Unplaced();

    /// <summary>Per element, its density-view group, or -1 for an outlier or when the view did not run.</summary>
    public int[] DensityLabels => Clustering.Density?.Labels ?? Unplaced();

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
    public int[][] Members() => Clustering.Consensus.Members();

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

        var consensus = Clustering.Consensus;
        text.AppendLine($"Consensus    {Groups} natural groups"
            + (consensus.MergedGroups > 0 ? $", {consensus.MergedGroups} small group(s) merged" : string.Empty));
        text.AppendLine("Weights      " + string.Join(", ",
            consensus.Views.Select((view, v) => $"{view.Name} {consensus.Weights[v].ToString("0.00", inv)}")));
        text.AppendLine("Agreement    " + string.Join(", ",
            consensus.Views.Select((view, v) => $"{view.Name} {consensus.ViewAgreement[v].ToString("0.00", inv)}"))
            + " (each view against the consensus, 1 is identical)");
        text.AppendLine();

        text.AppendLine("Group  Elements  Lines  Surfaces  Mean size  Extent x/y/z    Connections  To support  Pieces  Agreement");
        foreach (var group in GroupSummaries)
        {
            string extent = $"{group.MeanExtentX:0.00}/{group.MeanExtentY:0.00}/{group.MeanExtentZ:0.00}";
            text.AppendLine(
                $"{group.Index,5}  {group.Elements.Length,8}  {group.Lines,5}  {group.Surfaces,8}  {G(group.MeanSize),9}  "
                + $"{extent,-14}  {G(group.MeanConnections),11}  {G(group.MeanSupportDistance),10}  {group.Pieces,6}  "
                + $"{group.MeanAgreement.ToString("0.00", inv),9}");
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

    private int[] Unplaced() => Enumerable.Repeat(-1, ElementCount).ToArray();
}
