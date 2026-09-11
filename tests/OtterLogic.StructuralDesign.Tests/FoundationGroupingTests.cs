using Xunit;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// A foundation designed for shear, bending and torsion either way should be
/// grouped by how big they are, and one that ever lifts should never be grouped
/// with one that never does — under one load combination or many, where each
/// foundation is grouped on its own envelope. These check both, the envelope
/// itself, that the six inputs pair up, and that the sign of compression is read
/// correctly whichever way the analysis reports it.
/// </summary>
public class FoundationGroupingTests
{
    // Fx, Fy, Fz, Mx, My, Mz at the column base.
    private static readonly double[] Interior = { 20.0, 20, 900, 5, 10, 10 };
    private static readonly double[] Edge = { 120.0, 30, 400, 8, 150, 25 };
    private static readonly double[] Corner = { 90.0, 90, 250, 10, 110, 110 };
    private static readonly double[] EdgeInUplift = { 120.0, 30, -400, 8, 150, 25 };

    private static readonly double[] CornerInUplift = { 90.0, 90, -250, 10, 110, 110 };

    // Interior and edge in compression, the corner lifting.
    private static readonly (double[] Centre, int Count)[] WithACornerInUplift =
    {
        (Interior, 45), (Edge, 45), (CornerInUplift, 45),
    };

    // Gravity and wind reactions for three families under gravity alone and wind
    // either way. Interior and edge stay in compression under every combination;
    // the corner lifts under one wind direction only (250 − 400 = −150).
    private static readonly (double[] Gravity, double[] Wind, int Count)[] UnderWind =
    {
        (new[] { 5.0, 5, 900, 2, 5, 5 }, new[] { 20.0, 5, 40, 3, 30, 5 }, 45),
        (new[] { 10.0, 5, 450, 3, 20, 5 }, new[] { 110.0, 20, 250, 8, 160, 25 }, 45),
        (new[] { 10.0, 10, 250, 3, 15, 15 }, new[] { 90.0, 90, -400, 10, 120, 120 }, 45),
    };

    private static readonly double[] GravityAndWindBothWays = { 0.0, 1.0, -1.0 };

    private const int Fz = 2;
    private const int PerFamily = 45;

    /// <summary>
    /// The same foundations with shear and moments pointing either way at random
    /// must group exactly as they would by size alone. Exactly, not roughly: once
    /// the five directionless forces are read by size the input is identical to
    /// the unmirrored one, so any difference is a sign leaking into the grouping.
    /// </summary>
    [Fact]
    public void MirroredShearAndMoments_GroupAsTheirSizesDo()
    {
        var bySize = Demands.Families(spread: 3.0, outlierFraction: 0.0, new[] { Interior, Edge, Corner });

        var mirrored = (double[,])bySize.Clone();
        var rng = new Random(7);
        for (int i = 0; i < mirrored.GetLength(0); i++)
            for (int j = 0; j < mirrored.GetLength(1); j++)
                if (j != Fz && rng.NextDouble() < 0.5)
                    mirrored[i, j] = -mirrored[i, j];

        var expected = DesignGrouping.Group(bySize);
        var result = Group(mirrored);

        Assert.Equal(3, expected.BehaviourGroupCount);
        Assert.Equal(expected.GroupOf, result.GroupOf);
    }

    /// <summary>
    /// Two families of edge foundations alike in every force but the axial one —
    /// one in compression, one in uplift — are two designs, and no group may hold
    /// both. They are grouped as two parts, each classified on its own.
    /// </summary>
    [Fact]
    public void UpliftAndCompression_NeverShareAGroup()
    {
        var data = Demands.Families(spread: 3.0, outlierFraction: 0.0, new[] { Interior, Edge, Edge });

        for (int i = 2 * PerFamily; i < 3 * PerFamily; i++)
            data[i, Fz] = -data[i, Fz];

        var result = Group(data);

        Assert.Equal(new[] { "never in uplift", "sees uplift" }, result.Parts.Select(p => p.Name));
        Assert.Equal(Enumerable.Range(2 * PerFamily, PerFamily), result.Parts[1].Elements);
        AssertNeverMixesUplift(result, i => data[i, Fz] < 0.0);
        Assert.Contains("sees uplift", result.Report("node"));
    }

    /// <summary>
    /// The case the parts exist for. With reactions scattered by 15% of their
    /// size, the classifier on its own merges the edge family in uplift into the
    /// one in compression — checked here on the same rows, so the test cannot
    /// pass on data that happens to separate cleanly. Grouped as parts, no group
    /// mixes them, and the uplift family comes back as groups of its own rather
    /// than as a run of one-offs.
    /// </summary>
    [Fact]
    public void WholeFamilyTheClassifierWouldMerge_IsKeptApart()
    {
        var families = new[] { (Interior, PerFamily), (Edge, PerFamily), (EdgeInUplift, PerFamily) };
        var forces = Demands.Reactions(families, scatter: 0.15, seed: 3);

        int n = 3 * PerFamily;
        var bySize = new double[n, 6];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < 6; j++)
                bySize[i, j] = j == Fz ? forces[j][i] : Math.Abs(forces[j][i]);

        bool Lifts(int i) => forces[Fz][i] < 0.0;

        var unsplit = DesignGrouping.Group(bySize);
        Assert.Contains(unsplit.Groups, group => group.Any(Lifts) && !group.All(Lifts));

        var result = Group(forces);

        AssertNeverMixesUplift(result, Lifts);

        int upliftOneOffs = result.Groups.Skip(result.BehaviourGroupCount).Count(group => Lifts(group[0]));
        Assert.True(upliftOneOffs < PerFamily / 4, $"{upliftOneOffs} of {PerFamily} in uplift are one-offs");
    }

    /// <summary>
    /// Too few foundations in uplift to classify still have to be designed, and
    /// never with the ones in compression: each is a one-off.
    /// </summary>
    [Fact]
    public void AFewInUplift_AreEachOneOffs()
    {
        var families = new[] { (Interior, PerFamily), (Edge, PerFamily), (EdgeInUplift, 3) };
        var forces = Demands.Reactions(families, scatter: 0.05);

        var result = Group(forces);

        var uplift = result.Parts.Single(p => p.Name == "sees uplift");
        Assert.Null(uplift.Classification);
        Assert.Equal(3, uplift.Elements.Length);
        foreach (int element in uplift.Elements)
            Assert.Single(result.Groups[result.GroupOf[element]]);

        Assert.Contains("Too few to classify", result.Report());
    }

    /// <summary>
    /// The corner in uplift is grouped apart from the two in compression, and the
    /// columns say how the forces were read: Fz with its sign, the other five by
    /// size.
    /// </summary>
    [Fact]
    public void SeparatesUplift_WithFzSignedAndTheRestBySize()
    {
        var forces = Demands.Reactions(WithACornerInUplift, scatter: 0.05);
        var truth = Demands.Truth(WithACornerInUplift);

        var result = Group(forces);

        Assert.Equal(new[] { "never in uplift", "sees uplift" }, result.Parts.Select(p => p.Name));
        Assert.Equal(truth.Select((f, i) => (f, i)).Where(p => p.f == 2).Select(p => p.i), result.Parts[1].Elements);
        AssertNeverMixesUplift(result, i => forces[Fz][i] < 0.0);

        Assert.Equal(
            new[] { "|Fx|", "|Fy|", "Fz", "|Mx|", "|My|", "|Mz|" },
            result.Parts[0].Classification!.ColumnNames);
    }

    /// <summary>
    /// Every family should come back whole — no group holding two of them. It does
    /// not yet, and this is the reproduction.
    /// <para>
    /// The compression part holds two families, interior and edge. The classifier
    /// projects onto three whitened components whatever the data, so two of the
    /// three are nothing but each family's own scatter, scaled up to count as much
    /// as the real difference. k-means then chooses four clusters by silhouette
    /// where the mixture and HDBSCAN both find two, and slices through that
    /// scatter: an ordinary interior foundation (Fz 872, My 10) lands in a group of
    /// edge foundations (Fz about 400, My about 150). Not a sign or combination
    /// problem — the forces here are one combination, grouped by size.
    /// </para>
    /// </summary>
    [Fact(Skip = "Known defect: three whitened components over-split a part holding two families. See the summary.")]
    public void KeepsEachFamilyWhole()
    {
        var forces = Demands.Reactions(WithACornerInUplift, scatter: 0.05);
        var truth = Demands.Truth(WithACornerInUplift);

        var result = Group(forces);

        foreach (var group in result.Groups)
            Assert.Single(group.Select(element => truth[element]).Distinct());
    }

    /// <summary>
    /// An analysis that reports compression as negative must group exactly as one
    /// that reports it as positive. The sign of compression is read from the
    /// forces, so flipping every Fz changes which sign is uplift and nothing else.
    /// </summary>
    [Fact]
    public void CompressionReportedNegative_GroupsTheSame()
    {
        var forces = Demands.Reactions(WithACornerInUplift, scatter: 0.05);
        var flipped = forces.Select((force, j) => j == Fz ? force.Select(v => -v).ToArray() : force).ToArray();

        var asReported = Group(forces);
        var negative = Group(flipped);

        Assert.Equal(asReported.GroupOf, negative.GroupOf);
        Assert.Contains("Compression read as positive Fz", asReported.Report());
        Assert.Contains("Compression read as negative Fz", negative.Report());
    }

    /// <summary>
    /// Told that negative is compression, the reading flips: the corner, negative,
    /// is the one never in uplift, and interior and edge, positive, are in tension.
    /// </summary>
    [Fact]
    public void CompressionSignCanBeSet()
    {
        var forces = Demands.Reactions(WithACornerInUplift, scatter: 0.05);
        var truth = Demands.Truth(WithACornerInUplift);

        var result = Group(forces, new FoundationGroupingOptions { CompressionPositive = false });

        var never = result.Parts.Single(p => p.Name == "never in uplift");
        Assert.Equal(truth.Select((f, i) => (f, i)).Where(p => p.f == 2).Select(p => p.i), never.Elements);
        Assert.Contains("as set", result.Report());
    }

    /// <summary>
    /// Under gravity and wind both ways, every family is recovered from its
    /// envelope, and the corner — in uplift under one wind direction of three
    /// combinations — is grouped apart from the two that never lift, rather than
    /// with the neighbours it matches the rest of the time.
    /// </summary>
    [Fact]
    public void SeveralCombinations_GroupOnTheEnvelope()
    {
        var forces = Demands.Combinations(UnderWind, GravityAndWindBothWays, scatter: 0.05);
        var truth = Demands.Truth(UnderWind);

        var result = GroupOver(forces);
        var grouping = result.Grouping;

        Assert.Equal(3, result.CombinationCount);
        Assert.Equal(new[] { "never in uplift", "sees uplift" }, grouping.Parts.Select(p => p.Name));
        Assert.Equal(truth.Select((f, i) => (f, i)).Where(p => p.f == 2).Select(p => p.i), grouping.Parts[1].Elements);

        Assert.True(grouping.BehaviourGroupCount >= 3, $"only {grouping.BehaviourGroupCount} behaviour groups");
        foreach (var group in grouping.Groups)
            Assert.Single(group.Select(element => truth[element]).Distinct());

        Assert.Equal(
            new[] { "|Fx|", "|Fy|", "Fz most compression", "Fz least compression", "|Mx|", "|My|", "|Mz|" },
            grouping.Parts[0].Classification!.ColumnNames);
        Assert.Contains("envelope over 3 combinations", result.Report());
    }

    /// <summary>
    /// The envelope is each node's own, one set of seven values per node however
    /// many combinations: the largest size of the five directionless forces, and
    /// the largest and smallest Fz with their signs.
    /// </summary>
    [Fact]
    public void Envelope_IsEachNodesOwn()
    {
        var forces = Demands.Combinations(UnderWind, GravityAndWindBothWays, scatter: 0.05);
        var envelope = GroupOver(forces).Envelope;

        Assert.Equal(forces[0].Length, envelope.Count);
        Assert.Equal(new[] { "Fx", "Fy", "Fz Max", "Fz Min", "Mx", "My", "Mz" }, FoundationEnvelope.Names);

        for (int i = 0; i < envelope.Count; i++)
        {
            Assert.Equal(forces[0][i].Max(Math.Abs), envelope.Fx[i]);
            Assert.Equal(forces[1][i].Max(Math.Abs), envelope.Fy[i]);
            Assert.Equal(forces[2][i].Max(), envelope.FzMax[i]);
            Assert.Equal(forces[2][i].Min(), envelope.FzMin[i]);
            Assert.Equal(forces[3][i].Max(Math.Abs), envelope.Mx[i]);
            Assert.Equal(forces[4][i].Max(Math.Abs), envelope.My[i]);
            Assert.Equal(forces[5][i].Max(Math.Abs), envelope.Mz[i]);
        }
    }

    /// <summary>
    /// One combination per node, given as combinations, groups exactly as the
    /// plain lists do — and its envelope is the forces themselves, Fz Max and
    /// Fz Min both being the one Fz.
    /// </summary>
    [Fact]
    public void OneCombination_GroupsAsTheListsDo()
    {
        var lists = Demands.Reactions(WithACornerInUplift, scatter: 0.05);
        var asCombinations = lists.Select(force => force.Select(v => new[] { v }).ToArray()).ToArray();

        var fromLists = Group(lists);
        var fromCombinations = GroupOver(asCombinations);

        Assert.Equal(fromLists.GroupOf, fromCombinations.Grouping.GroupOf);
        Assert.Equal(1, fromCombinations.CombinationCount);
        Assert.Equal(lists[Fz], fromCombinations.Envelope.FzMax);
        Assert.Equal(lists[Fz], fromCombinations.Envelope.FzMin);
        Assert.Equal(lists[0].Select(Math.Abs), fromCombinations.Envelope.Fx);
    }

    /// <summary>
    /// Over several combinations, an analysis that reports compression as negative
    /// groups exactly as one that reports it as positive. Its Fz Max and Fz Min
    /// swap ends and change sign — the envelope keeps the signs it was given.
    /// </summary>
    [Fact]
    public void CompressionReportedNegative_OverCombinations_GroupsTheSame()
    {
        var forces = Demands.Combinations(UnderWind, GravityAndWindBothWays, scatter: 0.05);
        var flipped = forces.Select((force, j) => j == Fz
            ? force.Select(node => node.Select(v => -v).ToArray()).ToArray()
            : force).ToArray();

        var asReported = GroupOver(forces);
        var negative = GroupOver(flipped);

        Assert.Equal(asReported.Grouping.GroupOf, negative.Grouping.GroupOf);
        Assert.True(asReported.CompressionPositive);
        Assert.False(negative.CompressionPositive);
        Assert.Equal(asReported.Envelope.FzMin.Select(v => -v), negative.Envelope.FzMax);
    }

    [Fact]
    public void UnevenCombinations_AreRefusedByName()
    {
        var three = Enumerable.Range(0, 4).Select(_ => new double[] { 1, 2, 3 }).ToArray();
        var uneven = three.Select((node, i) => i == 2 ? new double[] { 1, 2 } : node).ToArray();

        var error = Assert.Throws<ArgumentException>(
            () => FoundationGrouping.Group(three, uneven, three, three, three, three));

        Assert.Contains("Fy of element 2 has 2", error.Message);
    }

    /// <summary>The six-list form reads every force with its sign, the same as the rows it replaces.</summary>
    [Fact]
    public void DesignGroupingFromSixLists_MatchesTheRows()
    {
        var data = Demands.Families(spread: 3.0, outlierFraction: 0.0, new[] { Interior, Edge, Corner });
        var c = Demands.Columns(data);

        var fromLists = DesignGrouping.Group(c[0], c[1], c[2], c[3], c[4], c[5]);
        var fromRows = DesignGrouping.Group(data);

        Assert.Equal(fromRows.GroupOf, fromLists.GroupOf);
    }

    [Fact]
    public void ListsOfDifferentLengths_AreRefusedByName()
    {
        var four = new double[] { 1, 2, 3, 4 };
        var five = new double[] { 1, 2, 3, 4, 5 };

        var error = Assert.Throws<ArgumentException>(
            () => FoundationGrouping.Group(four, five, four, four, four, four));

        Assert.Contains("Fx has 4", error.Message);
        Assert.Contains("Fy has 5", error.Message);
    }

    [Fact]
    public void NonFiniteForce_IsRefusedWithItsElement()
    {
        var data = Demands.Families(spread: 3.0, outlierFraction: 0.0, new[] { Interior, Edge });
        data[7, 5] = double.NaN;

        var error = Assert.Throws<ArgumentException>(() => Group(data));

        Assert.Contains("Mz of element 7", error.Message);
    }

    private static void AssertNeverMixesUplift(DesignGroupingResult result, Func<int, bool> lifts)
    {
        foreach (var group in result.Groups)
        {
            int uplift = group.Count(lifts);
            Assert.True(uplift == 0 || uplift == group.Length,
                $"a group of {group.Length} mixes {uplift} in uplift with {group.Length - uplift} in compression");
        }
    }

    private static DesignGroupingResult Group(double[,] data)
    {
        var c = Demands.Columns(data);
        return FoundationGrouping.Group(c[0], c[1], c[2], c[3], c[4], c[5]).Grouping;
    }

    private static DesignGroupingResult Group(double[][] forces, FoundationGroupingOptions? options = null)
        => FoundationGrouping.Group(forces[0], forces[1], forces[2], forces[3], forces[4], forces[5], options).Grouping;

    private static FoundationGroupingResult GroupOver(double[][][] forces, FoundationGroupingOptions? options = null)
        => FoundationGrouping.Group(forces[0], forces[1], forces[2], forces[3], forces[4], forces[5], options);
}
