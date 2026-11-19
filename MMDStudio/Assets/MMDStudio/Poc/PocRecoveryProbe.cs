using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocRecoveryProbe : MonoBehaviour
    {
        [Serializable]
        private sealed class CheckResult { public string name; public bool passed; }
        [Serializable]
        private sealed class Report { public bool success; public CheckResult[] checks; public string error; }
        private readonly List<CheckResult> checks = new List<CheckResult>();

        public IEnumerator Run(PocWorkspace workspace, string reportPath)
        {
            var pmx = PocArguments.Value("-pocPmx");
            var vmd = PocArguments.Value("-pocVmd");
            var directory = Path.GetDirectoryName(reportPath);
            Directory.CreateDirectory(directory);
            var invalidPmx = Path.Combine(directory, "recovery-invalid.pmx");
            var truncatedPmx = Path.Combine(directory, "recovery-truncated.pmx");
            var invalidVmd = Path.Combine(directory, "recovery-invalid.vmd");
            File.WriteAllText(invalidPmx, "invalid PMX fixture");
            File.WriteAllBytes(truncatedPmx, new byte[] { 80, 77, 88, 32, 0, 0, 0, 64, 8 });
            File.WriteAllBytes(invalidVmd, new byte[64]);

            yield return workspace.LoadModel(pmx);
            Check("external_model_load", workspace.Current != null && !workspace.Busy && workspace.Error.Length == 0);
            if (workspace.Current == null) { Finish(reportPath, workspace.Error); yield break; }
            yield return workspace.LoadMotion(vmd);
            Check("external_motion_load", workspace.Motion != null && workspace.Error.Length == 0);
            if (workspace.Motion == null) { Finish(reportPath, workspace.Error); yield break; }
            var root = workspace.Current.root;
            var oldMotion = workspace.Motion;
            oldMotion.Playing = false;
            oldMotion.Sample(15);
            yield return workspace.LoadMotion(vmd);
            yield return null;
            Check("motion_reassignment_from_animated_pose", workspace.Error.Length == 0 && workspace.Motion != oldMotion &&
                root == workspace.Current.root && root.GetComponents<MmdPocMotion>().Length == 1);

            var motion = workspace.Motion;
            motion.Playing = false;
            motion.Sample(15);
            var pose = motion.PosePositions();
            yield return workspace.LoadMotion(invalidVmd);
            yield return null;
            Check("invalid_motion_preserves_model_and_motion", workspace.Error.Length > 0 && workspace.Current.root == root && workspace.Motion == motion);
            Check("invalid_motion_restores_pose", SamePose(pose, motion.PosePositions()) && !motion.Playing && !workspace.Busy);
            var cameraPosition = Camera.main.transform.position;
            yield return workspace.LoadModel(invalidPmx);
            Check("invalid_model_preserves_current_scene", workspace.Error.Length > 0 && workspace.Current.root == root && workspace.Motion == motion &&
                Vector3.Distance(cameraPosition, Camera.main.transform.position) < 0.00001f);
            yield return workspace.LoadModel(truncatedPmx);
            Check("partial_parse_failure_cleans_container", workspace.Error.Length > 0 && workspace.Current.root == root && LifetimeCount() == 1);
            workspace.ApplyPickerResult(null, true);
            Check("cancel_is_non_destructive", workspace.Current.root == root && workspace.Motion == motion && workspace.Error.Length == 0 && !workspace.Busy);
            yield return workspace.LoadModel(pmx, invalidVmd);
            Check("failed_model_and_motion_transaction_rolls_back", workspace.Error.Length > 0 && workspace.Current.root == root && workspace.Motion == motion && LifetimeCount() == 1);

            for (var i = 0; i < 3; i++)
            {
                var owned = workspace.Owner.OwnedResources;
                yield return workspace.LoadModel(pmx, vmd);
                yield return null;
                Check("replacement_releases_owned_resources_" + i, owned.All(resource => resource == null) &&
                    LifetimeCount() == 1 && workspace.Error.Length == 0 && workspace.Motion != null);
            }
            var finalResources = workspace.Owner.OwnedResources;
            workspace.Clear();
            yield return null;
            yield return Resources.UnloadUnusedAssets();
            Check("clear_releases_model_and_owned_resources", workspace.Current == null && workspace.Motion == null &&
                LifetimeCount() == 0 && finalResources.All(resource => resource == null));
            Finish(reportPath, "");
        }

        private static bool SamePose(Vector3[] a, Vector3[] b)
        {
            return a.Length == b.Length && !a.Where((position, i) => Vector3.Distance(position, b[i]) > 0.0001f).Any();
        }

        private static int LifetimeCount() => Resources.FindObjectsOfTypeAll<PocAssetLifetime>().Length;
        private void Check(string name, bool passed)
        {
            checks.Add(new CheckResult { name = name, passed = passed });
            Debug.Log("RECOVERY_CHECK " + name + " " + passed);
        }
        private void Finish(string path, string error)
        {
            var report = new Report { success = error.Length == 0 && checks.All(check => check.passed), checks = checks.ToArray(), error = error };
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log(report.success ? "POC_RECOVERY_SUCCESS" : "POC_RECOVERY_FAILED");
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
