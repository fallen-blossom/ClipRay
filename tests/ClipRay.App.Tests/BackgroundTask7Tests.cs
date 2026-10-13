using System.Drawing;
using System.IO;
using System.Text.Json;
using ClipRay.App.Capture;
using ClipRay.App.Cleanup;
using ClipRay.App.Infrastructure;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

internal static partial class BackgroundTask7Tests
{
    internal static (string Name,Action Test)[] CreateCases() => [("upgrade-start-guard",UpgradeStartGuard),("exit-identities",ExitIdentities),("exit-fail-closed",ExitFailure),
            ("saved-producers",SavedProducers),("receipt-failure",ReceiptFailure),("cleanup-recheck",CleanupRecheck),
            ("cleanup-continuation",CleanupContinuation),("cleanup-coordination",CleanupCoordination),("uninstall-fake",UninstallFake),
            ("actual-recovery",BackgroundTask6Tests.SavedRecoveryCleanupContract),("prior-cleanup-companions",PriorCleanupCompanions),
            ("shared-install-maintenance",SharedInstallMaintenance),("recycle-adapter",RecycleAdapter)];

    internal static int Run()
    {
        var cases=CreateCases();
        var failures=0;
        foreach(var item in cases)
        {
            var prior=SynchronizationContext.Current;
            var install=System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;
                SynchronizationContext.SetSynchronizationContext(item.Name=="actual-recovery" ? new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher):null);
                item.Test();Console.WriteLine("PASS task-7/"+item.Name);
            }
            catch(Exception error){failures++;Console.Error.WriteLine("FAIL task-7/"+item.Name+": "+error);}
            finally{SynchronizationContext.SetSynchronizationContext(prior);System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=install;}
        }
        Console.WriteLine($"TASK_7_BACKGROUND {cases.Length-failures}/{cases.Length} passed");return failures==0?0:1;
    }
    internal static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    private static readonly string Exe=Path.Combine(Path.GetTempPath(),"fixture-install","app","ClipRay.App.exe");
    private static void UpgradeStartGuard()
    {
        var name = @"Local\ClipRayUpgradeFixture-" + Guid.NewGuid().ToString("N");
        Check(!InstallerUpgradeGuard.IsActive(name), "Missing upgrade lock blocked normal startup.");
        using (var mutex = new Mutex(false, name))
            Check(InstallerUpgradeGuard.IsActive(name), "Active upgrade lock did not block startup.");
        Check(!InstallerUpgradeGuard.IsActive(name), "Released upgrade lock still blocked startup.");
    }

    private static void ExitIdentities()
    {
        foreach(var mismatch in new[]{"path","start","pid","protocol","empty-id"})
        {
            var identity=new InstallProcessIdentity(42,10,Exe);var calls=0;
            var request=new InstallExitRequest(1,Guid.NewGuid(),identity);
            request=mismatch switch {"path"=>request with{Target=identity with{ExecutablePath=Path.Combine(Path.GetTempPath(),"sibling","ClipRay.App.exe")}},
                "start"=>request with{Target=identity with{StartUtcTicks=11}},"pid"=>request with{Target=identity with{ProcessId=43}},
                "protocol"=>request with{Protocol=2},_=>request with{RequestId=Guid.Empty}};
            var reply=InstallExitProtocol.HandleAsync(request,identity,()=>{calls++;return Task.FromResult(true);}).GetAwaiter().GetResult();
            Check(calls==0&&reply.Status=="identity-or-protocol-mismatch","Mismatched identity retired the server.");
        }
        var backend=new ExitBackend();
        Check(new InstallExitClient(backend).ConfirmExitAsync(Exe,"shared-release",TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult(),"Exact target did not exit.");
        Check(backend.FindCalls==2&&backend.Handle.Waits==1&&backend.Handle.Disposed&&backend.Requests==1,"Physical wait / restart check not reached.");
        Check(AppActivationPolicy.ShouldBypassSingleInstance(["--request-install-exit"],false),"Maintenance helper can become primary.");
        Check(AppActivationPolicy.ForwardedCommand(["--interactive"])==AppActivationCommand.StartInteractiveCapture
            &&AppActivationPolicy.ForwardedCommand(["--quick-current"])==AppActivationCommand.CaptureCurrentScreen
            &&AppActivationPolicy.ForwardedCommand(["--open-settings"])==AppActivationCommand.OpenSettings
            &&InstallExitProtocol.PipeName("ClipRay.user.release")=="ClipRay.user.release.Activation","Known activation / shared pipe scope changed.");
    }
    private static void ExitFailure()
    {
        foreach(var failure in new[]{"unknown","legacy","cancelled","alive","restart","unknown-after","wrong-id","wrong-target","timeout","access"})
        {
            var backend=new ExitBackend{Failure=failure};
            Check(!new InstallExitClient(backend).ConfirmExitAsync(Exe,"shared",TimeSpan.FromMilliseconds(80)).GetAwaiter().GetResult(),"Unsafe exit succeeded: "+failure);
            Check(backend.Handle.Disposed||failure=="access","Exact retained handle leaked: "+failure);
        }
    }
    internal static CleanupPlan Plan(string root)=>new(DateTime.UtcNow.AddDays(1),[new(root,new HashSet<CleanupFileKind>{CleanupFileKind.CompletedImage,CleanupFileKind.CompletedRecording})]);
    private static void SavedProducers()
    {
        using var temp=new Temp();using var bitmap=new Bitmap(3,4);
        var image=new ImageOutputService(temp.Root,false).SavePng(bitmap,DateTime.Now);
        var longImage=new ImageOutputService(temp.Root,false,OutputArtifactKind.LongCapture).SavePng(bitmap,DateTime.Now);
        var paths=RecordingOutputPaths.Create(temp.Root,DateTime.Now);Directory.CreateDirectory(Path.GetDirectoryName(paths.PartialPath)!);
        File.WriteAllBytes(paths.PartialPath,[1,2,3]);new RecordingFileMover().Finalize(paths);
        var backend=new Recycle();var cleanup=new CleanupService(recycle:backend);var plan=Plan(temp.Root);
        var candidates=cleanup.Scan(plan);Check(candidates.Count==3,"Actual PNG/long/video commits did not become eligible.");
        var result=cleanup.ExecuteCandidates(plan,candidates);
        Check(result.All(x=>x.Status=="recycled")&&backend.Paths.ToHashSet().SetEquals([image,longImage,paths.FinalPath]),"Saved products did not reach fake recycle.");
        Check(File.Exists(image)&&File.Exists(longImage)&&File.Exists(paths.FinalPath)&&!File.Exists(paths.PartialPath),"Fake backend changed real outputs or final commit failed.");
    }
    private static void ReceiptFailure()
    {
        using var temp=new Temp();using var bitmap=new Bitmap(2,2);
        var output=new ImageOutputService(temp.Root,false){CompletedOutputs=new(){BeforeReceiptWrite=_=>throw new IOException("proof")}};
        var saved=output.SaveWithDelivery(bitmap);Check(File.Exists(saved.Path)&&saved.DiagnosticFailure=="completed-output-receipt-unavailable","Receipt failure reversed image Saved.");
        var paths=RecordingOutputPaths.Create(temp.Root,DateTime.Now);Directory.CreateDirectory(Path.GetDirectoryName(paths.PartialPath)!);File.WriteAllBytes(paths.PartialPath,[9]);
        new RecordingFileMover{CompletedOutputs=output.CompletedOutputs}.Finalize(paths);
        Check(File.Exists(paths.FinalPath)&&new CleanupService(recycle:new Recycle()).Scan(Plan(temp.Root)).Count==0,"Unknown successful outputs were removed or claimed complete without proof.");
    }
    private static void CleanupRecheck()
    {
        foreach(var change in new[]{"size","same-size","write","receipt-state","cutoff","outside","traversal","kind"})
        {
            using var temp=new Temp();using var bitmap=new Bitmap(2,2);var file=new ImageOutputService(temp.Root,false).SavePng(bitmap,DateTime.Now);
            var recycle=new Recycle();var cleanup=new CleanupService(recycle:recycle);var plan=Plan(temp.Root);var candidate=cleanup.Scan(plan).Single();
            switch(change)
            {
                case "size":File.AppendAllText(file,"changed");break;
                case "same-size":var time=File.GetLastWriteTimeUtc(file);var bytes=File.ReadAllBytes(file);bytes[^1]^=1;File.WriteAllBytes(file,bytes);File.SetLastWriteTimeUtc(file,time);break;
                case "write":File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddMinutes(1));break;
                case "receipt-state":File.WriteAllText(CompletedOutputStore.ReceiptPath(file),JsonSerializer.Serialize(candidate.Receipt with{State="pending"}));break;
                case "cutoff":plan=plan with{CutoffUtc=DateTime.UtcNow.AddDays(-31)};break;
                case "outside":candidate=candidate with{Root=Path.Combine(temp.Root,"other")};break;
                case "traversal":candidate=candidate with{Path=Path.Combine(Path.GetDirectoryName(file)!,"..",Path.GetFileName(Path.GetDirectoryName(file))!,Path.GetFileName(file))};break;
                case "kind":candidate=candidate with{Receipt=candidate.Receipt with{Kind=CleanupFileKind.CompletedRecording}};break;
            }
            var results=cleanup.ExecuteCandidates(plan,[candidate]);Check(results.Single().Status=="skipped"&&recycle.Paths.Count==0&&File.Exists(file),"Changed candidate recycled: "+change);
        }
        Check(!CleanupPathPolicy.CanDescend(FileAttributes.Directory|FileAttributes.ReparsePoint),"Reparse policy allowed descent.");
        Check(!CompletedOutputStore.IsOrdinaryPath("\\\\unknown\\share\\file.png"),"Network output accepted.");
        Check(CleanupPlan.FromSettings(new AppSettings(),DateTimeOffset.UtcNow) is null,"Cleanup default changed.");
    }
    private static void CleanupContinuation()
    {
        using var temp=new Temp();using var bitmap=new Bitmap(2,2);var output=new ImageOutputService(temp.Root,false);
        var first=output.SavePng(bitmap,DateTime.Now);var second=output.SavePng(bitmap,DateTime.Now);
        var historical=Path.Combine(temp.Root,"ClipRay_120000_001.png");File.WriteAllBytes(historical,[1]);
        var partial=Path.Combine(temp.Root,"ClipRay-Recording_2026-01-01_00-00-00-000.partial.mp4");File.WriteAllBytes(partial,[2]);
        var recovery=Path.Combine(temp.Root,"recovery");Directory.CreateDirectory(recovery);File.WriteAllBytes(Path.Combine(recovery,"000.png"),[3]);
        var recycle=new Recycle{FailPath=first};var result=new CleanupService(recycle:recycle).Execute(Plan(temp.Root));
        Check(result.FailedPathCount==1&&result.RecycledFileCount==1&&recycle.Paths.Contains(second)&&File.Exists(historical)&&File.Exists(partial)&&Directory.Exists(recovery),"One failure blocked other saved output or removed protected material.");
        var status=new CleanupRunStatus(DateTimeOffset.UtcNow,false,result.RecycledFileCount,result.SkippedPathCount,result.FailedPathCount){Items=result.Items};
        var store=new CleanupRunStatusStore(Path.Combine(temp.Root,"status.json"));store.Write(status);
        Check(store.Load()?.Items.Count==result.Items.Count,"Per-path outcomes lost at persistence.");
    }
    private static void CleanupCoordination()
    {
        using var temp=new Temp();using var bitmap=new Bitmap(2,2);var file=new ImageOutputService(temp.Root,false).SavePng(bitmap,DateTime.Now);
        var recycle=new Recycle();var cleanup=new CleanupService(recycle:recycle);var plan=Plan(temp.Root);var candidate=cleanup.Scan(plan).Single();
        Task<IReadOnlyList<CleanupPathOutcome>> work;
        using(var lease=CompletedOutputStore.EnterDirectory(Path.GetDirectoryName(file)!))
        {
            work=Task.Run(()=>cleanup.ExecuteCandidates(plan,[candidate]));Thread.Sleep(60);
            Check(!work.IsCompleted&&recycle.Paths.Count==0,"Cleanup bypassed the product directory owner.");
            File.AppendAllText(file,"replacement");
        }
        Check(work.GetAwaiter().GetResult().Single().Status=="skipped"&&recycle.Paths.Count==0,"Waited cleanup did not recheck.");
    }
    private static void UninstallFake()
    {
        using var temp=new Temp();var paths=new ApplicationPathSet(temp.Root,Path.Combine(temp.Root,"data"),"unused","unused","release");
        foreach(var recycle in new[]{false,true})
        {
            var backend=new UninstallBackend();var result=new UninstallCleanupService(paths,Exe,backend).Execute(recycle);
            Check(result.FailedOutputCount==0&&backend.Events.Contains("startup")&&backend.Events.Contains("schedule")&&backend.Events.Contains("vault")&&backend.Events.Contains("data:"+paths.DataRoot),"Uninstall external action bypassed fake backend.");
            Check(backend.Events.Contains("outputs:"+Path.Combine(temp.Root,"Outputs"))==recycle,"Default output preservation changed.");
        }
        var bad=new UninstallBackend{Safe=false};Check(new UninstallCleanupService(paths,Exe,bad).Execute(true).FailedOutputCount==1&&bad.Events.Count==0,"Unsafe tree performed system mutations.");
        var failure=new UninstallBackend{FailRecycle=true};Check(new UninstallCleanupService(paths,Exe,failure).Execute(true).FailedOutputCount==1&&!failure.Events.Any(x=>x.StartsWith("data:")),"Failed recycle removed remaining data.");
    }
    private static void PriorCleanupCompanions()
    {
        foreach(var name in new[]{"DeletesOnlyExpiredCompletedClipRayOutputs","IncludesDedicatedLongCaptureOutputInCleanup",
            "KeepsCleanupBoundaryIncompleteRecoveryAndUnrelatedFiles","DeduplicatesCleanupRootsAndRemovesOnlyEmptyDateFolders",
            "KeepsOwnedLookingFilesBelowNonDateSubdirectories","RejectsReparsePointTraversalDuringCleanup","ContinuesCleanupAfterALockedFileFailure",
            "DoesNotBuildAScheduledCleanupPlanWhileDisabled","RoundTripsPrivacySafeCleanupRunStatus","FormatsLastCleanupExecutionForSettings"})
            typeof(Program).GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,null);
    }
    private static void SharedInstallMaintenance()
    {
        using var temp=new Temp();var paths=new ApplicationPathSet(temp.Root,Path.Combine(temp.Root,"shared-data"),"unused","unused","release");
        foreach(var mode in new[]{"sibling","unknown","access"})
        {
            var inventory=new ExitBackend{Failure=mode,NoTarget=true};var cleanup=new UninstallBackend();
            var result=new InstallMaintenance(inventory).ExecuteUninstallAsync(Exe,"shared-release",paths,false,TimeSpan.FromMilliseconds(100),cleanup).GetAwaiter().GetResult();
            Check(result is null&&cleanup.Events.Count==0&&inventory.Requests==0,"Target absent with sibling/unknown performed shared cleanup: "+mode);
        }
        var gone=new ExitBackend{NoTarget=true};var backend=new UninstallBackend();
        Check(new InstallMaintenance(gone).ExecuteUninstallAsync(Exe,"shared-release",paths,false,TimeSpan.FromMilliseconds(100),backend).GetAwaiter().GetResult() is not null
            &&gone.FindCalls==2&&gone.Requests==0&&backend.Events.Contains("vault"),"Confirmed absence could not resume the legitimate maintenance route.");
        var restarted=new ExitBackend{Failure="sibling-after",NoTarget=true};var untouched=new UninstallBackend();
        Check(new InstallMaintenance(restarted).ExecuteUninstallAsync(Exe,"shared-release",paths,false,TimeSpan.FromMilliseconds(100),untouched).GetAwaiter().GetResult() is null&&untouched.Events.Count==0,"Sibling appeared between confirmations and cleanup.");
    }
    private static void RecycleAdapter()
    {
        using var temp=new Temp();var file=Path.Combine(temp.Root,"owned-file.png");File.WriteAllBytes(file,[1]);
        foreach(var phase in new[]{"success","flags","delete","perform","aborted-check","aborted"})
        {
            var operation=new RecycleOperation{Failure=phase};var backend=new WindowsCleanupRecycleBackend(()=>operation);
            var failed=false;try{backend.RecycleFile(file);}catch{failed=true;}
            Check(failed==(phase!="success")&&operation.Disposed&&File.Exists(file),"Recycle adapter failed-open: "+phase);
            Check(operation.Flags==0x00180404&&(operation.Flags&0x10)==0&&(operation.Path is null)==(phase=="flags"),"Recycle-only/silent/noerror/earlyfailure contract changed.");
        }
        var directory=Path.Combine(temp.Root,"outputs");Directory.CreateDirectory(directory);var whole=new RecycleOperation();
        new WindowsCleanupRecycleBackend(()=>whole).RecycleDirectory(directory);
        Check(whole.Path==directory&&whole.Disposed&&Directory.Exists(directory),"Explicit whole-output recycle bypassed the strict adapter.");
    }
    private sealed class ExitBackend:IInstallExitBackend
    {
        public string Failure="";public int FindCalls,Requests;public bool NoTarget;public ExitHandle Handle=new(new(42,10,Exe));
        public InstallProcessInventory Find(string path)
        {
            FindCalls++;if(Failure=="access")throw new UnauthorizedAccessException();
            if(FindCalls==1)return new(NoTarget?[]:[Handle],Failure!="unknown",Failure=="sibling");
            return new(Failure=="restart"?[new ExitHandle(new(43,11,Exe))]:[],Failure!="unknown-after",Failure=="sibling-after");
        }
        public async Task<InstallExitResponse?> RequestAsync(string scope,InstallExitRequest request,TimeSpan timeout,CancellationToken token)
        {
            Requests++;if(Failure=="legacy")return null;if(Failure=="timeout")await Task.Delay(timeout+TimeSpan.FromMilliseconds(20),token);
            Handle.Exited=Failure!="alive";
            return new(1,Failure=="wrong-id"?Guid.NewGuid():request.RequestId,Failure=="wrong-target"?request.Target with{StartUtcTicks=11}:request.Target,Failure=="cancelled"?"cancelled-or-busy":"retired");
        }
    }
    private sealed class ExitHandle(InstallProcessIdentity identity):IInstallProcessHandle
    {
        public InstallProcessIdentity Identity=>identity;public bool Exited=true,Disposed;public int Waits;
        public Task<bool> WaitForExitAsync(TimeSpan timeout,CancellationToken token){Waits++;return Task.FromResult(Exited);}
        public void Dispose()=>Disposed=true;
    }
    internal sealed class Recycle:ICleanupRecycleBackend
    {
        public List<string> Paths=[];public string? FailPath;
        public void RecycleFile(string path){Paths.Add(path);if(path==FailPath)throw new IOException("fake recycle unavailable");}
    }
    private sealed class UninstallBackend:IUninstallCleanupBackend
    {
        public List<string> Events=[];public bool Safe=true,FailRecycle;
        public bool DirectoryExists(string path)=>true;public bool IsOrdinaryTree(string path)=>Safe;
        public void RemoveStartup(string path)=>Events.Add("startup");public void RemoveSchedule(string path)=>Events.Add("schedule");
        public void RemoveCredentials()=>Events.Add("vault");public void RemoveData(string path)=>Events.Add("data:"+path);
        public void RecycleOutputs(string path){Events.Add("outputs:"+path);if(FailRecycle)throw new IOException("fake");}
    }
    private sealed class RecycleOperation:IRecycleOperation
    {
        public string Failure="";public uint Flags;public string? Path;public bool Disposed;
        private int Result(string step)=>Failure==step?unchecked((int)0x80070005):0;
        public int SetFlags(uint flags){Flags=flags;return Result("flags");}
        public int Delete(string path){Path=path;return Result("delete");}
        public int Perform()=>Result("perform");
        public int AnyAborted(out bool aborted){aborted=Failure=="aborted";return Result("aborted-check");}
        public void Dispose()=>Disposed=true;
    }
    private sealed class Temp:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"clipray-task7-"+Guid.NewGuid().ToString("N"));
        public Temp()=>Directory.CreateDirectory(Root);
        public void Dispose(){Check(Root.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase)&&CompletedOutputStore.IsOrdinaryPath(Root),"Unsafe fixture root.");Directory.Delete(Root,true);}
    }
}
