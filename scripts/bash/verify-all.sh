#!/bin/bash
# H1-04: one command for the scene regression suite. Builds the V0 Player once, then runs
#   UI (143+ checks) + relaunched restore (9), scene file format (H1-05), repeated open
#   cancel/retry (H1-01) and crash recovery (H1-06), then prints one summary with the result
#   locations and checks that the build left no tracked files modified.
# Opens Player windows for several minutes (do not run while the user is gaming).
# usage: scripts/bash/verify-all.sh <debug|release> <Mono|IL2CPP> [skipbuild] [cycles=3]
set -u
cd /e/workspace/EasyDanceStudio
CFG=${1:-release}; BK=${2:-IL2CPP}; SKIP=${3:-}; CYCLES=${4:-3}
UI="artifacts/scene-ui/$BK"; [ "$CFG" = release ] && UI="$UI/release"
LOGS="artifacts/verify-all/$BK-$CFG"; rm -rf "$LOGS"; mkdir -p "$LOGS"
# What the build and Players could touch: the Unity project. Status lines plus a hash of each
# changed file's contents, so a file that was already modified and gets rewritten is caught too.
# (Edits elsewhere, e.g. docs during the run, are not the build's doing and are ignored.)
tracked_state() {
  git status --porcelain --untracked-files=no -- MMDStudio | while read -r _ file; do
    echo "$file $(git hash-object "$file" 2>/dev/null || echo deleted)"
  done
}
BEFORE=$(tracked_state)
START=$(date +%s)

step() {   # $1 = name, rest = command
  local name=$1; shift
  local t0=$(date +%s)
  "$@" > "$LOGS/$name.txt" 2>&1
  echo "$name: exit $? ($(( $(date +%s) - t0 )) s, log $LOGS/$name.txt)"
}

step ui bash scripts/bash/run-scene-ui.sh "$CFG" "$BK" $SKIP
step format bash scripts/bash/run-scene-format.sh "$CFG" "$BK"
step cycle bash scripts/bash/run-scene-cycle.sh "$CFG" "$BK" "$CYCLES"
step recovery bash scripts/bash/run-recovery.sh "$CFG" "$BK"
AFTER=$(tracked_state)

PYTHONIOENCODING=utf-8 python - "$UI" "$BK" "$CFG" "$START" "$BEFORE" "$AFTER" <<'PY'
import json, os, sys
ui, bk, cfg, start, before, after = sys.argv[1:7]
start = int(start)
rows, failed = [], False
def load(path):
    if not os.path.exists(path): return None, 'missing'
    if os.path.getmtime(path) < start: return None, 'stale (not written by this run)'
    try: return json.load(open(path, encoding='utf-8')), None
    except Exception as ex: return None, 'unreadable: %s' % ex
def add(name, path, ok, detail):
    global failed
    failed |= not ok
    rows.append(('PASS' if ok else 'FAIL', name, detail, path))
def checked(name, path, minimum=None, extra=None):
    r, problem = load(path)
    if r is None: return add(name, path, False, problem)
    count = len(r.get('checks', []))
    ok = bool(r.get('success')) and (minimum is None or count >= minimum)
    detail = '%d checks' % count + ('' if minimum is None else ' (need %d)' % minimum)
    if extra: e_ok, e_detail = extra(r); ok = ok and e_ok; detail += ', ' + e_detail
    if not r.get('success'): detail += ' | ' + (r.get('error') or '')[:300].replace('\n', ' ')
    add(name, path, ok, detail)
checked('UI', os.path.join(ui, 'scene-ui.json'), 143)
checked('restore', os.path.join(ui, 'scene-restore.json'), 9)
checked('format', 'artifacts/scene-format/%s-%s/scene-format.json' % (bk, cfg), 7)
r, problem = load('artifacts/scene-cycle/%s-%s/scene-cycle.json' % (bk, cfg))
path = 'artifacts/scene-cycle/%s-%s/scene-cycle.json' % (bk, cfg)
if r is None: add('cycle', path, False, problem)
else: add('cycle', path, bool(r['success']) and bool(r['objectCountsStable']),
          '%d cycles, object counts stable %s, mono growth %.1f MiB' % (r['cycles'], r['objectCountsStable'], r['monoGrowthMiB']) +
          ('' if r['success'] else ' | ' + r['error'][:300]))
for name in ('write1', 'restore', 'write2', 'discard'):
    checked('recovery ' + name, 'artifacts/recovery/%s-%s/%s.json' % (bk, cfg, name))
changed = sorted(set(after.splitlines()) - set(before.splitlines()))
add('tracked files', 'git status', not changed, 'unchanged' if not changed else 'modified by the run: ' + ', '.join(changed))
print()
print('=== verify-all %s %s ===' % (bk, cfg))
for status, name, detail, path in rows: print('%s  %-18s %s\n      %s' % (status, name, detail, path))
print('RESULT', 'FAIL' if failed else 'PASS')
sys.exit(1 if failed else 0)
PY
RESULT=$?
echo "total $(( $(date +%s) - START )) s · step logs in $LOGS"
exit $RESULT
