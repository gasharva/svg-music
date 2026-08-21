namespace MusicStructure;

/// <summary>
/// Backward-compatible facade. New code should use MusicScoreBuilder directly.
/// </summary>
public sealed class NoteBuilder
{
    private readonly MusicScoreBuilder _scoreBuilder = new();

    public MusicScore Build(MusicStructureInput input) => _scoreBuilder.Build(input);
}
