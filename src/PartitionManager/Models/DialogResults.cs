namespace PartitionManager.Models;

public sealed class CreatePartitionDialogResult
{
    public ulong Size { get; init; }
    public char? DriveLetter { get; init; }
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "NTFS";
    public bool FormatAfterCreate { get; init; } = true;
    public bool QuickFormat { get; init; } = true;
}

public sealed class ResizePartitionDialogResult
{
    public ulong NewSize { get; init; }
}

public sealed class FormatPartitionDialogResult
{
    public string FileSystem { get; init; } = "NTFS";
    public string Label { get; init; } = "";
    public bool QuickFormat { get; init; } = true;
}

public sealed class DriveLetterDialogResult
{
    public char? DriveLetter { get; init; }
}

public sealed class LabelDialogResult
{
    public string Label { get; init; } = "";
}

public sealed class InitializeDiskDialogResult
{
    public PartitionStyleKind Style { get; init; } = PartitionStyleKind.Gpt;
}
