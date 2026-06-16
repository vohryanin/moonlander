#pragma once

#ifndef CUSTOM_SAFE_RANGE
  #error "You must specify variable CUSTOM_SAFE_RANGE for songs keycodes."
#endif

enum songs_keycodes {
  SONGS_START = CUSTOM_SAFE_RANGE,

  MU_LANG,
  MU_LAEN,
  MU_LARU,
  MU_LAN1,
  MU_LAN2,
  MU_LAN3,
  MU_LAN4,
  MU_CTJ,
  MU_SCR,
  MU_WNL,

  SONGS_NEW_SAFE_RANGE,
  #undef CUSTOM_SAFE_RANGE
  #define CUSTOM_SAFE_RANGE SONGS_NEW_SAFE_RANGE
};

// Музыка обязательно должна находиться вне функции, потому что она проигрывается асинхронно...
float my_song1[][2] = SONG(QWERTY_SOUND);
float my_song2[][2] = SONG(PLANCK_SOUND);
float my_song3[][2] = SONG(AG_SWAP_SOUND);
float my_song4[][2] = SONG(VIOLIN_SOUND);
float my_song5[][2] = SONG(GUITAR_SOUND);
float my_song6[][2] = SONG(CHROMATIC_SOUND);

static bool music_keycode_disabled = false;

#ifndef LANG_SWITCH_AUDIO_COOLDOWN
  #define LANG_SWITCH_AUDIO_COOLDOWN 120
#endif

static uint32_t lang_switch_audio_timer = 0;
static bool lang_switch_audio_timer_started = false;

static void play_lang_switch_audio(void) {
  if (lang_switch_audio_timer_started && LANG_SWITCH_AUDIO_COOLDOWN > 0) {
    if (timer_elapsed32(lang_switch_audio_timer) < LANG_SWITCH_AUDIO_COOLDOWN) {
      return;
    }
  }

  lang_switch_audio_timer = timer_read32();
  lang_switch_audio_timer_started = true;
  PLAY_SONG(my_song1);
}

static void music_press_arbitrary_keycode(uint16_t keycode, bool down) {
  music_keycode_disabled = true;
  press_arbitrary_keycode(keycode, down);
  music_keycode_disabled = false;
}

// Эта функция должна находиться самой последней по приоритету
bool process_my_music_keys(uint16_t keycode, keyrecord_t *record) {
  // https://github.com/qmk/qmk_firmware/blob/master/quantum/audio/song_list.h
  // https://docs.qmk.fm/#/feature_audio

  if (music_keycode_disabled) {
    return true;
  }

  #define MUSIC_KEYCODE(FROM, TO, SONG) \
    case FROM: \
      if (record->event.pressed) { \
        PLAY_SONG(SONG); \
      } \
      music_press_arbitrary_keycode(TO, record->event.pressed); \
      return false;

  #define MUSIC_KEYCODE_LANG(FROM, TO) \
    case FROM: \
      music_press_arbitrary_keycode(TO, record->event.pressed); \
      if (record->event.pressed) { \
        play_lang_switch_audio(); \
      } \
      return false;

  switch (keycode) {
    MUSIC_KEYCODE_LANG(MU_LANG, LA_CHNG)
    MUSIC_KEYCODE_LANG(MU_LAEN, LA_EN)
    MUSIC_KEYCODE_LANG(MU_LARU, LA_RU)
    MUSIC_KEYCODE(MU_LAN1, LA_CAPS, my_song2)
    MUSIC_KEYCODE(MU_LAN2, LA_ALSH, my_song4)
    MUSIC_KEYCODE(MU_LAN3, LA_CTSH, my_song5)
    MUSIC_KEYCODE(MU_LAN4, LA_WISP, my_song6)
    MUSIC_KEYCODE(MU_CTJ, CT_J, my_song3)
    MUSIC_KEYCODE(MU_SCR, KC_PSCR, my_song3)
    MUSIC_KEYCODE(MU_WNL, WN_L, my_song3)

    case TG(LAYER_RED):
    case TG(LAYER_GREEN):
    case TG(LAYER_GAME):
    case TG(LAYER_PURPLE):
    case TG(LAYER_YELLOW):
    MUSIC_KEYCODE(TG(LAYER_ORANGE), keycode, my_song6)
  }

  #undef MUSIC_KEYCODE_LANG
  #undef MUSIC_KEYCODE

  return true;
}
