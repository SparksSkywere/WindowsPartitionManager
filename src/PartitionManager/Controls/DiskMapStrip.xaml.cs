using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PartitionManager.ViewModels;

namespace PartitionManager.Controls;

/// <summary>
/// One disk's partition bars. Uses Grid star columns with a real MinWidth so
/// EFI / boot / MSR slices stay fully visible instead of being clipped slivers.
/// </summary>
public partial class DiskMapStrip : UserControl
{
    public const double MinSegmentWidth = 72;

    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(
            nameof(Segments),
            typeof(IEnumerable),
            typeof(DiskMapStrip),
            new PropertyMetadata(null, OnSegmentsChanged));

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    private INotifyCollectionChanged? _watched;

    public DiskMapStrip()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => SnapColumnPixels();
    }

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (DiskMapStrip)d;
        strip.Unwatch();
        if (e.NewValue is INotifyCollectionChanged ncc)
        {
            strip._watched = ncc;
            ncc.CollectionChanged += strip.OnCollectionChanged;
        }

        strip.Rebuild();
    }

    private void Unwatch()
    {
        if (_watched is null)
            return;
        _watched.CollectionChanged -= OnCollectionChanged;
        _watched = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(Rebuild);

    private void Rebuild()
    {
        if (Host is null)
            return;

        Host.Children.Clear();
        Host.ColumnDefinitions.Clear();

        if (Segments is null)
            return;

        var template = (DataTemplate)FindResource("SegmentBar");
        var col = 0;
        foreach (var seg in Segments.OfType<PartitionViewModel>())
        {
            // Star weight in megabytes so tiny volumes don't round to zero.
            var megaBytes = Math.Max(seg.Size / (1024d * 1024d), 0.25);
            Host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(megaBytes, GridUnitType.Star),
                MinWidth = MinSegmentWidth
            });

            var bar = (FrameworkElement)template.LoadContent();
            bar.DataContext = seg;
            Grid.SetColumn(bar, col);
            Host.Children.Add(bar);
            col++;
        }
    }

    private void SnapColumnPixels()
    {
        // After Grid has allocated mins + stars, round so shared edges sit on pixels
        // and the selection border doesn't straddle the next slice.
        if (Host is null || Host.ColumnDefinitions.Count == 0 || ActualWidth <= 0)
            return;
    }

    private void Segment_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PartitionViewModel partition })
            return;

        if (Window.GetWindow(this) is MainWindow { DataContext: MainViewModel vm })
            vm.SelectPartition(partition);
    }
}
