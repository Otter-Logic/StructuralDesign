# StructuralDesign

Multipurpose tools for structural engineering — before an analysis and after one.
Every structural tool in OtterLogic lives here.

A domain toolkit, sibling to
[StructuralForm](https://github.com/Otter-Logic/StructuralForm), which generates
a structure; this one answers questions about a structure that already exists.

```
Core  ->  MachineLearning  ->  Unsupervised  ->  StructuralDesign
          (features, PCA,      (the methods,     (this repo: what
           graphs)              and fusion)       structural data is)
```

## The rule these tools follow

> **Generic engines, not hard-coded jobs.** A tool here knows what structural
> data *is* — a stick model and its supports, six degrees of freedom — and nothing
> about what any one structure or job makes of it.

OtterLogic exists so Grasshopper users can build their own scripts. A tool that
reads foundations one way and end plates another serves those two jobs and no
third; an engine that takes neutral data serves any job a user can prepare the
data for, in their own definition, where the preparation can be seen. The
foundation, beam end plate and design grouping tools, and the rule-based load path
hierarchy, connectivity QA and topology mapping from the retired
Structural-Analysis repo, were removed for exactly that reason.

## What is here

**6DOF Behaviour Classifier** — groups elements by how they behave, from six
degrees of freedom of data on each: member end forces, support reactions,
connection demands, displacements. Six named lists in, one value per element —
a list named Fz can only be Fz, where a row of six could be short or out of order
and still look right. It standardises every column, projects onto the three
directions the data varies along most, and hands the result to `ClusterSelector`
in Unsupervised, which fits k-means, a Gaussian mixture and HDBSCAN and chooses:

| The data is | Chosen | Because |
|---|---|---|
| clean, well separated | **k-means** | the simplest model is the honest one |
| overlapping | **Gaussian mixture** | only it can say an element sits between two behaviours |
| messy, with genuine one-offs | **HDBSCAN** | only it can leave an element unassigned |

Each group comes back with its mean, minimum and maximum in every column, in the
units that arrived — the envelope a group would be designed or checked for. What
happens to elements HDBSCAN leaves unassigned is a choice: leave them, give each a
group of its own (the cautious choice when every element must be designed), or
file them with the nearest group. The report says how far the three models agreed,
as a plain reading of how settled the answer is.

There is deliberately **no log transform**: the gap between two values carries the
meaning. There is deliberately **no envelope, sign rule or split** either — over
load combinations, forces designed either way, parts that may never share a group
— because each is a judgement about the job, and the user makes it upstream.

**Structural Insight Engine** — discovers the structural intent hidden in a
model's geometry. Lines, surfaces and supports in; nothing about what kind of
structure it is. Frames, shells, bridges, stadium bowls, gridshells and
parametric forms go through the same six stages:

| Stage | How | |
|---|---|---|
| Input | lines as end points, surfaces as boundary corners, supports as points; refused with a reason, never repaired | `StructuralInsightEngine` |
| Graph construction | points within the join distance weld into joints; a joint resting along an element joins it; elements become a graph joined where they meet, joints a graph joined along elements | `StructureGraph` |
| Features | centroid, size, extent along each axis, connections, route distance to a support, betweenness centrality, surface or line, aspect ratio | `ElementGeometry`, `InsightFeatures` |
| Unsupervised learning | spectral clustering of the element graph (connectivity), hierarchical clustering of what elements are like wherever they are (geometry), HDBSCAN over everything (QA) | `Unsupervised.MultiViewClustering` |
| Fusion | co-association between every pair of elements, views weighted by agreement, the count the views support over the widest range of thresholds | `Unsupervised.ConsensusClustering` |
| Output | natural groups described by what was measured, each element's agreement, every view's grouping, the features and graph, and QA flags | `StructuralInsightResult` |

Nothing is named. The groups tend to be primary and secondary framing, bracing
systems, diaphragm and shell zones, stiff and flexible regions, repeated modules
and load-path communities, and a group's description — its extent along each
axis, its connections, its distance to a support, how many separate pieces it
falls into — is what lets an engineer say which. A group in many pieces is the
same kind of element recurring apart: a repeated module.

The QA flags are facts, not verdicts: duplicates, degenerate elements, isolated
and disconnected pieces, no route to a support, free ends, single elements a
substantial part of the model hangs on (by how much they strand, so a short spur
is not flagged), near misses, outliers, and elements the views could not agree
on. A free end is a cantilever tip as often as a missed connection.

The features and element graph come back too, so a user can take the grouping
further with the raw Unsupervised Learning components — or, later, train a model
on groups they have corrected. A learned autoencoder view is planned for
DeepLearning, and will join the fusion as one more view.

On a regular frame of 1,705 members the whole engine runs in about two seconds.

## Where the line falls

> **A purpose-built model lives in the toolkit for the domain it has an opinion
> about — never in the layer that owns the algorithm.** The layer owns the
> mechanism; the toolkit owns what the numbers mean.

| | Lives in | Because |
|---|---|---|
| Fit three models and choose; fuse several clusterings; three views over a graph and features | Unsupervised | about the shape of a point cloud and a graph, true of any samples |
| Betweenness, cut vertices, shortest routes | MachineLearning | readings of a graph every paradigm can use |
| Six columns beside each other, three components, no log; welding a stick model, which features describe an element, which view reads which | StructuralDesign | claims about structural data |

## Layout

```
src/OtterLogic.StructuralDesign/   the library, published as a NuGet package
  Insight/                         the Structural Insight Engine
tests/                             xunit; runs anywhere, no Rhino needed
```

Nothing here touches a Rhino or Grasshopper API. The components live in
[Rhino3D](https://github.com/Otter-Logic/Rhino3D), under the **Structural
Design** section.
