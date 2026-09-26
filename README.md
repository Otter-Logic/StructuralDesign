# StructuralDesign

Multipurpose tools for structural engineering — before an analysis and after one.
Every structural tool in OtterLogic lives here.

A domain toolkit, sibling to
[StructuralForm](https://github.com/Otter-Logic/StructuralForm), which generates
a structure; this one answers questions about a structure that already exists.

```
Core  ->  MachineLearning  ->  Unsupervised  ->  StructuralEngine  ->  StructuralDesign
          (features, PCA,      (the methods,     (reading a model:     (this repo: what
           graphs)              and fusion)       joints, members,      structural data
                                                  assemblies, loads)    means)
```

How a model is read — points welded into joints, lines chained into members,
members triangulated into assemblies, weight drained to the supports — lives one
layer down in [StructuralEngine](https://github.com/Otter-Logic/StructuralEngine),
because [Construction](https://github.com/Otter-Logic/Construction) and
[Fabrication](https://github.com/Otter-Logic/Fabrication) read the model the same
way. This repo holds what the reading *means* to a structural engineer.

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

**Section Groups** (`SectionGrouping`) — groups a steel frame's members into the
sections they can share, from its geometry alone, before any analysis. Lines and
supports in; a section group for every line out, with the pieces behind each
group and the length each has to be designed over. It was the Structural Insight
Engine until 2026-09-26; the reading and the role clustering are that engine's,
and the question is narrower — "which members can share a section?" rather than
"what natural groups are there?" — so QA flags went to Geometry QA and levels,
features and the graph to Describe Member.

| Step | How | |
|---|---|---|
| Read | points welded into joints, lines that carry straight on chained into runs, triangles into bodies, every line's weight drained to the supports | `StructuralEngine.ModelReading` |
| Cut into pieces | each run cut where it hands a substantial share of its weight on part of the way along — to the ground, to another body, or to a member carrying on through the joint — so a beam line over five columns is five beams, a column stack stays whole and a truss is not cut by its own webs | `StructuralEngine.Pieces` |
| Role | runs sorted into role families on how they carry load and nothing else: how upright, how straight, their level, whether in a triangulated body and where in it, and whether others hand load onto them part of the way along; geometry, role and density views fused, connectivity off by default | `RoleFeatures`, `Unsupervised.MultiViewClustering` |
| Size | each family split at its widest gap into the fewest groups within 1.3x in design length, 2x in flow and a quarter in uprightness; or into a fixed number of sections | `SizeBands` |
| Output | per line its group and confidence; per group its pieces, design length, span, flow and level ranges; a report ending in every assumption | `SectionGroupingResult` |

**Assumed, and said.** A steel frame drawn as centrelines, lines only; simple
connections, so a beam is a piece from bearing to bearing; a straight column
stack is one piece, with no splices inferred; gravity down -Z; and flow — the
share of the frame's own weight passing along a piece — as a proxy for demand,
not a force. `SectionGrouping.Assumptions` holds the list and every report ends
with it. After analysis, the 6DOF Behaviour Classifier groups by force.

**Role carries no position.** Run length, connection counts, centrality and
support distance described the old engine's members; on a six-storey frame of
identical bays they split the beams six ways, into groups of the same length and
flow. How long a piece is and how much it carries is the size step's question,
not the role's. Nothing refers to x, y or position in plan: turned about the
vertical, a frame falls into the same groups — that is tested.

**Design length.** A standing piece is sized over its longest unbraced stretch, a
lying one over its span, and a raking one in proportion — the same split between
axis and bending the load path makes.

**Known soft spot.** A beam triangulated with the top of a column — the top storey
of a braced bay — is one body with it and is not cut there.

The QA flags are facts, not verdicts: duplicates, degenerate elements, isolated
and disconnected pieces, no route to a support, free ends, single elements a
substantial part of the model hangs on (by how much they strand, so a short spur
is not flagged), near misses, outliers, and elements the views could not agree
on. A free end is a cantilever tip as often as a missed connection.

The features and element graph come back too, so a user can take the grouping
further with the raw Unsupervised Learning components — or, later, train a model
on groups they have corrected. A learned autoencoder view is planned for
DeepLearning, and will join the fusion as one more view; the per-member features,
levels and assemblies here are what it will be trained on top of, so that it
learns judgement rather than having to rediscover gravity.

On a regular frame of 1,705 members the whole engine runs in about two seconds.

## Where the line falls

> **A purpose-built model lives in the toolkit for the domain it has an opinion
> about — never in the layer that owns the algorithm.** The layer owns the
> mechanism; the toolkit owns what the numbers mean.

| | Lives in | Because |
|---|---|---|
| Fit three models and choose; fuse several clusterings; three views over a graph and features | Unsupervised | about the shape of a point cloud and a graph, true of any samples |
| Betweenness, cut vertices, shortest routes | Graphs | readings of a graph anything can use, learning or not |
| Welding a stick model, chaining members, finding bodies, draining weight to the ground | StructuralEngine | true of any structure, and needed by every structural toolkit |
| Six columns beside each other, three components, no log; which features describe an element, which view reads which | StructuralDesign | claims about what structural data means |

## Layout

```
src/OtterLogic.StructuralDesign/   the library, published as a NuGet package
  SectionGrouping/                 Section Groups
  Grids/                           grid and level inference
  QA/                              geometry QA
tests/                             xunit; runs anywhere, no Rhino needed
```

Joint Signature and Connection Typology, which once lived here for want of a
shared reading, now live in Fabrication; the erection sequence in Construction.

Nothing here touches a Rhino or Grasshopper API. The components live in
[Rhino3D](https://github.com/Otter-Logic/Rhino3D), under the **Structural
Design** section.
