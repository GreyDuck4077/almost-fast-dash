// Adalight receiver for an addressable LED strip (WS2812B / NeoPixel), for
// Almost Fast Dash' "Other LED devices". Needs the FastLED library
// (Arduino IDE: Tools > Manage Libraries > FastLED).
//
// Set the three values below, upload, then click Scan USB on the Devices page.
// Protocol: "Ada", LED count - 1 (high byte, low byte), checksum (high ^ low ^ 0x55),
// then red, green, blue for every LED.
//
// Part of Almost Fast Dash, GPL-3.0-or-later.

#include <FastLED.h>

#define LED_PIN   6        // data pin the strip is wired to
#define NUM_LEDS  30       // LEDs on the strip
#define BAUD      115200   // must match the app (115200)

CRGB leds[NUM_LEDS];
unsigned long lastGreeting = 0;

// Waits for the next byte. While nothing arrives it says "Ada" once a second,
// so the app can find boards that don't restart when their port opens.
uint8_t readByte() {
  while (!Serial.available()) {
    if (millis() - lastGreeting > 1000) {
      Serial.print("Ada\n");
      lastGreeting = millis();
    }
  }
  return Serial.read();
}

void setup() {
  FastLED.addLeds<WS2812B, LED_PIN, GRB>(leds, NUM_LEDS);
  FastLED.clear(true);
  Serial.begin(BAUD);
  Serial.print("Ada\n");
  lastGreeting = millis();
}

void loop() {
  // Find the start of a frame.
  if (readByte() != 'A') return;
  if (readByte() != 'd') return;
  if (readByte() != 'a') return;
  uint8_t high = readByte(), low = readByte(), check = readByte();
  if (check != (high ^ low ^ 0x55)) return;

  uint16_t count = ((uint16_t)high << 8 | low) + 1;
  for (uint16_t i = 0; i < count; i++) {
    uint8_t r = readByte(), g = readByte(), b = readByte();
    if (i < NUM_LEDS) leds[i] = CRGB(r, g, b);
  }
  FastLED.show();
}
