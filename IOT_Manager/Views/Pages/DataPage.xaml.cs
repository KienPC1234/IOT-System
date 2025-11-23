using IOT_Manager.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace IOT_Manager.Views.Pages
{
    public partial class DataPage : INavigableView<DataViewModel>
    {
        public DataViewModel ViewModel { get; }

        public DataPage(DataViewModel viewModel)
        {
            ViewModel = viewModel;

            // QUAN TRỌNG: Gán DataContext là ViewModel (không phải 'this')
            DataContext = ViewModel;

            InitializeComponent();
        }
    }
}