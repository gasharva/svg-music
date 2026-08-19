import json
from pathlib import Path
import numpy as np
from sklearn.decomposition import PCA
from sklearn.metrics import pairwise_distances


def export_dotnet_model(
    X,
    labels,
    names,
    output_path="glyph-model.json",
    components=4,
    normalization_mode="pca_rotate",
    boundary_samples=512,
    target_radius=0.8,
    sdf_grid_size=32,
    sdf_grid_extent=1.0,
    sdf_clip=0.30,
    sdf_boundary_samples=1024,
    class_threshold_multiplier=1.25,
    ratio_threshold=0.50,
    wrong_distance_threshold_fraction=0.85,
):
    X = np.asarray(X, dtype=np.float64)
    labels = np.asarray(labels)
    names = np.asarray(names)

    k = min(int(components), X.shape[0] - 1, X.shape[1])
    if k < 1:
        raise ValueError("Need at least two training glyphs")

    pca = PCA(n_components=k, whiten=False)
    fingerprints = pca.fit_transform(X)

    distances = pairwise_distances(fingerprints, metric="euclidean")
    np.fill_diagonal(distances, np.nan)

    nearest_same = []
    nearest_wrong = []
    margins = []
    class_same_distances = {}
    class_wrong_distances = {}

    for i in range(len(labels)):
        cls = str(labels[i])
        same = np.where((labels == labels[i]) & (np.arange(len(labels)) != i))[0]
        wrong = np.where(labels != labels[i])[0]

        if len(same):
            ds = float(np.min(distances[i, same]))
            nearest_same.append(ds)
            class_same_distances.setdefault(cls, []).append(ds)
        else:
            ds = None

        if len(wrong):
            dw = float(np.min(distances[i, wrong]))
            nearest_wrong.append(dw)
            class_wrong_distances.setdefault(cls, []).append(dw)
        else:
            dw = None

        if ds is not None and dw is not None:
            margins.append(dw - ds)

    same_p95 = float(np.percentile(nearest_same, 95)) if nearest_same else 0.0
    same_p99 = float(np.percentile(nearest_same, 99)) if nearest_same else same_p95
    wrong_p05 = float(np.percentile(nearest_wrong, 5)) if nearest_wrong else same_p95 * 2
    wrong_p01 = float(np.percentile(nearest_wrong, 1)) if nearest_wrong else wrong_p05

    class_calibration = {}
    for cls in sorted(set(str(x) for x in labels)):
        same_values = class_same_distances.get(cls, [])
        wrong_values = class_wrong_distances.get(cls, [])

        if same_values:
            median = float(np.median(same_values))
            maximum = float(np.max(same_values))
            p95 = float(np.percentile(same_values, 95))
            same_limit = maximum * float(class_threshold_multiplier)
        else:
            # A class with a single prototype cannot estimate its own spread.
            # Fall back to the global same-class p95 so inference still works.
            median = same_p95
            maximum = same_p95
            p95 = same_p95
            same_limit = same_p95 * float(class_threshold_multiplier)

        # A class can be extremely compact in the training corpus (for example several nearly
        # identical copies of one font's G clef). Using only the observed same-class spread then
        # creates an unrealistically tiny open-set radius and rejects legitimate glyphs from a
        # different font even when they are vastly closer to this class than to every other class.
        #
        # Keep the within-class limit, but allow most of the measured gap to the nearest wrong
        # class to be used as an open-set radius. This is intentionally below the wrong-class
        # distance itself; the independent d1/d2 ratio test still guards the ambiguous region
        # where two classes become similarly plausible.
        if wrong_values:
            class_wrong_p05 = float(np.percentile(wrong_values, 5))
        else:
            class_wrong_p05 = wrong_p05

        separation_floor = class_wrong_p05 * float(wrong_distance_threshold_fraction)
        threshold = max(same_limit, separation_floor)

        class_calibration[cls] = {
            "nearestSameMedian": median,
            "nearestSameP95": p95,
            "nearestSameMax": maximum,
            "nearestWrongP05": class_wrong_p05,
            "sameClassDistanceLimit": same_limit,
            "separationFloor": separation_floor,
            "distanceThreshold": threshold,
        }

    model = {
        "version": 2,
        "normalization": {
            "mode": normalization_mode,
            "boundarySamples": int(boundary_samples),
            "targetRadius": float(target_radius),
            "sdfBoundarySamples": int(sdf_boundary_samples),
        },
        "sdf": {
            "gridSize": int(sdf_grid_size),
            "gridExtent": float(sdf_grid_extent),
            "clip": float(sdf_clip),
        },
        "pca": {
            "componentsCount": int(k),
            "explainedVarianceRatio": [float(x) for x in pca.explained_variance_ratio_],
            "mean": [float(x) for x in pca.mean_],
            "components": [[float(x) for x in row] for row in pca.components_],
        },
        "calibration": {
            "nearestSameP95": same_p95,
            "nearestSameP99": same_p99,
            "nearestWrongP05": wrong_p05,
            "nearestWrongP01": wrong_p01,
            "marginP05": float(np.percentile(margins, 5)) if margins else 0.0,
            "classThresholdMultiplier": float(class_threshold_multiplier),
            "wrongDistanceThresholdFraction": float(wrong_distance_threshold_fraction),
            "ratioThreshold": float(ratio_threshold),
            "classes": class_calibration,
        },
        "references": [
            {
                "class": str(labels[i]),
                "source": str(names[i]),
                "fingerprint": [float(x) for x in fingerprints[i]],
            }
            for i in range(len(labels))
        ],
    }

    output_path = Path(output_path)
    output_path.write_text(json.dumps(model, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"Exported: {output_path}")
    print(f"PCA dimensions: {k}")
    print(f"Explained variance: {pca.explained_variance_ratio_.sum():.2%}")
    print(f"References: {len(model['references'])}")
    print("Calibration:")
    print(json.dumps(model["calibration"], indent=2, ensure_ascii=False))

    return model, pca, fingerprints
