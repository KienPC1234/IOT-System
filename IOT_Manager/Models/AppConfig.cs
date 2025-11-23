using CommunityToolkit.Mvvm.ComponentModel;

namespace IOT_Manager.Models
{
    // Kế thừa ObservableObject để UI tự cập nhật khi code thay đổi giá trị (vd: Random HubID)
    public partial class AppConfig : ObservableObject
    {
        [ObservableProperty]
        private string _savedComPort = "";

        [ObservableProperty]
        private string _firmwareVersion = "Unknown";

        [ObservableProperty]
        private string _hubId = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _apiEndpoint = "http://103.252.0.76:8000/api/v1/data/ingest";

        [ObservableProperty]
        private int _dataIntervalSeconds = 60;

        [ObservableProperty]
        private bool _isDataSendEnabled = true;
    }
}