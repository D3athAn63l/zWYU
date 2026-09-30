// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Recovers the "full nearby-resource candidates" that While You're Up's construction-supply hook inspected (BeforeCarryDetour.cs,
// WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin, without touching IL.
//
// Why this exists: vanilla's ResourceDeliverJobFor collects candidate stacks into the private static list `resourcesAvailable`
// (via the private `FindAvailableNearbyResources(Thing, Pawn, out int)`), and THEN TRIMS that list (`RemoveRange`, `Remove(foundRes)`) to build
// the delivery job. The job that a postfix sees (`targetA` + `targetQueueA`) is therefore only a subset of what the original inspected -
// a larger stack that vanilla did not need to fill the carrying load is simply not in it.
//
// Approach (chosen over capturing state from a second hook, and over copying the collector): the postfix asks vanilla's OWN collector again,
// for the resource vanilla just selected, and reads the list it fills. That is vanilla's calculation, not a reproduction of it, it runs only
// for real (non-forced, non-HasJobOnThing) decisions that already passed every cheap zWYU check, and it has no shared capture state that
// could go stale or need an owner. Re-running it is harmless: the list is cleared at the start of every use, no other code reads it
// (verified: it is only used inside WorkGiver_ConstructDeliverResources), and vanilla has already finished with it for this call.
// Everything is bound by reflection ONCE at startup (SupplyAnchor.Verify); any mismatch switches Feature.SupplyHook off with a reason.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace zWYU
{
    internal static class SupplyNearbyResources
    {
        // private void FindAvailableNearbyResources(Thing firstFoundResource, Pawn pawn, out int resTotalAvailable)
        delegate void CollectDelegate(WorkGiver_ConstructDeliverResources giver, Thing firstFoundResource, Pawn pawn, out int resTotalAvailable);

        static CollectDelegate collect;
        static FieldInfo       listField; // private static readonly List<Thing> resourcesAvailable

        static readonly List<int> stackCounts = new List<int>(16);

        public static bool IsBound => collect != null && listField != null;

        /// <summary>Binds vanilla's private collector and its list. Returns null on success, otherwise why it cannot be used.</summary>
        public static string Bind() {
            try {
                var method = AccessTools.DeclaredMethod(
                    typeof(WorkGiver_ConstructDeliverResources), "FindAvailableNearbyResources", new[] { typeof(Thing), typeof(Pawn), typeof(int).MakeByRefType() });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                    return "WorkGiver_ConstructDeliverResources.FindAvailableNearbyResources(Thing, Pawn, out int) no longer exists in that form.";

                var field = AccessTools.DeclaredField(typeof(WorkGiver_ConstructDeliverResources), "resourcesAvailable");
                if (field == null || !field.IsStatic || !typeof(List<Thing>).IsAssignableFrom(field.FieldType))
                    return "WorkGiver_ConstructDeliverResources.resourcesAvailable is no longer a static List<Thing>.";

                collect   = (CollectDelegate)Delegate.CreateDelegate(typeof(CollectDelegate), method);
                listField = field;
                return null;
            } catch (Exception e) {
                collect   = null;
                listField = null;
                return "could not bind vanilla's nearby-resource collector: " + e.Message;
            }
        }

        /// <summary>
        /// The largest stack (first maximum, vanilla's order) among the FULL candidate list vanilla collects for <paramref name="foundRes"/>,
        /// i.e. what the original inspected. Null if unbound or the list is empty (the caller then declines the detour).
        /// </summary>
        public static Thing LargestNearbyStack(WorkGiver_ConstructDeliverResources giver, Pawn pawn, Thing foundRes, out int candidateCount) {
            candidateCount = 0;
            if (!IsBound) return null;

            collect(giver, foundRes, pawn, out _);
            var list = (List<Thing>)listField.GetValue(null);
            if (list == null || list.Count == 0) return null;

            stackCounts.Clear();
            for (var i = 0; i < list.Count; i++)
                stackCounts.Add(list[i] == null ? int.MinValue : list[i].stackCount);
            candidateCount = list.Count;

            var index = SupplyRules.IndexOfLargest(stackCounts);
            return index < 0 ? null : list[index];
        }
    }
}
