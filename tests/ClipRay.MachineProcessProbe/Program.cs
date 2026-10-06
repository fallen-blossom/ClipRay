using System.Text.Json;
using ClipRay.App.Translation;

if (args.Length != 4) return 2;
string? text = null;
string? error = null;
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
try
{
    var worker = new BundledMachineTranslationProcess(args[0], args[1], Path.GetDirectoryName(args[1])!,
        args[2] == "timeout" ? TimeSpan.FromSeconds(1) : null);
    var result = await worker.TranslateAsync(new MachineTranslationProcessRequest("en", "zh", [args[2], args[3] + ".pid"]), stop.Token);
    text = result.Single();
}
catch (Exception exception)
{
    error = exception.GetType().Name;
}
File.WriteAllText(args[3], JsonSerializer.Serialize(new { Text = text, Error = error }));
return 0;
