using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using ClipRay.App.Scrolling;

internal static partial class BackgroundTask6Tests
{
    private static string RunPieces(string root,int frames=3,bool edge=false)
    {
        var capture = new MovingCapture(edge?300:24,edge?80:24,edge?20:12,edge);
        using var session = new ScrollCaptureSession(new(0,0,capture.Width,capture.Height),ScrollCaptureMode.Automatic,capture,new Input(),new Settler(),root,
            new ScrollCaptureOptions(MaximumFrames:frames,MaximumPolls:20,SettleDelay:TimeSpan.Zero));
        session.PreviewChanged += (_,_) =>
        {
            var candidate=new ScrollCaptureRecovery(root).List().Single(c=>c.Status=="recoverable"&&new ScrollCaptureRecovery(root).ReadValidated(c.Directory).Status=="capturing");
            var manifest=new ScrollCaptureRecovery(root).ReadValidated(candidate.Directory);
            Check(manifest.Status=="capturing" && manifest.Segments.Count==sessionFrame(capture),"Manifest was deferred until final preservation.");
        };
        using var result=Complete(session.RunAsync(CancellationToken.None));
        Check(result.AcceptedFrameCount==frames,"Valid moving frames were lost.");
        return result.TempSessionDirectory;
        static int sessionFrame(MovingCapture source)=>source.Calls;
    }
    private static void PersistenceAndRestartPixels()
    {
        using var temp=new Temp();var directory=RunPieces(temp.Root);
        var restarted=new ScrollCaptureRecovery(temp.Root);
        var manifest=restarted.ReadValidated(directory);
        Check(manifest.SchemaVersion==2&&manifest.Status=="recoverable"&&manifest.Segments.Select(s=>s.OverlapRows).SequenceEqual(new[]{0,12,12}),"Overlap/status was not durably indexed.");
        using var output=restarted.CreateComposite(directory);AssertPattern(output,24,48);
        var bytes=Directory.EnumerateFiles(directory).ToDictionary(p=>p,File.ReadAllBytes);
        ScrollCaptureTempStore.CleanupOrphans(temp.Root);
        Check(bytes.All(p=>File.Exists(p.Key)&&p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"Restart cleanup changed pending artifacts.");
    }
    private static void FailedManifestRetainsIndexedSubset()
    {
        using var temp=new Temp();var writes=0;
        using var session=new ScrollCaptureSession(new(0,0,24,24),ScrollCaptureMode.Automatic,new MovingCapture(24,24,12),new Input(),new Settler(),temp.Root,
            new ScrollCaptureOptions(MaximumFrames:3,SettleDelay:TimeSpan.Zero),manifestCommit:(dir,manifest)=>
            {
                if(++writes==2) throw new IOException("fake manifest failure");
                ScrollCaptureRecovery.WriteAtomic(dir,manifest);
            });
        using var result=session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(result.AcceptedFrameCount==1&&result.StopReason==ScrollCaptureStopReason.CaptureFailure,"Uncommitted segment was represented as indexed.");
        var restart=new ScrollCaptureRecovery(temp.Root);var candidate=restart.List().Single();
        Check(candidate.CanRecover&&candidate.Status=="indexed-subset"&&File.Exists(Path.Combine(candidate.Directory,"001.png")),"Unindexed piece was removed or concealed.");
        using var output=restart.CreateComposite(candidate.Directory);AssertPattern(output,24,24);
    }
    private static void RejectsUnsafeManifests()
    {
        using var temp=new Temp();var directory=RunPieces(temp.Root);var recovery=new ScrollCaptureRecovery(temp.Root);
        var original=recovery.ReadValidated(directory);var marker=Path.Combine(directory,ScrollCaptureTempStore.RecoveryMarker);
        var bytes=File.ReadAllBytes(marker);var segments=original.Segments.ToArray();
        var bad=new List<ScrollCaptureManifest>
        {
            original with{SchemaVersion=3}, original with{SessionId="other"}, original with{Status="unknown"},
            original with{Segments=null!}, original with{Segments=[]}, original with{Segments=[null!]},
            original with{Segments=segments.Select((s,i)=>i==0?s with{FileName="../outside.png"}:s).ToArray()},
            original with{Segments=segments.Select((s,i)=>i==0?s with{FileName="https://invalid/image.png"}:s).ToArray()},
            original with{Segments=segments.Select((s,i)=>i==0?s with{Width=int.MaxValue}:s).ToArray()},
            original with{Segments=segments.Select((s,i)=>i==0?s with{Height=0}:s).ToArray()},
            original with{Segments=segments.Select((s,i)=>i==1?s with{OverlapRows=24}:s).ToArray()},
            original with{Segments=segments.Select((s,i)=>i==0?s with{OverlapRows=1}:s).ToArray()},
            original with{Segments=segments.Select((s,i)=>i==1?s with{Width=25}:s).ToArray()},
            original with{Segments=Enumerable.Range(0,3).Select(i=>new ScrollCaptureSegment($"{i:D3}.png",2400,30000,i==0?0:1)).ToArray()},
            original with{Segments=Enumerable.Range(0,3).Select(i=>new ScrollCaptureSegment($"{i:D3}.png",24,30000,i==0?0:1)).ToArray()},
        };
        foreach(var manifest in bad)
        {
            File.WriteAllText(marker,JsonSerializer.Serialize(manifest));
            Check(!recovery.List().Single().CanRecover,"Invalid manifest was offered for reliable recovery.");
            Reject(()=>recovery.CreateComposite(directory).Dispose());
            Check(Directory.EnumerateFiles(directory,"*.png").Count()==3,"Invalid manifest deleted pieces.");
        }
        File.WriteAllBytes(marker,bytes);
        using(var output=recovery.CreateComposite(directory)) AssertPattern(output,24,48);
        Reject(()=>recovery.CreateComposite(Path.Combine(temp.Root,"..",Path.GetFileName(directory))).Dispose());
        Reject(()=>new ScrollCaptureRecovery("relative"));
        var damaged=Path.Combine(directory,"001.png");var png=File.ReadAllBytes(damaged);png[19]^=1;File.WriteAllBytes(damaged,png);
        Reject(()=>recovery.CreateComposite(directory).Dispose());
        Check(File.ReadAllBytes(damaged).SequenceEqual(png),"Header rejection removed damaged material.");
    }
    private static void LegacyAndCorruptMaterialsStay()
    {
        using var temp=new Temp();
        foreach(var marker in new[]{"{\"SchemaVersion\":1,\"AcceptedFrameCount\":2}","{broken", ""})
        {
            var directory=Path.Combine(temp.Root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,ScrollCaptureTempStore.RecoveryMarker),marker);
            using var image=Pattern(24,24,0);image.Save(Path.Combine(directory,"000.png"),ImageFormat.Png);
        }
        var before=Directory.EnumerateFiles(temp.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        Check(new ScrollCaptureRecovery(temp.Root).List().All(c=>!c.CanRecover),"Legacy overlap was guessed.");
        ScrollCaptureTempStore.CleanupOrphans(temp.Root);
        Check(before.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"Unknown materials were changed.");
        var valid=RunPieces(temp.Root);var path=Path.Combine(valid,"001.png");var bytes=File.ReadAllBytes(path);File.WriteAllBytes(path,bytes[..30]);
        Reject(()=>new ScrollCaptureRecovery(temp.Root).CreateComposite(valid).Dispose());
        Check(File.ReadAllBytes(path).Length==30,"Decode failure deleted a partial PNG.");
    }
    private static void ReparseMaterialsStay()
    {
        var fixture=Environment.GetEnvironmentVariable("CLIPRAY_TASK6_JUNCTION_FIXTURE")??throw new InvalidOperationException("Own OS-temp junction fixture was not supplied.");
        fixture=Path.GetFullPath(fixture);
        Check(Path.GetDirectoryName(fixture)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(fixture).StartsWith("clipray-task6-junction-",StringComparison.Ordinal),"Junction fixture is outside its owned OS-temp scope.");
        var root=Path.Combine(fixture,"sessions");var target=Path.Combine(fixture,"target");
        var link=Directory.EnumerateDirectories(root).Single();
        Check((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0&&new DirectoryInfo(link).ResolveLinkTarget(true)!.FullName==target,"Expected owned junction target changed.");
        using var temp=new Temp();var directory=RunPieces(temp.Root);
        foreach(var file in Directory.EnumerateFiles(directory))File.Copy(file,Path.Combine(target,Path.GetFileName(file)));
        var before=Directory.EnumerateFiles(target).ToDictionary(p=>p,File.ReadAllBytes);
        try
        {
            var recovery=new ScrollCaptureRecovery(root);
            Check(!recovery.List().Single().CanRecover,"Recovery traversed a redirected directory.");
            Reject(()=>recovery.CreateComposite(link).Dispose());ScrollCaptureTempStore.CleanupOrphans(root);
            Check(Directory.Exists(link)&&before.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"Reparse cleanup removed target material.");
        }
        finally { Directory.Delete(link); }
    }
    private static void ManualContinuationAfterPause()
    {
        using var temp=new Temp();var polls=0;var capture=new MovingCapture(24,24,0);var input=new Input();ScrollCaptureSession? session=null;
        using(session=new(new(0,0,24,24),ScrollCaptureMode.Automatic,capture,input,new Settler(()=>
        {if(++polls==9)capture.Offset=12;if(polls==10)session!.RequestCompletion();}),temp.Root,new ScrollCaptureOptions(SettleDelay:TimeSpan.Zero)))
        using(var result=session.RunAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            Check(input.Calls==8&&!session.AutomaticScrollingEnabled&&result.AcceptedFrameCount==2,"Manual continuation after initial pause was lost.");
            using var image=result.CreateComposite();AssertPattern(image,24,36);
        }
    }
    private static void NarrowEdgesComposeExactPixels()
    {
        using var temp=new Temp();var directory=RunPieces(temp.Root,edge:true);
        using var image=new ScrollCaptureRecovery(temp.Root).CreateComposite(directory);
        Check(image.Width==300&&image.Height==120,"Edge-only content rows were omitted.");
        for(var y=0;y<120;y++)for(var x=0;x<12;x++)Check(image.GetPixel(x,y)==PatternPixel(x,y),"Narrow text edge was duplicated or lost.");
        using var first=Edge(300,80,0);using var next=Edge(300,80,20);
        first.RotateFlip(RotateFlipType.RotateNoneFlipX);next.RotateFlip(RotateFlipType.RotateNoneFlipX);
        var analysis=new FrameStitcher().Analyze(first,next);Check(analysis.Disposition==FrameStitchDisposition.Append&&analysis.OverlapRows==60,"Right edge-only movement was not recognized.");
        using var blank=new Bitmap(300,1000);using var sparse=new Bitmap(300,1000);
        using(var graphics=Graphics.FromImage(blank))graphics.Clear(Color.White);
        using(var graphics=Graphics.FromImage(sparse))graphics.Clear(Color.White);
        sparse.SetPixel(0,997,Color.Black);
        Check(new FrameStitcher().Analyze(blank,sparse).Disposition!=FrameStitchDisposition.Repeated,"A sparse changed edge was averaged into high-confidence repetition.");
    }
    private static void NoiseAndStaticControlsRemainSupported()
    {
        using var first=Pattern(300,80,0);using var noise=Pattern(300,80,0);
        var color=noise.GetPixel(2,2);noise.SetPixel(2,2,Color.FromArgb(color.A,Math.Min(255,color.R+1),color.G,color.B));
        Check(new FrameStitcher().Analyze(first,noise).Disposition==FrameStitchDisposition.Repeated,"Dispersed tiny noise no longer permits repetition.");
        using var next=Pattern(300,80,20);
        for(var y=0;y<80;y++)for(var x=280;x<300;x++){first.SetPixel(x,y,Color.Gray);next.SetPixel(x,y,Color.Gray);}
        var analysis=new FrameStitcher().Analyze(first,next);Check(analysis.Disposition==FrameStitchDisposition.Append&&analysis.OverlapRows==60,"Static edge controls broke legitimate content overlap.");
    }
    private static void PreviousRecoveryCases()
    {
        RecoveryRepairTests.PreserveSurvivesDisposeAndOrphanCleanup();
        RecoveryRepairTests.ClipboardFailureDoesNotLoseSavedImage();
        typeof(Program).GetMethod("DisposesBoundedLosslessSegmentsAfterManualPartialCapture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,null);
        typeof(Program).GetMethod("CleansOrphanLongCaptureSessionsWhilePreservingActiveSessions",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,null);
        foreach(var method in new[]{"StitchesUniqueLargeOverlapDespiteSparseDynamicPixels","StitchesThroughChangingEdgesAndSparseDynamicRows","RejectsOtherwiseRepeatedLargeFrameWithSparseDynamicPixels","RejectsAmbiguousRepeatedTextureOverlaps","RejectsRowRunAmbiguityInLargeRepeatedTexture"})
            typeof(Program).GetMethod(method,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,null);
    }
    private static void AssertPattern(Bitmap bitmap,int width,int height)
    {
        Check(bitmap.Width==width&&bitmap.Height==height,"Composite dimensions disagree.");
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)Check(bitmap.GetPixel(x,y)==PatternPixel(x,y),"Final pixels changed, duplicated or disappeared.");
    }
    private static void Reject(Action operation)
    {
        var rejected=false;try{operation();}catch(Exception error)when(error is IOException or InvalidOperationException or ArgumentException or JsonException or OverflowException or System.Runtime.InteropServices.ExternalException){rejected=true;}
        Check(rejected,"Invalid recovery was not rejected.");
    }
    private sealed class MovingCapture(int width,int height,int step,bool edge=false) : IScrollRegionCapture
    {
        public int Width=>width;public int Height=>height;public int Calls;public int Step=step;public int Offset;
        public Bitmap Capture(Rectangle region){var bitmap=edge?Edge(width,height,Offset):Pattern(width,height,Offset);Offset+=Step;Calls++;return bitmap;}
    }
}
