using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOT_Manager.Models;
using IOT_Manager.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using NullSoftware.ToolKit; // Thêm namespace của thư viện TrayIcon

namespace IOT_Manager.ViewModels.Pages
{
    // --- MODELS ---
    public class SensorAttribute
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }
        public SymbolRegular Icon { get; set; }
    }

    public partial class NodeDisplayModel : ObservableObject
    {
        [ObservableProperty] private string _nodeId;
        [ObservableProperty] private string _type;
        [ObservableProperty] private string _status;
        public ObservableCollection<SensorAttribute> Attributes { get; } = new();
        public Dictionary<string, object> RawSensors { get; set; }
    }

    // --- VIEW MODEL ---
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly ISerialService _serialService;
        private readonly SettingsService _settingsService;
        private readonly HttpClient _httpClient;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private DispatcherTimer _dataTimer;
        private DispatcherTimer _scanTimer;
        private DispatcherTimer _uploadDebounceTimer;

        private bool _isScanning = false;
        private bool _isInternalUpdate = false;

        private StringBuilder _logBuilder = new StringBuilder();

        // Event này chỉ còn dùng cho Snackbar trong App (Success/Danger/Info)
        public event Action<string, string, string> RequestNotification;

        // --- TRAY ICON SERVICE (Được Inject từ XAML) ---
        public INotificationService NotificationService { get; set; }

        [ObservableProperty] private bool _isMasterConnected;
        [ObservableProperty] private string _masterStatus = "Searching...";
        [ObservableProperty] private string _firmwareVersion = "Unknown";
        [ObservableProperty] private bool _isRegisterMode;
        [ObservableProperty] private string _appLogs = "System initialized...";

        public ObservableCollection<NodeDisplayModel> Nodes { get; } = new();

        // --- CHARTS ---
        [ObservableProperty] private PlotModel _soilPlotModel;
        [ObservableProperty] private PlotModel _atmPlotModel;

        private LineSeries _soilTempSeries;
        private LineSeries _soilMoistSeries;
        private LineSeries _atmTempSeries;
        private LineSeries _atmHumidSeries;
        private LineSeries _atmRainSeries;
        private LineSeries _atmWindSeries;
        private LineSeries _atmLightSeries;
        private LineSeries _atmPressSeries;

        private int _dataPointIndex = 0;
        private const int MAX_POINTS = 100;

        public DashboardViewModel(ISerialService serialService, SettingsService settingsService)
        {
            _serialService = serialService;
            _settingsService = settingsService;
            _httpClient = new HttpClient();

            _serialService.DataReceived += OnSerialDataReceived;

            InitializePlots();

            _dataTimer = new DispatcherTimer();
            _dataTimer.Tick += async (s, e) => await GetDataRoutine();
            if (_settingsService.Config != null)
                _dataTimer.Interval = TimeSpan.FromSeconds(_settingsService.Config.DataIntervalSeconds > 0 ? _settingsService.Config.DataIntervalSeconds : 5);

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _scanTimer.Tick += AutoScanPorts;
            _scanTimer.Start();

            _uploadDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _uploadDebounceTimer.Tick += (s, e) =>
            {
                _uploadDebounceTimer.Stop();
                if (_settingsService.Config.IsDataSendEnabled) UploadDataToServer();
            };

            AddToLog("Dashboard started. Waiting for connection...");
        }

        partial void OnIsRegisterModeChanged(bool value)
        {
            if (_isInternalUpdate) return;

            if (value)
            {
                _serialService.SendData("registerNewNode");
                AddToLog("CMD: Entering Register Mode...");
            }
            else
            {
                _serialService.SendData("cancelRegister");
                AddToLog("CMD: Cancelling Register Mode...");
                ShowWindowsNotify("Registration", "Register Mode Cancelled");
            }
        }

        // --- SERIAL LOGIC ---

        private void OnSerialDataReceived(string data)
        {
            Application.Current.Dispatcher.Invoke(() => ProcessData(data.Trim()));
        }

        private void ProcessData(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            if (!data.StartsWith("{") && !data.StartsWith("["))
            {
                AddToLog($"RX: {data}");
                if (data.StartsWith("FW_"))
                {
                    FirmwareVersion = data;
                    _isMasterConnected = true;
                }
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var list = JsonSerializer.Deserialize<List<NodeInfo>>(data, _jsonOptions);
                    UpdateNodeList(list);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("status", out var s) || root.TryGetProperty("Status", out s))
                        HandleStatus(s.GetString(), root);

                    if (root.TryGetProperty("event", out var e) || root.TryGetProperty("Event", out e))
                        HandleEvent(e.GetString(), root);

                    JsonElement sensors, nodeId;
                    bool hasSensors = root.TryGetProperty("sensors", out sensors) || root.TryGetProperty("Sensors", out sensors);
                    bool hasId = root.TryGetProperty("id", out nodeId) || root.TryGetProperty("Id", out nodeId);

                    if (hasSensors && hasId)
                    {
                        string id = nodeId.GetString();
                        UpdateNodeData(id, sensors);
                        AddToLog($"Data <{id}> updated.");

                        _uploadDebounceTimer.Stop();
                        _uploadDebounceTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                AddToLog($"JSON Error: {ex.Message}");
            }
        }

        private void HandleStatus(string status, JsonElement root)
        {
            _isInternalUpdate = true;
            if (status == "register_mode_active") IsRegisterMode = true;
            if (status == "system_ready") IsRegisterMode = false;
            _isInternalUpdate = false;
        }

        private void HandleEvent(string evt, JsonElement root)
        {
            if (evt == "registered")
            {
                _serialService.SendData("getListDevice");

                string id = "";
                if (root.TryGetProperty("id", out var idElem)) id = idElem.GetString();
                ShowWindowsNotify("New Device", $"Node {id} registered successfully!");

                _isInternalUpdate = true;
                IsRegisterMode = false;
                _isInternalUpdate = false;
            }
            if (evt == "register_cancelled")
            {
                _isInternalUpdate = true;
                IsRegisterMode = false;
                _isInternalUpdate = false;
                ShowWindowsNotify("Registration", "Registration process was cancelled.");
            }
            if (evt == "deleted")
            {
                string id = "";
                if (root.TryGetProperty("id", out var idElem)) id = idElem.GetString();
                ShowWindowsNotify("Device Deleted", $"Node {id} has been removed.");
            }
            if (evt == "data_collection_finished")
            {
                _uploadDebounceTimer.Stop();
                if (_settingsService.Config.IsDataSendEnabled) UploadDataToServer();
            }
        }

        private void UpdateNodeData(string id, JsonElement sensorElement)
        {
            var node = Nodes.FirstOrDefault(n => n.NodeId == id);

            if (node == null)
            {
                string type = id.ToLower().Contains("atm") ? "atmospheric" : "soil";
                node = new NodeDisplayModel { NodeId = id, Type = type, Status = "online" };
                Nodes.Add(node);
            }

            node.Status = "online";

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(sensorElement.GetRawText(), _jsonOptions);
                node.RawSensors = dict;

                node.Attributes.Clear();
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        var attr = MapSensorToAttribute(kvp.Key, kvp.Value?.ToString());
                        if (attr != null) node.Attributes.Add(attr);
                    }
                }
            }
            catch { AddToLog($"Error parsing sensors for {id}"); }

            UpdateCharts();
        }

        private SensorAttribute MapSensorToAttribute(string key, string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue)) return null;
            if (double.TryParse(rawValue, out double dVal)) rawValue = Math.Round(dVal, 1).ToString();

            string k = key.ToLower();

            if (k.Contains("soil_moisture")) return new SensorAttribute { Name = "Moisture", Value = rawValue, Unit = "%", Icon = SymbolRegular.Drop24 };
            if (k.Contains("soil_temperature")) return new SensorAttribute { Name = "Soil Temp", Value = rawValue, Unit = "°C", Icon = SymbolRegular.Temperature24 };
            if (k.Contains("air_temperature")) return new SensorAttribute { Name = "Air Temp", Value = rawValue, Unit = "°C", Icon = SymbolRegular.Temperature24 };
            if (k.Contains("air_humidity")) return new SensorAttribute { Name = "Humidity", Value = rawValue, Unit = "%", Icon = SymbolRegular.Drop12 };
            if (k.Contains("rain")) return new SensorAttribute { Name = "Rain", Value = rawValue, Unit = "mm", Icon = SymbolRegular.WeatherRain24 };
            if (k.Contains("wind")) return new SensorAttribute { Name = "Wind", Value = rawValue, Unit = "m/s", Icon = SymbolRegular.WeatherThunderstorm24 };
            if (k.Contains("light")) return new SensorAttribute { Name = "Light", Value = rawValue, Unit = "lux", Icon = SymbolRegular.WeatherSunny24 };
            if (k.Contains("pressure")) return new SensorAttribute { Name = "Pressure", Value = rawValue, Unit = "hPa", Icon = SymbolRegular.Gauge24 };

            return new SensorAttribute { Name = key, Value = rawValue, Unit = "", Icon = SymbolRegular.QuestionCircle24 };
        }

        // --- CHARTS LOGIC ---
        private void UpdateCharts()
        {
            _dataPointIndex++;
            bool soilUpdated = false;
            bool atmUpdated = false;

            var soilNodes = Nodes.Where(n => n.Type.ToLower().Contains("soil") && n.RawSensors != null).ToList();
            if (soilNodes.Any())
            {
                double avgTemp = 0, avgMoist = 0;
                int countT = 0, countM = 0;
                foreach (var n in soilNodes)
                {
                    if (TryGetVal(n.RawSensors, "soil_temperature", out double t)) { avgTemp += t; countT++; }
                    if (TryGetVal(n.RawSensors, "soil_moisture", out double m)) { avgMoist += m; countM++; }
                }
                if (countT > 0) { _soilTempSeries.Points.Add(new DataPoint(_dataPointIndex, avgTemp / countT)); soilUpdated = true; }
                if (countM > 0) { _soilMoistSeries.Points.Add(new DataPoint(_dataPointIndex, avgMoist / countM)); soilUpdated = true; }
            }

            var atmNode = Nodes.FirstOrDefault(n => n.Type.ToLower().Contains("atm") && n.RawSensors != null);
            if (atmNode != null)
            {
                if (TryGetVal(atmNode.RawSensors, "air_temperature", out double t)) _atmTempSeries.Points.Add(new DataPoint(_dataPointIndex, t));
                if (TryGetVal(atmNode.RawSensors, "air_humidity", out double h)) _atmHumidSeries.Points.Add(new DataPoint(_dataPointIndex, h));
                if (TryGetVal(atmNode.RawSensors, "rain_intensity", out double r)) _atmRainSeries.Points.Add(new DataPoint(_dataPointIndex, r));
                if (TryGetVal(atmNode.RawSensors, "wind_speed", out double w)) _atmWindSeries.Points.Add(new DataPoint(_dataPointIndex, w));
                if (TryGetVal(atmNode.RawSensors, "light_intensity", out double l)) _atmLightSeries.Points.Add(new DataPoint(_dataPointIndex, l));
                if (TryGetVal(atmNode.RawSensors, "barometric_pressure", out double p)) _atmPressSeries.Points.Add(new DataPoint(_dataPointIndex, p));
                atmUpdated = true;
            }

            if (soilUpdated) PruneAndRefresh(SoilPlotModel, _soilTempSeries, _soilMoistSeries);
            if (atmUpdated) PruneAndRefresh(AtmPlotModel, _atmTempSeries, _atmHumidSeries, _atmRainSeries, _atmWindSeries, _atmLightSeries, _atmPressSeries);
        }

        private void PruneAndRefresh(PlotModel model, params LineSeries[] seriesList)
        {
            foreach (var series in seriesList)
            {
                while (series.Points.Count > MAX_POINTS) series.Points.RemoveAt(0);
            }
            model.InvalidatePlot(true);
        }

        private bool TryGetVal(Dictionary<string, object> dict, string key, out double val)
        {
            val = 0;
            if (dict == null) return false;
            var entry = dict.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (entry.Key != null && double.TryParse(entry.Value?.ToString(), out val)) return true;
            return false;
        }

        private async void AutoScanPorts(object sender, EventArgs e)
        {
            if (_serialService.IsConnected || _isScanning || _isMasterConnected) return;
            _isScanning = true;
            await Task.Run(async () =>
            {
                try
                {
                    var ports = _serialService.GetAvailablePorts();
                    foreach (var port in ports)
                    {
                        using var cts = new CancellationTokenSource(1000);
                        if (await TryConnectPort(port, cts.Token)) return;
                    }
                }
                catch { }
                finally { _isScanning = false; }
            });
        }

        private async Task<bool> TryConnectPort(string port, CancellationToken token)
        {
            try
            {
                _serialService.Connect(port, 115200);
                _serialService.SendData("helloMaster");

                int waited = 0;
                while (waited < 1500)
                {
                    if (_isMasterConnected)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MasterStatus = $"Connected ({port})";
                            _settingsService.Config.SavedComPort = port;
                            _settingsService.SaveConfig();
                            _serialService.SendData("getListDevice");
                            _dataTimer.Start();
                            _scanTimer.Stop();
                            ShowWindowsNotify("System", $"Connected to Master at {port}");
                        });
                        return true;
                    }
                    await Task.Delay(100, token);
                    waited += 100;
                }
            }
            catch { }

            _serialService.Disconnect();
            return false;
        }

        [RelayCommand] private void GetDataNow() => _serialService.SendData("getDataNow");

        [RelayCommand]
        private void DeleteNode(string id)
        {
            var node = Nodes.FirstOrDefault(n => n.NodeId == id);
            if (node != null) Nodes.Remove(node);

            _serialService.SendData($"deleteNode {id}");
            ShowWindowsNotify("System", $"Sent delete command for {id}");
        }

        private void UpdateNodeList(List<NodeInfo> list)
        {
            Nodes.Clear();
            if (list == null) return;
            foreach (var item in list)
            {
                string type = item.Type ?? (item.Id.Contains("atm") ? "atmospheric" : "soil");
                Nodes.Add(new NodeDisplayModel { NodeId = item.Id, Type = type, Status = item.Status });
            }
        }

        private async Task GetDataRoutine()
        {
            if (_isMasterConnected) _serialService.SendData("getDataNow");
        }

        private async void UploadDataToServer()
        {
            if (!_settingsService.Config.IsDataSendEnabled) return;
            AddToLog("API: Preparing upload...");

            try
            {
                var atmNode = Nodes.FirstOrDefault(n => n.Type.Contains("atm"));

                var payload = new
                {
                    hub_id = _settingsService.Config.HubId,
                    timestamp = DateTime.UtcNow,
                    data = new
                    {
                        soil_nodes = Nodes.Where(n => n.Type.Contains("soil")).Select(n => new { node_id = n.NodeId, sensors = n.RawSensors }),
                        atmospheric_node = atmNode != null ? new { node_id = atmNode.NodeId, sensors = atmNode.RawSensors } : null
                    }
                };

                string url = _settingsService.Config.ApiEndpoint;
                if (string.IsNullOrEmpty(url)) { AddToLog("API Error: Endpoint is empty"); return; }

                var response = await _httpClient.PostAsJsonAsync(url, payload);
                if (response.IsSuccessStatusCode)
                    ShowToast("Cloud", "Data Uploaded Successfully", "Success");
                else
                    ShowToast("Cloud", $"Upload Failed: {response.StatusCode}", "Danger");
            }
            catch (Exception ex)
            {
                ShowToast("Cloud", "API Connection Error", "Danger");
                AddToLog($"API Ex: {ex.Message}");
            }
        }

        private void AddToLog(string msg)
        {
            Application.Current.Dispatcher.Invoke(() => {
                _logBuilder.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                if (_logBuilder.Length > 20000) _logBuilder.Length = 20000;
                AppLogs = _logBuilder.ToString();
            });
        }

        private void ShowToast(string t, string m, string type) => RequestNotification?.Invoke(t, m, type);

        private void ShowWindowsNotify(string t, string m)
        {
            NotificationService?.Notify(t, m, NotificationType.Information);
        }

        // --- INIT PLOTS (Đầy đủ & Chi tiết) ---
        private void InitializePlots()
        {
            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            var textColor = isDark ? OxyColors.White : OxyColors.Black;
            var gridColor = isDark ? OxyColor.Parse("#30FFFFFF") : OxyColor.Parse("#30000000");

            // 1. Soil Plot
            SoilPlotModel = CreateBasePlot("Soil Data (Average)", textColor, gridColor, false);

            _soilTempSeries = CreateSeries("Temp (°C)", OxyColors.OrangeRed, "LeftAxis");
            _soilMoistSeries = CreateSeries("Moisture (%)", OxyColors.ForestGreen, "LeftAxis");

            SoilPlotModel.Series.Add(_soilTempSeries);
            SoilPlotModel.Series.Add(_soilMoistSeries);

            SoilPlotModel.InvalidatePlot(true);

            // 2. Atm Plot
            AtmPlotModel = CreateBasePlot("Atmospheric Data (6 Params)", textColor, gridColor, true);

            _atmTempSeries = CreateSeries("Temp (°C)", OxyColors.OrangeRed, "LeftAxis");
            _atmHumidSeries = CreateSeries("Humid (%)", OxyColors.DeepSkyBlue, "LeftAxis");
            _atmRainSeries = CreateSeries("Rain (mm)", OxyColors.Blue, "LeftAxis");
            _atmWindSeries = CreateSeries("Wind (m/s)", OxyColors.Teal, "LeftAxis");
            _atmLightSeries = CreateSeries("Light (lux)", OxyColors.Gold, "RightAxis");
            _atmPressSeries = CreateSeries("Pressure (hPa)", OxyColors.Purple, "RightAxis");

            AtmPlotModel.Series.Add(_atmTempSeries);
            AtmPlotModel.Series.Add(_atmHumidSeries);
            AtmPlotModel.Series.Add(_atmRainSeries);
            AtmPlotModel.Series.Add(_atmWindSeries);
            AtmPlotModel.Series.Add(_atmLightSeries);
            AtmPlotModel.Series.Add(_atmPressSeries);

            AtmPlotModel.InvalidatePlot(true);
        }

        private LineSeries CreateSeries(string title, OxyColor color, string axisKey)
        {
            return new LineSeries
            {
                Title = title,
                Color = color,
                MarkerType = MarkerType.Circle,
                MarkerSize = 2,
                StrokeThickness = 1.5,
                YAxisKey = axisKey
            };
        }

        private PlotModel CreateBasePlot(string title, OxyColor textColor, OxyColor gridColor, bool useDualAxis)
        {
            var model = new PlotModel
            {
                Title = title,
                TextColor = textColor,
                PlotAreaBorderColor = OxyColors.Transparent,
                TitleFontSize = 14
            };

            var legend = new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendOrientation = LegendOrientation.Horizontal,
                LegendBorderThickness = 0,
                LegendFontSize = 10,
                LegendTextColor = textColor
            };
            model.Legends.Add(legend);

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = gridColor,
                TicklineColor = textColor,
                TextColor = textColor,
                MinimumRange = 10
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Key = "LeftAxis",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = gridColor,
                TicklineColor = textColor,
                TextColor = textColor,
                MinimumRange = 10,
                Title = "Value"
            });

            if (useDualAxis)
            {
                model.Axes.Add(new LinearAxis
                {
                    Position = AxisPosition.Right,
                    Key = "RightAxis",
                    TicklineColor = textColor,
                    TextColor = textColor,
                    MinimumRange = 100,
                    Title = "High Value"
                });
            }

            return model;
        }
    }
}