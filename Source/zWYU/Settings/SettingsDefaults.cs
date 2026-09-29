// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Default values of the original While You're Up settings, unchanged (Settings.ExposeData in WYU 4.0.4).
// Kept free of any RimWorld type so that it can be unit-tested (see Source/zWYU.Tests).
// Derived from While You're Up, Copyright (C) 2020 Christopher S. Galpin.

namespace zWYU
{
    /// <summary>How the final "is this detour acceptable" check is made. Names are persisted in the config file; do not rename.</summary>
    internal enum PathCheckerMode
    {
        /// <summary>Vanilla style: only distance ratios and a region-count reachability check. Fastest; fewest opportunities.</summary>
        Vanilla,

        /// <summary>Cheap checks while searching, then real pathfinding once for the chosen haul; if that fails there is no opportunity. Default.</summary>
        Default,

        /// <summary>Real pathfinding for every candidate that survives the cheap checks. Most opportunities; slowest.</summary>
        Pathfinding,
    }

    internal static class SettingsDefaults
    {
        public const bool Enabled = true;

        public const PathCheckerMode Opportunity_PathChecker = PathCheckerMode.Default;
        public const bool            Opportunity_TweakVanilla = false;
        public const bool            Opportunity_ToStockpiles = true;
        public const bool            Opportunity_AutoBuildings = true;

        // Cheap "range" limits, expanded gradually while searching (a performance heuristic).
        public const float Opportunity_MaxStartToThing            = 30f;
        public const float Opportunity_MaxStartToThingPctOrigTrip = 0.5f;
        public const float Opportunity_MaxStoreToJob              = 50f;
        public const float Opportunity_MaxStoreToJobPctOrigTrip   = 0.6f;

        // Hard limits on the whole detour, as a ratio of the original trip.
        public const float Opportunity_MaxTotalTripPctOrigTrip = 1.7f;
        public const float Opportunity_MaxNewLegsPctOrigTrip   = 1.0f;

        public const int Opportunity_MaxStartToThingRegionLookCount = 25;
        public const int Opportunity_MaxStoreToJobRegionLookCount   = 25;

        public const bool HaulBeforeCarry_Supplies         = true;
        public const bool HaulBeforeCarry_Bills            = true;
        public const bool HaulBeforeCarry_ToEqualPriority  = true;
        public const bool HaulBeforeCarry_ToStockpiles     = true;
        public const bool HaulBeforeCarry_AutoBuildings    = true;

        // Range-heuristic expansion factor per pass (a [TweakValue] in the original; 1.1 - 3.0 allowed).
        public const float HeuristicRangeExpandFactor = 2f;

        // ---- zWYU additions (safety valves; see docs/PORTING_NOTES.md "Known deviations") ----

        /// <summary>Upper bound on range-expansion passes in one opportunity search (the original relied on float overflow to terminate).</summary>
        public const int MaxRangeExpansions = 40;

        /// <summary>Upper bound on candidates given a real pathfinding check in one search ("Pathfind during search" mode only).</summary>
        public const int MaxPathfindingCandidates = 12;
    }
}
