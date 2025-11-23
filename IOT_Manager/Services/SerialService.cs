using System;
using System.IO.Ports;
using System.Diagnostics;

namespace IOT_Manager.Services
{
    public class SerialService : ISerialService
    {
        private SerialPort _serialPort;

        // Event bắn dữ liệu ra ngoài khi nhận được từ COM
        public event Action<string> DataReceived;

        public SerialService()
        {
            _serialPort = new SerialPort();
            _serialPort.DataReceived += OnSerialDataReceived;
        }

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void Connect(string portName, int baudRate)
        {
            if (_serialPort.IsOpen) _serialPort.Close();

            _serialPort.PortName = portName;
            _serialPort.BaudRate = baudRate;
            _serialPort.Parity = Parity.None;
            _serialPort.DataBits = 8;
            _serialPort.StopBits = StopBits.One;
            _serialPort.Handshake = Handshake.None;

            try
            {
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening port: {ex.Message}");
                throw; // Ném lỗi để UI xử lý (hiện thông báo)
            }
        }

        public void Disconnect()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        public void SendData(string data)
        {
            if (IsConnected)
            {
                _serialPort.WriteLine(data); // Hoặc Write(data) tùy thiết bị
            }
        }

        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // Đọc dữ liệu (ReadExisting hoặc ReadLine tùy protocol)
                string data = _serialPort.ReadLine();

                // Bắn sự kiện ra ngoài (UI sẽ bắt cái này)
                DataReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Read error: {ex.Message}");
            }
        }
    }
}