Install:
```bash
git clone -b firmware20 https://github.com/zsa/qmk_firmware zsa_qmk
cd zsa_qmk
./util/qmk_install.sh
git submodule init
git submodule update
```

Make symbolic link:
```bash
ln -s /home/v/my/moonlander /opt/zsa_qmk_fw20/zsa_qmk/keyboards/moonlander/keymaps/optozorax
```

Flash:
```bash
make moonlander:optozorax:flash
```

### make.sh - Make script
(
#!/bin/bash

### vars for repo
export VCS_URL=https://read_repository:glpat-p1cjbSBtzLY6duw41ugf@gitlab.sigma-it.ru/k8s
export VCS_URL=https://github.com/vohryanin
export REPO=moonlander
export BRANCH="main"
export WORK_DIR="/home/v/my"
export QMK_DIR="/opt/zsa_qmk_fw20/zsa_qmk"

### cloning repo
mkdir -p ${WORK_DIR}
cd ${WORK_DIR}
find ./${REPO}/ -type f -delete
find ./${REPO}/ -type d -delete
#rm -rf ./${REPO}/
git clone -b ${BRANCH} ${VCS_URL}/${REPO}.git

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
)




### flash.sh - Make and flash script
(
#!/bin/bash

### vars for repo
export VCS_URL=https://read_repository:glpat-p1cjbSBtzLY6duw41ugf@gitlab.sigma-it.ru/k8s
export VCS_URL=https://github.com/vohryanin
export REPO=moonlander
export BRANCH="main"
export WORK_DIR="/home/v/my"
export QMK_DIR="/opt/zsa_qmk_fw20/zsa_qmk"

### cloning repo
mkdir -p ${WORK_DIR}
cd ${WORK_DIR}
find ./${REPO}/ -type f -delete
find ./${REPO}/ -type d -delete
#rm -rf ./${REPO}/
git clone -b ${BRANCH} ${VCS_URL}/${REPO}.git

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
./flash.sh
)