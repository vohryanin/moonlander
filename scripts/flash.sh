#!/bin/bash

. ./vars.sh

### make image and flash
cd ${QMK_DIR}
make moonlander:optozorax:flash
chown -R v:v ${QMK_DIR}