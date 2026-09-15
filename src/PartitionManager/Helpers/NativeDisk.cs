using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartitionManager.Helpers;

/// <summary>Raw reads and writes on physical disks and volumes.</summary>
internal static class NativeDisk
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint ShareReadWrite = 0x3;
    public const uint OpenExisting = 3;
    public const uint NoBuffering = 0x20000000;

    public const uint FsctlLockVolume = 0x00090018;
    public const uint FsctlDismountVolume = 0x00090020;
    public const uint FsctlUnlockVolume = 0x0009001C;
    public const uint FsctlGetVolumeBitmap = 0x0009006F;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle h,
        uint code,
        byte[]? inBuffer,
        int inSize,
        byte[]? outBuffer,
        int outSize,
        out int returned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(
        string path,
        out uint sectorsPerCluster,
        out uint bytesPerSector,
        out uint freeClusters,
        out uint totalClusters);

    public static SafeFileHandle OpenPhysical(int diskNumber, bool write)
    {
        var access = GenericRead | (write ? GenericWrite : 0);
        var handle = CreateFileW(
            $@"\\.\PhysicalDrive{diskNumber}",
            access,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            NoBuffering,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Cannot open disk {diskNumber}: {Marshal.GetLastWin32Error()}");
        return handle;
    }

    public static SafeFileHandle OpenVolume(char letter, bool write)
    {
        var access = GenericRead | (write ? GenericWrite : 0);
        var handle = CreateFileW(
            $@"\\.\{letter}:",
            access,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Cannot open volume {letter}: {Marshal.GetLastWin32Error()}");
        return handle;
    }

    public static void Read(SafeFileHandle handle, long offset, byte[] buffer, int count) =>
        Read(handle, offset, buffer.AsSpan(0, count));

    public static void Write(SafeFileHandle handle, long offset, byte[] buffer, int count) =>
        Write(handle, offset, buffer.AsSpan(0, count));

    public static void Read(SafeFileHandle handle, long offset, Span<byte> buffer)
    {
        var got = 0;
        while (got < buffer.Length)
        {
            var n = RandomAccess.Read(handle, buffer[got..], offset + got);
            if (n <= 0)
                throw new IOException("Read failed (end of disk).");
            got += n;
        }
    }

    public static void Write(SafeFileHandle handle, long offset, ReadOnlySpan<byte> buffer) =>
        RandomAccess.Write(handle, buffer, offset);

    public static bool TryLock(SafeFileHandle volume)
    {
        try
        {
            return DeviceIoControl(volume, FsctlLockVolume, null, 0, null, 0, out _, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    public static void TryDismount(SafeFileHandle volume)
    {
        try
        {
            DeviceIoControl(volume, FsctlDismountVolume, null, 0, null, 0, out _, IntPtr.Zero);
        }
        catch
        {
            // dest may already be unmounted
        }
    }

    public static void TryUnlock(SafeFileHandle volume)
    {
        try
        {
            DeviceIoControl(volume, FsctlUnlockVolume, null, 0, null, 0, out _, IntPtr.Zero);
        }
        catch
        {
            // ignore
        }
    }

    public static bool TryClusterInfo(char letter, out uint bytesPerCluster, out ulong totalClusters)
    {
        bytesPerCluster = 0;
        totalClusters = 0;
        if (!GetDiskFreeSpaceW($@"{letter}:\", out var spc, out var bps, out _, out var total))
            return false;
        bytesPerCluster = spc * bps;
        totalClusters = total;
        return bytesPerCluster > 0;
    }

    public static bool TryVolumeBitmap(char letter, out byte[] bits, out ulong bitCount)
    {
        bits = [];
        bitCount = 0;
        using var volume = OpenVolume(letter, write: false);
        using var stream = new MemoryStream();
        var start = new byte[8];
        var output = new byte[1024 * 1024];
        ulong lcn = 0;

        while (true)
        {
            BitConverter.TryWriteBytes(start, (long)lcn);
            if (!DeviceIoControl(volume, FsctlGetVolumeBitmap, start, start.Length, output, output.Length, out var returned, IntPtr.Zero))
                break;
            if (returned < 16)
                break;

            var startingLcn = (ulong)BitConverter.ToInt64(output, 0);
            var chunkBits = (ulong)BitConverter.ToInt64(output, 8);
            if (chunkBits == 0)
                break;

            var byteCount = (int)((chunkBits + 7) / 8);
            if (16 + byteCount > returned)
                byteCount = returned - 16;
            if (byteCount <= 0)
                break;

            stream.Write(output, 16, byteCount);
            bitCount += chunkBits;
            lcn = startingLcn + chunkBits;
        }

        if (bitCount == 0)
            return false;
        bits = stream.ToArray();
        return true;
    }

    public static IEnumerable<(ulong Offset, ulong Length)> AllocatedRuns(byte[] bits, ulong bitCount, uint clusterSize)
    {
        if (clusterSize == 0 || bitCount == 0)
            yield break;

        ulong i = 0;
        while (i < bitCount)
        {
            var byteIndex = (int)(i >> 3);
            if ((i & 7) == 0)
            {
                while (byteIndex < bits.Length && bits[byteIndex] == 0 && i + 8 <= bitCount)
                {
                    i += 8;
                    byteIndex++;
                }
            }

            while (i < bitCount && !BitSet(bits, i))
                i++;
            if (i >= bitCount)
                yield break;

            var start = i;
            while (i < bitCount)
            {
                byteIndex = (int)(i >> 3);
                if ((i & 7) == 0 && byteIndex < bits.Length && bits[byteIndex] == 0xFF && i + 8 <= bitCount)
                {
                    i += 8;
                    continue;
                }

                if (!BitSet(bits, i))
                    break;
                i++;
            }

            yield return (start * clusterSize, (i - start) * clusterSize);
        }
    }

    private static bool BitSet(byte[] bits, ulong index)
    {
        var byteIndex = (int)(index >> 3);
        if ((uint)byteIndex >= (uint)bits.Length)
            return false;
        return (bits[byteIndex] & (1 << (int)(index & 7))) != 0;
    }
}
