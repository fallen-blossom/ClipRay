namespace ClipRay.TestLab;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => TestLabRunner.RunAsync(args, CancellationToken.None)
        .GetAwaiter().GetResult();
}
