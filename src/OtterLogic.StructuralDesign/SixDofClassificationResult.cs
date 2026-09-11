using System.Globalization;
using System.Text;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// The outcome of a classification: which model was chosen and why, where every
/// member ended up, and what each behaviour looks like in the units the analysis
/// produced.
/// <para>
/// A thin structural reading of a <see cref="ClusterSelection"/>. The selection
/// speaks in samples and clusters, because that is all it knows; this speaks in
/// members and behaviours, and adds the two things only a structural caller can
/// supply — centres back in the original degrees of freedom, and which of those
/// degrees carried any information at all.
/// </para>
/// </summary>
public sealed class SixDofClassificationResult
{
    private readonly ClusterSelection _selection;

    internal SixDofClassificationResult(
        ClusterSelection selection,
        double[,] centres,
        double explainedVariance,
        int[] keptColumns,
        int inputColumnCount,
        string[]? columnNames = null)
    {
        _selection = selection;
        Centres = centres;
        ExplainedVariance = explainedVariance;
        KeptColumns = keptColumns;
        InputColumnCount = inputColumnCount;
        ColumnNames = columnNames;
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

    /// <summary>Behaviour group per member; <c>-1</c> means unassigned.</summary>
    public int[] Labels => _selection.Labels;

    /// <summary>Per-member confidence in its assignment.</summary>
    public double[] Confidence => _selection.Confidence;

    /// <summary>Number of behaviour groups found.</summary>
    public int Groups => _selection.Groups;

    /// <summary>Number of members classified.</summary>
    public int MemberCount => _selection.SampleCount;

    /// <summary>
    /// Group centres, one row per group, mapped back through the whitening, the
    /// principal components and the standardisation into the original degrees of
    /// freedom.
    /// <para>
    /// The output that makes the rest actionable. A behaviour nobody can name is
    /// a behaviour nobody will design for, and this is what lets somebody say
    /// "group two is the high-torsion family".
    /// </para>
    /// </summary>
    public double[,] Centres { get; }

    /// <summary>
    /// Every member in the reduced space the clustering ran in, one row each.
    /// Three columns by default, so it plots straight into Rhino as points.
    /// </summary>
    public double[,] Projection => _selection.Data;

    /// <summary>Fraction of the original variance the retained components carry.</summary>
    public double ExplainedVariance { get; }

    /// <summary>Input columns that survived the constant-column check.</summary>
    public int[] KeptColumns { get; }

    /// <summary>Number of columns supplied.</summary>
    public int InputColumnCount { get; }

    /// <summary>
    /// What each column of <see cref="Centres"/> is, when the caller said —
    /// "Fz min", "|My| max". Null when the columns are simply the degrees of
    /// freedom as supplied.
    /// </summary>
    public string[]? ColumnNames { get; }

    /// <summary>Members the chosen model declined to place. Only HDBSCAN can produce these.</summary>
    public int[] Unassigned() => _selection.Unassigned();

    /// <summary>Member indices bucketed by behaviour group, unassigned members excluded.</summary>
    public int[][] Members() => _selection.Members();

    /// <summary>
    /// A diagnostic block meant to be wired straight to a panel: what was
    /// chosen, why, and how all three models scored, so the choice can be
    /// second-guessed rather than taken on trust.
    /// </summary>
    public string Report()
    {
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        text.AppendLine($"Members      {MemberCount}");
        var dropped = Enumerable.Range(0, InputColumnCount).Except(KeptColumns)
            .Select(j => ColumnNames?[j] ?? j.ToString(invariant));
        text.AppendLine(
            $"{(ColumnNames is null ? "Degrees " : "Features")}     {KeptColumns.Length} of {InputColumnCount} kept"
            + (KeptColumns.Length < InputColumnCount
                ? $" (dropped, no variation: {string.Join(", ", dropped)})"
                : string.Empty));
        text.AppendLine(
            $"Components   {Projection.GetLength(1)} retained, "
            + $"{(ExplainedVariance * 100.0).ToString("0.0", invariant)}% of variance");
        text.AppendLine();
        text.AppendLine($"Chosen       {ClusterSelection.Name(Chosen)}");
        text.AppendLine($"Because      {Rationale}");
        text.AppendLine($"Groups       {Groups}");

        int unassigned = Unassigned().Length;
        if (unassigned > 0)
            text.AppendLine($"Unassigned   {unassigned} member(s) placed in no group");

        text.AppendLine();

        // How the three scored is a clustering question, not a structural one,
        // so the table is the selection's own.
        text.Append(_selection.ScoreTable());

        return text.ToString().TrimEnd();
    }
}
