#!/bin/bash
# H1-06: autosave + crash recovery across a forced kill (taskkill /F, no normal quit).
#  1) write: open a copy of the UI-run scene, make unsaved changes, wait for the autosave; the
#     process is then killed.  2) restore: relaunch, "복구", compare, save, recovery removed.
#  3) write again + kill, then discard: relaunch, "버리기", recovery removed.
# usage: scripts/bash/run-recovery.sh <debug|release> <Mono|IL2CPP>   (run run-scene-ui.sh first)
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
R='E:\workspace\EasyDanceStudio'
A="artifacts/recovery/$BK-$CFG"; rm -rf "$A"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
PROJECT="artifacts/scene-ui/$BK/$CFG/project"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
SCENE="$R\\${PROJECT//\//\\}\\recovery-test.mmdscene"
COMMON=(-recoveryDir "$AW\\recovery" -autosaveSeconds 1 -screen-fullscreen 0 -screen-width 1280 -screen-height 720)

write_and_kill() {   # $1 = report name
  rm -rf "$A/recovery"   # each write phase starts without a pending recovery file
  cp "$PROJECT/scene.mmdscene" "$PROJECT/recovery-test.mmdscene" || exit 1
  "$EXEU" -pocRecoveryWrite "$AW\\$1.json" -sceneFile "$SCENE" -logFile "$AW\\$1.log" "${COMMON[@]}" > "$A/$1-stdout.txt" 2>&1 &
  for i in $(seq 1 240); do [ -f "$A/$1.json" ] && break; sleep 0.5; done
  sleep 0.5
  taskkill //F //IM MmdStudioPoC.exe > /dev/null 2>&1
  sleep 1
  echo "$1: report $( [ -f "$A/$1.json" ] && echo yes || echo NO ), recovery files: $(ls "$A/recovery" 2>/dev/null | tr '\n' ' ')"
}

check() {   # $1 = report name, $2 = expected report, $3 = extra flag
  timeout 300 "$EXEU" -pocRecoveryCheck "$AW\\$1.json" -pocRecoveryExpect "$AW\\$2.json" $3 -logFile "$AW\\$1.log" "${COMMON[@]}" > "$A/$1-stdout.txt" 2>&1
  echo "$1: exit $?, recovery files after: $(ls "$A/recovery" 2>/dev/null | tr '\n' ' ')"
}

write_and_kill write1
check restore write1 ""
write_and_kill write2
check discard write2 -pocRecoveryDiscard
PYTHONIOENCODING=utf-8 python -c "
import json,sys,os
for name in sys.argv[1:]:
    p=os.path.join('$A', name + '.json')
    if not os.path.exists(p): print(name, 'MISSING REPORT'); continue
    r=json.load(open(p,encoding='utf-8'))
    print('%-8s success %s %s | checks %d | %s' % (name, r['success'], r['backend'], len(r['checks']), ', '.join(r['checks'])))
    if r['error']: print('   ', r['error'][:600])
    if r.get('autosaves'): print('    autosaves %d after %.2f s' % (r['autosaves'], r['autosaveWaitSeconds']))
" write1 restore write2 discard
