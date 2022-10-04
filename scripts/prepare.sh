#!/bin/bash

. ./vars.sh

### modify QMK
cp ${WORK_DIR}/moonlander/quantum/keymap_common.c ${QMK_DIR}/quantum/keymap_common.c