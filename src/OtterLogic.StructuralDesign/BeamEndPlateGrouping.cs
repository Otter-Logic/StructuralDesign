using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Groups beams into end plate types from the six forces at their ends, over any
/// number of load combinations and either or both ends, so that every beam of a
/// type carries a set share of the type's governing forces.
/// <para>
/// This is deliberately not the behaviour classifier the foundation grouping is
/// built on. The classifier asks which beams behave alike; an end plate type asks
/// which beams can share one plate without wasting it, and on real forces the two
/// answers part company. Measured on a 110-bar model, the classifier returned two
/// groups, one of 96 bars running from 74 to 11,896 in shear and 235 to 12,587 in
/// moment. The causes were structural, not a bug: once every force is standardised
/// a few kN of noise weighs as much as the shear that governs, and a statistical
/// cluster says nothing about how far apart its members' forces may be.
/// </para>
/// <para>
/// So the grouping is on what governs an end plate — tension, major shear |Fz| and
/// major moment |My| — and nothing else. Compression goes through in bearing, and
/// minor-axis shear, torsion and minor-axis moment are small on a typical beam;
/// all of them come back in the envelope, but none splits a type. Forces are read
/// as ratios, so 80 against 100 is the same gap as 8,000 against 10,000, and any
/// value under <see cref="BeamEndPlateGroupingOptions.LightShare"/> of the model's
/// largest counts as that light level, so the light connections form light types
/// rather than splitting on differences nobody designs for.
/// </para>
/// <para>
/// The types come from <see cref="RatioClustering"/> in Unsupervised: a
/// complete-linkage tree over the largest ratio gap in any governing force, cut so
/// that every beam carries at least
/// <see cref="BeamEndPlateGroupingOptions.Efficiency"/> of its type's peak. The
/// number of types follows from that, rather than being guessed. The mechanism is
/// generic; what governs and where the light level sits are the structural
/// judgement, and stay here.
/// </para>
/// </summary>
public static class BeamEndPlateGrouping
{
    /// <summary>
    /// Groups n beams from their six end forces under one load combination: one
    /// list per degree of freedom, one value per beam, with the axial force signed
    /// as the internal force — the same sign at both ends.
    /// </summary>
    /// <exception cref="ArgumentException">The six lists are not all the same length.</exception>
    public static BeamEndPlateGroupingResult Group(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, IReadOnlyList<double> fz,
        IReadOnlyList<double> mx, IReadOnlyList<double> my, IReadOnlyList<double> mz,
        BeamEndPlateGroupingOptions? options = null)
        => Group(SixDof.AsOneCombination(SixDof.Read(fx, fy, fz, mx, my, mz)), options);

    /// <summary>
    /// Groups n beams from their six end forces, several values each: one list per
    /// degree of freedom, one entry per beam, and in each entry its values —
    /// <c>fx[i][k]</c> is value k of beam i. The values are every load combination,
    /// and may be both ends of each: shear and moment are taken by size, so equal
    /// and opposite ends give the same envelope as one.
    /// <para>
    /// Give the axial force as the internal force, the same sign at both ends. In
    /// the member-end convention the same tension reads +N at one end and −N at the
    /// other; the result says so through
    /// <see cref="BeamEndPlateGroupingResult.AxialLooksMirrored"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The six lists are not all the same length, or the beams do not all have the
    /// same number of values.
    /// </exception>
    public static BeamEndPlateGroupingResult Group(
        IReadOnlyList<IReadOnlyList<double>> fx, IReadOnlyList<IReadOnlyList<double>> fy,
        IReadOnlyList<IReadOnlyList<double>> fz, IReadOnlyList<IReadOnlyList<double>> mx,
        IReadOnlyList<IReadOnlyList<double>> my, IReadOnlyList<IReadOnlyList<double>> mz,
        BeamEndPlateGroupingOptions? options = null)
        => Group(SixDof.ReadCombinations(fx, fy, fz, mx, my, mz), options);

    /// <summary>The one implementation, over <c>forces[dof][beam][value]</c>.</summary>
    private static BeamEndPlateGroupingResult Group(double[][][] forces, BeamEndPlateGroupingOptions? options)
    {
        options ??= new BeamEndPlateGroupingOptions();
        options.Validate();

        var axial = forces[0];
        int n = axial.Length;
        int values = axial[0].Length;

        var envelope = new BeamEndPlateEnvelope(
            SixDof.Largest(axial),
            SixDof.Smallest(axial),
            SixDof.LargestSize(forces[1]),
            SixDof.LargestSize(forces[2]),
            SixDof.LargestSize(forces[3]),
            SixDof.LargestSize(forces[4]),
            SixDof.LargestSize(forces[5]));

        // What governs an end plate, per beam: tension, major shear, major moment.
        var tension = BeamEndPlateGroupingResult.Tension(envelope, options.TensionPositive);
        double[][] governing = { tension, envelope.Fz, envelope.My };

        // Tension and shear are both forces and share one light level; moment has
        // its own. Anything lighter reads as the light level itself.
        double lightForce = options.LightShare * Math.Max(tension.Max(), envelope.Fz.Max());
        double lightMoment = options.LightShare * envelope.My.Max();
        double[] light = { lightForce, lightForce, lightMoment };

        var levels = new double[n, governing.Length];
        for (int beam = 0; beam < n; beam++)
            for (int k = 0; k < governing.Length; k++)
                levels[beam, k] = Math.Max(governing[k][beam], light[k]);

        var types = RatioClustering.Fit(levels, options.Efficiency);
        var groups = types.Clusters();
        var leastShare = types.LeastShare;

        // Exactly mirrored, to rounding: +N and −N from the same beam. Zero is not
        // mirrored — a beam with no axial force says nothing about convention.
        int mirrored = Enumerable.Range(0, n).Count(i =>
        {
            double max = envelope.FxMax[i];
            double min = envelope.FxMin[i];
            return max > 0.0 && Math.Abs(max + min) <= 1e-9 * Math.Max(Math.Abs(max), Math.Abs(min));
        });
        int carryingAxial = Enumerable.Range(0, n).Count(i => envelope.FxMax[i] != 0.0 || envelope.FxMin[i] != 0.0);

        // Carried forces bigger than the governing ones they sit beside, and more
        // than light: the types were chosen without them.
        var minorDominant = Enumerable.Range(0, n)
            .Where(i => (envelope.Fy[i] > envelope.Fz[i] && envelope.Fy[i] > lightForce)
                        || (envelope.Mz[i] > envelope.My[i] && envelope.Mz[i] > lightMoment))
            .ToArray();

        return new BeamEndPlateGroupingResult(
            groups, envelope, options, values, lightForce, lightMoment, leastShare,
            mirrored, mirrored > 0 && mirrored == carryingAxial, minorDominant);
    }
}
