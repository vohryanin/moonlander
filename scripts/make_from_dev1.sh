#!/bin/bash

### vars for repo
export VCS_URL=https://read_repository:glpat-p1cjbSBtzLY6duw41ugf@gitlab.sigma-it.ru/k8s
export VCS_URL=https://github.com/vohryanin
export REPO=moonlander
export BRANCH="dev1"
export WORK_DIR="/home/v/my"
export QMK_DIR="/opt/zsa_qmk_fw20/zsa_qmk"

### cloning repo
mkdir -p ${WORK_DIR}
cd ${WORK_DIR}
find ./${REPO}/ -type f -delete
find ./${REPO}/ -type d -delete
#rm -rf ./${REPO}/
git clone --recurse-submodules -b ${BRANCH} ${VCS_URL}/${REPO}.git
cd ./${REPO}/
#git submodule init
#git submodule update

### prepare cloned files for working
cd ${WORK_DIR}/moonlander/scripts

echo
echo
pwd
ls -la
chmod +x *.sh

### modify QMK
./prepare.sh

### make image
./make.sh