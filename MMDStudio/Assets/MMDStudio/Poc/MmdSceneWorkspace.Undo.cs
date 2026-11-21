using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // H1-03: command-level undo/redo (limit 200). Lightweight edits (placement, visibility,
    // graphics, physics/spring settings, loop/speed) are recorded as before/after snapshots and
    // undone by re-applying only the fields that differ. Adding/removing objects and connecting a
    // VMD keep the detached object or the replaced motion alive, so undo is instant and needs no
    // reload; they are released when the command leaves the history or the document changes.
    // File loading itself is not recorded.
    public sealed partial class MmdSceneWorkspace
    {
        internal const int UndoLimit = 200;

        private sealed class ObjectState
        {
            public Vector3 position, rotation, scale;
            public bool visible, physics;
            public string spring;
            public float[] strength; // per MmdPhysicsParts.Order
        }
        private sealed class LightState
        {
            public string graphics;
            public bool loop;
            public double speed;
            public float audioVolume;
            public bool audioMuted;
            public Dictionary<string, ObjectState> objects = new Dictionary<string, ObjectState>();
        }

        private abstract class UndoCommand
        {
            public string Label, ObjectId;
            public float Time;
            public abstract void Undo(MmdSceneWorkspace w);
            public abstract void Redo(MmdSceneWorkspace w);
            // Called when the command leaves the history; `applied` tells whether its effect is
            // currently in the scene (so the resources it holds for the other state can go).
            public virtual void Drop(MmdSceneWorkspace w, bool applied) { }
        }
        private sealed class StateCommand : UndoCommand
        {
            public LightState Before, After;
            public override void Undo(MmdSceneWorkspace w) => w.ApplyLight(Before);
            public override void Redo(MmdSceneWorkspace w) => w.ApplyLight(After);
        }
        private sealed class AddCommand : UndoCommand
        {
            public MmdSceneService.SceneObject Item; public int Index;
            public override void Undo(MmdSceneWorkspace w) { Index = w.Scene.Detach(Item.Id); w.documentOrder.Remove(Item.Id); }
            public override void Redo(MmdSceneWorkspace w) => w.Scene.Attach(Item, Index);
            public override void Drop(MmdSceneWorkspace w, bool applied) { if (!applied) w.Scene.ReleaseDetached(Item); }
        }
        private sealed class RemoveCommand : UndoCommand
        {
            public MmdSceneService.SceneObject Item; public int Index, OrderIndex = -1; public UnresolvedMotion Motion;
            public override void Redo(MmdSceneWorkspace w)
            {
                OrderIndex = w.documentOrder.IndexOf(Item.Id);
                Index = w.Scene.Detach(Item.Id); w.unresolvedMotions.Remove(Item.Id);
            }
            public override void Undo(MmdSceneWorkspace w)
            {
                w.Scene.Attach(Item, Index);
                if (Motion != null) w.unresolvedMotions[Item.Id] = Motion;
                if (OrderIndex >= 0 && !w.documentOrder.Contains(Item.Id)) w.documentOrder.Insert(Math.Min(OrderIndex, w.documentOrder.Count), Item.Id);
            }
            public override void Drop(MmdSceneWorkspace w, bool applied) { if (applied) w.Scene.ReleaseDetached(Item); }
        }
        private sealed class RemoveMissingCommand : UndoCommand
        {
            public UnresolvedItem Item; public int OrderIndex;
            public override void Redo(MmdSceneWorkspace w) { w.unresolved.Remove(ObjectId); w.documentOrder.Remove(ObjectId); }
            public override void Undo(MmdSceneWorkspace w)
            {
                w.unresolved[ObjectId] = Item;
                if (!w.documentOrder.Contains(ObjectId)) w.documentOrder.Insert(Math.Min(Math.Max(0, OrderIndex), w.documentOrder.Count), ObjectId);
            }
        }
        private sealed class MotionCommand : UndoCommand
        {
            public MmdPocMotion Previous, Next; public string PreviousSource, NextSource; public UnresolvedMotion PreviousMissing;
            public override void Undo(MmdSceneWorkspace w)
            {
                w.Scene.SwapMotion(ObjectId, Previous, PreviousSource);
                if (PreviousMissing != null) w.unresolvedMotions[ObjectId] = PreviousMissing;
            }
            public override void Redo(MmdSceneWorkspace w)
            {
                w.Scene.SwapMotion(ObjectId, Next, NextSource);
                w.unresolvedMotions.Remove(ObjectId);
            }
            public override void Drop(MmdSceneWorkspace w, bool applied) => w.Scene.DiscardMotion(applied ? Previous : Next);
        }

        private readonly List<UndoCommand> history = new List<UndoCommand>();
        private int historyIndex, savedIndex;
        // A change outside the history (kept for safety); cleared on save/new/open.
        private bool untrackedChange;
        private LightState gizmoBefore;
        internal int UndoCount => historyIndex;
        internal int RedoCount => history.Count - historyIndex;
        internal string UndoLabel => historyIndex > 0 ? history[historyIndex - 1].Label : null;

        // ----- snapshots -----
        private LightState CaptureLight()
        {
            var state = new LightState { graphics = JsonUtility.ToJson(Graphics.Settings), loop = Scene.Loop, speed = Scene.Speed, audioVolume = Scene.AudioVolume, audioMuted = Scene.AudioMuted };
            foreach (var item in Scene.Objects)
                state.objects[item.Id] = new ObjectState
                {
                    position = item.Placement.localPosition, rotation = item.Placement.localEulerAngles, scale = item.Placement.localScale,
                    visible = item.Visible, physics = item.PhysicsEnabled, spring = item.SpringPreset,
                    strength = MmdPhysicsParts.Order.Select(item.StrengthOf).ToArray(),
                };
            return state;
        }

        private static bool Same(ObjectState a, ObjectState b) =>
            a.position == b.position && a.rotation == b.rotation && a.scale == b.scale && a.visible == b.visible &&
            a.physics == b.physics && a.spring == b.spring && a.strength.SequenceEqual(b.strength);

        private static bool Same(LightState a, LightState b) =>
            a.graphics == b.graphics && a.loop == b.loop && a.speed == b.speed && a.audioVolume == b.audioVolume && a.audioMuted == b.audioMuted && a.objects.Count == b.objects.Count &&
            a.objects.All(entry => b.objects.TryGetValue(entry.Key, out var other) && Same(entry.Value, other));

        // Re-applies a snapshot, touching only what differs (no needless physics re-bakes).
        private void ApplyLight(LightState target)
        {
            var current = CaptureLight();
            if (current.graphics != target.graphics) Graphics.Apply(JsonUtility.FromJson<SceneGraphicsSettings>(target.graphics));
            if (current.loop != target.loop) Scene.Loop = target.loop;
            if (current.speed != target.speed) Scene.Speed = target.speed;
            if (current.audioVolume != target.audioVolume) Scene.AudioVolume = target.audioVolume;
            if (current.audioMuted != target.audioMuted) Scene.AudioMuted = target.audioMuted;
            foreach (var entry in target.objects)
            {
                if (!current.objects.TryGetValue(entry.Key, out var now)) continue;
                var want = entry.Value;
                if (now.position != want.position || now.rotation != want.rotation || now.scale != want.scale)
                    Scene.SetPlacement(entry.Key, want.position, want.rotation, want.scale);
                if (now.visible != want.visible) Scene.SetVisible(entry.Key, want.visible);
                for (var i = 0; i < MmdPhysicsParts.Order.Length; i++)
                    if (now.strength[i] != want.strength[i]) Scene.SetPhysicsStrength(entry.Key, MmdPhysicsParts.Order[i], want.strength[i]);
                if (now.physics != want.physics) Scene.SetPhysics(entry.Key, want.physics);
                if (now.spring != want.spring) Scene.SetSpring(entry.Key, want.spring);
            }
        }

        // ----- recording -----
        // Runs a user edit and records it. `coalesce` merges repeated edits of the same kind on
        // the same object within a second (slider drags, typed numbers) into one command.
        internal void Record(string label, string objectId, Action change, bool coalesce = false)
        {
            var before = CaptureLight();
            change();
            var after = CaptureLight();
            if (Same(before, after)) return;
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (coalesce && historyIndex > 0 && historyIndex == history.Count && historyIndex != savedIndex &&
                history[historyIndex - 1] is StateCommand top && top.Label == label && top.ObjectId == objectId && now - top.Time < 1f)
            {
                top.After = after; top.Time = now; UpdateDirtyFromHistory();
                return;
            }
            Push(new StateCommand { Label = label, ObjectId = objectId, Before = before, After = after, Time = now });
        }

        private void Push(UndoCommand command)
        {
            for (var i = history.Count - 1; i >= historyIndex; i--) history[i].Drop(this, false);
            if (history.Count > historyIndex) history.RemoveRange(historyIndex, history.Count - historyIndex);
            if (savedIndex > historyIndex) savedIndex = -1; // the saved state was on the dropped branch
            command.Time = command.Time == 0 ? UnityEngine.Time.realtimeSinceStartup : command.Time;
            history.Add(command); historyIndex++;
            if (history.Count > UndoLimit)
            {
                history[0].Drop(this, true); history.RemoveAt(0); historyIndex--;
                savedIndex = savedIndex > 0 ? savedIndex - 1 : -1;
            }
            UpdateDirtyFromHistory();
        }

        private void UpdateDirtyFromHistory()
        {
            Dirty = untrackedChange || historyIndex != savedIndex;
            if (Dirty) NoteDirty();
        }

        // Saved: the current history position is the clean state.
        private void MarkSaved() { savedIndex = historyIndex; untrackedChange = false; UpdateDirtyFromHistory(); }

        // New/opened document: release everything the history holds.
        private void ClearHistory()
        {
            for (var i = history.Count - 1; i >= 0; i--) history[i].Drop(this, i < historyIndex);
            history.Clear(); historyIndex = savedIndex = 0; untrackedChange = false;
        }

        internal void Undo()
        {
            if (Busy || historyIndex == 0) return;
            var command = history[--historyIndex];
            command.Undo(this);
            AfterHistoryMove("실행 취소: " + command.Label, command.ObjectId);
        }

        internal void Redo()
        {
            if (Busy || historyIndex >= history.Count) return;
            var command = history[historyIndex++];
            command.Redo(this);
            AfterHistoryMove("다시 실행: " + command.Label, command.ObjectId);
        }

        private void AfterHistoryMove(string text, string objectId)
        {
            if (objectId != null && (Scene.Objects.Any(o => o.Id == objectId) || unresolved.ContainsKey(objectId))) SelectedId = objectId;
            else if (SelectedId != null && !Scene.Objects.Any(o => o.Id == SelectedId) && !unresolved.ContainsKey(SelectedId))
                SelectedId = Scene.Objects.FirstOrDefault()?.Id;
            Error = ""; message = text;
            UpdateDirtyFromHistory();
            Refresh();
        }
    }
}
