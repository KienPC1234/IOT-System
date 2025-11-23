/**
 * SOIL NODE (SLAVE) - DEBUG VERSION
 * - PA Level: HIGH
 * - Debug: In chi tiết quá trình gửi/nhận.
 * - Fix: Thêm __attribute__((packed))
 */

#include <SPI.h>
#include <RF24.h>
#include <EEPROM.h>

// --- CẤU HÌNH ID ---
const char* MY_NODE_ID = "soil00001"; 

// --- CHÂN KẾT NỐI ---
#define PIN_CE    9
#define PIN_CSN   10
#define PIN_LED   6
#define PIN_BTN   7
#define PIN_SOIL  A0
#define PIN_TEMP  A1

const float TEMP_OFFSET = -10.0;
const int EEPROM_ADDR_FLAG = 0; 

RF24 radio(PIN_CE, PIN_CSN);
const uint64_t REGISTER_PIPE = 0xF0F0F0F0E1LL;
const uint64_t BASE_ADDR_PREFIX = 0xF0F0F0F000LL;

struct __attribute__((packed)) SoilData {
  float moisture;
  float temperature;
};

struct __attribute__((packed)) RegisterPacket {
  char cmd[4];
  char id[11];
};

bool isRegistered = false;
uint64_t myAddress;
unsigned long lastBlink = 0;

uint64_t generateNodeAddress(const char* str) {
    unsigned long hash = 5381;
    int c;
    while ((c = *str++)) hash = ((hash << 5) + hash) + c;
    return BASE_ADDR_PREFIX | (hash & 0xFF);
}

void registerToMaster();
void listenAndReply();
void readSensors(SoilData &data);
void handleButton();

void setup() {
  Serial.begin(9600);
  pinMode(PIN_LED, OUTPUT);
  pinMode(PIN_BTN, INPUT_PULLUP);
  
  delay(500);

  if (!radio.begin()) {
    Serial.println(F("Radio failed!"));
    while (1) { digitalWrite(PIN_LED, HIGH); delay(100); digitalWrite(PIN_LED, LOW); delay(100); }
  }

  // CAO NHẤT THEO YÊU CẦU
  radio.setPALevel(RF24_PA_HIGH); 
  radio.setDataRate(RF24_250KBPS);
  radio.setRetries(5, 15);
  radio.enableDynamicPayloads();
  
  myAddress = generateNodeAddress(MY_NODE_ID);
  
  Serial.print("Node ID: "); Serial.println(MY_NODE_ID);
  // In ra địa chỉ của mình để so khớp với log Master nếu cần
  uint32_t addrLow = (uint32_t)myAddress;
  Serial.print("My Pipe Address (Low 32bit): "); Serial.println(addrLow, HEX);
  Serial.print("Struct Size: "); Serial.println((unsigned int)sizeof(RegisterPacket));

  if (EEPROM.read(EEPROM_ADDR_FLAG) == 1) {
    isRegistered = true;
    Serial.println("RECOVERED: Already REGISTERED.");
    for(int i=0; i<2; i++) { digitalWrite(PIN_LED, HIGH); delay(200); digitalWrite(PIN_LED, LOW); delay(200); }
  } else {
    Serial.println("System Ready. Waiting to register...");
  }
}

void loop() {
  handleButton(); 

  if (!isRegistered) {
    if (millis() - lastBlink > 500) {
      lastBlink = millis();
      digitalWrite(PIN_LED, !digitalRead(PIN_LED));
    }
    registerToMaster();
  } else {
    listenAndReply();
  }
}

void handleButton() {
  if (digitalRead(PIN_BTN) == LOW) {
    delay(50);
    if (digitalRead(PIN_BTN) == LOW) {
      Serial.println("Manual RESET Registration...");
      isRegistered = false;
      EEPROM.write(EEPROM_ADDR_FLAG, 0);
      digitalWrite(PIN_LED, LOW);
      while(digitalRead(PIN_BTN) == LOW);
      delay(1000);
    }
  }
}

void readSensors(SoilData &data) {
  data.moisture = 0.0;
  data.temperature = 0.0;

  int lm35Raw = analogRead(PIN_TEMP);
  float voltage = lm35Raw * (5.0 / 1023.0);
  data.temperature = voltage * 100.0 + TEMP_OFFSET;

  int soilRaw = analogRead(PIN_SOIL);
  int percent = map(soilRaw, 1023, 0, 0, 100);
  data.moisture = constrain(percent, 0, 100);
}

void registerToMaster() {
  radio.stopListening();
  radio.openWritingPipe(REGISTER_PIPE);
  
  RegisterPacket pkt;
  memset(&pkt, 0, sizeof(pkt)); 
  strcpy(pkt.cmd, "REG");
  strncpy(pkt.id, MY_NODE_ID, 10);
  
  Serial.print("Sending REG... ");
  if (radio.write(&pkt, sizeof(pkt))) {
    Serial.println("Sent OK. Waiting for ACK...");
    radio.openReadingPipe(1, myAddress);
    radio.startListening();
    
    unsigned long startWait = millis();
    bool received = false;
    while (millis() - startWait < 500) {
      if (radio.available()) {
        char ack[10] = {0};
        uint8_t len = radio.getDynamicPayloadSize();
        radio.read(&ack, len);
        Serial.print("Received: "); Serial.println(ack);
        
        if (strcmp(ack, "REG_OK") == 0) {
          isRegistered = true;
          EEPROM.write(EEPROM_ADDR_FLAG, 1);
          digitalWrite(PIN_LED, LOW);
          Serial.println("REGISTER SUCCESS!");
          for(int i=0; i<3; i++) { digitalWrite(PIN_LED, HIGH); delay(100); digitalWrite(PIN_LED, LOW); delay(100); }
          return;
        }
      }
    }
    if(!received) Serial.println("Timeout waiting for ACK.");
  } else {
    Serial.println("Send FAILED (No AutoAck received from Master?)");
  }
  delay(2000);
}

void listenAndReply() {
  radio.openReadingPipe(1, myAddress);
  radio.startListening();
  
  if (radio.available()) {
    char req[5] = {0};
    radio.read(&req, sizeof(req)); 
    
    if (strncmp(req, "GET", 3) == 0) {
      digitalWrite(PIN_LED, HIGH);
      Serial.println("CMD: GET received.");
      
      SoilData data;
      readSensors(data);
      
      radio.stopListening();
      radio.openWritingPipe(myAddress);
      
      if (radio.write(&data, sizeof(data))) {
          Serial.println("Data Sent. Waiting for OK...");
          radio.openReadingPipe(1, myAddress);
          radio.startListening();
          unsigned long waitAck = millis();
          while(millis() - waitAck < 150) { 
             if(radio.available()) {
                 char ack[5] = {0};
                 radio.read(&ack, sizeof(ack));
                 if(strncmp(ack, "OK", 2) == 0) {
                    Serial.println("Transaction COMPLETE.");
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