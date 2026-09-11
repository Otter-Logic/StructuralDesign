namespace OtterLogic.StructuralDesign;

/// <summary>
/// Elements grouped separately from the rest, because they can never share a
/// design with them — foundations that see uplift and foundations that never
/// do. A grouping with no such rule is one part holding every element.
/// </summary>
public sealed class DesignGroupingPart
{
    internal DesignGroupingPart(string? name, int[] elements, SixDofClassificationResult? classification)
    {
        Name = name;
        Elements = elements;
        Classification = classification;
    }

    /// <summary>What the part's elements have in common — "sees uplift". Null for the single part of a grouping with no rule.</summary>
    public string? Name { get; }

    /// <summary>
    /// The elements in this part, as indices into the input, ascending. Member r
    /// of <see cref="Classification"/> is element <c>Elements[r]</c>.
    /// </summary>
    public int[] Elements { get; }

    /// <summary>
    /// The classification the part's groups were read from — model, scores,
    /// centres. Null when the part held too few elements to classify, in which
    /// case each is a one-off.
    /// </summary>
    public SixDofClassificationResult? Classification { get; }
}
