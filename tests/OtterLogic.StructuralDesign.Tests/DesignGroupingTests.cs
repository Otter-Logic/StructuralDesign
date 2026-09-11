using Xunit;

using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// A design grouping has one rule a behaviour grouping does not: every element
/// ends up in exactly one design group. These check that rule, and the ordering
/// the Grasshopper components rely on to keep colours from shuffling.
/// </summary>
public class DesignGroupingTests
{
    private static readonly double[][] ThreeFamilies =
    {
        new[] { 400.0, 20, 15, 8, 90, 10 },
        new[] { 60.0, 140, 20, 95, 12, 8 },
        new[] { 30.0, 25, 180, 10, 15, 85 },
    };

    /// <summary>
    /// With outliers present HDBSCAN is chosen and leaves some elements
    /// unassigned. Each must still land in a design group — one of its own, after
    /// the behaviour groups — and no element may appear twice.
    /// </summary>
    [Fact]
    public void EveryElementLandsInExactlyOneGroup_OutliersEachOnTheirOwn()
    {
        var demands = Demands.Families(spread: 1.2, outlierFraction: 0.18, ThreeFamilies);
        var result = DesignGrouping.Group(demands);

        Assert.Equal(ClusteringModel.Hdbscan, result.Parts[0].Classification!.Chosen);
        Assert.True(result.OneOffCount > 0, "the fixture should produce outliers");

        var all = result.Groups.SelectMany(g => g).OrderBy(i => i).ToArray();
        Assert.Equal(Enumerable.Range(0, result.ElementCount).ToArray(), all);

        var unassigned = result.Parts[0].Classification!.Unassigned();
        Assert.Equal(unassigned.Length, result.OneOffCount);

        for (int k = 0; k < result.OneOffCount; k++)
        {
            var group = result.Groups[result.BehaviourGroupCount + k];
            Assert.Equal(new[] { unassigned[k] }, group);
        }

        Assert.DoesNotContain(-1, result.GroupOf);
    }

    [Fact]
    public void GroupOfAgreesWithGroups()
    {
        var demands = Demands.Families(spread: 1.2, outlierFraction: 0.18, ThreeFamilies);
        var result = DesignGrouping.Group(demands);

        for (int g = 0; g < result.Groups.Length; g++)
            foreach (int element in result.Groups[g])
                Assert.Equal(g, result.GroupOf[element]);
    }

    /// <summary>
    /// Behaviour groups come largest first, whichever model was chosen — k-means
    /// included, which numbers its clusters by where its seeds landed.
    /// </summary>
    [Theory]
    [InlineData(ClusteringModel.KMeans)]
    [InlineData(ClusteringModel.GaussianMixture)]
    [InlineData(ClusteringModel.Hdbscan)]
    public void BehaviourGroupsComeLargestFirst(ClusteringModel model)
    {
        var demands = Demands.Families(spread: 1.2, outlierFraction: 0.1, ThreeFamilies, perFamily: new[] { 20, 45, 30 });
        var result = DesignGrouping.Group(demands, new SixDofClassificationOptions
        {
            Selection = new ClusterSelectorOptions { Model = model },
        });

        for (int g = 1; g < result.BehaviourGroupCount; g++)
            Assert.True(result.Groups[g].Length <= result.Groups[g - 1].Length,
                $"{model}: group {g} has {result.Groups[g].Length} elements, more than group {g - 1}'s "
                + $"{result.Groups[g - 1].Length}");
    }

    /// <summary>
    /// The classifier's own labels, not just the design groups, come largest first
    /// when k-means is the model — the promise the 6DOF component makes to its
    /// users, and one k-means' own numbering does not keep. Forced, so the test is
    /// about the numbering rather than about which model the data favours.
    /// <para>
    /// Every ordering of the family sizes, because k-means numbers a cluster by
    /// where its seed landed, and on any one arrangement it can come out largest
    /// first by luck — the first version of this test did, and passed without the
    /// fix it was written for.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(20, 45, 30)]
    [InlineData(20, 30, 45)]
    [InlineData(45, 20, 30)]
    [InlineData(45, 30, 20)]
    [InlineData(30, 20, 45)]
    [InlineData(30, 45, 20)]
    public void ClassifierLabelsComeLargestFirstUnderKMeans(int first, int second, int third)
    {
        var demands = Demands.Families(
            spread: 0.6, outlierFraction: 0.0, ThreeFamilies, perFamily: new[] { first, second, third });
        var result = SixDofBehaviourClassifier.Classify(demands, new SixDofClassificationOptions
        {
            Selection = new ClusterSelectorOptions { Model = ClusteringModel.KMeans },
        });

        Assert.Equal(ClusteringModel.KMeans, result.Chosen);

        var sizes = result.Members().Select(m => m.Length).ToArray();
        Assert.Equal(sizes.OrderByDescending(s => s).ToArray(), sizes);
    }

    [Fact]
    public void ReportNamesTheOneOffs()
    {
        var demands = Demands.Families(spread: 1.2, outlierFraction: 0.18, ThreeFamilies);
        var report = DesignGrouping.Group(demands).Report("node");

        Assert.Contains("one-off", report);
        Assert.Contains("node(s)", report);
        Assert.Contains("Chosen", report);
    }
}
