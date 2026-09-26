using System.Globalization;
using System.Text;
using OtterLogic.StructuralEngine;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// One section group: pieces from one role family whose design length and flow sit
/// close enough to share a section. Described by what was measured, never named —
/// whether it is the ground-floor girders or the roof purlins is the engineer's reading.
/// </summary>
/// <param name="Index">The group number: families largest first, and within a family the longest pieces first.</param>
/// <param name="Family">The role family its pieces' runs fell into.</param>
/// <param name="Pieces">Its pieces, ascending.</param>
/// <param name="Lines">The lines given to it, ascending.</param>
/// <param name="DesignLength">
/// The longest design length among its pieces: the length the section has to cover.
/// Standing pieces count their longest unbraced stretch, lying ones their span, and a
/// raking piece something between, as the load path shares its load between axis and bending.
/// </param>
/// <param name="ShortestDesignLength">The shortest design length among its pieces.</param>
/// <param name="LongestSpan">The longest span, bearing to bearing.</param>
/// <param name="ShortestSpan">The shortest span.</param>
/// <param name="MostFlow">The heaviest share of the frame's weight any piece carries, 0 to 1.</param>
/// <param name="LeastFlow">The lightest.</param>
/// <param name="MeanUpright">How far its pieces stand up rather than lie, 0 to 1, on average.</param>
/// <param name="LowestLevel">The fewest hand-overs any of its pieces sits from the ground; -1 when none was traced.</param>
/// <param name="HighestLevel">The most.</param>
/// <param name="MeanConfidence">How firmly the role views agreed about its lines' families, 0 to 1, on average.</param>
public sealed record SectionGroup(
    int Index,
    int Family,
    int[] Pieces,
    int[] Lines,
    double DesignLength,
    double ShortestDesignLength,
    double LongestSpan,
    double ShortestSpan,
    double MostFlow,
    double LeastFlow,
    double MeanUpright,
    int LowestLevel,
    int HighestLevel,
    double MeanConfidence);

/// <summary>
/// What <see cref="SectionGrouping"/> found: a section group for every line, the
/// pieces behind them, and the reading they came from.
/// <para>
/// Lines are numbered in the order given. A line drawn across a cut — a beam line
/// drawn in one piece over a column, with no node there — takes the group of the
/// piece holding most of its length; its pieces are in <see cref="Pieces"/>.
/// </para>
/// </summary>
public sealed class SectionGroupingResult
{
    internal SectionGroupingResult()
    {
    }

    /// <summary>The settings it ran with.</summary>
    public SectionGroupingOptions Options { get; internal init; } = null!;

    /// <summary>Number of lines.</summary>
    public int LineCount { get; internal init; }

    /// <summary>The engine's whole reading of the frame: joints, runs, assemblies, load paths, pieces.</summary>
    public ModelReading Reading { get; internal init; } = null!;

    /// <summary>The pieces each run was cut into. The same object as the reading's.</summary>
    public Pieces Pieces => Reading.Pieces;

    /// <summary>Every role view and their consensus, over the runs.</summary>
    public MultiViewClusteringResult Clustering { get; internal init; } = null!;

    /// <summary>Per run, its role family, largest family first.</summary>
    public int[] RunFamily { get; internal init; } = null!;

    /// <summary>Number of role families.</summary>
    public int Families { get; internal init; }

    /// <summary>Per piece, the length it has to be designed over; see <see cref="SectionGroup.DesignLength"/>.</summary>
    public double[] DesignLength { get; internal init; } = null!;

    /// <summary>Per piece, its section group.</summary>
    public int[] PieceGroup { get; internal init; } = null!;

    /// <summary>Per line, its section group. Every line is placed.</summary>
    public int[] Group { get; internal init; } = null!;

    /// <summary>Per line, how firmly the role views agreed about its run's family, 0 to 1. Low is a line that could go either way.</summary>
    public double[] Confidence { get; internal init; } = null!;

    /// <summary>Each section group, described by what was measured.</summary>
    public IReadOnlyList<SectionGroup> Groups { get; internal init; } = null!;

    /// <summary>Number of section groups.</summary>
    public int GroupCount => Groups.Count;

    /// <summary>The number of sections asked for, or null when the limits decided.</summary>
    public int? FixedCount { get; internal init; }

    /// <summary>Runs the density view found in no dense family: worth checking by hand, whatever group they landed in.</summary>
    public int[] OutlierRuns { get; internal init; } = null!;

    /// <summary>Anything that changed what ran, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; internal init; } = null!;

    /// <summary>Line indices bucketed by section group.</summary>
    public int[][] Lines() => Groups.Select(g => g.Lines).ToArray();

    /// <summary>What was read, how the families were found, a schedule of the groups, and what was assumed.</summary>
    public string Report()
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string G(double value) => double.IsNaN(value) ? "-" : value.ToString("G3", inv);
        string Range(double low, double high) => Math.Abs(high - low) <= 1e-9 * Math.Max(1.0, Math.Abs(high)) ? G(high) : $"{G(low)}-{G(high)}";

        var structure = Reading.Structure;
        var members = Reading.Members;
        var paths = Reading.Paths;

        text.AppendLine($"Lines        {LineCount}, meeting at {structure.Joints.Length} joints; points within {Options.Join.ToString("G4", inv)} joined");
        text.AppendLine(structure.HasSupports
            ? $"Supports     {structure.Supported.Count(s => s)} joint(s) supported"
              + (structure.StrandedSupports.Length > 0 ? $", {structure.StrandedSupports.Length} at no joint" : string.Empty)
            : "Supports     none given");
        text.AppendLine($"Runs         {members.Count}"
            + (double.IsNaN(members.TurnLimit) ? string.Empty : $", lines turning up to {G(members.TurnLimit)} degrees read as carrying on"));
        text.AppendLine(Pieces.Traced
            ? $"Pieces       {Pieces.Count}, runs cut at {Pieces.Cuts} bearing(s) part of the way along"
            : $"Pieces       {Pieces.Count}, one per run: no load path traced to cut along");
        text.AppendLine(paths.Traced ? $"Load path    {paths.Levels + 1} level(s), level 0 resting on the supports" : "Load path    not traced");
        text.AppendLine();

        var consensus = Clustering.Consensus;
        text.AppendLine($"Families     {Families} role families of runs"
            + (consensus.MergedGroups > 0 ? $", {consensus.MergedGroups} small family(ies) merged" : string.Empty));
        text.AppendLine("Views        " + string.Join(", ",
            consensus.Views.Select((view, v) => $"{ViewName(view.Name)} {consensus.Weights[v].ToString("0.00", inv)}")));
        text.AppendLine(FixedCount is { } count
            ? $"Sections     {GroupCount}, {count} asked for"
            : $"Sections     {GroupCount}, the fewest keeping every group within {G(Options.MaximumLengthRatio)}x in design length"
              + (Pieces.Traced ? $", {G(Options.MaximumFlowRatio)}x in flow" : string.Empty) + ", with its pieces standing alike");
        text.AppendLine();

        text.AppendLine("Group  Family  Pieces  Lines  Design length  Span         Flow             Upright  Level  Confidence");
        foreach (var group in Groups)
        {
            string level = group.LowestLevel < 0 ? "-" : group.LowestLevel == group.HighestLevel
                ? group.LowestLevel.ToString(inv)
                : $"{group.LowestLevel}-{group.HighestLevel}";
            text.AppendLine(
                $"{group.Index,5}  {group.Family,6}  {group.Pieces.Length,6}  {group.Lines.Length,5}  "
                + $"{Range(group.ShortestDesignLength, group.DesignLength),-13}  {Range(group.ShortestSpan, group.LongestSpan),-11}  "
                + $"{Range(group.LeastFlow, group.MostFlow),-15}  {group.MeanUpright.ToString("0.00", inv),7}  {level,5}  "
                + $"{(double.IsNaN(group.MeanConfidence) ? "-" : group.MeanConfidence.ToString("0.00", inv)),10}");
        }

        if (OutlierRuns.Length > 0)
        {
            var lines = OutlierRuns.SelectMany(m => members.Elements[m]).Where(e => e < LineCount).OrderBy(e => e).ToArray();
            text.AppendLine();
            text.AppendLine($"{OutlierRuns.Length} run(s) fit no dense family and are worth checking by hand — lines "
                + string.Join(", ", lines.Take(20)) + (lines.Length > 20 ? ", ..." : string.Empty) + ".");
        }

        foreach (string note in Notes)
        {
            text.AppendLine();
            text.AppendLine(note);
        }

        text.AppendLine();
        text.AppendLine("Assumed:");
        foreach (string assumption in SectionGrouping.Assumptions)
            text.AppendLine("  - " + assumption);

        return text.ToString().TrimEnd();
    }

    /// <summary>The clustering's names for its views, in the words the settings use.</summary>
    private static string ViewName(string view) => view switch
    {
        "Spectral" => "connectivity",
        "Hierarchical" => "geometry",
        "Profile" => "role",
        "Density" => "density",
        _ => view.ToLowerInvariant(),
    };
}
