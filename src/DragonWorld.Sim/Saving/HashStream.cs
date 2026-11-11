namespace DragonWorld.Sim.Saving;

/// <summary>
/// A write-only stream that keeps an FNV-1a hash of everything written to it, so the save writers
/// can fingerprint a game without building its bytes (<see cref="SaveGame.Hash"/>).
/// </summary>
internal sealed class HashStream : Stream
{
    public ulong Value { get; private set; } = 14695981039346656037UL;

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // FNV-1a over 8-byte words, then the leftover bytes: a different hash from byte-wise
        // FNV, but just as stable, and the save writers make the same calls on every peer.
        var h = Value;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buffer);
        foreach (var word in words)
        {
            h ^= word;
            h *= 1099511628211UL;
        }
        foreach (var b in buffer[(words.Length * 8)..])
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        Value = h;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void WriteByte(byte value) => Write([value]);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
