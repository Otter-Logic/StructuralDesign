using Xunit;

using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// The classifier's job is to pick the right model for the data in front of it,
/// so the tests build data where the right answer is known by construction and
/// check it picks that.
/// <para>
/// These run over six-degree-of-freedom demand deliberately. The selection
/// mechanism is tested generically in the Unsupervised repo; what is tested here
/// is that the structural preprocessing in front of it produces data the
/// mechanism can read correctly.
/// </para>
/// </summary>
public class SixDofBehaviourClassifierTests
{
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

        var result = SixDofBehaviourClassifier.Classify(data);

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

        var result = SixDofBehaviourClassifier.Classify(data);

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

        var result = SixDofBehaviourClassifier.Classify(data);

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

        var result = SixDofBehaviourClassifier.Classify(data);

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
        var result = SixDofBehaviourClassifier.Classify(data);

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

        var result = SixDofBehaviourClassifier.Classify(
            data, new SixDofClassificationOptions
            {
                Selection = new ClusterSelectorOptions { Model = ClusteringModel.Hdbscan },
            });

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

        var first = SixDofBehaviourClassifier.Classify(data);
        var second = SixDofBehaviourClassifier.Classify(data);

        Assert.Equal(first.Chosen, second.Chosen);
        Assert.Equal(first.Labels, second.Labels);
    }

    [Fact]
    public void TooFewMembers_FailsWithSomethingReadable()
    {
        var data = new double[3, 6];
        var error = Assert.Throws<ArgumentException>(() => SixDofBehaviourClassifier.Classify(data));
        Assert.Contains("at least four members", error.Message);
    }

    [Fact]
    public void SixNamedLists_GiveTheSameAnswerAsTheMatrix()
    {
        var data = Families(spread: 0.6, outlierFraction: 0.0, centres: CleanCentres);
        var c = Demands.Columns(data);

        var fromLists = SixDofBehaviourClassifier.Classify(c[0], c[1], c[2], c[3], c[4], c[5]);
        var fromMatrix = SixDofBehaviourClassifier.Classify(data);

        Assert.Equal(fromMatrix.Labels, fromLists.Labels);
        Assert.Equal(new[] { "Fx", "Fy", "Fz", "Mx", "My", "Mz" }, fromLists.ColumnNames);
        Assert.Null(fromMatrix.ColumnNames);
    }

    [Fact]
    public void ListsOfDifferentLengths_FailWithSomethingReadable()
    {
        var six = Enumerable.Repeat(1.0, 6).ToArray();
        var five = Enumerable.Repeat(1.0, 5).ToArray();

        var error = Assert.Throws<ArgumentException>(() => SixDofBehaviourClassifier.Classify(six, six, five, six, six, six));
        Assert.Contains("Fz has 5", error.Message);
    }

    /// <summary>
    /// Every unassigned element gets a group of its own after the groups the model
    /// found, and nothing else moves.
    /// </summary>
    [Fact]
    public void OwnGroup_GivesEveryUnassignedElementItsOwnGroupAtTheEnd()
    {
        var data = Families(spread: 1.2, outlierFraction: 0.18, centres: OutlierCentres);

        var left = SixDofBehaviourClassifier.Classify(data);
        var own = SixDofBehaviourClassifier.Classify(data, new SixDofClassificationOptions { Unplaced = UnplacedPolicy.OwnGroup });

        var unassigned = left.Unassigned();
        Assert.NotEmpty(unassigned);
        Assert.Equal(left.Groups + unassigned.Length, own.Groups);
        Assert.All(own.Labels, label => Assert.True(label >= 0));
        Assert.All(unassigned, i => Assert.Single(own.Members()[own.Labels[i]]));
        for (int i = 0; i < data.GetLength(0); i++)
            if (left.Labels[i] >= 0)
                Assert.Equal(left.Labels[i], own.Labels[i]);
    }

    [Fact]
    public void Nearest_PlacesEveryElementInAnExistingGroup()
    {
        var data = Families(spread: 1.2, outlierFraction: 0.18, centres: OutlierCentres);

        var left = SixDofBehaviourClassifier.Classify(data);
        var nearest = SixDofBehaviourClassifier.Classify(data, new SixDofClassificationOptions { Unplaced = UnplacedPolicy.Nearest });

        Assert.Equal(left.Groups, nearest.Groups);
        Assert.All(nearest.Labels, label => Assert.InRange(label, 0, left.Groups - 1));
    }

    /// <summary>Every element sits inside its own group's envelope, in every column.</summary>
    [Fact]
    public void EveryElementLiesWithinItsGroupsRange()
    {
        var data = Families(spread: 4.0, outlierFraction: 0.0, centres: CleanCentres);
        var result = SixDofBehaviourClassifier.Classify(data);

        for (int i = 0; i < data.GetLength(0); i++)
        {
            int g = result.Labels[i];
            for (int j = 0; j < 6; j++)
                Assert.InRange(data[i, j], result.Minimum[g, j], result.Maximum[g, j]);
        }
    }

    [Fact]
    public void CleanFamilies_AllThreeModelsAgree()
    {
        var data = Families(spread: 0.6, outlierFraction: 0.0, centres: CleanCentres);
        var result = SixDofBehaviourClassifier.Classify(data);

        Assert.True(result.ModelAgreement > 0.9, $"agreement was {result.ModelAgreement:0.00}");
        Assert.Contains("Agreement", result.Report());
    }

    private static readonly double[][] CleanCentres =
    {
        new[] { 400.0, 20, 15, 8, 90, 10 },
        new[] { 60.0, 140, 20, 95, 12, 8 },
        new[] { 30.0, 25, 180, 10, 15, 85 },
        new[] { 500.0, 200, 30, 100, 110, 20 },
    };

    private static readonly double[][] OutlierCentres =
    {
        new[] { 400.0, 20, 15, 8, 90, 10 },
        new[] { 60.0, 140, 20, 95, 12, 8 },
        new[] { 30.0, 25, 180, 10, 15, 85 },
    };

    private static double[,] Families(
        double spread, double outlierFraction, double[][] centres, int perFamily = 45)
        => Demands.Families(spread, outlierFraction, centres, perFamily);
}
