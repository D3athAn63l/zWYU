// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Optional "Common Sense" coexistence. Derived in purpose from While You're Up's `:ResolveCsConflict` (Mod.cs / Settings.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin. Both mods offer "haul ingredients for a bill", so the original un-ticked one mod's option
// in the OTHER mod's settings file. zWYU does not write another mod's settings: while Common Sense's option is on, zWYU simply
// does not perform its own bill-ingredient detours. Failure-safe: if Common Sense's field cannot be found, nothing changes.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace zWYU
{
    internal static class CommonSenseCompat
    {
        static FieldInfo haulingOverBillsField;
        static bool      reportedYield;

        public static bool Detected { get; private set; }

        public static void Init() {
            try {
                var settingsType = GenTypes.GetTypeInAnyAssembly("CommonSense.Settings");
                var modType      = GenTypes.GetTypeInAnyAssembly("CommonSense.CommonSense");
                Detected = settingsType != null || modType != null;
                if (!Detected) return;

                haulingOverBillsField = settingsType == null ? null : AccessTools.DeclaredField(settingsType, "hauling_over_bills");
                if (haulingOverBillsField == null || haulingOverBillsField.FieldType != typeof(bool) || !haulingOverBillsField.IsStatic) {
                    haulingOverBillsField = null;
                    Diag.Info(Diag.Compat, "Common Sense detected, but its 'hauling_over_bills' setting was not found (changed?): zWYU bill-ingredient hauling is left as configured.");
                    return;
                }

                Diag.Info(Diag.Compat, "Common Sense detected: zWYU skips its bill-ingredient detours while Common Sense's \"haul ingredients for a bill\" option is on (zWYU never edits Common Sense's settings).");
            } catch (Exception e) {
                haulingOverBillsField = null;
                Features.Disable(Feature.CommonSenseGuard, nameof(CommonSenseCompat), "could not bind to Common Sense.", e);
            }
        }

        /// <summary>True while Common Sense itself is hauling bill ingredients, in which case zWYU steps aside for bills.</summary>
        public static bool BillHaulingHandledByCommonSense {
            get {
                if (haulingOverBillsField == null || !Features.IsAvailable(Feature.CommonSenseGuard)) return false;
                try {
                    var on = (bool)haulingOverBillsField.GetValue(null);
                    if (on && !reportedYield && Diag.Summary) {
                        reportedYield = true;
                        Diag.Decision(Diag.Compat, "bill-ingredient detours are yielding to Common Sense.");
                    }
                    return on;
                } catch (Exception e) {
                    haulingOverBillsField = null;
                    Features.Disable(Feature.CommonSenseGuard, nameof(CommonSenseCompat), "reading Common Sense's setting threw; guard turned off.", e);
                    return false;
                }
            }
        }
    }
}
