// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The control flow of While You're Up's `Opportunity_Job` loop and the verdict vocabulary of its `CanHaul` (OpportunityDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin, extracted so that it references no RimWorld type and can be unit-tested
// (Source/zWYU.Tests links this file). The candidate evaluation itself (OpportunitySearch.Evaluator) stays RimWorld-bound.
//
// The loop, exactly as the original:
//   * candidates are examined in list order;
//   * RangeFail  : skip for now, retry after the cheap range limits have been expanded (a performance heuristic) - unless the path checker is
//                  "Vanilla", where a range failure is final;
//   * HardFail   : this candidate can never qualify: remove it;
//   * FullStop   : end the whole search with no result;
//   * Success    : the FIRST success wins.
// After a full pass over the remaining candidates without a result the limits are expanded and the pass restarts.
// There is NO limit on how many candidates may be examined or how many receive an expensive (pathfinding) check: every eligible candidate
// stays eligible for as long as the search runs. The only bound is MaxRangeExpansions, a termination guard (see SettingsDefaults).

using System.Collections.Generic;

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

    /// <summary>What the path checker's FINAL check means for a candidate that has already passed every cheap check.</summary>
    internal static class PathCheckDispatch
    {
        /// <summary>
        /// <paramref name="finalCheckFailure"/> is <see cref="RejectReason.None"/> if the mode's final check (region count for Vanilla,
        /// real pathfinding for Default and Pathfinding) passed. A failure is
        ///   * Default     : FullStop - "pathfind once after search; if it fails, no opportunity" (original v4.0.0);
        ///   * Vanilla and Pathfinding : HardFail - only this candidate is dropped and the search continues with the next one.
        /// Nothing here depends on how many candidates were already checked: there is no candidate budget.
        /// </summary>
        public static HaulCheck Decide(PathCheckerMode mode, RejectReason finalCheckFailure) {
            if (finalCheckFailure == RejectReason.None)
                return HaulCheck.Success;
            return new HaulCheck(mode == PathCheckerMode.Default ? HaulVerdict.FullStop : HaulVerdict.HardFail, finalCheckFailure);
        }
    }

    internal enum SearchEnd
    {
        /// <summary>No candidate qualified (all removed, or none to begin with).</summary>
        NoCandidate,

        /// <summary>A candidate succeeded (<see cref="SearchLoopResult{T}.Selected"/>).</summary>
        Selected,

        /// <summary>A candidate returned FullStop (<see cref="SearchLoopResult{T}.StoppedOn"/>).</summary>
        Stopped,

        /// <summary>Termination guard: <c>maxExpansions</c> passes were made and candidates were still range-failing.</summary>
        ExpansionCapReached,
    }

    internal struct SearchLoopResult<T>
    {
        public SearchEnd End;
        public T         Selected;
        public HaulCheck StoppingCheck;
        public T         StoppedOn;
        public int       Evaluated;
        public int       Expansions;
        public int       CandidatesLeft;
    }

    /// <summary>Evaluates one candidate against the CURRENT range limits.</summary>
    internal delegate HaulCheck EvaluateCandidate<in T>(T candidate, in RangeLimits limits);

    internal static class SearchLoop
    {
        /// <summary>
        /// Runs the original loop over <paramref name="candidates"/> (which is consumed: HardFail candidates are removed from it).
        /// </summary>
        /// <param name="rangeFailIsFinal">True for the Vanilla path checker, where a range failure is treated as a hard failure.</param>
        /// <param name="maxExpansions">Termination guard only; see SettingsDefaults.MaxRangeExpansions.</param>
        public static SearchLoopResult<T> Run<T>(List<T> candidates, RangeLimits initialLimits, float expandFactor, int maxExpansions, bool rangeFailIsFinal,
            EvaluateCandidate<T> evaluate) {
            var result = new SearchLoopResult<T> { End = SearchEnd.NoCandidate };
            var limits = initialLimits;
            var i      = 0;

            while (candidates.Count > 0) {
                if (i == candidates.Count) {
                    // By expanding our cheap range checks gradually, our expensive checks will be performed in the most optimistic order.
                    // It won't always help - a detour can be far away yet still perfectly along our path - but it's a good performance heuristic.
                    if (limits.ExpandCount >= maxExpansions) {
                        result.End            = SearchEnd.ExpansionCapReached;
                        result.CandidatesLeft = candidates.Count;
                        return result;
                    }
                    limits = limits.Expanded(expandFactor);
                    result.Expansions++;
                    i = 0;
                }

                var candidate = candidates[i];
                result.Evaluated++;
                var check = evaluate(candidate, limits);

                switch (check.Verdict) {
                    case HaulVerdict.RangeFail:
                        if (rangeFailIsFinal) {
                            candidates.RemoveAt(i);
                        } else {
                            i++;
                        }
                        break;

                    case HaulVerdict.HardFail:
                        candidates.RemoveAt(i);
                        break;

                    case HaulVerdict.FullStop:
                        result.End           = SearchEnd.Stopped;
                        result.StoppingCheck = check;
                        result.StoppedOn     = candidate;
                        result.CandidatesLeft = candidates.Count;
                        return result;

                    case HaulVerdict.Success:
                        result.End            = SearchEnd.Selected;
                        result.Selected       = candidate;
                        result.CandidatesLeft = candidates.Count;
                        return result;
                }
            }

            return result;
        }
    }
}
