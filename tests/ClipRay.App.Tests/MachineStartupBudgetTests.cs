using System.Diagnostics;
using System.IO;
using ClipRay.App.Translation;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Settings;

internal static class MachineStartupBudgetTests
{
    internal static void CoordinatorWaitsForWarmup()
    {
        var coordinator = new LiveTranslationStartupCoordinator();
        var warming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var task = coordinator.RunAsync(new(0,0,100,100),TranslationRouteKind.Machine,
            AppSettings.CreateDefault(Path.GetTempPath()),"zh",_=>throw new Exception("Unexpected region selection."),
            _=>warming.Task,(_,_)=>{Interlocked.Increment(ref starts);return Task.CompletedTask;});
        if(task.IsCompleted || starts!=0) throw new Exception("Live session started before required model warmup.");
        warming.SetResult();
        task.GetAwaiter().GetResult();
        if(starts!=1) throw new Exception("Warm live session did not start exactly once.");
        warming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        task=coordinator.RunAsync(new(0,0,100,100),TranslationRouteKind.Machine,
            AppSettings.CreateDefault(Path.GetTempPath()),"zh",_=>throw new Exception("Unexpected region selection."),
            _=>warming.Task,(_,_)=>{Interlocked.Increment(ref starts);return Task.CompletedTask;});
        coordinator.Stop();
        warming.SetResult();
        task.GetAwaiter().GetResult();
        if(starts!=1) throw new Exception("Canceled warmup started an obsolete live session.");
    }

    internal static int Run(string[] args)
    {
        if(args.Length!=2) throw new ArgumentException("Python runtime path required.");
        using var temp = new BackgroundTask6Tests.Temp();
        var script=Path.Combine(temp.Root,"worker.py");
        var model=Path.Combine(temp.Root,"model");Directory.CreateDirectory(model);
        File.WriteAllText(script,"""
import json, sys, time, os
from pathlib import Path
Path(sys.argv[1], 'pid.txt').write_text(str(os.getpid()))
for line in sys.stdin:
    request=json.loads(line)
    texts=request['texts']
    if texts == ['Ready.']: time.sleep(.35)
    if texts == ['slow']: time.sleep(.8)
    print(json.dumps({'texts':['准备' if text=='Ready.' else '你好' for text in texts],
        'backend':{'device':'cpu','computeType':'int8','verified':True,'fallbackReason':None}}),flush=True)
""");
        using var process=new BundledMachineTranslationProcess(Path.GetFullPath(args[1]),script,model,
            workerTimeout:TimeSpan.FromMilliseconds(250),keepAlive:true,startupTimeout:TimeSpan.FromSeconds(3));
        var request=new MachineTranslationProcessRequest("en","zh",["hello"]);
        var result=process.TranslateAsync(request,default).GetAwaiter().GetResult();
        if(result.Single()!="你好" || process.ProcessStartCount!=1) throw new Exception("Startup consumed the short serve budget.");
        var oldPid=int.Parse(File.ReadAllText(Path.Combine(model,"pid.txt")));
        try { process.TranslateAsync(new("en","zh",["slow"]),default).GetAwaiter().GetResult(); throw new Exception("Serve budget was disabled by warmup."); }
        catch(TimeoutException) { }
        try { using var old=Process.GetProcessById(oldPid); if(!old.HasExited) throw new Exception("Timed out child still owns native resources."); }
        catch(ArgumentException) { }
        result=process.TranslateAsync(request,default).GetAwaiter().GetResult();
        if(result.Single()!="你好" || process.ProcessStartCount!=2) throw new Exception("Healthy restart did not reverify the startup owner.");
        process.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("MACHINE_STARTUP_BUDGET_TESTS_PASSED");
        return 0;
    }
}
