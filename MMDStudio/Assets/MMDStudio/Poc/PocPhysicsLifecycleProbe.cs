using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocPhysicsLifecycleProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, source, backend;
            public string policy = "typed stateful solver; precise direct VMD inputs; 60Hz; reset/preroll seeks; 3sec controlled motion";
            public int comparisons;
            public float maximumPositionError, maximumRotationError;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed)
        {
            if (!passed) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        private void Compare(Report report, MmdPocMotion motion, Vector3[] positions, Quaternion[] rotations)
        {
            var p=motion.PosePositions(); var q=motion.PoseRotations();
            for(var i=0;i<p.Length;i++)
            {
                var distance=Vector3.Distance(p[i],positions[i]);
                var angle=q[i].Equals(rotations[i])?0:Quaternion.Angle(q[i],rotations[i]);
                if(float.IsNaN(distance)||float.IsInfinity(distance)||float.IsNaN(angle)||float.IsInfinity(angle)||p[i].magnitude>100)
                    throw new InvalidOperationException("Nonfinite or out-of-bound pose");
                report.maximumPositionError=Mathf.Max(report.maximumPositionError,distance);
                report.maximumRotationError=Mathf.Max(report.maximumRotationError,angle);
                report.comparisons++;
                if(distance>.00001f||angle>.05f)throw new InvalidOperationException("Stateful replay differs at bone "+i+": "+distance+" m / "+angle+" deg");
            }
        }
        public IEnumerator Run(PocWorkspace workspace, string path)
        {
            var report=new Report{source=PocArguments.Value("-pocPmx")};
#if ENABLE_IL2CPP
            report.backend="IL2CPP";
#else
            report.backend="Mono";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var fixture=Path.Combine(Path.GetDirectoryName(path),"lifecycle.vmd");
            PocPhysicsReferenceProbe.WriteFixture(fixture);
            workspace.Initialize(false,"direct-default-ik");
            yield return workspace.LoadModel(report.source,fixture);
            if(workspace.Motion==null){Finish(path,report,workspace.Error);yield break;}
            var motion=workspace.Motion; motion.Playing=false;
            yield return null;
            MmdPhysicsPlayback session=null;
            try
            {
                var manager=workspace.Current.mmdTransformResult.transformManager;
                var p=new Vector3[181][];var q=new Quaternion[181][];
                manager.ResetToBindPose();
                var reference=new PocPhysicsPhaseTrace{RetainPhysicsSolverState=true,RetainOtherSolverHistory=true};
                motion.DiagnosticPhysicsSolve=reference.Solve;
                try
                {
                    for(var frame=0;frame<=180;frame++)
                    {
                        motion.SampleWithPhysics(frame/60f,frame==0?0:1f/60);
                        p[frame]=motion.PosePositions();q[frame]=motion.PoseRotations();
                    }
                }
                finally{motion.DiagnosticPhysicsSolve=null;}
                session=new MmdPhysicsPlayback(motion,workspace.Current);
                Compare(report,motion,p[0],q[0]);
                session.Playing=true;
                for(var frame=1;frame<=180;frame++)
                {
                    session.Advance(1.0/60);
                    CheckTick(session,frame);
                    Compare(report,motion,p[frame],q[frame]);
                }
                Check("typed_adapter_matches_retained_diagnostic_all_bones_181_poses",true);
                Check("one_solve_per_tick_plus_zero_seed",session.SolveCount==181);
                session.Playing=false;
                var count=session.SolveCount;
                session.Advance(2);
                Check("pause_does_not_advance",count==session.SolveCount&&session.Tick==180);
                Compare(report,motion,p[180],q[180]);
                var duplicateRejected=false;
                try{using(var duplicate=new MmdPhysicsPlayback(motion,workspace.Current)) { }}catch(InvalidOperationException){duplicateRejected=true;}
                Check("second_physics_owner_rejected",duplicateRejected);
                var overwriteRejected=false;
                try{motion.Sample(0);}catch(InvalidOperationException){overwriteRejected=true;}
                Check("external_pose_overwrite_rejected",overwriteRejected&&session.Tick==180);
                session.Seek(0);session.Playing=true;
                for(var i=0;i<120;i++)session.Advance(1.0/120);
                Check("fractional_clock_chunks_produce_60_ticks",session.Tick==60);
                Compare(report,motion,p[60],q[60]);
                session.Advance(1.0/120);session.Playing=false;session.Advance(3);session.Playing=true;session.Advance(1.0/120);
                Check("pause_preserves_fraction_without_accumulating_paused_time",session.Tick==61);
                Compare(report,motion,p[61],q[61]);
                session.Seek(1.237f);
                Check("fractional_seek_floors_to_tick_74",session.Tick==74);
                Compare(report,motion,p[74],q[74]);
                session.Seek(.5f);Compare(report,motion,p[30],q[30]);
                session.Seek(1.237f);Compare(report,motion,p[74],q[74]);
                Check("backward_and_repeated_seek_reconstruct",true);
                var rejected=false;
                try{session.Seek(float.NaN);}catch(ArgumentOutOfRangeException){rejected=true;}
                Check("invalid_seek_rejected_without_tick_change",rejected&&session.Tick==74);
                session.Stop();Compare(report,motion,p[0],q[0]);
                Check("stop_resets_pose_and_clock",!session.Playing&&session.Tick==0&&session.PendingSeconds==0);
                session.Seek(179f/60);session.Loop=true;session.Playing=true;session.Advance(1.0/60);
                Check("loop_resets_at_duration",session.Tick==0&&session.LoopCount==1);
                Compare(report,motion,p[0],q[0]);
                session.Advance(3);
                Check("second_loop_reconstructs_same_start",session.Tick==0&&session.LoopCount==2);
                Compare(report,motion,p[0],q[0]);
                session.Advance(5);
                Check("stall_budget_retains_unconsumed_time",session.Tick==60&&Math.Abs(session.PendingSeconds-1)<.000001);
                session.Advance(0);
                Check("retained_stall_time_is_consumed_without_new_elapsed_time",session.Tick==120&&Math.Abs(session.PendingSeconds)<.000001);
                Compare(report,motion,p[120],q[120]);
                session.Loop=false;session.Seek(3);session.Advance(1.0/60);
                Check("nonloop_end_pauses",!session.Playing&&session.Tick==180);
                Compare(report,motion,p[180],q[180]);
                motion.Playing=true;rejected=false;
                try{session.Advance(0);}catch(InvalidOperationException){rejected=true;}
                motion.Playing=false;
                Check("second_clock_owner_rejected",rejected);
                var replacement=Path.Combine(Path.GetDirectoryName(path),"replacement.vmd");
                PocPhysicsReferenceProbe.WriteFixture(replacement,.5f);
                motion.Load(workspace.Current,replacement,"direct-default-ik");motion.Playing=false;
                rejected=false;
                try{session.Seek(1);}catch(InvalidOperationException){rejected=true;}
                Check("clip_replacement_retires_old_session",rejected);
                session.Dispose();
                session=new MmdPhysicsPlayback(motion,workspace.Current);
                session.Seek(1);
                var changed=false;var replacementPose=motion.PosePositions();
                for(var i=0;i<replacementPose.Length;i++)if(Vector3.Distance(replacementPose[i],p[60][i])>.02f)changed=true;
                Check("replacement_motion_has_new_pose",changed);
                session.Dispose();rejected=false;
                try{session.Advance(1);}catch(InvalidOperationException){rejected=true;}
                Check("disposed_session_rejects_advancement",rejected);
                Finish(path,report,null);
            }
            catch(Exception ex){Finish(path,report,ex.ToString());}
            finally{session?.Dispose();motion.DiagnosticPhysicsSolve=null;}
        }
        private static void CheckTick(MmdPhysicsPlayback session,int frame)
        {if(session.Tick!=frame)throw new InvalidOperationException("Unexpected simulation tick");}
        private void Finish(string path,Report report,string error)
        {
            report.success=string.IsNullOrEmpty(error);report.error=error??"";report.checks=checks.ToArray();
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!report.success)Debug.LogError(error);
            Application.Quit(report.success?0:1);
        }
    }
}
