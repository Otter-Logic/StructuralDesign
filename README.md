# StructuralDesign

Tools that act on structural analysis results rather than producing geometry.
Feed them what came out of the analysis, read the answer off.

A domain toolkit, sibling to
[StructuralForm](https://github.com/Otter-Logic/StructuralForm) — which generates
a structure, where this one answers questions about a structure that already
exists.

```
Core  ->  MachineLearning  ->  Unsupervised  ->  StructuralDesign
          (features, PCA)      (the methods)     (this repo: what
                                                  the numbers mean)
```

## What is here

**6DOF Behaviour Classifier** — groups structural members by how they behave,
from the six-degree-of-freedom demand on each one. One input, no settings.

More will follow on the same pattern: deflection surrogates, section sizers,
capacity classifiers. They share the demand-column feature extraction, which is
the thing that makes them siblings.

## The rule this repo exists to demonstrate

> **A purpose-built model lives in the toolkit for the domain it has an opinion
> about — never in the layer that owns the algorithm.** The layer owns the
> mechanism; the toolkit owns what the numbers mean.

The classifier used to be one class doing both. It is now split on exactly that
line:

| | Lives in | Because |
|---|---|---|
| Fit k-means and a mixture across a range of counts, fit HDBSCAN once, score all three, apply the selection rules | `Unsupervised.ClusterSelector` | it is about the shape of a point cloud, which is not a structural question |
| Standardise per degree of freedom, no log transform, three components, the Fx…Mz column contract, centres back in the original units | `StructuralDesign.SixDofBehaviourClassifier` | every one of those is a claim about demand data |

The payoff is concrete: a Fabrication tool grouping panels extracts its own
features — area, corner angles, curvature — and calls the same `ClusterSelector`.
No domain-to-domain reference, and no second copy of the selection logic to keep
in step.

## How the classifier works

**1. Standardise and reduce.** Each degree of freedom is standardised — forces in
kN sit beside moments in kNm with no shared scale — then projected onto three
principal components. Three because demand across six degrees of freedom is
strongly correlated, so the members of a real structure lie close to a
low-dimensional surface inside the six. What is dropped is mostly noise.

There is deliberately **no log transform**. Demands are on a scale where the gap
between two values carries the meaning; a log compresses the large end and
inflates the small one, changing which members look alike for no physical reason.

**2. Hand it to `ClusterSelector`**, which fits all three models, scores them,
and chooses:

| The data is | Chosen | Because |
|---|---|---|
| clean, well separated | **k-means** | the simplest model is the honest one |
| overlapping | **Gaussian mixture** | only it can say a member sits between two behaviours |
| messy, with genuine one-offs | **HDBSCAN** | only it can leave a member unassigned |

**3. Map the centres back** through the whitening, the components and the
standardisation, into the degrees of freedom the analysis produced. That is the
step that makes a group nameable — "group two is the high-torsion family" —
and a group nobody can name is a group nobody will design for.

## Layout

```
src/OtterLogic.StructuralDesign/   the library, published as a NuGet package
tests/                             xunit; runs anywhere, no Rhino needed
```

Nothing here touches a Rhino or Grasshopper API. The component lives in
[Rhino3D](https://github.com/Otter-Logic/Rhino3D), under the **Structural
Design** section.
