// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace zWYU.Tests
{
    /// <summary>
    /// The control flow of the opportunity search (original `Opportunity_Job`) with the candidate evaluation replaced by a script.
    /// Pins the original semantics and the absence of any candidate budget.
    /// </summary>
    public class SearchLoopTests
    {
        static RangeLimits Limits() => RangeLimits.Initial(30f, 0.5f, 50f, 0.6f);

        static List<int> Candidates(int count) => Enumerable.Range(1, count).ToList();

        static SearchLoopResult<int> Run(List<int> candidates, PathCheckerMode mode, EvaluateCandidate<int> evaluate, int maxExpansions = SettingsDefaults.MaxRangeExpansions) =>
            SearchLoop.Run(candidates, Limits(), SettingsDefaults.HeuristicRangeExpandFactor, maxExpansions, rangeFailIsFinal: mode == PathCheckerMode.Vanilla, evaluate);

        // ---- D. no semantic pathfinding candidate cap -----------------------------------------------------------------------

        [Theory]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(40)]
        [InlineData(500)]
        public void Pathfinding_ACandidateBeyondTheTwelfth_IsStillConsidered_AndSelected(int winner) {
            var pathChecked = 0;
            var result = Run(Candidates(winner + 20), PathCheckerMode.Pathfinding, (int c, in RangeLimits _) => {
                pathChecked++;
                // every candidate reaches the mode's final (real pathfinding) check; only `winner` passes it
                return PathCheckDispatch.Decide(PathCheckerMode.Pathfinding, c == winner ? RejectReason.None : RejectReason.PathUnreachable);
            });

            Assert.Equal(SearchEnd.Selected, result.End);
            Assert.Equal(winner, result.Selected);
            Assert.Equal(winner, pathChecked); // every earlier candidate was path-checked and dropped; the search was never cut short
        }

        [Fact]
        public void Pathfinding_WhenNothingPasses_EveryCandidateIsExamined_AndTheSearchEndsWithoutAResult() {
            var seen = new HashSet<int>();
            var result = Run(Candidates(60), PathCheckerMode.Pathfinding, (int c, in RangeLimits _) => {
                seen.Add(c);
                return PathCheckDispatch.Decide(PathCheckerMode.Pathfinding, RejectReason.PathTotalTrip);
            });

            Assert.Equal(SearchEnd.NoCandidate, result.End);
            Assert.Equal(60, seen.Count);
        }

        [Fact]
        public void EveryPathCheckFailure_InPathfindingAndVanillaMode_OnlyDropsThatCandidate() {
            foreach (var mode in new[] { PathCheckerMode.Pathfinding, PathCheckerMode.Vanilla }) {
                var check = PathCheckDispatch.Decide(mode, RejectReason.PathUnreachable);
                Assert.Equal(HaulVerdict.HardFail, check.Verdict);
                Assert.Equal(RejectReason.PathUnreachable, check.Reason);
            }
        }

        // ---- the original semantics stay as they were --------------------------------------------------------------------------

        [Fact]
        public void Default_TheFirstFailedFinalPathCheck_StopsTheWholeSearch() {
            // original v4.0.0 "pathfind once after search; if it fails, no opportunity"
            var calls = 0;
            var result = Run(Candidates(10), PathCheckerMode.Default, (int c, in RangeLimits _) => {
                calls++;
                return PathCheckDispatch.Decide(PathCheckerMode.Default, c == 1 ? RejectReason.PathUnreachable : RejectReason.None);
            });

            Assert.Equal(SearchEnd.Stopped, result.End);
            Assert.Equal(HaulVerdict.FullStop, result.StoppingCheck.Verdict);
            Assert.Equal(1, result.StoppedOn);
            Assert.Equal(1, calls); // candidate 2 (which would have passed) is never reached
        }

        [Fact]
        public void PathCheckDispatch_Success_IsSuccessInEveryMode() {
            foreach (var mode in new[] { PathCheckerMode.Vanilla, PathCheckerMode.Default, PathCheckerMode.Pathfinding })
                Assert.Equal(HaulVerdict.Success, PathCheckDispatch.Decide(mode, RejectReason.None).Verdict);
        }

        [Fact]
        public void TheFirstSuccess_Wins_EvenIfLaterCandidatesWouldAlsoPass() {
            var calls = new List<int>();
            var result = Run(Candidates(5), PathCheckerMode.Default, (int c, in RangeLimits _) => { calls.Add(c); return c >= 2 ? HaulCheck.Success : new HaulCheck(HaulVerdict.HardFail, RejectReason.Forbidden); });
            Assert.Equal(2, result.Selected);
            Assert.Equal(new[] { 1, 2 }, calls);
        }

        [Fact]
        public void HardFail_RemovesTheCandidate_ItIsNeverEvaluatedAgain() {
            var counts = new Dictionary<int, int>();
            var candidates = Candidates(3);
            Run(candidates, PathCheckerMode.Default, (int c, in RangeLimits limits) => {
                counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
                if (c == 1) return new HaulCheck(HaulVerdict.HardFail, RejectReason.Forbidden);   // dropped for good
                if (c == 2 && limits.ExpandCount < 1) return new HaulCheck(HaulVerdict.RangeFail, RejectReason.StartToThingRange); // retried after expansion
                return c == 2 ? HaulCheck.Success : new HaulCheck(HaulVerdict.RangeFail, RejectReason.StartToThingRange);
            });
            Assert.Equal(1, counts[1]);
            Assert.True(counts[2] >= 2);
        }

        [Fact]
        public void RangeFail_IsRetriedAfterTheLimitsAreExpanded() {
            var result = Run(Candidates(3), PathCheckerMode.Default, (int c, in RangeLimits limits) =>
                limits.ExpandCount >= 2 && c == 3 ? HaulCheck.Success : new HaulCheck(HaulVerdict.RangeFail, RejectReason.StartToThingRange));
            Assert.Equal(SearchEnd.Selected, result.End);
            Assert.Equal(3, result.Selected);
            Assert.Equal(2, result.Expansions);
        }

        [Fact]
        public void RangeFail_IsFinalWithTheVanillaPathChecker() {
            var calls = 0;
            var result = Run(Candidates(3), PathCheckerMode.Vanilla, (int c, in RangeLimits _) => {
                calls++;
                return new HaulCheck(HaulVerdict.RangeFail, RejectReason.StartToThingRange);
            });
            Assert.Equal(SearchEnd.NoCandidate, result.End);
            Assert.Equal(3, calls);       // each examined once, none retried
            Assert.Equal(0, result.Expansions);
        }

        [Fact]
        public void NoCandidates_EndsImmediately() {
            var result = Run(new List<int>(), PathCheckerMode.Default, (int c, in RangeLimits _) => HaulCheck.Success);
            Assert.Equal(SearchEnd.NoCandidate, result.End);
            Assert.Equal(0, result.Evaluated);
        }

        // ---- the one bound: the termination guard, which is not a candidate budget ---------------------------------------------

        [Fact]
        public void TheExpansionGuard_EndsASearchWhoseCandidatesRangeFailForever() {
            var result = Run(Candidates(4), PathCheckerMode.Default, (int c, in RangeLimits _) => new HaulCheck(HaulVerdict.RangeFail, RejectReason.StartToThingRange), maxExpansions: 5);
            Assert.Equal(SearchEnd.ExpansionCapReached, result.End);
            Assert.Equal(5, result.Expansions);
            Assert.Equal(4, result.CandidatesLeft);
        }

        [Fact]
        public void TheExpansionGuard_DoesNotLimitHowManyCandidatesAreExamined() {
            // 2000 candidates, all failing the expensive check, then one that passes: the guard counts PASSES over the list, not candidates.
            var result = Run(Candidates(2001), PathCheckerMode.Pathfinding, (int c, in RangeLimits _) =>
                PathCheckDispatch.Decide(PathCheckerMode.Pathfinding, c == 2001 ? RejectReason.None : RejectReason.PathUnreachable));
            Assert.Equal(SearchEnd.Selected, result.End);
            Assert.Equal(2001, result.Selected);
            Assert.Equal(0, result.Expansions);
        }
    }
}
