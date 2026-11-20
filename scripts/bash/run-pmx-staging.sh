#!/bin/bash
# H1-01b: compare the staged async PMX import with the synchronous UMT import in the Player.
# Loads Gene, Sandrone and the generated stage twice each and writes pmx-staging.json.
# H1-01: then loads a VMD with a progress callback that always throws, and injects failures after UMT's late build stages and checks nothing is left behind.
# usage: scripts/bash/run-pmx-staging.sh <debug|release> <Mono|IL2CPP>   (build first with run-scene-ui.sh)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
R='E:\workspace\EasyDanceStudio'
A="artifacts/pmx-staging/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
python -I scripts/New-StageFixture.py --output "$A/fixtures" > /dev/null || exit 1
START=$(date +%s)
timeout 600 "$EXEU" -pocPmxStagingReport "$AW\\pmx-staging.json" \
  -pocPmx "$R\\test\\models\\Gene\\Gene_light.pmx" -pocSecondPmx "$R\\test\\Sandrone_Runtime\\桑多涅_目齿轮可动.pmx" \
  -pocStagePmx "$AW\\fixtures\\stage.pmx" -pocVmd "$R\test\aespa - Whiplash\aespa - Whiplash\Whiplash [SMOOTH].vmd" -logFile "$AW\\pmx-staging.log" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > "$A/stdout.txt" 2>&1
echo "STAGING_EXIT=$?"
PYTHONIOENCODING=utf-8 python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['configuration'])
print(r['error'][:800])
for m in r['models'] or []:
  print('%-40s identical=%s tex=%d mat=%d compared=%d shared=%d | reference %.0f ms | staged %.0f ms, worst frame %.1f ms, %d frames' % (os.path.basename(m['path']).encode('ascii','replace').decode(), m['identical'], m['textures'], m['materials'], m['compared'], m['sharedNameMaterials'], m['referenceMilliseconds'], m['stagedMilliseconds'], m['stagedWorstFrameMilliseconds'], m['stagedFrames']))
  for t in m['stagedTimings'] or []: print('     ', t)
  for t in m.get('slowestFrames') or []: print('     slow', t)
  for x in (m['mismatches'] or [])[:5]: print('   MISMATCH', x[:600].encode('ascii','replace').decode())
print('VMD with throwing progress callback: loaded=%s calls=%d %s' % (r.get('throwingProgressVmdLoaded'), r.get('throwingProgressCalls',0), (r.get('throwingProgressError') or '')[:300]))
print('UMT conversion failure: thrown %d/%d, leak %d bytes/failure (success baseline %d) %s' % (r.get('conversionFailuresThrown',0), r.get('conversionFailures',0), r.get('conversionLeakBytesPerFailure',0), r.get('conversionBaselineBytesPerSuccess',0), (r.get('conversionFailureError') or '')[:300]), 'rounds', r.get('conversionRounds'))
for f in r.get('lateFailures') or []: print('late failure after %-28s threw=%s clean=%s | %s' % (f['stage'], f['threw'], f['clean'], f['leaked']))
" "$A/pmx-staging.json" "$START"
