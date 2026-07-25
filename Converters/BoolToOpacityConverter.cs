using System;
using System.Globalization;
using System.Windows.Data;

namespace ImageTool.Converters;

/// <summary>布尔 → 透明度：true=1.0，false=0.4，用于让「当前模式不生效」的字段标签淡出。</summary>
[ValueConversion(typeof(bool), typeof(double))]
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.4;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
