namespace OtterLogic.StructuralDesign;

/// <summary>
/// Settings for <see cref="FoundationGrouping"/>. The intended call passes none.
/// <para>
/// <see cref="CompressionPositive"/> is a claim about foundations, so it lives
/// here; <see cref="Classification"/> nests the classifier's own settings rather
/// than repeating them, so the boundary between the two is visible where they
/// are set.
/// </para>
/// </summary>
public sealed record FoundationGroupingOptions
{
    /// <summary>
    /// Whether positive Fz is compression. Null works it out from the forces —
    /// see <see cref="FoundationGrouping"/> — which is right for any model
    /// carrying gravity load. Set it only when the forces have no gravity in
    /// them, such as a wind-only combination.
    /// </summary>
    public bool? CompressionPositive { get; init; }

    /// <summary>Settings for the classifier each part is grouped by.</summary>
    public SixDofClassificationOptions Classification { get; init; } = new();
}
