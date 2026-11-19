using System;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // Opt-in service; normal UI remains physics OFF. Owns integer 60 Hz ticks.
    // Seek rebuilds from frame zero, making its cost linear in target time.
    internal sealed class MmdPhysicsPlayback : IDisposable
    {
        internal const int Rate = 60;
        private readonly MmdPocMotion motion;
        private readonly MmdStatefulPhysicsSolver solver;
        private readonly int revision;
        private bool disposed;
        private double pending;
        internal int Tick { get; private set; }
        internal int SolveCount { get; private set; }
        internal int ResetCount { get; private set; }
        internal int LoopCount { get; private set; }
        internal bool Playing { get; set; }
        internal bool Loop { get; set; }
        internal float Seconds => Tick / (float)Rate;
        internal double PendingSeconds => pending;
        private int EndTick => (int)Math.Floor(motion.Summary.durationSeconds * (double)Rate + .0001);
        private int LoopTicks => Math.Max(1, (int)Math.Ceiling(motion.Summary.durationSeconds * (double)Rate - .0001));

        internal MmdPhysicsPlayback(MmdPocMotion motion, PMXImportResult model)
        {
            this.motion = motion;
            revision = motion.ClipRevision;
            motion.Playing = false;
            motion.AcquirePhysics(this);
            try
            {
                solver = new MmdStatefulPhysicsSolver(model.mmdTransformResult.transformManager);
                ResetSimulation();
            }
            catch { motion.ReleasePhysics(this); throw; }
        }
        private void Validate()
        {
            if (disposed || motion == null || revision != motion.ClipRevision || !motion.OwnsPhysics(this))
                throw new InvalidOperationException("Physics session is retired or its motion was replaced");
            if (motion.Playing) throw new InvalidOperationException("Physics session requires exclusive clock ownership");
        }
        private void ResetSimulation()
        {
            Validate();
            solver.Reset();
            Tick = 0;
            motion.SampleStatefulPhysics(0, 0, solver);
            SolveCount++;
            ResetCount++;
        }
        private void Step()
        {
            Tick++;
            motion.SampleStatefulPhysics(Seconds, 1f / Rate, solver);
            SolveCount++;
        }
        internal void Seek(float seconds)
        {
            Validate();
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            var target = (int)Math.Floor(Math.Max(0, Math.Min(seconds, motion.Summary.durationSeconds)) * Rate + .0001);
            ResetSimulation();
            pending = 0;
            while (Tick < target) Step();
        }
        internal void Stop()
        {
            Playing = false;
            Seek(0);
        }
        internal void Advance(double elapsedSeconds)
        {
            Validate();
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!Playing) return;
            pending += elapsedSeconds;
            // Keep excess elapsed time for subsequent calls rather than silently
            // dropping simulation steps when a rendered frame stalls.
            var budget = 240;
            while (budget-- > 0 && pending + 1e-10 >= 1.0 / Rate)
            {
                pending -= 1.0 / Rate;
                if (Loop && Tick + 1 >= LoopTicks)
                {
                    ResetSimulation();
                    LoopCount++;
                }
                else if (Tick < EndTick) Step();
                else { Playing = false; pending = 0; break; }
            }
        }
        public void Dispose()
        {
            if (motion != null) motion.ReleasePhysics(this);
            disposed = true;
            Playing = false;
        }
    }
}
