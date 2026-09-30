// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Ported from While You're Up's `BeforeCarryDetour_Job`, `BeforeSupplyDetour_Job` and the DoBill branch of `TryOpportunisticJob`
// (BeforeCarryDetour.cs / OpportunityDetour.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin.
//
// "Haul before carry": a builder is about to carry construction supplies (or a crafter bill ingredients) from where they lie to
// the blueprint/frame (workbench). If the storage that best serves that trip is CLOSER to the destination than the material is now,
// the pawn first hauls the material to that storage ("headed closer to X"), grabbing extras with vanilla's own duplicate pickup.
// When the haul ends, vanilla asks the same work giver again and the delivery is now shorter.

using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    internal static class BeforeCarry
    {
        /// <summary>
        /// Original: `BeforeCarryDetour_Job`. <paramref name="carryTarget"/> is where the material is needed; <paramref name="thing"/> the stack to move.
        /// Returns a HaulToCell job (registered with the DetourTracker) or null.
        /// </summary>
        public static Job TryCreate(Pawn pawn, LocalTargetInfo carryTarget, Thing thing, DetourKind kind) {
            if (thing == null || !thing.Spawned || thing.Map != pawn.Map) return null;
            if (thing.ParentHolder is Pawn_InventoryTracker) return null;

            // try to avoid haul-before-carry when there are no extras to grab
            // proper way is to recheck after grabbing everything, but here's a simple hack to at least avoid it with stone chunks
            if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 2)) return null; // already going for 1, so 2 to check for another

            if (!StorageSearch.TryFindMidwayStoreCell(
                    thing, LocalTargetInfo.Invalid, carryTarget, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out var storeCell, true)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.BeforeCarry, $"  no better storage for {Diag.Describe(thing)} toward {Diag.Describe(carryTarget)}");
                return null;
            }

            var fromHereSquared  = thing.Position.DistanceToSquared(carryTarget.Cell);
            var fromStoreSquared = storeCell.DistanceToSquared(carryTarget.Cell);
            if (!(fromStoreSquared < fromHereSquared)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.BeforeCarry, $"  storage {storeCell} is not closer to {carryTarget.Cell} than {Diag.Describe(thing)} already is");
                return null;
            }

            DebugDraw.BeforeCarry(pawn, thing, storeCell, carryTarget);

            var haulJob = HaulAIUtility.HaulToCellStorageJob(pawn, thing, storeCell, false);
            if (haulJob == null) return null;

            var why = Diag.Summary ? $"{Diag.Describe(thing)} is {thing.Position.DistanceTo(carryTarget.Cell):F1} from {carryTarget.Cell}, storage {storeCell} is {storeCell.DistanceTo(carryTarget.Cell):F1}" : null;
            DetourTracker.Issue(new DetourPlan(pawn, kind, carryTarget, thing, storeCell, null, why), haulJob);
            return haulJob;
        }

        /// <summary>
        /// The bill branch of the original TryOpportunisticJob patch: for a DoBill job the pawn is about to start, try each ingredient.
        /// vanilla has already required allowOpportunisticPrefix, a valid non-forbidden target, humanlike player pawn, not forced, etc.
        /// </summary>
        public static Job TryCreateForBill(Pawn pawn, Job billJob) {
            var queue  = billJob.targetQueueB;
            var counts = billJob.countQueue;
            if (queue == null || counts == null) return null;

            for (var i = 0; i < queue.Count; i++) {
                var ingredient = queue[i].Thing;
                if (ingredient == null) continue;

                // Only haul an ingredient to storage if there are extras. (Too difficult to know in advance otherwise; original v3.1.0.)
                if (i < counts.Count && ingredient.stackCount <= counts[i]) continue;

                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, ingredient, false)) continue; // fast check

                // Permitted when bleeding because it facilitates whatever bill is important enough to do while bleeding;
                //  may save precious time going back for ingredients, unless we want only 1 medicine ASAP. It's a trade-off. (original :Bleeding)
                var job = TryCreate(pawn, billJob.targetA, ingredient, DetourKind.BeforeCarryBill);
                if (job != null) return job;
            }
            return null;
        }

        /// <summary>
        /// Original: `BeforeSupplyDetour_Job`, now driven from the RESULT of vanilla's ResourceDeliverJobFor instead of from inside it.
        /// <paramref name="deliverJob"/> is vanilla's HaulToContainer job; only its `targetA` (the resource vanilla selected) is used from it.
        ///
        /// The two inputs of the original decision are NOT read from the job (it does not carry them faithfully):
        ///   * the CURRENT need for that material comes from vanilla's own calculation for this call - `IHaulEnroute.GetSpaceRemainingWithEnroute`
        ///     when the constructible is an IHaulEnroute, otherwise `IConstructible.ThingCountNeeded`; NOT `TotalMaterialCost()`, which is the whole
        ///     cost including what has already been delivered;
        ///   * the candidate stacks come from vanilla's collector re-run for that resource (SupplyNearbyResources), because vanilla trims the list
        ///     before building the job, so `targetQueueA` is only a subset of what the original inspected.
        /// </summary>
        public static Job TryCreateForSupply(Pawn pawn, WorkGiver_ConstructDeliverResources giver, IConstructible constructible, Job deliverJob) {
            var settings = ZwyuMod.Settings;
            if (!settings.Enabled || !settings.HaulBeforeCarry_Supplies) return null;
            if (deliverJob == null || deliverJob.def != JobDefOf.HaulToContainer) return null;
            if (constructible is Blueprint_Install) return null;
            if (!(constructible is Thing constructibleThing) || !constructibleThing.Spawned) return null;

            var foundRes = deliverJob.targetA.Thing;
            if (foundRes == null) return null;

            if (DetourTracker.IsBusy(pawn, null, out var busyWhy)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.BeforeCarry, $"  supply detour skipped for {Diag.Describe(pawn)}: {busyWhy}");
                return null;
            }
            if (!Eligibility.PawnMayDetour(pawn)) return null;

            // How much of this material is still needed right now: the same number vanilla's loop computed (`num`) before choosing the resource.
            var def         = foundRes.def;
            var currentNeed = constructible is IHaulEnroute enroute ? enroute.GetSpaceRemainingWithEnroute(def, pawn) : constructible.ThingCountNeeded(def);
            if (currentNeed <= 0) return null; // vanilla never returns a delivery job for a material that is not needed

            // Haul the largest nearby supply stack instead of the absolute closest (original v3.1.0), among the FULL candidate list.
            var mostThing = SupplyNearbyResources.LargestNearbyStack(giver, pawn, foundRes, out var candidateCount);
            if (mostThing == null) return null;

            // Only haul a construction supply to storage if there are extras (original v3.1.0): strictly more than is currently needed.
            if (!SupplyRules.ExtrasExist(mostThing.stackCount, currentNeed)) {
                if (Diag.Verbose)
                    Diag.Trace(Diag.BeforeCarry, $"  no extras for {Diag.Describe(constructibleThing)}: largest of {candidateCount} nearby stacks is {mostThing.stackCount}, current need {currentNeed}");
                return null;
            }

            // The original trusted the neighbour stacks vanilla had collected; a stack the pawn cannot actually reserve/reach would only fail the
            // haul job's pre-toil reservations, so check it exactly as the bill path does.
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, mostThing, false)) return null;

            if (Diag.Verbose)
                Diag.Trace(Diag.BeforeCarry, $"  supply candidate {Diag.Describe(mostThing)} (largest of {candidateCount} nearby stacks) vs current need {currentNeed} of {def.defName}");
            return TryCreate(pawn, constructibleThing.Position, mostThing, DetourKind.BeforeCarrySupply);
        }
    }
}
