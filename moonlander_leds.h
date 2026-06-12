#pragma once

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for moonlander leds keycodes."
#endif

enum moonlander_leds_keycodes {
  MOONLANDER_LEDS_START = CUSTOM_SAFE_RANGE,

  LED_1,
  LED_2,
  LED_3,
  LED_4,
  LED_5,
  LED_6,

  MOONLANDER_LEDS_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE MOONLANDER_LEDS_NEW_SAFE_RANGE
};

enum moonlander_leds_config {
  MOONLANDER_LED_COUNT = LED_6 - LED_1 + 1,
};

static void moonlander_led_set(uint8_t led_index, bool enabled) {
  switch (led_index) {
    case 0: ML_LED_1(enabled); break;
    case 1: ML_LED_2(enabled); break;
    case 2: ML_LED_3(enabled); break;
    case 3: ML_LED_4(enabled); break;
    case 4: ML_LED_5(enabled); break;
    case 5: ML_LED_6(enabled); break;
  }
}

void moonlander_leds_set_all(bool enabled) {
  for (uint8_t led_index = 0; led_index < MOONLANDER_LED_COUNT; led_index++) {
    moonlander_led_set(led_index, enabled);
  }
}

bool process_moonlander_leds(uint16_t keycode, keyrecord_t *record) {
  if (keycode < LED_1 || keycode > LED_6) {
    return true;
  }

  moonlander_led_set((uint8_t)(keycode - LED_1), record->event.pressed);
  return false;
}
