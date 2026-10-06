using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClipRay.App.Translation;

namespace ClipRay.App.Acceptance;

public enum RegionAcceptanceMode
{
    Original,
    Translated,
    Long,
    Live,
}

public sealed record RegionAcceptanceCommand(
    Rectangle Region,
    RegionAcceptanceMode Mode,
    string OutputRoot,
    TranslationRouteKind Route,
    int Iterations,
    int? ForegroundProcessId)
{
    public const string RegionArgument = "--acceptance-region";

    public static bool IsRequested(IEnumerable<string> arguments) =>
        arguments.Contains(RegionArgument, StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out RegionAcceptanceCommand? command,
        out string error)
    {
        command = null;
        error = string.Empty;
        if (!IsRequested(arguments)) return false;
        try
        {
            var regionIndex = IndexOf(arguments, RegionArgument);
            if (regionIndex < 0 || regionIndex + 4 >= arguments.Count)
                return Fail("acceptance-region-requires-four-integers", out error);
            if (!TryInteger(arguments[regionIndex + 1], out var left)
                || !TryInteger(arguments[regionIndex + 2], out var top)
                || !TryInteger(arguments[regionIndex + 3], out var width)
                || !TryInteger(arguments[regionIndex + 4], out var height)
                || width is < 1 or > 16384
                || height is < 1 or > 16384)
                return Fail("acceptance-region-invalid", out error);

            if (!TryValue(arguments, "--acceptance-mode", out var modeValue)
                || !Enum.TryParse<RegionAcceptanceMode>(modeValue, ignoreCase: true, out var mode))
                return Fail("acceptance-mode-invalid", out error);
            if (!TryValue(arguments, "--acceptance-output-root", out var outputRoot)
                || !Path.IsPathFullyQualified(outputRoot))
                return Fail("acceptance-output-root-must-be-absolute", out error);

            var route = TranslationRouteKind.Machine;
            if (TryValue(arguments, "--acceptance-route", out var routeValue)
                && !TryRoute(routeValue, out route))
                return Fail("acceptance-route-invalid", out error);

            var iterations = mode is RegionAcceptanceMode.Long or RegionAcceptanceMode.Live ? 3 : 1;
            if (TryValue(arguments, "--acceptance-iterations", out var iterationValue)
                && (!TryInteger(iterationValue, out iterations) || iterations is < 1 or > 180))
                return Fail("acceptance-iterations-invalid", out error);

            int? foregroundProcessId = null;
            if (TryValue(arguments, "--acceptance-foreground-pid", out var processIdValue))
            {
                if (!TryInteger(processIdValue, out var parsedProcessId) || parsedProcessId < 1)
                    return Fail("acceptance-foreground-pid-invalid", out error);
                foregroundProcessId = parsedProcessId;
            }

            command = new RegionAcceptanceCommand(
                new Rectangle(left, top, width, height),
                mode,
                Path.GetFullPath(outputRoot),
                route,
                iterations,
                foregroundProcessId);
            return true;
        }
        catch
        {
            return Fail("acceptance-command-invalid", out error);
        }
    }

    private static bool TryRoute(string value, out TranslationRouteKind route)
    {
        route = value.ToLowerInvariant() switch
        {
            "machine" => TranslationRouteKind.Machine,
            "local-lmt" => TranslationRouteKind.LocalLmt,
            "deepseek-flash" => TranslationRouteKind.DeepSeekFlash,
            _ => (TranslationRouteKind)(-1),
        };
        return Enum.IsDefined(route);
    }

    private static bool TryValue(IReadOnlyList<string> arguments, string name, out string value)
    {
        var index = IndexOf(arguments, name);
        if (index >= 0 && index + 1 < arguments.Count)
        {
            value = arguments[index + 1];
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count; index++)
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private static bool TryInteger(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}

public static class AcceptanceForegroundWindow
{
    public static void EnsureForeground(int processId)
    {
        if (!IsForeground(processId))
            throw new InvalidOperationException("acceptance-source-not-foreground");
    }

    public static bool IsForeground(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var handle = process.MainWindowHandle;
            return handle != IntPtr.Zero && !IsIconic(handle) && GetForegroundWindow() == handle;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

public sealed record RegionAcceptanceResult(
    string SchemaVersion,
    string Mode,
    string Route,
    int Left,
    int Top,
    int Width,
    int Height,
    int Iterations,
    int AcceptedFrameCount,
    int OutputWidth,
    int OutputHeight,
    string? OutputArtifactId,
    string Status,
    string ErrorCategory)
{
    public const string CurrentSchemaVersion = "clipray.region-acceptance.v1";
}
