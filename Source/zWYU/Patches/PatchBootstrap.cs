// SPDX-License-Identifier: AGPL-3.0-or-later
// New for the zWYU port. Replaces `harmony.PatchAll()` (which aborts the whole mod on the first patch that cannot apply).

using System;
using System.Text;
using HarmonyLib;

namespace zWYU
{
    /// <summary>
    /// Applies the Harmony patches feature by feature. A feature whose vanilla anchor or patch fails is disabled and reported;
    /// the others keep working, and vanilla behavior is never left half-patched in a way that matters (see each hook's header).
    /// </summary>
    internal static class PatchBootstrap
    {
        public static void Apply(Harmony harmony) {
            // Feature.OpportunityHook: opportunities + bill ingredients
            var anchorProblem = OpportunityAnchor.Verify();
            if (anchorProblem != null) {
                Features.Disable(Feature.OpportunityHook, "Pawn_JobTracker.TryOpportunisticJob", anchorProblem);
            } else if (Features.TryPatch(harmony, typeof(TryOpportunisticJob_Patch), Feature.OpportunityHook)
                       && Features.TryPatch(harmony, typeof(ThingsPotentiallyNeedingHauling_Patch), Feature.OpportunityHook)) {
                var selfTestProblem = OpportunityAnchor.SelfTest();
                if (selfTestProblem != null)
                    Features.Disable(Feature.OpportunityHook, nameof(ThingsPotentiallyNeedingHauling_Patch), selfTestProblem);
            }

            // Feature.SupplyHook: construction supplies
            if (Features.TryPatch(harmony, typeof(ResourceDeliverJobFor_Patch), Feature.SupplyHook))
                Features.TryPatch(harmony, typeof(SupplyHasJobOnThing_Patch), Feature.SupplyHook);

            // Feature.LifecycleTracking: bookkeeping + job reports; each is independent and cosmetic/diagnostic.
            Features.TryPatch(harmony, typeof(JobDriver_HaulToCell__Notify_Starting_Patch), Feature.LifecycleTracking);
            Features.TryPatch(harmony, typeof(JobDriver_HaulToCell__GetReport_Patch), Feature.LifecycleTracking);
            Features.TryPatch(harmony, typeof(Pawn_JobTracker__ClearQueuedJobs_Patch), Feature.LifecycleTracking);
            Features.TryPatch(harmony, typeof(Pawn__DeSpawn_Patch), Feature.LifecycleTracking);
            Features.TryPatch(harmony, typeof(JobUtility__TryStartErrorRecoverJob_Patch), Feature.LifecycleTracking);
        }

        public static string StatusLine() {
            var sb = new StringBuilder("loaded (RimWorld 1.6 port). ");
            sb.Append("Opportunities/bills: ").Append(Features.IsAvailable(Feature.OpportunityHook) ? "hooked" : "DISABLED").Append(". ");
            sb.Append("Construction supplies: ").Append(Features.IsAvailable(Feature.SupplyHook) ? "hooked" : "DISABLED").Append(". ");
            sb.Append("Job reports/lifecycle: ").Append(Features.IsAvailable(Feature.LifecycleTracking) ? "hooked" : "DISABLED").Append(". ");
            sb.Append("Debug logging: ").Append(Diag.Level).Append('.');
            return sb.ToString();
        }
    }
}
