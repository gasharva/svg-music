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

    for i in range(len(labels)):
        same = np.where((labels == labels[i]) & (np.arange(len(labels)) != i))[0]
        wrong = np.where(labels != labels[i])[0]
        if len(same):
            ds = float(np.min(distances[i, same]))
            nearest_same.append(ds)
        else:
            ds = None
        if len(wrong):
            dw = float(np.min(distances[i, wrong]))
            nearest_wrong.append(dw)
        else:
            dw = None
        if ds is not None and dw is not None:
            margins.append(dw - ds)

    same_p95 = float(np.percentile(nearest_same, 95)) if nearest_same else 0.0
    same_p99 = float(np.percentile(nearest_same, 99)) if nearest_same else same_p95
    wrong_p05 = float(np.percentile(nearest_wrong, 5)) if nearest_wrong else same_p95 * 2
    wrong_p01 = float(np.percentile(nearest_wrong, 1)) if nearest_wrong else wrong_p05

    model = {
        "version": 1,
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
    print(json.dumps(model["calibration"], indent=2))

    return model, pca, fingerprints
