using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClipRay.App.Cleanup;
using ClipRay.App.Infrastructure;

internal static partial class BackgroundTask7Tests
{
    internal static (string Name,Action Test)[] CreateRevisionCases() => [("native-metadata",NativeMetadata),("inventory-maintenance",InventoryMaintenance),
            ("exit-control",()=>{ExitIdentities();ExitFailure();}),("recycle-control",RecycleAdapter)];

    internal static int RunRevision()
    {
        var cases=CreateRevisionCases();
        var failures=0;
        foreach(var item in cases)
        {
            try{item.Test();Console.WriteLine("PASS task-7-revision/"+item.Name);}
            catch(Exception error){failures++;Console.Error.WriteLine("FAIL task-7-revision/"+item.Name+": "+error);}
        }
        Console.WriteLine($"TASK_7_REVISION {cases.Length-failures}/{cases.Length} passed");return failures==0?0:1;
    }
    private static void NativeMetadata()
    {
        // SDK ShObjIdl_core.h declaration order, not the alphabetical API reference index.
        var shell=typeof(WindowsCleanupRecycleBackend).Assembly.GetType("ClipRay.App.Cleanup.ShellRecycleOperation")!;
        var type=shell.GetNestedType("IFileOperation",BindingFlags.NonPublic)!;
        var item=shell.GetNestedType("IShellItem",BindingFlags.NonPublic)!;
        Check(type.IsImport&&type.GUID==new Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")
            &&type.GetCustomAttribute<InterfaceTypeAttribute>()?.Value==ComInterfaceType.InterfaceIsIUnknown,"Native IFileOperation identity changed.");
        Check(item.IsImport&&item.GUID==new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")
            &&item.GetCustomAttribute<InterfaceTypeAttribute>()?.Value==ComInterfaceType.InterfaceIsIUnknown,"Shell item identity changed.");
        var ptr=typeof(IntPtr);var text=typeof(string);var word=typeof(uint);
        (string Name,Type[] Parameters)[] expected=[("Advise",[ptr,word.MakeByRefType()]),("Unadvise",[word]),
            ("SetOperationFlags",[word]),("SetProgressMessage",[text]),("SetProgressDialog",[ptr]),("SetProperties",[ptr]),
            ("SetOwnerWindow",[ptr]),("ApplyPropertiesToItem",[item]),("ApplyPropertiesToItems",[ptr]),
            ("RenameItem",[item,text,ptr]),("RenameItems",[ptr,text]),("MoveItem",[item,item,text,ptr]),
            ("MoveItems",[ptr,item]),("CopyItem",[item,item,text,ptr]),("CopyItems",[ptr,item]),
            ("DeleteItem",[item,ptr]),("DeleteItems",[ptr]),("NewItem",[item,word,text,text,ptr]),
            ("PerformOperations",[]),("GetAnyOperationsAborted",[typeof(bool).MakeByRefType()])];
        var actual=type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).OrderBy(x=>x.MetadataToken).ToArray();
        Check(actual.Length==20,"Native method count: expected 20, actual "+actual.Length);
        for(var i=0;i<expected.Length;i++)
        {
            var method=actual[i];var parameters=method.GetParameters();
            Check(method.Name==expected[i].Name&&method.ReturnType==typeof(int)
                &&(method.MethodImplementationFlags&MethodImplAttributes.PreserveSig)!=0
                &&parameters.Select(x=>x.ParameterType).SequenceEqual(expected[i].Parameters),"Native slot/signature mismatch at "+i);
            foreach(var parameter in parameters.Where(x=>x.ParameterType==text))
                Check(parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value==UnmanagedType.LPWStr,"Unicode marshal missing: "+method.Name);
            foreach(var parameter in parameters.Where(x=>x.ParameterType==item))
                Check(parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value==UnmanagedType.Interface,"Shell interface marshal missing: "+method.Name);
        }
        Check(actual[0].GetParameters()[1].IsOut,"DWORD cookie is not out.");
        var aborted=actual[19].GetParameters().Single();
        Check(aborted.IsOut&&aborted.GetCustomAttribute<MarshalAsAttribute>()?.Value==UnmanagedType.Bool,"Native BOOL out marshal missing.");
        var factory=shell.GetMethod("SHCreateItemFromParsingName",BindingFlags.NonPublic|BindingFlags.Static)!;
        var import=factory.GetCustomAttribute<DllImportAttribute>();
        Check(import?.CharSet==CharSet.Unicode&&import.ExactSpelling&&import.PreserveSig&&import.Value=="shell32.dll"
            &&factory.ReturnType==typeof(int)&&factory.GetParameters().Select(x=>x.ParameterType).SequenceEqual([text,ptr,typeof(Guid).MakeByRefType(),item.MakeByRefType()])
            &&factory.GetParameters()[0].GetCustomAttribute<MarshalAsAttribute>()?.Value==UnmanagedType.LPWStr
            &&factory.GetParameters()[3].IsOut&&factory.GetParameters()[3].GetCustomAttribute<MarshalAsAttribute>()?.Value==UnmanagedType.Interface,
            "Shell item factory marshal missing.");
    }
    private static void InventoryMaintenance()
    {
        using var temp=new Temp();
        var target=Path.Combine(temp.Root,"release-b","app","ClipRay.App.exe");
        var releaseA=Path.Combine(temp.Root,"release-a","app","ClipRay.App.exe");
        var devRoot=Path.Combine(temp.Root,"development-a");Directory.CreateDirectory(devRoot);File.WriteAllText(Path.Combine(devRoot,"ClipRay.sln"),"fixture");
        var devA=Path.Combine(devRoot,"bin","ClipRay.App.exe");var localData=Path.Combine(temp.Root,"localdata");
        Check(ApplicationPaths.Create(Path.GetDirectoryName(target)!,localData).ProfileId=="release"
            &&ApplicationPaths.Create(Path.GetDirectoryName(devA)!,localData).ProfileId=="development","Cross-profile fixture not genuine.");
        foreach(var mode in new[]{"same-profile","cross-profile","other-user","other-user-inaccessible-identity","unknown-user","unknown-identity","access","exited","self","all-gone","appeared-after","target"})
        {
            var source=new InventorySource{LocalDataRoot=localData};var requests=0;var cleanup=new UninstallBackend();
            source.Create=call=>mode=="all-gone"?[]:mode=="appeared-after"&&call==1?[]:
                [new Observation(mode=="target"?target:mode=="same-profile"?releaseA:devA){Sid=mode.StartsWith("other-user")?"other-user":"current-user",
                    Failure=mode=="other-user-inaccessible-identity"?"unknown-identity":mode,Exited=mode=="exited",Self=mode=="self"}];
            var backend=new WindowsInstallExitBackend(source,(_,request,_,_)=>
            {requests++;Check(request.Target.ExecutablePath==target,"Requested sibling exit.");return Task.FromResult<InstallExitResponse?>(new(1,request.RequestId,request.Target,"retired"));});
            var paths=new ApplicationPathSet(Path.GetDirectoryName(Path.GetDirectoryName(target)!)!,Path.Combine(localData,"ClipRay"),"unused","unused","release");
            // After retained target handle exit, the second inventory is genuinely empty.
            if(mode=="target")source.Create=call=>call==1?[new Observation(target)]:[];
            var result=new InstallMaintenance(backend).ExecuteUninstallAsync(target,"user.release",paths,true,TimeSpan.FromMilliseconds(100),cleanup).GetAwaiter().GetResult();
            var blocked=mode is "same-profile" or "cross-profile" or "unknown-user" or "unknown-identity" or "access" or "appeared-after";
            Check((result is null)==blocked,"Actual Windows classifier allowed unsafe shared cleanup: "+mode);
            Check(requests==(mode=="target"?1:0),"Unexpected target/sibling request count: "+mode);
            Check(blocked?cleanup.Events.Count==0:cleanup.Events.Contains("vault")&&cleanup.Events.Contains("schedule")&&cleanup.Events.Contains("startup")
                &&cleanup.Events.Any(x=>x.StartsWith("data:"))&&cleanup.Events.Any(x=>x.StartsWith("outputs:")),"Uninstall fake effect boundary: "+mode);
            Check(source.Observed.All(x=>x.Disposed&&(!x.Retained||x.Handle.Disposed)),"Observation / retained handle leaked: "+mode);
        }
    }
    private sealed class InventorySource:IInstallProcessInventorySource
    {
        public string CurrentUserSid=>"current-user";public string LocalDataRoot{get;set;}="";
        public Func<int,Observation[]> Create=_=>[];public int Calls;public List<Observation> Observed=[];
        public IEnumerable<IInstallProcessObservation> Enumerate(string name)
        {var observations=Create(++Calls);Observed.AddRange(observations);return observations;}
    }
    private sealed class Observation(string path):IInstallProcessObservation
    {
        public bool Self,Exited,Disposed,Retained;public string Sid="current-user",Failure="";
        public ExitHandle Handle=new(new(42,10,path));
        public bool IsSelf=>Self;public bool HasExited=>Failure=="access"?throw new UnauthorizedAccessException():Exited;
        public string UserSid=>Failure=="unknown-user"?throw new IOException("unknown user"):Sid;
        public InstallProcessIdentity Identity=>Failure=="unknown-identity"?throw new IOException("unknown identity"):Handle.Identity;
        public IInstallProcessHandle RetainHandle(){Retained=true;return Handle;}
        public void Dispose()=>Disposed=true;
    }
}
