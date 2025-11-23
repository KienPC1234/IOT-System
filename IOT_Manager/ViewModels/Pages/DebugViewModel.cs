using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IOT_Manager.Services;
using System.Collections.ObjectModel;
using System.Windows;
using OxyPlot;
using OxyPlot.Series;


namespace IOT_Manager.ViewModels.Pages
{
    public partial class DebugViewModel : ObservableObject
    {
        private readonly ISerialService _serialService;
        private const int MaxLogLength = 5000;

        // --- KHAI BÁO TƯỜNG MINH (Fix lỗi CS0103) ---

        private ObservableCollection<string> _availablePorts = new();
        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            set => SetProperty(ref _availablePorts, value);
        }

        private string _selectedPort;
        public string SelectedPort
        {
            get => _selectedPort;
            set => SetProperty(ref _selectedPort, value);
        }

        private int _baudRate = 9600;
        public int BaudRate
        {
            get => _baudRate;
            set => SetProperty(ref _baudRate, value);
        }

        private string _serialLog = "";
        public string SerialLog
        {
            get => _serialLog;
            set => SetProperty(ref _serialLog, value);
        }

        private string _messageToSend = "";
        public string MessageToSend
        {
            get => _messageToSend;
            set => SetProperty(ref _messageToSend, value);
        }

        private bool _isConnected = false;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    // Khi IsConnected thay đổi, tự động báo cập nhật cho IsNotConnected
                    OnPropertyChanged(nameof(IsNotConnected));
                }
            }
        }

        // Thuộc tính phụ để Bind lên giao diện (để khóa nút)
        public bool IsNotConnected => !IsConnected;

        private string _connectButtonText = "Connect";
        public string ConnectButtonText
        {
            get => _connectButtonText;
            set => SetProperty(ref _connectButtonText, value);
        }

        // --- CONSTRUCTOR ---
        public DebugViewModel(ISerialService serialService)
        {
            _serialService = serialService;
            _serialService.DataReceived += OnDataReceivedFromService;
            RefreshPorts();
        }

        // --- COMMANDS ---

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            var ports = _serialService.GetAvailablePorts();
            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
            }
            if (AvailablePorts.Count > 0) SelectedPort = AvailablePorts[0];
        }

        [RelayCommand]
        private void ToggleConnection()
        {
            if (IsConnected)
            {
                // Disconnect logic
                try
                {
                    _serialService.Disconnect();
                    AppendLog("[System]: Disconnected.");
                }
                catch (Exception ex) { AppendLog($"[Error]: {ex.Message}"); }
                finally
                {
                    IsConnected = false;
                    ConnectButtonText = "Connect";
                }
            }
            else
            {
                // Connect logic
                if (string.IsNullOrEmpty(SelectedPort)) return;
                try
                {
                    _serialService.Connect(SelectedPort, BaudRate);
                    IsConnected = true;
                    ConnectButtonText = "Disconnect";
                    AppendLog($"[System]: Connected to {SelectedPort}.");
                }
                catch (Exception ex)
                {
                    AppendLog($"[Error]: {ex.Message}");
                    IsConnected = false;
                }
            }
        }

        [RelayCommand]
        private void SendData()
        {
            if (!IsConnected || string.IsNullOrEmpty(MessageToSend)) return;
            try
            {
                _serialService.SendData(MessageToSend);
                AppendLog($"[TX]: {MessageToSend}");
                MessageToSend = "";
            }
            catch (Exception ex) { AppendLog($"[Error]: {ex.Message}"); }
        }

        [RelayCommand]
        private void ClearLog() => SerialLog = "";

        // --- HELPER ---
        private void OnDataReceivedFromService(string data)
        {
            Application.Current.Dispatcher.Invoke(() => AppendLog($"[RX]: {data.TrimEnd()}"));
        }

        private void AppendLog(string message)
        {
            string newLog = $"{DateTime.Now:HH:mm:ss} {message}\n";
            if (SerialLog.Length > MaxLogLength) SerialLog = SerialLog.Substring(SerialLog.Length / 2);
            SerialLog += newLog;
        }
    }
}