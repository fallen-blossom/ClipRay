using System.Drawing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

// Borrowed pixels: BitmapSourceFactory copies them before the next update.
internal sealed class EditPreviewCache : IDisposable
{
    private Bitmap? _source,_committed,_active;
    private PixelRect _selection;
    private EditLayer? _layer;
    private EditOperation[] _operations=[];
    private PixelPoint[] _points=[];
    private int _brush,_block;
    private MosaicPaintState? _paint;
    internal long ReplayedOperations { get; private set; }
    internal long ActivePoints { get; private set; }
    internal Bitmap GetOverlay(Bitmap source,EditLayer layer,MosaicStroke? active)
    {
        var operations=layer.Operations;
        var same=ReferenceEquals(source,_source)&&ReferenceEquals(layer,_layer)&&layer.Selection==_selection;
        var prefix=same&&operations.Count>=_operations.Length&&_operations.Select((op,i)=>ReferenceEquals(op,operations[i])).All(x=>x);
        var unchanged=prefix&&operations.Count==_operations.Length;
        if(!unchanged)
        {
            ClearActive();
            if(!prefix)
            {
                _committed?.Dispose();
                using var empty=EditRenderer.RenderOverlay(source,new EditLayer(layer.Selection));
                _committed=new Bitmap(empty);
                _operations=[];
            }
            for(var i=_operations.Length;i<operations.Count;i++)
            { EditRenderer.DrawOperation(source,_committed!,operations[i],layer.Selection);ReplayedOperations++; }
            _operations=operations.ToArray();_source=source;_layer=layer;_selection=layer.Selection;
        }
        if(active is null) { ClearActive();return _committed!; }
        var continues=_active is not null&&_brush==active.BrushSize&&_block==active.BlockSize
            &&active.Points.Count>=_points.Length&&_points.Select((p,i)=>p==active.Points[i]).All(x=>x);
        if(!continues)
        {
            ClearActive();_active=new Bitmap(_committed!);_paint=new();_brush=active.BrushSize;_block=active.BlockSize;
        }
        var added=active.Points.Skip(_points.Length).ToArray();
        if(added.Length>0) { EditRenderer.DrawMosaicStroke(source,_active!,new(added,active.BrushSize,active.BlockSize),layer.Selection,_paint);ActivePoints+=added.Length; }
        _points=active.Points.ToArray();return _active!;
    }
    private void ClearActive() { _active?.Dispose();_active=null;_paint?.Dispose();_paint=null;_points=[]; }
    public void Dispose() { ClearActive();_committed?.Dispose();_committed=null;_source=null;_layer=null; }
}
