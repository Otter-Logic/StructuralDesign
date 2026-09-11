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
from the six-degree-of-freedom demand on each one. One input, no settings. A
library class only: its Grasshopper component was removed, and foundation grouping
is where it is used.

**Design Grouping** — the classifier read for design: foundations, connections,
anything designed once per group. It adds the one rule a behaviour grouping does
not have: every element lands in exactly one design group. An element the
classifier leaves unassigned — HDBSCAN's verdict that it fits no family — becomes
a group of its own rather than being filed with its nearest family, because it is
usually the unusually loaded one and grouping it would either inflate that
family's governing forces or under-design it. Behaviour groups come largest
first, then the one-offs. It takes the six forces as six named inputs, so a
force can never land under the wrong name, one value per element. Foundation
grouping, below, is built on it; beam end plate grouping deliberately is not.

**Foundation Grouping** — design grouping with what a foundation knows about its
forces. Only the axial force changes the design with its direction — compression
is a bearing problem, tension an uplift one — so Fz keeps its sign; shear,
bending and torsion are designed either way, so Fx, Fy, Mx, My and Mz are grouped
by size. Read signed, +80 and −80 kN of shear look like the two most different
foundations in the model: on a test set of three families with shear and moments
pointing either way, signed grouping found 7 groups and 38 one-offs where by size
it found the 3. And uplift is a rule, not just a feature: foundations in
tension under any combination are grouped separately from those in compression
throughout, so no group holds both. Which sign is compression is read from the
forces — the sign of the total Fz, which gravity decides — and the report says
which it took.

Over several load combinations each foundation is reduced to its own envelope
first and grouped once, on that: the largest |Fx|, |Fy|, |Mx|, |My| and |Mz|, and
the largest and smallest Fz with their signs — seven values per foundation
however many combinations there are. Grouping under each combination and keeping
the grouping that comes up most often was the alternative, and it answers the
wrong question: it groups by how a foundation usually behaves, where design is
governed by how it behaves at worst, and a support that lifts under one wind
direction in thirty combinations would be voted in with its neighbours. The
envelope comes back on the result, one set per foundation, as the component's
outputs.

**Beam End Plate Grouping** — end plate types for the ends of beams, cut so
that every beam carries a set share of its type's governing forces. The envelope
follows the foundation pattern: each beam's values — every combination, either
or both ends — reduced to Fx with its sign as its largest and smallest value, and
Fy, Fz, Mx, My, Mz by size, which also lets a beam's two ends, equal and
opposite, be given together.

The grouping does not. It first went through the behaviour classifier like the
foundations, and on a real 110-bar model it returned two groups, one of 96 bars
running from 74 to 11,896 in shear and 235 to 12,587 in moment — useless for
design. Standardising every force lets a few kN of noise weigh as much as the
shear that governs, and a statistical cluster says nothing about how far apart
its members' forces may be. An end plate type asks a different question: which
beams can share one plate without wasting it.

So types come from `RatioClustering` in Unsupervised — a complete-linkage tree,
the linkage whose clusters have a bounded spread, over the largest ratio gap in
the forces that govern an end plate: tension, |Fz| and |My|. The mechanism is
generic; which forces govern and where the light level sits are the structural
judgement and stay here. Cut at 1 − efficiency, every beam of a type carries
at least that share of the type's peak in each; compression, minor-axis shear
and moment, and torsion come back in the envelope but split nothing. Anything
under a fifth of the model's largest force (or moment) reads as that light level,
so light connections form light types instead of splitting on differences nobody
designs for. On the 110-bar model, efficiency 0.5 gave 9 types, 0.6 gave 13 with
every bar above 60% and the average near 75–80%, and 0.7 gave 24; 0.6 is the
default. Tension needs no hard rule: a beam with more than light ÷ efficiency of
tension can never share a type with one that has none, by the arithmetic of the
cut.

Two input traps are named rather than grouped on quietly: axial forces as
member-end forces — the same tension as +N at one end and −N at the other — and
beams whose minor-axis shear or moment exceeds the major one they were typed on,
a sign of turned local axes or genuine weak-axis bending.

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

Nothing here touches a Rhino or Grasshopper API. The components live in
[Rhino3D](https://github.com/Otter-Logic/Rhino3D), under the **Structural
Design** section.
