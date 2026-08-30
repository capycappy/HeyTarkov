namespace HeyTarkov;

/// <summary>
/// A growing buffer of captured audio, presented as the kind of Stream
/// System.Speech will actually read from. Two of its properties are
/// load-bearing, both verified with --streamdiag:
///
///  * CanSeek must be true. From a non-seekable stream the recognizer returns
///    nothing at all, with no error.
///  * Read must fill the whole request. A short read is taken as end-of-audio,
///    so returning "just what has arrived so far" ends the utterance instantly
///    and nothing is ever recognized.
///
/// So Read blocks until it can satisfy the request, and only returns less than
/// asked when capture has actually finished.
/// </summary>
public sealed class LiveAudioStream : Stream
{
    private readonly List<byte> _data;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _dataReady = new(false);

    private int _position;
    private bool _finished;
    private bool _disposed;

    public LiveAudioStream(int initialCapacity = 0) => _data = new List<byte>(initialCapacity);

    /// <summary>Bytes captured so far, including any trailing silence.</summary>
    public int Captured
    {
        get { lock (_gate) return _data.Count; }
    }

    public bool IsFinished
    {
        get { lock (_gate) return _finished; }
    }

    /// <summary>
    /// A copy of everything captured. Kept so a failed live recognition can be
    /// retried against the same audio as a wave file.
    /// </summary>
    public byte[] Snapshot()
    {
        lock (_gate) return _data.ToArray();
    }

    public void Append(byte[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (_finished) return;
            for (var i = 0; i < count; i++) _data.Add(buffer[offset + i]);
        }

        _dataReady.Set();
    }

    /// <summary>No more audio is coming. Pending and future reads drain, then end.</summary>
    public void Finish(int trailingSilenceBytes = 0)
    {
        lock (_gate)
        {
            if (_finished) return;
            for (var i = 0; i < trailingSilenceBytes; i++) _data.Add(0);
            _finished = true;
        }

        _dataReady.Set();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var filled = 0;

        while (filled < count)
        {
            lock (_gate)
            {
                var available = _data.Count - _position;

                if (available > 0)
                {
                    var taken = Math.Min(count - filled, available);
                    _data.CopyTo(_position, buffer, offset + filled, taken);
                    _position += taken;
                    filled += taken;
                    continue;
                }

                // Only ever return a short read once the audio has really ended.
                if (_finished || _disposed) break;
                _dataReady.Reset();
            }

            // Bounded, so a stalled device cannot hang the recognizer's thread.
            _dataReady.Wait(200);
        }

        return filled;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Finish();
            _dataReady.Dispose();
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanWrite => false;

    /// <summary>Must be true; see the type-level comment.</summary>
    public override bool CanSeek => true;

    /// <summary>
    /// Open-ended. The recognizer reads Length once, up front, and will not read
    /// past it, so a live capture cannot report the bytes it happens to hold yet.
    /// </summary>
    public override long Length => long.MaxValue;

    public override long Position
    {
        get { lock (_gate) return _position; }
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_gate)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _data.Count + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            _position = (int)Math.Clamp(target, 0, _data.Count);
            return _position;
        }
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
