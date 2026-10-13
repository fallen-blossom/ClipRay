namespace ClipRay.App.Recording;

/// <summary>Bounded serialized audio, including the sample held by WriteSample.</summary>
public sealed class RecordingAudioPump
{
    private readonly object _sync = new();
    private readonly Queue<AudioRecordingSample> _queue = new();
    private readonly SemaphoreSlim _ready = new(0);
    private readonly AudioCaptureFormat _format;
    private readonly RecordingFaultSignal _faults;
    private readonly Task _consumer;
    private readonly int _maxBytes, _maxSamples;
    private int _heldBytes, _heldSamples;
    private bool _closed;
    public RecordingAudioPump(AudioCaptureFormat format, Func<AudioRecordingSample, ValueTask> write,
        RecordingFaultSignal faults, int? maxBytes = null, int maxSamples = 128)
    {
        if (format.SampleRate <= 0 || format.Channels <= 0 || format.BitsPerSample <= 0 || format.BlockAlign <= 0)
            throw new ArgumentOutOfRangeException(nameof(format));
        _format = format; _faults = faults; _maxBytes = maxBytes ?? checked(format.SampleRate * format.BlockAlign * 2);
        _maxSamples = maxSamples;
        if (_maxBytes <= 0 || maxSamples <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _consumer = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await _ready.WaitAsync().ConfigureAwait(false);
                    AudioRecordingSample sample;
                    lock (_sync) { if (_queue.Count == 0) { if (_closed) return; else continue; } sample = _queue.Dequeue(); }
                    try { await write(sample).ConfigureAwait(false); }
                    finally { lock (_sync) { _heldBytes -= sample.PcmBytes.Length; _heldSamples--; } }
                }
            }
            catch (Exception error) { _faults.TrySet("audio-write", error); }
            finally
            {
                lock (_sync) { _closed = true; while (_queue.TryDequeue(out var sample)) { _heldBytes -= sample.PcmBytes.Length; _heldSamples--; } }
            }
        });
    }
    public int HeldBytes { get { lock (_sync) return _heldBytes; } }
    public int HeldSamples { get { lock (_sync) return _heldSamples; } }
    public bool TryPost(ReadOnlySpan<byte> bytes, long timestamp)
    {
        Exception? failure = null;
        lock (_sync)
        {
            if (_closed) return false;
            if (bytes.Length <= 0 || bytes.Length % _format.BlockAlign != 0) failure = new InvalidOperationException("audio-block-alignment");
            else if (bytes.Length > _maxBytes - _heldBytes || _heldSamples >= _maxSamples) failure = new InvalidOperationException("audio-capacity-exceeded");
            else
            {
                var sample = new AudioRecordingSample(bytes.ToArray(), _format.SampleRate, _format.Channels, _format.BitsPerSample,
                    timestamp, checked((long)((decimal)(bytes.Length / _format.BlockAlign) * 10_000_000m / _format.SampleRate)));
                _queue.Enqueue(sample); _heldBytes += bytes.Length; _heldSamples++; _ready.Release(); return true;
            }
            _closed = true; _ready.Release();
        }
        _faults.TrySet("audio-admission", failure);
        return false;
    }
    public void RequestStop() { lock (_sync) { if (_closed) return; _closed = true; _ready.Release(); } }
    public Task CompleteAsync() { RequestStop(); return _consumer; }
}

/// <summary>Four fixed-size leases: encoder + newest two queued frames + capture callback.</summary>
public sealed class RecordingPixelBuffers
{
    private readonly object _sync = new();
    private readonly Stack<byte[]> _available = new();
    private readonly int _length;
    private int _allocated, _held;
    public RecordingPixelBuffers(int length) { if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length)); _length = length; }
    public int Held { get { lock (_sync) return _held; } }
    public byte[]? Rent()
    {
        lock (_sync)
        {
            byte[] bytes;
            if (_available.Count > 0) bytes = _available.Pop();
            else if (_allocated < 4) { bytes = new byte[_length]; _allocated++; }
            else return null;
            _held++; return bytes;
        }
    }
    public void Return(byte[] bytes) { lock (_sync) { _held--; _available.Push(bytes); } }
}
