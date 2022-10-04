#!/bin/bash

. ./vars.sh

### cloning repo
mkdir -p ${WORK_DIR}
cd ${WORK_DIR}
find ./${REPO}/ -type f -delete
find ./${REPO}/ -type d -delete
#rm -rf ./${REPO}/
git clone -b ${BRANCH} ${VCS_URL}/${REPO}.git

### modify QMK
cp ${WORK_DIR}/moonlander/quantum/keymap_common.c ${QMK_DIR}/quantum/keymap_common.c