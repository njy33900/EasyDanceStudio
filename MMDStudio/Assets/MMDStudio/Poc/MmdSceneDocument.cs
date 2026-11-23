using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MmdStudio.Poc
{
    // MVP-07 .mmdscene (JSON) contract, spec 5.2/5.3 and MMD-112. Reference project:
    // source PMX/VMD files are never copied. Paths are stored relative to the scene
    // file when on the same volume, absolute otherwise. No Unity object references.
    // Schema 2 (H1-05, 2026-11-21): per-object physics / body-motion settings in a `physics`
    // block. Schema 1 files are migrated in memory on open (the file is not rewritten until the
    // user saves); newer versions are rejected and the original is left untouched.
    // H2a-03 (2026-11-21): optional `audio` block (WAV reference, volume, mute). Additive within
    // schema 2: a file without it opens with no audio.
    [Serializable]
    public sealed class SceneFile
    {
        public const int CurrentSchemaVersion = 2;
        public const int OldestReadableSchemaVersion = 1;
        public const string ApplicationId = "EasyDanceStudio";
        public int schemaVersion = CurrentSchemaVersion;
        public string application = ApplicationId;
        public string projectId;
        public List<SceneObjectData> sceneObjects = new List<SceneObjectData>();
        public string selectedId;
        public ViewData view = new ViewData();
        public SceneGraphicsSettings graphics = new SceneGraphicsSettings();
        public TimelineData timeline = new TimelineData();
        public AudioData audio = new AudioData();
        public CameraMotionData cameraMotion = new CameraMotionData();
        public OutputData output = new OutputData(); // H3 (additive in schema 2)
    }

    // H2a-04 (additive in schema 2): camera VMD reference and whether the view follows it.
    [Serializable]
    public sealed class CameraMotionData
    {
        public string path = "";
        public bool follow;
        // D-01: generator version, inputs and seed when the VMD was made by the automatic camera.
        public AutoCameraSettings generator = new AutoCameraSettings { version = 0 };
    }

    [Serializable]
    public sealed class AudioData
    {
        public string path = "";  // stored like model paths; empty when the scene has no audio
        public float volume = 1;  // 0..1
        public bool muted;
    }

    [Serializable]
    public sealed class SceneObjectData
    {
        public const string CharacterType = "MmdCharacter", StageType = "Stage";
        public string id;
        public string type;
        public string assetPath;
        public string motionPath;
        public Vector3 position;
        public Vector3 rotationEuler;
        public Vector3 scale = Vector3.one;
        public bool visible = true;
        // Schema 2: physics (PH-03), per-part strength (PH-04) and chest/hip spring (PH-05).
        public PhysicsData physics = new PhysicsData();
        // H2b-01 (additive in schema 2): material style override.
        public MaterialStyleData material = new MaterialStyleData();
        // v0.61 (additive in schema 2): screen correction (object and per-PMX-material).
        public ColorCorrectionData correction = new ColorCorrectionData();
    }

    [Serializable]
    public sealed class PhysicsData
    {
        public bool enabled;
        public List<PartStrength> strength = new List<PartStrength>();
        public string spring; // "light" / "medium" / "strong", empty when off
    }

    [Serializable]
    public sealed class PartStrength
    {
        public string part;
        public float value = 1;
    }

    // Schema 1 layout (read only, for migration).
    [Serializable]
    internal sealed class SceneFileV1
    {
        public int schemaVersion;
        public string application, projectId, selectedId;
        public List<SceneObjectDataV1> sceneObjects = new List<SceneObjectDataV1>();
        public ViewData view = new ViewData();
        public SceneGraphicsSettings graphics = new SceneGraphicsSettings();
        public TimelineData timeline = new TimelineData();
    }

    [Serializable]
    internal sealed class SceneObjectDataV1
    {
        public string id, type, assetPath, motionPath;
        public Vector3 position, rotationEuler, scale = Vector3.one;
        public bool visible = true;
        public bool physics;                 // optional fields added during 0.5 development
        public string[] physicsParts;
        public float[] physicsStrength;
        public string springPreset;
    }

    [Serializable]
    internal sealed class SceneFileHeader
    {
        public int schemaVersion;
        public string application;
    }

    [Serializable]
    public sealed class ViewData
    {
        public Vector3 pivot = new Vector3(0, 1, 0);
        public float yaw = MmdViewportCamera.DefaultYaw, pitch = MmdViewportCamera.DefaultPitch, distance = 4;
        public float roll; // v0.61 (additive in schema 2): screen tilt handed over from a camera VMD
    }

    [Serializable]
    public sealed class TimelineData
    {
        public double timeSeconds;
        public bool loop;
        public double speed = 1;
    }

    public static class MmdSceneDocument
    {
        public const string Extension = ".mmdscene";
        public const long MaxBytes = 4L * 1024 * 1024;
        public const int MaxObjects = 64;

        public static SceneFile Read(string path)
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists) throw new FileNotFoundException("장면 파일이 없습니다.", file.FullName);
            if (!string.Equals(file.Extension, Extension, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("확장자가 " + Extension + "가 아닙니다.");
            if (file.Length == 0 || file.Length > MaxBytes) throw new InvalidDataException("장면 파일 크기가 올바르지 않습니다(최대 4 MiB).");
            SceneFile scene;
            var json = File.ReadAllText(file.FullName, Encoding.UTF8);
            try
            {
                var header = JsonUtility.FromJson<SceneFileHeader>(json);
                if (header == null) throw new InvalidDataException("JSON 형식이 올바르지 않습니다.");
                if (header.application != SceneFile.ApplicationId) throw new InvalidDataException("Easy Dance Studio 장면 파일이 아닙니다.");
                if (header.schemaVersion < SceneFile.OldestReadableSchemaVersion || header.schemaVersion > SceneFile.CurrentSchemaVersion)
                    throw new InvalidDataException("지원하지 않는 장면 파일 버전입니다: " + header.schemaVersion +
                        " (지원: " + SceneFile.OldestReadableSchemaVersion + "~" + SceneFile.CurrentSchemaVersion + ")");
                scene = header.schemaVersion == 1 ? Migrate(JsonUtility.FromJson<SceneFileV1>(json)) : JsonUtility.FromJson<SceneFile>(json);
                MigratedFromVersion = header.schemaVersion == SceneFile.CurrentSchemaVersion ? 0 : header.schemaVersion;
            }
            catch (ArgumentException ex) { throw new InvalidDataException("JSON 형식이 올바르지 않습니다: " + ex.Message, ex); }
            if (scene == null) throw new InvalidDataException("JSON 형식이 올바르지 않습니다.");
            Validate(scene);
            return scene;
        }

        // Version of the last file Read migrated from (0 when it was already current).
        [ThreadStatic] public static int MigratedFromVersion;

        // Schema 1 -> 2: same fields; the flat physics fields become the `physics` block.
        internal static SceneFile Migrate(SceneFileV1 old)
        {
            if (old == null) return null;
            var scene = new SceneFile
            {
                projectId = old.projectId, selectedId = old.selectedId, view = old.view, graphics = old.graphics, timeline = old.timeline,
                application = old.application, schemaVersion = SceneFile.CurrentSchemaVersion,
            };
            foreach (var item in old.sceneObjects ?? new List<SceneObjectDataV1>())
            {
                if (item == null) { scene.sceneObjects.Add(null); continue; }
                var data = new SceneObjectData
                {
                    id = item.id, type = item.type, assetPath = item.assetPath, motionPath = item.motionPath,
                    position = item.position, rotationEuler = item.rotationEuler, scale = item.scale, visible = item.visible,
                    physics = new PhysicsData { enabled = item.physics, spring = item.springPreset ?? "" },
                };
                if (item.physicsParts != null && item.physicsStrength != null)
                    for (var i = 0; i < Math.Min(item.physicsParts.Length, item.physicsStrength.Length); i++)
                        data.physics.strength.Add(new PartStrength { part = item.physicsParts[i], value = item.physicsStrength[i] });
                scene.sceneObjects.Add(data);
            }
            return scene;
        }

        public static void Validate(SceneFile scene)
        {
            if (scene.application != SceneFile.ApplicationId) throw new InvalidDataException("Easy Dance Studio 장면 파일이 아닙니다.");
            if (scene.schemaVersion != SceneFile.CurrentSchemaVersion)
                throw new InvalidDataException("지원하지 않는 장면 파일 버전입니다: " + scene.schemaVersion + " (지원: " + SceneFile.CurrentSchemaVersion + ")");
            if (scene.sceneObjects == null || scene.sceneObjects.Count > MaxObjects) throw new InvalidDataException("장면 항목은 최대 " + MaxObjects + "개입니다.");
            var ids = new HashSet<string>();
            foreach (var item in scene.sceneObjects)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !ids.Add(item.id)) throw new InvalidDataException("장면 항목 ID가 없거나 중복되었습니다.");
                if (item.type != SceneObjectData.CharacterType && item.type != SceneObjectData.StageType) throw new InvalidDataException("알 수 없는 항목 종류: " + item.type);
                CheckPath(item.assetPath, ".pmx");
                if (!string.IsNullOrEmpty(item.motionPath))
                {
                    if (item.type == SceneObjectData.StageType) throw new InvalidDataException("배경에는 모션을 지정할 수 없습니다.");
                    CheckPath(item.motionPath, ".vmd");
                }
                foreach (var value in new[] { item.position.x, item.position.y, item.position.z, item.rotationEuler.x, item.rotationEuler.y, item.rotationEuler.z })
                    if (!Finite(value)) throw new InvalidDataException("배치 값이 올바르지 않습니다.");
                foreach (var value in new[] { item.scale.x, item.scale.y, item.scale.z })
                    if (!Finite(value) || value < .001f || value > 1000) throw new InvalidDataException("크기는 0.001~1000 범위여야 합니다.");
                if (item.physics == null) item.physics = new PhysicsData();
                if (item.physics.strength == null) item.physics.strength = new List<PartStrength>();
                foreach (var entry in item.physics.strength)
                    if (entry == null || Array.IndexOf(MmdPhysicsParts.Order, entry.part) < 0 || !Finite(entry.value) || entry.value < 0 || entry.value > 1)
                        throw new InvalidDataException("물리 강도 설정이 올바르지 않습니다.");
                if (!string.IsNullOrEmpty(item.physics.spring) && Array.IndexOf(MmdSpringBones.PresetIds, item.physics.spring) < 0)
                    throw new InvalidDataException("신체 흔들림 설정이 올바르지 않습니다: " + item.physics.spring);
                if (item.type == SceneObjectData.StageType && (item.physics.enabled || !string.IsNullOrEmpty(item.physics.spring)))
                    throw new InvalidDataException("배경에는 물리·신체 흔들림을 지정할 수 없습니다.");
                if (item.material == null) item.material = new MaterialStyleData();
                try { item.material.Validate(); }
                catch (ArgumentException ex) { throw new InvalidDataException("재질 스타일 설정이 올바르지 않습니다: " + ex.Message, ex); }
                if (item.material.style == MaterialStyleData.Pbr && item.type != SceneObjectData.StageType)
                    throw new InvalidDataException("PBR 재질은 배경에만 쓸 수 있습니다.");
                if (item.correction == null) item.correction = new ColorCorrectionData();
                try { item.correction.Validate(); }
                catch (ArgumentException ex) { throw new InvalidDataException("화면 보정 설정이 올바르지 않습니다: " + ex.Message, ex); }
            }
            if (scene.view == null || !Finite(scene.view.pivot.x) || !Finite(scene.view.pivot.y) || !Finite(scene.view.pivot.z) ||
                !Finite(scene.view.yaw) || !Finite(scene.view.pitch) || !Finite(scene.view.distance) || !Finite(scene.view.roll)) throw new InvalidDataException("시점 값이 올바르지 않습니다.");
            if (scene.timeline == null || double.IsNaN(scene.timeline.timeSeconds) || double.IsInfinity(scene.timeline.timeSeconds) || scene.timeline.timeSeconds < 0 ||
                double.IsNaN(scene.timeline.speed) || scene.timeline.speed < .5 || scene.timeline.speed > 2) throw new InvalidDataException("재생 설정이 올바르지 않습니다.");
            if (scene.graphics == null) throw new InvalidDataException("그래픽 설정이 없습니다.");
            try { scene.graphics.Validate(); }
            catch (ArgumentException ex) { throw new InvalidDataException("그래픽 설정이 올바르지 않습니다: " + ex.Message, ex); }
            if (scene.audio == null) scene.audio = new AudioData();
            if (scene.audio.path == null) scene.audio.path = "";
            if (scene.audio.path.Length > 0) CheckPath(scene.audio.path, ".wav");
            if (!Finite(scene.audio.volume) || scene.audio.volume < 0 || scene.audio.volume > 1) throw new InvalidDataException("오디오 음량이 올바르지 않습니다.");
            if (scene.cameraMotion == null) scene.cameraMotion = new CameraMotionData();
            if (scene.cameraMotion.path == null) scene.cameraMotion.path = "";
            if (scene.cameraMotion.path.Length > 0) CheckPath(scene.cameraMotion.path, ".vmd");
            if (scene.cameraMotion.generator == null) scene.cameraMotion.generator = new AutoCameraSettings { version = 0 };
            else if (scene.cameraMotion.generator.version > 0)
            {
                try { scene.cameraMotion.generator.Validate(); }
                catch (ArgumentException ex) { throw new InvalidDataException("자동 카메라 설정이 올바르지 않습니다: " + ex.Message, ex); }
            }
            if (scene.output == null) scene.output = new OutputData();
            try { scene.output.Validate(); }
            catch (ArgumentException ex) { throw new InvalidDataException("출력 설정이 올바르지 않습니다: " + ex.Message, ex); }
        }
        private static void CheckPath(string value, string extension)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidDataException("파일 경로가 올바르지 않습니다.");
            if (!string.Equals(Path.GetExtension(value), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("파일 확장자가 " + extension + "가 아닙니다: " + value);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Writes to a temporary file in the same directory, then replaces atomically.
        public static void Write(string path, SceneFile scene)
        {
            Validate(scene);
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(full), Extension, StringComparison.OrdinalIgnoreCase)) full += Extension;
            var directory = Path.GetDirectoryName(full);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("저장할 폴더가 없습니다: " + directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(scene, true), new UTF8Encoding(false));
                if (File.Exists(full)) File.Replace(temporary, full, null);
                else File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static string ToStored(string sceneFile, string assetPath)
        {
            var asset = Path.GetFullPath(assetPath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(sceneFile));
            if (!string.Equals(Path.GetPathRoot(asset), Path.GetPathRoot(directory), StringComparison.OrdinalIgnoreCase)) return asset;
            return Path.GetRelativePath(directory, asset).Replace('\\', '/');
        }
        public static string Resolve(string sceneFile, string storedPath)
        {
            var value = storedPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sceneFile)), value));
        }
    }
}
