using IOT_Manager.ViewModels.Pages;
using OxyPlot;
using OxyPlot.Series;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Abstractions.Controls;

namespace IOT_Manager.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }
 
        public DashboardPage(DashboardViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}