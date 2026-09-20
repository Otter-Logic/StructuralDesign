using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="GridLevelInference"/>. The claims about geometry are
/// here; how a population is banded is the nested <see cref="Banding"/>, so the
/// line between the two is visible at the call site.
/// </summary>
public sealed record GridLevelOptions
{
    /// <summary>
    /// Points closer than this are the same point, and positions closer than this
    /// the same position: the document's absolute tolerance. It decides which beams
    /// frame into a column, and no band is ever split by a gap this small.
    /// </summary>
    public double Tolerance { get; init; } = 0.001;

    /// <summary>How far each gridline is drawn past the last element on it, at both ends.</summary>
    public double Extension { get; init; }

    /// <summary>
    /// How populations are banded. Its resolution is replaced by
    /// <see cref="Tolerance"/> for every length, and left at zero for angles.
    /// </summary>
    public ValueBandsOptions Banding { get; init; } = new();

    /// <summary>Checks these settings.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Tolerance) || Tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Tolerance), Tolerance, "Tolerance must be above zero.");
        if (!double.IsFinite(Extension) || Extension < 0.0)
            throw new ArgumentOutOfRangeException(nameof(Extension), Extension, "Extension must be zero or above.");
        ArgumentNullException.ThrowIfNull(Banding);
        Banding.Validate();
    }
}
