using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // H1-02: relink a missing (unresolved) PMX entry to a new file. The entry keeps its ID,
    // placement, visibility, motion path and physics settings; a motion that is still missing
    // stays an unresolved motion. Undoable (the relinked object is detached, not reloaded).
    public sealed partial class MmdSceneWorkspace
    {
        private sealed class RelinkCommand : UndoCommand
        {
            public MmdSceneService.SceneObject Item; public int Index; public UnresolvedItem Missing; public UnresolvedMotion MotionMissing;
            public override void Undo(MmdSceneWorkspace w)
            {
                Index = w.Scene.Detach(ObjectId);
                w.unresolved[ObjectId] = Missing; w.unresolvedMotions.Remove(ObjectId);
            }
            public override void Redo(MmdSceneWorkspace w)
            {
                w.Scene.Attach(Item, Index);
                w.unresolved.Remove(ObjectId);
                if (MotionMissing != null) w.unresolvedMotions[ObjectId] = MotionMissing;
            }
            public override void Drop(MmdSceneWorkspace w, bool applied) { if (!applied) w.Scene.ReleaseDetached(Item); }
        }

        private void PickRelink()
        {
            if (Busy) return;
            var missing = SelectedUnresolved;
            if (missing == null) return;
            var playing = Scene.Playing; Scene.Playing = false;
            string path;
            try { path = FileDialogs.OpenModel(); }
            catch (Exception ex) { Error = ex.Message; Refresh(); return; }
            finally { Scene.ResumeAfterInterruption(playing); }
            if (string.IsNullOrEmpty(path)) { message = "파일 선택을 취소했습니다."; Error = ""; Refresh(); return; }
            StartCoroutine(RelinkMissing(missing.Data.id, path));
        }

        internal IEnumerator RelinkMissing(string id, string pmxPath)
        {
            if (Busy) yield break;
            if (!unresolved.TryGetValue(id, out var missing)) { Error = "다시 연결할 누락 항목이 없습니다."; Refresh(); yield break; }
            var pmx = Path.GetFullPath(pmxPath);
            if (!File.Exists(pmx) || !string.Equals(Path.GetExtension(pmx), ".pmx", StringComparison.OrdinalIgnoreCase))
            { Error = "PMX 파일을 선택하세요: " + Path.GetFileName(pmx); Refresh(); yield break; }
            var data = missing.Data;
            var kind = data.type == SceneObjectData.StageType ? MmdSceneService.ObjectKind.Stage : MmdSceneService.ObjectKind.Character;
            var vmd = string.IsNullOrEmpty(data.motionPath) ? null : MmdSceneDocument.Resolve(missing.BaseScene, data.motionPath);
            var playing = Scene.Playing;
            Busy = true; Error = ""; message = "누락 항목을 다시 연결하는 중…"; Scene.Playing = false; Refresh();
            loadCancellation = new System.Threading.CancellationTokenSource();
            RefreshLoadingCard();
            var task = Scene.PrepareAsync(pmx, vmd, kind, data.id, data.position, data.rotationEuler, data.scale, data.visible, stage =>
            {
                if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                message = "다시 연결하는 중 · " + stage; UpdateStatus(); RefreshLoadingCard();
            }, loadCancellation.Token);
            while (!task.IsCompleted) yield return null;
            try
            {
                var prepared = task.GetAwaiter().GetResult();
                // Keep the document order: insert after the live objects that precede it.
                var position = documentOrder.IndexOf(id);
                var index = position < 0 ? Scene.Objects.Count : Scene.Objects.Count(o => { var p = documentOrder.IndexOf(o.Id); return p >= 0 && p < position; });
                var item = Scene.AdoptPrepared(prepared, index);
                unresolved.Remove(id);
                UnresolvedMotion motionMissing = null;
                if (prepared.MotionError != null)
                    unresolvedMotions[id] = motionMissing = new UnresolvedMotion { Stored = data.motionPath, BaseScene = missing.BaseScene, Reason = prepared.MotionError };
                var physics = data.physics ?? new PhysicsData();
                foreach (var entry in physics.strength ?? new List<PartStrength>()) Scene.SetPhysicsStrength(id, entry.part, Mathf.Clamp01(entry.value));
                if (physics.enabled && item.Motion != null) Scene.SetPhysics(id, true);
                if (!string.IsNullOrEmpty(physics.spring) && item.Motion != null && item.SpringAvailable) Scene.SetSpring(id, physics.spring);
                if (data.material != null && data.material.style != MaterialStyleData.Original) ApplyStoredMaterial(id, data.material, null);
                Push(new RelinkCommand { Label = "파일 다시 연결", ObjectId = id, Item = item, Index = index, Missing = missing, MotionMissing = motionMissing });
                SelectedId = id; Target = InspectorTarget.Object;
                message = "다시 연결했습니다: " + Path.GetFileName(pmx) + (motionMissing != null ? " · VMD는 아직 누락입니다." : "");
            }
            catch (OperationCanceledException) { Error = ""; message = "다시 연결을 취소했습니다."; }
            catch (Exception ex) { Error = "다시 연결하지 못했습니다: " + ex.Message + " · 누락 항목은 그대로 남아 있습니다."; }
            finally
            {
                loadCancellation?.Dispose(); loadCancellation = null;
                Busy = false; Scene.ResumeAfterInterruption(playing); Refresh(); RefreshLoadingCard();
            }
        }
    }
}
