// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Ported from While You're Up's `TryFindBestBetterStoreCellFor_MidwayToTarget` (StoreUtility.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin.
//
// What it does (unchanged): vanilla's StoreUtility.TryFindBestBetterStoreCellFor picks the storage cell CLOSEST TO THE THING.
// A detour wants the storage that best serves the JOURNEY, so this variant measures from the point HALFWAY BETWEEN the thing and
// where the pawn is really going; and it can (for haul-before-carry) accept equal-priority storage and applies the user's
// stockpile / storage-building filters.
//
// Ported to 1.6 by re-deriving it from vanilla 1.6's `TryFindBestBetterStoreCellFor` + `...ForWorker` (the worker split itself already exists in 1.4), which since 1.4 gained:
//   * acceptance via `slotGroup.Settings.AllowedToAccept(thing)` (the original copied 1.4's `parent.Accepts`; vanilla changed it in 1.5);
//   * in 1.6: only slot groups whose parent is not a foreign-faction Thing and whose `HaulDestinationEnabled` is true.
// Both are applied here, otherwise zWYU could pick storage vanilla itself refuses.

using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace zWYU
{
    internal static class StorageSearch
    {
        /// <param name="opportunityTarget">Valid for opportunistic hauls: the job target. Storage is chosen midway toward it.</param>
        /// <param name="beforeCarryTarget">Valid for haul-before-carry: the blueprint/frame/workbench. Storage is chosen midway toward it.</param>
        /// <param name="needAccurateResult">
        /// Vanilla's flag: false = take the first acceptable cell; true = keep looking at a small random fraction of the group's cells for a closer one.
        /// </param>
        public static bool TryFindMidwayStoreCell(Thing thing, LocalTargetInfo opportunityTarget, LocalTargetInfo beforeCarryTarget,
            Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, out IntVec3 foundCell, bool needAccurateResult) {
            var settings = ZwyuMod.Settings;

            var closestSlot        = IntVec3.Invalid;
            var closestDistSquared = (float)int.MaxValue;
            var foundPriority      = currentPriority;

            // closest to halfway to the target (constant for the whole search, so hoisted out of the group loop)
            var thingPos     = thing.SpawnedOrAnyParentSpawned ? thing.PositionHeld : carrier.PositionHeld;
            var detourTarget = opportunityTarget.IsValid ? opportunityTarget.Cell : beforeCarryTarget.IsValid ? beforeCarryTarget.Cell : IntVec3.Invalid;
            var detourMidway = detourTarget.IsValid ? new IntVec3((detourTarget.x + thingPos.x) / 2, detourTarget.y, (detourTarget.z + thingPos.z) / 2) : IntVec3.Invalid;
            var position     = detourMidway.IsValid ? detourMidway : thingPos; // vanilla: just thingPos

            var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
            for (var g = 0; g < groups.Count; g++) {
                var slotGroup     = groups[g];
                var groupPriority = slotGroup.Settings.Priority;

                if (groupPriority < foundPriority) break;

                // vanilla: `if (priority <= currentPriority) break;` - we allow equal priority for haul-before-carry (see below)
                if (groupPriority < currentPriority) break;
                if (groupPriority == StoragePriority.Unstored) break;
                if (groupPriority == currentPriority && !beforeCarryTarget.IsValid) break;

                var stockpile       = slotGroup.parent as Zone_Stockpile;
                var buildingStorage = slotGroup.parent as Building_Storage;

                if (opportunityTarget.IsValid) {
                    if (stockpile != null && !settings.Opportunity_ToStockpiles) continue;
                    if (buildingStorage != null && !settings.Opportunity_BuildingFilter.Allows(buildingStorage.def)) continue;
                }

                if (beforeCarryTarget.IsValid) {
                    // equal-priority storage ("haul extra resources closer from same-priority storage")
                    if (!settings.HaulBeforeCarry_ToEqualPriority && groupPriority == currentPriority) break;
                    if (settings.HaulBeforeCarry_ToEqualPriority && thing.Position.IsValid && slotGroup == map.haulDestinationManager.SlotGroupAt(thing.Position)) continue;
                    if (stockpile != null && !settings.HaulBeforeCarry_ToStockpiles) continue;

                    if (buildingStorage != null) {
                        if (!settings.HaulBeforeCarry_BuildingFilter.Allows(buildingStorage.def)) continue;

                        // if we don't consider it suitable for opportunities (e.g. slow storing) we won't consider it suitable for same-priority delivery
                        if (groupPriority == currentPriority && !settings.Opportunity_BuildingFilter.Allows(buildingStorage.def)) continue;
                    }
                }

                // 1.6 vanilla worker preconditions
                if (slotGroup.parent is Thing parentThing && parentThing.Faction != faction) continue;
                if (!slotGroup.parent.HaulDestinationEnabled) continue;
                if (!slotGroup.Settings.AllowedToAccept(thing)) continue;

                // vanilla's per-cell scan
                var cells           = slotGroup.CellsList;
                var maxCheckedCells = needAccurateResult ? Mathf.FloorToInt(cells.Count * Rand.Range(0.005f, 0.018f)) : 0;
                for (var i = 0; i < cells.Count; i++) {
                    var cell        = cells[i];
                    var distSquared = (float)(position - cell).LengthHorizontalSquared;
                    if (distSquared > closestDistSquared) continue;
                    if (!StoreUtility.IsGoodStoreCell(cell, map, thing, carrier, faction)) continue;

                    closestSlot        = cell;
                    closestDistSquared = distSquared;
                    foundPriority      = groupPriority;

                    if (i >= maxCheckedCells) break;
                }
            }

            foundCell = closestSlot;
            return foundCell.IsValid;
        }
    }
}
