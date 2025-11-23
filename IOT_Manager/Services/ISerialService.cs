using System;

namespace IOT_Manager.Services
{
    public interface ISerialService
    {
        // Lấy danh sách cổng COM
        string[] GetAvailablePorts();

        // Kết nối và ngắt kết nối
        void Connect(string portName, int baudRate);
        void Disconnect();

        // Gửi dữ liệu
        void SendData(string data);

        // Trạng thái
        bool IsConnected { get; }

        // Sự kiện nhận dữ liệu (để bắn data về UI)
        event Action<string> DataReceived;
    }
}