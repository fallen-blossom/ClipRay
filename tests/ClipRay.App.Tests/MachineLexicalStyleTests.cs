using System.Drawing;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;

// Retained source file now tests the narrow attention anchor wire contract.
// No general lexical translation or independent word-model requests remain.
internal static class MachineLexicalStyleTests
{
    public static void Run()
    {
        const string source = "😀 I can read the room.";
        const string translated = "😀 我能读到房间。";
        var style = new SourceVisualStyle(Color.Red,20,28,SourceFontWeight.Normal,SourceFontCategory.SansSerif,
            false,SourceTextAlignment.Left,0,VisualStyleConfidence.High,28);
        var line = new OcrTextBlock(source,new(0,0,320,32),[]);
        var region = new VisualTextRegion("r",source,[line],line.Bounds,VisualTextRole.Body,VisualStructureConfidence.High)
            { SourceStyleSpans = [new(3,1,"I",new(18,0,8,28),style)] };
        var request = TranslationStyleResolver.AttachSourceSpans(region,
            new("en","zh",source,textKind:TranslationTextKind.Body));
        Check((int)TranslationStyleAlignmentEvidence.LexicalAnchorAttention == 3,
            "Production Python anchor evidence wire value drifted.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var wire = JsonSerializer.Deserialize<TranslationTextAlignment>(
            "{\"sourceStart\":3,\"sourceLength\":1,\"targetStart\":3,\"targetLength\":1,\"confidence\":0.922,\"evidence\":3}",options)!;
        Check(wire.Evidence == TranslationStyleAlignmentEvidence.LexicalAnchorAttention,
            "Python wire value failed to deserialize to narrow attention evidence.");
        var process = new SingleFullSentenceProcess(source,translated,wire);
        using var provider = new LocalMachineTranslationProvider(process);
        var result = provider.TranslateManyAsync([request],CancellationToken.None).GetAwaiter().GetResult().Single();
        Check(process.Calls == 1 && result.IsSuccess && result.Text == translated,
            "Narrow attention style performed another word request or changed the full translation.");
        var resolution = TranslationStyleResolver.Resolve(region,request,result);
        Check(resolution.UnresolvedSourceSpanCount == 0 && resolution.Runs.Single() is
            { Start:3,Length:1,Evidence:TranslationStyleAlignmentEvidence.LexicalAnchorAttention },
            "Valid UTF-16 source/target anchor disappeared between worker wire and resolver.");
        Check(resolution.Runs[0].Style.ForegroundColor == Color.Red,
            "Narrow attention evidence lost its independently supplied source style.");
        var invalid = result with { TextAlignments = [wire with { SourceStart = 4 }] };
        Check(TranslationStyleResolver.Resolve(region,request,invalid).Runs.Count == 0 && invalid.Text == translated,
            "A source-offset mismatch gained style or discarded the main translation.");
        var absent = result with { TextAlignments = [] };
        Check(TranslationStyleResolver.Resolve(region,request,absent).UnresolvedSourceSpanCount == 1
            && absent.Text == translated,"Absent model evidence did not remain unresolved with complete text.");
        Console.WriteLine("Narrow pronoun attention wire/provider/resolver cases passed (controlled process; no pixel/model quality claim).");
    }
    private static void Check(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class SingleFullSentenceProcess(string source,string translated,TranslationTextAlignment alignment) : IMachineTranslationProcess
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request,CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>([translated]);
        public Task<MachineTranslationProcessResult> TranslateWithEvidenceAsync(MachineTranslationProcessRequest request,CancellationToken token)
        {
            Calls++;
            Check(Calls == 1 && request.Texts.Count == 1 && request.Texts[0] == source && request.ReturnAlignments,
                "Provider fragmented the main sentence or introduced a lexical supplement.");
            return Task.FromResult(new MachineTranslationProcessResult([translated],TextAlignments:[[alignment]]));
        }
    }
}
