namespace SvgSymbols.Services;

/// <summary>
/// Open-set rejection policy for clef recognition.
/// Distances are normalized by the separation between the G and F references so the thresholds
/// remain meaningful if descriptor weights are tuned later.
/// </summary>
public sealed class ClefOpenSetPolicy
{
    public double MaximumNearestToReferenceSeparationRatio { get; init; } = 0.72;
    public double MinimumMarginToReferenceSeparationRatio { get; init; } = 0.20;

    public ClefOpenSetDecision Evaluate(
        double nearestDistance,
        double secondDistance,
        double referenceSeparation)
    {
        if (referenceSeparation <= 1e-12)
            return new ClefOpenSetDecision(false, double.PositiveInfinity, 0, "Reference separation is zero.");

        var nearestRatio = nearestDistance / referenceSeparation;
        var marginRatio = Math.Max(0, secondDistance - nearestDistance) / referenceSeparation;
        var accepted =
            nearestRatio <= MaximumNearestToReferenceSeparationRatio &&
            marginRatio >= MinimumMarginToReferenceSeparationRatio;

        var reason = accepted
            ? null
            : $"Open-set rejection: nearest/ref={nearestRatio:0.###} (max {MaximumNearestToReferenceSeparationRatio:0.###}), " +
              $"margin/ref={marginRatio:0.###} (min {MinimumMarginToReferenceSeparationRatio:0.###}).";

        return new ClefOpenSetDecision(accepted, nearestRatio, marginRatio, reason);
    }
}

public sealed record ClefOpenSetDecision(
    bool Accepted,
    double NearestRatio,
    double MarginRatio,
    string? RejectionReason);
