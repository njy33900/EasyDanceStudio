using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // MVP-01: synchronous, physics-OFF scene model. UI/persistence are separate.
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
        }
        private readonly List<SceneObject> objects = new List<SceneObject>();
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
                Destroy(previous);
            }
            item.Motion = candidate;
            item.MotionSource = Path.GetFullPath(path);
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
                Destroy(previous);
            }
            item.Motion = candidate;
            item.MotionSource = Path.GetFullPath(path);
            Seek(Seconds);
        }

        public void Remove(string id)
        {
            var item = Find(id);
            objects.Remove(item);
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
            foreach (var item in objects) if (item.Motion != null) item.Motion.SampleFromSceneClock(this, (float)time);
            Seconds = time;
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
            var placement = Find(id).Placement;
            placement.localPosition = position;
            placement.localEulerAngles = rotation;
            placement.localScale = scale;
            Seek(Seconds);
        }
        public void Clear()
        {
            foreach (var item in objects)
            {
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
        }
        private void LateUpdate()
        {
            // Also cover imports issued after this component's Update, before
            // UMT reconciles SDEF and the renderer consumes the pose.
            if (Time.frameCount <= settleThroughFrame) Seek(Seconds);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
