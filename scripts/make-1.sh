#!/bin/bash

### vars for repo
export VCS_URL=https://github.com/vohryanin
export REPO=moonlander
export BRANCH="master"
export REVISION="c51b7762b4f5a35b12b8ca0cdb5253c17ad5107c"
export WORK_DIR="/home/v/my"
export QMK_DIR="/opt/zsa_qmk_fw20/zsa_qmk"

rm -rf ./${REPO}/
mv ${WORK_DIR}/moonlander-1 ${WORK_DIR}/${REPO}/
cp -rp ${WORK_DIR}/${REPO}_old/scripts ${WORK_DIR}/${REPO}/scripts

### prepare cloned files for working
cd ${WORK_DIR}/moonlander/scripts

echo
echo
pwd
ls -la
#chmod +x *.sh

### modify QMK
#./prepare.sh

### make image
./make.sh