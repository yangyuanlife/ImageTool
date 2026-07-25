using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ImageTool.Converters;

/// <summary>根据 Value/Max/Min/ActualWidth 计算 ProgressBar 指示条宽度（圆角进度条用）</summary>
public class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4) return 0d;
        if (values[0] is double v && values[1] is double max && values[3] is double width)
        {
            double min = values[2] is double m ? m : 0;
            if (max <= min || width <= 0) return 0d;
            double frac = (v - min) / (max - min);
            return Math.Max(0, Math.Min(1, frac)) * width;
        }
        return 0d;
    }

    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
