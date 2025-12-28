using System.Globalization;
using System.Windows.Data;

namespace TgdSoundboard.Converters;

public class LevelToHeightConverter : IValueConverter
{
    public double MaxHeight { get; set; } = 200;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float level)
        {
            return Math.Max(0, Math.Min(MaxHeight, level * MaxHeight));
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
