import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
from sklearn.decomposition import PCA
from sklearn.metrics import pairwise_distances

BENCHMARK_PCA_DIMS = [2, 4, 6, 8, 12, 16, 24, 32]
BENCHMARK_METRIC = "euclidean"


def pair_stats(Dm, labels, short_names):
    same = []
    wrong = []
    margins = []
    top1_ok = []
    worst_rows = []

    n = len(labels)
    for i in range(n):
        same_idx = [j for j in range(n) if j != i and labels[j] == labels[i]]
        wrong_idx = [j for j in range(n) if labels[j] != labels[i]]
        if not same_idx or not wrong_idx:
            continue

        same_distances = Dm[i, same_idx]
        wrong_distances = Dm[i, wrong_idx]

        nearest_same_pos = int(np.argmin(same_distances))
        nearest_wrong_pos = int(np.argmin(wrong_distances))
        nearest_same_idx = same_idx[nearest_same_pos]
        nearest_wrong_idx = wrong_idx[nearest_wrong_pos]

        nearest_same = float(same_distances[nearest_same_pos])
        nearest_wrong = float(wrong_distances[nearest_wrong_pos])
        margin = nearest_wrong - nearest_same

        same.extend(map(float, same_distances))
        wrong.extend(map(float, wrong_distances))
        margins.append(margin)
        top1_ok.append(nearest_same < nearest_wrong)

        worst_rows.append({
            "query": short_names[i],
            "nearest_same": short_names[nearest_same_idx],
            "same_distance": nearest_same,
            "nearest_wrong": short_names[nearest_wrong_idx],
            "wrong_distance": nearest_wrong,
            "margin": margin,
        })

    mean_same = float(np.mean(same))
    mean_wrong = float(np.mean(wrong))

    return {
        "top1_same_class": float(np.mean(top1_ok)),
        "mean_same_distance": mean_same,
        "mean_wrong_distance": mean_wrong,
        "mean_margin": float(np.mean(margins)),
        "min_margin": float(np.min(margins)),
        "separation_ratio": mean_wrong / mean_same if mean_same > 0 else np.inf,
        "same_distances": np.asarray(same),
        "wrong_distances": np.asarray(wrong),
        "margins": np.asarray(margins),
        "worst_rows": pd.DataFrame(worst_rows).sort_values("margin"),
    }


def distance_matrix(descriptor, metric):
    d = pairwise_distances(descriptor, metric=metric)
    np.fill_diagonal(d, np.nan)
    return d


def run_descriptor_benchmark(
    X,
    labels,
    short_names,
    pca_dims=BENCHMARK_PCA_DIMS,
    metric=BENCHMARK_METRIC,
    show_plots=True,
    worst_cases=6,
):
    rows = []
    details = {}

    raw_d = distance_matrix(X, metric)
    raw_stats = pair_stats(raw_d, labels, short_names)
    rows.append({
        "descriptor": f"raw SDF ({X.shape[1]} dims)",
        "family": "raw_sdf",
        "dims": X.shape[1],
        **{k: raw_stats[k] for k in [
            "top1_same_class", "mean_same_distance", "mean_wrong_distance",
            "mean_margin", "min_margin", "separation_ratio"
        ]},
    })
    details["raw SDF"] = {"descriptor": X, "distances": raw_d, "stats": raw_stats, "model": None}

    max_k = min(X.shape[0] - 1, X.shape[1])
    valid_dims = sorted(set(k for k in pca_dims if 1 <= k <= max_k))

    for k in valid_dims:
        for whiten in [False, True]:
            model = PCA(n_components=k, whiten=whiten)
            f = model.fit_transform(X)
            d = distance_matrix(f, metric)
            stats = pair_stats(d, labels, short_names)

            family = "pca_whiten" if whiten else "pca"
            name = f"{'whitened PCA' if whiten else 'PCA'} ({k} dims)"
            rows.append({
                "descriptor": name,
                "family": family,
                "dims": k,
                **{key: stats[key] for key in [
                    "top1_same_class", "mean_same_distance", "mean_wrong_distance",
                    "mean_margin", "min_margin", "separation_ratio"
                ]},
            })
            details[name] = {"descriptor": f, "distances": d, "stats": stats, "model": model}

    result = pd.DataFrame(rows).sort_values(
        ["top1_same_class", "min_margin", "mean_margin", "separation_ratio"],
        ascending=[False, False, False, False],
    ).reset_index(drop=True)

    display(result)

    if show_plots:
        fig, ax = plt.subplots(figsize=(10, 5))
        for family, title in [("pca", "PCA"), ("pca_whiten", "Whitened PCA")]:
            part = result[result.family == family].sort_values("dims")
            if len(part):
                ax.plot(part.dims, part.top1_same_class, marker="o", label=title)
        raw_acc = result.loc[result.family == "raw_sdf", "top1_same_class"].iloc[0]
        ax.axhline(raw_acc, linestyle="--", label=f"Raw SDF ({raw_acc:.2f})")
        ax.set_xlabel("Descriptor dimensions")
        ax.set_ylabel("Top-1 same-class")
        ax.set_ylim(0, 1.05)
        ax.grid(True)
        ax.legend()
        plt.show()

        fig, ax = plt.subplots(figsize=(10, 5))
        for family, title in [("pca", "PCA"), ("pca_whiten", "Whitened PCA")]:
            part = result[result.family == family].sort_values("dims")
            if len(part):
                ax.plot(part.dims, part.mean_margin, marker="o", label=title)
        raw_margin = result.loc[result.family == "raw_sdf", "mean_margin"].iloc[0]
        ax.axhline(raw_margin, linestyle="--", label=f"Raw SDF ({raw_margin:.3f})")
        ax.set_xlabel("Descriptor dimensions")
        ax.set_ylabel("Mean margin: nearest wrong - nearest same")
        ax.grid(True)
        ax.legend()
        plt.show()

        compare = ["raw SDF", "PCA (8 dims)", "whitened PCA (8 dims)"]
        for name in compare:
            if name not in details:
                continue
            stats = details[name]["stats"]
            plt.figure(figsize=(8, 4))
            plt.hist(stats["same_distances"], bins=20, alpha=0.55, label="same class")
            plt.hist(stats["wrong_distances"], bins=20, alpha=0.55, label="different class")
            plt.title(name)
            plt.xlabel("Distance")
            plt.ylabel("Pair count")
            plt.legend()
            plt.grid(True)
            plt.show()

    print("\nWorst cases for top descriptors")
    for row in result.head(5).itertuples(index=False):
        key = "raw SDF" if row.family == "raw_sdf" else row.descriptor
        print("\n" + "=" * 100)
        print(row.descriptor)
        print(
            f"top1={row.top1_same_class:.3f} | mean margin={row.mean_margin:.4f} | "
            f"min margin={row.min_margin:.4f} | separation={row.separation_ratio:.3f}"
        )
        display(details[key]["stats"]["worst_rows"].head(worst_cases).reset_index(drop=True))

    return result, details
