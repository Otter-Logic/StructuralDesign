using Xunit;

namespace OtterLogic.Clustering.Tests;

/// <summary>
/// The classifier's job is to pick the right model for the data in front of it,
/// so the tests build data where the right answer is known by construction and
/// check it picks that.
/// </summary>
public class ClustererTests
{
    private const int Seed = 20;

    [Fact]
    public void CleanWellSeparatedFamilies_ChooseKMeans()
    {
        var data = Families(
            spread: 0.6,
            outlierFraction: 0.0,
            centres: new[]
            {
                new[] { 400.0, 20, 15, 8, 90, 10 },
                new[] { 60.0, 140, 20, 95, 12, 8 },
                new[] { 30.0, 25, 180, 10, 15, 85 },
                new[] { 500.0, 200, 30, 100, 110, 20 },
            });

        var result = Clusterer.Classify(data);

        Assert.Equal(ClusteringModel.KMeans, result.Chosen);
        Assert.Equal(4, result.Groups);
        Assert.Empty(result.Unassigned());
    }

    [Fact]
    public void HeavilyOverlappingFamilies_ChooseGaussianMixture()
    {
        // Centres close together relative to their spread, so members genuinely
        // sit between behaviours rather than in one.
        var data = Families(
            spread: 26.0,
            outlierFraction: 0.0,
            centres: new[]
            {
                new[] { 100.0, 50, 40, 30, 25, 20 },
                new[] { 140.0, 62, 48, 38, 31, 25 },
                new[] { 120.0, 90, 44, 34, 55, 22 },
            });

        var result = Clusterer.Classify(data);

        Assert.Equal(ClusteringModel.GaussianMixture, result.Chosen);
        Assert.Empty(result.Unassigned());
    }

    [Fact]
    public void FamiliesWithGenuineOutliers_ChooseHdbscan()
    {
        var data = Families(
            spread: 1.2,
            outlierFraction: 0.18,
            centres: new[]
            {
                new[] { 400.0, 20, 15, 8, 90, 10 },
                new[] { 60.0, 140, 20, 95, 12, 8 },
                new[] { 30.0, 25, 180, 10, 15, 85 },
            });

        var result = Clusterer.Classify(data);

        Assert.Equal(ClusteringModel.Hdbscan, result.Chosen);
        Assert.NotEmpty(result.Unassigned());
    }

    [Fact]
    public void PlanarFrame_DropsTheZeroDegreesAndStillClassifies()
    {
        // A planar frame has no out-of-plane demand at all: three of the six
        // columns are identically zero and carry no information.
        var data = Families(
            spread: 0.6,
            outlierFraction: 0.0,
            centres: new[]
            {
                new[] { 400.0, 20, 0, 0, 90, 0 },
                new[] { 60.0, 140, 0, 0, 12, 0 },
                new[] { 30.0, 25, 0, 0, 15, 0 },
            });

        var result = Clusterer.Classify(data);

        Assert.Equal(3, result.KeptColumns.Length);
        Assert.Equal(6, result.InputColumnCount);
        Assert.True(result.Groups >= 2);
    }

    [Fact]
    public void Centres_ComeBackInTheOriginalUnits()
    {
        var centres = new[]
        {
            new[] { 400.0, 20, 15, 8, 90, 10 },
            new[] { 60.0, 140, 20, 95, 12, 8 },
        };

        var data = Families(spread: 0.5, outlierFraction: 0.0, centres: centres);
        var result = Clusterer.Classify(data);

        // Every reported centre should be close to one of the centres the data
        // was built around, in the units it arrived in.
        for (int g = 0; g < result.Groups; g++)
        {
            double best = centres.Min(c =>
            {
                double sum = 0.0;
                for (int j = 0; j < 6; j++)
                {
                    double delta = result.Centres[g, j] - c[j];
                    sum += delta * delta;
                }

                return Math.Sqrt(sum);
            });

            Assert.True(best < 15.0, $"Group {g} centre is {best:0.0} away from any true family centre.");
        }
    }

    [Fact]
    public void ForcingAModel_SkipsTheComparison()
    {
        var data = Families(
            spread: 0.6,
            outlierFraction: 0.0,
            centres: new[]
            {
                new[] { 400.0, 20, 15, 8, 90, 10 },
                new[] { 60.0, 140, 20, 95, 12, 8 },
            });

        var result = Clusterer.Classify(
            data, new ClusteringOptions { Model = ClusteringModel.Hdbscan });

        Assert.Equal(ClusteringModel.Hdbscan, result.Chosen);
        Assert.Equal(3, result.Candidates.Count);
    }

    [Fact]
    public void SameInputTwice_GivesTheSameAnswer()
    {
        var data = Families(
            spread: 4.0,
            outlierFraction: 0.05,
            centres: new[]
            {
                new[] { 400.0, 20, 15, 8, 90, 10 },
                new[] { 60.0, 140, 20, 95, 12, 8 },
                new[] { 30.0, 25, 180, 10, 15, 85 },
            });

        var first = Clusterer.Classify(data);
        var second = Clusterer.Classify(data);

        Assert.Equal(first.Chosen, second.Chosen);
        Assert.Equal(first.Labels, second.Labels);
    }

    [Fact]
    public void TooFewMembers_FailsWithSomethingReadable()
    {
        var data = new double[3, 6];
        var error = Assert.Throws<ArgumentException>(() => Clusterer.Classify(data));
        Assert.Contains("at least four members", error.Message);
    }

    /// <summary>
    /// Builds demand rows around the given family centres, with an optional
    /// share of members scattered far from all of them.
    /// </summary>
    private static double[,] Families(
        double spread, double outlierFraction, double[][] centres, int perFamily = 45)
    {
        var rng = new Random(Seed);
        var rows = new List<double[]>();

        foreach (var centre in centres)
        {
            for (int i = 0; i < perFamily; i++)
            {
                var row = new double[centre.Length];
                for (int j = 0; j < centre.Length; j++)
                {
                    // A zero column is a degree of freedom the structure has
                    // none of, and stays exactly zero.
                    row[j] = centre[j] == 0.0 ? 0.0 : Math.Max(0.0, centre[j] + Gauss(rng) * spread);
                }

                rows.Add(row);
            }
        }

        int outliers = (int)Math.Round(rows.Count * outlierFraction / (1.0 - outlierFraction));
        for (int i = 0; i < outliers; i++)
        {
            var row = new double[centres[0].Length];
            for (int j = 0; j < row.Length; j++)
                row[j] = centres[0][j] == 0.0 ? 0.0 : rng.NextDouble() * 600.0;

            rows.Add(row);
        }

        var data = new double[rows.Count, centres[0].Length];
        for (int i = 0; i < rows.Count; i++)
            for (int j = 0; j < data.GetLength(1); j++)
                data[i, j] = rows[i][j];

        return data;
    }

    private static double Gauss(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
