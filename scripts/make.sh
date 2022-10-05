#!/bin/bash

. ./vars.sh

### make image
cd ${QMK_DIR}
make moonlander:optozorax
chown -R v:v ${QMK_DIR}