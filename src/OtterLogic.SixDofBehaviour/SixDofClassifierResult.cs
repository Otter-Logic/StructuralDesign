using System.Globalization;
using System.Text;

namespace OtterLogic.SixDofBehaviour;

/// <summary>
/// The outcome of a classification: which model was chosen and why, where every
/// member ended up, and what each behaviour looks like in the units the analysis
/// produced.
/// </summary>
public sealed class SixDofClassifierResult
{
    internal SixDofClassifierResult(
        BehaviourModel chosen,
        string rationale,
        BehaviourCandidate winner,
        IReadOnlyList<BehaviourCandidate> candidates,
        double[,] centres,
        double[,] projection,
        double explainedVariance,
        int[] keptColumns,
        int inputColumnCount)
    {
        Chosen = chosen;
        Rationale = rationale;
        Winner = winner;
        Candidates = candidates;
        Centres = centres;
        Projection = projection;
        ExplainedVariance = explainedVariance;
        KeptColumns = keptColumns;
        InputColumnCount = inputColumnCount;
    }

    /// <summary>Which model the comparison chose.</summary>
    public BehaviourModel Chosen { get; }

    /// <summary>
    /// One sentence saying why, in the terms the choice was actually made on.
    /// Meant to be shown to the user, not logged.
    /// </summary>
    public string Rationale { get; }

    /// <summary>The chosen model's candidate, with its scores.</summary>
    public BehaviourCandidate Winner { get; }

    /// <summary>All three candidates, chosen or not, in model order.</summary>
    public IReadOnlyList<BehaviourCandidate> Candidates { get; }

    /// <summary>Behaviour group per member; <c>-1</c> means unassigned.</summary>
    public int[] Labels => Winner.Labels;

    /// <summary>Per-member confidence in its assignment.</summary>
    public double[] Confidence => Winner.Confidence;

    /// <summary>Number of behaviour groups found.</summary>
    public int Groups => Winner.Groups;

    /// <summary>
    /// Group centres, one row per group, mapped back through the whitening,
    /// the principal components and the standardisation into the original
    /// degrees of freedom.
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
    public double[,] Projection { get; }

    /// <summary>Fraction of the original variance the retained components carry.</summary>
    public double ExplainedVariance { get; }

    /// <summary>Input columns that survived the constant-column check.</summary>
    public int[] KeptColumns { get; }

    /// <summary>Number of columns supplied.</summary>
    public int InputColumnCount { get; }

    /// <summary>Number of members classified.</summary>
    public int MemberCount => Labels.Length;

    /// <summary>Members the chosen model declined to place. Only HDBSCAN can produce these.</summary>
    public int[] Unassigned()
        => Enumerable.Range(0, Labels.Length).Where(i => Labels[i] < 0).ToArray();

    /// <summary>Member indices bucketed by behaviour group, unassigned members excluded.</summary>
    public int[][] Members()
    {
        var buckets = new List<int>[Groups];
        for (int g = 0; g < Groups; g++)
            buckets[g] = new List<int>();

        for (int i = 0; i < Labels.Length; i++)
            if (Labels[i] >= 0)
                buckets[Labels[i]].Add(i);

        return buckets.Select(b => b.ToArray()).ToArray();
    }

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
        text.AppendLine(
            $"Degrees      {KeptColumns.Length} of {InputColumnCount} kept"
            + (KeptColumns.Length < InputColumnCount
                ? $" (dropped, no variation: {string.Join(", ", Enumerable.Range(0, InputColumnCount).Except(KeptColumns))})"
                : string.Empty));
        text.AppendLine(
            $"Components   {Projection.GetLength(1)} retained, "
            + $"{(ExplainedVariance * 100.0).ToString("0.0", invariant)}% of variance");
        text.AppendLine();
        text.AppendLine($"Chosen       {Name(Chosen)}");
        text.AppendLine($"Because      {Rationale}");
        text.AppendLine($"Groups       {Groups}");

        int unassigned = Unassigned().Length;
        if (unassigned > 0)
            text.AppendLine($"Unassigned   {unassigned} member(s) placed in no group");

        text.AppendLine();
        text.AppendLine("Model              Groups  Silhouette  Davies-Bouldin  Confidence  Boundary  Unplaced");

        foreach (var candidate in Candidates)
        {
            string db = double.IsNaN(candidate.DaviesBouldin)
                ? "     -"
                : candidate.DaviesBouldin.ToString("0.000", invariant).PadLeft(6);

            // Only the mixture's confidence is a probability, so only its
            // boundary share means anything. A k-means margin on the same scale
            // reads as though almost everything is borderline.
            string boundary = candidate.Model == BehaviourModel.GaussianMixture
                ? (candidate.AmbiguousFraction * 100.0).ToString("0.0", invariant) + "%"
                : "-";

            text.AppendLine(
                $"{(candidate.Model == Chosen ? "> " : "  ")}{Name(candidate.Model),-16} "
                + $"{candidate.Groups,6}  "
                + $"{candidate.Silhouette.ToString("0.000", invariant),10}  "
                + $"{db,14}  "
                + $"{candidate.MeanConfidence.ToString("0.000", invariant),10}  "
                + $"{boundary,8}  "
                + $"{(candidate.NoiseFraction * 100.0).ToString("0.0", invariant) + "%",8}");
        }

        text.AppendLine();
        text.AppendLine(
            "Confidence is each model's own measure and is not comparable between rows. Boundary is "
            + $"the share of members below {BehaviourCandidate.AmbiguousBelow:0.00} posterior "
            + "probability, which only the mixture has; unplaced is the share in no group at all.");

        return text.ToString().TrimEnd();
    }

    internal static string Name(BehaviourModel model) => model switch
    {
        BehaviourModel.KMeans => "K-Means",
        BehaviourModel.GaussianMixture => "Gaussian Mixture",
        BehaviourModel.Hdbscan => "HDBSCAN",
        _ => model.ToString(),
    };
}
