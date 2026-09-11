namespace OtterLogic.StructuralDesign;

/// <summary>
/// Each beam's own envelope over the values given for it — every load
/// combination, and both ends if both were given: one set of seven forces per
/// beam.
/// <para>
/// The axial force is the one whose direction changes an end plate's design —
/// tension puts the bolts and plate in tension, compression goes through in
/// bearing — so it keeps two values, the largest and the smallest, with their
/// signs as the analysis gave them. Shear, torsion and bending are taken by size:
/// an end plate is symmetrical, so it resists them the same either way, and the two
/// ends of a beam, equal and opposite, then give the same value.
/// </para>
/// <para>
/// Values from different combinations: the largest shear and the largest axial
/// force of one beam need not act together. That is what an envelope is, and it is
/// conservative, but it is not a set of forces that ever occurred.
/// </para>
/// </summary>
public sealed class BeamEndPlateEnvelope
{
    /// <summary>What each of <see cref="Columns"/> is, in order — the order the outputs appear in.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "Fx Max", "Fx Min", "Fy", "Fz", "Mx", "My", "Mz" };

    internal BeamEndPlateEnvelope(double[] fxMax, double[] fxMin, double[] fy, double[] fz, double[] mx, double[] my, double[] mz)
    {
        FxMax = fxMax;
        FxMin = fxMin;
        Fy = fy;
        Fz = fz;
        Mx = mx;
        My = my;
        Mz = mz;
        Columns = new[] { fxMax, fxMin, fy, fz, mx, my, mz };
    }

    /// <summary>Largest axial force per beam, signed as it came in.</summary>
    public double[] FxMax { get; }

    /// <summary>Smallest axial force per beam, signed as it came in.</summary>
    public double[] FxMin { get; }

    /// <summary>Largest |Fy| per beam.</summary>
    public double[] Fy { get; }

    /// <summary>Largest |Fz| per beam.</summary>
    public double[] Fz { get; }

    /// <summary>Largest |Mx| per beam.</summary>
    public double[] Mx { get; }

    /// <summary>Largest |My| per beam.</summary>
    public double[] My { get; }

    /// <summary>Largest |Mz| per beam.</summary>
    public double[] Mz { get; }

    /// <summary>All seven, in the order of <see cref="Names"/>: <c>Columns[k][i]</c> is value k of beam i.</summary>
    public IReadOnlyList<double[]> Columns { get; }

    /// <summary>Number of beams.</summary>
    public int Count => FxMax.Length;
}
