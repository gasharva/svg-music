using System.Text.Json;
using GlyphPcaGallery.Services;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

public sealed record StepByStepBatchResult(string InputFolder,string ArtifactsFolder,string HtmlReportPath,string MarkdownReportPath,IReadOnlyList<StepByStepItemResult> Items);
public sealed record StepByStepItemResult(string FileName,string ArtifactDirectoryName,int LineCount,int SystemCount,int PartCount,int MeasureCount,int PartMeasurePrimitiveCount=0,int MeasurePrimitiveCount=0,int PhysicalOnlyPrimitiveCount=0,int MusicSymbolCount=0,int MeterCount=0,int ClefCount=0,int LedgerLineCount=0,int NoteHeadCount=0,int NoteHeadCandidateCount=0,int AccidentalCount=0,int RestCount=0,int ExportedPrimitiveCount=0,int SourceElementCount=0,int SourceUseCount=0,ReferenceValidationResult? ReferenceValidation=null,string? Error=null);

public sealed class StepByStepBatchRunner
{
    public const string ArtifactsDirectoryName = "_artifacts";
    public const int DefaultSubdivisionsPerBeat = SvgStructureResolver.DefaultSubdivisionsPerBeat;
    private readonly SvgStructureResolver _resolver=new(); private readonly PrimitiveSvgExporter _primitiveSvgExporter=new(); private readonly MusicSymbolSvgExporter _musicSymbolSvgExporter=new(); private readonly SvgSourceModelDumper _sourceModelDumper=new(); private readonly ArcDiagnosticExporter _arcDiagnosticExporter=new(); private readonly NoteHeadDiagnosticExporter _noteHeadDiagnosticExporter=new(); private readonly NoteFlagDiagnosticExporter _noteFlagDiagnosticExporter=new(); private readonly RestDiagnosticExporter _restDiagnosticExporter=new(); private readonly PartMeasureOverlayRenderer _partMeasureOverlayRenderer=new(); private readonly PrimitiveOverlayRenderer _primitiveOverlayRenderer=new(); private readonly MeterOverlayRenderer _meterOverlayRenderer=new(); private readonly ArpeggiatoOverlayRenderer _arpeggiatoOverlayRenderer=new(); private readonly NoteFlagOverlayRenderer _noteFlagOverlayRenderer=new(); private readonly RestOverlayRenderer _restOverlayRenderer=new(); private readonly DotOverlayRenderer _dotOverlayRenderer=new(); private readonly StepByStepReportBuilder _reportBuilder=new();

    public StepByStepBatchResult Run(string inputFolder)
    {
        inputFolder=Path.GetFullPath(inputFolder); var artifactsFolder=Path.Combine(inputFolder,ArtifactsDirectoryName); if(Directory.Exists(artifactsFolder))Directory.Delete(artifactsFolder,true); Directory.CreateDirectory(artifactsFolder);
        var repositoryRoot=FindRepositoryRoot(inputFolder); var recognizerWork=Path.Combine(Path.GetTempPath(),$"svg-music-recognizers-{Guid.NewGuid():N}");
        try { var svgFiles=Directory.EnumerateFiles(inputFolder,"*.svg",SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray(); var items=new List<StepByStepItemResult>(); foreach(var svgPath in svgFiles) items.Add(Process(svgPath,artifactsFolder,repositoryRoot,recognizerWork)); var htmlReportPath=Path.Combine(artifactsFolder,"index.html"); var markdownReportPath=Path.Combine(artifactsFolder,"README.md"); _reportBuilder.WriteHtml(htmlReportPath,items); _reportBuilder.WriteMarkdown(markdownReportPath,items); return new(inputFolder,artifactsFolder,htmlReportPath,markdownReportPath,items); }
        finally { try { if(Directory.Exists(recognizerWork))Directory.Delete(recognizerWork,true); } catch{} }
    }

    private StepByStepItemResult Process(string svgPath,string artifactsFolder,string repositoryRoot,string recognizerWork)
    {
        var fileName=Path.GetFileName(svgPath); var stem=Path.GetFileNameWithoutExtension(svgPath); var itemDirectory=Path.Combine(artifactsFolder,stem); Directory.CreateDirectory(itemDirectory);
        try
        {
            File.Copy(svgPath,Path.Combine(itemDirectory,"source.svg"),true);
            var sourceModel=_sourceModelDumper.Dump(svgPath,itemDirectory);
            var resolved=_resolver.Resolve(svgPath,repositoryRoot,recognizerWork);
            var structure=resolved.Structure; var primitives=resolved.Primitives; var musicSymbols=resolved.MusicSymbols; var meters=resolved.Meters; var logicalGrid=resolved.LogicalGrid; var clefs=resolved.Clefs; var ledgerLines=resolved.LedgerLines; var noteHeads=resolved.NoteHeads; var accidentals=resolved.Accidentals; var stems=resolved.Stems; var arpeggiati=resolved.Arpeggiati; var beams=resolved.Beams; var noteFlags=resolved.NoteFlags; var arcs=resolved.Arcs; var rests=resolved.Rests; var dots=resolved.Dots;

            var primitiveExport=_primitiveSvgExporter.Export(primitives,itemDirectory);
            _musicSymbolSvgExporter.Export(musicSymbols,itemDirectory);
            _noteHeadDiagnosticExporter.Export(resolved.NoteHeadDiagnostics,Path.Combine(itemDirectory,"notehead-inputs"));

            _partMeasureOverlayRenderer.Render(structure,Path.Combine(itemDirectory,"measures.png"));
            _primitiveOverlayRenderer.Render(primitives,Path.Combine(itemDirectory,"classified.png"));
            var metersPath=Path.Combine(itemDirectory,"meters.png");
            _meterOverlayRenderer.Render(structure,meters,clefs,ledgerLines,noteHeads,accidentals,stems,beams,arcs,logicalGrid,metersPath);
            _arpeggiatoOverlayRenderer.Render(structure,arpeggiati,metersPath);
            _noteFlagOverlayRenderer.Render(structure,noteFlags,metersPath);
            _restOverlayRenderer.Render(structure,rests,metersPath);
            _dotOverlayRenderer.Render(structure,dots,metersPath);

            WriteResolutionJson(Path.Combine(itemDirectory,"structure.json"),fileName,structure,primitives,musicSymbols,meters,logicalGrid,clefs,ledgerLines,noteHeads,accidentals,stems,arpeggiati,beams,noteFlags,arcs,rests,dots);
            var referenceValidation=ReferenceValidationWithNotes.Run(svgPath,itemDirectory,resolved);
            return new(fileName,stem,structure.LineCount,structure.SystemCount,structure.Parts.Count,structure.Measures.Count,primitives.PartMeasurePrimitives.Count,primitives.MeasurePrimitives.Count,primitives.PhysicalOnlyPrimitives.Count,musicSymbols.Candidates.Count,meters.Count,clefs.Count,ledgerLines.Count,noteHeads.Count,resolved.NoteHeadDiagnostics.Count,accidentals.Count,rests.Count,primitiveExport.Items.Count,sourceModel.ElementCount,sourceModel.UseCount,referenceValidation);
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(itemDirectory,"error.txt"),ex.ToString());
            return new(fileName,stem,0,0,0,0,Error:ex.Message);
        }
    }

    private static void WriteResolutionJson(string path,string fileName,PartMeasureResolution structure,PrimitiveResolution primitives,MusicSymbolResolution musicSymbols,IReadOnlyList<MeterResolution> meters,LogicalGridResolution logicalGrid,IReadOnlyList<ClefResolution> clefs,IReadOnlyList<LedgerLineResolution> ledgerLines,IReadOnlyList<NoteHeadResolution> noteHeads,IReadOnlyList<AccidentalResolution> accidentals,IReadOnlyList<StemResolution> stems,IReadOnlyList<ArpeggiatoResolution> arpeggiati,IReadOnlyList<BeamResolution> beams,IReadOnlyList<NoteFlagResolution> noteFlags,IReadOnlyList<ArcResolution> arcs,IReadOnlyList<RestResolution> rests,IReadOnlyList<DotResolution> dots)
    {
        var payload=new { source=fileName, structure=new { lineCount=structure.LineCount,systemCount=structure.SystemCount,pageBounds=structure.Map.PageBounds,parts=structure.Parts,measures=structure.Measures,blocks=structure.Map.Blocks }, primitives=primitives.Primitives.Select(x=>new{x.Id,x.Scope,x.PartNumber,x.MeasureNumber,x.PhysicalBounds,source=new{x.Source.Anchor,x.Source.GroupAnchor,x.Source.ReferenceAnchor,x.Source.InstanceX,x.Source.InstanceY,x.Source.ElementType,x.Source.ElementId,x.Source.ElementAddress,x.Source.IsExplicitUse,groupContourCount=x.SourceGroupContours?.Count},contourPointCount=x.Contour.Points.Count}), musicSymbols=musicSymbols.Candidates.Select(x=>new{x.Id,x.ParentCandidateId,x.IsDerived,x.Scope,x.PartNumber,x.MeasureNumber,x.PhysicalBounds,x.PrimitiveIds,smoothPathCount=x.SmoothPaths.Count,sourceAddresses=x.Sources.Select(s=>s.ElementAddress??s.Anchor).ToArray()}), meters, logicalGrid=new{subdivisionsPerBeat=DefaultSubdivisionsPerBeat,blocks=logicalGrid.Blocks.Select(x=>new{x.PartNumber,x.MeasureNumber,x.BeatNumber,x.BeatValue,x.SubdivisionsPerBeat,x.HorizontalUnits,x.HalfStaffSpace,x.PhysicalBounds})}, clefs,ledgerLines,noteHeads,accidentals,stems,arpeggiati,beams,noteFlags,arcs,rests,dots };
        File.WriteAllText(path,JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
    }

    private static string FindRepositoryRoot(string start) { var current=new DirectoryInfo(start); while(current is not null){if(File.Exists(Path.Combine(current.FullName,"SvgToMusicXmlPoc.sln")))return current.FullName; current=current.Parent;} throw new DirectoryNotFoundException("Could not find repository root above input folder."); }
}
