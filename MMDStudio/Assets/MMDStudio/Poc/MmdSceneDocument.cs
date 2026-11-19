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
    [Serializable]
    public sealed class SceneFile
    {
        public const int CurrentSchemaVersion = 1;
        public const string ApplicationId = "EasyDanceStudio";
        public int schemaVersion = CurrentSchemaVersion;
        public string application = ApplicationId;
        public string projectId;
        public List<SceneObjectData> sceneObjects = new List<SceneObjectData>();
        public string selectedId;
        public ViewData view = new ViewData();
        public SceneGraphicsSettings graphics = new SceneGraphicsSettings();
        public TimelineData timeline = new TimelineData();
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
    }

    [Serializable]
    public sealed class ViewData
    {
        public Vector3 pivot = new Vector3(0, 1, 0);
        public float yaw = MmdViewportCamera.DefaultYaw, pitch = MmdViewportCamera.DefaultPitch, distance = 4;
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
            try { scene = JsonUtility.FromJson<SceneFile>(File.ReadAllText(file.FullName, Encoding.UTF8)); }
            catch (ArgumentException ex) { throw new InvalidDataException("JSON 형식이 올바르지 않습니다: " + ex.Message, ex); }
            if (scene == null) throw new InvalidDataException("JSON 형식이 올바르지 않습니다.");
            Validate(scene);
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
            }
            if (scene.view == null || !Finite(scene.view.pivot.x) || !Finite(scene.view.pivot.y) || !Finite(scene.view.pivot.z) ||
                !Finite(scene.view.yaw) || !Finite(scene.view.pitch) || !Finite(scene.view.distance)) throw new InvalidDataException("시점 값이 올바르지 않습니다.");
            if (scene.timeline == null || double.IsNaN(scene.timeline.timeSeconds) || double.IsInfinity(scene.timeline.timeSeconds) || scene.timeline.timeSeconds < 0 ||
                double.IsNaN(scene.timeline.speed) || scene.timeline.speed < .5 || scene.timeline.speed > 2) throw new InvalidDataException("재생 설정이 올바르지 않습니다.");
            if (scene.graphics == null) throw new InvalidDataException("그래픽 설정이 없습니다.");
            try { scene.graphics.Validate(); }
            catch (ArgumentException ex) { throw new InvalidDataException("그래픽 설정이 올바르지 않습니다: " + ex.Message, ex); }
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
