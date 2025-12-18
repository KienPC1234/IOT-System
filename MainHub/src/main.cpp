/**
 * ESP32 Master Node - COMPLETE LOGIC
 * - Auto Exit Register Mode: Tự thoát đăng ký khi xong.
 * - Data Validation: Kiểm tra size gói tin.
 * - PA Low: Ổn định sóng.
 * - ATM Logic: Đã map đủ trường dữ liệu.
 * - Handshake: Lệnh helloMaster.
 * - End of Data Signal: Báo hiệu khi quét xong danh sách.
 */

#include <Arduino.h>
#include <SPI.h>
#include <RF24.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <vector>

const String Version = "FW_V1.2"; // Phiên bản Firmware

#define PIN_CE    4
#define PIN_CSN   5
#define PIN_LED   21
#define PIN_BTN   22

RF24 radio(PIN_CE, PIN_CSN);
const uint64_t REGISTER_PIPE = 0xF0F0F0F0E1LL; 
const uint64_t BASE_ADDR_PREFIX = 0xF0F0F0F000LL; 

enum NodeType { UNKNOWN = 0, SOIL_NODE = 1, ATM_NODE = 2 };

struct NodeDevice {
  char id[11];
  NodeType type;
  bool isOnline;
};

// Ép kiểu packed để đảm bảo size đồng nhất
struct __attribute__((packed)) SoilData {
  float moisture;
  float temperature;
};

struct __attribute__((packed)) AtmData {
  float air_temp;
  float air_humid;
  uint8_t rain;
  float wind;
  float light;
  float pressure;
};

struct __attribute__((packed)) RegisterPacket {
  char cmd[4]; 
  char id[11];
};

enum SystemState { STATE_IDLE, STATE_REGISTERING, STATE_RESET_PENDING };

SystemState currentState = STATE_IDLE;
std::vector<NodeDevice> devices;
Preferences preferences;

unsigned long btnPressTime = 0;
bool lastBtnState = HIGH;
unsigned long lastReleaseTime = 0;
int clickCount = 0;
unsigned long lastBlink = 0;
bool ledState = LOW;

void loadDevices();
void saveDevices();
void clearDevices();
void processSerialCommand();
void handleButton();
void handleLed();
void enterRegisterMode();
void exitRegisterMode();
void handleRegistration();
uint64_t generateNodeAddress(const char* id);

void setup() {
  Serial.begin(115200);
  pinMode(PIN_LED, OUTPUT);
  pinMode(PIN_BTN, INPUT_PULLUP);
  digitalWrite(PIN_LED, LOW);

  if (!radio.begin()) {
    Serial.println(F("{\"error\":\"NRF24L01 init failed\"}"));
    while (1) delay(100);
  }
  
  radio.setPALevel(RF24_PA_LOW); 
  radio.setDataRate(RF24_250KBPS);
  radio.setRetries(5, 15);
  radio.enableDynamicPayloads();
  
  radio.openReadingPipe(1, REGISTER_PIPE);
  radio.startListening();

  loadDevices();
  Serial.println("{\"status\":\"system_ready\"}");
}

void loop() {
  processSerialCommand();
  handleButton();
  handleLed();

  if (currentState == STATE_REGISTERING) {
    handleRegistration();
  }
}

uint64_t generateNodeAddress(const char* str) {
    unsigned long hash = 5381;
    int c;
    while ((c = *str++)) hash = ((hash << 5) + hash) + c;
    return BASE_ADDR_PREFIX | (hash & 0xFF); 
}

void handleButton() {
  if (currentState == STATE_RESET_PENDING && (millis() - lastReleaseTime > 2000)) {
    currentState = STATE_IDLE;
    clickCount = 0;
    digitalWrite(PIN_LED, LOW);
    Serial.println("{\"event\":\"reset_cancelled_timeout\"}");
  }

  bool reading = digitalRead(PIN_BTN);
  if (reading == LOW && lastBtnState == HIGH) btnPressTime = millis();

  if (reading == HIGH && lastBtnState == LOW) {
    unsigned long pressDuration = millis() - btnPressTime;
    lastReleaseTime = millis();
    if (pressDuration < 50) { lastBtnState = reading; return; }

    if (currentState == STATE_REGISTERING) {
       exitRegisterMode();
       Serial.println("{\"event\":\"register_cancelled\"}");
       lastBtnState = reading; return;
    }
    if (currentState == STATE_RESET_PENDING) {
       clearDevices();
       currentState = STATE_IDLE;
       clickCount = 0;
       lastBtnState = reading; return;
    }
    if (currentState == STATE_IDLE) {
      if (pressDuration >= 1000) { enterRegisterMode(); clickCount = 0; } 
      else { clickCount++; }
    }
  }

  if (currentState == STATE_IDLE && clickCount > 0 && (millis() - lastReleaseTime > 400)) {
    if (clickCount == 2) {
       currentState = STATE_RESET_PENDING;
       Serial.println("{\"status\":\"wait_confirm_reset\"}");
    }
    clickCount = 0;
  }
  lastBtnState = reading;
}

void handleLed() {
  unsigned long currentMillis = millis();
  if (currentState == STATE_REGISTERING) {
    if (currentMillis - lastBlink >= 200) {
      lastBlink = currentMillis; ledState = !ledState; digitalWrite(PIN_LED, ledState);
    }
  } else if (currentState == STATE_RESET_PENDING) {
    unsigned long cycleTime = currentMillis % 1300;
    if (cycleTime < 200 || (cycleTime > 400 && cycleTime < 600)) digitalWrite(PIN_LED, HIGH);
    else digitalWrite(PIN_LED, LOW);
  } else {
    if (digitalRead(PIN_LED) && currentState == STATE_IDLE) digitalWrite(PIN_LED, LOW); 
  }
}

void processSerialCommand() {
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    if (cmd.length() == 0) return;

    // --- LỆNH HANDSHAKE ---
    if (cmd == "helloMaster") {
        Serial.println("Hi!"); 
        Serial.println(Version);
    }
    else if (cmd == "getListDevice") {
      JsonDocument doc;
      JsonArray arr = doc.to<JsonArray>();
      for (const auto& device : devices) {
        JsonObject obj = arr.add<JsonObject>();
        obj["id"] = device.id;
        obj["type"] = (device.type == SOIL_NODE) ? "soil" : "atm";
        obj["status"] = device.isOnline ? "online" : "offline";
      }
      serializeJson(doc, Serial); Serial.println();
    }
    else if (cmd == "getDataNow") {
      if (devices.empty()) {
        Serial.println("{\"error\":\"no_devices\"}"); 
        Serial.println("{\"event\":\"data_collection_finished\"}"); // Vẫn báo finish để app biết đường tắt loading
        return;
      }

      // Xóa buffer để tránh đọc phải gói tin rác/cũ
      radio.stopListening();
      radio.flush_rx(); 

      for (auto& device : devices) {
        uint64_t nodeAddr = generateNodeAddress(device.id);
        
        radio.stopListening();
        radio.flush_rx();
        radio.openWritingPipe(nodeAddr);
        
        bool success = false;
        char req[] = "GET";
        
        delay(5); 

        // Thử kết nối 5 lần
        for (int i = 0; i < 5; i++) {
          if (radio.write(&req, sizeof(req))) {
            radio.openReadingPipe(1, nodeAddr);
            radio.startListening();
            
            unsigned long startWait = millis();
            bool timeout = false;
            while (!radio.available()) {
              if (millis() - startWait > 500) { timeout = true; break; }
            }
            
            if (!timeout) {
              uint8_t payloadSize = radio.getDynamicPayloadSize(); 
              
              JsonDocument doc;
              JsonObject sensors = doc["sensors"].to<JsonObject>();
              
              if (device.type == SOIL_NODE) {
                if (payloadSize == sizeof(SoilData)) {
                    SoilData data;
                    radio.read(&data, sizeof(data));
                    sensors["soil_moisture"] = data.moisture;
                    sensors["soil_temperature"] = data.temperature;
                    success = true;
                } else {
                    char trash[32]; radio.read(&trash, payloadSize);
                }
              } else {
                // --- LOGIC ATM ĐẦY ĐỦ ---
                if (payloadSize == sizeof(AtmData)) {
                    AtmData data;
                    radio.read(&data, sizeof(data));
                    sensors["air_temperature"] = data.air_temp;
                    sensors["air_humidity"] = data.air_humid;
                    sensors["rain_intensity"] = data.rain;
                    sensors["wind_speed"] = data.wind;
                    sensors["light_intensity"] = data.light;
                    sensors["barometric_pressure"] = data.pressure;
                    success = true;
                } else {
                     char trash[32]; radio.read(&trash, payloadSize);
                }
              }
              
              if (success) {
                  radio.stopListening();
                  radio.openWritingPipe(nodeAddr);
                  char ack[] = "OK";
                  delay(10); 
                  radio.write(&ack, sizeof(ack));
                  
                  // In dữ liệu của từng node ngay khi nhận được
                  doc["id"] = device.id;
                  serializeJson(doc, Serial); Serial.println();
                  break; 
              }
            }
          }
          delay(20);
        }
        device.isOnline = success;
        if(!success) {
           Serial.print("{\"id\":\""); Serial.print(device.id); Serial.println("\",\"status\":\"offline\"}");
        }
      }
      
      // --- THÔNG BÁO HOÀN TẤT ---
      // Gửi sau khi vòng lặp duyệt hết danh sách thiết bị
      Serial.println("{\"event\":\"data_collection_finished\"}");

      radio.openReadingPipe(1, REGISTER_PIPE);
      radio.startListening();
    }
    else if (cmd == "deleteAllNode") { clearDevices(); }
    else if (cmd.startsWith("deleteNode ")) {
        String idToDelete = cmd.substring(11); idToDelete.trim();
        bool found = false;
        for (auto it = devices.begin(); it != devices.end(); ) {
            if (String(it->id) == idToDelete) { it = devices.erase(it); found = true; } else { ++it; }
        }
        if (found) { saveDevices(); Serial.print("{\"event\":\"deleted\",\"id\":\""); Serial.print(idToDelete); Serial.println("\"}"); }
    }
    else if (cmd == "registerNewNode") enterRegisterMode();
    else if (cmd == "cancelRegister") { exitRegisterMode(); Serial.println("{\"event\":\"register_cancelled\"}"); }
  }
}

void enterRegisterMode() {
  currentState = STATE_REGISTERING;
  Serial.println("{\"status\":\"register_mode_active\"}");
  radio.openReadingPipe(1, REGISTER_PIPE);
  radio.startListening();
}

void exitRegisterMode() {
  currentState = STATE_IDLE;
  digitalWrite(PIN_LED, LOW);
}

void handleRegistration() {
  if (radio.available()) {
    RegisterPacket packet;
    uint8_t size = radio.getDynamicPayloadSize();

    if (size == sizeof(RegisterPacket)) {
      radio.read(&packet, sizeof(packet));
      
      if (strncmp(packet.cmd, "REG", 3) == 0) {
        String newId = String(packet.id);
        NodeType newType = UNKNOWN;
        if (newId.startsWith("soil")) newType = SOIL_NODE;
        else if (newId.startsWith("atm")) newType = ATM_NODE;
        
        if (newType != UNKNOWN) {
          bool exists = false;
          for (const auto& d : devices) { if (String(d.id) == newId) { exists = true; break; } }
          
          if (!exists) {
            NodeDevice newNode;
            strncpy(newNode.id, packet.id, 10); newNode.id[10] = '\0';
            newNode.type = newType; newNode.isOnline = true;
            devices.push_back(newNode);
            saveDevices();
            
            radio.stopListening();
            uint64_t nodeAddr = generateNodeAddress(newNode.id);
            radio.openWritingPipe(nodeAddr); 
            
            delay(50); // Delay quan trọng
            
            char ack[] = "REG_OK";
            if (radio.write(&ack, sizeof(ack))) {
               Serial.print("{\"event\":\"registered\",\"id\":\""); Serial.print(newId); Serial.println("\"}");
               exitRegisterMode();
               digitalWrite(PIN_LED, HIGH); delay(500); digitalWrite(PIN_LED, LOW);
            } else {
               // Nếu gửi ACK thất bại, xóa node vừa lưu để thử lại
               devices.pop_back(); saveDevices();
            }
            
            radio.openReadingPipe(1, REGISTER_PIPE);
            radio.startListening();
          }
        }
      }
    } else {
        // Xóa gói tin rác sai kích thước
        char trash[32]; radio.read(&trash, size);
    }
  }
}

void loadDevices() {
  preferences.begin("nodes", false); 
  int count = preferences.getInt("count", 0);
  devices.clear();
  for (int i = 0; i < count; i++) {
    String key = "node" + String(i);
    if (preferences.isKey(key.c_str())) {
      size_t len = preferences.getBytesLength(key.c_str());
      char buf[len]; preferences.getBytes(key.c_str(), buf, len);
      NodeDevice nd; memcpy(&nd, buf, sizeof(NodeDevice)); devices.push_back(nd);
    }
  }
  preferences.end();
}

void saveDevices() {
  preferences.begin("nodes", false);
  preferences.putInt("count", devices.size());
  for (int i = 0; i < devices.size(); i++) {
    String key = "node" + String(i); preferences.putBytes(key.c_str(), &devices[i], sizeof(NodeDevice));
  }
  preferences.end();
}

void clearDevices() {
  preferences.begin("nodes", false); preferences.clear(); preferences.end();
  devices.clear();
  Serial.println("{\"event\":\"all_nodes_deleted\"}");
  digitalWrite(PIN_LED, LOW);
}