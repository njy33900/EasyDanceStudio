using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // MVP-01: synchronous scene model, physics OFF by default; PH-03 adds per-character
    // experimental physics through a bake cache. UI/persistence are separate.
    [DefaultExecutionOrder(9000)] // Before pinned UMT's 10000 LateUpdate/SDEF pass.
    public sealed class MmdSceneService : MonoBehaviour
    {
        // Stage: static PMX background. No motion, no clock contribution (MVP-04).
        public enum ObjectKind { Character, Stage }
        public sealed class SceneObject
        {
            public string Id { get; internal set; }
            public ObjectKind Kind { get; internal set; }
            public string ModelSource { get; internal set; }
            public string MotionSource { get; internal set; }
            public PMXImportResult Model { get; internal set; }
            public MmdPocMotion Motion { get; internal set; }
            public PocAssetLifetime Owner { get; internal set; }
            public MmdPocSummary Summary { get; internal set; }
            public bool Visible { get; internal set; } = true;
            public Transform Placement => Owner.transform;
            // PH-03: experimental physics, baked ahead of playback (MmdPhysicsBake). While a time
            // is not baked yet the physics-OFF pose is shown.
            public bool PhysicsEnabled { get; internal set; }
            internal MmdPhysicsBake Bake { get; set; }
            internal bool BakeStale { get; set; }
            public float PhysicsProgress => Bake == null || BakeStale ? 0 : Bake.Progress;
            public bool PhysicsReady => Bake != null && !BakeStale && Bake.Complete;
            public string PhysicsError { get; internal set; }
            // Ticks where a visible physics bone jumped more than 0.3 m (instability warning).
            public int PhysicsJumpTicks => Bake == null || BakeStale ? 0 : Bake.JumpTicks;
            public float PhysicsFirstJumpSeconds => Bake == null ? -1 : Bake.FirstJumpSeconds;
            // PH-04: per-part strength (0..1), kept across re-bakes; parts present in the model.
            internal readonly Dictionary<string, float> PhysicsStrength = new Dictionary<string, float>();
            public string[] PhysicsParts => Bake == null ? new string[0] : Bake.Parts;
            public float StrengthOf(string part) => PhysicsStrength.TryGetValue(part, out var value) ? value : 1;
            // PH-05: chest/hip spring bones (null: off). Candidates are detected once per model.
            public string SpringPreset { get; internal set; }
            internal MmdSpringBake SpringBake { get; set; }
            internal bool SpringStale { get; set; }
            private List<MmdSpringCandidate> springCandidates;
            internal List<MmdSpringCandidate> SpringCandidates => springCandidates ?? (springCandidates = MmdSpringBones.Detect(Model));
            // Probe hook: let roots that already have rigid bodies take a spring (test models only).
            internal bool SpringForceForTest { get; set; }
            public bool SpringAvailable => Kind == ObjectKind.Character && SpringCandidates.Any(c => c.Usable || (SpringForceForTest && c.Weighted));
            public string SpringSummary => MmdSpringBones.Describe(SpringCandidates);
            public bool SpringReady => SpringBake != null && !SpringStale && SpringBake.Complete;
            public float SpringProgress => SpringBake == null || SpringStale ? 0 : SpringBake.Progress;
            // Parts with a strength slider: physics parts plus spring parts.
            public string[] MotionParts => (Bake == null ? new string[0] : Bake.Parts)
                .Union(SpringBake == null ? new string[0] : SpringBake.Parts)
                .OrderBy(p => Array.IndexOf(MmdPhysicsParts.Order, p)).ToArray();
        }
        private readonly List<SceneObject> objects = new List<SceneObject>();
        // Per-frame physics bake budget (ms). One character is baked per frame, round-robin.
        // Paused, the bake may use more of the frame (the UI stays around 30 FPS).
        public double PhysicsBudgetMilliseconds { get; set; } = 8;
        public double PausedPhysicsBudgetMilliseconds { get; set; } = 24;
        private int bakeCursor;
        private int settleThroughFrame = -1;
        private bool skipNextAdvance;
        public IReadOnlyList<SceneObject> Objects => objects.AsReadOnly();
        public double Seconds { get; private set; }
        public bool Playing { get; set; }
        public bool Loop { get; set; }
        public bool AutomaticClock { get; set; } = true;
        private double speed = 1;
        public double Speed
        {
            get => speed;
            set
            {
                if (!Finite(value) || value < .5 || value > 2) throw new ArgumentOutOfRangeException(nameof(value));
                speed = value;
            }
        }
        public double Duration
        {
            get
            {
                double duration = 0;
                foreach (var item in objects) if (item.Motion != null) duration = Math.Max(duration, item.Motion.Summary.durationSeconds);
                return duration;
            }
        }

        public SceneObject Import(string pmx, string vmd, Vector3 position) => Import(pmx, vmd, position, ObjectKind.Character, null);
        public SceneObject ImportStage(string pmx, Vector3 position) => Import(pmx, null, position, ObjectKind.Stage, null);
        public async Task<SceneObject> ImportAsync(string pmx, Vector3 position, ObjectKind kind,
            Action<string> stage, CancellationToken cancellationToken)
        {
            if (kind != ObjectKind.Character && kind != ObjectKind.Stage) throw new ArgumentOutOfRangeException(nameof(kind));
            var prepared = await MmdPocProbe.LoadAsync(pmx, stage, cancellationToken);
            var owner = prepared.imported.root.GetComponentInParent<PocAssetLifetime>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.transform.SetParent(transform, false);
                owner.transform.localPosition = position;
                prepared.imported.mmdTransformResult.transformManager.transformEnabled = false;
                var item = new SceneObject
                {
                    Id = Guid.NewGuid().ToString("N"), Kind = kind, ModelSource = Path.GetFullPath(pmx),
                    Model = prepared.imported, Owner = owner, Summary = prepared.summary
                };
                objects.Add(item);
                settleThroughFrame = Time.frameCount + 1;
                return item;
            }
            catch { owner.Release(); throw; }
        }
        // Reopening a scene file keeps its object IDs (selection, unresolved entries and later undo use them).
        public SceneObject Import(string pmx, Vector3 position, ObjectKind kind, string id) => Import(pmx, null, position, kind, id);
        private SceneObject Import(string pmx, string vmd, Vector3 position, ObjectKind kind, string id)
        {
            if (id != null && (id.Length == 0 || id.Length > 64 || objects.Exists(item => item.Id == id)))
                throw new ArgumentException("장면 항목 ID가 비어 있거나 중복되었습니다.", nameof(id));
            if (kind == ObjectKind.Stage && !string.IsNullOrEmpty(vmd)) throw new ArgumentException("배경에는 VMD를 연결할 수 없습니다.", nameof(vmd));
            PMXImportResult model = null;
            PocAssetLifetime owner = null;
            try
            {
                var summary = MmdPocProbe.Load(pmx, null, out model, false);
                owner = model.root.GetComponentInParent<PocAssetLifetime>();
                owner.transform.SetParent(transform, false);
                owner.transform.localPosition = position;
                MmdPocMotion motion = null;
                model.mmdTransformResult.transformManager.transformEnabled = false;
                if (!string.IsNullOrEmpty(vmd))
                {
                    motion = model.root.AddComponent<MmdPocMotion>();
                    motion.ShowDiagnosticControls = false;
                    motion.Load(model, vmd, "direct-default-ik");
                    motion.AcquireSceneClock(this);
                    motion.SampleFromSceneClock(this, (float)Seconds);
                }
                var item = new SceneObject { Id = id ?? Guid.NewGuid().ToString("N"), Kind = kind, ModelSource = Path.GetFullPath(pmx),
                    MotionSource = string.IsNullOrEmpty(vmd) ? null : Path.GetFullPath(vmd), Model = model, Motion = motion, Owner = owner, Summary = summary };
                objects.Add(item);
                // UMT Start performs an initial solve even with transformEnabled
                // false. Reassert source-sampled poses through the next frame.
                settleThroughFrame = Time.frameCount + 1;
                return item;
            }
            catch
            {
                if (owner != null) owner.Release();
                throw;
            }
        }

        // ----- H1-01c: transactional scene open -----
        // A scene file is assembled as candidates under a separate node, hidden and outside the
        // registry and clock, while the current scene keeps running. CommitScene swaps the whole
        // set in at once; Discard throws a candidate away (cancel or failure keeps the scene).
        public sealed class PreparedObject
        {
            public string Id;
            public ObjectKind Kind;
            public string ModelSource, MotionSource, MotionError;
            public bool Visible = true;
            public PMXImportResult Model;
            public PocAssetLifetime Owner;
            public MmdPocSummary Summary;
            public MmdPocMotion Motion;
        }
        private Transform candidateRoot;
        private Transform CandidateRoot
        {
            get
            {
                if (candidateRoot == null)
                {
                    candidateRoot = new GameObject("Scene Candidates").transform;
                    candidateRoot.SetParent(transform, false);
                }
                return candidateRoot;
            }
        }
        internal int CandidateCount => candidateRoot == null ? 0 : candidateRoot.childCount;

        // vmd may be null. A missing or failing motion does not fail the object: it is reported in
        // MotionError so the caller can keep the reference as unresolved (R0-02a).
        public async Task<PreparedObject> PrepareAsync(string pmx, string vmd, ObjectKind kind, string id,
            Vector3 position, Vector3 rotation, Vector3 scale, bool visible, Action<string> stage, CancellationToken cancellationToken)
        {
            if (kind != ObjectKind.Character && kind != ObjectKind.Stage) throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == ObjectKind.Stage && !string.IsNullOrEmpty(vmd)) throw new ArgumentException("배경에는 VMD를 연결할 수 없습니다.", nameof(vmd));
            var loaded = await MmdPocProbe.LoadAsync(pmx, stage, cancellationToken);
            var prepared = new PreparedObject
            {
                Id = id, Kind = kind, ModelSource = Path.GetFullPath(pmx), Visible = visible,
                Model = loaded.imported, Owner = loaded.imported.root.GetComponentInParent<PocAssetLifetime>(), Summary = loaded.summary,
            };
            try
            {
                var placement = prepared.Owner.transform;
                placement.SetParent(CandidateRoot, false);
                placement.localPosition = position; placement.localEulerAngles = rotation; placement.localScale = scale;
                prepared.Model.mmdTransformResult.transformManager.transformEnabled = false;
                foreach (var renderer in prepared.Model.root.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                if (!string.IsNullOrEmpty(vmd))
                {
                    if (!File.Exists(vmd)) prepared.MotionError = "모션 파일 없음";
                    else
                    {
                        var motion = prepared.Model.root.AddComponent<MmdPocMotion>();
                        motion.ShowDiagnosticControls = false;
                        try
                        {
                            await motion.LoadAsync(prepared.Model, vmd, "direct-default-ik", stage, cancellationToken);
                            prepared.Motion = motion; prepared.MotionSource = Path.GetFullPath(vmd);
                        }
                        catch (OperationCanceledException) { motion.enabled = false; Destroy(motion); throw; }
                        catch (Exception ex) { motion.enabled = false; Destroy(motion); prepared.MotionError = "모션 불러오기 실패 (" + ex.Message + ")"; }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return prepared;
            }
            catch { Discard(prepared); throw; }
        }

        public void Discard(PreparedObject prepared)
        {
            if (prepared == null) return;
            if (prepared.Motion != null) { prepared.Motion.enabled = false; Destroy(prepared.Motion); prepared.Motion = null; }
            if (prepared.Owner != null) { prepared.Owner.Release(); prepared.Owner = null; }
        }

        // Replaces the whole scene with the prepared set. Validation happens before anything is
        // cleared, so a rejected set leaves the current scene untouched.
        public void CommitScene(IReadOnlyList<PreparedObject> prepared)
        {
            var ids = new HashSet<string>();
            foreach (var item in prepared)
            {
                if (item?.Owner == null || item.Model == null) throw new ArgumentException("준비되지 않은 장면 항목이 있습니다.");
                if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) throw new ArgumentException("장면 항목 ID가 비어 있거나 중복되었습니다.");
            }
            Clear();
            foreach (var item in prepared)
            {
                item.Owner.transform.SetParent(transform, false);
                foreach (var renderer in item.Model.root.GetComponentsInChildren<Renderer>()) renderer.enabled = item.Visible;
                if (item.Motion != null) item.Motion.AcquireSceneClock(this);
                objects.Add(new SceneObject
                {
                    Id = item.Id, Kind = item.Kind, ModelSource = item.ModelSource, MotionSource = item.MotionSource,
                    Model = item.Model, Motion = item.Motion, Owner = item.Owner, Summary = item.Summary, Visible = item.Visible,
                });
            }
            Seek(0);
            settleThroughFrame = Time.frameCount + 1;
        }

        // H1-02: puts one prepared candidate into the scene at `index` (relink of a missing entry).
        internal SceneObject AdoptPrepared(PreparedObject item, int index)
        {
            if (item?.Owner == null || item.Model == null) throw new ArgumentException("준비되지 않은 장면 항목입니다.");
            if (objects.Any(o => o.Id == item.Id)) throw new ArgumentException("같은 ID의 항목이 이미 있습니다: " + item.Id);
            item.Owner.transform.SetParent(transform, false);
            foreach (var renderer in item.Model.root.GetComponentsInChildren<Renderer>()) renderer.enabled = item.Visible;
            if (item.Motion != null) item.Motion.AcquireSceneClock(this);
            var adopted = new SceneObject
            {
                Id = item.Id, Kind = item.Kind, ModelSource = item.ModelSource, MotionSource = item.MotionSource,
                Model = item.Model, Motion = item.Motion, Owner = item.Owner, Summary = item.Summary, Visible = item.Visible,
            };
            objects.Insert(Mathf.Clamp(index, 0, objects.Count), adopted);
            settleThroughFrame = Time.frameCount + 1;
            Seek(Seconds);
            return adopted;
        }

        private SceneObject Find(string id)
        {
            var item = objects.Find(value => value.Id == id);
            return item ?? throw new KeyNotFoundException("Scene object not found: " + id);
        }

        public void ReplaceMotion(string id, string path)
        {
            var item = Find(id);
            if (item.Kind == ObjectKind.Stage) throw new InvalidOperationException("배경에는 VMD를 연결할 수 없습니다.");
            var previous = item.Motion;
            MmdPocMotion candidate = null;
            try
            {
                candidate = item.Model.root.AddComponent<MmdPocMotion>();
                candidate.ShowDiagnosticControls = false;
                candidate.Load(item.Model, path, "direct-default-ik");
                candidate.AcquireSceneClock(this);
                candidate.SampleFromSceneClock(this, (float)Seconds);
            }
            catch
            {
                if (candidate != null) { candidate.enabled = false; Destroy(candidate); }
                if (previous != null) previous.SampleFromSceneClock(this, (float)Seconds);
                else item.Model.mmdTransformResult.transformManager.ResetToBindPose();
                throw;
            }
            if (previous != null)
            {
                previous.ReleaseSceneClock(this);
                previous.enabled = false;
                // H1-03: kept for undo when requested; the caller destroys it via DiscardMotion.
                if (RetainReplacedMotions) LastReplacedMotion = (previous, item.MotionSource);
                else Destroy(previous);
            }
            else if (RetainReplacedMotions) LastReplacedMotion = (null, null);
            item.Motion = candidate;
            item.MotionSource = Path.GetFullPath(path);
            if (item.PhysicsEnabled) RebuildBake(item);
            if (item.SpringPreset != null) RebuildSpring(item);
            Seek(Seconds);
        }

        public async Task ReplaceMotionAsync(string id, string path, Action<string> stage, CancellationToken cancellationToken)
        {
            var item = Find(id);
            if (item.Kind == ObjectKind.Stage) throw new InvalidOperationException("배경에는 VMD를 연결할 수 없습니다.");
            var previous = item.Motion;
            MmdPocMotion candidate = null;
            try
            {
                candidate = item.Model.root.AddComponent<MmdPocMotion>();
                candidate.ShowDiagnosticControls = false;
                await candidate.LoadAsync(item.Model, path, "direct-default-ik", stage, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                candidate.AcquireSceneClock(this);
                candidate.SampleFromSceneClock(this, (float)Seconds);
            }
            catch
            {
                if (candidate != null) { candidate.enabled = false; Destroy(candidate); }
                if (previous != null) previous.SampleFromSceneClock(this, (float)Seconds);
                else item.Model.mmdTransformResult.transformManager.ResetToBindPose();
                throw;
            }
            if (previous != null)
            {
                previous.ReleaseSceneClock(this);
                previous.enabled = false;
                // H1-03: kept for undo when requested; the caller destroys it via DiscardMotion.
                if (RetainReplacedMotions) LastReplacedMotion = (previous, item.MotionSource);
                else Destroy(previous);
            }
            else if (RetainReplacedMotions) LastReplacedMotion = (null, null);
            item.Motion = candidate;
            item.MotionSource = Path.GetFullPath(path);
            if (item.PhysicsEnabled) RebuildBake(item);
            if (item.SpringPreset != null) RebuildSpring(item);
            Seek(Seconds);
        }

        // ----- H1-03 undo support -----
        // When true, ReplaceMotion keeps the previous motion component (disabled) in
        // LastReplacedMotion instead of destroying it.
        public bool RetainReplacedMotions { get; set; }
        internal (MmdPocMotion motion, string source) LastReplacedMotion { get; private set; }

        // Takes an object out of the scene without releasing it (renderers hidden, clock released,
        // bakes kept) so it can be put back. Returns its index in the scene list.
        internal int Detach(string id)
        {
            var item = Find(id);
            var index = objects.IndexOf(item);
            objects.Remove(item);
            if (item.Motion != null) item.Motion.ReleaseSceneClock(this);
            foreach (var renderer in item.Model.root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            if (objects.Count == 0) { Playing = false; Seconds = 0; } else Seek(Seconds);
            return index;
        }

        // Puts a detached object back at its index.
        internal void Attach(SceneObject item, int index)
        {
            if (objects.Contains(item)) return;
            objects.Insert(Mathf.Clamp(index, 0, objects.Count), item);
            if (item.Motion != null) item.Motion.AcquireSceneClock(this);
            foreach (var renderer in item.Model.root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = item.Visible;
            settleThroughFrame = Time.frameCount + 1;
            Seek(Seconds);
        }

        // Releases a detached object's resources for good.
        internal void ReleaseDetached(SceneObject item)
        {
            if (objects.Contains(item)) return;
            item.Bake = null; item.SpringBake = null;
            if (item.Owner != null) item.Owner.Release();
        }

        // Swaps a character's motion component (undo/redo of a VMD connection). Returns the
        // previous one, disabled and released from the clock, for the caller to keep.
        internal MmdPocMotion SwapMotion(string id, MmdPocMotion motion, string source)
        {
            var item = Find(id);
            var previous = item.Motion;
            if (previous != null) { previous.ReleaseSceneClock(this); previous.enabled = false; }
            item.Motion = motion; item.MotionSource = motion == null ? null : source;
            if (motion != null) { motion.enabled = true; motion.AcquireSceneClock(this); }
            else item.Model.mmdTransformResult.transformManager.ResetToBindPose();
            if (item.PhysicsEnabled && motion != null) RebuildBake(item); else if (motion == null) { item.Bake = null; item.PhysicsEnabled = false; }
            if (item.SpringPreset != null && motion != null) RebuildSpring(item); else if (motion == null) { item.SpringBake = null; item.SpringPreset = null; }
            Seek(Seconds);
            return previous;
        }

        internal void DiscardMotion(MmdPocMotion motion)
        {
            if (motion == null) return;
            foreach (var item in objects) if (item.Motion == motion) return; // still in use
            motion.enabled = false; Destroy(motion);
        }

        public void Remove(string id)
        {
            var item = Find(id);
            objects.Remove(item);
            item.Bake = null; item.SpringBake = null;
            if (item.Motion != null) item.Motion.ReleaseSceneClock(this);
            item.Owner.Release();
            if (objects.Count == 0) { Playing = false; Seconds = 0; }
            else Seek(Seconds);
        }

        public void Seek(double seconds)
        {
            if (!Finite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            var time = Math.Max(0, Math.Min(seconds, Duration));
            foreach (var item in objects)
                if (item.Motion != null && (!item.Motion.OwnsSceneClock(this) || item.Motion.Playing))
                    throw new InvalidOperationException("A scene motion lost exclusive clock ownership");
            foreach (var item in objects) Show(item, time);
            Seconds = time;
        }

        private void Show(SceneObject item, double time)
        {
            if (item.Motion == null) return;
            item.Motion.SampleFromSceneClock(this, (float)time);
            if (item.Bake != null && !item.BakeStale) item.Bake.Apply((float)time);
            if (item.SpringBake != null && !item.SpringStale) item.SpringBake.Apply((float)time);
        }

        // Turns experimental physics on/off for a character. Turning it on starts a fresh bake from
        // time zero; the pose follows the bake as it advances.
        public void SetPhysics(string id, bool enabled)
        {
            var item = Find(id);
            if (enabled && (item.Kind != ObjectKind.Character || item.Motion == null))
                throw new InvalidOperationException("물리는 VMD가 연결된 캐릭터에만 켤 수 있습니다.");
            item.PhysicsEnabled = enabled;
            item.PhysicsError = null;
            if (enabled) RebuildBake(item); else { item.Bake = null; item.BakeStale = false; }
            Seek(Seconds);
        }

        private void RebuildBake(SceneObject item)
        {
            item.Bake = null; item.BakeStale = false; item.PhysicsError = null;
            if (item.Motion == null) return;
            try
            {
                item.Bake = new MmdPhysicsBake(item.Motion, item.Model, this);
                foreach (var entry in item.PhysicsStrength) item.Bake.SetStrength(entry.Key, entry.Value);
            }
            catch (Exception ex) { item.PhysicsEnabled = false; item.PhysicsError = ex.Message; Debug.LogWarning("Physics disabled: " + ex.Message); }
        }

        // PH-05: chest/hip spring bones for a character (preset id, or null to turn off).
        public void SetSpring(string id, string preset)
        {
            var item = Find(id);
            if (preset != null && Array.IndexOf(MmdSpringBones.PresetIds, preset) < 0) throw new ArgumentOutOfRangeException(nameof(preset));
            if (preset != null && (item.Motion == null || !item.SpringAvailable))
                throw new InvalidOperationException("이 캐릭터에는 신체 흔들림을 추가할 가슴·엉덩이 본이 없습니다.");
            item.SpringPreset = preset;
            RebuildSpring(item);
            Seek(Seconds);
        }

        // Applies the preset to every character that can take it; returns how many.
        public int ApplySpringToAll(string preset)
        {
            var count = 0;
            foreach (var item in objects)
                if (item.Motion != null && item.SpringAvailable) { item.SpringPreset = preset; RebuildSpring(item); count++; }
            Seek(Seconds);
            return count;
        }

        private void RebuildSpring(SceneObject item)
        {
            item.SpringBake = null; item.SpringStale = false;
            if (item.SpringPreset == null || item.Motion == null) return;
            var candidates = item.SpringCandidates
                .Select(c => new MmdSpringCandidate { Bone = c.Bone, Name = c.Name, Part = c.Part, Weighted = c.Weighted, HasRigidBody = c.HasRigidBody && !item.SpringForceForTest })
                .ToList();
            item.SpringBake = new MmdSpringBake(item.Motion, item.Model, this, candidates, item.SpringPreset);
            foreach (var entry in item.PhysicsStrength) item.SpringBake.SetStrength(entry.Key, entry.Value);
        }

        // PH-04: strength of one physics part for a character (0..1). Instant, no re-bake.
        public void SetPhysicsStrength(string id, string part, float strength)
        {
            if (Array.IndexOf(MmdPhysicsParts.Order, part) < 0) throw new ArgumentOutOfRangeException(nameof(part));
            if (float.IsNaN(strength) || strength < 0 || strength > 1) throw new ArgumentOutOfRangeException(nameof(strength), "강도는 0~100% 범위여야 합니다.");
            var item = Find(id);
            item.PhysicsStrength[part] = strength;
            item.Bake?.SetStrength(part, strength);
            item.SpringBake?.SetStrength(part, strength);
            Seek(Seconds);
        }

        internal bool PhysicsBaking
        {
            get
            {
                foreach (var item in objects)
                    if ((item.Bake != null && (item.BakeStale || !item.Bake.Complete)) || (item.SpringBake != null && (item.SpringStale || !item.SpringBake.Complete))) return true;
                return false;
            }
        }

        // One bake slice for the next character that needs it, then re-show its pose (the bake
        // moves the bones). A failure turns physics off for that character only.
        private void BakeSlice()
        {
            if (objects.Count == 0) return;
            for (var n = 0; n < objects.Count; n++)
            {
                var item = objects[(bakeCursor + n) % objects.Count];
                var physicsPending = item.Bake != null && (item.BakeStale || !item.Bake.Complete);
                var springPending = item.SpringBake != null && (item.SpringStale || !item.SpringBake.Complete);
                if (!physicsPending && !springPending) continue;
                bakeCursor = (bakeCursor + n + 1) % objects.Count;
                var budget = Playing ? PhysicsBudgetMilliseconds : PausedPhysicsBudgetMilliseconds;
                if (!physicsPending)
                {
                    // PH-05 spring bake (cheap: one physics-OFF sample per tick).
                    try
                    {
                        if (item.SpringStale) { item.SpringBake.Restart(); item.SpringStale = false; }
                        item.SpringBake.Step(budget);
                    }
                    catch (Exception ex)
                    {
                        item.SpringBake = null; item.SpringPreset = null;
                        Debug.LogWarning("Spring disabled for " + item.Id + ": " + ex.Message);
                    }
                    Show(item, Seconds);
                    return;
                }
                try
                {
                    if (item.BakeStale) { item.Bake.Restart(); item.BakeStale = false; }
                    item.Bake.Step(budget);
                }
                catch (Exception ex)
                {
                    item.Bake = null; item.PhysicsEnabled = false; item.PhysicsError = ex.Message;
                    Debug.LogWarning("Physics disabled for " + item.Id + ": " + ex.Message);
                }
                Show(item, Seconds);
                return;
            }
        }

        public void Advance(double deltaSeconds)
        {
            if (!Finite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!Playing) return;
            var duration = Duration;
            if (duration <= 0) { Playing = false; return; }
            var time = Seconds + deltaSeconds * Speed;
            if (Loop) time %= duration;
            else if (time >= duration) { time = duration; Playing = false; }
            Seek(time);
        }
        public void Stop() { Playing = false; Seek(0); }
        public void ResumeAfterInterruption(bool playing) { Playing = playing; skipNextAdvance = true; }
        public void SetVisible(string id, bool visible)
        {
            var item = Find(id);
            // Keep solver/resource owners active; disabling their roots would
            // dispose native caches while the shared clock still samples them.
            foreach (var renderer in item.Model.root.GetComponentsInChildren<Renderer>()) renderer.enabled = visible;
            item.Visible = visible;
        }
        public void SetPlacement(string id, Vector3 position, Vector3 rotation, Vector3 scale)
        {
            foreach (var value in new[] { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, scale.x, scale.y, scale.z })
                if (!Finite(value)) throw new ArgumentOutOfRangeException("placement");
            if (scale.x < .001f || scale.y < .001f || scale.z < .001f || scale.x > 1000 || scale.y > 1000 || scale.z > 1000)
                throw new ArgumentOutOfRangeException("scale", "크기는 0.001~1000 범위여야 합니다.");
            var item = Find(id);
            var placement = item.Placement;
            var changed = placement.localPosition != position || placement.localEulerAngles != rotation || placement.localScale != scale;
            placement.localPosition = position;
            placement.localEulerAngles = rotation;
            placement.localScale = scale;
            // Physics runs in world space (gravity, ground), so a moved character is re-baked.
            if (changed && item.Bake != null) item.BakeStale = true;
            if (changed && item.SpringBake != null) item.SpringStale = true;
            Seek(Seconds);
        }
        public void Clear()
        {
            foreach (var item in objects)
            {
                item.Bake = null; item.SpringBake = null;
                if (item.Motion != null) item.Motion.ReleaseSceneClock(this);
                if (item.Owner != null) item.Owner.Release();
            }
            objects.Clear();
            Playing = false;
            Seconds = 0;
        }
        private void OnDestroy() { Clear(); }
        private void Update()
        {
            if (Time.frameCount <= settleThroughFrame) Seek(Seconds);
            if (skipNextAdvance) skipNextAdvance = false;
            else if (AutomaticClock) Advance(Time.unscaledDeltaTime);
            if (AutomaticClock) BakeSlice();
        }
        // Probe hook: one bake slice without the automatic clock.
        internal void BakePhysicsSlice() => BakeSlice();
        private void LateUpdate()
        {
            // Also cover imports issued after this component's Update, before
            // UMT reconciles SDEF and the renderer consumes the pose.
            if (Time.frameCount <= settleThroughFrame) Seek(Seconds);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
