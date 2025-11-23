using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOT_Manager.Services;
using System.Collections.ObjectModel;
using System.Windows;
using OxyPlot;
using OxyPlot.Series;


namespace IOT_Manager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject
    {
       
        public DashboardViewModel(ISerialService serialService)
        {
        }

    }
}