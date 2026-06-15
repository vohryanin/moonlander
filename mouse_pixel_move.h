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

  MS_DN_FAST,
  MS_UP_FAST,
  MS_LF_FAST,
  MS_RG_FAST,

  MS_PREC,
  MS_BOOST,

  MOUSE_PIXEL_MOVE_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE MOUSE_PIXEL_MOVE_NEW_SAFE_RANGE
};

enum mouse_pixel_move_config {
  MOUSE_PIXEL_MOVE_DIRECTION_COUNT = 4,
  MOUSE_PIXEL_MOVE_SMALL_STEP = 1,
  MOUSE_PIXEL_MOVE_LARGE_STEP = 10,
  MOUSE_PIXEL_MOVE_SCALE = 16,
  MOUSE_PIXEL_MOVE_DIAGONAL_SCALE = 181,
  MOUSE_PIXEL_MOVE_DIAGONAL_DIVISOR = 256,
};

#ifndef MOUSE_PIXEL_MOVE_HOLD_DELAY
#define MOUSE_PIXEL_MOVE_HOLD_DELAY 180
#endif

#ifndef MOUSE_PIXEL_MOVE_TICK_INTERVAL
#define MOUSE_PIXEL_MOVE_TICK_INTERVAL 8
#endif

#ifndef MOUSE_PIXEL_MOVE_ACCEL
#define MOUSE_PIXEL_MOVE_ACCEL 6
#endif

#ifndef MOUSE_PIXEL_MOVE_MAX_SPEED
#define MOUSE_PIXEL_MOVE_MAX_SPEED 24
#endif

#ifndef MOUSE_PIXEL_MOVE_PRECISION_ACCEL
#define MOUSE_PIXEL_MOVE_PRECISION_ACCEL 2
#endif

#ifndef MOUSE_PIXEL_MOVE_PRECISION_MAX_SPEED
#define MOUSE_PIXEL_MOVE_PRECISION_MAX_SPEED 5
#endif

#ifndef MOUSE_PIXEL_MOVE_BOOST_ACCEL
#define MOUSE_PIXEL_MOVE_BOOST_ACCEL 12
#endif

#ifndef MOUSE_PIXEL_MOVE_BOOST_MAX_SPEED
#define MOUSE_PIXEL_MOVE_BOOST_MAX_SPEED 40
#endif

#ifndef MOUSE_PIXEL_MOVE_FAST_STEP_START
#define MOUSE_PIXEL_MOVE_FAST_STEP_START 1
#endif

#ifndef MOUSE_PIXEL_MOVE_FRICTION
#define MOUSE_PIXEL_MOVE_FRICTION 12
#endif

enum mouse_pixel_move_direction {
  MOUSE_PIXEL_MOVE_DOWN,
  MOUSE_PIXEL_MOVE_UP,
  MOUSE_PIXEL_MOVE_LEFT,
  MOUSE_PIXEL_MOVE_RIGHT,
};

static uint8_t mouse_pixel_move_fast_directions = 0;
static uint8_t mouse_pixel_move_large_directions = 0;
static uint32_t mouse_pixel_move_large_hold_timer = 0;
static uint32_t mouse_pixel_move_tick_timer = 0;
static bool mouse_pixel_move_precision = false;
static bool mouse_pixel_move_boost = false;
static int16_t mouse_pixel_move_velocity_x = 0;
static int16_t mouse_pixel_move_velocity_y = 0;
static int16_t mouse_pixel_move_remainder_x = 0;
static int16_t mouse_pixel_move_remainder_y = 0;

static bool mouse_pixel_move_is_move_key(uint16_t keycode) {
  return MS_DN_1 <= keycode && keycode <= MS_RG_FAST;
}

static bool mouse_pixel_move_is_key(uint16_t keycode) {
  return mouse_pixel_move_is_move_key(keycode) || keycode == MS_PREC || keycode == MS_BOOST;
}

static bool mouse_pixel_move_is_small_key(uint16_t keycode) {
  return MS_DN_1 <= keycode && keycode <= MS_RG_1;
}

static bool mouse_pixel_move_is_large_key(uint16_t keycode) {
  return MS_DN10 <= keycode && keycode <= MS_RG10;
}

static bool mouse_pixel_move_is_fast_key(uint16_t keycode) {
  return MS_DN_FAST <= keycode && keycode <= MS_RG_FAST;
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

static int8_t mouse_pixel_move_clamp_report_delta(int16_t value) {
  if (value > 127) {
    return 127;
  }
  if (value < -127) {
    return -127;
  }
  return (int8_t)value;
}

static int16_t mouse_pixel_move_scaled_speed(uint16_t pixels_per_tick) {
  return (int16_t)(pixels_per_tick * MOUSE_PIXEL_MOVE_SCALE);
}

static int16_t mouse_pixel_move_apply_diagonal_scale(int16_t value) {
  return (int16_t)(((int32_t)value * MOUSE_PIXEL_MOVE_DIAGONAL_SCALE) / MOUSE_PIXEL_MOVE_DIAGONAL_DIVISOR);
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

static void mouse_pixel_move_send(int8_t x, int8_t y) {
  if (x == 0 && y == 0) {
    return;
  }

  report_mouse_t report = {};
  report.x = x;
  report.y = y;
  host_mouse_send(&report);
}

static void mouse_pixel_move_tap_direction(uint8_t direction, int8_t step) {
  report_mouse_t report = mouse_pixel_move_direction_report(direction, step);
  mouse_pixel_move_send(report.x, report.y);
}

static int8_t mouse_pixel_move_axis(uint8_t negative_direction, uint8_t positive_direction, uint8_t directions) {
  bool negative = (directions & (1 << negative_direction)) != 0;
  bool positive = (directions & (1 << positive_direction)) != 0;
  if (negative == positive) {
    return 0;
  }
  return negative ? -1 : 1;
}

static int16_t mouse_pixel_move_approach(int16_t value, int16_t target, int16_t delta) {
  if (value < target) {
    value += delta;
    return value > target ? target : value;
  }
  if (value > target) {
    value -= delta;
    return value < target ? target : value;
  }
  return value;
}

static bool mouse_pixel_move_large_active(void) {
  return mouse_pixel_move_large_directions != 0 &&
    timer_elapsed32(mouse_pixel_move_large_hold_timer) >= MOUSE_PIXEL_MOVE_HOLD_DELAY;
}

static uint8_t mouse_pixel_move_active_directions(void) {
  uint8_t directions = mouse_pixel_move_fast_directions;
  if (mouse_pixel_move_large_active()) {
    directions |= mouse_pixel_move_large_directions;
  }
  return directions;
}

static int16_t mouse_pixel_move_current_accel(uint8_t active_directions) {
  if (mouse_pixel_move_precision) {
    return mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_PRECISION_ACCEL);
  }
  if (mouse_pixel_move_boost || (mouse_pixel_move_large_active() && active_directions != 0)) {
    return mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_BOOST_ACCEL);
  }
  return mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_ACCEL);
}

static int16_t mouse_pixel_move_current_max_speed(uint8_t active_directions) {
  if (mouse_pixel_move_precision) {
    return mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_PRECISION_MAX_SPEED);
  }
  if (mouse_pixel_move_boost || (mouse_pixel_move_large_active() && active_directions != 0)) {
    return mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_BOOST_MAX_SPEED);
  }
  return mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_MAX_SPEED);
}

static bool mouse_pixel_move_has_velocity(void) {
  return mouse_pixel_move_velocity_x != 0 ||
    mouse_pixel_move_velocity_y != 0 ||
    mouse_pixel_move_remainder_x != 0 ||
    mouse_pixel_move_remainder_y != 0;
}

static void mouse_pixel_move_update_velocity(uint8_t active_directions) {
  int8_t x_axis = mouse_pixel_move_axis(MOUSE_PIXEL_MOVE_LEFT, MOUSE_PIXEL_MOVE_RIGHT, active_directions);
  int8_t y_axis = mouse_pixel_move_axis(MOUSE_PIXEL_MOVE_UP, MOUSE_PIXEL_MOVE_DOWN, active_directions);
  bool diagonal = x_axis != 0 && y_axis != 0;

  int16_t accel = mouse_pixel_move_current_accel(active_directions);
  int16_t max_speed = mouse_pixel_move_current_max_speed(active_directions);
  if (diagonal) {
    accel = mouse_pixel_move_apply_diagonal_scale(accel);
    max_speed = mouse_pixel_move_apply_diagonal_scale(max_speed);
  }

  int16_t target_x = x_axis * max_speed;
  int16_t target_y = y_axis * max_speed;
  int16_t release_delta = mouse_pixel_move_scaled_speed(MOUSE_PIXEL_MOVE_FRICTION);
  int16_t delta_x = x_axis == 0 && active_directions == 0 ? release_delta : accel;
  int16_t delta_y = y_axis == 0 && active_directions == 0 ? release_delta : accel;

  mouse_pixel_move_velocity_x = mouse_pixel_move_approach(mouse_pixel_move_velocity_x, target_x, delta_x);
  mouse_pixel_move_velocity_y = mouse_pixel_move_approach(mouse_pixel_move_velocity_y, target_y, delta_y);

  if (mouse_pixel_move_velocity_x == 0 && active_directions == 0) {
    mouse_pixel_move_remainder_x = 0;
  }
  if (mouse_pixel_move_velocity_y == 0 && active_directions == 0) {
    mouse_pixel_move_remainder_y = 0;
  }
}

static void mouse_pixel_move_send_velocity(void) {
  mouse_pixel_move_remainder_x += mouse_pixel_move_velocity_x;
  mouse_pixel_move_remainder_y += mouse_pixel_move_velocity_y;

  int16_t x = mouse_pixel_move_remainder_x / MOUSE_PIXEL_MOVE_SCALE;
  int16_t y = mouse_pixel_move_remainder_y / MOUSE_PIXEL_MOVE_SCALE;

  mouse_pixel_move_remainder_x %= MOUSE_PIXEL_MOVE_SCALE;
  mouse_pixel_move_remainder_y %= MOUSE_PIXEL_MOVE_SCALE;

  mouse_pixel_move_send(
    mouse_pixel_move_clamp_report_delta(x),
    mouse_pixel_move_clamp_report_delta(y)
  );
}

static void mouse_pixel_move_press_large(uint16_t keycode) {
  if (mouse_pixel_move_large_directions == 0) {
    mouse_pixel_move_large_hold_timer = timer_read32();
  }
  mouse_pixel_move_large_directions |= 1 << mouse_pixel_move_direction(keycode);
}

static void mouse_pixel_move_release_large(uint16_t keycode) {
  mouse_pixel_move_large_directions &= (uint8_t)~(1 << mouse_pixel_move_direction(keycode));
}

static void mouse_pixel_move_press_fast(uint16_t keycode) {
  mouse_pixel_move_fast_directions |= 1 << mouse_pixel_move_direction(keycode);
  mouse_pixel_move_tick_timer = timer_read32();
}

static void mouse_pixel_move_release_fast(uint16_t keycode) {
  mouse_pixel_move_fast_directions &= (uint8_t)~(1 << mouse_pixel_move_direction(keycode));
}

void mouse_pixel_move_reset(void) {
  mouse_pixel_move_fast_directions = 0;
  mouse_pixel_move_large_directions = 0;
  mouse_pixel_move_precision = false;
  mouse_pixel_move_boost = false;
  mouse_pixel_move_velocity_x = 0;
  mouse_pixel_move_velocity_y = 0;
  mouse_pixel_move_remainder_x = 0;
  mouse_pixel_move_remainder_y = 0;
}

uint8_t mouse_pixel_move_telemetry_directions(void) {
  return mouse_pixel_move_active_directions();
}

uint8_t mouse_pixel_move_telemetry_flags(void) {
  uint8_t flags = 0;
  if (mouse_pixel_move_precision) {
    flags |= 1;
  }
  if (mouse_pixel_move_boost) {
    flags |= 2;
  }
  if (mouse_pixel_move_large_active()) {
    flags |= 4;
  }
  if (mouse_pixel_move_has_velocity()) {
    flags |= 8;
  }
  return flags;
}

int16_t mouse_pixel_move_telemetry_velocity_x(void) {
  return mouse_pixel_move_velocity_x;
}

int16_t mouse_pixel_move_telemetry_velocity_y(void) {
  return mouse_pixel_move_velocity_y;
}

uint8_t mouse_pixel_move_telemetry_scale(void) {
  return MOUSE_PIXEL_MOVE_SCALE;
}

bool process_mouse_pixel_move(uint16_t keycode, keyrecord_t *record) {
  if (!mouse_pixel_move_is_key(keycode)) {
    return true;
  }

  bool pressed = record->event.pressed;

  switch (keycode) {
    case MS_PREC:
      mouse_pixel_move_precision = pressed;
      return false;
    case MS_BOOST:
      mouse_pixel_move_boost = pressed;
      return false;
  }

  if (pressed) {
    uint8_t direction = mouse_pixel_move_direction(keycode);
    if (mouse_pixel_move_is_small_key(keycode)) {
      mouse_pixel_move_tap_direction(direction, MOUSE_PIXEL_MOVE_SMALL_STEP);
    } else if (mouse_pixel_move_is_large_key(keycode)) {
      mouse_pixel_move_tap_direction(direction, MOUSE_PIXEL_MOVE_LARGE_STEP);
      mouse_pixel_move_press_large(keycode);
    } else if (mouse_pixel_move_is_fast_key(keycode)) {
      mouse_pixel_move_tap_direction(direction, MOUSE_PIXEL_MOVE_FAST_STEP_START);
      mouse_pixel_move_press_fast(keycode);
    }
  } else {
    if (mouse_pixel_move_is_large_key(keycode)) {
      mouse_pixel_move_release_large(keycode);
    } else if (mouse_pixel_move_is_fast_key(keycode)) {
      mouse_pixel_move_release_fast(keycode);
    }
  }

  return false;
}

void mouse_pixel_move_user_timer(void) {
  uint8_t active_directions = mouse_pixel_move_active_directions();
  if (active_directions == 0 && !mouse_pixel_move_has_velocity()) {
    return;
  }

  if (timer_elapsed32(mouse_pixel_move_tick_timer) < MOUSE_PIXEL_MOVE_TICK_INTERVAL) {
    return;
  }

  mouse_pixel_move_tick_timer = timer_read32();
  mouse_pixel_move_update_velocity(active_directions);
  mouse_pixel_move_send_velocity();
}
