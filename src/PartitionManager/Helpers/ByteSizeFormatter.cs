namespace PartitionManager.Helpers;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(ulong bytes, int decimals = 2)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = value.ToString(unit <= 1 ? "F0" : $"F{decimals}");
        return $"{text} {Units[unit]}";
    }

    public static ulong FromMegaBytes(double megaBytes) =>
        (ulong)Math.Round(megaBytes * 1024d * 1024d);

    public static ulong AlignDown(ulong value, ulong alignment)
    {
        if (alignment == 0) return value;
        return value / alignment * alignment;
    }

    public static ulong AlignUp(ulong value, ulong alignment)
    {
        if (alignment == 0) return value;
        var rem = value % alignment;
        return rem == 0 ? value : value + (alignment - rem);
    }
}
