namespace OtterLogic.SixDofBehaviour;

/// <summary>
/// One model's attempt at the data, with every score the choice was made on.
/// <para>
/// Kept and returned even for the models that lost, because the interesting
/// question when a result looks wrong is usually not "what did it pick" but
/// "what did the other two say".
/// </para>
/// </summary>
/// <param name="Model">Which model produced this.</param>
/// <param name="Groups">Behaviour groups found, noise excluded.</param>
/// <param name="Labels">Group index per member; <c>-1</c> is noise, from HDBSCAN only.</param>
/// <param name="Confidence">
/// Per-member confidence in this model's own terms — a posterior probability
/// for the mixture, a margin between the nearest two centres for k-means, a
/// density membership for HDBSCAN.
/// <para>
/// Comparable within a model, not between them. Only the mixture's is a
/// probability, and only the mixture's is used to decide anything: a k-means
/// margin is a ratio that is naturally small whenever two centres are close, so
/// reading it on the same scale as a posterior would say nine members in ten are
/// borderline on data where none are.
/// </para>
/// </param>
/// <param name="Silhouette">
/// Mean silhouette, -1 to 1, higher better. Measures compactness and
/// separation, so it favours round clusters and cannot be used alone to choose
/// between these three models.
/// </param>
/// <param name="DaviesBouldin">
/// Davies-Bouldin index, lower better, NaN when fewer than two groups. Driven by
/// the single most confusable pair rather than the average.
/// </param>
/// <param name="NoiseFraction">
/// Fraction of members the model declined to place. Zero for k-means and the
/// mixture by construction — only HDBSCAN can leave a member out.
/// </param>
public readonly record struct BehaviourCandidate(
    BehaviourModel Model,
    int Groups,
    int[] Labels,
    double[] Confidence,
    double Silhouette,
    double DaviesBouldin,
    double NoiseFraction)
{
    /// <summary>
    /// Confidence below which a member counts as sitting between two behaviours
    /// rather than inside one.
    /// </summary>
    public const double AmbiguousBelow = 0.75;

    /// <summary>Mean of <see cref="Confidence"/> over placed members.</summary>
    public double MeanConfidence
    {
        get
        {
            double total = 0.0;
            int counted = 0;

            for (int i = 0; i < Labels.Length; i++)
            {
                if (Labels[i] < 0)
                    continue;

                total += Confidence[i];
                counted++;
            }

            return counted == 0 ? 0.0 : total / counted;
        }
    }

    /// <summary>
    /// Share of placed members whose confidence falls below
    /// <see cref="AmbiguousBelow"/>.
    /// <para>
    /// This rather than <see cref="MeanConfidence"/> is what says whether
    /// behaviours overlap, and the difference is not a detail. Overlap is a
    /// property of the boundary, not of the bulk: even where two families
    /// genuinely intermingle, most members sit clearly inside one and the mean
    /// stays high. Worse, the mean is confounded with the number of groups — a
    /// mixture that settles on fewer, broader components is confident precisely
    /// because it is coarse. Measured on families overlapping heavily enough to
    /// be indistinguishable, the mean still read 0.87 to 0.98. The tail is where
    /// the signal is.
    /// </para>
    /// </summary>
    public double AmbiguousFraction
    {
        get
        {
            int ambiguous = 0;
            int counted = 0;

            for (int i = 0; i < Labels.Length; i++)
            {
                if (Labels[i] < 0)
                    continue;

                counted++;
                if (Confidence[i] < AmbiguousBelow)
                    ambiguous++;
            }

            return counted == 0 ? 0.0 : (double)ambiguous / counted;
        }
    }
}
