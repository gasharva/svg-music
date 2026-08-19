using System.Text.Json;
using GlyphPcaGallery.Services;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

public sealed record StepByStepBatchResult(string InputFolder,string ArtifactsFolder,string HtmlReportPath,string MarkdownReportPath,IReadOnlyList<StepByStepItemResult> Items);
public sealed record StepByStepItemResult(string FileName,string ArtifactDirectoryName,int LineCount,int SystemCount,int PartCount,int MeasureCount,int PartMeasurePrimitiveCount=0,int MeasurePrimitiveCount=0,int PhysicalOnlyPrimitiveCount=0,int MusicSymbolCount=0,int MeterCount=0,int ClefCount=0,int LedgerLineCount=0,int NoteHeadCount=0,int NoteHeadCandidateCount=0,int AccidentalCount=0,int RestCount=0,int ExportedPrimitiveCount=0,int SourceElementCount=0,int SourceUseCount=0,string? Error=null);

public sealed class StepByStepBatchRunner
{
    public const string ArtifactsDirectoryName = "_artifacts";
    public const int DefaultSubdivisionsPerBeat = 8;
    private readonly PartMeasureResolver _partMeasureResolver=new(); private readonly PrimitiveResolver _primitiveResolver=new(1.5); private readonly PrimitiveSvgExporter _primitiveSvgExporter=new(); private readonly MusicSymbolResolver _musicSymbolResolver=new(); private readonly MusicSymbolSvgExporter _musicSymbolSvgExporter=new(); private readonly SvgSourceModelDumper _sourceModelDumper=new(); private readonly LogicalGridResolver _logicalGridResolver=new(DefaultSubdivisionsPerBeat); private readonly LedgerLineResolver _ledgerLineResolver=new(); private readonly NoteHeadResolver _noteHeadResolver=new(); private readonly StemDetector _stemDetector=new(); private readonly BeamResolver _beamResolver=new(); private readonly ArcResolver _arcResolver=new(); private readonly ArcDiagnosticExporter _arcDiagnosticExporter=new(); private readonly NoteHeadDiagnosticExporter _noteHeadDiagnosticExporter=new(); private readonly NoteFlagDiagnosticExporter _noteFlagDiagnosticExporter=new(); private readonly RestDiagnosticExporter _restDiagnosticExporter=new(); private readonly PartMeasureOverlayRenderer _partMeasureOverlayRenderer=new(); private readonly PrimitiveOverlayRenderer _primitiveOverlayRenderer=new(); private readonly MeterOverlayRenderer _meterOverlayRenderer=new(); private readonly NoteFlagOverlayRenderer _noteFlagOverlayRenderer=new(); private readonly RestOverlayRenderer _restOverlayRenderer=new(); private readonly StepByStepReportBuilder _reportBuilder=new();

    public StepByStepBatchResult Run(string inputFolder)
    {
        inputFolder=Path.GetFullPath(inputFolder); var artifactsFolder=Path.Combine(inputFolder,ArtifactsDirectoryName); if(Directory.Exists(artifactsFolder))Directory.Delete(artifactsFolder,true); Directory.CreateDirectory(artifactsFolder);
        var repositoryRoot=FindRepositoryRoot(inputFolder); var recognizerWork=Path.Combine(Path.GetTempPath(),$"svg-music-recognizers-{Guid.NewGuid():N}"); var glyphs=Path.Combine(repositoryRoot,"References","glyphs"); var glyphModelBundlePath=Path.Combine(repositoryRoot,"GlyphPcaGallery","glyph-models.zip");
        var glyphModelBundle=GlyphModelBundleLoader.Load(glyphModelBundlePath);
        var baseNumberRecognizer=new GlyphPcaNumberRecognizer(glyphModelBundle,Path.Combine(recognizerWork,"meter-pca"),minimumConfidence:0.20); var diagnosticNumberRecognizer=new DiagnosticNumberRecognizer(baseNumberRecognizer); var meterResolver=new MeterResolver(diagnosticNumberRecognizer);
        var baseClefRecognizer=new GlyphPcaClefRecognizer(glyphModelBundle,Path.Combine(recognizerWork,"clef-pca")); var legacyIoUClefAnalyzer=new LegacyIoUClefAnalyzer(glyphs); var diagnosticClefRecognizer=new DiagnosticClefRecognizer(baseClefRecognizer,legacyIoUClefAnalyzer); var clefResolver=new ClefResolver(diagnosticClefRecognizer);
        var accidentalRecognizer=new GlyphPcaAccidentalRecognizer(glyphModelBundle,Path.Combine(recognizerWork,"accidental-pca")); var accidentalResolver=new AccidentalResolver(accidentalRecognizer);
        var noteFlagRecognizer=new GlyphPcaNoteFlagRecognizer(glyphModelBundle,Path.Combine(recognizerWork,"flag-pca")); var noteFlagResolver=new NoteFlagResolver(noteFlagRecognizer);
        var restRecognizer=new GlyphPcaRestRecognizer(glyphModelBundle,Path.Combine(recognizerWork,"rest-pca")); var restResolver=new RestResolver(restRecognizer);
        try { var svgFiles=Directory.EnumerateFiles(inputFolder,"*.svg",SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray(); var items=new List<StepByStepItemResult>(); foreach(var svgPath in svgFiles) items.Add(Process(svgPath,artifactsFolder,meterResolver,clefResolver,accidentalResolver,noteFlagResolver,restResolver,diagnosticNumberRecognizer,diagnosticClefRecognizer)); var htmlReportPath=Path.Combine(artifactsFolder,"index.html"); var markdownReportPath=Path.Combine(artifactsFolder,"README.md"); _reportBuilder.WriteHtml(htmlReportPath,items); _reportBuilder.WriteMarkdown(markdownReportPath,items); return new(inputFolder,artifactsFolder,htmlReportPath,markdownReportPath,items); }
        finally { try { if(Directory.Exists(recognizerWork))Directory.Delete(recognizerWork,true); } catch{} }
    }

    private StepByStepItemResult Process(string svgPath,string artifactsFolder,MeterResolver meterResolver,ClefResolver clefResolver,AccidentalResolver accidentalResolver,NoteFlagResolver noteFlagResolver,RestResolver restResolver,DiagnosticNumberRecognizer diagnosticNumberRecognizer,DiagnosticClefRecognizer diagnosticClefRecognizer)
    {
        var fileName=Path.GetFileName(svgPath); var stem=Path.GetFileNameWithoutExtension(svgPath); var itemDirectory=Path.Combine(artifactsFolder,stem); Directory.CreateDirectory(itemDirectory);
        try
        {
            File.Copy(svgPath,Path.Combine(itemDirectory,"source.svg"),true);
            var sourceModel=_sourceModelDumper.Dump(svgPath,itemDirectory);
            var structure=_partMeasureResolver.Resolve(svgPath);
            var primitives=_primitiveResolver.Resolve(structure);
            var primitiveExport=_primitiveSvgExporter.Export(primitives,itemDirectory);
            var musicSymbols=_musicSymbolResolver.Resolve(primitives);
            _musicSymbolSvgExporter.Export(musicSymbols,itemDirectory);

            diagnosticNumberRecognizer.BeginDocument(Path.Combine(itemDirectory,"meter-inputs"));
            var meters=structure.Map.Blocks
                .Select(block=>meterResolver.Resolve(block,musicSymbols))
                .Where(x=>x is not null)
                .Select(x=>x!)
                .ToArray();
            var logicalGrid=_logicalGridResolver.Resolve(structure,meters);

            var claimed=new List<RectD>();
            claimed.AddRange(meters.Select(x=>x.PhysicalBounds));

            diagnosticClefRecognizer.BeginDocument(Path.Combine(itemDirectory,"clef-inputs"));
            var clefSymbols=RecognitionCandidateFilter.ExcludeClaimed(musicSymbols,claimed);
            var clefs=structure.Map.Blocks
                .SelectMany(block=>clefResolver.Resolve(block,clefSymbols,logicalGrid))
                .ToArray();
            claimed.AddRange(clefs.Select(x=>x.PhysicalBounds));

            var ledgerPrimitives=RecognitionCandidateFilter.ExcludeClaimed(primitives,claimed);
            var ledgerLines=_ledgerLineResolver.Resolve(ledgerPrimitives,logicalGrid);

            var noteHeadPrimitives=RecognitionCandidateFilter.ExcludeClaimed(primitives,claimed);
            var noteHeads=_noteHeadResolver.Resolve(noteHeadPrimitives,logicalGrid,clefs,ledgerLines);
            var noteHeadDiagnostics=_noteHeadResolver.LastDiagnostics;
            _noteHeadDiagnosticExporter.Export(noteHeadDiagnostics,Path.Combine(itemDirectory,"notehead-inputs"));
            claimed.AddRange(noteHeads.Select(x=>x.PhysicalBounds));

            var stemPrimitives=RecognitionCandidateFilter.ExcludeClaimed(primitives,claimed);
            var stems=_stemDetector.Resolve(stemPrimitives,logicalGrid,noteHeads);
            claimed.AddRange(stems.Select(x=>x.PhysicalBounds));

            var beamPrimitives=RecognitionCandidateFilter.ExcludeClaimed(primitives,claimed);
            var beams=_beamResolver.Resolve(beamPrimitives,logicalGrid,stems);
            claimed.AddRange(beams.Select(x=>x.PhysicalBounds));

            var flagSymbols=RecognitionCandidateFilter.ExcludeClaimed(musicSymbols,claimed);
            var noteFlags=noteFlagResolver.Resolve(flagSymbols,logicalGrid,stems,beams);
            _noteFlagDiagnosticExporter.Export(noteFlagResolver.LastDiagnostics,Path.Combine(itemDirectory,"flag-inputs"));
            claimed.AddRange(noteFlags.Select(x=>x.PhysicalBounds));

            var arcPrimitives=RecognitionCandidateFilter.ExcludeClaimed(primitives,claimed);
            var arcs=_arcResolver.Resolve(arcPrimitives,logicalGrid,noteHeads,stems);
            _arcDiagnosticExporter.Export(arcPrimitives,_arcResolver.LastDiagnostics,Path.Combine(itemDirectory,"arc-inputs"));
            claimed.AddRange(arcs.Select(x=>x.PhysicalBounds));

            var accidentalSymbols=RecognitionCandidateFilter.ExcludeClaimed(musicSymbols,claimed);
            var accidentals=accidentalResolver.Resolve(accidentalSymbols,logicalGrid,noteHeads,clefs,meters);
            claimed.AddRange(accidentals.Select(x=>x.PhysicalBounds));

            var restSymbols=RecognitionCandidateFilter.ExcludeClaimed(musicSymbols,claimed);
            var rests=restResolver.Resolve(restSymbols,logicalGrid,claimed);
            _restDiagnosticExporter.Export(restResolver.LastDiagnostics,Path.Combine(itemDirectory,"rest-inputs"));

            _partMeasureOverlayRenderer.Render(structure,Path.Combine(itemDirectory,"measures.png"));
            _primitiveOverlayRenderer.Render(primitives,Path.Combine(itemDirectory,"classified.png"));
            var metersPath=Path.Combine(itemDirectory,"meters.png");
            _meterOverlayRenderer.Render(structure,meters,clefs,ledgerLines,noteHeads,accidentals,stems,beams,arcs,logicalGrid,metersPath);
            _noteFlagOverlayRenderer.Render(structure,noteFlags,metersPath);
            _restOverlayRenderer.Render(structure,rests,metersPath);

            WriteResolutionJson(Path.Combine(itemDirectory,"structure.json"),fileName,structure,primitives,musicSymbols,meters,logicalGrid,clefs,ledgerLines,noteHeads,accidentals,stems,beams,noteFlags,arcs,rests);
            return new(fileName,stem,structure.LineCount,structure.SystemCount,structure.Parts.Count,structure.Measures.Count,primitives.PartMeasurePrimitives.Count,primitives.MeasurePrimitives.Count,primitives.PhysicalOnlyPrimitives.Count,musicSymbols.Candidates.Count,meters.Length,clefs.Length,ledgerLines.Count,noteHeads.Count,noteHeadDiagnostics.Count,accidentals.Count,rests.Count,primitiveExport.Items.Count,sourceModel.ElementCount,sourceModel.UseCount);
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(itemDirectory,"error.txt"),ex.ToString());
            return new(fileName,stem,0,0,0,0,Error:ex.Message);
        }
    }

    private static void WriteResolutionJson(string path,string fileName,PartMeasureResolution structure,PrimitiveResolution primitives,MusicSymbolResolution musicSymbols,IReadOnlyList<MeterResolution> meters,LogicalGridResolution logicalGrid,IReadOnlyList<ClefResolution> clefs,IReadOnlyList<LedgerLineResolution> ledgerLines,IReadOnlyList<NoteHeadResolution> noteHeads,IReadOnlyList<AccidentalResolution> accidentals,IReadOnlyList<StemResolution> stems,IReadOnlyList<BeamResolution> beams,IReadOnlyList<NoteFlagResolution> noteFlags,IReadOnlyList<ArcResolution> arcs,IReadOnlyList<RestResolution> rests)
    {
        var payload=new { source=fileName, structure=new { lineCount=structure.LineCount,systemCount=structure.SystemCount,pageBounds=structure.Map.PageBounds,parts=structure.Parts,measures=structure.Measures,blocks=structure.Map.Blocks }, primitives=primitives.Primitives.Select(x=>new{x.Id,x.Scope,x.PartNumber,x.MeasureNumber,x.PhysicalBounds,source=new{x.Source.Anchor,x.Source.GroupAnchor,x.Source.ReferenceAnchor,x.Source.InstanceX,x.Source.InstanceY,x.Source.ElementType,x.Source.ElementId,x.Source.ElementAddress,x.Source.IsExplicitUse,groupContourCount=x.SourceGroupContours?.Count},contourPointCount=x.Contour.Points.Count}), musicSymbols=musicSymbols.Candidates.Select(x=>new{x.Id,x.ParentCandidateId,x.IsDerived,x.Scope,x.PartNumber,x.MeasureNumber,x.PhysicalBounds,x.PrimitiveIds,smoothPathCount=x.SmoothPaths.Count,sourceAddresses=x.Sources.Select(s=>s.ElementAddress??s.Anchor).ToArray()}), meters, logicalGrid=new{subdivisionsPerBeat=DefaultSubdivisionsPerBeat,blocks=logicalGrid.Blocks.Select(x=>new{x.PartNumber,x.MeasureNumber,x.BeatNumber,x.BeatValue,x.SubdivisionsPerBeat,x.HorizontalUnits,x.HalfStaffSpace,x.PhysicalBounds})}, clefs,ledgerLines,noteHeads,accidentals,stems,beams,noteFlags,arcs,rests };
        File.WriteAllText(path,JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
    }

    private static string FindRepositoryRoot(string start) { var current=new DirectoryInfo(start); while(current is not null){if(File.Exists(Path.Combine(current.FullName,"SvgToMusicXmlPoc.sln")))return current.FullName; current=current.Parent;} throw new DirectoryNotFoundException("Could not find repository root above input folder."); }
}
