using System.Globalization;
using System.Text;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Elements sorted into design groups: behaviour groups first, largest first,
/// then one group for each element that fits no behaviour group.
/// </summary>
public sealed class DesignGroupingResult
{
    private readonly string[] _notes;

    internal DesignGroupingResult(
        IReadOnlyList<DesignGroupingPart> parts, int[][] groups, int[] groupPart, int behaviourGroupCount,
        int elementCount, string[] notes)
    {
        Parts = parts;
        Groups = groups;
        GroupPart = groupPart;
        BehaviourGroupCount = behaviourGroupCount;
        _notes = notes;

        GroupOf = ClusterLabels.FromMembers(groups, elementCount);
    }

    /// <summary>
    /// The populations grouped separately, each with the classification its
    /// groups were read from. One part holding everything unless the grouping
    /// had a rule that keeps some elements apart.
    /// </summary>
    public IReadOnlyList<DesignGroupingPart> Parts { get; }

    /// <summary>
    /// Element indices per design group. Every element appears exactly once, and
    /// within a group elements keep their input order.
    /// </summary>
    public int[][] Groups { get; }

    /// <summary>Which of <see cref="Parts"/> each design group came from. A group never spans two.</summary>
    public int[] GroupPart { get; }

    /// <summary>Design group of each element, in input order. Never <c>-1</c>.</summary>
    public int[] GroupOf { get; }

    /// <summary>
    /// How many of <see cref="Groups"/> are behaviour groups. The rest, at the end,
    /// are one-off groups of a single element each.
    /// </summary>
    public int BehaviourGroupCount { get; }

    /// <summary>Elements that fit no behaviour group, each designed on its own.</summary>
    public int OneOffCount => Groups.Length - BehaviourGroupCount;

    /// <summary>Number of elements grouped.</summary>
    public int ElementCount => GroupOf.Length;

    /// <summary>
    /// What was grouped and how, headed by the design groups and followed by each
    /// part's classification report — so a design group can be traced back to the
    /// evidence it was drawn from.
    /// </summary>
    /// <param name="element">What an element is called in the report — "node", "bar".</param>
    public string Report(string element = "element")
    {
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        text.AppendLine($"Design groups {Groups.Length} "
            + $"({BehaviourGroupCount} behaviour group(s), {OneOffCount} one-off)");

        for (int g = 0; g < Groups.Length; g++)
        {
            string kind = g < BehaviourGroupCount ? "behaviour" : "one-off  ";
            string part = Parts[GroupPart[g]].Name is { } name ? $"  {name}" : string.Empty;
            text.AppendLine(string.Create(invariant,
                $"  {g,3}  {Groups[g].Length,5} {element}(s)  {kind}{part}"));
        }

        if (OneOffCount > 0)
            text.AppendLine($"One-off {element}(s) fit no behaviour group. Each is its own design group; "
                + "design them individually rather than with the nearest group.");

        foreach (string note in _notes)
            text.AppendLine(note);

        foreach (var part in Parts)
        {
            text.AppendLine();

            if (part.Name is not null)
                text.AppendLine($"--- {part.Elements.Length} {element}(s) {part.Name} ---");

            text.AppendLine(part.Classification is { } classification
                ? classification.Report()
                : $"Too few to classify — at least {SixDofBehaviourClassifier.MinimumMembers} are needed — "
                    + "so each is a one-off.");
        }

        return text.ToString().TrimEnd();
    }
}
