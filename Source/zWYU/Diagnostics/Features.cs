// SPDX-License-Identifier: AGPL-3.0-or-later
// New code for the zWYU port: feature gating so that a broken hook disables one feature, never the game's job system.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace zWYU
{
    /// <summary>
    /// Independently gateable pieces of zWYU. Each maps to a small set of Harmony hooks. If any of the hooks of a feature cannot be
    /// applied, or its vanilla anchor is no longer where the hook expects it, the feature is switched off and vanilla behavior remains.
    /// </summary>
    internal enum Feature
    {
        /// <summary>Pawn_JobTracker.TryOpportunisticJob context + ListerHaulables.ThingsPotentiallyNeedingHauling anchor. Drives Opportunities and bill ingredients.</summary>
        OpportunityHook,

        /// <summary>WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor postfix. Drives "haul extra construction supplies closer".</summary>
        SupplyHook,

        /// <summary>JobDriver_HaulToCell.Notify_Starting/GetReport hooks: detour lifecycle tracking and "on the way to" job reports.</summary>
        LifecycleTracking,

        /// <summary>Optional: skip detours while a Pick Up And Haul pawn is carrying not-yet-unloaded inventory.</summary>
        PickUpAndHaulGuard,

        /// <summary>Optional: respect Common Sense's "haul ingredients for a bill" option.</summary>
        CommonSenseGuard,
    }

    internal static class Features
    {
        static readonly int Count = Enum.GetValues(typeof(Feature)).Length;

        static readonly bool[]   disabled = new bool[Count];
        static readonly string[] reasons  = new string[Count];

        public static bool IsAvailable(Feature feature) => !disabled[(int)feature];

        public static string ReasonDisabled(Feature feature) => reasons[(int)feature];

        /// <summary>Switch a feature off for the rest of the session and say exactly why.</summary>
        public static void Disable(Feature feature, string affectedPatch, string reason, Exception exception = null) {
            var first = !disabled[(int)feature];
            disabled[(int)feature] = true;
            reasons[(int)feature]  = $"{affectedPatch}: {reason}";
            if (first) {
                Diag.Error(
                    Diag.Compat,
                    $"Feature '{feature}' DISABLED (patch: {affectedPatch}). {reason}\n"
                    + "  zWYU stays loaded and vanilla RimWorld behavior is unaffected for this feature. Please report this with your log and mod list.",
                    exception);
            }
        }

        static readonly int[] faults = new int[Count];

        /// <summary>Faults tolerated per feature before it is switched off (each one is logged; the first also shows the stack trace).</summary>
        const int FaultBudget = 3;

        /// <summary>
        /// A runtime exception inside one of our hooks. The hook itself must already have fallen back to vanilla behavior for this call;
        /// this counts the fault and, if it keeps happening, disables the feature so it cannot spam errors or destabilize the game.
        /// </summary>
        public static void RecordFault(Feature feature, string where, Exception exception) {
            if (disabled[(int)feature]) return;
            var count = ++faults[(int)feature];
            if (count >= FaultBudget) {
                Disable(feature, where, $"threw {count} exceptions at runtime; switched off for the rest of the session.", exception);
                return;
            }
            Diag.Error(Diag.Compat, $"exception in {where} (feature '{feature}', fault {count}/{FaultBudget}); this call fell back to vanilla behavior.", exception);
        }

        public static IEnumerable<KeyValuePair<Feature, string>> DisabledFeatures() {
            for (var i = 0; i < Count; i++) {
                if (disabled[i])
                    yield return new KeyValuePair<Feature, string>((Feature)i, reasons[i]);
            }
        }

        /// <summary>
        /// Applies one [HarmonyPatch] class. On failure the owning feature is disabled (with a precise diagnostic) instead of
        /// letting the exception abort mod initialization or leave the class half-applied.
        /// </summary>
        public static bool TryPatch(Harmony harmony, Type patchClass, Feature feature) {
            try {
                harmony.CreateClassProcessor(patchClass).Patch();
                return true;
            } catch (Exception e) {
                Disable(feature, patchClass.Name, "Harmony could not apply this patch (a vanilla method changed, or another mod interfered).", e);
                return false;
            }
        }
    }
}
