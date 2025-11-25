using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace IOT_Manager.Services
{
    public interface ISerialService
    {
        string[] GetAvailablePorts();
        void Connect(string portName, int baudRate);
        void Disconnect();
        void SendData(string data);
        bool IsConnected { get; }
        event Action<string> DataReceived;
    }

    public class SerialService : ISerialService
    {
        private SerialPort _serialPort;
        private CancellationTokenSource _readCts;
        public event Action<string> DataReceived;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public string[] GetAvailablePorts() => SerialPort.GetPortNames();

        public void Connect(string portName, int baudRate)
        {
            Disconnect(); // Đảm bảo ngắt kết nối cũ trước

            _serialPort = new SerialPort
            {
                PortName = portName,
                BaudRate = baudRate,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 2000,
                WriteTimeout = 500
            };

            try
            {
                _serialPort.Open();

                // Bắt đầu luồng đọc dữ liệu riêng biệt để không block UI
                _readCts = new CancellationTokenSource();
                Task.Run(() => ReadSerialLoop(_readCts.Token));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Serial Connection Error: {ex.Message}");
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            _readCts?.Cancel();

            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    try { _serialPort.Close(); } catch { }
                }
                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        public void SendData(string data)
        {
            if (IsConnected)
            {
                try
                {
                    _serialPort.WriteLine(data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Send Error: {ex.Message}");
                    Disconnect(); // Tự động ngắt nếu mất kết nối vật lý
                }
            }
        }

        // Vòng lặp đọc dữ liệu liên tục (Hiệu quả hơn DataReceived event truyền thống trên một số driver)
        private async Task ReadSerialLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                try
                {
                    string data = await Task.Run(() =>
                    {
                        try { return _serialPort.ReadLine(); }
                        catch { return null; }
                    }, token);

                    if (!string.IsNullOrEmpty(data))
                    {
                        DataReceived?.Invoke(data);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    // Lỗi đọc nghiêm trọng (ví dụ rút cáp)
                    Disconnect();
                    break;
                }
            }
        }
    }
}