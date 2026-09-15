using System.Runtime.InteropServices;

namespace PartitionManager.Helpers;

/// <summary>Sector-aligned buffer for unbuffered disk I/O.</summary>
internal sealed unsafe class AlignedBuffer : IDisposable
{
    private void* _ptr;

    public AlignedBuffer(int length, int alignment = 4096)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
        _ptr = NativeMemory.AlignedAlloc((nuint)length, (nuint)alignment);
        if (_ptr is null)
            throw new OutOfMemoryException();
    }

    public int Length { get; }

    public Span<byte> Span => new(_ptr, Length);

    public void Dispose()
    {
        if (_ptr is null)
            return;
        NativeMemory.AlignedFree(_ptr);
        _ptr = null;
        GC.SuppressFinalize(this);
    }
}
