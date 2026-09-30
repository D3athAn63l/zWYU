// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The two decisions of While You're Up's construction-supply detour (`BeforeSupplyDetour_Job`, BeforeCarryDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin, stated without any RimWorld type so they can be unit-tested (Source/zWYU.Tests links this file).
//
// The original spliced its call into vanilla's ResourceDeliverJobFor right after `FindAvailableNearbyResources`, and there it saw
//   * `need`             : the material the builder is about to fetch, with `need.count` = how much of it is CURRENTLY still needed
//                          (verified in the 1.4 binaries: the loop ran over `MaterialsNeeded()`, i.e. the remaining materials, not the total cost);
//   * `resourcesAvailable`: the FULL list of candidate stacks vanilla had just collected (the chosen stack first, then same-def stacks
//                          within 5 cells, until a carrying load is reached) - BEFORE vanilla trimmed it to build the delivery job.
// and decided:  "haul the LARGEST candidate stack to nearer storage, but only if it is larger than what is currently needed".
//
// Since RimWorld 1.5 (and in 1.6) `need.count` is no longer the current need: the loop runs over TotalMaterialCost() and computes the current need
// separately (`num`). The last shipped original (4.0.6 on 1.5) therefore compared against the WHOLE cost by accident; zWYU keeps the 4.0.4 rule
// (remaining need). See BeforeCarry.TryCreateForSupply and docs/PORTING_NOTES.md §4 and §12.2 for how each input is obtained now.

using System.Collections.Generic;

namespace zWYU
{
    internal static class SupplyRules
    {
        /// <summary>
        /// The "only if there are extras" rule (original v3.1.0). STRICT: a stack exactly as large as the current need has no extras.
        /// <paramref name="currentNeed"/> is what is still needed NOW - never the construction's total material cost. Example: a wall costing
        /// 100 steel with 90 already delivered needs 10; a nearby stack of 20 has extras (20 &gt; 10). A stack of 20 against a need of 20 has none.
        /// </summary>
        public static bool ExtrasExist(int largestNearbyStack, int currentNeed) => largestNearbyStack > currentNeed;

        /// <summary>
        /// Index of the first largest stack (vanilla's `MaxBy`: an equal later stack never replaces an earlier one), or -1 if there are none.
        /// Must be given the FULL candidate list in vanilla's order (chosen resource first), not the trimmed queue of the delivery job.
        /// </summary>
        public static int IndexOfLargest(IReadOnlyList<int> stackCounts) {
            var best      = -1;
            var bestCount = int.MinValue;
            for (var i = 0; i < stackCounts.Count; i++) {
                if (stackCounts[i] > bestCount) {
                    bestCount = stackCounts[i];
                    best      = i;
                }
            }
            return best;
        }
    }
}
