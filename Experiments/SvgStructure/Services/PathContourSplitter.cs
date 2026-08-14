using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Splits a rendered SVG path into independent contours.
/// A new MoveTo starts a new contour; standalone Skia shape commands are emitted separately.
/// </summary>
public sealed class PathContourSplitter
{
    public IReadOnlyList<Shim.SKPath> Split(Shim.SKPath path)
    {
        var result = new List<Shim.SKPath>();
        Shim.SKPath? current = null;

        foreach (var command in path)
        {
            if (IsStandaloneShape(command))
            {
                FlushCurrent(result, ref current);

                var standalone = CreatePath(path);
                standalone.Add(command.DeepClone());
                result.Add(standalone);
                continue;
            }

            if (command is Shim.MoveToPathCommand)
            {
                FlushCurrent(result, ref current);
                current = CreatePath(path);
            }

            current ??= CreatePath(path);
            current.Add(command.DeepClone());
        }

        FlushCurrent(result, ref current);
        return result;
    }

    private static bool IsStandaloneShape(Shim.PathCommand command) => command is
        Shim.AddCirclePathCommand or
        Shim.AddOvalPathCommand or
        Shim.AddPolyPathCommand or
        Shim.AddRectPathCommand or
        Shim.AddRoundRectPathCommand;

    private static Shim.SKPath CreatePath(Shim.SKPath source) => new()
    {
        FillType = source.FillType
    };

    private static void FlushCurrent(ICollection<Shim.SKPath> result, ref Shim.SKPath? current)
    {
        if (current is { Count: > 0 })
            result.Add(current);

        current = null;
    }
}
