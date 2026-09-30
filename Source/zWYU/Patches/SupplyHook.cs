// SPDX-License-Identifier: AGPL-3.0-or-later
//
// REPLACES the original's IL transpiler on WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor (BeforeCarryDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin.
//
// Semantic of the original: "when the builder has found the resource they will carry to a blueprint/frame, BEFORE creating the delivery
// job, consider hauling the largest nearby stack of that resource to storage nearer the site first - but only if that stack is larger
// than what is currently needed."
// The original spliced its call in after `FindAvailableNearbyResources`, reaching into a compiler-generated closure class for the
// `need` local and into IL local slots for `job`/`foundRes`. In 1.6 the method is again restructured around closures.
//
// The 1.6 hook is a Postfix on ResourceDeliverJobFor that may REPLACE the returned job. What it takes from vanilla's result is only
// `targetA` (the resource vanilla selected) and the fact that a real HaulToContainer delivery job exists. It deliberately does NOT
// rebuild the original's two inputs from the job, because the job does not carry them faithfully:
//   * current need   -> recomputed with vanilla's own APIs (see BeforeCarry.TryCreateForSupply); the job's count is not the need, and
//                       TotalMaterialCost() is the total cost, not what is still needed;
//   * nearby stacks  -> vanilla's private collector is re-run for the selected resource (SupplyNearbyResources), because vanilla trims
//                       its candidate list before building the job (`targetQueueA` is a subset).
// If anything is off, the vanilla job is left untouched.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    /// <summary>
    /// The scanners' `HasJobOnThing` only asks "is there a job?". Any non-null vanilla job stays non-null after our postfix,
    /// so evaluating (and allocating) a detour there would be pure overhead - and a `HasJobOnThing` evaluation must never
    /// count as a detour decision. Depth > 0 means "inside a HasJobOnThing call".
    /// </summary>
    internal static class SupplyScanSuppression
    {
        public static int Depth;
    }

    [HarmonyPatch]
    internal static class ResourceDeliverJobFor_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor", new[] { typeof(Pawn), typeof(IConstructible), typeof(bool), typeof(bool) });

        [HarmonyPostfix]
        static void BeforeSupplyDetour(WorkGiver_ConstructDeliverResources __instance, Pawn pawn, IConstructible c, bool forced, ref Job __result) {
            if (__result == null || forced || SupplyScanSuppression.Depth > 0) return; // forced: a player's explicit order is never redirected
            if (!Features.IsAvailable(Feature.SupplyHook)) return;

            try {
                var detour = BeforeCarry.TryCreateForSupply(pawn, __instance, c, __result);
                if (detour != null)
                    __result = detour;
            } catch (Exception e) {
                Features.RecordFault(Feature.SupplyHook, nameof(ResourceDeliverJobFor_Patch), e); // vanilla's job is left in place
            }
        }
    }

    [HarmonyPatch]
    internal static class SupplyHasJobOnThing_Patch
    {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.DeclaredMethod(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_Scanner.HasJobOnThing));
            yield return AccessTools.DeclaredMethod(typeof(WorkGiver_ConstructDeliverResourcesToFrames), nameof(WorkGiver_Scanner.HasJobOnThing));
        }

        [HarmonyPrefix]
        static void Enter() => SupplyScanSuppression.Depth++;

        // Finalizer so the counter is restored even if the check throws.
        [HarmonyFinalizer]
        static Exception Exit(Exception __exception) {
            if (SupplyScanSuppression.Depth > 0) SupplyScanSuppression.Depth--;
            return __exception;
        }
    }

    /// <summary>Startup verification that vanilla's supply logic still has the shape the supply hook's inputs are derived from.</summary>
    internal static class SupplyAnchor
    {
        /// <summary>
        /// Reads vanilla's IL (never patches it) and binds the private collector. Returns null if fine, otherwise the reason the feature cannot work.
        /// Pinned assumptions: ResourceDeliverJobFor(Pawn, IConstructible, bool, bool) exists; it derives the current need from
        /// IConstructible.ThingCountNeeded / EnrouteUtility.GetSpaceRemainingWithEnroute (which is what the hook recomputes);
        /// and it calls FindAvailableNearbyResources, whose full candidate list the hook re-collects.
        /// </summary>
        public static string Verify() {
            try {
                var deliver = AccessTools.DeclaredMethod(
                    typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor", new[] { typeof(Pawn), typeof(IConstructible), typeof(bool), typeof(bool) });
                if (deliver == null)
                    return "WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor(Pawn, IConstructible, bool, bool) no longer exists in that form.";

                var bindProblem = SupplyNearbyResources.Bind();
                if (bindProblem != null) return bindProblem;

                var thingCountNeeded = AccessTools.DeclaredMethod(typeof(IConstructible), nameof(IConstructible.ThingCountNeeded));
                var spaceRemaining   = AccessTools.DeclaredMethod(typeof(EnrouteUtility), nameof(EnrouteUtility.GetSpaceRemainingWithEnroute));
                var findNearby       = AccessTools.DeclaredMethod(typeof(WorkGiver_ConstructDeliverResources), "FindAvailableNearbyResources");
                if (thingCountNeeded == null || spaceRemaining == null)
                    return "IConstructible.ThingCountNeeded or EnrouteUtility.GetSpaceRemainingWithEnroute no longer exist; the current material need cannot be determined the way vanilla does.";

                var instructions = PatchProcessor.GetOriginalInstructions(deliver);
                if (!instructions.Any(ci => ci.Calls(findNearby)))
                    return "vanilla's ResourceDeliverJobFor no longer calls FindAvailableNearbyResources; the nearby-resource candidates cannot be recovered.";
                if (!instructions.Any(ci => ci.Calls(thingCountNeeded)) || !instructions.Any(ci => ci.Calls(spaceRemaining)))
                    return "vanilla's ResourceDeliverJobFor no longer derives the current need from ThingCountNeeded / GetSpaceRemainingWithEnroute; zWYU's copy of that rule may be stale.";

                return null;
            } catch (Exception e) {
                return "could not inspect vanilla ResourceDeliverJobFor: " + e.Message;
            }
        }
    }
}
