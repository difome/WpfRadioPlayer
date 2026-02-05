using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RadioPlayer.Converters;

public class StringToGeometryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Geometry geometry)
            return geometry;

        if (value is string geometryString && !string.IsNullOrEmpty(geometryString))
        {
            try
            {
                return Geometry.Parse(geometryString);
            }
            catch
            {
                return Geometry.Empty;
            }
        }

        if (value == null)
            return Geometry.Empty;

        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

