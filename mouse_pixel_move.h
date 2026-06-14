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

#ifndef MOUSE_PIXEL_MOVE_HOLD_DELAY
#define MOUSE_PIXEL_MOVE_HOLD_DELAY 180
#endif

#ifndef MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_START
#define MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_START 45
#endif

#ifndef MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_MIN
#define MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_MIN 12
#endif

#ifndef MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_ACCEL
#define MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_ACCEL 2
#endif

#ifndef MOUSE_PIXEL_MOVE_REPEAT_STEP_MAX
#define MOUSE_PIXEL_MOVE_REPEAT_STEP_MAX 40
#endif

#ifndef MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL_EVERY
#define MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL_EVERY 8
#endif

#ifndef MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL
#define MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL 5
#endif

enum mouse_pixel_move_direction {
  MOUSE_PIXEL_MOVE_DOWN,
  MOUSE_PIXEL_MOVE_UP,
  MOUSE_PIXEL_MOVE_LEFT,
  MOUSE_PIXEL_MOVE_RIGHT,
};

static uint8_t mouse_pixel_move_held_directions = 0;
static uint32_t mouse_pixel_move_hold_timer = 0;
static uint32_t mouse_pixel_move_repeat_timer = 0;
static uint8_t mouse_pixel_move_repeat_count = 0;

static bool mouse_pixel_move_is_key(uint16_t keycode) {
  return MS_DN_1 <= keycode && keycode <= MS_RG10;
}

static bool mouse_pixel_move_is_large_key(uint16_t keycode) {
  return MS_DN10 <= keycode && keycode <= MS_RG10;
}

static bool mouse_pixel_move_is_vertical(uint8_t direction) {
  return direction == MOUSE_PIXEL_MOVE_DOWN || direction == MOUSE_PIXEL_MOVE_UP;
}

static bool mouse_pixel_move_is_negative(uint8_t direction) {
  return direction == MOUSE_PIXEL_MOVE_UP || direction == MOUSE_PIXEL_MOVE_LEFT;
}

static uint8_t mouse_pixel_move_direction(uint16_t keycode) {
  return (keycode - MS_DN_1) % MOUSE_PIXEL_MOVE_DIRECTION_COUNT;
}

static int8_t mouse_pixel_move_clamp_delta(int16_t value) {
  if (value > 127) {
    return 127;
  }
  if (value < -127) {
    return -127;
  }
  return (int8_t)value;
}

static report_mouse_t mouse_pixel_move_direction_report(uint8_t direction, int8_t step) {
  int8_t delta = mouse_pixel_move_is_negative(direction) ? -step : step;

  report_mouse_t report = {};
  if (mouse_pixel_move_is_vertical(direction)) {
    report.y = delta;
  } else {
    report.x = delta;
  }
  return report;
}

static report_mouse_t mouse_pixel_move_key_report(uint16_t keycode) {
  uint8_t offset = keycode - MS_DN_1;
  int8_t step = offset < MOUSE_PIXEL_MOVE_DIRECTION_COUNT
    ? MOUSE_PIXEL_MOVE_SMALL_STEP
    : MOUSE_PIXEL_MOVE_LARGE_STEP;

  return mouse_pixel_move_direction_report(mouse_pixel_move_direction(keycode), step);
}

static report_mouse_t mouse_pixel_move_held_report(int8_t step) {
  int16_t x = 0;
  int16_t y = 0;

  for (uint8_t direction = 0; direction < MOUSE_PIXEL_MOVE_DIRECTION_COUNT; direction++) {
    if ((mouse_pixel_move_held_directions & (1 << direction)) == 0) {
      continue;
    }

    report_mouse_t report = mouse_pixel_move_direction_report(direction, step);
    x += report.x;
    y += report.y;
  }

  report_mouse_t report = {};
  report.x = mouse_pixel_move_clamp_delta(x);
  report.y = mouse_pixel_move_clamp_delta(y);
  return report;
}

static void mouse_pixel_move_send(int8_t x, int8_t y) {
  report_mouse_t report = {};
  report.x = x;
  report.y = y;
  host_mouse_send(&report);
}

static uint16_t mouse_pixel_move_repeat_interval(void) {
  if (MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_START <= MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_MIN) {
    return MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_MIN;
  }

  uint16_t acceleration = (uint16_t)mouse_pixel_move_repeat_count * MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_ACCEL;
  uint16_t available = MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_START - MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_MIN;
  if (acceleration > available) {
    return MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_MIN;
  }
  return MOUSE_PIXEL_MOVE_REPEAT_INTERVAL_START - acceleration;
}

static int8_t mouse_pixel_move_repeat_step(void) {
  uint16_t accel_every = MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL_EVERY == 0
    ? 1
    : MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL_EVERY;
  uint16_t step = MOUSE_PIXEL_MOVE_LARGE_STEP +
    ((uint16_t)mouse_pixel_move_repeat_count / accel_every) *
    MOUSE_PIXEL_MOVE_REPEAT_STEP_ACCEL;

  if (step > MOUSE_PIXEL_MOVE_REPEAT_STEP_MAX) {
    step = MOUSE_PIXEL_MOVE_REPEAT_STEP_MAX;
  }
  return (int8_t)step;
}

static void mouse_pixel_move_press_large(uint16_t keycode) {
  if (mouse_pixel_move_held_directions == 0) {
    mouse_pixel_move_hold_timer = timer_read32();
    mouse_pixel_move_repeat_timer = mouse_pixel_move_hold_timer;
    mouse_pixel_move_repeat_count = 0;
  }
  mouse_pixel_move_held_directions |= 1 << mouse_pixel_move_direction(keycode);
}

static void mouse_pixel_move_release_large(uint16_t keycode) {
  mouse_pixel_move_held_directions &= (uint8_t)~(1 << mouse_pixel_move_direction(keycode));
  if (mouse_pixel_move_held_directions == 0) {
    mouse_pixel_move_repeat_count = 0;
  }
}

bool process_mouse_pixel_move(uint16_t keycode, keyrecord_t *record) {
  if (!mouse_pixel_move_is_key(keycode)) {
    return true;
  }

  if (record->event.pressed) {
    report_mouse_t report = mouse_pixel_move_key_report(keycode);
    mouse_pixel_move_send(report.x, report.y);
    if (mouse_pixel_move_is_large_key(keycode)) {
      mouse_pixel_move_press_large(keycode);
    }
  } else if (mouse_pixel_move_is_large_key(keycode)) {
    mouse_pixel_move_release_large(keycode);
  }
  return false;
}

void mouse_pixel_move_user_timer(void) {
  if (mouse_pixel_move_held_directions == 0) {
    return;
  }

  if (timer_elapsed32(mouse_pixel_move_hold_timer) < MOUSE_PIXEL_MOVE_HOLD_DELAY) {
    return;
  }

  if (timer_elapsed32(mouse_pixel_move_repeat_timer) < mouse_pixel_move_repeat_interval()) {
    return;
  }

  mouse_pixel_move_repeat_timer = timer_read32();
  if (mouse_pixel_move_repeat_count < UINT8_MAX) {
    mouse_pixel_move_repeat_count++;
  }

  report_mouse_t report = mouse_pixel_move_held_report(mouse_pixel_move_repeat_step());
  mouse_pixel_move_send(report.x, report.y);
}
