using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MmdStudio.Poc
{
    public sealed class PocPhysicsFeatureProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string source, backend, error, policy;
            public int steps, checks;
            public float driverRotationDelta, physicsPositionDelta, maximumReplayError;
            public float maximumPositionMagnitude;
        }
        public IEnumerator Run(PocWorkspace workspace, string path, bool realMotion)
        {
            var report=new Report{source=PocArguments.Value("-pocPmx")};
#if ENABLE_IL2CPP
            report.backend="IL2CPP";
#else
            report.backend="Mono";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var motionPath=PocArguments.Value("-pocVmd");
            if(!realMotion)
            {
                motionPath=Path.Combine(Path.GetDirectoryName(path),"feature.vmd");
                PocPhysicsReferenceProbe.WriteFixture(motionPath);
            }
            workspace.Initialize(false,"direct-default-ik");
            yield return workspace.LoadModel(report.source,motionPath);
            if(workspace.Motion==null){Finish(path,report,workspace.Error);yield break;}
            var motion=workspace.Motion;motion.Playing=false;
            yield return null;
            MmdPhysicsPlayback session=null;
            try
            {
                var manager=workspace.Current.mmdTransformResult.transformManager;
                if(realMotion)
                {
                    var requested=PocArguments.Value("-pocPhysicsSeconds")??"30";
                    var duration=requested=="full"?motion.Summary.durationSeconds:float.Parse(requested,CultureInfo.InvariantCulture);
                    if(float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0)throw new ArgumentOutOfRangeException("duration");
                    duration=Mathf.Min(duration,motion.Summary.durationSeconds);
                    report.steps=(int)Math.Floor(duration*60+.0001);
                    report.policy="typed stateful service; real VMD; 60Hz; two full-pose replays; finite/coarse 100m bounds; not physical accuracy";
                    var baseline=new Vector3[report.steps+1][];
                    var rotations=new Quaternion[report.steps+1][];
                    for(var trial=0;trial<2;trial++)
                    {
                        session?.Dispose();session=new MmdPhysicsPlayback(motion,workspace.Current);session.Playing=true;
                        for(var tick=0;tick<=report.steps;tick++)
                        {
                            if(tick>0)session.Advance(1.0/60);
                            if(session.Tick!=tick)throw new InvalidOperationException("Real-motion tick mismatch");
                            var pose=motion.PosePositions();var rotation=motion.PoseRotations();
                            for(var i=0;i<pose.Length;i++)
                            {
                                var size=pose[i].magnitude;
                                if(float.IsNaN(size)||float.IsInfinity(size)||size>100||
                                    !Finite(rotation[i].x)||!Finite(rotation[i].y)||!Finite(rotation[i].z)||!Finite(rotation[i].w))
                                    throw new InvalidOperationException("Nonfinite/out-of-bound real-motion pose at tick "+tick);
                                report.maximumPositionMagnitude=Mathf.Max(report.maximumPositionMagnitude,size);
                                if(trial==1)
                                {
                                    report.maximumReplayError=Mathf.Max(report.maximumReplayError,Vector3.Distance(baseline[tick][i],pose[i]));
                                    if(!rotation[i].Equals(rotations[tick][i])&&Quaternion.Angle(rotation[i],rotations[tick][i])>.05f)
                                        throw new InvalidOperationException("Real-motion rotation replay mismatch");
                                }
                            }
                            if(trial==0){baseline[tick]=pose;rotations[tick]=rotation;}
                            if(tick%600==0)Debug.Log("Stateful real-motion trial="+trial+" tick="+tick+"/"+report.steps);
                        }
                        report.checks++;
                    }
                    if(report.maximumReplayError>.00001f)throw new InvalidOperationException("Real-motion position replay mismatch");
                    report.checks++;
                }
                else
                {
                    report.policy="non-collinear fixture; feature enabled/disabled control; kinetic driver cannot be overwritten by dynamic body output";
                    var append=Path.GetFileNameWithoutExtension(report.source)=="append-driven";
                    var poses=new Vector3[2][];var driver=new Quaternion[2];
                    for(var trial=0;trial<2;trial++)
                    {
                        manager.solveConstraints=!append||trial==0;
                        manager.solveIK=append||trial==0;
                        session?.Dispose();session=new MmdPhysicsPlayback(motion,workspace.Current);session.Seek(1);
                        poses[trial]=motion.PosePositions();driver[trial]=workspace.Current.bones[2].rotation;
                    }
                    report.driverRotationDelta=Quaternion.Angle(driver[0],driver[1]);
                    report.physicsPositionDelta=Vector3.Distance(poses[0][3],poses[1][3]);
                    if(!Finite(report.driverRotationDelta)||report.driverRotationDelta<1)throw new InvalidOperationException("Feature did not excite kinetic driver by at least 1 degree");
                    report.checks++;
                    if(!Finite(report.physicsPositionDelta)||report.physicsPositionDelta<.0001f)throw new InvalidOperationException("Feature did not change dynamic body by at least 0.1mm");
                    report.checks++;
                    manager.solveConstraints=true;manager.solveIK=true;
                    session.Dispose();session=new MmdPhysicsPlayback(motion,workspace.Current);session.Seek(1);
                    var replay=motion.PosePositions();
                    for(var i=0;i<replay.Length;i++)
                    {
                        var delta=Vector3.Distance(replay[i],poses[0][i]);
                        if(!Finite(delta))throw new InvalidOperationException("Nonfinite feature replay");
                        report.maximumReplayError=Mathf.Max(report.maximumReplayError,delta);
                    }
                    if(report.maximumReplayError>.00001f)throw new InvalidOperationException("Feature replay failed");
                    report.steps=60;report.checks++;
                }
                Finish(path,report,null);
            }
            catch(Exception ex){Finish(path,report,ex.ToString());}
            finally{session?.Dispose();}
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static void Finish(string path,Report report,string error)
        {
            report.success=string.IsNullOrEmpty(error);report.error=error??"";
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!report.success)Debug.LogError(error);
            Application.Quit(report.success?0:1);
        }
    }
}
