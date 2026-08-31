using System.Collections.ObjectModel;
using PartitionManager.Helpers;
using PartitionManager.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PartitionManager.ViewModels;

public partial class DiskViewModel : ObservableObject
{
    public DiskViewModel(DiskModel model)
    {
        Model = model;
        Segments = new ObservableCollection<PartitionViewModel>(
            model.Segments.Select(s => new PartitionViewModel(s, this)));
    }

    public DiskModel Model { get; }
    public ObservableCollection<PartitionViewModel> Segments { get; }

    public int Number => Model.Number;
    public Guid Id => Model.Id;
    public bool IsOptical => Model.IsOptical;
    public bool IsOffline => Model.IsOffline;
    public bool IsBoot => Model.IsBoot;
    public bool IsSystem => Model.IsSystem;
    public bool IsReadOnly => Model.IsReadOnly;
    public PartitionStyleKind PartitionStyle => Model.PartitionStyle;
    public bool IsInitialized => Model.PartitionStyle != PartitionStyleKind.Unknown;

    public string HeaderText => Model.IsOptical ? Model.FriendlyName : $"Disk {Model.Number}";

    public string DetailText
    {
        get
        {
            var bits = new List<string>
            {
                string.IsNullOrWhiteSpace(Model.Model) ? Model.FriendlyName : Model.Model,
                ByteSizeFormatter.Format(Model.Size)
            };
            if (!Model.IsOptical)
                bits.Add(Model.StyleText);
            if (!string.IsNullOrWhiteSpace(Model.BusType))
                bits.Add(Model.BusType);
            if (Model.IsBoot) bits.Add("Boot");
            if (Model.IsOffline) bits.Add("Offline");
            return string.Join("  ·  ", bits.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    public string SizeText => ByteSizeFormatter.Format(Model.Size);
    public string StyleText => Model.StyleText;
    public string StatusText => Model.Status;
    public string HealthText => Model.Health;
}
