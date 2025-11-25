import serial
import time
import json
import random
import threading
import sys

# --- CẤU HÌNH ---
DEFAULT_PORT = 'COM11'
BAUD_RATE = 115200

# Danh sách thiết bị giả lập sẵn (3 Soil, 1 ATM)
VIRTUAL_DEVICES = [
    {"id": "soil00001", "type": "soil", "status": "online"},
    {"id": "soil00002", "type": "soil", "status": "online"},
    {"id": "soil00003", "type": "soil", "status": "online"},
    {"id": "atm00001",  "type": "atm",  "status": "online"}
]

def open_serial_port():
    port = input(f"Nhập cổng COM (mặc định {DEFAULT_PORT}): ").strip()
    if not port:
        port = DEFAULT_PORT
    
    try:
        ser = serial.Serial(port, BAUD_RATE, timeout=0.1)
        print(f"\n[SYSTEM] Đã mở {port} tốc độ {BAUD_RATE}")
        print("[SYSTEM] Đang lắng nghe lệnh...")
        return ser
    except serial.SerialException as e:
        print(f"[ERROR] Không thể mở cổng {port}. Lỗi: {e}")
        return None

def generate_soil_data(node_id):
    return {
        "sensors": {
            "soil_moisture": round(random.uniform(40.0, 90.0), 2),
            "soil_temperature": round(random.uniform(20.0, 35.0), 2)
        },
        "id": node_id
    }

def generate_atm_data(node_id):
    return {
        "sensors": {
            "air_temperature": round(random.uniform(25.0, 38.0), 2),
            "air_humidity": round(random.uniform(50.0, 95.0), 1),
            "rain_intensity": random.randint(0, 1),
            "wind_speed": round(random.uniform(0.0, 15.0), 2),
            "light_intensity": round(random.uniform(100.0, 5000.0), 1),
            "barometric_pressure": round(random.uniform(990.0, 1015.0), 1)
        },
        "id": node_id
    }

def handle_get_data_now(ser):
    if not VIRTUAL_DEVICES:
        ser.write(b'{"error":"no_devices"}\r\n')
        ser.write(b'{"event":"data_collection_finished"}\r\n')
        return

    for device in VIRTUAL_DEVICES:
        time.sleep(0.3)
        is_offline = random.random() < 0.05

        if is_offline:
            resp = json.dumps({"id": device["id"], "status": "offline"})
        else:
            if device["type"] == "soil":
                data = generate_soil_data(device["id"])
            else:
                data = generate_atm_data(device["id"])
            resp = json.dumps(data)
        
        print(f"[SENDING] {resp}")
        ser.write((resp + '\r\n').encode('utf-8'))
    
    time.sleep(0.1)
    end_msg = '{"event":"data_collection_finished"}'
    print(f"[SENDING] {end_msg}")
    ser.write((end_msg + '\r\n').encode('utf-8'))

def main():
    ser = open_serial_port()
    if not ser:
        input("Nhấn Enter để thoát...")
        return

    time.sleep(1)
    ready_msg = '{"status":"system_ready"}'
    print(f"[SENDING] {ready_msg}")
    ser.write((ready_msg + '\r\n').encode('utf-8'))

    buffer = ""

    try:
        while True:
            if ser.in_waiting > 0:
                raw = ser.read(ser.in_waiting)
                print(f"[RAW] {raw}  (hex: {raw.hex()})")

                data = raw.decode('utf-8', errors='ignore')
                buffer += data

                # --- Trường hợp có xuống dòng ---
                if '\n' in buffer:
                    lines = buffer.split('\n')
                    commands = lines[:-1]
                    buffer = lines[-1]
                else:
                    # --- Không có xuống dòng → thử parse command luôn ---
                    commands = [buffer]
                    buffer = ""

                for cmd in commands:
                    cmd = cmd.strip()
                    if not cmd:
                        continue
                    
                    print(f"[RECEIVED] {cmd}")

                    # Xử lý lệnh
                    if cmd == "helloMaster":
                        ser.write(b"Hi!\r\n")
                        ser.write(b"FW_V1.2_SIMULATOR\r\n")

                    elif cmd == "getListDevice":
                        resp = json.dumps(VIRTUAL_DEVICES)
                        print(f"[SENDING] {resp}")
                        ser.write((resp + '\r\n').encode('utf-8'))

                    elif cmd == "getDataNow":
                        handle_get_data_now(ser)

                    elif cmd == "deleteAllNode":
                        VIRTUAL_DEVICES.clear()
                        ser.write(b'{"event":"all_nodes_deleted"}\r\n')

                    elif cmd.startswith("deleteNode "):
                        node_id = cmd.split(" ")[1].strip()
                        VIRTUAL_DEVICES[:] = [d for d in VIRTUAL_DEVICES if d["id"] != node_id]
                        msg = json.dumps({"event": "deleted", "id": node_id})
                        ser.write((msg + '\r\n').encode('utf-8'))

                    elif cmd == "registerNewNode":
                        ser.write(b'{"status":"register_mode_active"}\r\n')

                    elif cmd == "cancelRegister":
                        ser.write(b'{"event":"register_cancelled"}\r\n')
            time.sleep(0.01)

    except KeyboardInterrupt:
        print("\n[SYSTEM] Đang thoát...")
        ser.close()


if __name__ == "__main__":
    main()
