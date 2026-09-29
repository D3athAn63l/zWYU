// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Detour lifecycle hooks and job reports. Replaces the original's cleanup patches (BaseDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin, most importantly:
//   * JobDriver_HaulToCell.MakeNewToils postfix + AddFinishAction(() => Deactivate)      -> Notify_Starting postfix + finish action
//     (1.6's AddFinishAction takes an Action<JobCondition>, and patching a compiler-generated iterator was never necessary);
//   * Pawn_JobTracker.ClearQueuedJobs postfix, Pawn.Destroy postfix                        -> kept in spirit (ClearQueuedJobs, DeSpawn);
//   * JobDriver_HaulToCell.GetReport postfix                                               -> kept ("on the way to" / "closer to");
//   * JobUtility.TryStartErrorRecoverJob prefix ("offer support")                          -> now an actionable diagnostic.
// Every hook here is a cheap postfix/prefix that cannot change a job's behavior; all of them live in Feature.LifecycleTracking.

using System;
using CodeOptimist;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    /// <summary>PENDING -> ACTIVE transition; registers the finish action that ends the detour on success, failure or cancellation.</summary>
    [HarmonyPatch(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.Notify_Starting))]
    internal static class JobDriver_HaulToCell__Notify_Starting_Patch
    {
        [HarmonyPostfix]
        static void TrackDetourStart(JobDriver_HaulToCell __instance) {
            if (!Features.IsAvailable(Feature.LifecycleTracking)) return;
            try {
                DetourTracker.OnHaulDriverStarting(__instance);
            } catch (Exception e) {
                Features.RecordFault(Feature.LifecycleTracking, nameof(JobDriver_HaulToCell__Notify_Starting_Patch), e);
            }
        }
    }

    /// <summary>"... on the way to X." / "..., headed closer to X."</summary>
    [HarmonyPatch(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.GetReport))]
    internal static class JobDriver_HaulToCell__GetReport_Patch
    {
        [HarmonyPostfix]
        static void DecorateReport(JobDriver_HaulToCell __instance, ref string __result) {
            if (!Features.IsAvailable(Feature.LifecycleTracking)) return;
            try {
                var plan = DetourTracker.ActiveForJob(__instance.pawn, __instance.job);
                if (plan == null) return;

                var key = plan.Kind == DetourKind.Opportunity ? "Opportunity_LoadReport" : "HaulBeforeCarry_LoadReport";
                __result = key.ModTranslate(__result.TrimEnd('.').Named("ORIGINAL"), plan.Destination.Label.Named("DESTINATION"));
            } catch (Exception e) {
                Features.RecordFault(Feature.LifecycleTracking, nameof(JobDriver_HaulToCell__GetReport_Patch), e); // vanilla's text is kept
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ClearQueuedJobs))]
    internal static class Pawn_JobTracker__ClearQueuedJobs_Patch
    {
        [HarmonyPostfix]
        static void NoteQueueCleared(Pawn ___pawn) {
            if (___pawn != null && Features.IsAvailable(Feature.LifecycleTracking))
                DetourTracker.OnQueueCleared(___pawn);
        }
    }

    /// <summary>Despawn covers destruction, death, entering a caravan, leaving the map and (for a pawn) any map change.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    internal static class Pawn__DeSpawn_Patch
    {
        [HarmonyPrefix]
        static void ForgetDetour(Pawn __instance) {
            if (Features.IsAvailable(Feature.LifecycleTracking))
                DetourTracker.Forget(__instance);
        }
    }

    /// <summary>
    /// Vanilla's "started 10 jobs in one tick" (and friends) recovery. If zWYU had just issued a detour for that pawn this tick,
    /// say so, with the plan, so a report can be acted on. (The original printed a support-chat invitation.)
    /// </summary>
    [HarmonyPatch(typeof(JobUtility), nameof(JobUtility.TryStartErrorRecoverJob))]
    internal static class JobUtility__TryStartErrorRecoverJob_Patch
    {
        [HarmonyPrefix]
        static void ReportInvolvement(Pawn pawn, string message) {
            if (!Features.IsAvailable(Feature.LifecycleTracking)) return;
            try {
                var issued = DetourTracker.LastIssuedThisTickFor(pawn);
                var active = DetourTracker.ActiveFor(pawn);
                if (issued == null && active == null) return;
                Diag.Warn(Diag.Lifecycle,
                    $"vanilla is recovering {Diag.Describe(pawn)} from a job error (\"{message}\"). A zWYU detour was involved: "
                    + $"issued this tick = {issued ?? "no"}; active = {active?.ToString() ?? "no"}. "
                    + "Set zWYU's debug level to Summary and reproduce, then report the log at https://github.com/D3athAn63l/zWYU/issues.");
            } catch (Exception e) {
                Diag.ErrorOnce(Diag.Lifecycle, "exception while reporting a job error", e);
            }
        }
    }
}
