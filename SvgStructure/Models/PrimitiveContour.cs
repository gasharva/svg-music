using System.Numerics;

namespace SvgStructure.Models;

/// <summary>
/// Vector contour captured once by PrimitiveResolver in physical SVG coordinates.
/// Later recognition steps must use this geometry instead of reopening the source SVG.
/// </summary>
public sealed record PrimitiveContour(IReadOnlyList<Vector2> Points);
