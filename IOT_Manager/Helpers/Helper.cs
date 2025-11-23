using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls; // Để dùng SymbolIcon enum

namespace IOT_Manager.Helpers
{
    // 1. Chuyển đổi trạng thái (online/offline) sang màu sắc
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                return status.ToLower() == "online" ? "Success" : "Danger";
            }
            return "Secondary";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    // 2. Chuyển đổi loại Node (soil/atm) sang Icon
    public class TypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                return type.ToLower() switch
                {
                    "soil" => SymbolRegular.Drop24,       // Icon giọt nước cho đất
                    "atm" => SymbolRegular.Cloud24,       // Icon mây cho khí quyển
                    "atmospheric" => SymbolRegular.Cloud24,
                    _ => SymbolRegular.Question24
                };
            }
            return SymbolRegular.Question24;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}