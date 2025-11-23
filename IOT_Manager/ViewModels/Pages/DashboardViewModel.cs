using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOT_Manager.Models;
using IOT_Manager.Services;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows;
using System.Threading.Tasks;

namespace IOT_Manager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly ISerialService _serialService;
        private readonly SettingsService _settingsService;
        private readonly HttpClient _httpClient;
        private DispatcherTimer _dataTimer;
        private DispatcherTimer _scanTimer;

        // Cờ kiểm tra đang scan để tránh spam lệnh khi timer tick tiếp
        private bool _isScanning = false;

        // --- Properties ---
        [ObservableProperty] private bool _isMasterConnected;
        [ObservableProperty] private string _masterStatus = "Searching...";
        [ObservableProperty] private string _firmwareVersion = "Unknown";
        [ObservableProperty] private bool _isRegisterMode;

        public AppConfig Config => _settingsService.Config;

        public ObservableCollection<NodeDisplayModel> Nodes { get; } = new();

        public DashboardViewModel(ISerialService serialService, SettingsService settingsService)
        {
            _serialService = serialService;
            _settingsService = settingsService;
            _httpClient = new HttpClient();

            _serialService.DataReceived += OnSerialDataReceived;

            _dataTimer = new DispatcherTimer();
            _dataTimer.Tick += async (s, e) => await GetDataRoutine();
            UpdateTimerInterval();

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _scanTimer.Tick += AutoScanPorts;
            _scanTimer.Start();
        }

        // --- Auto Connect Logic (Updated with Timeout 5s) ---
        private async void AutoScanPorts(object sender, EventArgs e)
        {
            if (_serialService.IsConnected || _isScanning || _isMasterConnected) return;

            _isScanning = true;

            try
            {
                var ports = _serialService.GetAvailablePorts();

                if (ports.Length == 0)
                {
                    MasterStatus = "No COM ports found";
                    return;
                }

                bool deviceFound = false;

                foreach (var port in ports)
                {
                    MasterStatus = $"Scanning {port} (Timeout 1s)...";

                    // Chạy kết nối Background
                    bool success = await Task.Run(async () =>
                    {
                        try
                        {
                            // 1. Connect Serial
                            _serialService.Connect(port, 115200);

                            // 2. Gửi Handshake
                            _serialService.SendData("helloMaster");

                            // 3. Timeout Logic: Đợi phản hồi tối đa 5 giây
                            int timeoutMs = 1000;
                            int checkInterval = 100;
                            int elapsed = 0;

                            while (elapsed < timeoutMs)
                            {
                                // Nếu cờ kết nối đã bật (do nhận được FW_...), return true ngay
                                if (_isMasterConnected) return true;

                                await Task.Delay(checkInterval);
                                elapsed += checkInterval;
                            }

                            // Hết 5s vẫn chưa thấy phản hồi -> Timeout -> Ngắt kết nối
                            _serialService.Disconnect();
                            return false;
                        }
                        catch
                        {
                            _serialService.Disconnect();
                            return false;
                        }
                    });

                    if (success)
                    {
                        // 4. Thông báo thành công + FW Version
                        MasterStatus = $"Connected! ({FirmwareVersion})";

                        _settingsService.Config.SavedComPort = port;
                        _settingsService.SaveConfig();

                        _serialService.SendData("getListDevice");
                        _dataTimer.Start();
                        _scanTimer.Stop();
                        deviceFound = true;
                        break; // Thoát vòng lặp scan
                    }
                }

                // Nếu quét hết danh sách mà không thấy
                if (!deviceFound)
                {
                    MasterStatus = "Device not found (Handshake failed)";
                }
            }
            finally
            {
                _isScanning = false;
            }
        }

        // --- Serial Data Handler ---
        private void OnSerialDataReceived(string data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProcessData(data.Trim());
            });
        }

        private void ProcessData(string json)
        {
            // 1. Handshake response
            if (json.StartsWith("FW_"))
            {
                FirmwareVersion = json;
                _settingsService.Config.FirmwareVersion = json;
                // Bật cờ kết nối -> Vòng lặp AutoScanPorts sẽ nhận biết và thoát
                _isMasterConnected = true;
                return;
            }

            // 2. JSON Processing
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Case: List Devices
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var list = JsonSerializer.Deserialize<List<NodeInfo>>(json);
                    UpdateNodeList(list);
                }
                // Case: Single Object
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("status", out var statusProp))
                    {
                        var status = statusProp.GetString();
                        if (status == "register_mode_active") IsRegisterMode = true;
                        if (status == "system_ready") IsRegisterMode = false;
                        if (status == "offline" && root.TryGetProperty("id", out var idProp))
                        {
                            UpdateNodeStatus(idProp.GetString(), "offline");
                        }
                    }

                    if (root.TryGetProperty("event", out var eventProp))
                    {
                        var evt = eventProp.GetString();
                        if (evt == "register_cancelled") IsRegisterMode = false;
                        if (evt == "registered") _serialService.SendData("getListDevice");
                        if (evt == "deleted") _serialService.SendData("getListDevice");
                        if (evt == "data_collection_finished")
                        {
                            if (_settingsService.Config.IsDataSendEnabled) UploadDataToServer();
                        }
                    }

                    if (root.TryGetProperty("sensors", out var sensors) && root.TryGetProperty("id", out var nodeId))
                    {
                        UpdateNodeData(nodeId.GetString(), sensors);
                    }
                }
            }
            catch { }
        }

        // --- Helper Methods ---
        private void UpdateNodeList(List<NodeInfo> list)
        {
            Nodes.Clear();
            foreach (var item in list)
            {
                Nodes.Add(new NodeDisplayModel
                {
                    NodeId = item.Id,
                    Type = item.Type,
                    Status = item.Status,
                    SensorDataDisplay = "Waiting for data..."
                });
            }
        }

        private void UpdateNodeData(string id, JsonElement sensorElement)
        {
            var node = Nodes.FirstOrDefault(n => n.NodeId == id);
            if (node != null)
            {
                node.Status = "online";
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(sensorElement.GetRawText());
                node.RawSensors = dict;

                string display = "";
                foreach (var kvp in dict) display += $"{kvp.Key}: {kvp.Value}\n";
                node.SensorDataDisplay = display.Trim();
            }
        }

        private void UpdateNodeStatus(string id, string status)
        {
            var node = Nodes.FirstOrDefault(n => n.NodeId == id);
            if (node != null) node.Status = status;
        }

        private async Task GetDataRoutine()
        {
            if (_isMasterConnected)
            {
                _serialService.SendData("getDataNow");
            }
        }

        private async void UploadDataToServer()
        {
            try
            {
                var payload = new
                {
                    hub_id = _settingsService.Config.HubId,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    data = new
                    {
                        soil_nodes = Nodes.Where(n => n.Type == "soil" && n.RawSensors.Count > 0)
                            .Select(n => new { node_id = n.NodeId, sensors = n.RawSensors }).ToList(),
                        atmospheric_node = Nodes.Where(n => n.Type == "atm" && n.RawSensors.Count > 0)
                             .Select(n => new { node_id = n.NodeId, sensors = n.RawSensors }).FirstOrDefault()
                    }
                };

                var response = await _httpClient.PostAsJsonAsync(_settingsService.Config.ApiEndpoint, payload);
                if (response.IsSuccessStatusCode)
                {
                    MasterStatus = $"Data Uploaded: {DateTime.Now:HH:mm:ss}";
                }
            }
            catch (Exception ex) { MasterStatus = $"Upload Err: {ex.Message}"; }
        }

        public void UpdateTimerInterval()
        {
            _dataTimer.Interval = TimeSpan.FromSeconds(_settingsService.Config.DataIntervalSeconds);
        }

        // --- Commands ---
        [RelayCommand]
        private void GetDataNow() => _serialService.SendData("getDataNow");

        [RelayCommand]
        private void ToggleRegisterMode()
        {
            if (IsRegisterMode) _serialService.SendData("cancelRegister");
            else _serialService.SendData("registerNewNode");
        }

        [RelayCommand]
        private void DeleteNode(string id)
        {
            _serialService.SendData($"deleteNode {id}");
        }
    }
}