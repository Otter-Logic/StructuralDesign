using System.Globalization;
using System.Text;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Beams sorted into end plate types, with the envelope each was grouped on and
/// how well each type fits the beams in it.
/// </summary>
public sealed class BeamEndPlateGroupingResult
{
    internal BeamEndPlateGroupingResult(
        int[][] groups, BeamEndPlateEnvelope envelope, BeamEndPlateGroupingOptions options, int valuesPerBeam,
        double lightForce, double lightMoment, double[] leastShare, int mirroredAxialCount, bool axialLooksMirrored,
        int[] minorAxisDominant)
    {
        Groups = groups;
        Envelope = envelope;
        Options = options;
        ValuesPerBeam = valuesPerBeam;
        LightForce = lightForce;
        LightMoment = lightMoment;
        LeastShare = leastShare;
        MirroredAxialCount = mirroredAxialCount;
        AxialLooksMirrored = axialLooksMirrored;
        MinorAxisDominant = minorAxisDominant;

        GroupOf = ClusterLabels.FromMembers(groups, envelope.Count);
    }

    /// <summary>Beam indices per end plate type, largest type first; within a type, input order.</summary>
    public int[][] Groups { get; }

    /// <summary>End plate type of each beam, in input order.</summary>
    public int[] GroupOf { get; }

    /// <summary>Each beam's own envelope, indexed like the input.</summary>
    public BeamEndPlateEnvelope Envelope { get; }

    /// <summary>The settings the grouping ran with.</summary>
    public BeamEndPlateGroupingOptions Options { get; }

    /// <summary>How many values each beam was enveloped over — combinations, times ends if both were given.</summary>
    public int ValuesPerBeam { get; }

    /// <summary>Tension or shear below which a connection counts as light.</summary>
    public double LightForce { get; }

    /// <summary>Major-axis moment below which a connection counts as light.</summary>
    public double LightMoment { get; }

    /// <summary>
    /// For each type, the smallest share of the type's peak that any of its beams
    /// carries in any governing force, light values counting as the light level.
    /// At least <see cref="BeamEndPlateGroupingOptions.Efficiency"/> by construction.
    /// </summary>
    public double[] LeastShare { get; }

    /// <summary>
    /// Beams whose largest axial force is exactly the negative of their smallest —
    /// +N and −N. Every beam with any axial force doing so is the signature of end
    /// forces in the member-end convention; see <see cref="AxialLooksMirrored"/>.
    /// </summary>
    public int MirroredAxialCount { get; }

    /// <summary>
    /// True when every beam with any axial force has it mirrored, +N and −N — end
    /// forces in the member-end convention rather than internal forces, which makes
    /// tension unreadable.
    /// </summary>
    public bool AxialLooksMirrored { get; }

    /// <summary>
    /// Beams whose minor-axis shear or moment — carried, not grouped on — is larger
    /// than their major-axis one and more than light. Either their local axes are
    /// turned relative to the rest, or they genuinely bend the weak way; both are
    /// worth knowing before trusting a type that was chosen without those forces.
    /// </summary>
    public int[] MinorAxisDominant { get; }

    /// <summary>Number of end plate types.</summary>
    public int GroupCount => Groups.Length;

    /// <summary>
    /// What was grouped and how: the types with their peaks and fit, how the forces
    /// were read, and anything that deserves a second look.
    /// </summary>
    /// <param name="element">What a beam is called in the report — "bar", "beam".</param>
    public string Report(string element = "beam")
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0", inv);

        text.AppendLine($"End plate types  {GroupCount}, every {element} carrying at least "
            + $"{Percent(Options.Efficiency)} of its type's governing forces");
        text.AppendLine($"Governing        tension, |Fz| and |My|; compression, Fy, Mx and Mz are carried, not grouped on");
        text.AppendLine($"Light            tension or shear under {F(LightForce)}, moment under {F(LightMoment)} — "
            + $"{Percent(Options.LightShare)} of the largest");
        text.AppendLine($"Values           {ValuesPerBeam} per {element}, enveloped");
        text.AppendLine();
        text.AppendLine($"  type  {element}s   tension     |Fz|     |My|   least share");

        var tension = Tension(Envelope, Options.TensionPositive);
        for (int g = 0; g < GroupCount; g++)
        {
            var members = Groups[g];
            text.AppendLine(string.Create(inv,
                $"  {g,4}  {members.Length,5}  {members.Max(i => tension[i]),8:0}  {members.Max(i => Envelope.Fz[i]),7:0}  "
                + $"{members.Max(i => Envelope.My[i]),7:0}   {Percent(LeastShare[g]),6}"));
        }

        if (MinorAxisDominant.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"{MinorAxisDominant.Length} {element}(s) carry more minor-axis shear or moment than major: "
                + $"{string.Join(", ", MinorAxisDominant.Take(20))}{(MinorAxisDominant.Length > 20 ? ", ..." : string.Empty)}. "
                + "Their types were chosen on Fz and My alone — check their local axes, or whether they bend "
                + "the weak way.");
        }

        if (AxialLooksMirrored)
        {
            text.AppendLine();
            text.AppendLine($"Every {element} with an axial force has it as +N and -N. That is the member-end convention, "
                + "where one tension reads opposite at the two ends — give the axial force as the internal force, the "
                + "same sign at both ends, or tension cannot be read.");
        }

        return text.ToString().TrimEnd();
    }

    private static string Percent(double share)
        => (share * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Each beam's largest tension, zero for a beam never in tension.</summary>
    internal static double[] Tension(BeamEndPlateEnvelope envelope, bool tensionPositive)
        => tensionPositive
            ? envelope.FxMax.Select(v => Math.Max(v, 0.0)).ToArray()
            : envelope.FxMin.Select(v => Math.Max(-v, 0.0)).ToArray();
}
