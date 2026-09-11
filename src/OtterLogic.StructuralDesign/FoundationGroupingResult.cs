namespace OtterLogic.StructuralDesign;

/// <summary>
/// Foundations sorted into design groups, with the envelope each was grouped on.
/// </summary>
public sealed class FoundationGroupingResult
{
    internal FoundationGroupingResult(
        DesignGroupingResult grouping, FoundationEnvelope envelope, bool compressionPositive, int combinationCount)
    {
        Grouping = grouping;
        Envelope = envelope;
        CompressionPositive = compressionPositive;
        CombinationCount = combinationCount;
    }

    /// <summary>The design groups — which foundations share a design, and the evidence for it.</summary>
    public DesignGroupingResult Grouping { get; }

    /// <summary>Each foundation's own envelope over the combinations, indexed like the input.</summary>
    public FoundationEnvelope Envelope { get; }

    /// <summary>Whether positive Fz was read as compression — worked out from the forces, or as set.</summary>
    public bool CompressionPositive { get; }

    /// <summary>How many load combinations each foundation was enveloped over.</summary>
    public int CombinationCount { get; }

    /// <summary>The grouping's report — see <see cref="DesignGroupingResult.Report"/>.</summary>
    public string Report(string element = "node") => Grouping.Report(element);
}
