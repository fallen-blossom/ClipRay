using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

public sealed record LiveTranslationCacheDiagnostic(
    string Kind, int RequestCount, IReadOnlyList<int> HitIndices,
    int DistinctMissCount, int RetryCount = 0);

/// <summary>Session-scoped semantic cache, independent of source geometry.</summary>
public sealed class LiveTranslationRequestCache(
    Func<TranslationRouteKind,IReadOnlyList<TranslationRequest>,CancellationToken,Task<TranslationBatchOutcome>> translate,
    Action<LiveTranslationCacheDiagnostic>? diagnostic = null,
    TimeProvider? clock = null)
{
    private readonly Dictionary<CacheKey,CachedValue> _results = [];
    private readonly Queue<CacheKey> _order = [];
    private readonly Dictionary<CacheKey,FailureEntry> _failures = [];
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<TranslationBatchOutcome> TranslateAsync(TranslationRouteKind route,
        IReadOnlyList<TranslationRequest> requests,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var requestKeys=requests.Select(request=>CacheKey.Create(route,request)).ToArray();
        var cached = new Dictionary<CacheKey,CachedValue>();
        foreach (var key in requestKeys.Distinct())
            if (_results.TryGetValue(key, out var completed)) cached.Add(key, completed);
            else if (_failures.TryGetValue(key, out var failure)
                && _clock.GetElapsedTime(failure.RecordedAt) < FailureDelay(failure.Count))
                cached.Add(key, failure.Value);
        var hitIndices = requestKeys.Select((key,index)=>(key,index))
            .Where(pair=>cached.ContainsKey(pair.key)).Select(pair=>pair.index).ToArray();
        var missing=requests.Zip(requestKeys)
            .GroupBy(static pair=>pair.Second)
            .Where(group=>!cached.ContainsKey(group.Key))
            .Select(static group=>group.First()).ToArray();
        var missingRequests=missing.Select(static pair=>pair.First).ToArray();
        Report(new("cache_plan", requests.Count, hitIndices, missing.Length));
        var outcome=missing.Length==0 ? new TranslationBatchOutcome([],new(route,0,0,0))
            : await translate(route,missingRequests,token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if(outcome.Results.Count!=missing.Length) throw new InvalidOperationException("translation-count-mismatch");
        var translated=outcome.Results.ToArray();
        var retryCount = 0;
        for(var attempt=0;attempt<2;attempt++)
        {
            var failed=Enumerable.Range(0,missing.Length).Where(index=>!translated[index].IsSuccess
                && translated[index].Status is "machine-worker-timeout" or "machine-provider-failed").ToArray();
            if(failed.Length==0) break;
            retryCount += failed.Length;
            await Task.Delay(150*(1<<attempt),token).ConfigureAwait(false);
            var retry=await translate(route,failed.Select(index=>missingRequests[index]).ToArray(),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if(retry.Results.Count!=failed.Length) throw new InvalidOperationException("translation-count-mismatch");
            for(var i=0;i<failed.Length;i++) translated[failed[i]]=retry.Results[i];
        }
        var current=new Dictionary<CacheKey,CachedValue>();
        for(var i=0;i<missing.Length;i++)
        {
            var key=missing[i].Second;
            var value=new CachedValue(translated[i],missingRequests[i].OcrText);
            current.Add(key,value);
            if(translated[i].IsSuccess)
            {
                _results[key]=value; _order.Enqueue(key);
                _failures.Remove(key);
            }
            else if (translated[i].Status is "machine-content-integrity-failed" or "machine-protected-content-failed")
            {
                // A repeatable bad OCR/model response should not pin a region in
                // English for the entire session. Retry it with bounded backoff.
                var count = _failures.TryGetValue(key, out var previous)
                    ? Math.Min(previous.Count + 1, 6) : 1;
                if (!_failures.ContainsKey(key) && _failures.Count >= 512)
                    _failures.Remove(_failures.MinBy(pair => pair.Value.RecordedAt).Key);
                _failures[key] = new(value, _clock.GetTimestamp(), count);
            }
        }
        // Gather before eviction: one large input batch may exceed cache capacity.
        var results=requestKeys.Select((key,index)=>(current.TryGetValue(key,out var value)
            ? value : cached[key]).ForSource(requests[index].OcrText)).ToArray();
        while(_order.Count>512) _results.Remove(_order.Dequeue());
        Report(new("cache_complete", requests.Count, hitIndices, missing.Length, retryCount));
        return new(results,outcome.Trace with { FailedCount=results.Count(result=>!result.IsSuccess) });

        void Report(LiveTranslationCacheDiagnostic value)
        {
            try { diagnostic?.Invoke(value); }
            catch { /* Evidence must not change translation. */ }
        }
    }

    private static TimeSpan FailureDelay(int count) => TimeSpan.FromSeconds(
        Math.Min(30, 3 * (1 << Math.Min(count - 1, 4))));

    private sealed record CachedValue(TranslationResult Result,string ExactSourceText)
    {
        internal TranslationResult ForSource(string source)=>ExactSourceText==source || Result.TextAlignments.Count==0
            ? Result : Result with { TextAlignments=[] };
    }
    private sealed record FailureEntry(CachedValue Value, long RecordedAt, int Count);

    private sealed record CacheKey(
        TranslationRouteKind Route,
        string SourceLanguage,
        string TargetLanguage,
        string Text,
        string Model,
        TranslationTextKind TextKind,
        string Scene,
        string TranslationHint,
        string SourceSpanIdentity)
    {
        internal static CacheKey Create(TranslationRouteKind route,TranslationRequest request)=>new(
            route,
            request.SourceLanguage.Trim().ToLowerInvariant(),
            request.TargetLanguage.Trim().ToLowerInvariant(),
            // Alignment coordinates refer to this exact UTF-16 input and this
            // requested span structure. A semantic hit alone cannot certify them.
            request.SourceSpans.Count>0 ? request.OcrText
                : string.Join(' ',request.OcrText.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)),
            request.Model,
            request.TextKind,
            request.Scene,
            request.TranslationHint,
            request.SourceSpans.Count==0 ? string.Empty : System.Text.Json.JsonSerializer.Serialize(request.SourceSpans));
    }
}
