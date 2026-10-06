namespace ClipRay.App.Overlay;

public enum DoubleEscapeResult
{
    ShowHint,
    Close,
}

public sealed class DoubleEscapePolicy
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);
    private DateTimeOffset? _firstPress;

    public DoubleEscapeResult Press(DateTimeOffset timestamp)
    {
        if (_firstPress is { } firstPress && timestamp - firstPress < Window)
        {
            _firstPress = null;
            return DoubleEscapeResult.Close;
        }

        _firstPress = timestamp;
        return DoubleEscapeResult.ShowHint;
    }

    public void Reset() => _firstPress = null;
}
