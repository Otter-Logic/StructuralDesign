using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Clustering;

/// <summary>
/// Groups structural members by how they behave, from the six-degree-of-freedom
/// demand on each one.
/// <para>
/// The end product this repo exists for. It standardises the six degrees of
/// freedom, projects onto the handful of principal components the demand
/// actually varies along, fits k-means, a Gaussian mixture and HDBSCAN, and
/// chooses between them on the evidence. A caller supplies the numbers and
/// nothing else.
/// </para>
/// <para>
/// Every algorithm here belongs to <c>OtterLogic.MachineLearning</c> and is
/// callable directly. What this adds is the part that is specific to
/// six-degree-of-freedom structural results and would otherwise have to be
/// rediscovered by every user: which preprocessing suits demand data, what
/// counts as evidence for each model, and how to decide.
/// </para>
/// </summary>
public static class Clusterer
{
    /// <summary>
    /// Classifies n members described by their degree-of-freedom demands.
    /// </summary>
    /// <param name="demands">
    /// n x d, one row per member. Six columns is the intended case — Fx, Fy, Fz,
    /// Mx, My, Mz — but any consistent set of demand columns works.
    /// </param>
    /// <param name="options">Settings. The intended call passes none.</param>
    public static ClusteringResult Classify(double[,] demands, ClusteringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(demands);
        options ??= new ClusteringOptions();

        int n = demands.GetLength(0);
        int d = demands.GetLength(1);

        if (n < 4)
            throw new ArgumentException(
                $"Need at least four members to compare clusterings; got {n}.", nameof(demands));

        options.Validate(n);

        // Step 1 — standardise every degree of freedom, then project.
        //
        // No log transform. These are signed or unsigned demands on an interval
        // scale where the gap between two values is what carries the meaning,
        // and a log would compress the large end and inflate the small one,
        // changing which members look alike for no physical reason.
        var pipeline = FeaturePipeline.Fit(demands, logTransform: false, normaliseRows: false, weights: null);
        var standardised = pipeline.Transform(demands);

        var pca = PrincipalComponents.FitCount(standardised, options.Components, whiten: true);
        var reduced = pca.Transform(standardised);

        // Step 2 — run all three models.
        int maximumGroups = Math.Min(options.MaximumGroups, n - 1);
        int minimumGroups = Math.Min(options.MinimumGroups, maximumGroups);

        var kMeans = FitKMeans(reduced, minimumGroups, maximumGroups, options.Seed);
        var mixture = FitMixture(reduced, minimumGroups, maximumGroups, options.Seed);
        var density = FitHdbscan(reduced, options.MinimumClusterSize);

        var candidates = new[] { kMeans, mixture, density };

        // Steps 3 and 4 — compare, then choose.
        var (chosen, rationale) = Choose(options, kMeans, mixture, density);
        var winner = candidates.First(c => c.Model == chosen);

        var centres = GroupCentres(reduced, winner, pca, pipeline);

        return new ClusteringResult(
            chosen, rationale, winner, candidates,
            centres, reduced, pca.ExplainedVarianceRatio,
            pipeline.KeptColumns, d);
    }

    /// <summary>
    /// k-means across the group range, keeping the count with the best
    /// silhouette.
    /// <para>
    /// Silhouette rather than inertia, because inertia falls monotonically as
    /// groups are added and so cannot choose between counts. This is the one
    /// place a silhouette is the right referee: it is comparing k-means against
    /// itself, where its preference for round clusters is a constant.
    /// </para>
    /// </summary>
    private static ClusterCandidate FitKMeans(double[,] x, int minimum, int maximum, int seed)
    {
        int[]? bestLabels = null;
        double[,]? bestCentroids = null;
        double bestScore = double.NegativeInfinity;

        for (int k = minimum; k <= maximum; k++)
        {
            var fit = KMeans.Fit(x, new KMeansOptions { Clusters = k, Seed = seed });
            double score = ClusterQuality.Silhouette(x, fit.Labels);

            if (score > bestScore)
            {
                bestScore = score;
                bestLabels = fit.Labels;
                bestCentroids = fit.Centroids;
            }
        }

        var labels = bestLabels!;
        var confidence = CentroidMargin(x, labels, bestCentroids!);

        return new ClusterCandidate(
            ClusteringModel.KMeans,
            Groups(labels),
            labels,
            confidence,
            bestScore,
            ClusterQuality.DaviesBouldin(x, labels),
            0.0);
    }

    /// <summary>
    /// A Gaussian mixture across the group range, keeping the count with the
    /// lowest BIC.
    /// <para>
    /// BIC rather than silhouette, because the mixture has a likelihood and BIC
    /// uses it — and because scoring the mixture by a compactness measure would
    /// throw away exactly the elongated, overlapping solutions it exists to
    /// find. Every fit here is over the same transformed data, so the BIC values
    /// are comparable to each other.
    /// </para>
    /// <para>
    /// Full covariance, not diagonal. Whitening decorrelates the data as a
    /// whole, which says nothing about correlation inside a single behaviour
    /// family, and letting each component take its own orientation is the
    /// mixture's advantage over k-means. At three components that costs six
    /// parameters per group.
    /// </para>
    /// </summary>
    private static ClusterCandidate FitMixture(double[,] x, int minimum, int maximum, int seed)
    {
        GaussianMixtureResult? best = null;
        double bestBic = double.PositiveInfinity;

        for (int k = minimum; k <= maximum; k++)
        {
            var fit = GaussianMixture.Fit(x, new GaussianMixtureOptions
            {
                Components = k,
                Covariance = CovarianceType.Full,
                Seed = seed,
            });

            if (fit.Bic < bestBic)
            {
                bestBic = fit.Bic;
                best = fit;
            }
        }

        var mixture = best!;

        // Number the groups largest first, so a small change upstream does not
        // permute them and shuffle every colour downstream.
        int k2 = mixture.ComponentCount;
        var order = Enumerable.Range(0, k2)
            .OrderByDescending(c => mixture.MixingWeights[c])
            .ThenBy(c => c)
            .ToArray();

        var rank = new int[k2];
        for (int position = 0; position < k2; position++)
            rank[order[position]] = position;

        var raw = mixture.Labels();
        var labels = new int[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            labels[i] = rank[raw[i]];

        return new ClusterCandidate(
            ClusteringModel.GaussianMixture,
            Groups(labels),
            labels,
            mixture.Confidence(),
            ClusterQuality.Silhouette(x, labels),
            ClusterQuality.DaviesBouldin(x, labels),
            0.0);
    }

    /// <summary>
    /// HDBSCAN once. It is not swept, because it is not told how many groups to
    /// find — the count is something it reports.
    /// </summary>
    private static ClusterCandidate FitHdbscan(double[,] x, int? minimumClusterSize)
    {
        int n = x.GetLength(0);
        var fit = Hdbscan.Fit(x, new HdbscanOptions
        {
            MinimumClusterSize = minimumClusterSize ?? HdbscanOptions.DefaultMinimumClusterSize(n),
        });

        return new ClusterCandidate(
            ClusteringModel.Hdbscan,
            fit.ClusterCount,
            fit.Labels,
            fit.Probabilities,
            ClusterQuality.Silhouette(x, fit.Labels),
            ClusterQuality.DaviesBouldin(x, fit.Labels),
            fit.NoiseFraction);
    }

    /// <summary>
    /// Picks a model, testing each hypothesis with the measure that can answer
    /// it rather than scoring all three on one number.
    /// <para>
    /// This ordering is deliberate and the reason it is not a leaderboard.
    /// Silhouette and Davies-Bouldin both reward compact, round, well-separated
    /// clusters — which is what k-means optimises — so ranking the three on them
    /// would hand k-means the result almost regardless of the data. Measured on
    /// two interleaved crescents, where HDBSCAN recovers the truth exactly and
    /// k-means fails badly, the silhouette still prefers k-means by 0.49 to
    /// 0.33. A referee that agrees with one player is not a referee.
    /// </para>
    /// <para>
    /// So: messiness is tested by what only HDBSCAN can measure, overlap by what
    /// only the mixture can measure, and cleanliness is what is left when
    /// neither fires — which is also the only question a silhouette is
    /// trustworthy on.
    /// </para>
    /// </summary>
    private static (ClusteringModel Model, string Rationale) Choose(
        ClusteringOptions options,
        ClusterCandidate kMeans,
        ClusterCandidate mixture,
        ClusterCandidate density)
    {
        if (options.Model is { } forced)
            return (forced, "the model was specified rather than chosen.");

        // Messy: a real share of members belong to no dense region. Above the
        // ceiling HDBSCAN has not found outliers, it has found nothing, and its
        // own noise fraction is what says so.
        bool outliers = density.Groups >= 2
            && density.NoiseFraction >= options.MessyNoiseFloor
            && density.NoiseFraction <= options.MessyNoiseCeiling;

        if (outliers)
            return (ClusteringModel.Hdbscan,
                $"{density.NoiseFraction:P0} of members sit outside every dense group, so a model "
                + "that can leave a member unplaced describes this better than one that must file "
                + "everything.");

        // Irregular: no round partition of this data scores well at any count,
        // which is evidence about the shape of the behaviours rather than about
        // how many there are.
        bool irregular = density.Groups >= 2 && kMeans.Silhouette < options.CleanSilhouetteFloor;

        if (irregular)
            return (ClusteringModel.Hdbscan,
                $"no round partition scores well at any count (best silhouette {kMeans.Silhouette:0.00}), "
                + "so the behaviours are not the shape k-means and a mixture assume.");

        // Overlap: members sitting between two behaviours. Only the mixture
        // measures this, because only the mixture assigns softly — and it is the
        // share of boundary members that says so, not the average confidence,
        // which stays high even when families genuinely intermingle.
        if (mixture.AmbiguousFraction >= options.OverlapAmbiguousShare)
            return (ClusteringModel.GaussianMixture,
                $"{mixture.AmbiguousFraction:P0} of members sit between two behaviours rather than "
                + "inside one, so a soft assignment reports the structure where a hard split would "
                + "file them silently.");

        return (ClusteringModel.KMeans,
            $"behaviours are clean and well separated (silhouette {kMeans.Silhouette:0.00}, "
            + $"{mixture.AmbiguousFraction:P0} of members on a boundary), so the simplest model is "
            + "the honest one.");
    }

    /// <summary>
    /// Confidence for a hard partition: how much closer a member is to its own
    /// centre than to the next nearest, scaled to 0..1.
    /// <para>
    /// k-means has no probability to report, but it does know whether a member
    /// was a close call. Zero means the two nearest centres are equidistant.
    /// </para>
    /// </summary>
    private static double[] CentroidMargin(double[,] x, int[] labels, double[,] centroids)
    {
        int n = x.GetLength(0);
        int d = x.GetLength(1);
        int k = centroids.GetLength(0);

        var margin = new double[n];

        for (int i = 0; i < n; i++)
        {
            double own = double.MaxValue;
            double other = double.MaxValue;

            for (int c = 0; c < k; c++)
            {
                double distance = 0.0;
                for (int j = 0; j < d; j++)
                {
                    double delta = x[i, j] - centroids[c, j];
                    distance += delta * delta;
                }

                distance = Math.Sqrt(distance);

                if (c == labels[i])
                    own = distance;
                else if (distance < other)
                    other = distance;
            }

            margin[i] = other is double.MaxValue or 0.0 ? 1.0 : Math.Clamp((other - own) / other, 0.0, 1.0);
        }

        return margin;
    }

    /// <summary>
    /// Group centres in the original degrees of freedom, taken as the mean of
    /// each group in the reduced space and mapped back out.
    /// <para>
    /// Computed from the labels rather than from any one model's own centres, so
    /// all three report the same thing in the same way — HDBSCAN has no centroid
    /// of its own, and a mixture mean is not the mean of the members assigned to
    /// it.
    /// </para>
    /// </summary>
    private static double[,] GroupCentres(
        double[,] reduced, ClusterCandidate winner, PrincipalComponents pca, FeaturePipeline pipeline)
    {
        int n = reduced.GetLength(0);
        int width = reduced.GetLength(1);
        int groups = winner.Groups;

        var centres = new double[groups, width];
        var counts = new int[groups];

        for (int i = 0; i < n; i++)
        {
            int g = winner.Labels[i];
            if (g < 0)
                continue;

            counts[g]++;
            for (int j = 0; j < width; j++)
                centres[g, j] += reduced[i, j];
        }

        for (int g = 0; g < groups; g++)
        {
            if (counts[g] == 0)
                continue;

            for (int j = 0; j < width; j++)
                centres[g, j] /= counts[g];
        }

        return pipeline.InverseTransform(pca.InverseTransform(centres));
    }

    private static int Groups(int[] labels)
    {
        int highest = -1;
        foreach (int label in labels)
            if (label > highest)
                highest = label;

        return highest + 1;
    }
}
