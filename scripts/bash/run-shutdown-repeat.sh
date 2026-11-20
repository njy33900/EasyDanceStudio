#!/bin/bash
# Repeats the legacy morph/IK deformation probe (the run that once crashed during shutdown with
# -1073741819) and records each exit code. usage: scripts/bash/run-shutdown-repeat.sh <debug|release> <Mono|IL2CPP> <count>
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2; N=$3
R='E:\workspace\EasyDanceStudio'
A="artifacts/shutdown/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
ok=0; crash=0; other=0
for i in $(seq 1 $N); do
  timeout 180 "$EXEU" -batchmode -pocPmx "$R\\test\\models\\Gene\\Gene_light.pmx" \
    -pocDeformationReport "$AW\\deformation-$i.json" -logFile "$AW\\deformation-$i.log" > /dev/null 2>&1
  code=$?
  success=$(python -c "import json,sys;print(json.load(open(sys.argv[1],encoding='utf-8'))['success'])" "$A/deformation-$i.json" 2>/dev/null)
  echo "run $i exit=$code report_success=$success"
  if [ $code -eq 0 ] && [ "$success" = True ]; then ok=$((ok+1)); elif [ $code -eq 197 ] || [ $code -eq 3221225477 ]; then crash=$((crash+1)); else other=$((other+1)); fi
done
echo "SUMMARY $BK-$CFG runs=$N ok=$ok access_violation=$crash other=$other"
