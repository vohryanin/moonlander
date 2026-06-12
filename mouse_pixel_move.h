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
  MOUSE_PIXEL_MOVE_SMALL_STEP = 1,
  MOUSE_PIXEL_MOVE_LARGE_REPEAT = 10,
};

static void mouse_pixel_move_send(int8_t x, int8_t y) {
  report_mouse_t report = {};
  report.x = x;
  report.y = y;
  host_mouse_send(&report);
}

static void mouse_pixel_move_send_repeated(int8_t x, int8_t y, uint8_t repeat) {
  for (uint8_t i = 0; i < repeat; i++) {
    mouse_pixel_move_send(x, y);
  }
}

static bool mouse_pixel_move_process(keyrecord_t *record, int8_t x, int8_t y, uint8_t repeat) {
  if (record->event.pressed) {
    mouse_pixel_move_send_repeated(x, y, repeat);
  }
  return false;
}

bool process_mouse_pixel_move(uint16_t keycode, keyrecord_t *record) {
  switch (keycode) {
    case MS_DN_1: return mouse_pixel_move_process(record, 0, MOUSE_PIXEL_MOVE_SMALL_STEP, 1);
    case MS_UP_1: return mouse_pixel_move_process(record, 0, -MOUSE_PIXEL_MOVE_SMALL_STEP, 1);
    case MS_LF_1: return mouse_pixel_move_process(record, -MOUSE_PIXEL_MOVE_SMALL_STEP, 0, 1);
    case MS_RG_1: return mouse_pixel_move_process(record, MOUSE_PIXEL_MOVE_SMALL_STEP, 0, 1);

    case MS_DN10: return mouse_pixel_move_process(record, 0, MOUSE_PIXEL_MOVE_SMALL_STEP, MOUSE_PIXEL_MOVE_LARGE_REPEAT);
    case MS_UP10: return mouse_pixel_move_process(record, 0, -MOUSE_PIXEL_MOVE_SMALL_STEP, MOUSE_PIXEL_MOVE_LARGE_REPEAT);
    case MS_LF10: return mouse_pixel_move_process(record, -MOUSE_PIXEL_MOVE_SMALL_STEP, 0, MOUSE_PIXEL_MOVE_LARGE_REPEAT);
    case MS_RG10: return mouse_pixel_move_process(record, MOUSE_PIXEL_MOVE_SMALL_STEP, 0, MOUSE_PIXEL_MOVE_LARGE_REPEAT);
  }

  return true;
}
