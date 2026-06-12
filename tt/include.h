#pragma once

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for tt extension."
#endif

#define TT_KEYS_COUNT 10

enum tt_keycodes {
	TT_START = CUSTOM_SAFE_RANGE,

	#include "keycodes.h"

	TT_NEW_SAFE_RANGE,
	#undef CUSTOM_SAFE_RANGE
	#define CUSTOM_SAFE_RANGE TT_NEW_SAFE_RANGE
};

enum tt_key_fields {
	TT_FIELD_KEYCODE,
	TT_FIELD_HOLD,
	TT_FIELD_TOGGLE,
	TT_FIELD_COUNT,
};

enum tt_config {
	TT_POS_NONE = 255,
	TT_COUNT_MAX = 255,
	TT_TOGGLE_TAP_COUNT = 3,
};

const uint16_t tt_keys[][TT_FIELD_COUNT];
const uint8_t tt_size;

uint8_t tt_get_pos(uint16_t key) {
	for (int i = 0; i < tt_size; ++i) {
		if (tt_keys[i][TT_FIELD_KEYCODE] == key) {
			return i;
		}
	}
	return TT_POS_NONE;
}

uint16_t tt_previous_key = 0;
uint8_t tt_count = 0;
bool tt_now_press = false;

static void tt_reset(void) {
	tt_previous_key = 0;
	tt_count = 0;
}

static bool tt_pos_is_valid(uint8_t pos) {
	return pos != TT_POS_NONE;
}

static void tt_press_field(uint8_t pos, uint8_t field, bool down) {
	press_arbitrary_keycode(tt_keys[pos][field], down);
}

static void tt_tap_field(uint8_t pos, uint8_t field) {
	tt_press_field(pos, field, true);
	tt_press_field(pos, field, false);
}

static void tt_update_count(uint16_t key, bool down) {
	if (key == tt_previous_key) {
		if (down && tt_count < TT_COUNT_MAX) {
			tt_count += 1;
		}
	} else {
		tt_previous_key = key;
		tt_count = 1;
	}
}

bool tt_process_record(uint16_t key, keyrecord_t *record) {
	if (TT_000 <= key && key < TT_NEW_SAFE_RANGE) {
		uint8_t pos = tt_get_pos(key);
		if (tt_pos_is_valid(pos)) {
			tt_update_count(key, record->event.pressed);

			tt_now_press = true;
			tt_press_field(pos, TT_FIELD_HOLD, record->event.pressed);
			if (tt_count == TT_TOGGLE_TAP_COUNT && !record->event.pressed) {
				tt_tap_field(pos, TT_FIELD_TOGGLE);
				tt_reset();
			}
			tt_now_press = false;

			return false;
		}
	} else {
		if (!tt_now_press) {
			tt_reset();
		}
	}

	return true;
}
