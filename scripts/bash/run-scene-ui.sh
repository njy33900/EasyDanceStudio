#!/bin/bash
# Bash equivalent of scripts/Invoke-SceneVerification.ps1 -Ui, for sessions without PowerShell 7.
# Run from Git Bash on Windows. PROFILE must match the output folder used by the .ps1 scripts.
# usage: scripts/bash/run-scene-ui.sh <debug|release> <Mono|IL2CPP> [skipbuild]
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
PROFILE=V0
R='E:\workspace\EasyDanceStudio'
A="artifacts/scene-ui/$BK"; [ "$CFG" = release ] && A="$A/release"
mkdir -p "$A"
AW="$R\\${A//\//\\}"
EXE="$R\\out\\$CFG\\$PROFILE-$BK\\MmdStudioPoC.exe"
EXEU="./out/$CFG/$PROFILE-$BK/MmdStudioPoC.exe"
echo "artifacts=$AW exe=$EXE"
if [ "${3:-}" != skipbuild ]; then
  timeout 1800 "/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" -batchmode -nographics -quit \
    -projectPath "$R\\MMDStudio" -pocConfiguration "$CFG" -executeMethod MmdStudio.Poc.PocBuild.BuildWindows \
    -pocOutput "$EXE" -pocBackend "${BK,,}" -logFile "$AW\\poc-build.log" > "$A/build-stdout.txt" 2>&1
  B=$?
  echo "BUILD_EXIT=$B"; grep -m3 "error CS\|Result:" "$A/poc-build.log"
  [ $B -eq 0 ] || exit 1
fi
python -I scripts/New-StageFixture.py --output "$A/fixtures" > /dev/null || exit 1
START=$(date +%s)
timeout 300 "$EXEU" -pocPmx "$R\\test\\models\\Gene\\Gene_light.pmx" \
  -pocSecondPmx "$R\\test\\Sandrone_Runtime\\桑多涅_目齿轮可动.pmx" \
  -pocVmd "$R\\test\\aespa - Whiplash\\aespa - Whiplash\\Whiplash [SMOOTH].vmd" \
  -pocSceneUiReport "$AW\\scene-ui.json" -logFile "$AW\\scene.log" \
  -pocStagePmx "$AW\\fixtures\\stage.pmx" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/run-stdout.txt" 2>&1
echo "RUN_EXIT=$?"
python -c "
import json,os,sys
p=sys.argv[1]; fresh=os.path.getmtime(p)>=int(sys.argv[2])
r=json.load(open(p,encoding='utf-8'))
print('fresh',fresh,'success',r['success'],r['backend'],'checks',len(r['checks']))
print(r['error'][:1200])" "$A/scene-ui.json" "$START"
grep "graphics-probe" "$A/scene.log" | head -3
START=$(date +%s)
timeout 300 "$EXEU" -pocSceneRestoreReport "$AW\\scene-restore.json" -sceneFile "$AW\\project\\scene.mmdscene" \
  -logFile "$AW\\scene-restore.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/restore-stdout.txt" 2>&1
echo "RESTORE_EXIT=$?"
python -c "
import json,os,sys
p=sys.argv[1]; fresh=os.path.getmtime(p)>=int(sys.argv[2])
r=json.load(open(p,encoding='utf-8'))
print('restore fresh',fresh,'success',r['success'],r['backend'],'checks',len(r['checks']),'objects',r['objects'])
print(r['error'][:1200])" "$A/scene-restore.json" "$START"
