# 6DOF Behaviour Classifier

Groups structural members by how they behave, from the six-degree-of-freedom
demand on each one. Feed it analysis results, read the groups off.

The end product of the OtterLogic clustering stack. It sits above
[MachineLearning](https://github.com/Otter-Logic/MachineLearning), which holds
the algorithms, and it exists so that using them does not require knowing
anything about them.

```
Core  ->  MachineLearning  ->  6DOF Behaviour Classifier
          (K-means, GMM,       (this repo: which one, and why)
           HDBSCAN, PCA)
```

The assembly is `OtterLogic.SixDofBehaviour` because a C# identifier cannot
start with a digit. Everywhere a user sees it, the tool is called **6DOF
Behaviour Classifier**.

## What it does

One call, four steps, no settings.

**1. Standardise and reduce.** Each degree of freedom is standardised — forces
in kN sit beside moments in kNm with no shared scale — then projected onto three
principal components. Three because demand across six degrees of freedom is
strongly correlated, so the members of a real structure lie close to a
low-dimensional surface inside the six. What is dropped is mostly noise.

There is deliberately **no log transform**. Demands are on a scale where the gap
between two values carries the meaning; a log compresses the large end and
inflates the small one, changing which members look alike for no physical
reason.

**2. Fit all three models.** k-means and a Gaussian mixture across a range of
group counts, HDBSCAN once — it is not told how many groups to find, because
finding out is what it does.

**3. Score them.** Silhouette and Davies-Bouldin, plus the share of members each
model leaves on a boundary or in no group at all.

**4. Choose.**

| The data is | Chosen | Because |
|---|---|---|
| clean, well separated | **k-means** | the simplest model is the honest one |
| overlapping | **Gaussian mixture** | only it can say a member sits between two behaviours |
| messy, with genuine one-offs | **HDBSCAN** | only it can leave a member unassigned |

### Why the choice is not a leaderboard

The obvious design — score all three on silhouette and take the winner — does
not work, and it fails in a direction that is easy to miss.

Silhouette and Davies-Bouldin both reward compact, round, well-separated
clusters. That is *exactly what k-means optimises*. Ranking the three on those
metrics is a contest where the referee and one of the players share a definition
of good, and k-means wins almost regardless of the data. Measured on two
interleaved crescents, where HDBSCAN recovers the true grouping exactly
(adjusted Rand 1.000) and k-means fails badly (0.222), the silhouette still
prefers k-means, 0.49 to 0.33.

So each hypothesis is tested by the measure that can actually answer it:

- **messy** — HDBSCAN's noise fraction, the only model that produces one
- **overlapping** — the *share of members* below 0.75 posterior probability in
  the mixture, the only model that assigns softly
- **clean** — silhouette, which is trustworthy on precisely this question, and
  is what is left when neither of the others fires

Two details in there are load-bearing. The overlap test is a tail measure, not a
mean: overlap is a property of the boundary, and even where families genuinely
intermingle most members sit clearly inside one, so the mean stays high — on
data overlapping heavily enough to be indistinguishable it still read 0.87 to
0.98. And the noise fraction has a ceiling as well as a floor: on that same data
HDBSCAN left 47–77% of members unplaced, which is not "found outliers", it is
"found nothing".

## Using it

```csharp
var result = SixDofBehaviourClassifier.Classify(demands);

result.Chosen;      // BehaviourModel.KMeans | GaussianMixture | Hdbscan
result.Rationale;   // one sentence, in the terms the choice was made on
result.Labels;      // group per member, -1 for unassigned
result.Centres;     // group centres back in Fx..Mz
result.Report();    // everything, including how the models that lost scored
```

`demands` is n x 6, one row per member: Fx, Fy, Fz, Mx, My, Mz. Other column
counts work as long as they are consistent.

In Grasshopper it is one component with one required input, under the **6DOF
Behaviour** section. The three raw methods live under **Machine Learning** for
anyone who wants to drive them directly, or reproduce this by hand with their
own choices.

## Working on it

```bash
dotnet test
```

No Rhino and no licence — the classifier is pure numerics, so the tests run on a
hosted CI runner. They build data where the right model is known by
construction, and check it gets picked.
