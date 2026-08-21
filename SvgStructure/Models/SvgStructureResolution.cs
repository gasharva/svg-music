namespace SvgStructure.Models;

public sealed record SvgStructureResolution(
    PartMeasureResolution Structure,
    PrimitiveResolution Primitives,
    MusicSymbolResolution MusicSymbols,
    IReadOnlyList<MeterResolution> Meters,
    LogicalGridResolution LogicalGrid,
    IReadOnlyList<ClefResolution> Clefs,
    IReadOnlyList<LedgerLineResolution> LedgerLines,
    IReadOnlyList<NoteHeadResolution> NoteHeads,
    IReadOnlyList<AccidentalResolution> Accidentals,
    IReadOnlyList<StemResolution> Stems,
    IReadOnlyList<ArpeggiatoResolution> Arpeggiati,
    IReadOnlyList<BeamResolution> Beams,
    IReadOnlyList<NoteFlagResolution> NoteFlags,
    IReadOnlyList<ArcResolution> Arcs,
    IReadOnlyList<RestResolution> Rests,
    IReadOnlyList<DotResolution> Dots,
    IReadOnlyList<NoteHeadDiagnosticEntry> NoteHeadDiagnostics);
