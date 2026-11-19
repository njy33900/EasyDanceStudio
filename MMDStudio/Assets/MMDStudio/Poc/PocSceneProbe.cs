using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocSceneProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, firstId, secondId, screenshot;
            public float maximumReplayError, realtimeSeconds;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed)
        {
            if (!passed) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        private static float Difference(Vector3[] a, Vector3[] b)
        {
            var delta=0f;
            for(var i=0;i<a.Length;i++)delta=Mathf.Max(delta,Vector3.Distance(a[i],b[i]));
            if(float.IsNaN(delta)||float.IsInfinity(delta))throw new InvalidOperationException("Nonfinite scene pose");
            return delta;
        }
        public IEnumerator Run(string path)
        {
            var report=new Report();
#if ENABLE_IL2CPP
            report.backend="IL2CPP";
#else
            report.backend="Mono";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var controlled=Path.Combine(Path.GetDirectoryName(path),"second-motion.vmd");
            PocPhysicsReferenceProbe.WriteFixture(controlled,.5f);
            var invalid=Path.Combine(Path.GetDirectoryName(path),"invalid.pmx");
            var invalidMotion=Path.Combine(Path.GetDirectoryName(path),"invalid.vmd");
            File.WriteAllText(invalid,"invalid PMX");File.WriteAllBytes(invalidMotion,new byte[64]);
            var scene=gameObject.AddComponent<MmdSceneService>();scene.AutomaticClock=false;
            MmdSceneService.SceneObject a=null,b=null;
            UnityEngine.Object[] retired=null,surviving=null;
            Vector3[] firstAtOne=null,secondAtOne=null;
            try
            {
                var camera=Camera.main;
                var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
                a=scene.Import(PocArguments.Value("-pocPmx"),PocArguments.Value("-pocVmd"),new Vector3(-1,0,0));
                scene.Seek(1);firstAtOne=a.Motion.PosePositions();
                b=scene.Import(PocArguments.Value("-pocSecondPmx"),controlled,new Vector3(1,0,0));
                report.firstId=a.Id;report.secondId=b.Id;
                Check("two_distinct_stable_ids_and_asset_owners",scene.Objects.Count==2&&a.Id!=b.Id&&a.Owner!=b.Owner);
                Check("second_import_preserves_first_pose",Difference(firstAtOne,a.Motion.PosePositions())<.00001f);
                Check("imports_do_not_reframe_camera",camera.transform.position==cameraPosition&&camera.transform.rotation==cameraRotation);
                Check("independent_motion_sources",a.MotionSource!=b.MotionSource&&a.Motion.Summary.durationSeconds>b.Motion.Summary.durationSeconds);
                Check("physics_and_component_clocks_off",scene.Objects.All(x=>!x.Motion.Playing&&!x.Model.mmdTransformResult.transformManager.livePhysics&&!x.Model.mmdTransformResult.transformManager.transformEnabled));
                secondAtOne=b.Motion.PosePositions();
                scene.Seek(0);var firstAtZero=a.Motion.PosePositions();var secondAtZero=b.Motion.PosePositions();
                scene.Playing=true;scene.Advance(1);
                Check("shared_clock_drives_distinct_poses",Math.Abs(scene.Seconds-1)<1e-7&&a.Motion.TimeSeconds==1&&b.Motion.TimeSeconds==1&&
                    Difference(firstAtZero,a.Motion.PosePositions())>.001f&&Difference(secondAtZero,b.Motion.PosePositions())>.001f);
                scene.Playing=false;scene.Advance(1);
                Check("shared_pause_preserves_both_poses",scene.Seconds==1&&Difference(firstAtOne,a.Motion.PosePositions())<.00001f&&Difference(secondAtOne,b.Motion.PosePositions())<.00001f);
                scene.Seek(5);
                Check("shorter_motion_holds_its_end",a.Motion.TimeSeconds==5&&b.Motion.TimeSeconds==b.Motion.Summary.durationSeconds);
                scene.Seek(0);scene.Seek(1);
                report.maximumReplayError=Mathf.Max(Difference(firstAtOne,a.Motion.PosePositions()),Difference(secondAtOne,b.Motion.PosePositions()));
                Check("repeated_seek_reconstructs_both",report.maximumReplayError<.00001f);
                var placement=b.Placement.position;var before=b.Motion.PosePositions();
                b.Placement.position+=Vector3.right;scene.Seek(1);
                var after=b.Motion.PosePositions();
                Check("placement_changes_only_selected_root",Difference(firstAtOne,a.Motion.PosePositions())<.00001f&&Vector3.Distance(after[0]-before[0],Vector3.right)<.0001f);
                b.Placement.position=placement;scene.Seek(1);
                var rejected=false;
                try{scene.Import(invalid,controlled,Vector3.zero);}catch(InvalidDataException){rejected=true;}
                Check("failed_model_import_preserves_scene",rejected&&scene.Objects.Count==2&&Difference(firstAtOne,a.Motion.PosePositions())<.00001f);
                rejected=false;
                try{scene.Import(PocArguments.Value("-pocPmx"),invalidMotion,Vector3.zero);}catch(Exception){rejected=true;}
                Check("failed_motion_during_model_import_retires_candidate",rejected&&scene.Objects.Count==2&&
                    FindObjectsByType<PocAssetLifetime>(FindObjectsSortMode.None).Length==2&&Difference(firstAtOne,a.Motion.PosePositions())<.00001f);
                rejected=false;var oldMotion=b.Motion;
                try{scene.ReplaceMotion(b.Id,invalidMotion);}catch(Exception){rejected=true;}
                Check("failed_motion_replacement_restores_pose",rejected&&b.Motion==oldMotion&&Difference(secondAtOne,b.Motion.PosePositions())<.00001f);
                rejected=false;
                try{a.Motion.Sample(0);}catch(InvalidOperationException){rejected=true;}
                Check("external_clock_write_rejected",rejected&&scene.Seconds==1);
                rejected=false;
                try{a.Motion.Load(a.Model,controlled);}catch(InvalidOperationException){rejected=true;}
                Check("external_clip_replacement_rejected",rejected&&a.Motion.OwnsSceneClock(scene)&&Difference(firstAtOne,a.Motion.PosePositions())<.00001f);
                var retainedId=b.Id;var retainedOwner=b.Owner;
                scene.ReplaceMotion(b.Id,controlled);
                Check("per_object_replacement_preserves_other_object",b.Id==retainedId&&b.Owner==retainedOwner&&scene.Objects.Count==2&&
                    Difference(firstAtOne,a.Motion.PosePositions())<.00001f&&Difference(secondAtOne,b.Motion.PosePositions())<.00001f);
                scene.Speed=2;scene.Playing=true;scene.Advance(.25);
                Check("shared_playback_speed",scene.Seconds==1.5&&a.Motion.TimeSeconds==1.5f&&b.Motion.TimeSeconds==1.5f);
                scene.Speed=1;scene.Loop=true;scene.Seek(scene.Duration-.25);scene.Advance(.5);
                Check("shared_loop_wraps_both",Math.Abs(scene.Seconds-.25)<.00001&&Math.Abs(a.Motion.TimeSeconds-.25f)<.00001&&Math.Abs(b.Motion.TimeSeconds-.25f)<.00001);
                scene.Stop();Check("shared_stop_resets_both",!scene.Playing&&scene.Seconds==0&&a.Motion.TimeSeconds==0&&b.Motion.TimeSeconds==0);
                scene.Seek(1);
                var renderers=scene.GetComponentsInChildren<SkinnedMeshRenderer>();
                var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                camera.transform.position=bounds.center+Vector3.forward*Mathf.Max(2,bounds.extents.magnitude*2.5f);camera.transform.LookAt(bounds.center);
                camera.nearClipPlane=.01f;camera.farClipPlane=100;
                var cube=GameObject.Find("Cube");if(cube!=null)cube.SetActive(false);
                report.screenshot=Path.Combine(Path.GetDirectoryName(path),"two-models.png");
            }
            catch(Exception ex){Finish(path,report,ex.ToString());yield break;}
            yield return null;
            try
            {
                Check("paused_poses_survive_player_frame",Difference(firstAtOne,a.Motion.PosePositions())<.00001f&&Difference(secondAtOne,b.Motion.PosePositions())<.00001f);
                PocSmoke.CaptureImage(Camera.main,report.screenshot);
                Check("two_model_gpu_image_created",File.Exists(report.screenshot));
                retired=a.Owner.OwnedResources.Where(x=>x!=null).ToArray();surviving=b.Owner.OwnedResources.Where(x=>x!=null).ToArray();
                scene.Remove(a.Id);
            }
            catch(Exception ex){Finish(path,report,ex.ToString());yield break;}
            yield return null;
            try
            {
                Check("remove_releases_only_selected_resources",scene.Objects.Count==1&&retired.All(x=>x==null)&&surviving.All(x=>x!=null));
                scene.Playing=true;scene.Advance(.25);
                Check("surviving_character_keeps_playing",b.Motion.TimeSeconds==1.25f&&scene.Seconds==1.25);
                var replacement=Path.Combine(Path.GetDirectoryName(path),"replacement.vmd");
                PocPhysicsReferenceProbe.WriteFixture(replacement,.25f);
                var id=b.Id;scene.ReplaceMotion(id,replacement);
                Check("valid_motion_replacement_keeps_id_and_clock",b.Id==id&&b.MotionSource==replacement&&b.Motion.TimeSeconds==1.25f);
                scene.AutomaticClock=true;
            }
            catch(Exception ex){Finish(path,report,ex.ToString());yield break;}
            yield return new WaitForSecondsRealtime(.15f);
            try
            {
                scene.AutomaticClock=false;
                report.realtimeSeconds=(float)scene.Seconds-1.25f;
                Check("actual_player_frames_advance_shared_clock",report.realtimeSeconds>.05f&&Math.Abs(b.Motion.TimeSeconds-scene.Seconds)<.00001);
                scene.Clear();
            }
            catch(Exception ex){Finish(path,report,ex.ToString());yield break;}
            yield return null;
            try{Check("clear_releases_remaining_owner",scene.Objects.Count==0&&scene.Seconds==0&&!scene.Playing&&surviving.All(x=>x==null));Finish(path,report,null);}
            catch(Exception ex){Finish(path,report,ex.ToString());}
        }
        private void Finish(string path,Report report,string error)
        {
            report.success=string.IsNullOrEmpty(error);report.error=error??"";report.checks=checks.ToArray();
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!report.success)Debug.LogError(error);
            Application.Quit(report.success?0:1);
        }
    }
}
