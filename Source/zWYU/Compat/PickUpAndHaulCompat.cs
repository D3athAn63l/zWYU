// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Optional Pick Up And Haul (PUAH) coexistence. NEW for the zWYU port.
//
// SCOPE OF THIS BASELINE: the original While You're Up ("PUAH+") replaced its own HaulToCell detours by PUAH's multi-item
// HaulToInventory job and patched PUAH internals (its store-cell search, `skipCells`, unload ordering, ...). That is multi-item
// opportunistic hauling and depends on PUAH internals that changed in PUAH for 1.6 (container `StoreTarget`s, `skipThings`,
// `AllocateThingAtCell`); it is intentionally NOT ported here. See docs/PORTING_NOTES.md.
//
// What IS provided, isolated behind Feature.PickUpAndHaulGuard and failing safe (any problem => guard off, base mod unaffected):
//   * detection, reported once at startup;
//   * a guard: zWYU starts no detour for a pawn that is still carrying items PUAH put in their inventory and has not unloaded yet
//     ("because we may load a game with an incomplete haul" in the original). zWYU's own hauls carry in hands and never touch
//     PUAH's inventory bookkeeping, so there is no shared ownership to corrupt.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace zWYU
{
    internal static class PickUpAndHaulCompat
    {
        const string CompTypeName = "PickUpAndHaul.CompHauledToInventory";
        const string WorkGiverTypeName = "PickUpAndHaul.WorkGiver_HaulToInventory";

        static Type                                compType;
        static Func<ThingComp, HashSet<Thing>>     getHauledSet;

        public static bool Detected    { get; private set; }
        public static bool GuardActive { get; private set; }

        /// <summary>Called once from the mod constructor (all mod assemblies are already loaded at that point).</summary>
        public static void Init() {
            try {
                compType = GenTypes.GetTypeInAnyAssembly(CompTypeName);
                Detected = compType != null || GenTypes.GetTypeInAnyAssembly(WorkGiverTypeName) != null;
                if (!Detected) return;

                if (compType == null) {
                    Diag.Info(Diag.Compat, "Pick Up And Haul detected, but its CompHauledToInventory type was not found: the inventory guard is off. zWYU's own hauls are unaffected.");
                    return;
                }

                getHauledSet = BuildAccessor(compType);
                if (getHauledSet == null) {
                    Diag.Info(Diag.Compat, "Pick Up And Haul detected, but neither CompHauledToInventory.GetHashSet() nor its 'takenToInventory' field exists: the inventory guard is off.");
                    return;
                }

                GuardActive = true;
                Diag.Info(Diag.Compat, "Pick Up And Haul detected. Coexistence only: zWYU's PUAH+ enhancements are not part of this baseline; "
                                       + "detours are skipped while a pawn still carries PUAH-hauled inventory.");
            } catch (Exception e) {
                GuardActive = false;
                Features.Disable(Feature.PickUpAndHaulGuard, nameof(PickUpAndHaulCompat), "could not bind to Pick Up And Haul.", e);
            }
        }

        static Func<ThingComp, HashSet<Thing>> BuildAccessor(Type type) {
            var parameter = Expression.Parameter(typeof(ThingComp), "comp");
            var typed     = Expression.Convert(parameter, type);

            var method = AccessTools.DeclaredMethod(type, "GetHashSet", Type.EmptyTypes);
            if (method != null && typeof(HashSet<Thing>).IsAssignableFrom(method.ReturnType))
                return Expression.Lambda<Func<ThingComp, HashSet<Thing>>>(Expression.Call(typed, method), parameter).Compile();

            var field = AccessTools.DeclaredField(type, "takenToInventory");
            if (field != null && typeof(HashSet<Thing>).IsAssignableFrom(field.FieldType))
                return Expression.Lambda<Func<ThingComp, HashSet<Thing>>>(Expression.Field(typed, field), parameter).Compile();

            return null;
        }

        /// <summary>True if the pawn has things in PUAH's "hauled to inventory, awaiting unload" set.</summary>
        public static bool HasPendingInventoryHaul(Pawn pawn) {
            if (!GuardActive || pawn == null || !Features.IsAvailable(Feature.PickUpAndHaulGuard)) return false;
            try {
                var comps = pawn.AllComps;
                for (var i = 0; i < comps.Count; i++) {
                    var comp = comps[i];
                    if (comp.GetType() != compType) continue;

                    var set = getHauledSet(comp);
                    if (set == null) return false;
                    foreach (var thing in set) {
                        if (thing != null)
                            return true;
                    }
                    return false;
                }
                return false;
            } catch (Exception e) {
                GuardActive = false;
                Features.Disable(Feature.PickUpAndHaulGuard, nameof(PickUpAndHaulCompat), "reading a pawn's PUAH inventory set threw; guard turned off.", e);
                return false;
            }
        }
    }
}
