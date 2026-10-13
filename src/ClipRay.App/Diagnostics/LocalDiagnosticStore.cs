using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipRay.App.Diagnostics;

// Diagnostics only. Completed-output receipts and user artifacts never enter this store.
internal sealed record DiagnosticBudget(int EntryBytes=65_536,long RunBytes=8_388_608,long TotalBytes=67_108_864,int ImageBytes=4_194_304);
internal sealed class LocalDiagnosticSession
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string,long> Totals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;
    private readonly DiagnosticBudget _budget;
    private long _written;
    private readonly string _markerId=Guid.NewGuid().ToString("N");
    private bool _markerWritten;
    internal bool Truncated { get; private set; }
    internal static void RegisterRoot(string root) { lock (Gate) Roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))); }
    internal LocalDiagnosticSession(string root,DiagnosticBudget? budget=null)
    {
        _root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); _budget=budget??new(); RegisterRoot(_root);
    }
    internal bool Json(string path,object? value,bool append=true,bool includeContent=false)
    {
        try
        {
            var json=JsonSerializer.Serialize(DiagnosticProjection.Project(value,includeContent));
            Truncated|=json.Contains("\"diagnostic_truncated\":true",StringComparison.Ordinal);
            var bytes=Encoding.UTF8.GetBytes(json+(append?"\n":""));
            if (bytes.Length>_budget.EntryBytes)
            {
                Truncated=true;
                bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { diagnostic_truncated=true, reason="entry-budget", original_bytes=bytes.Length })+(append?"\n":""));
            }
            return Bytes(path,bytes,append,_budget.EntryBytes);
        }
        catch { Truncated=true;return false; }
    }
    internal bool Text(string path,string text) => Bytes(path,Encoding.UTF8.GetBytes(text),false,_budget.EntryBytes);
    internal bool Image(string path,Action<Stream> encode)
    {
        try
        {
            using var stream=new LimitedMemoryStream(_budget.ImageBytes);
            encode(stream);
            return Bytes(path,stream.ToArray(),false,_budget.ImageBytes);
        }
        catch { Truncated=true;return false; }
    }
    private bool Bytes(string path,byte[] bytes,bool append,int limit)
    {
        try
        {
            lock(Gate)
            {
                var full=Path.GetFullPath(path);
                if (!full.StartsWith(_root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                    || !ScreenshotTranslationEvidence.TryResolveLocalRoot(Path.GetDirectoryName(full),out _)) throw new IOException("Non-local diagnostic path.");
                var totalRoot=Roots.Where(r=>_root.Equals(r,StringComparison.OrdinalIgnoreCase)||_root.StartsWith(r+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r=>r.Length).First();
                if (!Totals.TryGetValue(totalRoot,out var total))
                {
                    total=0;
                    if (Directory.Exists(totalRoot))
                    {
                        var options=new EnumerationOptions { RecurseSubdirectories=true,IgnoreInaccessible=false,AttributesToSkip=FileAttributes.ReparsePoint };
                        var count=0;
                        foreach(var file in Directory.EnumerateFiles(totalRoot,"*",options))
                        {
                            if (++count>4096) { total=_budget.TotalBytes;break; }
                            total+=new FileInfo(file).Length;
                            if(total>=_budget.TotalBytes)break;
                        }
                    }
                    Totals[totalRoot]=total;
                }
                var reserve=Math.Min(1024,Math.Min(_budget.RunBytes,_budget.TotalBytes)/8);
                if(bytes.Length>limit || _written+bytes.Length>_budget.RunBytes-reserve || total+bytes.Length>_budget.TotalBytes-reserve)
                {
                    Truncated=true;
                    if(!_markerWritten)
                    {
                        var marker=Encoding.UTF8.GetBytes("{\"diagnostic_truncated\":true,\"reason\":\"write-budget\"}");
                        if(_written+marker.Length<=_budget.RunBytes&&total+marker.Length<=_budget.TotalBytes)
                        {
                            Directory.CreateDirectory(_root);
                            File.WriteAllBytes(Path.Combine(_root,"diagnostic-truncated-"+_markerId+".json"),marker);
                            _written+=marker.Length;Totals[totalRoot]=total+marker.Length;_markerWritten=true;
                        }
                    }
                    return false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                var previous=!append&&File.Exists(full)?new FileInfo(full).Length:0;
                var target=append?full:full+".tmp";
                using(var stream=new FileStream(target,append?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.ReadWrite))
                {stream.Write(bytes);stream.Flush();}
                if(!append)File.Move(target,full,overwrite:true);
                _written+=bytes.Length; Totals[totalRoot]=Math.Max(0,total+bytes.Length-previous);
                return true;
            }
        }
        catch {Truncated=true;return false;}
    }
    private sealed class LimitedMemoryStream(int limit):MemoryStream
    {
        public override void Write(byte[] buffer,int offset,int count) { if(Position+count>limit)throw new IOException("Image budget.");base.Write(buffer,offset,count); }
        public override void Write(ReadOnlySpan<byte> buffer) { if(Position+buffer.Length>limit)throw new IOException("Image budget.");base.Write(buffer); }
        public override void WriteByte(byte value) { if(Position+1>limit)throw new IOException("Image budget.");base.WriteByte(value); }
        public override void SetLength(long value) {if(value>limit)throw new IOException("Image budget.");base.SetLength(value);}
    }
}

internal static class DiagnosticProjection
{
    private static readonly HashSet<string> ContentKeys=new(StringComparer.OrdinalIgnoreCase) {
        "text","OcrText","AlternativeText","source","restored","Scene","TranslationHint","prompt","response","payload","message" };
    private static readonly HashSet<string> StructuralStatuses=new(StringComparer.OrdinalIgnoreCase) {
        "success","failed","cancelled","partial","completed","translated","preserved","fallback","rejected","ready","pending","not-attempted","stopped",
        "ocr-empty","translation-count-mismatch","layout-failed","layout-incomplete","translation-failed" };
    internal static JsonNode? Project(object? value,bool includeContent) => Clean(JsonSerializer.SerializeToNode(value),includeContent,0,"");
    private static JsonNode? Clean(JsonNode? node,bool content,int depth,string key)
    {
        if(depth>16) return JsonValue.Create("depth-limit");
        if(node is JsonObject obj)
        {
            var output=new JsonObject();
            foreach(var pair in obj.Take(128))
            {
                if(!content&&ContentKeys.Contains(pair.Key)) output[pair.Key+"_redacted"]=new JsonObject { ["kind"]=pair.Value is JsonArray?"array":"content",["length"]=pair.Value?.ToJsonString().Length??0 };
                else output[pair.Key]=Clean(pair.Value,content,depth+1,pair.Key);
            }
            if(obj.Count>128)output["diagnostic_truncated"]=true;
            return output;
        }
        if(node is JsonArray array)
        {
            var result=new JsonArray(array.Take(array.Count>256?255:256).Select(x=>Clean(x,content,depth+1,key)).ToArray());
            if(array.Count>256)result.Add(new JsonObject { ["diagnostic_truncated"]=true,["original_count"]=array.Count });
            return result;
        }
        if(node is JsonValue v && v.TryGetValue<string>(out var text))
        {
            // Structural identifiers only; arbitrary descriptions remain size metadata.
            var normalized=key.Replace("_","").ToLowerInvariant();
            var identifier=normalized.EndsWith("id")||normalized.EndsWith("sha256")||normalized.EndsWith("version")||normalized.EndsWith("utc")
                || normalized is "kind" or "stage" or "reason" or "route" or "category" or "engine" or "model" or "script"
                or "operation" or "artifactid" or "name" or "failuretype" or "errorcategory" or "failurestage" or "failedstage" or "laststage"
                or "schemaversion" or "sourcelanguage" or "targetlanguage" or "visibilityclaim" or "evidencemode" or "pixelformat"
                or "stopreason" or "binaryversion" or "semanticoutcome" or "textkind" or "overlaydecision";
            identifier |= normalized is "diagnosticfailure" or "type" or "mismatch" or "role" or "state"
                || normalized=="status"&&StructuralStatuses.Contains(text);
            var timestamp=normalized.EndsWith("utc")&&DateTimeOffset.TryParse(text,out _);
            var language=normalized is "sourcelanguage" or "targetlanguage" && text is "简体中文" or "繁體中文" or "繁体中文" or "英语" or "日语" or "韩语" or "自动";
            if(!content && (!identifier || text.Length>160 || !timestamp&&!language&&text.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '-' and not '_' and not '.' and not '/' and not ' ' and not '+')))
                return new JsonObject { ["string_length"]=text.Length };
            return text.Length<=2048?JsonValue.Create(text):new JsonObject { ["diagnostic_truncated"]=true,["string_length"]=text.Length,["prefix"]=text[..2048] };
        }
        return node?.DeepClone();
    }
}
