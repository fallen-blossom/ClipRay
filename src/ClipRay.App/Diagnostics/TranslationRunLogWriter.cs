using System.Text;
using System.Text.Json;
using System.IO;

namespace ClipRay.App.Diagnostics;

public interface ITranslationRunLogWriter
{
    void Write(TranslationRunLog entry);
}

public sealed class JsonlTranslationRunLogWriter : ITranslationRunLogWriter
{
    private readonly object _lock = new();
    private readonly string _diagnosticsRoot;

    public JsonlTranslationRunLogWriter(string diagnosticsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsRoot);
        _diagnosticsRoot = diagnosticsRoot;
    }

    public void Write(TranslationRunLog entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            var fileName = $"translation-{entry.StartedUtc:yyyy-MM-dd}.jsonl";
            var path = Path.Combine(_diagnosticsRoot, fileName);
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            lock (_lock)
            {
                Directory.CreateDirectory(_diagnosticsRoot);
                File.AppendAllText(path, json + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (Exception)
        {
            // Diagnostics are strictly best effort. Keep failures inside this boundary
            // so logging can never alter translation behavior.
        }
    }
}
