using CommunityToolkit.Mvvm.ComponentModel;
using IOT_Manager.Models;
using OxyPlot;          // <--- Thêm
using OxyPlot.Series;   // <--- Thêm
using OxyPlot.Axes;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace IOT_Manager.ViewModels.Pages
{
    public partial class DataViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private IEnumerable<DataColor> _colors;

        // Thêm Property cho biểu đồ ở đây
        [ObservableProperty]
        private PlotModel _myModel; 

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            // 1. Khởi tạo Colors (Code cũ của bạn)
            var random = new Random();
            var colorCollection = new List<DataColor>();
            for (int i = 0; i < 8192; i++)
            {
                colorCollection.Add(new DataColor
                {
                    Color = new SolidColorBrush(Color.FromArgb((byte)200, (byte)random.Next(0, 250), (byte)random.Next(0, 250), (byte)random.Next(0, 250)))
                });
            }
            Colors = colorCollection;

            // 2. Khởi tạo Biểu đồ (Chuyển từ code-behind sang đây)
            CreateChart();

            _isInitialized = true;
        }

        private void CreateChart()
        {
            var model = new PlotModel { Title = "Demo Line Chart" };
            ApplyDarkTheme(model);
            model.Series.Add(new LineSeries
            {
                Title = "Sample Data",
                Points = 
                {
                    new DataPoint(0, 0),
                    new DataPoint(1, 2),
                    new DataPoint(2, 4),
                    new DataPoint(3, 3),
                    new DataPoint(4, 5)
                }
            });

            MyModel = model; // Gán vào ObservableProperty để UI tự cập nhật
        }

        public static void ApplyDarkTheme(PlotModel model)
        {
            var fg = OxyColors.WhiteSmoke;
            var bg = OxyColor.FromRgb(30, 30, 30);

            model.TextColor = fg;
            model.TitleColor = fg;
            model.SubtitleColor = fg;
            model.PlotAreaBorderColor = fg;
            model.Background = bg;

            foreach (var axis in model.Axes)
            {
                axis.TextColor = fg;
                axis.TicklineColor = fg;
                axis.AxislineColor = fg;
                axis.MinorTicklineColor = fg;
            }

            foreach (var s in model.Series)
            {
                if (s is LineSeries ls)
                {
                    ls.Color = OxyColors.Cyan;
                    ls.MarkerStroke = OxyColors.White;
                }
                else if (s is BarSeries bs)
                {
                    bs.FillColor = OxyColors.LightSkyBlue;
                    bs.StrokeColor = OxyColors.White;
                }
            }

            model.InvalidatePlot(false);
        }

    }
}