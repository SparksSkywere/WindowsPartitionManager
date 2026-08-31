using System.Windows;
using PartitionManager.Helpers;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class PropertiesWindow : Window
{
    public PropertiesWindow(PartitionViewModel partition)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        Title = $"{partition.DisplayName} properties";
        var m = partition.Model;
        Add("Disk", partition.DiskText);
        Add("Drive", partition.DriveText);
        Add("Label", partition.LabelText);
        Add("File system", partition.FileSystemText);
        Add("Type", partition.TypeText);
        Add("Status", partition.StatusText);
        Add("Size", partition.SizeText);
        Add("Used", partition.UsedText);
        Add("Free", partition.FreeText);
        Add("Offset", partition.OffsetText);
        Add("Partition number", m.PartitionNumber == 0 ? "—" : m.PartitionNumber.ToString());
        Add("Boot", YesNo(m.IsBoot));
        Add("System", YesNo(m.IsSystem));
        Add("Active", YesNo(m.IsActive));
        Add("Hidden", YesNo(m.IsHidden));
        Add("Read-only", YesNo(m.IsReadOnly));
        Add("GPT type", string.IsNullOrWhiteSpace(m.GptType) ? "—" : m.GptType);
        Add("Access paths", string.IsNullOrWhiteSpace(m.AccessPaths) ? "—" : m.AccessPaths);
    }

    public PropertiesWindow(DiskViewModel disk)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        Title = $"{disk.HeaderText} properties";
        var m = disk.Model;
        Add("Disk number", m.Number.ToString());
        Add("Model", m.Model);
        Add("Friendly name", m.FriendlyName);
        Add("Serial", string.IsNullOrWhiteSpace(m.SerialNumber) ? "—" : m.SerialNumber);
        Add("Firmware", string.IsNullOrWhiteSpace(m.FirmwareVersion) ? "—" : m.FirmwareVersion);
        Add("Bus", m.BusType);
        Add("Size", disk.SizeText);
        Add("Allocated", ByteSizeFormatter.Format(m.AllocatedSize));
        Add("Partition style", m.StyleText);
        Add("Status", m.Status);
        Add("Health", m.Health);
        Add("Boot disk", YesNo(m.IsBoot));
        Add("System disk", YesNo(m.IsSystem));
        Add("Offline", YesNo(m.IsOffline));
        Add("Read-only", YesNo(m.IsReadOnly));
        Add("Removable", YesNo(m.IsRemovable));
        Add("Virtual", YesNo(m.IsVirtual));
        Add("Logical sector", m.LogicalSectorSize + " bytes");
        Add("Physical sector", m.PhysicalSectorSize + " bytes");
        Add("Location", string.IsNullOrWhiteSpace(m.Location) ? "—" : m.Location);
        Add("Unique ID", string.IsNullOrWhiteSpace(m.UniqueId) ? "—" : m.UniqueId);
        Add("Partitions", disk.Segments.Count(s => !s.IsUnallocated).ToString());
    }

    private void Add(string key, string value) =>
        PropList.Items.Add(new { Key = key, Value = value });

    private static string YesNo(bool value) => value ? "Yes" : "No";
}
