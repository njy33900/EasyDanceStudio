#!/bin/bash
# Bash equivalent of scripts/Invoke-ScenePerformance.ps1. usage: scripts/bash/run-scene-perf.sh <debug|release> <Mono|IL2CPP>
set -u
cd /e/workspace/EasyDanceStudio
CFG=$1; BK=$2
R='E:\workspace\EasyDanceStudio'
A="artifacts/performance/$BK-$CFG"; mkdir -p "$A"; AW="$R\\${A//\//\\}"
EXEU="./out/$CFG/V0-$BK/MmdStudioPoC.exe"
python -I scripts/New-StageFixture.py --output "$A/fixtures" > /dev/null || exit 1
START=$(date +%s)
timeout 600 "$EXEU" -pocScenePerfReport "$AW\\performance.json" \
  -pocPmx "$R\\test\\models\\Gene\\Gene_light.pmx" -pocSecondPmx "$R\\test\\Sandrone_Runtime\\桑多涅_目齿轮可动.pmx" \
  -pocVmd "$R\\test\\aespa - Whiplash\\aespa - Whiplash\\Whiplash [SMOOTH].vmd" -pocStagePmx "$AW\\fixtures\\stage.pmx" \
  -logFile "$AW\\performance.log" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 > "$A/stdout.txt" 2>&1
echo "PERF_EXIT=$?"
python -c "
import json,os,sys
p=sys.argv[1]; r=json.load(open(p,encoding='utf-8'))
print('fresh',os.path.getmtime(p)>=int(sys.argv[2]),'success',r['success'],r['backend'],r['configuration'],r['error'][:600])
print('gpu',r['gpu'],'| cpu',r['cpu'],'| screen',r['screenWidth'],'x',r['screenHeight'],'| vsync',r['vSyncCount'])
print('import s: gene %.2f sandrone %.2f motions %.2f stage %.2f | save %.3f open %.2f | duration %.1f s' % (r['importFirstCharacterSeconds'],r['importSecondCharacterSeconds'],r['connectMotionsSeconds'],r['importStageSeconds'],r['saveSeconds'],r['openSceneSeconds'],r['motionDurationSeconds']))
for x in r['passes'] or []: print('  %-28s avg %7.1f fps  median %6.2f ms  p95 %6.2f  p99 %6.2f  worst %6.1f  1%%low %6.1f fps  gc0 %d  frames %d  >=30 %s' % (x['name'],x['averageFps'],x['medianFrameMs'],x['p95FrameMs'],x['p99FrameMs'],x['worstFrameMs'],x['onePercentLowFps'],x['gen0Collections'],x['frames'],x['meetsThirtyFpsTarget']))
ps={x['name']:x for x in r['passes'] or []}
if 'effects_on_shadows_medium' in ps:
  base=ps['effects_on_shadows_medium']['medianFrameMs']
  for n in ('effects_on_dof_ssao_vignette','effects_on_dof_ssao_vignette_high'):
    if n in ps: print('  decision 9: %s median %.2f ms vs reference %.2f ms = %+.0f%% (limit +50%%)' % (n, ps[n]['medianFrameMs'], base, (ps[n]['medianFrameMs']/base-1)*100))
rc={x['name']:x['msPerRender'] for x in r.get('renderCosts') or []}
for n,v in rc.items(): print('  render 1920x1080 %-40s %6.2f ms' % (n, v))
if 'reference_bloom_tonemap_shadows_medium' in rc:
  base=rc['reference_bloom_tonemap_shadows_medium']
  for n in ['plus_dof_ssao_vignette','plus_dof_ssao_vignette_high','plus_dof_ssao_vignette_low'] + [k for k in rc if k.startswith('look_')]:
    if n in rc: print('  decision 9 (render cost): %s %+.0f%% vs reference (limit +50%%)' % (n, (rc[n]/base-1)*100))
if r.get('exportFrames'): print('  export 1080p60 MP4: %d frames in %.1f s (%.1f frames/s), %s, audio %s, %.1f MB' % (r['exportFrames'], r['exportSeconds'], r['exportFramesPerSecond'], r['exportCodec'], r['exportAudio'], r['exportBytes']/2**20))
elif r.get('exportError'): print('  export failed:', r['exportError'])
print('memory MB: allocated %.0f reserved %.0f mono %.0f workingset %.0f' % (r['allocatedBytes']/2**20, r['reservedBytes']/2**20, r['monoUsedBytes']/2**20, r['workingSetBytes']/2**20))
print('notes', r['notes'])" "$A/performance.json" "$START"
