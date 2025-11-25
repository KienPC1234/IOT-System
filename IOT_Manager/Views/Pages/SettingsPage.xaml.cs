using IOT_Manager.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace IOT_Manager.Views.Pages
{
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel; // Sửa lỗi Binding: DataContext trỏ trực tiếp vào ViewModel

            InitializeComponent();
        }

        // Tự bắt sự kiện click nút Save để hiện Toast (Giản lược, không cần event từ VM)
        private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var snackbar = new Snackbar(SettingsSnackbarPresenter)
            {
                Title = "Settings",
                Content = "Configuration Saved!",
                Appearance = ControlAppearance.Success,
                Timeout = TimeSpan.FromSeconds(2),
            };
            snackbar.Show();
        }
    }
}