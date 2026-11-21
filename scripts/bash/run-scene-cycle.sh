#!/bin/bash
# H1-01: repeated cancel/retry of .mmdscene open in one Player process.
# Reuses the scene saved by run-scene-ui.sh (Gene + Sandrone + stage, with motions).
# usage: scripts/bash/run-scene-cycle.sh <debug|release> <Mono|IL2CPP> [cycles=10]   (run run-scene-ui.sh first)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2; N=${3:-10}
R='E:\workspace\EasyDanceStudio'
A="artifacts/scene-cycle/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
UI="artifacts/scene-ui/$BK"; [ "$CFG" = release ] && UI="$UI/release"   # same layout as run-scene-ui.sh
SCENE="$R\\${UI//\//\\}\\project\\scene.mmdscene"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
START=$(date +%s)
timeout 900 "$EXEU" -pocSceneCycleReport "$AW\scene-cycle.json" -sceneFile "$SCENE" -pocCycles "$N" \
  -logFile "$AW\scene-cycle.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "CYCLE_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['configuration'],'cycles',r['cycles'],'entries',r['entries'])
print(r['error'][:800])
print('objectCountsStable',r['objectCountsStable'],'growth MiB: unity allocated %.1f reserved %.1f mono %.1f' % (r['allocatedGrowthMiB'],r['reservedGrowthMiB'],r['monoGrowthMiB']))
for s in r['samples']:
  print('#%2d cancel@%d kept=%s open=%s %.2fs | mesh %d mat %d tex %d go %d so %d life %d cand %d | alloc %.1f MiB reserved %.1f MiB mono %.1f MiB | %s' % (s['cycle'],s['cancelAtEntry'],s['cancelKeptScene'],s['fullOpenOk'],s['openSeconds'],s['meshes'],s['materials'],s['textures'],s['gameObjects'],s['scriptableObjects'],s['lifetimes'],s['candidates'],s['unityAllocatedBytes']/1048576,s['unityReservedBytes']/1048576,s['monoUsedBytes']/1048576,s['cancelStage']))
" "$A/scene-cycle.json" "$START"
