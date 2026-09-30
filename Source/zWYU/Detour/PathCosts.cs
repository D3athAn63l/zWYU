// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Real-pathfinding cost helper. Derived from the nested `GetPathCost` in While You're Up's `CanHaul()` (OpportunityDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin, re-implemented for RimWorld 1.6's pathfinding API.
//
// 1.6 changes that matter here:
//   * `PathFinder.FindPath(...)` no longer exists; the synchronous entry point is `FindPathNow(start, target, traverseParms, tuning, peMode, ...)`.
//   * An unsuccessful search is `PawnPath.NotFound` with a NEGATIVE cost and `Found == false`. The original treated
//     "cost == 0" as failure (its old NotFound path had cost 0); the port therefore tests `Found`, never the cost value.
//   * A path of zero length is reported as not found, so "the start already satisfies the end condition" (pawn already touching
//     the target) is decided first with ReachabilityImmediate and costs 0 - it is the best possible leg, not a failure.

using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    internal static class PathCosts
    {
        // Diagnostics only: how many synchronous FindPathNow queries zWYU has issued, and (only while logging is on) the time they took.
        // Pathfinding cost stays visible in the per-search summary; it is a documented performance concern, not a semantic limit.
        public static int  Queries;
        public static long Ticks;

        /// <summary>
        /// Cost of walking from <paramref name="start"/> to <paramref name="destCell"/> for <paramref name="pawn"/>.
        /// Returns false if there is no path. The destination is always passed as a CELL (the original's fix for an
        /// IndexOutOfRangeException in EdificeGrid when a Thing target was used with an arbitrary start).
        /// </summary>
        public static bool TryGet(Pawn pawn, IntVec3 start, IntVec3 destCell, PathEndMode peMode, out float cost) {
            cost = 0f;
            var map = pawn.Map;
            if (map == null || !start.IsValid || !destCell.IsValid || !start.InBounds(map) || !destCell.InBounds(map))
                return false;

            var target = new LocalTargetInfo(destCell);
            if (ReachabilityImmediate.CanReachImmediate(start, target, map, peMode, pawn))
                return true; // already there: zero-cost leg

            Queries++;
            var started = Diag.Summary ? Stopwatch.GetTimestamp() : 0L;
            try {
                using (var path = map.pathFinder.FindPathNow(start, target, TraverseParms.For(pawn), null, peMode)) {
                    if (path == null || !path.Found)
                        return false;
                    cost = path.TotalCost;
                    return true;
                }
            } finally {
                if (started != 0L)
                    Ticks += Stopwatch.GetTimestamp() - started;
            }
        }
    }
}
