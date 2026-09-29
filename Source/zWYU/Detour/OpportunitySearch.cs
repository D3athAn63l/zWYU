// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Ported from While You're Up's `Opportunity_Job` / `CanHaul` / `MaxRanges` (OpportunityDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin. The algorithm is deliberately UNCHANGED (this baseline is for learning its exact semantics):
//
//   For a pawn about to walk from `start` to a `job` target, scan the map's haulables for one that
//     - is near the start and not too far out of the way            (cheap range checks, expanded gradually as a heuristic)
//     - is unreserved, unforbidden, and haulable by this pawn
//     - has storage of higher priority, chosen midway toward the job (StorageSearch)
//     - keeps the total trip start->thing->store->job within the configured ratios of start->job
//     - and (depending on the "path checker") passes a region-count or real-pathfinding check.
//   The FIRST candidate that passes everything is returned as a HaulToCell job. Vanilla then queues the pawn's original job first
//   (Pawn_JobTracker.StartJob), so the original job resumes when the haul ends.
//
// What is new: reject reasons + a per-search summary for diagnostics; a hard cap on range-expansion passes and (in
// "Pathfind during search" mode) on candidates that get real pathfinding; the search is exception-safe and always clears its buffers.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    internal enum HaulVerdict
    {
        /// <summary>Failed a cheap range limit. Not final: retried after the limits are expanded (unless the path checker is Vanilla).</summary>
        RangeFail,

        /// <summary>Can never qualify for this search: drop it.</summary>
        HardFail,

        /// <summary>Stop the whole search: there will be no opportunity this time.</summary>
        FullStop,

        Success,
    }

    internal enum RejectReason
    {
        None,
        NotSpawned,
        StartToThingRange,
        StartToThingRatio,
        TotalTripBeforeStorage,
        ReservedByOther,
        Forbidden,
        CannotAutomaticallyHaul,
        NoBetterStorage,
        StoreToJobRange,
        StoreToJobRatio,
        NewLegs,
        TotalTrip,
        RegionCount,
        PathUnreachable,
        PathNewLegs,
        PathTotalTrip,
        PathBudgetExhausted,
    }

    internal readonly struct HaulCheck
    {
        public readonly HaulVerdict  Verdict;
        public readonly RejectReason Reason;

        public HaulCheck(HaulVerdict verdict, RejectReason reason = RejectReason.None) {
            Verdict = verdict;
            Reason  = reason;
        }

        public static readonly HaulCheck Success = new HaulCheck(HaulVerdict.Success);
    }

    internal static class OpportunitySearch
    {
        /// <summary>How fast the cheap range limits grow per pass. Was a [TweakValue] in the original; still is (Dev mode -> Tweaks).</summary>
        [TweakValue("zWYU.Opportunity", 1.1f, 3f)]
        public static float heuristicRangeExpandFactor = SettingsDefaults.HeuristicRangeExpandFactor;

        // Reused buffers (no per-search allocation). Always cleared in the `finally` of FindJob so they never retain Things between searches.
        static readonly List<Thing>              haulables      = new List<Thing>(32);
        static readonly Dictionary<Thing, IntVec3> storeCellCache = new Dictionary<Thing, IntVec3>(64);
        static readonly int[]                    rejectCounts   = new int[Enum.GetValues(typeof(RejectReason)).Length];

        /// <summary>
        /// Original: `Opportunity_Job`. <paramref name="jobTarget"/> is where the pawn is really going (for a bill: the first ingredient).
        /// Returns a HaulToCell job (already registered with the DetourTracker), or null.
        /// </summary>
        public static Job FindJob(Pawn pawn, Job originalJob, LocalTargetInfo jobTarget) {
            var summary   = Diag.Summary;
            var stopwatch = summary ? Stopwatch.StartNew() : null;
            var evaluated = 0;
            var expansions = 0;
            var pathChecks = 0;
            var outcome   = "no opportunity";
            Job result    = null;

            try {
                var settings = ZwyuMod.Settings;
                var limits   = RangeLimits.Initial(
                    settings.Opportunity_MaxStartToThing, settings.Opportunity_MaxStartToThingPctOrigTrip,
                    settings.Opportunity_MaxStoreToJob, settings.Opportunity_MaxStoreToJobPctOrigTrip);

                Array.Clear(rejectCounts, 0, rejectCounts.Length);
                haulables.Clear();
                haulables.AddRange(pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling());
                if (summary)
                    Diag.Decision(Diag.Opportunity, $"{Diag.Describe(pawn)} about to do {Diag.Describe(originalJob)}; searching {haulables.Count} haulables (target {Diag.Describe(jobTarget)}, path checker {settings.Opportunity_PathChecker})");

                var i = 0;
                while (haulables.Count > 0) {
                    if (i == haulables.Count) {
                        // By expanding our cheap range checks gradually, our expensive checks will be performed in the most optimistic order.
                        // It won't always help - a detour can be far away yet still perfectly along our path - but it's a good performance heuristic.
                        if (limits.ExpandCount >= SettingsDefaults.MaxRangeExpansions) {
                            outcome = $"gave up after {limits.ExpandCount} range expansions ({haulables.Count} candidates left)";
                            break;
                        }
                        limits = limits.Expanded(heuristicRangeExpandFactor);
                        expansions++;
                        i = 0;
                    }

                    var thing = haulables[i];
                    evaluated++;
                    var check = CanHaul(pawn, thing, jobTarget, limits, out var storeCell, ref pathChecks);
                    if (check.Reason != RejectReason.None) {
                        rejectCounts[(int)check.Reason]++;
                        if (Diag.Verbose)
                            Diag.Trace(Diag.Opportunity, $"  reject {Diag.Describe(thing)}: {check.Reason} ({check.Verdict})");
                    }

                    switch (check.Verdict) {
                        case HaulVerdict.RangeFail:
                            if (settings.Opportunity_PathChecker == PathCheckerMode.Vanilla)
                                goto case HaulVerdict.HardFail;
                            i++;
                            continue;
                        case HaulVerdict.HardFail:
                            haulables.RemoveAt(i);
                            continue;
                        case HaulVerdict.FullStop:
                            outcome = $"stopped: {check.Reason} on {Diag.Describe(thing)}";
                            haulables.Clear();
                            continue;
                        case HaulVerdict.Success:
                            result = MakeJob(pawn, originalJob, jobTarget, thing, storeCell);
                            outcome = result == null ? "haul job could not be created" : "selected";
                            haulables.Clear();
                            continue;
                    }
                }
            } finally {
                haulables.Clear();
                storeCellCache.Clear();
            }

            if (summary) {
                var sb = new StringBuilder();
                for (var r = 1; r < rejectCounts.Length; r++) {
                    if (rejectCounts[r] > 0)
                        sb.Append(sb.Length > 0 ? ", " : string.Empty).Append((RejectReason)r).Append('=').Append(rejectCounts[r]);
                }
                Diag.Decision(Diag.Opportunity,
                    $"{Diag.Describe(pawn)}: {outcome}. evaluated {evaluated}, range expansions {expansions}, path checks {pathChecks}, {stopwatch.Elapsed.TotalMilliseconds:F2} ms"
                    + (sb.Length > 0 ? $". Rejected: {sb}" : string.Empty));
            }
            return result;
        }

        static Job MakeJob(Pawn pawn, Job originalJob, LocalTargetInfo jobTarget, Thing thing, IntVec3 storeCell) {
            DebugDraw.Opportunity(pawn, thing, storeCell, jobTarget);

            var haulJob = HaulAIUtility.HaulToCellStorageJob(pawn, thing, storeCell, false);
            if (haulJob == null) return null;

            string why = null;
            if (Diag.Summary) {
                var startToJob = pawn.Position.DistanceTo(jobTarget.Cell);
                var trip       = pawn.Position.DistanceTo(thing.Position) + thing.Position.DistanceTo(storeCell) + storeCell.DistanceTo(jobTarget.Cell);
                why = $"start->job {startToJob:F1}, detour trip {trip:F1} ({(startToJob > 0 ? trip / startToJob : 0f):P0} of original)";
            }
            DetourTracker.Issue(new DetourPlan(pawn, DetourKind.Opportunity, jobTarget, thing, storeCell, originalJob, why), haulJob);
            return haulJob;
        }

        /// <summary>Original: `CanHaul`. See TripRules for the geometry; this adds the RimWorld-dependent checks in the original order.</summary>
        static HaulCheck CanHaul(Pawn pawn, Thing thing, LocalTargetInfo jobTarget, in RangeLimits limits, out IntVec3 storeCell, ref int pathChecks) {
            storeCell = IntVec3.Invalid;
            var settings = ZwyuMod.Settings;

            if (!thing.Spawned)
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.NotSpawned);

            var jobCell = jobTarget.Cell;

            // I don't know if avoiding `Sqrt()` is currently faster in Unity, but it's easy enough (when not summing distances).
            var startToThingSquared = pawn.Position.DistanceToSquared(thing.Position);
            var startToJobSquared   = pawn.Position.DistanceToSquared(jobCell);
            var startRange          = TripRules.CheckStartToThingRange(startToThingSquared, startToJobSquared, limits);
            if (startRange != RangeCheck.Pass)
                return new HaulCheck(HaulVerdict.RangeFail, startRange == RangeCheck.AbsoluteLimit ? RejectReason.StartToThingRange : RejectReason.StartToThingRatio);

            var startToThing = pawn.Position.DistanceTo(thing.Position);
            var thingToJob   = thing.Position.DistanceTo(jobCell);
            var startToJob   = pawn.Position.DistanceTo(jobCell);
            if (TripRules.ExceedsTotalTripBeforeStorage(startToThing, thingToJob, startToJob, settings.Opportunity_MaxTotalTripPctOrigTrip))
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.TotalTripBeforeStorage);
            if (pawn.Map.reservationManager.FirstRespectedReserver(thing, pawn) != null)
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.ReservedByOther);
            if (thing.IsForbidden(pawn))
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.Forbidden);
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false))
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.CannotAutomaticallyHaul);

            var currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
            if (!storeCellCache.TryGetValue(thing, out storeCell)) {
                if (!StorageSearch.TryFindMidwayStoreCell(
                        thing, jobTarget, LocalTargetInfo.Invalid, pawn, pawn.Map, currentPriority, pawn.Faction, out storeCell, limits.ExpandCount == 0))
                    return new HaulCheck(HaulVerdict.HardFail, RejectReason.NoBetterStorage);
            }

            // this won't change for a given thing as our same unmoved pawn loops through haulables, so cache it
            storeCellCache[thing] = storeCell;

            var storeToJobSquared = storeCell.DistanceToSquared(jobCell);
            var storeRange        = TripRules.CheckStoreToJobRange(storeToJobSquared, startToJobSquared, limits);
            if (storeRange != RangeCheck.Pass)
                return new HaulCheck(HaulVerdict.RangeFail, storeRange == RangeCheck.AbsoluteLimit ? RejectReason.StoreToJobRange : RejectReason.StoreToJobRatio);

            var storeToJob = storeCell.DistanceTo(jobCell);
            if (TripRules.ExceedsNewLegs(startToThing, storeToJob, startToJob, settings.Opportunity_MaxNewLegsPctOrigTrip))
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.NewLegs);
            var thingToStore = thing.Position.DistanceTo(storeCell);
            if (TripRules.ExceedsTotalTrip(startToThing, thingToStore, storeToJob, startToJob, settings.Opportunity_MaxTotalTripPctOrigTrip))
                return new HaulCheck(HaulVerdict.HardFail, RejectReason.TotalTrip);

            switch (settings.Opportunity_PathChecker) {
                case PathCheckerMode.Vanilla:
                    return WithinRegionCount(pawn, thing, storeCell, jobCell)
                        ? HaulCheck.Success
                        : new HaulCheck(HaulVerdict.HardFail, RejectReason.RegionCount);

                case PathCheckerMode.Pathfinding: {
                    if (pathChecks >= SettingsDefaults.MaxPathfindingCandidates)
                        return new HaulCheck(HaulVerdict.FullStop, RejectReason.PathBudgetExhausted);
                    pathChecks++;
                    var reason = WithinPathCost(pawn, thing, storeCell, jobTarget, settings);
                    return reason == RejectReason.None ? HaulCheck.Success : new HaulCheck(HaulVerdict.HardFail, reason);
                }

                case PathCheckerMode.Default: {
                    pathChecks++;
                    var reason = WithinPathCost(pawn, thing, storeCell, jobTarget, settings);
                    return reason == RejectReason.None ? HaulCheck.Success : new HaulCheck(HaulVerdict.FullStop, reason);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.Opportunity_PathChecker), settings.Opportunity_PathChecker, "unknown path checker");
            }
        }

        static bool WithinRegionCount(Pawn pawn, Thing thing, IntVec3 storeCell, IntVec3 jobCell) {
            var settings = ZwyuMod.Settings;
            if (!pawn.Position.WithinRegions(thing.Position, pawn.Map, settings.Opportunity_MaxStartToThingRegionLookCount, TraverseParms.For(pawn)))
                return false;
            return storeCell.WithinRegions(jobCell, pawn.Map, settings.Opportunity_MaxStoreToJobRegionLookCount, TraverseParms.For(pawn));
        }

        /// <summary>The same ratios as the cheap checks, but on real path costs. Returns None if acceptable.</summary>
        static RejectReason WithinPathCost(Pawn pawn, Thing thing, IntVec3 storeCell, LocalTargetInfo jobTarget, ZwyuSettings settings) {
            var jobCell = jobTarget.Cell;

            if (!PathCosts.TryGet(pawn, pawn.Position, thing.Position, PathEndMode.ClosestTouch, out var pawnToThing)) return RejectReason.PathUnreachable;
            if (!PathCosts.TryGet(pawn, storeCell, jobCell, PathEndMode.Touch, out var storeToJob)) return RejectReason.PathUnreachable;
            if (!PathCosts.TryGet(pawn, pawn.Position, jobCell, PathEndMode.Touch, out var pawnToJob)) return RejectReason.PathUnreachable;

            if (TripRules.ExceedsNewLegs(pawnToThing, storeToJob, pawnToJob, settings.Opportunity_MaxNewLegsPctOrigTrip))
                return RejectReason.PathNewLegs;

            if (!PathCosts.TryGet(pawn, thing.Position, storeCell, PathEndMode.ClosestTouch, out var thingToStore)) return RejectReason.PathUnreachable;

            if (TripRules.ExceedsTotalTrip(pawnToThing, thingToStore, storeToJob, pawnToJob, settings.Opportunity_MaxTotalTripPctOrigTrip))
                return RejectReason.PathTotalTrip;

            return RejectReason.None;
        }
    }
}
