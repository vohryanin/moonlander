#pragma once

#include "map.h"

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for combo extension."
#endif

enum combo_keycodes {
  COMBO_START = CUSTOM_SAFE_RANGE,

  #include "keycodes.h"

  COMBO_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE COMBO_NEW_SAFE_RANGE
};

#if COMBO_KEYS_COUNT > 64
  #error "Combo bitmask supports up to 64 combo keys."
#endif

#if COMBO_MAX_SIZE > 4
  #error "COMBO_COUNT supports up to 4-key chords."
#endif

typedef struct ComboMask {
  uint32_t low;
  uint32_t high;
} ComboMask;

#define COMBO_KEY_INDEX(x) ((x) - CMB_000)
#define COMBO_MASK_LOW_BIT(x) (COMBO_KEY_INDEX(x) < 32 ? ((uint32_t)1UL << (COMBO_KEY_INDEX(x) & 31)) : 0UL)
#define COMBO_MASK_HIGH_BIT(x) (COMBO_KEY_INDEX(x) >= 32 ? ((uint32_t)1UL << (COMBO_KEY_INDEX(x) & 31)) : 0UL)
#define COMBO_MASK_LOW_PART(x) COMBO_MASK_LOW_BIT(x) |
#define COMBO_MASK_HIGH_PART(x) COMBO_MASK_HIGH_BIT(x) |
#define COMBO_MASK_INIT(...) { MAP(COMBO_MASK_LOW_PART, __VA_ARGS__) 0UL, MAP(COMBO_MASK_HIGH_PART, __VA_ARGS__) 0UL }

#define COMBO_COUNT_IMPL(_1, _2, _3, _4, N, ...) N
#define COMBO_COUNT(...) COMBO_COUNT_IMPL(__VA_ARGS__, 4, 3, 2, 1, 0)

// С помощью этого макроса задаётся аккорд
#define CHORD(KEYCODE, ...) { .mask = COMBO_MASK_INIT(__VA_ARGS__), .size = COMBO_COUNT(__VA_ARGS__), .keycode = KEYCODE, .undo_keycode = 0 }
#define IMMEDIATE_CHORD(KEYCODE, UNDO, ...) { .mask = COMBO_MASK_INIT(__VA_ARGS__), .size = COMBO_COUNT(__VA_ARGS__), .keycode = KEYCODE, .undo_keycode = UNDO }

// Uncomment this line if you want to print debug this extension
// #define COMBO_DEBUG

// Newtype pattern allows us wrap some type in that type and use static type-checking
#define NEWTYPE(name, func_name, type, max, none_name) \
  typedef struct name { \
    type repr; \
  } name; \
  \
  const name none_name = ((name){ .repr = max }); \
  \
  type get_ ## func_name(name a) { \
    return a.repr; \
  } \
  \
  bool eq_ ## func_name(name a, name b) { \
    return get_ ## func_name(a) == get_ ## func_name(b); \
  } \
  \
  bool neq_ ## func_name(name a, name b) { \
    return get_ ## func_name(a) != get_ ## func_name(b); \
  }

NEWTYPE(ComboKey, combo_key, uint8_t, 255, NONE_COMBO_KEY)
#define COMBO_KEY(x) ((ComboKey){ .repr = (x) }) // C is weak piece of shit: it can't do #define inside #define, or it can't do const function which we can use inside initialization. So we must write this by hand.

NEWTYPE(ComboPos, combo_pos, uint8_t, 255, NONE_COMBO_POS)
#define COMBO_POS(x) ((ComboPos){ .repr = (x) })

typedef struct ComboWithKeycode {
  ComboMask mask;
  uint8_t size;
  uint16_t keycode;
  uint16_t undo_keycode;
} ComboWithKeycode;

const ComboWithKeycode PROGMEM combos[];
const uint8_t combos_size;

bool combo_process_record(uint16_t key, keyrecord_t *record);
void combo_user_timer(void);
void combo_reset_all(void);
void combo_max_count_error(void);
void combo_max_size_error(void);

// Инклюжу код напрямую, потому что нельзя сделать линковку, ведь код внутри использует кейкоды отсюда, и обязательно нужно это делать через safe_range
#include "src.c"
