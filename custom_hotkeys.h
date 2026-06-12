#pragma once

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for custom hotkeys keycodes."
#endif

enum custom_hotkeys_keycodes {
  CUSTOM_HOTKEYS_START = CUSTOM_SAFE_RANGE,

  KG_NEXT,
  F6_CT_C,
  MY_SCRN,
  CT_A_C,
  CT_A_V,
  CT_A_X,
  CT_D,
  CT_SLSH,
  CT_Y,
  CT_Z,
  KC_LF5,
  KC_RG5,
  CT_RBRC,

  CUSTOM_HOTKEYS_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE CUSTOM_HOTKEYS_NEW_SAFE_RANGE
};

// Helpers for modifier hotkeys.
static void tap_with_mod(uint8_t mod, uint8_t key) {
  register_code(mod);
  tap_code(key);
  unregister_code(mod);
}

static void tap_with_mods(uint8_t mod1, uint8_t mod2, uint8_t key) {
  register_code(mod1);
  register_code(mod2);
  tap_code(key);
  unregister_code(mod2);
  unregister_code(mod1);
}

static void tap_ctrl(uint8_t key) {
  tap_with_mod(KC_LCTRL, key);
}

static void tap_ctrl_shift(uint8_t key) {
  tap_with_mods(KC_LCTRL, KC_LSHIFT, key);
}

static void tap_gui_shift(uint8_t key) {
  tap_with_mods(KC_LGUI, KC_LSHIFT, key);
}

static void tap_ctrl_sequence(uint8_t first_key, uint8_t second_key) {
  register_code(KC_LCTRL);
  tap_code(first_key);
  tap_code(second_key);
  unregister_code(KC_LCTRL);
}

static void hold_ctrl(uint8_t key, bool down) {
  if (down) {
    register_code(KC_LCTRL);
    register_code(key);
  } else {
    unregister_code(key);
    unregister_code(KC_LCTRL);
  }
}

// Custom hotkey processing.
bool process_my_hotkeys(uint16_t keycode, keyrecord_t *record) {
  switch (keycode) {
    case KG_NEXT:
      if (record->event.pressed) {
        tap_code(KC_TAB);
        tap_code(KC_TAB);
        tap_ctrl(KC_RGHT);
      }    
      return false;
      break;
    case F6_CT_C:
      if (record->event.pressed) {
        tap_code(KC_F6);
        tap_ctrl(KC_C);
      }
      return false;
      break;
    case MY_SCRN:
      if (record->event.pressed) {
        switch (lang_current_change) {
          case LANG_CHANGE_CAPS: {
            tap_ctrl_shift(KC_PSCR);
          } break;
          case LANG_CHANGE_ALT_SHIFT:
          case LANG_CHANGE_CTRL_SHIFT: {
            tap_gui_shift(KC_S);
          } break;
          case LANG_CHANGE_WIN_SPACE: {
            // No screenshot, maybe it android
          } break;
        } 
      }
      return false;
      break;
    case CT_A_C:
      if (record->event.pressed) {
        shift_activate(0);
        tap_ctrl_sequence(KC_A, KC_C);
      }
      return false;
    case CT_A_V:
      if (record->event.pressed) {
        shift_activate(0);
        tap_ctrl_sequence(KC_A, KC_V);
      }
      return false;
    case CT_A_X:
      if (record->event.pressed) {
        shift_activate(0);
        tap_ctrl_sequence(KC_A, KC_X);
      }
      return false;
    case CT_D:
      if (record->event.pressed) {
        lang_activate(0);
        hold_ctrl(KC_D, true);
      } else {
        hold_ctrl(KC_D, false);
      }
      return false;
    case CT_Y:
      if (record->event.pressed) {
        shift_activate(0);
        hold_ctrl(KC_Y, true);
      } else {
        hold_ctrl(KC_Y, false);
      }
      return false;
    case CT_Z:
      if (record->event.pressed) {
        shift_activate(0);
        hold_ctrl(KC_Z, true);
      } else {
        hold_ctrl(KC_Z, false);
      }
      return false;
    case CT_SLSH:
      if (record->event.pressed) {
        lang_activate(0);
        hold_ctrl(KC_SLSH, true);
      } else {
        hold_ctrl(KC_SLSH, false);
      }
      return false;
    case CT_RBRC:
      if (record->event.pressed) {
        lang_activate(0);
        hold_ctrl(KC_RBRC, true);
      } else {
        hold_ctrl(KC_RBRC, false);
      }
      return false;
  }

  return true;
}
