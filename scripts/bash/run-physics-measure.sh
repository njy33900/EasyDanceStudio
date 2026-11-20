#!/bin/bash
# PH-00: stateful 60 Hz physics measurements on Gene + Sandrone with the full Whiplash motion.
# usage: scripts/bash/run-physics-measure.sh <debug|release> <Mono|IL2CPP>   (build first with run-scene-ui.sh)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
R='E:\workspace\EasyDanceStudio'
A="artifacts/physics-measure/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
START=$(date +%s)
timeout 900 "$EXEU" -pocPhysicsMeasureReport "$AW\physics-measure.json" \
  -pocPmx "$R\test\models\Gene\Gene_light.pmx" -pocSecondPmx "$R\test\Sandrone_Runtime\桑多涅_目齿轮可动.pmx" \
  -pocVmd "$R\test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd" -logFile "$AW\physics-measure.log" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "MEASURE_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['configuration'],'motion %.1fs' % r['motionSeconds'])
print(r['error'][:800])
for m in r['models'] or []:
  print('%s: bones %d, rigid %d (kinetic %d, dynamic %d, boneAligned %d), joints %d | step avg %.3f p99 %.3f max %.3f ms over %d ticks, finite %s' % (m['name'].encode('ascii','replace').decode(), m['bones'], m['rigidBodies'], m['kinetic'], m['dynamic'], m['boneAligned'], m['joints'], m['stepAvgMs'], m['stepP99Ms'], m['stepMaxMs'], m['ticks'], m['finite']))
  for p in m['parts']:
    if p['bodies']: print('   %-9s %3d bodies (dyn %d, aligned %d) step disp max %.4f p99 %.4f m, jump ticks %d (first %.2fs) worst %s | %s' % (p['part'], p['bodies'], p['dynamic'], p['boneAligned'], p['maxStepDisplacementM'], p['p99StepDisplacementM'], p['jumpTicks'], p['firstJumpSeconds'], p['worstBody'], ', '.join(p['sampleNames'][:4])))
print('both: step avg %.3f p99 %.3f max %.3f ms (%d ticks)' % (r['bothStepAvgMs'], r['bothStepP99Ms'], r['bothStepMaxMs'], r['bothTicks']))
print('seek 30s %.2fs, 60s %.2fs, full-length estimate %.1fs, repeat identical %s; second model seek 30s %.2fs' % (r['seek30Seconds'], r['seek60Seconds'], r['seekFullEstimateSeconds'], r['seekRepeatIdentical'], r['secondSeek30Seconds']))
print('interaction max difference (overlapping characters) %.6f m' % r['interactionMaxDifferenceM'])
print('lifetime: %d session cycles, %d reload cycles, allocated growth %d bytes' % (r['sessionCycles'], r['reloadCycles'], r['allocatedGrowthBytes']))
print('  before', r['objectsBefore']); print('  after ', r['objectsAfter'])
for n in r['notes'] or []: print('note', n)
" "$A/physics-measure.json" "$START"
