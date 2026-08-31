using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PartitionManager.Models;

namespace PartitionManager.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class UpdateStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var app = Application.Current;
        if (value is true)
        {
            if (app?.TryFindResource("UpdateAvailableBrush") is Brush accent)
                return accent;
            return new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        }

        if (app?.TryFindResource("UpdateOkBrush") is Brush muted)
            return muted;
        return new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ProgressBarVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && i >= 0)
            return Visibility.Visible;
        if (value is double d && d >= 0)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ProgressValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i)
            return i < 0 ? 0 : Math.Clamp(i, 0, 100);
        if (value is double d)
            return d < 0 ? 0d : Math.Clamp(d, 0, 100);
        return 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToBoldConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeights.SemiBold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Partition map fill based on <see cref="SegmentKind"/> / filesystem.</summary>
public sealed class PartitionFillConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SegmentKind kind)
            return Freeze(Color.FromRgb(0xB0, 0xB0, 0xB0));

        return kind switch
        {
            SegmentKind.Unallocated => Freeze(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            SegmentKind.Efi => Freeze(Color.FromRgb(0x8E, 0x6B, 0xC7)),
            SegmentKind.Recovery => Freeze(Color.FromRgb(0xE6, 0xA2, 0x3C)),
            SegmentKind.MicrosoftReserved => Freeze(Color.FromRgb(0x90, 0xA4, 0xAE)),
            SegmentKind.Logical => Freeze(Color.FromRgb(0x5C, 0xB8, 0x5C)),
            SegmentKind.Extended => Freeze(Color.FromRgb(0xA5, 0xD6, 0xA7)),
            SegmentKind.Oem => Freeze(Color.FromRgb(0x78, 0x90, 0x9C)),
            SegmentKind.Primary => Freeze(Color.FromRgb(0x4A, 0x90, 0xD9)),
            _ => Freeze(Color.FromRgb(0x64, 0xB5, 0xF6))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}

public sealed class UsedPercentToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0d
        };
        percent = Math.Clamp(percent, 0, 100);
        return new GridLength(percent, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RemainingPercentToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0d
        };
        percent = Math.Clamp(percent, 0, 100);
        var remaining = 100 - percent;
        if (remaining < 0.5) remaining = 0.5;
        return new GridLength(remaining, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
