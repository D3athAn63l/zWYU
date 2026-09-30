// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Ported from While You're Up's `Opportunity_Job` / `CanHaul` / `MaxRanges` (OpportunityDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin. The candidate-selection rules and control flow are the original's (this baseline exists to
// study their exact semantics); the differences are listed here so that none is hidden:
//   * 1.6 storage-worker checks in StorageSearch, and pathfinding semantics in PathCosts (`Found` instead of `TotalCost == 0`; an "already
//     touching" leg costs 0 and is accepted - both differ from the original's literal check, see docs/PORTING_NOTES.md deviation 9);
//   * a candidate that is not spawned is dropped first (a haulable held by a container has no position to measure from);
//   * MaxRangeExpansions: a TERMINATION GUARD on the range-expansion passes (the original ended by float overflow). It is not a candidate
//     budget; a unit test proves it cannot cut off a legitimate candidate on any supported map size;
//   * per-candidate reject reasons and a per-search summary, for diagnostics only.
// There is deliberately NO cap on how many candidates are examined or pathfound: in "Pathfind during search" mode every eligible candidate
// stays eligible (its synchronous pathfinding cost is reported in the summary and is a documented performance concern, not a semantic limit).
//
//   For a pawn about to walk from `start` to a `job` target, scan the map's haulables for one that
//     - is near the start and not too far out of the way            (cheap range checks, expanded gradually as a heuristic)
//     - is unreserved, unforbidden, and haulable by this pawn
//     - has storage of higher priority, chosen midway toward the job (StorageSearch)
//     - keeps the total trip start->thing->store->job within the configured ratios of start->job
//     - and (depending on the "path checker") passes a region-count or real-pathfinding check.
//   The FIRST candidate that passes everything is returned as a HaulToCell job. Vanilla then queues the pawn's original job first
//   (Pawn_JobTracker.StartJob), so the original job resumes when the haul ends.

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
    internal static class OpportunitySearch
    {
        /// <summary>How fast the cheap range limits grow per pass. Was a [TweakValue] in the original; still is (Dev mode -> Tweaks).</summary>
        [TweakValue("zWYU.Opportunity", 1.1f, 3f)]
        public static float heuristicRangeExpandFactor = SettingsDefaults.HeuristicRangeExpandFactor;

        // Reused buffers (no per-search allocation). Always cleared in the `finally` of FindJob so they never retain Things between searches.
        static readonly List<Thing>                haulables      = new List<Thing>(32);
        static readonly Dictionary<Thing, IntVec3> storeCellCache = new Dictionary<Thing, IntVec3>(64);
        static readonly int[]                      rejectCounts   = new int[Enum.GetValues(typeof(RejectReason)).Length];

        static readonly Evaluator                  evaluator    = new Evaluator();
        static readonly EvaluateCandidate<Thing>   evaluateThing = evaluator.Evaluate;

        /// <summary>
        /// Original: `Opportunity_Job`. <paramref name="jobTarget"/> is where the pawn is really going (for a bill: the first ingredient).
        /// Returns a HaulToCell job (already registered with the DetourTracker), or null.
        /// </summary>
        public static Job FindJob(Pawn pawn, Job originalJob, LocalTargetInfo jobTarget) {
            var summary   = Diag.Summary;
            var stopwatch = summary ? Stopwatch.StartNew() : null;
            var outcome   = "no opportunity";
            Job result    = null;
            SearchLoopResult<Thing> run = default;
            var pathQueriesBefore = PathCosts.Queries;
            var pathTicksBefore   = PathCosts.Ticks;

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

                evaluator.Begin(pawn, jobTarget);
                run = SearchLoop.Run(
                    haulables, limits, heuristicRangeExpandFactor, SettingsDefaults.MaxRangeExpansions,
                    rangeFailIsFinal: settings.Opportunity_PathChecker == PathCheckerMode.Vanilla, evaluateThing);

                switch (run.End) {
                    case SearchEnd.Selected:
                        result  = MakeJob(pawn, originalJob, jobTarget, run.Selected, evaluator.SelectedStoreCell);
                        outcome = result == null ? "haul job could not be created" : "selected";
                        break;
                    case SearchEnd.Stopped:
                        outcome = $"stopped: {run.StoppingCheck.Reason} on {Diag.Describe(run.StoppedOn)}";
                        break;
                    case SearchEnd.ExpansionCapReached:
                        outcome = $"gave up after {SettingsDefaults.MaxRangeExpansions} range expansions ({run.CandidatesLeft} candidates left; termination guard)";
                        break;
                }
            } finally {
                evaluator.End();
                haulables.Clear();
                storeCellCache.Clear();
            }

            if (summary) {
                var sb = new StringBuilder();
                for (var r = 1; r < rejectCounts.Length; r++) {
                    if (rejectCounts[r] > 0)
                        sb.Append(sb.Length > 0 ? ", " : string.Empty).Append((RejectReason)r).Append('=').Append(rejectCounts[r]);
                }
                var pathMs = (PathCosts.Ticks - pathTicksBefore) * 1000.0 / Stopwatch.Frequency;
                Diag.Decision(Diag.Opportunity,
                    $"{Diag.Describe(pawn)}: {outcome}. evaluated {run.Evaluated}, range expansions {run.Expansions}, "
                    + $"path checks {evaluator.PathChecks} ({PathCosts.Queries - pathQueriesBefore} synchronous path queries, {pathMs:F2} ms), {stopwatch.Elapsed.TotalMilliseconds:F2} ms total"
                    + (sb.Length > 0 ? $". Reject events: {sb}" : string.Empty));
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

        /// <summary>
        /// Original: `CanHaul`. See TripRules for the geometry; this adds the RimWorld-dependent checks in the original order.
        /// One reusable instance holds the per-search state (the delegate handed to SearchLoop is created once).
        /// </summary>
        sealed class Evaluator
        {
            Pawn            pawn;
            LocalTargetInfo jobTarget;

            public int     PathChecks;
            public IntVec3 SelectedStoreCell;

            public void Begin(Pawn searchingPawn, LocalTargetInfo target) {
                pawn              = searchingPawn;
                jobTarget         = target;
                PathChecks        = 0;
                SelectedStoreCell = IntVec3.Invalid;
            }

            public void End() {
                pawn      = null;
                jobTarget = default;
            }

            public HaulCheck Evaluate(Thing thing, in RangeLimits limits) {
                var check = CanHaul(thing, limits, out var storeCell);
                if (check.Reason != RejectReason.None) {
                    rejectCounts[(int)check.Reason]++;
                    if (Diag.Verbose)
                        Diag.Trace(Diag.Opportunity, $"  reject {Diag.Describe(thing)}: {check.Reason} ({check.Verdict})");
                }
                if (check.Verdict == HaulVerdict.Success)
                    SelectedStoreCell = storeCell;
                return check;
            }

            HaulCheck CanHaul(Thing thing, in RangeLimits limits, out IntVec3 storeCell) {
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

                // The mode's FINAL check. What a failure means (FullStop vs HardFail) is decided by PathCheckDispatch and does not depend on how
                // many candidates were checked before this one.
                switch (settings.Opportunity_PathChecker) {
                    case PathCheckerMode.Vanilla:
                        return PathCheckDispatch.Decide(PathCheckerMode.Vanilla, WithinRegionCount(thing, storeCell, jobCell) ? RejectReason.None : RejectReason.RegionCount);

                    case PathCheckerMode.Pathfinding:
                    case PathCheckerMode.Default:
                        PathChecks++;
                        return PathCheckDispatch.Decide(settings.Opportunity_PathChecker, WithinPathCost(thing, storeCell, settings));

                    default:
                        throw new ArgumentOutOfRangeException(nameof(settings.Opportunity_PathChecker), settings.Opportunity_PathChecker, "unknown path checker");
                }
            }

            bool WithinRegionCount(Thing thing, IntVec3 storeCell, IntVec3 jobCell) {
                var settings = ZwyuMod.Settings;
                if (!pawn.Position.WithinRegions(thing.Position, pawn.Map, settings.Opportunity_MaxStartToThingRegionLookCount, TraverseParms.For(pawn)))
                    return false;
                return storeCell.WithinRegions(jobCell, pawn.Map, settings.Opportunity_MaxStoreToJobRegionLookCount, TraverseParms.For(pawn));
            }

            /// <summary>The same ratios as the cheap checks, but on real path costs. Returns None if acceptable.</summary>
            RejectReason WithinPathCost(Thing thing, IntVec3 storeCell, ZwyuSettings settings) {
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
}
