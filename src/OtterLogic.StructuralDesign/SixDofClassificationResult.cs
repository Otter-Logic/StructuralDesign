using System.Globalization;
using System.Text;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// The outcome of a classification: which model was chosen and why, where every
/// element ended up, and what each group looks like in the units the data arrived
/// in.
/// <para>
/// A reading of a <see cref="ClusterSelection"/>. The selection speaks in samples
/// and clusters, because that is all it knows; this speaks in elements and groups,
/// and adds what only a caller holding the original columns can: each group's
/// range in every one of them, which columns carried any information at all, and
/// what became of the elements no group would take.
/// </para>
/// </summary>
public sealed class SixDofClassificationResult
{
    private readonly ClusterSelection _selection;

    internal SixDofClassificationResult(
        ClusterSelection selection,
        int[] labels,
        int groups,
        double[,] centres,
        double[,] minimum,
        double[,] maximum,
        double explainedVariance,
        int[] keptColumns,
        int inputColumnCount,
        string[]? columnNames,
        UnplacedPolicy unplaced)
    {
        _selection = selection;
        Labels = labels;
        Groups = groups;
        Centres = centres;
        Minimum = minimum;
        Maximum = maximum;
        ExplainedVariance = explainedVariance;
        KeptColumns = keptColumns;
        InputColumnCount = inputColumnCount;
        ColumnNames = columnNames;
        Unplaced = unplaced;
    }

    /// <summary>Which model the comparison chose.</summary>
    public ClusteringModel Chosen => _selection.Chosen;

    /// <summary>
    /// One sentence saying why, in the terms the choice was actually made on.
    /// Meant to be shown to the user, not logged.
    /// </summary>
    public string Rationale => _selection.Rationale;

    /// <summary>The chosen model's candidate, with its scores.</summary>
    public ClusterCandidate Winner => _selection.Winner;

    /// <summary>All three candidates, chosen or not, in model order.</summary>
    public IReadOnlyList<ClusterCandidate> Candidates => _selection.Candidates;

    /// <summary>
    /// Group per element, after <see cref="Unplaced"/> was applied: <c>-1</c> only
    /// when elements were left unassigned. Groups the model found come first,
    /// largest first; groups of one made for unassigned elements follow them.
    /// </summary>
    public int[] Labels { get; }

    /// <summary>
    /// Per-element confidence in its assignment, in the chosen model's own terms.
    /// Zero for an element the model left unassigned, whatever became of it after.
    /// </summary>
    public double[] Confidence => _selection.Confidence;

    /// <summary>Number of groups, counting any made for unassigned elements.</summary>
    public int Groups { get; }

    /// <summary>Number of elements classified.</summary>
    public int MemberCount => _selection.SampleCount;

    /// <summary>What was done with the elements the model left unassigned.</summary>
    public UnplacedPolicy Unplaced { get; }

    /// <summary>
    /// Elements the chosen model declined to place — only HDBSCAN produces these —
    /// whatever <see cref="Unplaced"/> then did with them.
    /// </summary>
    public int[] Unassigned() => _selection.Unassigned();

    /// <summary>Element indices bucketed by group; elements still unassigned are excluded.</summary>
    public int[][] Members() => ClusterLabels.Members(Labels, Groups);

    /// <summary>
    /// The mean of every column within every group, one row per group, in the
    /// units the data arrived in.
    /// <para>
    /// The output that makes the rest actionable. A group nobody can name is a
    /// group nobody will act on, and this is what lets somebody say "group two is
    /// the high-torsion family".
    /// </para>
    /// </summary>
    public double[,] Centres { get; }

    /// <summary>Smallest value of every column within every group, one row per group.</summary>
    public double[,] Minimum { get; }

    /// <summary>
    /// Largest value of every column within every group, one row per group — with
    /// <see cref="Minimum"/>, the envelope a group would be designed or checked for.
    /// </summary>
    public double[,] Maximum { get; }

    /// <summary>
    /// Every element in the reduced space the clustering ran in, one row each.
    /// Three columns by default, so it plots straight into Rhino as points.
    /// </summary>
    public double[,] Projection => _selection.Data;

    /// <summary>Fraction of the original variance the retained components carry.</summary>
    public double ExplainedVariance { get; }

    /// <summary>Input columns that survived the constant-column check.</summary>
    public int[] KeptColumns { get; }

    /// <summary>Number of columns supplied.</summary>
    public int InputColumnCount { get; }

    /// <summary>What each column is — "Fx" to "Mz" when the six lists were given. Null for a plain matrix.</summary>
    public string[]? ColumnNames { get; }

    /// <summary>
    /// Mean adjusted Rand index between the three models' groupings, between about
    /// 0 and 1.
    /// <para>
    /// Not what the choice was made on — see <see cref="ClusterSelector"/> — but a
    /// plain reading of how settled the answer is. Near one, three models with
    /// different ideas of a cluster drew much the same groups, and which was chosen
    /// matters little. Low, the grouping depends on which idea of a cluster you
    /// take, and the rationale is worth reading.
    /// </para>
    /// </summary>
    public double ModelAgreement
    {
        get
        {
            var candidates = _selection.Candidates;
            double total = 0.0;
            int pairs = 0;
            for (int a = 0; a < candidates.Count; a++)
                for (int b = a + 1; b < candidates.Count; b++, pairs++)
                    total += ClusterAgreement.AdjustedRand(candidates[a].Labels, candidates[b].Labels);

            return pairs == 0 ? 1.0 : total / pairs;
        }
    }

    /// <summary>
    /// A diagnostic block meant to be wired straight to a panel: what was
    /// chosen, why, how all three models scored, and each group's range, so the
    /// choice can be second-guessed rather than taken on trust.
    /// </summary>
    public string Report()
    {
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;
        string Column(int j) => ColumnNames?[j] ?? j.ToString(invariant);

        text.AppendLine($"Elements     {MemberCount}");
        var dropped = Enumerable.Range(0, InputColumnCount).Except(KeptColumns).Select(Column);
        text.AppendLine(
            $"Columns      {KeptColumns.Length} of {InputColumnCount} kept"
            + (KeptColumns.Length < InputColumnCount
                ? $" (dropped, no variation: {string.Join(", ", dropped)})"
                : string.Empty));
        text.AppendLine(
            $"Components   {Projection.GetLength(1)} retained, "
            + $"{(ExplainedVariance * 100.0).ToString("0.0", invariant)}% of variance");
        text.AppendLine();
        text.AppendLine($"Chosen       {ClusterSelection.Name(Chosen)}");
        text.AppendLine($"Because      {Rationale}");
        text.AppendLine($"Agreement    {ModelAgreement.ToString("0.00", invariant)} between the three models (1 is identical groups)");
        text.AppendLine($"Groups       {Groups}");

        int unassigned = Unassigned().Length;
        if (unassigned > 0)
            text.AppendLine(Unplaced switch
            {
                UnplacedPolicy.OwnGroup => $"Unassigned   {unassigned} element(s) fit no group, each given a group of its own at the end",
                UnplacedPolicy.Nearest => $"Unassigned   {unassigned} element(s) fit no group, each filed with the nearest",
                _ => $"Unassigned   {unassigned} element(s) placed in no group",
            });

        text.AppendLine();

        // How the three scored is a clustering question, so the table is the
        // selection's own.
        text.AppendLine(_selection.ScoreTable());
        text.AppendLine();

        var members = Members();
        text.AppendLine("Group  Size  " + string.Join("  ", Enumerable.Range(0, InputColumnCount).Select(j => $"{Column(j),-21}")));
        for (int g = 0; g < Groups; g++)
        {
            var ranges = Enumerable.Range(0, InputColumnCount).Select(j =>
                $"{Minimum[g, j].ToString("G4", invariant)} to {Maximum[g, j].ToString("G4", invariant)}".PadRight(21));
            text.AppendLine($"{g,5}  {members[g].Length,4}  {string.Join("  ", ranges)}");
        }

        return text.ToString().TrimEnd();
    }
}
