namespace OtterLogic.SixDofBehaviour;

/// <summary>
/// The three clustering models the classifier chooses between.
/// <para>
/// They are not three implementations of one idea. Each assumes something
/// different about what a behaviour family looks like, and the whole point of
/// running all three is that the data decides which assumption holds.
/// </para>
/// </summary>
public enum BehaviourModel
{
    /// <summary>
    /// k-means. Assumes round families of roughly equal size, and puts every
    /// member in one. Fastest and steadiest when that is true.
    /// </summary>
    KMeans,

    /// <summary>
    /// Gaussian mixture. Allows families to be elongated, unequal, and to
    /// overlap, and reports how strongly each member belongs. The one to use
    /// when members sit between two behaviours.
    /// </summary>
    GaussianMixture,

    /// <summary>
    /// HDBSCAN. Assumes families are dense regions of any shape, and refuses to
    /// place members that belong to none. The one to use when there are genuine
    /// one-off members or the families are irregular.
    /// </summary>
    Hdbscan,
}
