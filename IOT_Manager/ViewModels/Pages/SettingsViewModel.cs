using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOT_Manager.Models;
using IOT_Manager.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Abstractions.Controls;

namespace IOT_Manager.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private readonly SettingsService _settingsService;
        private bool _isInitialized = false;
        [ObservableProperty]
        private string _appVersion = String.Empty;
        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;
        // Expose Config để Binding trực tiếp từ View
        public AppConfig Config => _settingsService.Config;
        // Constructor Injection
        public SettingsViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }
        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();
            return Task.CompletedTask;
        }
        public Task OnNavigatedFromAsync() => Task.CompletedTask;
        private void InitializeViewModel()
        {
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            AppVersion = GetAssemblyVersion();
            _isInitialized = true;
        }
        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }
        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light) break;
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;
                    break;
                default:
                    if (CurrentTheme == ApplicationTheme.Dark) break;
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;
                    break;
            }
        }
        [RelayCommand]
        private void GenerateNewHubId()
        {
            // Tự động update UI nhờ ObservableObject trong AppConfig
            Config.HubId = Guid.NewGuid().ToString();
        }
        [RelayCommand]
        private void CopyHubId()
        {
            Clipboard.SetText(Config.HubId);
            MessageBox.Show("Hub ID copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        [RelayCommand]
        private void SaveSettings()
        {
            _settingsService.SaveConfig();
            MessageBox.Show("Configuration saved successfully!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}