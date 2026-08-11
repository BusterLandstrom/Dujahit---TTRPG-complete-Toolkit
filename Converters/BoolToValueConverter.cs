using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Dujahit.Converters
{
    public class BoolToValueConverter<T> : IValueConverter
    {
        public T TrueValue { get; set; }
        public T FalseValue { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TrueValue : FalseValue;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is T t && Equals(t, TrueValue);
    }
}