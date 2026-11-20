#!/bin/bash
# PH-03: physics bake cache accuracy (vs uninterrupted simulation), negative control and cost.
# usage: scripts/bash/run-physics-bake.sh <debug|release> <Mono|IL2CPP>   (build first with run-scene-ui.sh)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
R='E:\workspace\EasyDanceStudio'
A="artifacts/physics-bake/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
START=$(date +%s)
timeout 900 "$EXEU" -pocPhysicsBakeReport "$AW\physics-bake.json" \
  -pocPmx "$R\test\models\Gene\Gene_light.pmx" -pocSecondPmx "$R\test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx" \
  -pocVmd "$R\test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd" -logFile "$AW\physics-bake.log" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "BAKE_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['configuration'],'budget %.0f ms/frame' % r['budgetMs'])
print(r['error'][:1500])
for m in r['models'] or []:
  print('%s: %d ticks checked at %d checkpoints | max error %.7f m (control without state restore %.4f m) | cached bones %d, full length %d ticks = %.1f MB (compressed)' % (m['name'].encode('ascii','replace').decode(), m['ticks'], m['checkpoints'], m['maxErrorM'], m['controlMaxErrorM'], m['cachedBones'], m['totalTicks'], m['fullLengthBytes']/1048576))
  print('   budget %.0f ms: bake %.1f s wall over %d frames, %.0f ticks/s, worst frame %.1f ms, longest step %.1f ms, mid-tick %s, beyond-range rejected %s' % (m['budgetMs'], m['bakeWallSeconds'], m['bakeFrames'], m['bakeTicksPerSecond'], m['worstFrameMs'], m['longestStepMs'], m['midTickApplies'], m['beyondBakedRejected']))
  for v in m.get('variants') or []: print('   ', v.encode('ascii','replace').decode())
  print('   weighted bones %d, position tracks kept %d, jump ticks %d (first %.2fs, worst %.3f m %s)' % (m['weightedBones'], m['positionTracks'], m['jumpTicks'], m['firstJumpSeconds'], m['worstJumpM'], m['worstJumpBone']))
" "$A/physics-bake.json" "$START"
