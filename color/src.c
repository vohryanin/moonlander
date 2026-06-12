extern bool g_suspend_state;
extern rgb_config_t rgb_matrix_config;

static HSV color_make_hsv(uint8_t hue, uint8_t sat, uint8_t val) {
  HSV hsv;
  hsv.h = hue;
  hsv.s = sat;
  hsv.v = val;
  return hsv;
}

static HSV color_black_hsv(void) {
  return color_make_hsv(0, 0, 0);
}

static HSV color_read_colormap_hsv(uint8_t color) {
  return color_make_hsv(
    pgm_read_byte(&colormap[color][0]),
    pgm_read_byte(&colormap[color][1]),
    pgm_read_byte(&colormap[color][2])
  );
}

static HSV color_read_layermap_hsv(uint8_t layer) {
  return color_make_hsv(
    pgm_read_byte(&layermap[layer][0]),
    pgm_read_byte(&layermap[layer][1]),
    pgm_read_byte(&layermap[layer][2])
  );
}

static HSV color_current_layer_hsv(void) {
  uint8_t layer = biton32(layer_state);
  if (layer < layermap_size) {
    return color_read_layermap_hsv(layer);
  }
  return color_black_hsv();
}

static HSV color_led_hsv(uint8_t color) {
  if (color == COLOR_LAYER) {
    return color_current_layer_hsv();
  }

  if (color < colormap_size) {
    return color_read_colormap_hsv(color);
  }

  return color_black_hsv();
}

void set_layer_color(int picture) {
  if (picture < 0 || picture >= ledmap_size) {
    return;
  }

  for (int i = 0; i < DRIVER_LED_TOTAL; i++) {
    uint8_t color = pgm_read_byte(&ledmap[picture][i]);
    if (color == COLOR_TRANS) {
      continue;
    }

    HSV hsv = color_led_hsv(color);
    RGB rgb = hsv_to_rgb(hsv);
    float f = (float)rgb_matrix_config.hsv.v / UINT8_MAX;
    rgb_matrix_set_color(i, f * rgb.r, f * rgb.g, f * rgb.b);
  }
}

uint8_t draw_layer = COLOR_PICTURE_DEFAULT;
void color_rgb_matrix_indicators(void) {
  if (g_suspend_state || keyboard_config.disable_layer_led) { return; }
  if (draw_layer != 0 && draw_layer <= ledmap_size) {
  	set_layer_color(draw_layer - 1);
  }
}

void color_set_picture(uint8_t picture) {
	draw_layer = picture;
}

static bool color_handle_picture_key(uint16_t keycode, keyrecord_t *record) {
	if (keycode < PIC_0 || keycode > PIC_2) {
		return false;
	}

	if (record->event.pressed) {
		color_set_picture((uint8_t)(keycode - PIC_0));
	}
	return true;
}

static bool color_handle_rgb_mode_key(uint16_t keycode, keyrecord_t *record) {
	if (keycode < RGB__0 || keycode > RGB__36) {
		return false;
	}

	if (record->event.pressed) {
		rgblight_mode((uint8_t)(keycode - RGB__0));
	}
	return true;
}

bool color_process_record(uint16_t keycode, keyrecord_t *record) {
	if (color_handle_picture_key(keycode, record)) {
		return false;
	}

	if (color_handle_rgb_mode_key(keycode, record)) {
		return false;
	}

	switch (keycode) {
		case RGB_PRT: 
		if (record->event.pressed) {
			uprintf(
				"enabled: %d, "
				"mode: %d, "
				"HSV: (%d, %d, %d), "
				"speed: %d, "
				"suspend state: %d\n", 
				rgb_matrix_is_enabled(),
				rgb_matrix_get_mode(),
				rgb_matrix_get_hue(),
				rgb_matrix_get_sat(),
				rgb_matrix_get_val(),
				rgb_matrix_get_speed(),
				rgb_matrix_get_suspend_state()
			);
		}
		return false;
	}

	return true;
}
