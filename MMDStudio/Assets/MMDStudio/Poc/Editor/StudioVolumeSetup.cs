using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MmdStudio.Poc
{
    // H2a-01: the project's own volume defaults are neutral, so the app owns the look without
    // overriding every component at runtime. Two places feed the camera's volume stack before the
    // app's Volume: the URP asset's volume profile (the template pointed it at SampleSceneProfile:
    // bloom, vignette, tonemapping, motion blur) and the global default profile in the URP global
    // settings (every component, some with template values). Normalize clears the former and
    // resets every parameter of the latter to its component's constructor default (the default
    // profile must keep every component, which also keeps their post-processing shader variants
    // from being stripped). Verify is run by PocBuild and fails the build if either drifts back.
    public static class StudioVolumeSetup
    {
        // Lighting data, not a screen effect: keeps URP's template values (unused: no probe volumes).
        private static bool Exempt(VolumeComponent component) => component.GetType().Name == "ProbeVolumesOptions";
        private static readonly string[] PipelineAssets = { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" };

        [MenuItem("EasyDanceStudio/Normalize Volume Defaults")]
        public static void Normalize()
        {
            var changes = new List<string>();
            foreach (var path in PipelineAssets)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;
                var serialized = new SerializedObject(asset);
                var property = serialized.FindProperty("m_VolumeProfile");
                if (property != null && property.objectReferenceValue != null)
                {
                    changes.Add(path + ": volume profile " + property.objectReferenceValue.name + " -> none");
                    property.objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(asset);
                }
            }
            var profile = DefaultProfile();
            if (profile == null) throw new InvalidOperationException("URP default volume profile not found.");
            foreach (var component in profile.components.Where(item => !Exempt(item)))
            {
                var fresh = (VolumeComponent)ScriptableObject.CreateInstance(component.GetType());
                try
                {
                    for (var i = 0; i < component.parameters.Count; i++)
                    {
                        if (Same(component.parameters[i], fresh.parameters[i])) continue;
                        changes.Add(profile.name + "/" + component.GetType().Name + "[" + i + "] " + Describe(component.parameters[i]) + " -> " + Describe(fresh.parameters[i]));
                        component.parameters[i].SetValue(fresh.parameters[i]);
                    }
                    if (!component.active) { changes.Add(profile.name + "/" + component.GetType().Name + " active -> true"); component.active = true; }
                }
                finally { UnityEngine.Object.DestroyImmediate(fresh); }
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("STUDIO_VOLUME_NORMALIZED " + changes.Count + " change(s)\n" + string.Join("\n", changes));
        }

        // Returns the problems; empty when the defaults are neutral.
        public static List<string> Verify()
        {
            var problems = new List<string>();
            foreach (var path in PipelineAssets)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                var property = asset == null ? null : new SerializedObject(asset).FindProperty("m_VolumeProfile");
                if (property != null && property.objectReferenceValue != null) problems.Add(path + " has volume profile " + property.objectReferenceValue.name);
            }
            var profile = DefaultProfile();
            if (profile == null) { problems.Add("URP default volume profile not found"); return problems; }
            foreach (var component in profile.components.Where(item => !Exempt(item)))
            {
                var fresh = (VolumeComponent)ScriptableObject.CreateInstance(component.GetType());
                try
                {
                    for (var i = 0; i < component.parameters.Count; i++)
                        if (!Same(component.parameters[i], fresh.parameters[i]))
                            problems.Add(profile.name + "/" + component.GetType().Name + "[" + i + "] = " + Describe(component.parameters[i]));
                }
                finally { UnityEngine.Object.DestroyImmediate(fresh); }
            }
            return problems;
        }

        public static void VerifyOrThrow()
        {
            var problems = Verify();
            if (problems.Count > 0)
                throw new InvalidOperationException("Volume defaults are not neutral (run EasyDanceStudio/Normalize Volume Defaults):\n" + string.Join("\n", problems.Take(20)));
            Debug.Log("STUDIO_VOLUME_NEUTRAL ok");
        }

        // The profile UniversalRenderPipelineGlobalSettings uses as its default (guid ab09877e...).
        private const string DefaultProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
        private static VolumeProfile DefaultProfile() => AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultProfilePath);

        // Value types (floats, colours, vectors, enums) compare by value; curves (TextureCurve)
        // have no value equality and keep their template identity defaults, so they are skipped;
        // textures compare by reference (the defaults are none).
        private static bool Same(VolumeParameter a, VolumeParameter b)
        {
            var x = Value(a); var y = Value(b);
            if (x != null && !(x is UnityEngine.Object) && !x.GetType().IsValueType && !(x is string)) return true;
            return Equals(x, y);
        }
        private static object Value(VolumeParameter parameter)
        {
            var value = parameter.GetType().GetProperty("value")?.GetValue(parameter);
            // A missing/destroyed Unity object compares equal to null (Unity's fake null).
            return value is UnityEngine.Object obj && obj == null ? null : value;
        }
        private static string Describe(VolumeParameter parameter)
        {
            var value = Value(parameter);
            return value == null ? "null" : value is UnityEngine.Object obj ? obj.name : value.ToString();
        }
    }
}
