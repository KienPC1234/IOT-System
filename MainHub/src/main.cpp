/**
 * ESP32 Master Node - FLUSH RX FIX
 * - Fix: Thêm radio.flush_rx() để xóa dữ liệu cũ (15 bytes) trước khi nhận dữ liệu mới (8 bytes).
 * - Fix: Ép kiểu __attribute__((packed)) để đồng bộ.
 */

#include <Arduino.h>
#include <SPI.h>
#include <RF24.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <vector>

#define PIN_CE    4
#define PIN_CSN   5
#define PIN_LED   21
#define PIN_BTN   22
#define Version "1.0B"

RF24 radio(PIN_CE, PIN_CSN);
const uint64_t REGISTER_PIPE = 0xF0F0F0F0E1LL; 
const uint64_t BASE_ADDR_PREFIX = 0xF0F0F0F000LL; 

enum NodeType { UNKNOWN = 0, SOIL_NODE = 1, ATM_NODE = 2 };

struct NodeDevice {
  char id[11];
  NodeType type;
  bool isOnline;
};

// Ép kiểu packed để đảm bảo size là 8 bytes trên cả ESP32 và Arduino
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

  Serial.println("--- MASTER STARTING ---");

  if (!radio.begin()) {
    Serial.println(F("{\"error\":\"NRF24L01 init failed\"}"));
    while (1) delay(100);
  }
  
  radio.setPALevel(RF24_PA_HIGH); 
  radio.setDataRate(RF24_250KBPS);
  radio.setRetries(5, 15);
  radio.enableDynamicPayloads(); // Quan trọng
  
  radio.openReadingPipe(1, REGISTER_PIPE);
  radio.startListening();

  Serial.printf("Struct Check: RegisterPacket=%d bytes, SoilData=%d bytes\n", sizeof(RegisterPacket), sizeof(SoilData));

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

    if (cmd == "getListDevice") {
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
        Serial.println("{\"error\":\"no_devices\"}"); return;
      }

      // --- FIX QUAN TRỌNG: XÓA SẠCH BUFFER TRƯỚC KHI BẮT ĐẦU ---
      // Điều này giúp loại bỏ các gói tin REG cũ (15 bytes) còn sót lại
      radio.stopListening();
      radio.flush_rx(); 
      // ---------------------------------------------------------

      for (auto& device : devices) {
        uint64_t nodeAddr = generateNodeAddress(device.id);
        // Serial.printf("DEBUG: Pinging Node %s...\n", device.id);
        
        radio.stopListening();
        // Flush lần nữa để chắc chắn sạch sẽ cho node này
        radio.flush_rx();
        radio.openWritingPipe(nodeAddr);
        
        bool success = false;
        char req[] = "GET";
        
        delay(5); // Delay cực nhỏ để ổn định

        for (int i = 0; i < 5; i++) {
          if (radio.write(&req, sizeof(req))) {
            radio.openReadingPipe(1, nodeAddr);
            radio.startListening();
            
            unsigned long startWait = millis();
            bool timeout = false;
            while (!radio.available()) {
              if (millis() - startWait > 200) { timeout = true; break; }
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
                    // Serial.printf("DEBUG: Size Mismatch! Exp: %d, Got: %d\n", sizeof(SoilData), payloadSize);
                    char trash[32]; radio.read(&trash, payloadSize); // Đọc bỏ rác
                }
              } else {
                 // ATM Logic
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
                  delay(10); // Delay để Slave kịp chuyển sang RX
                  radio.write(&ack, sizeof(ack));
                  
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
      // Quay lại lắng nghe kênh đăng ký
      radio.openReadingPipe(1, REGISTER_PIPE);
      radio.startListening();
    }
    else if (cmd == "deleteAllNode") { clearDevices(); }
    else if (cmd == "registerNewNode") enterRegisterMode();
    else if (cmd == "cancelRegister") { exitRegisterMode(); Serial.println("{\"event\":\"register_cancelled\"}"); }
    else if (cmd == "helloMaster") {Serial.println("Hi!"); Serial.println(Version);};
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
    uint8_t size = radio.getDynamicPayloadSize();
    RegisterPacket packet;
    
    if (size == sizeof(RegisterPacket)) {
        radio.read(&packet, sizeof(packet));
        // Serial.printf("DEBUG: Reg Req from %s\n", packet.id);

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
              
              delay(50); // Chờ Slave chuyển RX
              char ack[] = "REG_OK";
              if (radio.write(&ack, sizeof(ack))) {
                  Serial.print("{\"event\":\"registered\",\"id\":\""); Serial.print(newId); Serial.println("\"}");
                  exitRegisterMode();
                  digitalWrite(PIN_LED, HIGH); delay(500); digitalWrite(PIN_LED, LOW);
              } else {
                  devices.pop_back(); saveDevices();
              }
              
              radio.openReadingPipe(1, REGISTER_PIPE);
              radio.startListening();
            }
          }
        }
    } else {
        char trash[32]; radio.read(trash, size); // Đọc bỏ
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