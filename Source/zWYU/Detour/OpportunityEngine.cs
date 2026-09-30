// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The decision made when vanilla is about to scan for an opportunistic haul. Ported from the body of the original
// `Pawn_JobTracker__TryOpportunisticJob_Patch.TryOpportunisticJob` (OpportunityDetour.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin.

using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    internal static class OpportunityEngine
    {
        /// <summary>
        /// Called from inside vanilla's TryOpportunisticJob, i.e. only after vanilla's own preconditions passed (job allows an
        /// opportunistic prefix, pawn is a spawned, non-drafted, non-downed humanlike colonist, job is not player-forced, the target
        /// is valid and farther than 3 cells, the pawn is capable of hauling ...).
        /// Returns the haul job to run first, or null for "no detour" (vanilla then proceeds unmodified).
        /// </summary>
        public static Job Run(Pawn pawn, Job job) {
            var settings = ZwyuMod.Settings;
            if (!pawn.Spawned) return null;

            if (DetourTracker.IsBusy(pawn, job, out var busyWhy)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.Opportunity, $"skip {Diag.Describe(pawn)} / {Diag.Describe(job)}: {busyWhy}");
                return null;
            }
            if (PickUpAndHaulCompat.HasPendingInventoryHaul(pawn)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.Opportunity, $"skip {Diag.Describe(pawn)}: still carrying Pick Up And Haul inventory");
                return null;
            }

            // Haul extra bill ingredients closer (original ":BeforeBillDetour", implemented right here in TryOpportunisticJob).
            if (job.def == JobDefOf.DoBill && settings.HaulBeforeCarry_Bills && !CommonSenseCompat.BillHaulingHandledByCommonSense) {
                var billJob = BeforeCarry.TryCreateForBill(pawn, job);
                if (billJob != null) return billJob;
            }

            if (Eligibility.IsCaravanPreparationJob(job.def)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.Opportunity, $"skip {Diag.Describe(pawn)}: preparing a caravan");
                return null;
            }
            if (Eligibility.IsBleeding(pawn)) { // :Bleeding
                if (Diag.Verbose)
                    Diag.Trace(Diag.Opportunity, $"skip {Diag.Describe(pawn)}: bleeding");
                return null;
            }

            // use first ingredient location if bill because our pawn will go directly to it
            var jobTarget = job.targetA;
            if (job.def == JobDefOf.DoBill && job.targetQueueB != null && job.targetQueueB.Count > 0 && job.targetQueueB[0].IsValid)
                jobTarget = job.targetQueueB[0]; // (original used FirstOrDefault()?? which yielded an invalid target for an empty queue)

            return OpportunitySearch.FindJob(pawn, job, jobTarget);
        }
    }
}
