#pragma once

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for mouse pixel move keycodes."
#endif

enum mouse_pixel_move_keycodes {
  MOUSE_PIXEL_MOVE_START = CUSTOM_SAFE_RANGE,

  MS_DN_1,
  MS_UP_1,
  MS_LF_1,
  MS_RG_1,

  MS_DN10,
  MS_UP10,
  MS_LF10,
  MS_RG10,

  MOUSE_PIXEL_MOVE_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE MOUSE_PIXEL_MOVE_NEW_SAFE_RANGE
};

enum mouse_pixel_move_config {
  MOUSE_PIXEL_MOVE_DIRECTION_COUNT = 4,
  MOUSE_PIXEL_MOVE_SMALL_STEP = 1,
  MOUSE_PIXEL_MOVE_LARGE_STEP = 10,
};

enum mouse_pixel_move_direction {
  MOUSE_PIXEL_MOVE_DOWN,
  MOUSE_PIXEL_MOVE_UP,
  MOUSE_PIXEL_MOVE_LEFT,
  MOUSE_PIXEL_MOVE_RIGHT,
};

static bool mouse_pixel_move_is_key(uint16_t keycode) {
  return MS_DN_1 <= keycode && keycode <= MS_RG10;
}

static report_mouse_t mouse_pixel_move_report(uint16_t keycode) {
  uint8_t offset = keycode - MS_DN_1;
  uint8_t direction = offset % MOUSE_PIXEL_MOVE_DIRECTION_COUNT;
  int8_t step = offset < MOUSE_PIXEL_MOVE_DIRECTION_COUNT
    ? MOUSE_PIXEL_MOVE_SMALL_STEP
    : MOUSE_PIXEL_MOVE_LARGE_STEP;

  report_mouse_t report = {};
  switch (direction) {
    case MOUSE_PIXEL_MOVE_DOWN: report.y = step; break;
    case MOUSE_PIXEL_MOVE_UP: report.y = -step; break;
    case MOUSE_PIXEL_MOVE_LEFT: report.x = -step; break;
    case MOUSE_PIXEL_MOVE_RIGHT: report.x = step; break;
  }
  return report;
}

bool process_mouse_pixel_move(uint16_t keycode, keyrecord_t *record) {
  if (!mouse_pixel_move_is_key(keycode)) {
    return true;
  }

  if (!record->event.pressed) {
    return false;
  }

  report_mouse_t report = mouse_pixel_move_report(keycode);
  host_mouse_send(&report);
  return false;
}
