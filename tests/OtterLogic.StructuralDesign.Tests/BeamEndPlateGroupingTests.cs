using Xunit;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// End plate types are cut so every beam carries a set share of its type's
/// governing forces — tension, |Fz|, |My|. These check that promise directly, and
/// around it the envelope, both ends reading as one, tension kept apart from
/// compression, light connections kept together, and the input traps that are
/// caught rather than silently misread.
/// </summary>
public class BeamEndPlateGroupingTests
{
    // Fx, Fy, Fz, Mx, My, Mz at the beam end, as gravity and wind parts.
    private static readonly (double[] Gravity, double[] Wind, int Count)[] ThreeKinds =
    {
        // Floor beams: next to no axial force, gravity shear and moment.
        (new[] { -5.0, 3, 60, 1, 4, 90 }, new[] { 10.0, 1, 5, 0.5, 2, 15 }, 45),
        // Ties: mostly tension, little bending.
        (new[] { 120.0, 2, 15, 0.5, 1, 10 }, new[] { 40.0, 1, 3, 0.2, 0.5, 4 }, 45),
        // Moment frame beams: heavy shear and bending, reversing under wind.
        (new[] { -15.0, 8, 140, 3, 10, 260 }, new[] { 30.0, 5, 40, 2, 8, 150 }, 45),
    };

    private static readonly double[] GravityAndWindBothWays = { 0.0, 1.0, -1.0 };

    /// <summary>
    /// The promise the types are cut to: in every type, every beam carries at least
    /// the efficiency share of the type's peak in tension, |Fz| and |My|, a light
    /// value counting as the light level. Checked from the envelope, not taken from
    /// the result's own figures.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.6)]
    [InlineData(0.8)]
    public void EveryBeamCarriesTheEfficiencyShareOfItsType(double efficiency)
    {
        var forces = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.15);
        var result = Group(forces, new BeamEndPlateGroupingOptions { Efficiency = efficiency });
        var env = result.Envelope;

        double[] tension = env.FxMax.Select(v => Math.Max(v, 0.0)).ToArray();
        var governing = new[] { (tension, result.LightForce), (env.Fz, result.LightForce), (env.My, result.LightMoment) };

        foreach (var members in result.Groups)
        {
            foreach (var (values, light) in governing)
            {
                double peak = members.Max(i => Math.Max(values[i], light));
                foreach (int i in members)
                    Assert.True(Math.Max(values[i], light) >= efficiency * peak - 1e-9,
                        $"beam {i} carries {Math.Max(values[i], light) / peak:P1} of its type's peak, under {efficiency:P0}");
            }
        }

        Assert.All(result.LeastShare, share => Assert.True(share >= efficiency - 1e-9));
    }

    /// <summary>Distinct kinds of beam never share a type: they are further apart than any efficiency allows.</summary>
    [Fact]
    public void DistinctKindsNeverShareAType()
    {
        var forces = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.05);
        var truth = Demands.Truth(ThreeKinds);

        var result = Group(forces);

        foreach (var members in result.Groups)
            Assert.Single(members.Select(i => truth[i]).Distinct());
    }

    /// <summary>
    /// Raising the efficiency can only split types, never merge them: the types
    /// are cuts of one tree, lower in it as the efficiency rises.
    /// </summary>
    [Fact]
    public void HigherEfficiencyNeverGivesFewerTypes()
    {
        var forces = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.15);

        var counts = new[] { 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 }
            .Select(e => Group(forces, new BeamEndPlateGroupingOptions { Efficiency = e }).GroupCount)
            .ToArray();

        for (int k = 1; k < counts.Length; k++)
            Assert.True(counts[k] >= counts[k - 1], $"counts {string.Join(", ", counts)}");
    }

    /// <summary>One set of seven per beam: the signed largest and smallest Fx, the largest size of the rest.</summary>
    [Fact]
    public void Envelope_IsEachBeamsOwn()
    {
        var forces = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.05);
        var envelope = Group(forces).Envelope;

        Assert.Equal(new[] { "Fx Max", "Fx Min", "Fy", "Fz", "Mx", "My", "Mz" }, BeamEndPlateEnvelope.Names);

        for (int i = 0; i < envelope.Count; i++)
        {
            Assert.Equal(forces[0][i].Max(), envelope.FxMax[i]);
            Assert.Equal(forces[0][i].Min(), envelope.FxMin[i]);
            Assert.Equal(forces[1][i].Max(Math.Abs), envelope.Fy[i]);
            Assert.Equal(forces[2][i].Max(Math.Abs), envelope.Fz[i]);
            Assert.Equal(forces[3][i].Max(Math.Abs), envelope.Mx[i]);
            Assert.Equal(forces[4][i].Max(Math.Abs), envelope.My[i]);
            Assert.Equal(forces[5][i].Max(Math.Abs), envelope.Mz[i]);
        }
    }

    /// <summary>
    /// Both ends of each beam in the branch — the second equal and opposite in
    /// shear and moment, the same axial force — give exactly the types and the
    /// envelope that one end does.
    /// </summary>
    [Fact]
    public void BothEndsEqualAndOpposite_GroupAsOneEndDoes()
    {
        var oneEnd = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.15);
        var bothEnds = oneEnd.Select((force, j) => force
            .Select(values => values.Concat(values.Select(v => j == 0 ? v : -v)).ToArray())
            .ToArray()).ToArray();

        var single = Group(oneEnd);
        var both = Group(bothEnds);

        Assert.Equal(6, both.ValuesPerBeam);
        Assert.Equal(single.GroupOf, both.GroupOf);
        for (int k = 0; k < BeamEndPlateEnvelope.Names.Count; k++)
            Assert.Equal(single.Envelope.Columns[k], both.Envelope.Columns[k]);
    }

    /// <summary>
    /// A beam in real tension never shares a type with one in none. Real means more
    /// than the light level divided by the efficiency: a beam with no tension counts
    /// as carrying the light level, and the efficiency then rules out anything in its
    /// type above that. Below it, tension is light — treated as none, on purpose — so
    /// a lightly tensioned beam may sit with compression beams, and that is the rule
    /// working, not failing. Heavy scatter, so every seed has beams on both sides of
    /// the line.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RealTensionNeverSharesATypeWithNone(int seed)
    {
        var kinds = new (double[] Gravity, double[] Wind, int Count)[]
        {
            (new[] { 100.0, 3, 20, 1, 2, 20 }, new[] { 10.0, 1, 3, 0.2, 0.5, 4 }, 45),
            (new[] { -100.0, 3, 20, 1, 2, 20 }, new[] { 10.0, 1, 3, 0.2, 0.5, 4 }, 45),
            ThreeKinds[0],
        };

        var forces = Demands.Combinations(kinds, GravityAndWindBothWays, scatter: 0.3, seed: seed);
        var result = Group(forces);

        double[] tension = result.Envelope.FxMax.Select(v => Math.Max(v, 0.0)).ToArray();
        double real = result.LightForce / result.Options.Efficiency;

        foreach (var members in result.Groups)
            Assert.False(members.Any(i => tension[i] > real) && members.Any(i => tension[i] == 0.0),
                $"a type holds a beam in more than {real:F0} of tension and a beam in none");
    }

    /// <summary>
    /// Connections lighter than the light level in every governing force are all
    /// one type, however much their small forces differ — 5 against 50 when the
    /// heaviest is 1,000 is a difference nobody designs for.
    /// </summary>
    [Fact]
    public void LightConnectionsShareOneType()
    {
        var kinds = new (double[] Gravity, double[] Wind, int Count)[]
        {
            (new[] { 0.0, 0, 1000, 0, 0, 1000 }, new double[6], 10),
            (new[] { 0.0, 0, 5, 0, 0, 5 }, new double[6], 10),
            (new[] { 0.0, 0, 50, 0, 0, 50 }, new double[6], 10),
            (new[] { 0.0, 0, 150, 0, 0, 150 }, new double[6], 10),
        };

        var forces = Demands.Combinations(kinds, new[] { 0.0 }, scatter: 0.05);
        var truth = Demands.Truth(kinds);

        // Mz rather than My above is deliberate nowhere: swap so the moment governs.
        (forces[4], forces[5]) = (forces[5], forces[4]);

        var result = Group(forces);
        var light = Enumerable.Range(0, truth.Length).Where(i => truth[i] is 1 or 2 or 3).ToArray();

        Assert.Single(light.Select(i => result.GroupOf[i]).Distinct());
        Assert.NotEqual(result.GroupOf[0], result.GroupOf[light[0]]);
    }

    /// <summary>
    /// Beams whose carried minor-axis shear or moment is larger than the major one
    /// they were grouped on are named, so their types are not trusted blind.
    /// </summary>
    [Fact]
    public void MinorAxisDominantBeamsAreNamed()
    {
        var kinds = new (double[] Gravity, double[] Wind, int Count)[]
        {
            (new[] { 0.0, 20, 100, 1, 150, 30 }, new double[6], 10),
            (new[] { 0.0, 180, 100, 1, 150, 300 }, new double[6], 5),
        };

        var forces = Demands.Combinations(kinds, new[] { 0.0 }, scatter: 0.02);
        var result = Group(forces);

        Assert.Equal(Enumerable.Range(10, 5), result.MinorAxisDominant);
        Assert.Contains("minor-axis", result.Report());
    }

    /// <summary>
    /// End forces in the member-end convention — the same tension as +N at one
    /// end and −N at the other — are caught and said, not grouped on quietly.
    /// </summary>
    [Fact]
    public void EndForcesInTheMemberEndConvention_AreFlagged()
    {
        var oneEnd = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.05);
        var memberEnd = oneEnd.Select(force => force
            .Select(values => values.Concat(values.Select(v => -v)).ToArray())
            .ToArray()).ToArray();

        Assert.False(Group(oneEnd).AxialLooksMirrored);

        var endForces = Group(memberEnd);
        Assert.True(endForces.AxialLooksMirrored);
        Assert.Equal(oneEnd[0].Length, endForces.MirroredAxialCount);
        Assert.Contains("member-end convention", endForces.Report());
    }

    /// <summary>
    /// With compression reported positive, telling the grouping so gives the same
    /// types as tension-positive forces flipped the other way.
    /// </summary>
    [Fact]
    public void TensionSignCanBeSet()
    {
        var forces = Demands.Combinations(ThreeKinds, GravityAndWindBothWays, scatter: 0.15);
        var flipped = forces.Select((force, j) => j == 0
            ? force.Select(values => values.Select(v => -v).ToArray()).ToArray()
            : force).ToArray();

        var asGiven = Group(forces);
        var compressionPositive = Group(flipped, new BeamEndPlateGroupingOptions { TensionPositive = false });

        Assert.Equal(asGiven.GroupOf, compressionPositive.GroupOf);
    }

    /// <summary>
    /// One value per beam, given as a branch of one, groups exactly as the plain
    /// lists do, and its envelope is the forces themselves.
    /// </summary>
    [Fact]
    public void OneValue_GroupsAsTheListsDo()
    {
        var forces = Demands.Combinations(ThreeKinds, new[] { 0.0 }, scatter: 0.05);
        var lists = forces.Select(force => force.Select(values => values[0]).ToArray()).ToArray();

        var fromLists = BeamEndPlateGrouping.Group(lists[0], lists[1], lists[2], lists[3], lists[4], lists[5]);
        var fromTrees = Group(forces);

        Assert.Equal(fromLists.GroupOf, fromTrees.GroupOf);
        Assert.Equal(1, fromTrees.ValuesPerBeam);
        Assert.Equal(lists[0], fromTrees.Envelope.FxMax);
        Assert.Equal(lists[0], fromTrees.Envelope.FxMin);
        Assert.Equal(lists[1].Select(Math.Abs), fromTrees.Envelope.Fy);
    }

    [Fact]
    public void EfficiencyOutsideItsRange_IsRefused()
    {
        var forces = Demands.Combinations(ThreeKinds, new[] { 0.0 }, scatter: 0.05);
        Assert.Throws<ArgumentOutOfRangeException>(() => Group(forces, new BeamEndPlateGroupingOptions { Efficiency = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Group(forces, new BeamEndPlateGroupingOptions { Efficiency = 1.5 }));
    }

    private static BeamEndPlateGroupingResult Group(double[][][] forces, BeamEndPlateGroupingOptions? options = null)
        => BeamEndPlateGrouping.Group(forces[0], forces[1], forces[2], forces[3], forces[4], forces[5], options);
}
