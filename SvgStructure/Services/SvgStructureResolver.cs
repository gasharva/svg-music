using GlyphGeometry;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Pure resolution pipeline. No files, overlays, reports or diagnostic artifact export live here.
/// </summary>
public sealed class SvgStructureResolver
{
    public const int DefaultSubdivisionsPerBeat = 8;

    private readonly PartMeasureResolver _partMeasureResolver = new();
    private readonly PrimitiveResolver _primitiveResolver = new(1.5);
    private readonly MusicSymbolResolver _musicSymbolResolver = new();
    private readonly LogicalGridResolver _logicalGridResolver = new(DefaultSubdivisionsPerBeat);
    private readonly LedgerLineResolver _ledgerLineResolver = new();
    private readonly NoteHeadResolver _noteHeadResolver = new();
    private readonly StemDetector _stemDetector = new();
    private readonly ArpeggiatoResolver _arpeggiatoResolver = new();
    private readonly BeamResolver _beamResolver = new();
    private readonly ArcResolver _arcResolver = new();
    private readonly ArcAttachmentRefiner _arcAttachmentRefiner = new();
    private readonly DotResolver _dotResolver = new();

    public SvgStructureResolution Resolve(string svgPath, string repositoryRoot, string recognizerWork)
    {
        // One shared, immutable class-mean geometry classifier. Reference descriptors are lazily
        // built once from the embedded References/dataset.zip and then reused by every resolver.
        var glyphClassifier = new GeometryGlyphClassifier(GeometryGlyphClassifier.DefaultPointCount);
        var meterResolver = new MeterResolver(new GeometryNumberRecognizer(glyphClassifier));
        var clefResolver = new ClefResolver(new GeometryClefRecognizer(glyphClassifier));
        var accidentalResolver = new AccidentalResolver(new GeometryAccidentalRecognizer(glyphClassifier));
        var noteFlagResolver = new NoteFlagResolver(new GeometryNoteFlagRecognizer(glyphClassifier));
        var restResolver = new RestResolver(new GeometryRestRecognizer(glyphClassifier));

        var structure = _partMeasureResolver.Resolve(svgPath);
        var primitives = _primitiveResolver.Resolve(structure);
        var musicSymbols = _musicSymbolResolver.Resolve(primitives);

        var meters = structure.Map.Blocks
            .Select(block => meterResolver.Resolve(
                block,
                ThinWavySymbolFilter.ExcludeForMeter(musicSymbols, block)))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();
        var logicalGrid = _logicalGridResolver.Resolve(structure, meters);

        var claimed = new List<RectD>();
        claimed.AddRange(meters.Select(x => x.PhysicalBounds));

        var clefSymbols = RecognitionCandidateFilter.ExcludeClaimed(musicSymbols, claimed);
        var clefs = structure.Map.Blocks
            .SelectMany(block => clefResolver.Resolve(block, clefSymbols, logicalGrid))
            .ToArray();
        claimed.AddRange(clefs.Select(x => x.PhysicalBounds));

        var ledgerPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var ledgerLines = _ledgerLineResolver.Resolve(ledgerPrimitives, logicalGrid);
        foreach (var ledger in ledgerLines)
            if (logicalGrid.TryGetBlock(ledger.PartNumber, ledger.MeasureNumber, out var block))
                claimed.Add(block.ToPhysical(ledger.LogicalBounds));

        var noteHeadPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var noteHeads = _noteHeadResolver.Resolve(noteHeadPrimitives, logicalGrid, clefs, ledgerLines);
        var noteHeadDiagnostics = _noteHeadResolver.LastDiagnostics.ToArray();
        claimed.AddRange(noteHeads.Select(x => x.PhysicalBounds));

        var stemPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var stems = _stemDetector.Resolve(stemPrimitives, logicalGrid, noteHeads);
        claimed.AddRange(stems.Select(x => x.PhysicalBounds));

        var arpeggiatoPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var arpeggiati = _arpeggiatoResolver.Resolve(arpeggiatoPrimitives, logicalGrid, noteHeads);
        var arpeggiatoPrimitiveIds = arpeggiati.SelectMany(x => x.PrimitiveIds).ToHashSet();
        claimed.AddRange(arpeggiatoPrimitives.Primitives
            .Where(x => arpeggiatoPrimitiveIds.Contains(x.Id))
            .Select(x => x.PhysicalBounds));

        var beamPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var beams = _beamResolver.Resolve(beamPrimitives, logicalGrid, stems);
        claimed.AddRange(beams.Select(x => x.PhysicalBounds));

        var flagSymbols = RecognitionCandidateFilter.ExcludeClaimed(musicSymbols, claimed);
        var noteFlags = noteFlagResolver.Resolve(flagSymbols, logicalGrid, stems, beams);
        claimed.AddRange(noteFlags.Select(x => x.PhysicalBounds));

        var arcPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var rawArcs = _arcResolver.Resolve(arcPrimitives, logicalGrid, noteHeads, stems);
        var arcs = _arcAttachmentRefiner.Refine(rawArcs, noteHeads, stems);
        claimed.AddRange(arcs.Select(x => x.PhysicalBounds));

        var accidentalSymbols = RecognitionCandidateFilter.ExcludeClaimed(musicSymbols, claimed);
        var accidentals = accidentalResolver.Resolve(accidentalSymbols, logicalGrid, noteHeads, clefs, meters);
        claimed.AddRange(accidentals.Select(x => x.PhysicalBounds));

        var restSymbols = RecognitionCandidateFilter.ExcludeClaimed(musicSymbols, claimed);
        var rests = restResolver.Resolve(restSymbols, logicalGrid, claimed);
        claimed.AddRange(rests.Select(x => x.PhysicalBounds));

        var dotPrimitives = RecognitionCandidateFilter.ExcludeClaimed(primitives, claimed);
        var dots = _dotResolver.Resolve(dotPrimitives, logicalGrid, noteHeads, rests);

        return new SvgStructureResolution(
            structure,
            primitives,
            musicSymbols,
            meters,
            logicalGrid,
            clefs,
            ledgerLines,
            noteHeads,
            accidentals,
            stems,
            arpeggiati,
            beams,
            noteFlags,
            arcs,
            rests,
            dots,
            noteHeadDiagnostics);
    }
}
