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

static bool mouse_pixel_move_is_vertical(uint8_t direction) {
  return direction == MOUSE_PIXEL_MOVE_DOWN || direction == MOUSE_PIXEL_MOVE_UP;
}

static bool mouse_pixel_move_is_negative(uint8_t direction) {
  return direction == MOUSE_PIXEL_MOVE_UP || direction == MOUSE_PIXEL_MOVE_LEFT;
}

static report_mouse_t mouse_pixel_move_report(uint16_t keycode) {
  uint8_t offset = keycode - MS_DN_1;
  uint8_t direction = offset % MOUSE_PIXEL_MOVE_DIRECTION_COUNT;
  int8_t step = offset < MOUSE_PIXEL_MOVE_DIRECTION_COUNT
    ? MOUSE_PIXEL_MOVE_SMALL_STEP
    : MOUSE_PIXEL_MOVE_LARGE_STEP;
  int8_t delta = mouse_pixel_move_is_negative(direction) ? -step : step;

  report_mouse_t report = {};
  if (mouse_pixel_move_is_vertical(direction)) {
    report.y = delta;
  } else {
    report.x = delta;
  }
  return report;
}

static void mouse_pixel_move_send(int8_t x, int8_t y) {
  report_mouse_t report = {};
  report.x = x;
  report.y = y;
  host_mouse_send(&report);
}

bool process_mouse_pixel_move(uint16_t keycode, keyrecord_t *record) {
  if (!mouse_pixel_move_is_key(keycode)) {
    return true;
  }

  if (record->event.pressed) {
    report_mouse_t report = mouse_pixel_move_report(keycode);
    mouse_pixel_move_send(report.x, report.y);
  }
  return false;
}
