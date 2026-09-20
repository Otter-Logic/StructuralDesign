using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="GeometryQA"/>. Only the tolerance is a claim about the
/// model; the rest set how widely it looks, never what it decides is wrong.
/// </summary>
public sealed record GeometryQAOptions
{
    /// <summary>
    /// The document's absolute tolerance: points closer than this are one node, as a
    /// solver will join them, and nothing closer than this is a gap. The one distance
    /// given rather than learned, because it is a fact about the analysis rather than
    /// a judgement about the model.
    /// </summary>
    public double Tolerance { get; init; } = 0.001;

    /// <summary>
    /// How many of its nearest nodes each node is compared with when looking for
    /// connections that were meant and missed. A search breadth: more finds a miss
    /// hidden behind more near neighbours, at more cost; whether a pair is flagged is
    /// decided by the model's own detour ratios, not by this.
    /// </summary>
    public int Neighbours { get; init; } = 6;

    /// <summary>
    /// How many of the connectivity graph's lowest eigenvectors are swept for weakly
    /// attached regions. A search breadth: each finds at most one region, the next
    /// weakest attachment.
    /// </summary>
    public int SpectralProbes { get; init; } = 6;

    /// <summary>
    /// How line directions, heights and grid positions are banded when checking
    /// alignment — the same banding Grid and Level Inference uses.
    /// </summary>
    public ValueBandsOptions Banding { get; init; } = new();

    /// <summary>Checks these settings.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Tolerance) || Tolerance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Tolerance), Tolerance, "Tolerance must be above zero.");
        if (Neighbours < 1)
            throw new ArgumentOutOfRangeException(nameof(Neighbours), Neighbours, "Compare each node with at least one other.");
        if (SpectralProbes < 1)
            throw new ArgumentOutOfRangeException(nameof(SpectralProbes), SpectralProbes, "Sweep at least one eigenvector.");
        ArgumentNullException.ThrowIfNull(Banding);
        Banding.Validate();
    }
}
