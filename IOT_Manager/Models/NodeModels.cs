// Models/NodeModels.cs
#nullable disable
using System.Text.Json.Serialization;

namespace IOT_Manager.Models
{
    // Dùng để hứng list device từ lệnh getListDevice
    public class NodeInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } // "soil" hoặc "atm"

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    // Dùng để hiển thị lên UI
    public class NodeDisplayModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string NodeId { get; set; }
        public string Type { get; set; } // "soil" or "atmospheric"

        private string _status;
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        // Dữ liệu cảm biến (hiển thị dạng chuỗi gộp cho gọn hoặc tách ra tùy ý)
        private string _sensorDataDisplay;
        public string SensorDataDisplay { get => _sensorDataDisplay; set => SetProperty(ref _sensorDataDisplay, value); }

        // Lưu trữ object raw để gửi API
        public Dictionary<string, object> RawSensors { get; set; } = new();
    }
}