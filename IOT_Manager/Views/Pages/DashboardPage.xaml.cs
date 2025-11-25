using IOT_Manager.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using System.Windows.Media;
using System;
using System.Windows;

namespace IOT_Manager.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage(DashboardViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;

            InitializeComponent();

            // Đăng ký nhận thông báo từ ViewModel (Chỉ dùng cho Snackbar trong App)
            ViewModel.RequestNotification += ShowNotification;
        }

        private void ShowNotification(string title, string message, string type)
        {
            // Không cần xử lý type="Windows" ở đây nữa vì ViewModel tự gọi Service

            // Xử lý các loại thông báo Snackbar (Success/Error/Info)
            ControlAppearance appearance = ControlAppearance.Info;
            if (type == "Success") appearance = ControlAppearance.Success;
            if (type == "Danger") appearance = ControlAppearance.Danger;
            if (type == "Caution") appearance = ControlAppearance.Caution;

            var snackbar = new Snackbar(SnackbarPresenter)
            {
                Title = title,
                Content = message,
                Appearance = appearance,
                Timeout = TimeSpan.FromSeconds(4),
            };
            snackbar.Show();
        }
    }
}