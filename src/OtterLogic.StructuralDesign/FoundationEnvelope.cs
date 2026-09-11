namespace OtterLogic.StructuralDesign;

/// <summary>
/// Each foundation's own envelope over the load combinations: one set of seven
/// forces per foundation, however many combinations there were.
/// <para>
/// Shear, bending and torsion are designed to act either way, so each is its
/// largest size under any combination — a positive number. The axial force is
/// the one with a direction that changes the design, so it keeps two values, the
/// largest and the smallest under any combination, with their signs as the
/// analysis gave them: one end is the most compression a foundation bears, the
/// other the least — or, when it goes negative of compression, the most uplift.
/// Which end is which depends on the analysis's sign convention, and
/// <see cref="FoundationGroupingResult.CompressionPositive"/> says which was read.
/// </para>
/// <para>
/// Values from different combinations: the largest shear and the largest axial
/// force of one foundation need not act together. That is what an envelope is,
/// and it is conservative, but it is not a set of forces that ever occurred.
/// </para>
/// </summary>
public sealed class FoundationEnvelope
{
    /// <summary>What each of <see cref="Columns"/> is, in order — the order the outputs appear in.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "Fx", "Fy", "Fz Max", "Fz Min", "Mx", "My", "Mz" };

    internal FoundationEnvelope(double[] fx, double[] fy, double[] fzMax, double[] fzMin, double[] mx, double[] my, double[] mz)
    {
        Fx = fx;
        Fy = fy;
        FzMax = fzMax;
        FzMin = fzMin;
        Mx = mx;
        My = my;
        Mz = mz;
        Columns = new[] { fx, fy, fzMax, fzMin, mx, my, mz };
    }

    /// <summary>Largest |Fx| per foundation over every combination.</summary>
    public double[] Fx { get; }

    /// <summary>Largest |Fy| per foundation over every combination.</summary>
    public double[] Fy { get; }

    /// <summary>Largest Fz per foundation over every combination, signed as it came in.</summary>
    public double[] FzMax { get; }

    /// <summary>Smallest Fz per foundation over every combination, signed as it came in.</summary>
    public double[] FzMin { get; }

    /// <summary>Largest |Mx| per foundation over every combination.</summary>
    public double[] Mx { get; }

    /// <summary>Largest |My| per foundation over every combination.</summary>
    public double[] My { get; }

    /// <summary>Largest |Mz| per foundation over every combination.</summary>
    public double[] Mz { get; }

    /// <summary>All seven, in the order of <see cref="Names"/>: <c>Columns[k][i]</c> is value k of foundation i.</summary>
    public IReadOnlyList<double[]> Columns { get; }

    /// <summary>Number of foundations.</summary>
    public int Count => Fx.Length;
}
