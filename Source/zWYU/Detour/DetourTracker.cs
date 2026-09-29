// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Replaces the original's `detours` WeakDictionary + `BaseDetour.Deactivate/SetOrAddDetour/CatchLoop_Job/AlreadyHauling`
// (BaseDetour.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin. Same purpose, explicit lifecycle.

using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    /// <summary>
    /// Owns all per-pawn detour state. State machine of one detour, per pawn:
    ///
    ///   (search picks a haul job)  --Issue-->  PENDING  --driver.Notify_Starting-->  ACTIVE  --job ends (any condition)-->  gone
    ///
    ///   PENDING: the haul job exists but has not started. It may never start (a WorkGiver's HasJobOnThing check throws the job
    ///            away; vanilla may reject it). A newer PENDING replaces an older one; nothing needs cleaning up.
    ///   ACTIVE : the pawn's current job is the haul job. Removed by the driver's finish action, which runs for success,
    ///            failure and cancellation alike. Also removed when the pawn despawns/dies, and self-healed on read whenever
    ///            <c>pawn.CurJob</c> no longer is the haul job.
    ///
    /// Both slots hold their value weakly per pawn (<see cref="PawnSlot{T}"/>), so nothing here can outlive its owner.
    /// The only other state is the same-tick loop guard, stored as plain integers (no object references).
    /// </summary>
    internal static class DetourTracker
    {
        static readonly PawnSlot<DetourPlan> pending = new PawnSlot<DetourPlan>();
        static readonly PawnSlot<DetourPlan> active  = new PawnSlot<DetourPlan>();

        // Loop guard (original: BaseDetour.lastPawn / lastFrameCount): a pawn is issued at most one detour per game tick.
        // Without it a haul job that fails its pre-toil reservations is retried within the same tick until vanilla reports
        // "started 10 jobs in one tick".  Differences from the original, both deliberate:
        //   * the unit is the game tick (RealTime.frameCount is a render frame; several ticks can share one at >1x speed);
        //   * only an ISSUED detour arms the guard. The original also armed it when a search found nothing, which silently
        //     suppressed the opportunity check for whatever job vanilla was about to start in the same frame.
        // Two detours in a row are additionally prevented by identity, not timing: see IsDetourJob.
        static int    lastIssuedPawnId = -1;
        static int    lastIssuedTick   = -1;
        static string lastIssuedText;

        static int Now => Find.TickManager.TicksGame;

        // ---- queries ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// True if zWYU must not start another detour for this pawn right now. <paramref name="startingJob"/> is the vanilla job the
        /// pawn is about to start, when known (vanilla marks HaulToCell/HaulToContainer as opportunistic-prefix-capable, so a zWYU
        /// haul job would otherwise be offered yet another detour the moment it starts).
        /// (Original: `AlreadyHauling`, minus the PUAH inventory check which now lives in <see cref="Eligibility"/>.)
        /// </summary>
        public static bool IsBusy(Pawn pawn, Job startingJob, out string why) {
            if (startingJob != null && IsDetourJob(pawn, startingJob)) {
                why = "the job being started is itself a zWYU detour haul";
                return true;
            }
            if (pawn.thingIDNumber == lastIssuedPawnId && Now == lastIssuedTick) {
                why = "a detour was already issued for this pawn this tick";
                return true;
            }
            if (ActiveFor(pawn) != null) {
                why = "pawn is mid-detour";
                return true;
            }
            why = null;
            return false;
        }

        /// <summary>True if <paramref name="job"/> is a haul job zWYU created for this pawn (pending or active).</summary>
        public static bool IsDetourJob(Pawn pawn, Job job) {
            if (job == null || job.loadID < 0) return false; // a cleared/pooled job has loadID -1: never a match
            if (pending.TryGet(pawn, out var pend) && pend.JobLoadId == job.loadID) return true;
            return active.TryGet(pawn, out var act) && act.JobLoadId == job.loadID;
        }

        /// <summary>The detour the pawn is executing right now, or null. Self-heals stale entries.</summary>
        public static DetourPlan ActiveFor(Pawn pawn) {
            if (!active.TryGet(pawn, out var plan))
                return null;
            var curJob = pawn.CurJob;
            if (curJob == null || curJob.loadID != plan.JobLoadId) {
                active.Remove(pawn);
                if (Diag.Summary)
                    Diag.Decision(Diag.Lifecycle, $"dropped stale active detour for {Diag.Describe(pawn)} (current job is no longer the haul): {plan}");
                return null;
            }
            return plan;
        }

        /// <summary>The active detour, only if <paramref name="job"/> is its haul job.</summary>
        public static DetourPlan ActiveForJob(Pawn pawn, Job job) {
            var plan = ActiveFor(pawn);
            return plan != null && job != null && job.loadID >= 0 && plan.JobLoadId == job.loadID ? plan : null;
        }

        // ---- transitions -----------------------------------------------------------------------------------------------------

        /// <summary>Attach a freshly chosen plan to its haul job. The plan is PENDING until the job's driver starts.</summary>
        public static void Issue(DetourPlan plan, Job haulJob) {
            plan.JobLoadId = haulJob.loadID;
            pending.Set(plan.Pawn, plan);
            lastIssuedPawnId = plan.Pawn.thingIDNumber;
            lastIssuedTick   = Now;
            lastIssuedText   = Diag.Summary ? plan.ToString() : plan.Kind.ToString();
            if (Diag.Summary)
                Diag.Decision(plan.Kind == DetourKind.Opportunity ? Diag.Opportunity : Diag.BeforeCarry, $"SELECTED {plan}");
        }

        /// <summary>PENDING -> ACTIVE. Called from the haul driver's Notify_Starting. Registers the finish action that ends the detour.</summary>
        public static void OnHaulDriverStarting(JobDriver driver) {
            var pawn = driver.pawn;
            var job  = driver.job;
            if (pawn == null || job == null) return;
            if (!pending.TryGet(pawn, out var plan) || plan.JobLoadId != job.loadID) return;

            pending.Remove(pawn);
            active.Set(pawn, plan);
            driver.AddFinishAction(condition => OnHaulFinished(plan, condition));
            if (Diag.Summary)
                Diag.Decision(Diag.Lifecycle, $"STARTED {plan}");
        }

        static void OnHaulFinished(DetourPlan plan, JobCondition condition) {
            try {
                if (active.TryGet(plan.Pawn, out var current) && ReferenceEquals(current, plan))
                    active.Remove(plan.Pawn);

                if (!Diag.Summary) return;
                var resumes = plan.Kind == DetourKind.Opportunity && plan.OriginalJob != null && !plan.OriginalJobDiscarded;
                var outcome = condition == JobCondition.Succeeded
                    ? "completed"
                    : $"ABORTED ({condition})";
                var tail = plan.Kind == DetourKind.Opportunity
                    ? resumes ? $"; vanilla resumes the original job {Diag.Describe(plan.OriginalJob)} from the job queue" : "; original job was discarded, vanilla thinks for a new job"
                    : "; vanilla re-evaluates the pawn's work normally";
                Diag.Decision(Diag.Lifecycle, $"detour {outcome}{tail}: {plan}");
            } catch (Exception e) {
                // A logging problem must never disturb job cleanup.
                Diag.ErrorOnce(Diag.Lifecycle, "exception while finishing a detour", e);
            }
        }

        /// <summary>The pawn's job queue was cleared: an opportunity's queued original job no longer exists.</summary>
        public static void OnQueueCleared(Pawn pawn) {
            if (active.TryGet(pawn, out var plan))
                plan.OriginalJobDiscarded = true;
            if (pending.TryGet(pawn, out var pend))
                pend.OriginalJobDiscarded = true;
        }

        /// <summary>Drop every trace of the pawn (despawned, destroyed, changed map).</summary>
        public static void Forget(Pawn pawn) {
            active.Remove(pawn);
            pending.Remove(pawn);
            if (pawn.thingIDNumber == lastIssuedPawnId) {
                lastIssuedPawnId = -1;
                lastIssuedText   = null;
            }
        }

        /// <summary>For "10 jobs in one tick" diagnostics: the detour zWYU last issued to this pawn, if it was this tick.</summary>
        public static string LastIssuedThisTickFor(Pawn pawn) =>
            pawn != null && pawn.thingIDNumber == lastIssuedPawnId && Now == lastIssuedTick ? lastIssuedText : null;
    }
}
