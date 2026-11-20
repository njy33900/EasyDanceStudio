#!/bin/bash
# PH-05: chest/hip spring-bone candidates per model and full-length spring bakes per preset.
# usage: scripts/bash/run-spring.sh <debug|release> <Mono|IL2CPP> [extra.pmx]   (build first with run-scene-ui.sh)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2; EXTRA=${3:-}
R='E:\workspace\EasyDanceStudio'
A="artifacts/spring/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
START=$(date +%s)
THIRD=()
[ -n "$EXTRA" ] && THIRD=(-pocThirdPmx "$EXTRA")
timeout 900 "$EXEU" -pocSpringReport "$AW\\spring.json" \
  -pocPmx "$R\\test\\models\\Gene\\Gene_light.pmx" -pocSecondPmx "$R\\test\\Sandrone_Runtime\\桑多涅_目齿轮可动.pmx" "${THIRD[@]}" \
  -pocVmd "$R\\test\\aespa - Whiplash\\aespa - Whiplash\\Whiplash [SMOOTH].vmd" -logFile "$AW\\spring.log" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "SPRING_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['error'][:500])
for m in r['models']:
  print(m['name'], '|', m['summary'], '| usable roots', m['usableRoots'], '(FORCED test only)' if m.get('forced') else '')
  for c in m['candidates'] or []: print('    candidate', c)
  for p in m.get('presets') or []: print('    %-7s bake %.1fs (%d ticks), deterministic %s, jumps %d, max swing %.1f deg, longest step %.2f ms, %.1f MB' % (p['preset'], p['bakeSeconds'], p['ticks'], p['deterministic'], p['jumpTicks'], p['maxAngleDeg'], p['longestStepMs'], p['bytes']/1048576))
" "$A/spring.json" "$START"
