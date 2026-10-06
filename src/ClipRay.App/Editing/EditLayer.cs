using System.Drawing;
using System.Runtime.InteropServices;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public enum EditTool
{
    None,
    Pen,
    Mosaic,
    Picker,
}

public sealed class EditLayer
{
    private readonly List<PenStroke> _penStrokes = [];
    private readonly List<MosaicStroke> _mosaicStrokes = [];
    private readonly List<EditOperation> _operations = [];
    private readonly List<EditOperation> _redoOperations = [];

    public EditLayer(PixelRect selection)
    {
        Selection = selection;
    }

    public PixelRect Selection { get; private set; }

    public EditTool ActiveTool { get; private set; }

    public Color PenColor { get; private set; } = Color.Red;

    public float PenWidth { get; set; } = 3;

    public int MosaicBrushSize { get; set; } = 16;

    public int MosaicBlockSize { get; set; } = 12;

    public IReadOnlyList<PenStroke> PenStrokes => _penStrokes;

    public IReadOnlyList<MosaicStroke> MosaicStrokes => _mosaicStrokes;

    internal IReadOnlyList<EditOperation> Operations => _operations;

    public void SwitchTool(EditTool tool) => ActiveTool = tool;

    public void AddPenStroke(PenStroke stroke)
    {
        AddOperation(new PenEditOperation(stroke));
        _redoOperations.Clear();
    }

    public void AddMosaicStroke(MosaicStroke stroke)
    {
        AddOperation(new MosaicEditOperation(stroke));
        _redoOperations.Clear();
    }

    public bool UndoLast()
    {
        if (_operations.Count == 0) return false;
        var operation = _operations[^1];
        _operations.RemoveAt(_operations.Count - 1);
        RemoveOperation(operation);
        _redoOperations.Add(operation);
        return true;
    }

    public bool RedoLast()
    {
        if (_redoOperations.Count == 0) return false;
        var operation = _redoOperations[^1];
        _redoOperations.RemoveAt(_redoOperations.Count - 1);
        AddOperation(operation);
        return true;
    }

    private void AddOperation(EditOperation operation)
    {
        _operations.Add(operation);
        if (operation is PenEditOperation pen) _penStrokes.Add(pen.Stroke);
        if (operation is MosaicEditOperation mosaic) _mosaicStrokes.Add(mosaic.Stroke);
    }

    private void RemoveOperation(EditOperation operation)
    {
        if (operation is PenEditOperation pen) _penStrokes.Remove(pen.Stroke);
        if (operation is MosaicEditOperation mosaic) _mosaicStrokes.Remove(mosaic.Stroke);
    }

    public void SetPenColor(Color color) => PenColor = color;

    public void UpdateSelection(PixelRect selection) => Selection = selection;

    public string SampleSourceColor(Bitmap source, PixelPoint point)
    {
        var x = Math.Clamp((int)Math.Floor(point.X), 0, source.Width - 1);
        var y = Math.Clamp((int)Math.Floor(point.Y), 0, source.Height - 1);
        PenColor = source.GetPixel(x, y);
        return $"#{PenColor.R:X2}{PenColor.G:X2}{PenColor.B:X2}";
    }
}

internal abstract record EditOperation;

internal sealed record PenEditOperation(PenStroke Stroke) : EditOperation;

internal sealed record MosaicEditOperation(MosaicStroke Stroke) : EditOperation;

public interface IClipboardTextSink
{
    void SetText(string text);
}

public interface IColorClipboardTextSink : IClipboardTextSink
{
    void SetColorText(string text);
}

public readonly record struct SourceColorPickResult(string Text, bool Copied);

public sealed class SourceColorPicker
{
    private const int MaximumCopyAttempts = 3;
    private readonly EditLayer _layer;
    private readonly IClipboardTextSink _clipboard;
    private readonly Action<TimeSpan> _delay;

    public SourceColorPicker(EditLayer layer, IClipboardTextSink clipboard, Action<TimeSpan>? delay = null)
    {
        _layer = layer;
        _clipboard = clipboard;
        _delay = delay ?? Thread.Sleep;
    }

    public SourceColorPickResult Pick(Bitmap source, PixelPoint point)
    {
        var text = _layer.SampleSourceColor(source, point);
        for (var attempt = 0; attempt < MaximumCopyAttempts; attempt++)
        {
            try
            {
                if (_clipboard is IColorClipboardTextSink colorClipboard)
                    colorClipboard.SetColorText(text);
                else
                    _clipboard.SetText(text);
                return new SourceColorPickResult(text, Copied: true);
            }
            catch (ExternalException) when (attempt < MaximumCopyAttempts - 1)
            {
                _delay(TimeSpan.FromMilliseconds(25));
            }
            catch (ExternalException)
            {
                return new SourceColorPickResult(text, Copied: false);
            }
        }

        return new SourceColorPickResult(text, Copied: false);
    }
}
