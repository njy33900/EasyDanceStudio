using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocDeformationProbe : MonoBehaviour
    {
        [Serializable] private sealed class WeightSample { public float seconds; public float expected; public float eye; public float mouth; }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error;
            public string eyeMorph, mouthMorph, ikController;
            public string[] morphTypes, checks;
            public WeightSample[] weights;
            public int changedVertices;
            public int changedPixels;
            public float maxVertexDelta, ikOnDistance, ikOffDistance, ikRepeatError;
            public bool staleMorphObserved;
        }
        private readonly List<string> checks = new List<string>();
        private readonly Report report = new Report();
        private void Check(string name, bool value)
        {
            if (!value) throw new InvalidOperationException("Deformation check failed: " + name);
            checks.Add(name);
        }

        public IEnumerator Run(PocWorkspace workspace, string path)
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            yield return workspace.LoadModel(PocArguments.Value("-pocPmx"));
            if (workspace.Current == null) { Finish(path, workspace.Error); yield break; }
            var model = workspace.Current;
            var morphs = model.model.morphs;
            var bones = model.mmdTransformResult.transformManager.bones;
            var renderers = model.root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var eye = -1;
            var mouth = -1;
            MMDBoneTransform controller = null;
            string boneFixture = Path.Combine(directory, "deform-bone-only.vmd");
            string morphFixture = Path.Combine(directory, "deform-morph.vmd");
            string ikFixture = Path.Combine(directory, "deform-ik.vmd");
            try
            {
                report.morphTypes = morphs.GroupBy(m => m.type).Select(g => g.Key + ":" + g.Count()).ToArray();
                eye = FindMorph(morphs, "まばたき", 2);
                mouth = FindMorph(morphs, "あ", 3);
                report.eyeMorph = morphs[eye].originalName.ToString();
                report.mouthMorph = morphs[mouth].originalName.ToString();
                controller = bones.First(b => (b.flags & PMXBone.Flags.IK) != 0 && b.boneName.Contains("足") && b.ik.links.Length >= 2);
                report.ikController = controller.boneName;
                var head = bones.First(b => b.boneName == "頭");
                WriteFixture(boneFixture, head.boneName, false, null, null);
                WriteFixture(morphFixture, head.boneName, false, report.eyeMorph, report.mouthMorph);
                WriteFixture(ikFixture, controller.boneName, true, null, null);
                Check("fixtures_outside_assets", !boneFixture.Replace('\\', '/').Contains("/Assets/"));
            }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }

            yield return workspace.LoadMotion(morphFixture);
            if (workspace.Motion == null || workspace.Error.Length > 0) { Finish(path, workspace.Error); yield break; }
            workspace.Motion.Playing = false;
            yield return null;
            var neutralVertices = new List<Vector3[]>();
            try
            {
                var samples = new List<WeightSample>();
                foreach (var seconds in new[] { 0f, .125f, .25f, .375f, .5f, .75f, 1f })
                {
                    workspace.Motion.Sample(seconds);
                    var expected = (seconds <= .5f ? seconds * 2 : (1-seconds)*2) * 100;
                    var e = Weight(renderers, morphs[eye].renamedName.ToString());
                    var m = Weight(renderers, morphs[mouth].renamedName.ToString());
                    samples.Add(new WeightSample { seconds = seconds, expected = expected, eye = e, mouth = m });
                    Check("weights_" + seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                        Math.Abs(e-expected) < .001f && Math.Abs(m-expected) < .001f);
                }
                report.weights = samples.ToArray();
                workspace.Motion.Sample(0);
                foreach (var renderer in renderers) neutralVertices.Add(Bake(renderer));
                var head = bones.First(b => b.boneName == "頭").transform;
                Camera.main.transform.position = head.position + Vector3.forward * .55f;
                Camera.main.transform.LookAt(head.position - Vector3.up * .025f);
                Camera.main.fieldOfView = 35;
            }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            yield return null;
            try { PocSmoke.CaptureImage(Camera.main, Path.Combine(directory, "deform-neutral.png")); workspace.Motion.Sample(.5f); }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            yield return null;
            try
            {
                for (var i = 0; i < renderers.Length; i++)
                {
                    var active = Bake(renderers[i]);
                    for (var j = 0; j < active.Length; j++)
                    {
                        var delta = Vector3.Distance(active[j], neutralVertices[i][j]);
                        if (delta > 1e-6f) report.changedVertices++;
                        report.maxVertexDelta = Mathf.Max(report.maxVertexDelta, delta);
                    }
                }
                Check("morph_deforms_mesh", report.changedVertices > 20 && report.maxVertexDelta > .0001f);
                PocSmoke.CaptureImage(Camera.main, Path.Combine(directory, "deform-active.png"));
                var neutralImage = new Texture2D(2, 2);
                var activeImage = new Texture2D(2, 2);
                try
                {
                    neutralImage.LoadImage(File.ReadAllBytes(Path.Combine(directory, "deform-neutral.png")));
                    activeImage.LoadImage(File.ReadAllBytes(Path.Combine(directory, "deform-active.png")));
                    var neutralPixels = neutralImage.GetPixels32();
                    var activePixels = activeImage.GetPixels32();
                    Check("image_dimensions_match", neutralPixels.Length == activePixels.Length);
                    for (var i = 0; i < neutralPixels.Length; i++)
                    {
                        var a = activePixels[i]; var n = neutralPixels[i];
                        if (Math.Abs(a.r-n.r)+Math.Abs(a.g-n.g)+Math.Abs(a.b-n.b) > 30) report.changedPixels++;
                    }
                    Check("morph_visible_in_gpu_render", report.changedPixels > 20);
                }
                finally { Destroy(neutralImage); Destroy(activeImage); }
            }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            yield return workspace.LoadMotion(boneFixture);
            workspace.Motion.Playing = false;
            try
            {
                workspace.Motion.Sample(0);
                report.staleMorphObserved = renderers.Any(r => Enumerable.Range(0, r.sharedMesh.blendShapeCount).Any(i => Math.Abs(r.GetBlendShapeWeight(i)) > .001f));
                Check("unaddressed_morphs_reset_on_replacement", !report.staleMorphObserved);
            }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            yield return workspace.LoadMotion(ikFixture);
            workspace.Motion.Playing = false;
            yield return null;
            try
            {
                Check("ik_clip_loaded", workspace.Error.Length == 0 && workspace.Motion.Summary.ikBindings == 1);
                workspace.Motion.Sample(0);
                report.ikOnDistance = Vector3.Distance(controller.transform.position, controller.ik.target.transform.position);
                var onPose = controller.ik.target.transform.position;
                Check("ik_toggle_on", controller.ikEnabled);
                workspace.Motion.Sample(.5f);
                report.ikOffDistance = Vector3.Distance(controller.transform.position, controller.ik.target.transform.position);
                Check("ik_toggle_off", !controller.ikEnabled);
                Check("ik_reduces_target_error", report.ikOffDistance > .02f && report.ikOnDistance < report.ikOffDistance * .2f);
                workspace.Motion.Sample(1);
                Check("ik_toggle_returns_on", controller.ikEnabled);
                workspace.Motion.Sample(0);
                report.ikRepeatError = Vector3.Distance(onPose, controller.ik.target.transform.position);
                Check("ik_repeat_seek", report.ikRepeatError < 1e-5f);
                foreach (var link in controller.ik.links)
                    Check("ik_finite_link_" + link.bone.boneName, !float.IsNaN(link.bone.transform.localRotation.w));
            }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            try
            {
                var focus = controller.ik.target.transform.position + Vector3.up * .25f;
                Camera.main.transform.position = focus + Vector3.forward * .9f;
                Camera.main.transform.LookAt(focus);
                Camera.main.fieldOfView = 50;
            }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            yield return null;
            try { PocSmoke.CaptureImage(Camera.main, Path.Combine(directory, "deform-ik-on.png")); workspace.Motion.Sample(.5f); }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            yield return null;
            try { PocSmoke.CaptureImage(Camera.main, Path.Combine(directory, "deform-ik-off.png")); Check("ik_gpu_images", true); }
            catch (Exception ex) { Finish(path, ex.ToString()); yield break; }
            Finish(path, null);
        }

        private static int FindMorph(PMXMorph[] morphs, string preferred, byte panel)
        {
            var index = Array.FindIndex(morphs, m => m.type == PMXMorph.Type.Vertex && m.originalName.ToString() == preferred);
            if (index < 0) index = Array.FindIndex(morphs, m => m.type == PMXMorph.Type.Vertex && m.panel == panel);
            if (index < 0) throw new InvalidOperationException("No vertex morph for panel " + panel);
            return index;
        }
        private static float Weight(SkinnedMeshRenderer[] renderers, string name)
        {
            var values = renderers.Select(r => new { renderer = r, index = r.sharedMesh.GetBlendShapeIndex(name) }).Where(x => x.index >= 0).ToArray();
            if (values.Length == 0) throw new InvalidOperationException("No renderer morph: " + name);
            var value = values[0].renderer.GetBlendShapeWeight(values[0].index);
            if (values.Any(x => Math.Abs(x.renderer.GetBlendShapeWeight(x.index)-value) > .001f)) throw new InvalidOperationException("Renderer morph weights disagree.");
            return value;
        }
        private static Vector3[] Bake(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh();
            try { renderer.BakeMesh(mesh); return mesh.vertices; }
            finally { Destroy(mesh); }
        }

        private static void Text(BinaryWriter writer, string value, int length)
        {
            // Controlled Gene fixture names only. Mono Player has no CJK
            // encoding provider; do not add one to the product for a test writer.
            var encoded = value switch
            {
                "頭" => "k6o=",
                "まばたき" => "gtyCzoK9gqs=",
                "あ" => "gqA=",
                "左足ＩＫ" => "jbaRq4Jogmo=",
                "右足ＩＫ" => "iUWRq4Jogmo=",
                _ => null
            };
            if (encoded == null && value.Any(c => c > 127)) throw new InvalidDataException("Unsupported controlled fixture name: " + value);
            var bytes = encoded == null ? Encoding.ASCII.GetBytes(value) : Convert.FromBase64String(encoded);
            if (bytes.Length > length) throw new InvalidDataException("Fixture name is too long.");
            writer.Write(bytes); writer.Write(new byte[length-bytes.Length]);
        }
        private static void WriteFixture(string path, string bone, bool ik, string eye, string mouth)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                Text(writer, "Vocaloid Motion Data 0002", 30); Text(writer, "deformation probe", 20);
                writer.Write(2u);
                foreach (var frame in new[] { 0u, 30u })
                {
                    Text(writer, bone, 15); writer.Write(frame);
                    writer.Write(ik ? -1f : 0f); writer.Write(ik ? .5f : 0f); writer.Write(0f);
                    writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
                    // All repeated bone Bezier controls are collinear (linear).
                    writer.Write(Enumerable.Repeat((byte)20, 64).ToArray());
                }
                writer.Write(eye == null ? 0u : 6u);
                if (eye != null) foreach (var morph in new[] { eye, mouth }) foreach (var frame in new[] { 0u, 15u, 30u })
                { Text(writer, morph, 15); writer.Write(frame); writer.Write(frame == 15 ? 1f : 0f); }
                writer.Write(0u); writer.Write(0u); writer.Write(0u); // camera, light, shadow
                writer.Write(ik ? 3u : 0u);
                if (ik) foreach (var frame in new[] { 0u, 15u, 30u })
                { writer.Write(frame); writer.Write((byte)1); writer.Write(1u); Text(writer, bone, 20); writer.Write((byte)(frame == 15 ? 0 : 1)); }
            }
        }
        private void Finish(string path, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.checks = checks.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
