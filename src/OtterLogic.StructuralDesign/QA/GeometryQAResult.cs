using System.Globalization;
using System.Text;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// What kind of problem a <see cref="GeometryIssue"/> is. Listed roughly in the order
/// they stop an analysis: the first few leave the solver with a structure that is not
/// one piece or not held; the last few run but give answers nobody intended.
/// </summary>
public enum GeometryIssueKind
{
    /// <summary>A part of the model not connected to the main structure at all.</summary>
    SeparatePart,

    /// <summary>The main structure has no support among those given.</summary>
    Unsupported,

    /// <summary>A support at no node, holding nothing.</summary>
    StrandedSupport,

    /// <summary>Two points close in space but far apart along the structure — a connection meant and missed.</summary>
    NearMiss,

    /// <summary>An element end resting on another element with no node there, so the solver does not connect them.</summary>
    UnnodedBearing,

    /// <summary>Two lines crossing with no node, rarer in this model than crossings of its other kinds.</summary>
    UnjoinedCrossing,

    /// <summary>A region held to the rest by fewer elements than meet at its own typical node.</summary>
    WeaklyAttached,

    /// <summary>An element drawn twice between the same nodes.</summary>
    Duplicate,

    /// <summary>Two lines lying along each other for part of their length.</summary>
    Overlap,

    /// <summary>An element with no length, or a surface with fewer than three distinct corners.</summary>
    ZeroLength,

    /// <summary>An element on a smaller scale than the elements it meets.</summary>
    ShortElement,

    /// <summary>A line not quite at the height of the level it belongs to.</summary>
    OffLevel,

    /// <summary>A line not quite on the gridline it belongs to.</summary>
    OffGrid,
}

/// <summary>One thing worth a look.</summary>
/// <param name="Kind">What kind of problem.</param>
/// <param name="Elements">The elements involved — lines first, then surfaces, as they came in.</param>
/// <param name="Nodes">The nodes involved.</param>
/// <param name="Location">Where to look, as x, y, z.</param>
/// <param name="Measure">
/// The number behind it, in the unit the message says: the gap for a near miss, the
/// offset for an alignment, the elements across the cut for a weak attachment, the
/// scale factor for a short element.
/// </param>
/// <param name="Message">What was found, in words.</param>
public sealed record GeometryIssue(GeometryIssueKind Kind, int[] Elements, int[] Nodes, double[] Location, double Measure, string Message);

/// <summary>Every issue of one kind, and every element they involve.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Issues">Each finding of this kind.</param>
/// <param name="Elements">Every element any of them involves, each once, ascending — lines first, then surfaces.</param>
public sealed record GeometryIssueGroup(GeometryIssueKind Kind, GeometryIssue[] Issues, int[] Elements)
{
    /// <summary>The kind as a heading: "Near misses".</summary>
    public string Name => GeometryQAResult.Name(Kind);

    /// <summary>How many findings of this kind.</summary>
    public int Count => Issues.Length;
}

/// <summary>A model's first-pass health check: its nodes as a solver will see them, and what is worth a look.</summary>
public sealed class GeometryQAResult
{
    internal GeometryQAResult(
        double[,] nodes, int lineCount, int elementCount, int[] part, int partCount,
        IReadOnlyList<GeometryIssue> issues, IReadOnlyList<string> notes)
    {
        Nodes = nodes;
        LineCount = lineCount;
        ElementCount = elementCount;
        Part = part;
        PartCount = partCount;
        Issues = issues;
        Notes = notes;
    }

    /// <summary>m x 3: every node, joined at the document's tolerance and nothing more.</summary>
    public double[,] Nodes { get; }

    /// <summary>Number of lines; elements from here on are surfaces.</summary>
    public int LineCount { get; }

    /// <summary>Number of elements, lines and surfaces together.</summary>
    public int ElementCount { get; }

    /// <summary>Per node, which connected part it is in; 0 is the main structure, the largest.</summary>
    public int[] Part { get; }

    /// <summary>How many separate parts the model is in. One is a single structure.</summary>
    public int PartCount { get; }

    /// <summary>Everything worth a look, most serious kind first.</summary>
    public IReadOnlyList<GeometryIssue> Issues { get; }

    /// <summary>How the model was read, and anything that limited it, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Per element, how many issues involve it — for colouring the model.</summary>
    public int[] IssuesPerElement()
    {
        var counts = new int[ElementCount];
        foreach (var issue in Issues)
            foreach (int e in issue.Elements.Distinct())
                counts[e]++;
        return counts;
    }

    /// <summary>
    /// The issues as a model check lists them: one group per kind found, most serious
    /// first, each with every element it involves.
    /// <para>
    /// This is the view a user works from — "these 14 ends need a node, these 2 parts
    /// float" — where <see cref="Issues"/> is one entry per finding. An element in
    /// several issues of one kind is listed once in that kind's group.
    /// </para>
    /// </summary>
    public IReadOnlyList<GeometryIssueGroup> Groups()
        => Issues.GroupBy(i => i.Kind).OrderBy(g => g.Key)
            .Select(g => new GeometryIssueGroup(
                g.Key,
                g.ToArray(),
                g.SelectMany(i => i.Elements).Distinct().OrderBy(e => e).ToArray()))
            .ToList();

    /// <summary>
    /// Per element, the kinds of issue it is in, most serious first — "near miss;
    /// off level" — and empty for an element with none. The answer to "what is wrong
    /// with this member", element by element.
    /// </summary>
    public string[] IssueLabels()
    {
        var kinds = new SortedSet<GeometryIssueKind>[ElementCount];
        foreach (var issue in Issues)
            foreach (int e in issue.Elements)
                (kinds[e] ??= new SortedSet<GeometryIssueKind>()).Add(issue.Kind);

        return kinds.Select(set => set is null ? string.Empty : string.Join("; ", set.Select(Label))).ToArray();
    }

    /// <summary>Issue counts by kind, then every issue, grouped by kind.</summary>
    public string Summary(int perKind = 20)
    {
        var text = new StringBuilder();
        var byKind = Issues.GroupBy(i => i.Kind).OrderBy(g => g.Key).ToList();

        text.AppendLine(Issues.Count == 0
            ? $"Nothing found worth a look among {ElementCount} elements and {Nodes.GetLength(0)} nodes."
            : $"{Issues.Count} issue(s) among {ElementCount} elements and {Nodes.GetLength(0)} nodes, "
                + $"in {PartCount} connected part(s).");

        foreach (var group in byKind)
        {
            text.AppendLine();
            text.AppendLine($"{Name(group.Key)} ({group.Count()})");
            foreach (var issue in group.Take(perKind))
                text.AppendLine("  " + issue.Message);
            if (group.Count() > perKind)
                text.AppendLine($"  ... and {group.Count() - perKind} more.");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>A kind as a heading.</summary>
    public static string Name(GeometryIssueKind kind) => kind switch
    {
        GeometryIssueKind.SeparatePart => "Separate parts",
        GeometryIssueKind.Unsupported => "Unsupported",
        GeometryIssueKind.StrandedSupport => "Supports at no node",
        GeometryIssueKind.NearMiss => "Near misses",
        GeometryIssueKind.UnnodedBearing => "Ends bearing with no node",
        GeometryIssueKind.UnjoinedCrossing => "Unjoined crossings",
        GeometryIssueKind.WeaklyAttached => "Weakly attached",
        GeometryIssueKind.Duplicate => "Duplicates",
        GeometryIssueKind.Overlap => "Overlaps",
        GeometryIssueKind.ZeroLength => "No length",
        GeometryIssueKind.ShortElement => "Short elements",
        GeometryIssueKind.OffLevel => "Off level",
        GeometryIssueKind.OffGrid => "Off grid",
        _ => kind.ToString(),
    };

    /// <summary>A kind as a label on one element: "near miss", "off level".</summary>
    public static string Label(GeometryIssueKind kind) => kind switch
    {
        GeometryIssueKind.SeparatePart => "separate part",
        GeometryIssueKind.Unsupported => "unsupported",
        GeometryIssueKind.StrandedSupport => "support at no node",
        GeometryIssueKind.NearMiss => "near miss",
        GeometryIssueKind.UnnodedBearing => "end bearing with no node",
        GeometryIssueKind.UnjoinedCrossing => "unjoined crossing",
        GeometryIssueKind.WeaklyAttached => "weakly attached",
        GeometryIssueKind.Duplicate => "duplicate",
        GeometryIssueKind.Overlap => "overlap",
        GeometryIssueKind.ZeroLength => "no length",
        GeometryIssueKind.ShortElement => "short element",
        GeometryIssueKind.OffLevel => "off level",
        GeometryIssueKind.OffGrid => "off grid",
        _ => kind.ToString(),
    };

    internal static string Length(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
}
