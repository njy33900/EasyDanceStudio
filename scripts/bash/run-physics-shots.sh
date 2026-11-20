#!/bin/bash
# PH-03 visual check: physics OFF/ON screenshots of Gene and Sandrone at fixed times (UI hidden).
# usage: scripts/bash/run-physics-shots.sh <debug|release> <Mono|IL2CPP>   (build first with run-scene-ui.sh)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2; MODE=${3:-}   # third argument "spring": PH-05 spring shots
R='E:\workspace\EasyDanceStudio'
A="artifacts/physics-shots/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
START=$(date +%s)
EXTRA=()
[ "$MODE" = "spring" ] && EXTRA=(-pocSpringShots)
timeout 900 "$EXEU" -pocPhysicsShots "$AW" "${EXTRA[@]}" \
  -pocPmx "$R\\test\\models\\Gene\\Gene_light.pmx" -pocSecondPmx "$R\\test\\Sandrone_Runtime\\桑多涅_目齿轮可动.pmx" \
  -pocVmd "$R\\test\\aespa - Whiplash\\aespa - Whiplash\\Whiplash [SMOOTH].vmd" -logFile "$AW\\physics-shots.log" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "SHOTS_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['error'][:500])
for s in r['shots']: print(s)
print('max bone difference ON vs OFF (m):', ['%.3f' % d for d in r['maxDifferenceM']])
" "$A/physics-shots.json" "$START"
