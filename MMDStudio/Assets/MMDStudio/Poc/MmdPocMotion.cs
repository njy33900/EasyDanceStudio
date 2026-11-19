using System;
using System.Collections.Generic;
using System.IO;
using UMT;
using UnityEngine;
using UnityEngine.Playables;

namespace MmdStudio.Poc
{
    [Serializable]
    public sealed class MmdMotionSummary
    {
        public string source;
        public string mode;
        public double conversionMilliseconds;
        public long unityAllocatedBytesBeforeConversion;
        public long unityAllocatedBytesAfterConversion;
        public long processPeakWorkingSetBytes;
        public string processPeakWorkingSetStatus;
        public int boneKeyframes;
        public int morphKeyframes;
        public int cameraKeyframes;
        public int mappedBoneTracks;
        public int morphBindings;
        public int ikBindings;
        public string[] missingBones;
        public string[] missingMorphs;
        public string[] disabledUnaddressedIk;
        public float durationSeconds;
        public int changedBones;
        public int rotatedBones;
        public int sourceRotationChecks;
        public float maxSourceRotationErrorDegrees;
        public float realtimeAdvancedSeconds;
        public bool pausePassed;
        public bool loopPassed;
        public bool seekRepeatPassed;
        public string[] poseScreenshots;
        public MmdPoseSnapshot[] poses;
        public MmdConstraintDiagnostic[] constraints;
        public MmdFkPose[] fkSweep;
    }

    [Serializable]
    public sealed class MmdFkPose
    {
        public float seconds;
        public MmdFkSample[] sourceCurveRotations;
    }

    [Serializable]
    public sealed class MmdConstraintDiagnostic
    {
        public string name;
        public string target;
        public string flags;
        public float influence;
        public bool rotatable;
        public bool hasRotationCurve;
        public bool hasPositionCurve;
    }

    [Serializable]
    public sealed class MmdPoseSnapshot
    {
        public float seconds;
        public MmdBonePose[] bones;
        public float[] morphWeights;
        public MmdFkSample[] sourceCurveRotations;
    }

    [Serializable]
    public sealed class MmdBonePose
    {
        public string name;
        public Vector3 worldPosition;
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    [Serializable]
    public sealed class MmdFkSample
    {
        public string name;
        public Quaternion rotation;
    }

    // Diagnostic curve player. UMT performs parsing, interpolation conversion and
    // constraint/IK solving; this component binds the returned runtime curves.
    public sealed class MmdPocMotion : MonoBehaviour
    {
        private PMXImportResult model;
        private VMDModelClipData clip;
        private Transform[] targets;
        private SkinnedMeshRenderer[] morphTargets;
        private SkinnedMeshRenderer[] allMorphRenderers;
        private int[] morphIndices;
        private MMDBoneTransform[] ikTargets;
        private bool[] initialIk;
        private bool bakedReference;
        private bool skipNextAdvance;
        private MmdDirectBoneTracks directTracks;
        private PlayableGraph referenceGraph;
        private ScriptPlayable<UMTDemo.VMDModelPlayableBehaviour> referencePlayable;
        public MmdMotionSummary Summary { get; private set; }
        public bool Playing { get; set; }
        public bool ShowDiagnosticControls { get; set; } = true;
        public float TimeSeconds { get; private set; }
        internal int ClipRevision { get; private set; }
        private object physicsOwner;
        private bool statefulSample;
        internal void AcquirePhysics(object owner)
        {
            if (physicsOwner != null) throw new InvalidOperationException("Another physics session already owns this motion");
            physicsOwner = owner;
        }
        internal bool OwnsPhysics(object owner) => ReferenceEquals(physicsOwner, owner);
        internal void ReleasePhysics(object owner) { if (OwnsPhysics(owner)) physicsOwner = null; }

        public void Load(PMXImportResult imported, string path, string mode = "sparse")
        {
            ClipRevision++;
            physicsOwner = null;
            if (mode != "sparse" && mode != "sparse-default-ik" && mode != "baked-reference" &&
                mode != "direct" && mode != "direct-default-ik")
                throw new ArgumentException("Unknown -pocMotionMode: " + mode);
            bakedReference = mode == "baked-reference";
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists || !string.Equals(file.Extension, ".vmd", StringComparison.OrdinalIgnoreCase) ||
                file.Length < 50 || file.Length > 32L * 1024 * 1024)
                throw new InvalidDataException("VMD is missing, too small, or exceeds the 32 MiB probe limit.");
            model = imported;
            var addressedIk = new HashSet<string>(StringComparer.Ordinal);
            var rotationReferences = new List<RotationReference>();
            VMDAnimation animation = null;
            try
            {
                using (var stream = File.OpenRead(file.FullName)) animation = VMDReader.Read(stream);
                uint lastFrame = 0;
                var sourceBones = new HashSet<string>(StringComparer.Ordinal);
                var sourceMorphs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var frame in animation.boneFrames)
                {
                    lastFrame = Math.Max(lastFrame, frame.frame);
                    sourceBones.Add(frame.boneName.ToString());
                    addressedIk.Add(frame.boneName.ToString());
                    var name = frame.boneName.ToString();
                    if ((frame.frame == 0 || frame.frame == 450 || frame.frame == 900) &&
                        (name == "左腕" || name == "右腕" || name == "左ひじ" || name == "右ひじ"))
                    {
                        for (var i = 0; i < model.model.bones.Length; i++)
                        {
                            if (model.model.bones[i].originalName.ToString() != name) continue;
                            var q = frame.rotation.value;
                            rotationReferences.Add(new RotationReference
                            {
                                bone = i, seconds = frame.frame / 30f,
                                rotation = Quaternion.Normalize(new Quaternion(-q.x, q.y, -q.z, q.w)) * model.mmdTransformResult.transformManager.bones[i].initialLocalRotation
                            });
                            break;
                        }
                    }
                }
                foreach (var frame in animation.morphFrames)
                {
                    lastFrame = Math.Max(lastFrame, frame.frame);
                    sourceMorphs.Add(frame.morphName.ToString());
                }
                foreach (var frame in animation.showIKFrames)
                {
                    lastFrame = Math.Max(lastFrame, frame.frame);
                    foreach (var toggle in frame.ikToggles) addressedIk.Add(toggle.boneName.ToString());
                }
                if (animation.boneFrames.Length == 0 || lastFrame == 0 || lastFrame > 18000)
                    throw new InvalidDataException("Expected a character VMD of at most 18,000 frames; camera-only motion is unsupported here.");
                var modelBones = new HashSet<string>(StringComparer.Ordinal);
                var modelMorphs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var bone in model.model.bones) modelBones.Add(bone.originalName.ToString());
                foreach (var morph in model.model.morphs) modelMorphs.Add(morph.originalName.ToString());
                Summary = new MmdMotionSummary
                {
                    source = file.FullName,
                    mode = mode,
                    boneKeyframes = animation.boneFrames.Length,
                    morphKeyframes = animation.morphFrames.Length,
                    cameraKeyframes = animation.cameraFrames.Length,
                    missingBones = Missing(sourceBones, modelBones),
                    missingMorphs = Missing(sourceMorphs, modelMorphs),
                    durationSeconds = lastFrame / 30f
                };
                Summary.unityAllocatedBytesBeforeConversion = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                var conversionWatch = System.Diagnostics.Stopwatch.StartNew();
                clip = VMDAnimationClipConverter.Convert(animation, model.model, new VMDAnimationClipOptions
                {
                    frameRate = 30,
                    bakeIKToFK = bakedReference,
                    bakePhysicsToFK = false
                });
                if (mode == "direct" || mode == "direct-default-ik") directTracks = new MmdDirectBoneTracks(animation, model);
                conversionWatch.Stop();
                Summary.conversionMilliseconds = conversionWatch.Elapsed.TotalMilliseconds;
                Summary.unityAllocatedBytesAfterConversion = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
#if ENABLE_IL2CPP
                Summary.processPeakWorkingSetStatus = "Unavailable: IL2CPP does not support process data.";
#else
                try
                {
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                        Summary.processPeakWorkingSetBytes = process.PeakWorkingSet64;
                    Summary.processPeakWorkingSetStatus = Summary.processPeakWorkingSetBytes > 0 ? "Available" : "Unavailable: runtime returned zero.";
                }
                catch (NotSupportedException) { Summary.processPeakWorkingSetStatus = "Unavailable on this runtime."; }
#endif
            }
            finally { if (animation != null) Destroy(animation); }

            var manager = model.mmdTransformResult.transformManager;
            manager.livePhysics = false;
            manager.transformEnabled = false; // We explicitly solve once after sampling.
            if (manager.animator != null) manager.animator.enabled = false;
            initialIk = new bool[manager.bones.Length];
            var disabledIk = new List<string>();
            for (var i = 0; i < initialIk.Length; i++)
            {
                var bone = manager.bones[i];
                var hasIk = (model.model.bones[i].flags & PMXBone.Flags.IK) != 0;
                initialIk[i] = hasIk && ((mode != "sparse" && mode != "direct") || addressedIk.Contains(bone.boneName));
                // Gene has extra arm IK controllers absent from standard FK VMDs.
                // Leave only controllers addressed by a source track/toggle active;
                // otherwise their rest targets would override the animated arms.
                if (hasIk && !initialIk[i]) disabledIk.Add(bone.boneName);
            }
            Summary.disabledUnaddressedIk = disabledIk.ToArray();

            targets = new Transform[clip.bones.paths.Length];
            for (var i = 0; i < targets.Length; i++)
            {
                var pathToBone = clip.bones.paths[i];
                if (pathToBone == null) continue;
                targets[i] = Resolve(pathToBone);
                Summary.mappedBoneTracks++;
            }
            morphTargets = new SkinnedMeshRenderer[clip.morphs.paths.Length];
            morphIndices = new int[morphTargets.Length];
            for (var i = 0; i < morphTargets.Length; i++)
            {
                morphTargets[i] = Resolve(clip.morphs.paths[i]).GetComponent<SkinnedMeshRenderer>();
                if (morphTargets[i] == null) throw new InvalidOperationException("Morph renderer binding failed.");
                morphIndices[i] = morphTargets[i].sharedMesh.GetBlendShapeIndex(clip.morphs.names[i]);
                if (morphIndices[i] < 0) throw new InvalidOperationException("Morph name binding failed: " + clip.morphs.names[i]);
            }
            Summary.morphBindings = morphTargets.Length;
            allMorphRenderers = model.root.GetComponentsInChildren<SkinnedMeshRenderer>();
            ikTargets = new MMDBoneTransform[clip.ikToggles == null ? 0 : clip.ikToggles.paths.Length];
            for (var i = 0; i < ikTargets.Length; i++)
            {
                ikTargets[i] = Resolve(clip.ikToggles.paths[i]).GetComponent<MMDBoneTransform>();
                if (ikTargets[i] == null) throw new InvalidOperationException("IK binding failed.");
            }
            Summary.ikBindings = ikTargets.Length;
            if (Summary.mappedBoneTracks == 0) throw new InvalidDataException("VMD has no matching bone tracks.");
            foreach (var reference in bakedReference ? new List<RotationReference>() : rotationReferences)
            {
                var channel = reference.bone * 6 + 3;
                var euler = Vector3.zero;
                for (var axis = 0; axis < 3; axis++)
                {
                    var curve = clip.bones.curves[channel + axis];
                    if (curve != null) euler[axis] = curve.Evaluate(reference.seconds) * Mathf.Rad2Deg;
                }
                var sampledRotation = directTracks == null ? Quaternion.Euler(euler) : directTracks.Rotation(reference.bone, reference.seconds);
                var error = Quaternion.Angle(reference.rotation, sampledRotation);
                Summary.maxSourceRotationErrorDegrees = Mathf.Max(Summary.maxSourceRotationErrorDegrees, error);
                Summary.sourceRotationChecks++;
            }
            if (Summary.maxSourceRotationErrorDegrees > 0.1f)
                throw new InvalidOperationException("Converted rotation disagrees with original VMD quaternion keys.");
            var diagnostics = new List<MmdConstraintDiagnostic>();
            for (var i = 0; i < model.model.bones.Length; i++)
            {
                var bone = model.model.bones[i];
                if ((bone.flags & (PMXBone.Flags.RotationConstraint | PMXBone.Flags.TranslationConstraint)) == 0) continue;
                var c = i * (bakedReference ? 7 : 6);
                diagnostics.Add(new MmdConstraintDiagnostic
                {
                    name = bone.originalName.ToString(), flags = bone.flags.ToString(), influence = bone.constraintInfluence,
                    target = bone.constraintTargetIndex < 0 ? "" : model.model.bones[bone.constraintTargetIndex].originalName.ToString(),
                    rotatable = (bone.flags & PMXBone.Flags.Rotatable) != 0,
                    hasRotationCurve = clip.bones.curves[c + 3] != null,
                    hasPositionCurve = clip.bones.curves[c] != null
                });
            }
            Summary.constraints = diagnostics.ToArray();
            if (bakedReference)
            {
                referenceGraph = PlayableGraph.Create("Official UMTDemo reference");
                referenceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                referencePlayable = ScriptPlayable<UMTDemo.VMDModelPlayableBehaviour>.Create(referenceGraph);
                referencePlayable.GetBehaviour().clipData = clip;
                referencePlayable.GetBehaviour().root = model.root.transform;
                var output = ScriptPlayableOutput.Create(referenceGraph, "Model pose");
                output.SetSourcePlayable(referencePlayable);
                referenceGraph.Play();
            }
            Sample(0);
        }

        private struct RotationReference
        {
            public int bone;
            public float seconds;
            public Quaternion rotation;
        }

        private Transform Resolve(string path)
        {
            var target = path.Length == 0 ? model.root.transform : model.root.transform.Find(path);
            if (target == null) throw new InvalidOperationException("Animation binding missing: " + path);
            return target;
        }

        private static string[] Missing(HashSet<string> requested, HashSet<string> available)
        {
            var result = new List<string>();
            foreach (var name in requested) if (!available.Contains(name)) result.Add(name);
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private void Update()
        {
            if (skipNextAdvance) { skipNextAdvance = false; return; }
            if (Playing && clip != null) Advance(Time.unscaledDeltaTime);
        }

        public void ResumeAfterInterruption(bool playing)
        {
            Playing = playing;
            skipNextAdvance = true;
        }

        private void OnGUI()
        {
            if (Summary == null || Application.isBatchMode || !ShowDiagnosticControls) return;
            GUILayout.BeginArea(new Rect(12, 12, 440, 146), GUI.skin.box);
            GUILayout.Label("MMD PoC / " + Summary.mode + " / physics & audio OFF");
            GUILayout.Label(Path.GetFileName(Summary.source));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Playing ? "Pause" : "Play")) Playing = !Playing;
            if (GUILayout.Button("Stop")) { Playing = false; Sample(0); }
            GUILayout.Label(TimeSeconds.ToString("F1") + " / " + Summary.durationSeconds.ToString("F1") + " sec");
            GUILayout.EndHorizontal();
            var seek = GUILayout.HorizontalSlider(TimeSeconds, 0, Summary.durationSeconds);
            if (Mathf.Abs(seek - TimeSeconds) > 0.02f) Sample(seek);
            GUILayout.Label("Unmapped: " + Summary.missingBones.Length + " bones, " + Summary.missingMorphs.Length + " morphs");
            GUILayout.EndArea();
        }

        public void Advance(float deltaSeconds)
        {
            Sample(Mathf.Repeat(TimeSeconds + deltaSeconds, Summary.durationSeconds));
        }

        public void Sample(float seconds)
        {
            SampleInternal(seconds, null);
        }

        // Diagnostic fixed-step entry point. UI playback continues to use the
        // physics-OFF Sample method until a user-facing seek policy is ready.
        public void SampleWithPhysics(float seconds, float deltaSeconds)
        {
            if (bakedReference) throw new InvalidOperationException("Fixed-step physics requires runtime direct/sparse mode.");
            if (deltaSeconds < 0 || deltaSeconds > 1f/30 || float.IsNaN(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            SampleInternal(seconds, deltaSeconds);
        }

        // Mono-only diagnostic hook; production playback leaves this null.
        internal Action<MMDTransformManager, float> DiagnosticPhysicsSolve;

        internal void SampleStatefulPhysics(float seconds, float deltaSeconds, MmdStatefulPhysicsSolver solver)
        {
            if (bakedReference || directTracks == null) throw new InvalidOperationException("Stateful physics requires direct VMD mode");
            if (DiagnosticPhysicsSolve != null) throw new InvalidOperationException("Diagnostic override already owns the solve");
            DiagnosticPhysicsSolve = (manager, dt) => solver.Solve(dt);
            statefulSample = true;
            try { SampleWithPhysics(seconds, deltaSeconds); }
            finally { DiagnosticPhysicsSolve = null; statefulSample = false; }
        }

        private void SampleInternal(float seconds, float? physicsDelta)
        {
            if (physicsOwner != null && !statefulSample)
                throw new InvalidOperationException("A physics session owns this pose; use its seek/advance methods");
            TimeSeconds = Mathf.Clamp(seconds, 0, Summary.durationSeconds);
            // A replacement clip owns the complete expression pose. Channels
            // absent from that clip must not retain the previous clip's weights.
            foreach (var renderer in allMorphRenderers)
                for (var index = 0; index < renderer.sharedMesh.blendShapeCount; index++)
                    renderer.SetBlendShapeWeight(index, 0);
            var manager = model.mmdTransformResult.transformManager;
            for (var i = 0; i < manager.bones.Length; i++)
            {
                var bone = manager.bones[i];
                bone.transform.localPosition = bone.initialLocalPosition;
                bone.transform.localRotation = bone.initialLocalRotation;
                bone.ikEnabled = initialIk[i];
                bone.solverResetPending = true;
            }
            if (bakedReference)
            {
                // The official behaviour already contains baked constraints/IK.
                // Never apply the live IK solver again to this pose.
                referencePlayable.SetTime(TimeSeconds);
                referenceGraph.Evaluate(0);
                return;
            }
            if (directTracks != null) directTracks.Apply(TimeSeconds);
            else for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target == null) continue;
                var p = target.localPosition;
                // UMT 0.5.1 BuildSparseBoneKeyframes writes math.Euler radians,
                // despite the clip-data description naming localEulerAnglesRaw.
                var e = target.localEulerAngles * Mathf.Deg2Rad;
                var c = i * 6;
                target.localPosition = new Vector3(Eval(c, p.x), Eval(c + 1, p.y), Eval(c + 2, p.z));
                target.localRotation = Quaternion.Euler(new Vector3(
                    Eval(c + 3, e.x), Eval(c + 4, e.y), Eval(c + 5, e.z)) * Mathf.Rad2Deg);
            }
            for (var i = 0; i < morphTargets.Length; i++)
                morphTargets[i].SetBlendShapeWeight(morphIndices[i], clip.morphs.curves[i].Evaluate(TimeSeconds));
            for (var i = 0; i < ikTargets.Length; i++)
                ikTargets[i].ikEnabled = clip.ikToggles.curves[i].Evaluate(TimeSeconds) >= 0.5f;
            if (physicsDelta.HasValue && DiagnosticPhysicsSolve != null) DiagnosticPhysicsSolve(manager, physicsDelta.Value);
            else if (physicsDelta.HasValue) manager.SolveWithPhysics(true, physicsDelta.Value, true);
            else manager.SolveTransforms(true);
        }

        private float Eval(int channel, float fallback)
        {
            var curve = clip.bones.curves[channel];
            return curve == null ? fallback : curve.Evaluate(TimeSeconds);
        }

        public Vector3[] PosePositions()
        {
            var result = new Vector3[model.bones.Length];
            for (var i = 0; i < result.Length; i++) result[i] = model.bones[i].position;
            return result;
        }

        public Quaternion[] PoseRotations()
        {
            var result = new Quaternion[model.bones.Length];
            for (var i = 0; i < result.Length; i++) result[i] = model.bones[i].localRotation;
            return result;
        }

        public MmdPoseSnapshot Snapshot()
        {
            var result = new MmdPoseSnapshot
            {
                seconds = TimeSeconds,
                bones = new MmdBonePose[model.bones.Length],
                morphWeights = new float[morphTargets.Length]
            };
            for (var i = 0; i < model.bones.Length; i++)
            {
                var bone = model.bones[i];
                var name = model.model.bones[i].originalName.ToString();
                result.bones[i] = new MmdBonePose
                {
                    name = name, worldPosition = bone.position,
                    localPosition = bone.localPosition, localRotation = bone.localRotation
                };
            }
            for (var i = 0; i < morphTargets.Length; i++)
                result.morphWeights[i] = morphTargets[i].GetBlendShapeWeight(morphIndices[i]);
            result.sourceCurveRotations = SourceRotations();
            return result;
        }

        private MmdFkSample[] SourceRotations()
        {
            var result = new List<MmdFkSample>();
            if (bakedReference) return result.ToArray();
            for (var i = 0; i < targets.Length; i++)
            {
                var name = model.model.bones[i].originalName.ToString();
                if (targets[i] == null || (name != "左腕" && name != "右腕" && name != "左ひじ" &&
                    name != "右ひじ" && name != "上半身")) continue;
                var e = Vector3.zero;
                for (var axis = 0; axis < 3; axis++)
                {
                    var curve = clip.bones.curves[i * 6 + 3 + axis];
                    if (curve != null) e[axis] = curve.Evaluate(TimeSeconds) * Mathf.Rad2Deg;
                }
                result.Add(new MmdFkSample { name = name,
                    rotation = directTracks == null ? Quaternion.Euler(e) : directTracks.Rotation(i, TimeSeconds) });
            }
            return result.ToArray();
        }

        public void SweepSourceRotations()
        {
            if (bakedReference) { Summary.fkSweep = Array.Empty<MmdFkPose>(); return; }
            var poses = new MmdFkPose[256];
            for (var i = 0; i < poses.Length; i++)
            {
                // Include endpoints and deterministic nonsequential fractional
                // samples over the entire duration, exercising backwards seeks.
                var time = i == 0 ? 0 : i == 1 ? Summary.durationSeconds :
                    Summary.durationSeconds * (((i * 397) % 1021) + 0.37f) / 1021;
                Sample(time);
                poses[i] = new MmdFkPose { seconds = TimeSeconds, sourceCurveRotations = SourceRotations() };
            }
            Summary.fkSweep = poses;
        }

        private void OnDestroy()
        {
            if (referenceGraph.IsValid()) referenceGraph.Destroy();
        }
    }
}
