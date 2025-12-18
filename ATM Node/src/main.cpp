/**
 * ATM NODE (SLAVE) - ESP32
 * Chức năng: Trạm khí tượng (Nhiệt, Ẩm, Áp suất, Mưa, Gió, Ánh sáng)
 * Giao tiếp: NRF24L01 với Master Node
 */

#include <SPI.h>
#include <RF24.h>
#include <EEPROM.h>
#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BMP280.h>
#include <DHT.h>

// --- CẤU HÌNH ID (QUAN TRỌNG: PHẢI BẮT ĐẦU BẰNG "atm") ---
const char* MY_NODE_ID = "atm00001"; 

// --- CẤU HÌNH PIN NRF24L01 (ESP32) ---
#define PIN_CE    17
#define PIN_CSN   5
// SCK=18, MISO=19, MOSI=23 (Mặc định VSPI ESP32)

// --- CẤU HÌNH PIN UI ---
#define PIN_BTN   25
#define PIN_LED   2

// --- CẤU HÌNH PIN CẢM BIẾN ---
#define PIN_LIGHT_SENSOR    35 
#define PIN_RAIN_SENSOR     34 
#define PIN_DHT             16 
#define PIN_WIND_SENSOR     12 

// --- KHỞI TẠO CẢM BIẾN ---
#define DHTTYPE DHT11
DHT dht(PIN_DHT, DHTTYPE);
Adafruit_BMP280 bmp; // I2C (SDA=21, SCL=22)

// --- BIẾN ĐO GIÓ ---
volatile unsigned long windPulseCount = 0;
unsigned long lastWindTime = 0;
const float WIND_CUP_CIRCUMFERENCE = 0.565; // Chu vi quay (m)

// --- CẤU HÌNH RADIO ---
RF24 radio(PIN_CE, PIN_CSN);
const uint64_t REGISTER_PIPE = 0xF0F0F0F0E1LL;
const uint64_t BASE_ADDR_PREFIX = 0xF0F0F0F000LL;
const int EEPROM_ADDR_FLAG = 0; 
#define EEPROM_SIZE 4 // Cần khai báo size cho ESP32

// --- STRUCT DỮ LIỆU (PACKED - KHỚP 100% VỚI MASTER) ---
struct __attribute__((packed)) AtmData {
  float air_temp;
  float air_humid;
  uint8_t rain;       // Master nhận 0-100
  float wind;
  float light;
  float pressure;
};

struct __attribute__((packed)) RegisterPacket {
  char cmd[4];
  char id[11];
};

// --- BIẾN HỆ THỐNG ---
bool isRegistered = false;
uint64_t myAddress;
unsigned long lastBlink = 0;

// --- HÀM NGẮT ĐẾM GIÓ ---
void IRAM_ATTR countWindPulse() {
  windPulseCount++;
}

// --- HÀM HASH ĐỊA CHỈ ---
uint64_t generateNodeAddress(const char* str) {
    unsigned long hash = 5381;
    int c;
    while ((c = *str++)) hash = ((hash << 5) + hash) + c;
    return BASE_ADDR_PREFIX | (hash & 0xFF);
}

// Forward declaration
void registerToMaster();
void listenAndReply();
void readSensors(AtmData &data);
void handleButton();

void setup() {
  Serial.begin(115200);
  
  // 1. Cấu hình Pin
  pinMode(PIN_LED, OUTPUT);
  pinMode(PIN_BTN, INPUT_PULLUP);
  pinMode(PIN_LIGHT_SENSOR, INPUT);
  pinMode(PIN_RAIN_SENSOR, INPUT);
  pinMode(PIN_WIND_SENSOR, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(PIN_WIND_SENSOR), countWindPulse, FALLING);
  
  // 2. Khởi động EEPROM (ESP32 cần begin size)
  EEPROM.begin(EEPROM_SIZE);

  // 3. Khởi động Cảm biến
  dht.begin();
  if (!bmp.begin(0x76)) { // Thử 0x76 trước
    if (!bmp.begin(0x77)) { // Thử tiếp 0x77
        Serial.println(F("BMP280 Error!"));
    }
  }
  lastWindTime = millis();

  // 4. Khởi động Radio
  if (!radio.begin()) {
    Serial.println(F("Radio failed!"));
    while (1) { digitalWrite(PIN_LED, HIGH); delay(100); digitalWrite(PIN_LED, LOW); delay(100); }
  }

  // Cấu hình NRF (Giống Soil Node & Master)
  radio.setPALevel(RF24_PA_HIGH); 
  radio.setDataRate(RF24_250KBPS);
  radio.setRetries(5, 15);
  radio.enableDynamicPayloads();
  
  // Tính địa chỉ
  myAddress = generateNodeAddress(MY_NODE_ID);
  
  Serial.print("Node ID: "); Serial.println(MY_NODE_ID);
  Serial.printf("Address: %08X%08X\n", (uint32_t)(myAddress >> 32), (uint32_t)myAddress);
  Serial.print("Struct Size AtmData: "); Serial.println(sizeof(AtmData));

  // Kiểm tra trạng thái đăng ký cũ
  if (EEPROM.read(EEPROM_ADDR_FLAG) == 1) {
    isRegistered = true;
    Serial.println("RECOVERED: Already REGISTERED.");
    // Nháy LED 2 lần
    for(int i=0; i<2; i++) { digitalWrite(PIN_LED, HIGH); delay(200); digitalWrite(PIN_LED, LOW); delay(200); }
  } else {
    Serial.println("System Ready. Waiting to register...");
  }
}

void loop() {
  handleButton(); 

  if (!isRegistered) {
    // Nháy đèn chậm chờ đăng ký
    if (millis() - lastBlink > 500) {
      lastBlink = millis();
      digitalWrite(PIN_LED, !digitalRead(PIN_LED));
    }
    registerToMaster();
  } else {
    listenAndReply();
  }
}

// --- XỬ LÝ NÚT NHẤN (RESET) ---
void handleButton() {
  if (digitalRead(PIN_BTN) == LOW) {
    delay(50);
    if (digitalRead(PIN_BTN) == LOW) {
      Serial.println("Manual RESET Registration...");
      isRegistered = false;
      EEPROM.write(EEPROM_ADDR_FLAG, 0);
      EEPROM.commit(); // ESP32 cần commit
      digitalWrite(PIN_LED, LOW);
      while(digitalRead(PIN_BTN) == LOW);
      delay(1000);
    }
  }
}

// --- ĐỌC CẢM BIẾN ---
void readSensors(AtmData &data) {
  // 1. Tính toán Gió (Dựa trên thời gian từ lần đọc trước đến nay)
  unsigned long currentTime = millis();
  float deltaTime = (currentTime - lastWindTime) / 1000.0;
  if (deltaTime <= 0) deltaTime = 1.0; // Tránh chia 0
  
  // Đọc xung an toàn
  detachInterrupt(digitalPinToInterrupt(PIN_WIND_SENSOR));
  unsigned long pulses = windPulseCount;
  windPulseCount = 0; // Reset xung cho chu kỳ tiếp theo
  attachInterrupt(digitalPinToInterrupt(PIN_WIND_SENSOR), countWindPulse, FALLING);
  
  data.wind = (pulses / deltaTime) * WIND_CUP_CIRCUMFERENCE;
  lastWindTime = currentTime; // Cập nhật thời gian mốc

  // 2. DHT11
  float h = dht.readHumidity();
  float t = dht.readTemperature();
  if (isnan(h) || isnan(t)) { h = 0; t = 0; }
  data.air_humid = h;
  data.air_temp = t;

  // 3. BMP280
  data.pressure = bmp.readPressure() / 100.0F; // hPa

  // 4. Ánh sáng
  data.light = analogRead(PIN_LIGHT_SENSOR);

  // 5. Mưa (Chuyển đổi sang thang 0-100)
  int rainRaw = analogRead(PIN_RAIN_SENSOR);
  int rainIntensity = map(rainRaw, 0, 4095, 100, 0); // ESP32 ADC 12bit (0-4095)
  if (rainIntensity < 0) rainIntensity = 0;
  if (rainIntensity < 5) rainIntensity = 0; // Lọc nhiễu
  data.rain = (uint8_t)rainIntensity;

  // Debug
  Serial.printf("Sensors -> T:%.1f H:%.1f P:%.1f Rain:%d Wind:%.1f Light:%.0f\n", 
                data.air_temp, data.air_humid, data.pressure, data.rain, data.wind, data.light);
}

// --- ĐĂNG KÝ VỚI MASTER ---
void registerToMaster() {
  radio.stopListening();
  radio.openWritingPipe(REGISTER_PIPE);
  
  RegisterPacket pkt;
  memset(&pkt, 0, sizeof(pkt)); 
  strcpy(pkt.cmd, "REG");
  strncpy(pkt.id, MY_NODE_ID, 10);
  
  Serial.print("Sending REG... ");
  if (radio.write(&pkt, sizeof(pkt))) {
    Serial.println("Sent OK.");
    
    // Mở kênh nghe phản hồi
    radio.openReadingPipe(1, myAddress);
    radio.startListening();
    
    unsigned long startWait = millis();
    while (millis() - startWait < 1000) { 
      if (radio.available()) {
        char ack[10] = {0};
        uint8_t len = radio.getDynamicPayloadSize();
        if (len > 0 && len < 10) {
            radio.read(&ack, len);
            if (strcmp(ack, "REG_OK") == 0) {
              isRegistered = true;
              EEPROM.write(EEPROM_ADDR_FLAG, 1);
              EEPROM.commit();
              digitalWrite(PIN_LED, LOW);
              Serial.println("REGISTER SUCCESS!");
              // Nháy 3 lần
              for(int i=0; i<3; i++) { digitalWrite(PIN_LED, HIGH); delay(100); digitalWrite(PIN_LED, LOW); delay(100); }
              return;
            }
        } else {
             char trash[32]; radio.read(&trash, len);
        }
      }
    }
    if (!isRegistered) Serial.println("Timeout waiting for ACK.");
  } else {
    Serial.println("Send FAILED.");
  }
  delay(random(1500, 3000));
}

// --- LẮNG NGHE LỆNH GET ---
void listenAndReply() {
  radio.openReadingPipe(1, myAddress);
  radio.startListening();
  
  if (radio.available()) {
    char req[5] = {0};
    radio.read(&req, sizeof(req)); 
    
    if (strncmp(req, "GET", 3) == 0) {
      digitalWrite(PIN_LED, HIGH); // Bật đèn khi đang xử lý
      Serial.println("CMD: GET received.");
      
      AtmData data;
      readSensors(data); // Đọc cảm biến ngay tức thì
      
      radio.stopListening();
      radio.openWritingPipe(myAddress);
      
      if (radio.write(&data, sizeof(data))) {
          Serial.println("Data Sent. Waiting for OK...");
          
          radio.openReadingPipe(1, myAddress);
          radio.startListening();
          
          unsigned long waitAck = millis();
          while(millis() - waitAck < 200) { 
             if(radio.available()) {
                 char ack[5] = {0};
                 radio.read(&ack, sizeof(ack));
                 if(strncmp(ack, "OK", 2) == 0) {
                    Serial.println("Done.");
                    break;
                 }
             }
          }
      } else {
          Serial.println("Data Send Failed.");
      }
      digitalWrite(PIN_LED, LOW);
    } else {
        char trash[32]; radio.read(&trash, sizeof(trash)); 
    }
  }
}