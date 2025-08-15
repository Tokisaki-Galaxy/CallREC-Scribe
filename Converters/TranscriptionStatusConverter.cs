using System.Globalization;

namespace CallREC_Scribe.Converters
{
    public class TranscriptionStatusConverter : IValueConverter
    {
        // 从 ViewModel -> View 的转换
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 如果传入的值是 null 或者空字符串
            if (string.IsNullOrEmpty(value as string))
            {
                // 则向UI显示 "未转录"
                return "未转录";
            }
            // 否则，直接显示原始值
            return value;
        }

        // 从 View -> ViewModel 的转换 (我们这里用不到，所以不用实现)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}