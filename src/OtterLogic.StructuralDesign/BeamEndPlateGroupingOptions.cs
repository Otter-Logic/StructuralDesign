namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="BeamEndPlateGrouping"/>. The intended call passes none.
/// </summary>
public sealed record BeamEndPlateGroupingOptions
{
    /// <summary>
    /// Share of its group's peak that every beam must carry, in each governing
    /// force, between 0 and 1. 0.6 by default.
    /// <para>
    /// The one setting that matters. Higher keeps every end plate closer to what
    /// its beam needs and gives more types; lower gives fewer types and designs more
    /// beams for forces they never see. Measured on a 110-bar model: 0.5 gave 9
    /// groups with the worst beam at half its group's peak, 0.6 gave 13 with the
    /// worst above 60% and the average near 75–80%, and 0.7 gave 24. 0.6 is where the
    /// count stops being small enough to be useful before the waste becomes large.
    /// </para>
    /// </summary>
    public double Efficiency { get; init; } = 0.6;

    /// <summary>
    /// Share of the model's largest governing force below which a connection is
    /// simply light, between 0 and 1. 0.2 by default.
    /// <para>
    /// Every tension or shear under this share of the model's largest, and every
    /// moment under this share of its largest, is treated as equal to it — so all
    /// the light connections fall into one or a few light types instead of being
    /// split on differences nobody designs for: 120 against 300 when the heaviest
    /// is 12,000. Measured on the same model, 0.1 gave 23 groups at 60% efficiency,
    /// five of them single bars; 0.2 gave 13, one single.
    /// </para>
    /// </summary>
    public double LightShare { get; init; } = 0.2;

    /// <summary>
    /// Whether positive Fx is tension. True by default: internal axial force is
    /// tension-positive in almost every analysis package.
    /// </summary>
    public bool TensionPositive { get; init; } = true;

    internal void Validate()
    {
        if (!(Efficiency > 0.0 && Efficiency <= 1.0))
            throw new ArgumentOutOfRangeException(nameof(Efficiency), Efficiency,
                "Efficiency is a share of the group's peak, above 0 and at most 1.");
        if (!(LightShare >= 0.0 && LightShare < 1.0))
            throw new ArgumentOutOfRangeException(nameof(LightShare), LightShare,
                "Light share is a share of the model's largest force, from 0 up to but not including 1.");
    }
}
