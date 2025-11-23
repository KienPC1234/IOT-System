using IOT_Manager.ViewModels.Pages;
using OxyPlot;
using OxyPlot.Series;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Abstractions.Controls;

namespace IOT_Manager.Views.Pages
{
    public partial class DebugPage : INavigableView<DebugViewModel>
    {
        public DebugViewModel ViewModel { get; }
 
        public DebugPage(DebugViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }

        // Tự động cuộn xuống cuối log
        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            (sender as TextBox)?.ScrollToEnd();
        }
    }

    // --- FIX LỖI XLS0414: Đặt class này Ở ĐÂY ---
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}