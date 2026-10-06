using ClipRay.App.LiveTranslation;
using ClipRay.App.Translation;

internal static class LiveTranslationCacheTests
{
    public static void OnlyTransientFailedParagraphIsRetried()
    {
        var calls=0;
        var cache=new LiveTranslationRequestCache((route,requests,_)=>
        {
            calls++;
            if(calls>1 && (requests.Count!=1 || requests[0].OcrText!="failed paragraph"))
                throw new Exception("Retry included a completed unrelated paragraph.");
            return Task.FromResult(new TranslationBatchOutcome(requests.Select(request=>
                request.OcrText=="failed paragraph" && calls==1
                    ? new TranslationResult(false,"","machine-worker-timeout")
                    : new TranslationResult(true,"译文","machine-completed")).ToArray(),new(route,0,0,0)));
        });
        var result=cache.TranslateAsync(TranslationRouteKind.Machine,
            [new TranslationRequest("en","zh","good paragraph"),new TranslationRequest("en","zh","failed paragraph")],default)
            .GetAwaiter().GetResult();
        if(calls!=2 || result.Results.Any(result=>!result.IsSuccess)) throw new Exception("Transient paragraph did not recover locally.");
    }
    public static void SemanticCacheKeepsNumbersAndRetriesTimeouts()
    {
        var calls=0;
        var cache=new LiveTranslationRequestCache((route,requests,_)=>
        {
            calls++;
            return Task.FromResult(new TranslationBatchOutcome(requests.Select(request=>
                request.OcrText=="timeout" && calls==3
                    ? new TranslationResult(false,"","machine-worker-timeout")
                    : new TranslationResult(true,"译文","machine-completed")).ToArray(),new(route,0,0,0)));
        });
        var one=new TranslationRequest("en","zh","The limit is 3.",textKind:TranslationTextKind.Body);
        var changed=new TranslationRequest("en","zh","The limit is 8.",textKind:TranslationTextKind.Body);
        var timeout=new TranslationRequest("en","zh","timeout");
        foreach(var request in new[]{one,one,changed,timeout,timeout})
            cache.TranslateAsync(TranslationRouteKind.Machine,[request],default).GetAwaiter().GetResult();
        if(calls!=4) throw new Exception("Semantic cache retranslated stable text, ignored a number, or cached a timeout.");
    }

    public static void NormalizesWhitespaceAndRetriesUnchangedNaturalLanguage()
    {
        var calls=0;
        var cache=new LiveTranslationRequestCache((route,requests,_)=>
        {
            calls++;
            return Task.FromResult(new TranslationBatchOutcome(requests.Select(_=>calls==1
                ? new TranslationResult(false,"","machine-output-unchanged")
                : new TranslationResult(true,"译文","machine-completed")).ToArray(),new(route,0,0,0)));
        });
        var spaced=new TranslationRequest("EN","zh","A   natural\n sentence",textKind:TranslationTextKind.Body);
        var normalized=new TranslationRequest("en","ZH","A natural sentence",textKind:TranslationTextKind.Body);
        var first=cache.TranslateAsync(TranslationRouteKind.Machine,[spaced],default).GetAwaiter().GetResult();
        var second=cache.TranslateAsync(TranslationRouteKind.Machine,[normalized],default).GetAwaiter().GetResult();
        var third=cache.TranslateAsync(TranslationRouteKind.Machine,[spaced],default).GetAwaiter().GetResult();
        if(first.Results[0].IsSuccess || !second.Results[0].IsSuccess || !third.Results[0].IsSuccess || calls!=2)
            throw new Exception("Live semantic cache preserved an unchanged failure or missed normalized text reuse.");
    }

    public static void IntegrityFailureExpiresWithoutRepeatedImmediateRequests()
    {
        var clock = new ManualCacheClock();
        var calls = 0;
        var cache = new LiveTranslationRequestCache((route, requests, _) =>
        {
            calls++;
            return Task.FromResult(new TranslationBatchOutcome(requests.Select(_ => calls < 3
                ? new TranslationResult(false, "", "machine-content-integrity-failed")
                : new TranslationResult(true, "译文", "machine-completed")).ToArray(),
                new(route, 0, 0, 0)));
        }, clock: clock);
        var original = new TranslationRequest("en", "zh", "Keep ticket XR-17");
        var changed = new TranslationRequest("en", "zh", "Keep ticket XR-18");

        TranslationResult Read(TranslationRequest request) =>
            cache.TranslateAsync(TranslationRouteKind.Machine, [request], default)
                .GetAwaiter().GetResult().Results[0];

        if (Read(original).IsSuccess || Read(original).IsSuccess || calls != 1)
            throw new Exception("An integrity failure retried immediately or was treated as success.");
        clock.Advance(TimeSpan.FromSeconds(3));
        if (Read(original).IsSuccess || calls != 2)
            throw new Exception("An integrity failure stayed cached beyond its first retry deadline.");
        clock.Advance(TimeSpan.FromSeconds(3));
        if (Read(original).IsSuccess || calls != 2)
            throw new Exception("Repeated integrity failures did not back off.");
        if (!Read(changed).IsSuccess || calls != 3)
            throw new Exception("A changed source was blocked by another text's failure cooldown.");
        clock.Advance(TimeSpan.FromSeconds(3));
        if (!Read(original).IsSuccess || !Read(original).IsSuccess || calls != 4)
            throw new Exception("A recovered translation was not reused after the failure expired.");
    }

    private sealed class ManualCacheClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public void Advance(TimeSpan duration) => _milliseconds += (long)duration.TotalMilliseconds;
    }
}
