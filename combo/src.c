typedef struct Combo {
  ComboMask mask;
  uint8_t size;
  uint8_t state;
  uint32_t last_modify_time;
} Combo;

enum ComboState {
  COMBO_STATE_COLLECTING = 1,
  COMBO_STATE_PRESSED,
  COMBO_STATE_RELEASE_ONLY,
  COMBO_STATE_IMMEDIATE,
};

// #define COMBO_DEBUG

#ifdef COMBO_DEBUG
  #define TRANSITION_DEBUG(a) uprintf( \
    "transition '" #a "' now it is #%d: mask=%lu/%lu size=%d state=%d\n", \
    (int)(combo - &combo_stack[0]), \
    (unsigned long)combo->mask.high, \
    (unsigned long)combo->mask.low, \
    combo->size, \
    combo->state)
#else
  #define TRANSITION_DEBUG(a) ;
#endif

Combo combo_stack[COMBO_STACK_MAX_SIZE] = {};
uint8_t combo_stack_size = 0;
bool combo_enabled = true;
bool combo_k_enabled = true;

bool combo_pos_is_valid(ComboPos pos) {
  return neq_combo_pos(pos, NONE_COMBO_POS) && pos.repr < combos_size;
}

bool combo_is_combo_key(uint16_t key) {
  return CMB_000 <= key && key < CMB_000 + COMBO_KEYS_COUNT;
}

ComboKey combo_key_to_combo_key(uint16_t key) {
  if (combo_is_combo_key(key)) {
    return COMBO_KEY(key - CMB_000);
  } else {
    return NONE_COMBO_KEY;
  }
}

static ComboMask combo_empty_mask(void) {
  ComboMask mask = {0, 0};
  return mask;
}

static ComboMask combo_key_mask(ComboKey key) {
  ComboMask mask = combo_empty_mask();
  if (eq_combo_key(key, NONE_COMBO_KEY)) {
    return mask;
  }

  if (key.repr < 32) {
    mask.low = (uint32_t)1UL << key.repr;
  } else {
    mask.high = (uint32_t)1UL << (key.repr - 32);
  }

  return mask;
}

static ComboMask combo_mask_add_key(ComboMask mask, ComboKey key) {
  ComboMask bit = combo_key_mask(key);
  mask.low |= bit.low;
  mask.high |= bit.high;
  return mask;
}

static ComboMask combo_mask_remove_key(ComboMask mask, ComboKey key) {
  ComboMask bit = combo_key_mask(key);
  mask.low &= ~bit.low;
  mask.high &= ~bit.high;
  return mask;
}

static bool combo_mask_eq(ComboMask a, ComboMask b) {
  return a.low == b.low && a.high == b.high;
}

static bool combo_mask_contains(ComboMask mask, ComboMask required) {
  return (mask.low & required.low) == required.low &&
    (mask.high & required.high) == required.high;
}

static bool combo_mask_has_key(ComboMask mask, ComboKey key) {
  return combo_mask_contains(mask, combo_key_mask(key));
}

static ComboMask combo_get_mask(ComboPos pos) {
  if (!combo_pos_is_valid(pos)) {
    return combo_empty_mask();
  }

  ComboMask mask;
  mask.low = pgm_read_dword(&(combos[pos.repr].mask.low));
  mask.high = pgm_read_dword(&(combos[pos.repr].mask.high));
  return mask;
}

bool combo_has_key(Combo *combo, ComboKey key) {
  return combo_mask_has_key(combo->mask, key);
}

uint8_t combo_get_len(ComboPos elem_index) {
  if (!combo_pos_is_valid(elem_index)) {
    return 0;
  }

  return pgm_read_byte(&(combos[elem_index.repr].size));
}

ComboPos combo_get_pos(Combo *combo) {
  for (uint8_t i = 0; i < combos_size; ++i) {
    ComboPos pos = COMBO_POS(i);
    if (combo_get_len(pos) == combo->size && combo_mask_eq(combo_get_mask(pos), combo->mask)) {
      return pos;
    }
  }
  return NONE_COMBO_POS;
}

bool combo_has_prefix(Combo *combo, ComboKey another_key) {
  ComboMask required = combo_mask_add_key(combo->mask, another_key);
  if (combo_mask_eq(required, combo->mask)) {
    return false;
  }

  for (uint8_t i = 0; i < combos_size; ++i) {
    ComboPos pos = COMBO_POS(i);
    if (combo_get_len(pos) > combo->size) {
      if (combo_mask_contains(combo_get_mask(pos), required)) {
        return true;
      }
    }
  }
  return false;
}

uint16_t combo_get_keycode(ComboPos pos) {
  if (!combo_pos_is_valid(pos)) {
    return KC_NO;
  }

  return pgm_read_word(&(combos[pos.repr].keycode));
}

uint16_t combo_get_undo(ComboPos elem_index) {
  if (!combo_pos_is_valid(elem_index)) {
    return 0;
  }

  return pgm_read_word(&(combos[elem_index.repr].undo_keycode));
}

bool combo_is_immediate(ComboPos elem_index) {
  if (!combo_pos_is_valid(elem_index)) {
    return false;
  }

  return combo_get_undo(elem_index) != 0;
}

static void combo_press_keycode(ComboPos pos, bool down) {
  if (!combo_pos_is_valid(pos)) {
    return;
  }

  bool was_combo_enabled = combo_enabled;
  combo_enabled = false;
  press_arbitrary_keycode(combo_get_keycode(pos), down);
  combo_enabled = was_combo_enabled;
}

void combo_press(ComboPos pos, bool down) {
  #ifdef COMBO_DEBUG
  uprintf("combo press pos: %d %s\n", pos.repr, down ? "down" : "up");
  #endif

  combo_press_keycode(pos, down);
}

void combo_press_undo(ComboPos pos) {
  if (!combo_pos_is_valid(pos)) {
    return;
  }

  #ifdef COMBO_DEBUG
  uprintf("combo press undo up: %d\n", pos.repr);
  #endif

  bool was_combo_enabled = combo_enabled;
  combo_enabled = false;
  press_arbitrary_keycode(combo_get_undo(pos), false);
  combo_enabled = was_combo_enabled;
}

void combo_reset_all(void) {
  for (uint8_t i = 0; i < combo_stack_size; ++i) {
    Combo *combo = &combo_stack[i];
    if (combo->state == COMBO_STATE_PRESSED || combo->state == COMBO_STATE_IMMEDIATE) {
      combo_press_keycode(combo_get_pos(combo), false);
    }
  }

  combo_stack_size = 0;
  combo_k_enabled = true;
}

void process_as_usual(keyrecord_t* record) {
  #ifdef COMBO_DEBUG
  uprintf("process as usual\n");
  #endif
  combo_enabled = false;
  process_record(record);
  combo_enabled = true;
}

void process_combo_as_usual(keyrecord_t* record) {
  #ifdef COMBO_DEBUG
  uprintf("process combo as usual\n");
  #endif
  combo_k_enabled = false;
  process_record(record);
  combo_k_enabled = true;
}

void combo_onenter_1(Combo *combo) {
  combo->state = COMBO_STATE_COLLECTING;
}

void combo_onenter_2(Combo *combo, ComboPos pos, keyrecord_t* record) {
  combo_press(pos, true);
  process_as_usual(record);
  combo->state = COMBO_STATE_PRESSED;
}

void combo_onenter_end(Combo *combo) {
  uint8_t pos = combo_stack_size;
  for (uint8_t i = 0; i < combo_stack_size; ++i) {
    if (&combo_stack[i] == combo) {
      pos = i;
    }
  }

  if (pos == combo_stack_size) {
    return;
  }

  combo_stack_size--;

  for (uint8_t i = pos; i < combo_stack_size; ++i) {
    combo_stack[i] = combo_stack[i+1];
  }
}

bool combo_onenter_3(Combo *combo, ComboKey key) {
  if (!combo_has_key(combo, key)) {
    return false;
  }

  combo->size--;
  combo->mask = combo_mask_remove_key(combo->mask, key);
  combo->state = COMBO_STATE_RELEASE_ONLY;

  if (combo->size == 0) {
    combo_onenter_end(combo);
  }

  return true;
}

bool combo_process_1(Combo *combo, uint16_t key, keyrecord_t *record) {
  bool down = record->event.pressed;
  bool up = !down;
  ComboKey key_combo = combo_key_to_combo_key(key);
  ComboPos pos = combo_get_pos(combo);

  if (down && neq_combo_key(key_combo, NONE_COMBO_KEY)) {
    if (combo_has_prefix(combo, key_combo)) {
      if (combo->size == COMBO_MAX_SIZE) {
        combo_max_size_error();
      } else {
        combo->mask = combo_mask_add_key(combo->mask, key_combo);
        combo->size++;
        combo->last_modify_time = timer_read();
        TRANSITION_DEBUG(e);

        ComboPos newpos = combo_get_pos(combo);
        if (combo_is_immediate(newpos)) {
          combo_press(newpos, true);
          combo->state = COMBO_STATE_IMMEDIATE;
        }
      }
      return false;
    } else {
      if (neq_combo_pos(pos, NONE_COMBO_POS) && combo_k_enabled) {
        combo_press(pos, true);
        process_combo_as_usual(record);
        combo->state = COMBO_STATE_PRESSED;
        TRANSITION_DEBUG(k);
        return false;
      }
    }
  }

  if (neq_combo_pos(pos, NONE_COMBO_POS)) {
    if (neq_combo_key(key_combo, NONE_COMBO_KEY)) {
      if (up && combo_has_key(combo, key_combo)) {
        combo_press(pos, true);
        combo_press(pos, false);

        combo_onenter_3(combo, key_combo);
        TRANSITION_DEBUG(g);
        if (combo->size == 0) {
          TRANSITION_DEBUG(i);
        }
        return false;  
      }
    } else {
      if (down) {
        combo_onenter_2(combo, pos, record);
        TRANSITION_DEBUG(b);
        return false;
      }
    }
  } else {
    if (up && neq_combo_key(key_combo, NONE_COMBO_KEY) && combo_has_key(combo, key_combo)) {
      combo_onenter_3(combo, key_combo);
      TRANSITION_DEBUG(f);
      if (combo->size == 0) {
        TRANSITION_DEBUG(i);
      }
      return false;
    }
  }

  return true;
}

bool combo_process_2(Combo *combo, uint16_t key, keyrecord_t *record) {
  bool down = record->event.pressed;
  bool up = !down;
  ComboKey key_combo = combo_key_to_combo_key(key);

  if (up && neq_combo_key(key_combo, NONE_COMBO_KEY) && combo_has_key(combo, key_combo)) {

    ComboPos pos = combo_get_pos(combo);
    combo_press(pos, false);

    combo_onenter_3(combo, key_combo);
    TRANSITION_DEBUG(c);
    if (combo->size == 0) {
      TRANSITION_DEBUG(i);
    }
    return false;
  }

  return true;
}

bool combo_process_3(Combo *combo, uint16_t key, keyrecord_t *record) {
  bool down = record->event.pressed;
  bool up = !down;
  ComboKey key_combo = combo_key_to_combo_key(key);

  if (up && neq_combo_key(key_combo, NONE_COMBO_KEY) && combo_has_key(combo, key_combo)) {
    combo_onenter_3(combo, key_combo);
    TRANSITION_DEBUG(h);
    if (combo->size == 0) {
      TRANSITION_DEBUG(i);
    }
    return false;
  }

  return true;
}

bool combo_process_4(Combo *combo, uint16_t key, keyrecord_t *record) {
  bool down = record->event.pressed;
  bool up = !down;
  ComboKey key_combo = combo_key_to_combo_key(key);
  ComboPos pos = combo_get_pos(combo);

  if (down && neq_combo_key(key_combo, NONE_COMBO_KEY)) {
    if (combo_has_prefix(combo, key_combo)) {
      combo_press_undo(pos);
      combo->state = COMBO_STATE_COLLECTING;
      TRANSITION_DEBUG(e4);
      return combo_process_1(combo, key, record);
    } else {
      if (neq_combo_pos(pos, NONE_COMBO_POS) && combo_k_enabled) {
        combo->state = COMBO_STATE_PRESSED;
        TRANSITION_DEBUG(k4);
        return true;
      }
    }
  }

  // This is guaranteed to be true
  if (!neq_combo_pos(pos, NONE_COMBO_POS)) {
    combo_max_count_error();
    return false;
  }

  if (neq_combo_key(key_combo, NONE_COMBO_KEY)) {
    if (up && combo_has_key(combo, key_combo)) {
      TRANSITION_DEBUG(g4);
      return combo_process_1(combo, key, record);
    }
  } else {
    if (down) {
      TRANSITION_DEBUG(b4);
      return true;
    }
  }

  return true;
}


bool combo_process_local_states(Combo *combo, uint16_t key, keyrecord_t *record) {
  switch (combo->state) {
    case COMBO_STATE_COLLECTING: return combo_process_1(combo, key, record);
    case COMBO_STATE_PRESSED: return combo_process_2(combo, key, record);
    case COMBO_STATE_RELEASE_ONLY: return combo_process_3(combo, key, record);
    case COMBO_STATE_IMMEDIATE: return combo_process_4(combo, key, record);
  }
  return true;
}

bool combo_process_record(uint16_t key, keyrecord_t *record) {
  if (!combo_enabled)
    return true;

  bool down = record->event.pressed;
  ComboKey key_combo = combo_key_to_combo_key(key);
  #ifdef COMBO_DEBUG
  uprintf("%d pressed %s\n", key_combo.repr, down ? "down" : "up");
  #endif

  for (uint8_t i = 0; i < combo_stack_size; ++i) {
    Combo *combo = &combo_stack[i];
    if (!combo_process_local_states(combo, key, record))
      return false;
  }

  if (down && neq_combo_key(key_combo, NONE_COMBO_KEY)) {
    if (combo_stack_size == COMBO_STACK_MAX_SIZE) {
      combo_max_count_error();
    } else {
      Combo* combo = &combo_stack[combo_stack_size];
      combo_stack_size++;
      combo->mask = combo_key_mask(key_combo);
      combo->size = 1;
      combo->state = COMBO_STATE_COLLECTING;
      combo->last_modify_time = timer_read();
      TRANSITION_DEBUG(a);

      ComboPos pos = combo_get_pos(combo);
      if (combo_is_immediate(pos)) {
        combo_press(pos, true);
        combo->state = COMBO_STATE_IMMEDIATE;
      }
    }
    return false;
  }

  return true;
}

void combo_user_timer(void) {
  for (int i = 0; i < combo_stack_size; ++i) {
    Combo* combo = &combo_stack[i];
    if (combo->state == COMBO_STATE_COLLECTING) {
      if (timer_read() - combo->last_modify_time > COMBO_WAIT_TIME) {
        ComboPos pos = combo_get_pos(combo);
        if (neq_combo_pos(pos, NONE_COMBO_POS)) {
          combo_press(pos, true);
          combo->state = COMBO_STATE_PRESSED;
          TRANSITION_DEBUG(d);
        }
      }
    } else if (combo->state == COMBO_STATE_IMMEDIATE) {
      if (timer_read() - combo->last_modify_time > COMBO_WAIT_TIME) {
        ComboPos pos = combo_get_pos(combo);
        if (neq_combo_pos(pos, NONE_COMBO_POS)) {
          combo->state = COMBO_STATE_PRESSED;
          TRANSITION_DEBUG(d4);
        }
      }
    }
  }
}
