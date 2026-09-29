// SPDX-License-Identifier: AGPL-3.0-or-later
//
// REPLACES the original's IL transpiler on WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor (BeforeCarryDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin.
//
// Semantic of the original: "when the builder has found the resource they will carry to a blueprint/frame, BEFORE creating the delivery
// job, consider hauling that resource (and its neighbours) to storage nearer the site first."
// The original spliced its call in after `FindAvailableNearbyResources`, reaching into a compiler-generated closure class for the
// `need` local and into IL local slots for `job`/`foundRes`. In 1.6 the method is again restructured around closures.
//
// The 1.6 hook is a Postfix on ResourceDeliverJobFor: vanilla's RESULT already carries everything the original fished out of the IL
//   targetA = the resource stack, targetQueueA = the nearby stacks picked up with it, targetC = the blueprint/frame.
// If we decide to detour we simply replace the returned job; if anything is off we leave vanilla's job untouched.

using System;
using System.Collections.Generic;
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
        static void BeforeSupplyDetour(Pawn pawn, IConstructible c, bool forced, ref Job __result) {
            if (__result == null || forced || SupplyScanSuppression.Depth > 0) return; // forced: a player's explicit order is never redirected
            if (!Features.IsAvailable(Feature.SupplyHook)) return;

            try {
                var detour = BeforeCarry.TryCreateForSupply(pawn, c, __result);
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
}
