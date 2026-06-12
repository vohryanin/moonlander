#pragma once

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for custom lang keycodes."
#endif

enum custom_lang_keycodes {
  CUSTOM_LANG_START = CUSTOM_SAFE_RANGE,

  EN_LTEQ, // <=
  EN_GTEQ, // >=
  EN_ARR1, // ->
  EN_ARR2, // =>
  EN_FISH, // ::<>()◀️◀️◀️
  EN_MACR, // #[]◀️
  EN_CLTG, // </

  CUSTOM_LANG_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE CUSTOM_LANG_NEW_SAFE_RANGE
};

static void tap_lang_pair(uint16_t first_keycode, uint16_t second_keycode) {
  lang_shift_tap_key(first_keycode);
  lang_shift_tap_key(second_keycode);
}

static void tap_key_times(uint16_t keycode, uint8_t count) {
  for (uint8_t i = 0; i < count; i++) {
    tap_code(keycode);
  }
}

// Мои языко-символьные клавиши
bool process_my_lang_keys(uint16_t keycode, keyrecord_t *record) {
  // English-specific codes
  switch (keycode) {
    case EN_LTEQ:
      if (record->event.pressed) {
        tap_lang_pair(EN_LT, AG_EQL);
      }
      return false;
    case EN_GTEQ:
      if (record->event.pressed) {
        tap_lang_pair(EN_GT, AG_EQL);
      }
      return false;
    case EN_ARR1:
      if (record->event.pressed) {
        tap_lang_pair(AG_MINS, EN_GT);
      }
      return false;
    case EN_ARR2:
      if (record->event.pressed) {
        tap_lang_pair(AG_EQL, EN_GT);
      }
      return false;
    case EN_FISH:
      if (record->event.pressed) {
        tap_lang_pair(AG_COLN, AG_COLN);
        tap_lang_pair(EN_LT, EN_GT);
        tap_lang_pair(EN_LPRN, EN_RPRN);
        tap_key_times(KC_LEFT, 3);
      }
      return false;
    case EN_MACR:
      if (record->event.pressed) {
        lang_shift_tap_key(EN_HASH);
        tap_lang_pair(EN_LBRC, EN_RBRC);
        tap_key_times(KC_LEFT, 1);
      }
      return false;
    case EN_CLTG:
      if (record->event.pressed) {
        tap_lang_pair(EN_LT, AG_SLSH);
      }
      return false;
  }
  return true;
}
