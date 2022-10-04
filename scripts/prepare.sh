#!/bin/bash

. ./vars.sh

### modify QMK
cp ${WORK_DIR}/moonlander/quantum/keymap_common.c ${QMK_DIR}/quantum/keymap_common.c
cp ${WORK_DIR}/moonlander/tmk_core/common/action.c ${QMK_DIR}/tmk_core/common/action.c
cp ${WORK_DIR}/moonlander/tmk_core/common/keyboard.h ${QMK_DIR}/tmk_core/common/keyboard.h