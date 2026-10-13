using System.Drawing;
using ClipRay.App.Scrolling;

internal static partial class BackgroundTask6Tests
{
    private static void NativeInputIdentityAndCoordinates()
    {
        foreach(var change in new[]{"moved","process","thread","class","hidden","minimized","missing","covered","unrelated","post","range"})
        {
            var native=new FakeScrollNative();var service=new ScrollInputService(new(2),native);var point=new Point(-20,-15);
            switch(change)
            {
                case "moved":native.Windows[1]=native.Windows[1] with{Bounds=new(-299,-100,600,400)};break;
                case "process":native.Windows[2]=native.Windows[2] with{ProcessId=42};break;
                case "thread":native.Windows[2]=native.Windows[2] with{ThreadId=42};break;
                case "class":native.Windows[2]=native.Windows[2] with{WindowClass="replaced"};break;
                case "hidden":native.Windows[1]=native.Windows[1] with{Visible=false};break;
                case "minimized":native.Windows[1]=native.Windows[1] with{Minimized=true};break;
                case "missing":native.Windows.Remove(2);break;
                case "covered":native.AtPoint=new(4);break;
                case "unrelated":native.AtPoint=new(5);break;
                case "post":native.RejectPost=true;break;
                case "range":point=new(40000,0);break;
            }
            Reject(()=>service.SendWheelAsync(point,-120,CancellationToken.None).GetAwaiter().GetResult());
            Check(native.Posts==(change=="post"?1:0),"Changed/invalid source received wheel input.");
        }
        foreach(var receiver in new[]{1,2,3})
        {
            var native=new FakeScrollNative();var service=new ScrollInputService(new(2),native);native.AtPoint=new(receiver);
            service.SendWheelAsync(new(-20,-15),-120,CancellationToken.None).GetAwaiter().GetResult();
            Check(native.Posts==1&&native.LastHandle==new IntPtr(receiver)&&native.Message==0x020A
                &&unchecked((short)(native.WParam.ToInt64()>>16))==-120
                &&unchecked((short)(native.LParam.ToInt64()&0xffff))==-20
                &&unchecked((short)((native.LParam.ToInt64()>>16)&0xffff))==-15,"Legitimate child/parent or signed physical coordinates were altered.");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            try{service.SendWheelAsync(new(-20,-15),-120,cancelled.Token);throw new Exception("Cancelled input accepted.");}catch(OperationCanceledException){}
            service.SendWheelAsync(new(-20,-15),0,CancellationToken.None).GetAwaiter().GetResult();
            Check(native.Posts==1,"Cancel/zero sent input.");
        }
    }
    private static void InvalidSourcePausesWithPieces()
    {
        using var temp=new Temp();var polls=0;ScrollCaptureSession? session=null;
        var native=new FakeScrollNative();var input=new ScrollInputService(new(2),native);native.Windows.Remove(2);
        using(session=new(new(-32,-27,24,24),ScrollCaptureMode.Automatic,new FixtureCapture(),input,new Settler(()=>
        {if(++polls==3)session!.RequestCompletion();}),temp.Root,new ScrollCaptureOptions(SettleDelay:TimeSpan.Zero)))
        using(var result=session.RunAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            Check(!session.AutomaticScrollingEnabled&&native.Posts==0&&result.AcceptedFrameCount==1,"Invalid source lost first frame or continued automatic input.");
            using var image=new ScrollCaptureRecovery(temp.Root).CreateComposite(result.TempSessionDirectory);AssertPattern(image,24,24);
        }
    }
    private sealed class FakeScrollNative : IScrollWindowNative
    {
        public readonly Dictionary<int,ScrollWindowSnapshot> Windows=new()
        {
            [1]=new(new(1),10,20,new(-300,-100,600,400),true,false,"root"),
            [2]=new(new(2),10,20,new(-50,-25,100,100),true,false,"child"),
            [3]=new(new(3),10,20,new(-50,-25,100,100),true,false,"nested"),
            [4]=new(new(4),11,21,new(-300,-100,600,400),true,false,"other"),
            [5]=new(new(5),10,20,new(-300,-100,600,400),true,false,"unrelated")
        };
        public IntPtr AtPoint=new(2);public bool RejectPost;public int Posts;public IntPtr LastHandle,WParam,LParam;public uint Message;
        public ScrollWindowSnapshot? Observe(IntPtr handle)=>Windows.GetValueOrDefault(handle.ToInt32());
        public IntPtr WindowAt(Point point)=>AtPoint;
        public IntPtr Root(IntPtr handle)=>new(1);
        public bool IsChild(IntPtr parent,IntPtr child)=>parent==new IntPtr(1)&&(child==new IntPtr(2)||child==new IntPtr(3));
        public bool Post(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam){Posts++;LastHandle=handle;Message=message;WParam=wParam;LParam=lParam;return !RejectPost;}
    }
}
