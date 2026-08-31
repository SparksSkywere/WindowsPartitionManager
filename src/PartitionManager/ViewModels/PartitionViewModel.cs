using PartitionManager.Helpers;
using PartitionManager.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PartitionManager.ViewModels;

public partial class PartitionViewModel : ObservableObject
{
    public PartitionViewModel(SegmentModel model, DiskViewModel disk)
    {
        Model = model;
        Disk = disk;
    }

    public SegmentModel Model { get; }
    public DiskViewModel Disk { get; }

    [ObservableProperty] private bool _isSelected;

    public Guid Id => Model.Id;
    public int DiskNumber => Model.DiskNumber;
    public bool IsUnallocated => Model.IsUnallocated;
    public bool IsPending => Model.IsPending;
    public bool IsProtected => Model.IsProtected;
    public SegmentKind Kind => Model.Kind;
    public ulong Size => Model.Size;
    public ulong Offset => Model.Offset;
    public ulong Used => Model.Used;
    public ulong Free => Model.SizeRemaining;
    public double UsedPercent => Model.UsedPercent;
    public char? DriveLetter => Model.DriveLetter;

    public string DriveText =>
        Model.IsUnallocated
            ? "*"
            : Model.DriveLetter is char c
                ? $"{c}:"
                : "—";

    public string LabelText =>
        Model.IsUnallocated
            ? "Unallocated"
            : string.IsNullOrWhiteSpace(Model.Label)
                ? DefaultLabel()
                : Model.Label;

    public string FileSystemText =>
        Model.IsUnallocated ? "" : (string.IsNullOrWhiteSpace(Model.FileSystem) ? "—" : Model.FileSystem);

    public string StatusText =>
        string.IsNullOrWhiteSpace(Model.Status) ? "Healthy" : Model.Status;

    public string TypeText => Model.Kind switch
    {
        SegmentKind.Unallocated => "Unallocated",
        SegmentKind.Primary => "Primary",
        SegmentKind.Logical => "Logical",
        SegmentKind.Extended => "Extended",
        SegmentKind.Efi => "EFI System",
        SegmentKind.Recovery => "Recovery",
        SegmentKind.MicrosoftReserved => "MSR",
        SegmentKind.Oem => "OEM",
        _ => "Unknown"
    };

    public string SizeText => ByteSizeFormatter.Format(Model.Size);
    public string MapSizeText => ByteSizeFormatter.Format(Model.Size, decimals: 0);
    public string UsedText => Model.IsUnallocated || string.IsNullOrEmpty(Model.FileSystem) ? "—" : ByteSizeFormatter.Format(Model.Used);
    public string FreeText => Model.IsUnallocated ? ByteSizeFormatter.Format(Model.Size) :
        string.IsNullOrEmpty(Model.FileSystem) ? "—" : ByteSizeFormatter.Format(Model.SizeRemaining);
    public string DiskText => $"Disk {Disk.Number}";
    public string OffsetText => ByteSizeFormatter.Format(Model.Offset);

    public string DisplayName
    {
        get
        {
            if (Model.IsUnallocated)
                return $"Unallocated ({SizeText})";
            var drive = Model.DriveLetter is char c ? $"{c}: " : "";
            var label = string.IsNullOrWhiteSpace(Model.Label) ? DefaultLabel() : Model.Label;
            return $"{drive}{label}".Trim();
        }
    }

    public string MapCaption
    {
        get
        {
            if (Model.IsUnallocated)
                return "Unallocated";
            if (Model.DriveLetter is char c)
                return string.IsNullOrWhiteSpace(Model.Label) ? $"{c}:" : $"{c}: {Model.Label}";
            return TypeText;
        }
    }

    public string MapToolTip =>
        $"{DisplayName}\n{TypeText}  {SizeText}" +
        (string.IsNullOrEmpty(FileSystemText) ? "" : $"  {FileSystemText}") +
        $"\nOffset {OffsetText}";

    private string DefaultLabel() => Kind switch
    {
        SegmentKind.Efi => "EFI System",
        SegmentKind.Recovery => "Recovery",
        SegmentKind.MicrosoftReserved => "MSR",
        SegmentKind.Oem => "OEM",
        _ => "Partition"
    };
}
