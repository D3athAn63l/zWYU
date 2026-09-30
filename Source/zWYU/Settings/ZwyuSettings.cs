// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Derived from While You're Up's `Settings : ModSettings` (Settings.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin.
// Field names and defaults are the original's so behavior (and future config migration) stays recognizable.
// Changes: the "Pick Up And Haul+" switches are not part of this baseline; building filters are persisted by defName
// (StorageBuildingFilter) instead of an XML node that had to be re-parsed after defs load; a DebugLevel was added.

using LudeonTK;
using RimWorld;
using Verse;

namespace zWYU
{
    internal sealed class ZwyuSettings : ModSettings
    {
        // ---- general ----
        public bool       Enabled = SettingsDefaults.Enabled;
        public DebugLevel DebugLevel = DebugLevel.Off;

        /// <summary>Mirrors vanilla's "Development mode -> Visibility -> Draw Opportunistic Jobs" (kept in sync, as in the original).</summary>
        public bool DrawSpecialHauls {
            get => DebugViewSettings.drawOpportunisticJobs;
            set => DebugViewSettings.drawOpportunisticJobs = value;
        }

        // ---- opportunities ("... on the way to ...") ----
        public PathCheckerMode Opportunity_PathChecker = SettingsDefaults.Opportunity_PathChecker;
        public bool            Opportunity_TweakVanilla = SettingsDefaults.Opportunity_TweakVanilla;
        public bool            Opportunity_ToStockpiles = SettingsDefaults.Opportunity_ToStockpiles;
        public bool            Opportunity_AutoBuildings = SettingsDefaults.Opportunity_AutoBuildings;

        public float Opportunity_MaxStartToThing            = SettingsDefaults.Opportunity_MaxStartToThing;
        public float Opportunity_MaxStartToThingPctOrigTrip = SettingsDefaults.Opportunity_MaxStartToThingPctOrigTrip;
        public float Opportunity_MaxStoreToJob              = SettingsDefaults.Opportunity_MaxStoreToJob;
        public float Opportunity_MaxStoreToJobPctOrigTrip   = SettingsDefaults.Opportunity_MaxStoreToJobPctOrigTrip;
        public float Opportunity_MaxTotalTripPctOrigTrip    = SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip;
        public float Opportunity_MaxNewLegsPctOrigTrip      = SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip;

        public int Opportunity_MaxStartToThingRegionLookCount = SettingsDefaults.Opportunity_MaxStartToThingRegionLookCount;
        public int Opportunity_MaxStoreToJobRegionLookCount   = SettingsDefaults.Opportunity_MaxStoreToJobRegionLookCount;

        /// <summary>User-edited list of storage buildings allowed for opportunities (used when <see cref="Opportunity_AutoBuildings"/> is off).</summary>
        public StorageBuildingFilter OpportunityManualBuildings = new StorageBuildingFilter();

        // ---- haul before carry ("... closer to ...") ----
        public bool HaulBeforeCarry_Supplies        = SettingsDefaults.HaulBeforeCarry_Supplies;
        public bool HaulBeforeCarry_Bills           = SettingsDefaults.HaulBeforeCarry_Bills;
        public bool HaulBeforeCarry_ToEqualPriority = SettingsDefaults.HaulBeforeCarry_ToEqualPriority;
        public bool HaulBeforeCarry_ToStockpiles    = SettingsDefaults.HaulBeforeCarry_ToStockpiles;
        public bool HaulBeforeCarry_AutoBuildings   = SettingsDefaults.HaulBeforeCarry_AutoBuildings;

        public StorageBuildingFilter HaulBeforeCarryManualBuildings = new StorageBuildingFilter();

        /// <summary>The filter actually in force for opportunities.</summary>
        public StorageBuildingFilter Opportunity_BuildingFilter {
            get {
                if (Opportunity_AutoBuildings) return StorageBuildingCatalog.OpportunityDefaults;
                if (!OpportunityManualBuildings.Initialized) OpportunityManualBuildings.CopyFrom(StorageBuildingCatalog.OpportunityDefaults);
                return OpportunityManualBuildings;
            }
        }

        /// <summary>The filter actually in force for haul-before-carry.</summary>
        public StorageBuildingFilter HaulBeforeCarry_BuildingFilter {
            get {
                if (HaulBeforeCarry_AutoBuildings) return StorageBuildingCatalog.HaulBeforeCarryDefaults;
                if (!HaulBeforeCarryManualBuildings.Initialized) HaulBeforeCarryManualBuildings.CopyFrom(StorageBuildingCatalog.HaulBeforeCarryDefaults);
                return HaulBeforeCarryManualBuildings;
            }
        }

        public override void ExposeData() {
            base.ExposeData();

            Scribe_Values.Look(ref Enabled, nameof(Enabled), SettingsDefaults.Enabled);
            Scribe_Values.Look(ref DebugLevel, nameof(DebugLevel), DebugLevel.Off);

            var draw = DebugViewSettings.drawOpportunisticJobs;
            Scribe_Values.Look(ref draw, nameof(DrawSpecialHauls), false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                DebugViewSettings.drawOpportunisticJobs = draw;

            Scribe_Values.Look(ref Opportunity_PathChecker, nameof(Opportunity_PathChecker), SettingsDefaults.Opportunity_PathChecker);
            Scribe_Values.Look(ref Opportunity_TweakVanilla, nameof(Opportunity_TweakVanilla), SettingsDefaults.Opportunity_TweakVanilla);
            Scribe_Values.Look(ref Opportunity_MaxStartToThing, nameof(Opportunity_MaxStartToThing), SettingsDefaults.Opportunity_MaxStartToThing);
            Scribe_Values.Look(ref Opportunity_MaxStartToThingPctOrigTrip, nameof(Opportunity_MaxStartToThingPctOrigTrip), SettingsDefaults.Opportunity_MaxStartToThingPctOrigTrip);
            Scribe_Values.Look(ref Opportunity_MaxStoreToJob, nameof(Opportunity_MaxStoreToJob), SettingsDefaults.Opportunity_MaxStoreToJob);
            Scribe_Values.Look(ref Opportunity_MaxStoreToJobPctOrigTrip, nameof(Opportunity_MaxStoreToJobPctOrigTrip), SettingsDefaults.Opportunity_MaxStoreToJobPctOrigTrip);
            Scribe_Values.Look(ref Opportunity_MaxTotalTripPctOrigTrip, nameof(Opportunity_MaxTotalTripPctOrigTrip), SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip);
            Scribe_Values.Look(ref Opportunity_MaxNewLegsPctOrigTrip, nameof(Opportunity_MaxNewLegsPctOrigTrip), SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip);
            Scribe_Values.Look(ref Opportunity_MaxStartToThingRegionLookCount, nameof(Opportunity_MaxStartToThingRegionLookCount), SettingsDefaults.Opportunity_MaxStartToThingRegionLookCount);
            Scribe_Values.Look(ref Opportunity_MaxStoreToJobRegionLookCount, nameof(Opportunity_MaxStoreToJobRegionLookCount), SettingsDefaults.Opportunity_MaxStoreToJobRegionLookCount);
            Scribe_Values.Look(ref Opportunity_ToStockpiles, nameof(Opportunity_ToStockpiles), SettingsDefaults.Opportunity_ToStockpiles);
            Scribe_Values.Look(ref Opportunity_AutoBuildings, nameof(Opportunity_AutoBuildings), SettingsDefaults.Opportunity_AutoBuildings);

            Scribe_Values.Look(ref HaulBeforeCarry_Supplies, nameof(HaulBeforeCarry_Supplies), SettingsDefaults.HaulBeforeCarry_Supplies);
            Scribe_Values.Look(ref HaulBeforeCarry_Bills, nameof(HaulBeforeCarry_Bills), SettingsDefaults.HaulBeforeCarry_Bills);
            Scribe_Values.Look(ref HaulBeforeCarry_ToEqualPriority, nameof(HaulBeforeCarry_ToEqualPriority), SettingsDefaults.HaulBeforeCarry_ToEqualPriority);
            Scribe_Values.Look(ref HaulBeforeCarry_ToStockpiles, nameof(HaulBeforeCarry_ToStockpiles), SettingsDefaults.HaulBeforeCarry_ToStockpiles);
            Scribe_Values.Look(ref HaulBeforeCarry_AutoBuildings, nameof(HaulBeforeCarry_AutoBuildings), SettingsDefaults.HaulBeforeCarry_AutoBuildings);

            Scribe_Deep.Look(ref OpportunityManualBuildings, nameof(OpportunityManualBuildings));
            Scribe_Deep.Look(ref HaulBeforeCarryManualBuildings, nameof(HaulBeforeCarryManualBuildings));
            if (Scribe.mode == LoadSaveMode.LoadingVars) {
                OpportunityManualBuildings      ??= new StorageBuildingFilter();
                HaulBeforeCarryManualBuildings ??= new StorageBuildingFilter();
            }

            Sanitize();
        }

        /// <summary>Repairs values a hand-edited or corrupted config could otherwise turn into NaN/negative limits.</summary>
        public void Sanitize() {
            Opportunity_MaxStartToThing            = Clamp(Opportunity_MaxStartToThing, 0f, 999f, SettingsDefaults.Opportunity_MaxStartToThing);
            Opportunity_MaxStartToThingPctOrigTrip = Clamp(Opportunity_MaxStartToThingPctOrigTrip, 0f, 10f, SettingsDefaults.Opportunity_MaxStartToThingPctOrigTrip);
            Opportunity_MaxStoreToJob              = Clamp(Opportunity_MaxStoreToJob, 0f, 999f, SettingsDefaults.Opportunity_MaxStoreToJob);
            Opportunity_MaxStoreToJobPctOrigTrip   = Clamp(Opportunity_MaxStoreToJobPctOrigTrip, 0f, 10f, SettingsDefaults.Opportunity_MaxStoreToJobPctOrigTrip);
            Opportunity_MaxTotalTripPctOrigTrip    = Clamp(Opportunity_MaxTotalTripPctOrigTrip, 0f, 10f, SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip);
            Opportunity_MaxNewLegsPctOrigTrip      = Clamp(Opportunity_MaxNewLegsPctOrigTrip, 0f, 10f, SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip);
            if (Opportunity_MaxStartToThingRegionLookCount < 1) Opportunity_MaxStartToThingRegionLookCount = SettingsDefaults.Opportunity_MaxStartToThingRegionLookCount;
            if (Opportunity_MaxStoreToJobRegionLookCount < 1) Opportunity_MaxStoreToJobRegionLookCount = SettingsDefaults.Opportunity_MaxStoreToJobRegionLookCount;
        }

        static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value < min ? min : value > max ? max : value;

        /// <summary>"Restore to default settings" button.</summary>
        public void RestoreDefaults() {
            Enabled = SettingsDefaults.Enabled;
            DebugLevel = DebugLevel.Off;
            DrawSpecialHauls = false;

            Opportunity_PathChecker = SettingsDefaults.Opportunity_PathChecker;
            Opportunity_TweakVanilla = SettingsDefaults.Opportunity_TweakVanilla;
            Opportunity_MaxStartToThing = SettingsDefaults.Opportunity_MaxStartToThing;
            Opportunity_MaxStartToThingPctOrigTrip = SettingsDefaults.Opportunity_MaxStartToThingPctOrigTrip;
            Opportunity_MaxStoreToJob = SettingsDefaults.Opportunity_MaxStoreToJob;
            Opportunity_MaxStoreToJobPctOrigTrip = SettingsDefaults.Opportunity_MaxStoreToJobPctOrigTrip;
            Opportunity_MaxTotalTripPctOrigTrip = SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip;
            Opportunity_MaxNewLegsPctOrigTrip = SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip;
            Opportunity_MaxStartToThingRegionLookCount = SettingsDefaults.Opportunity_MaxStartToThingRegionLookCount;
            Opportunity_MaxStoreToJobRegionLookCount = SettingsDefaults.Opportunity_MaxStoreToJobRegionLookCount;
            Opportunity_ToStockpiles = SettingsDefaults.Opportunity_ToStockpiles;
            Opportunity_AutoBuildings = SettingsDefaults.Opportunity_AutoBuildings;
            OpportunityManualBuildings.Clear();

            HaulBeforeCarry_Supplies = SettingsDefaults.HaulBeforeCarry_Supplies;
            HaulBeforeCarry_Bills = SettingsDefaults.HaulBeforeCarry_Bills;
            HaulBeforeCarry_ToEqualPriority = SettingsDefaults.HaulBeforeCarry_ToEqualPriority;
            HaulBeforeCarry_ToStockpiles = SettingsDefaults.HaulBeforeCarry_ToStockpiles;
            HaulBeforeCarry_AutoBuildings = SettingsDefaults.HaulBeforeCarry_AutoBuildings;
            HaulBeforeCarryManualBuildings.Clear();
        }
    }
}
