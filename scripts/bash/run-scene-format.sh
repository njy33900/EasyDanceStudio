#!/bin/bash
# H1-05: scene file schema 2 checks — schema 1 migration, Korean/space/special-character folder,
# another drive (absolute paths), read-only file. Reuses the scene saved by run-scene-ui.sh.
# usage: scripts/bash/run-scene-format.sh <debug|release> <Mono|IL2CPP>   (run run-scene-ui.sh first)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
R='E:\workspace\EasyDanceStudio'
A="artifacts/scene-format/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
PROJECT="artifacts/scene-ui/$BK/$CFG/project"
SCENE="$R\\${PROJECT//\//\\}\\scene.mmdscene"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
START=$(date +%s)
timeout 600 "$EXEU" -pocSceneFormatReport "$AW\\scene-format.json" -sceneFile "$SCENE" \
  -logFile "$AW\\scene-format.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "FORMAT_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],'checks',len(r['checks']))
for c in r['checks']: print('   ', c)
print(r['error'][:800]); print('notes:', r['notes'])
" "$A/scene-format.json" "$START"
