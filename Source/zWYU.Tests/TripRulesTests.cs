// SPDX-License-Identifier: AGPL-3.0-or-later
using Xunit;

namespace zWYU.Tests
{
    /// <summary>
    /// TripRules is the geometry of While You're Up's CanHaul, extracted unchanged. These tests pin its exact semantics
    /// (strict inequalities, absolute-before-ratio, which legs count) so the later optimization work can prove it changed nothing.
    /// Distances are cells. "Sq" arguments are squared distances.
    /// </summary>
    public class TripRulesTests
    {
        static RangeLimits Defaults() => RangeLimits.Initial(
            SettingsDefaults.Opportunity_MaxStartToThing, SettingsDefaults.Opportunity_MaxStartToThingPctOrigTrip,
            SettingsDefaults.Opportunity_MaxStoreToJob, SettingsDefaults.Opportunity_MaxStoreToJobPctOrigTrip);

        // ---- start -> thing ------------------------------------------------------------------------------------------------

        [Fact]
        public void StartToThing_AtExactlyTheAbsoluteLimit_Passes() {
            var limits = RangeLimits.Initial(30f, 100f, 50f, 100f); // ratio limit effectively out of the way
            Assert.Equal(RangeCheck.Pass, TripRules.CheckStartToThingRange(30f * 30f, 100_000f, limits));
        }

        [Fact]
        public void StartToThing_JustOverTheAbsoluteLimit_IsRejectedAsAbsolute() {
            var limits = RangeLimits.Initial(30f, 100f, 50f, 100f);
            Assert.Equal(RangeCheck.AbsoluteLimit, TripRules.CheckStartToThingRange(30f * 30f + 1f, 100_000f, limits));
        }

        [Fact]
        public void StartToThing_RatioOfOriginalTrip_IsAppliedToSquaredDistances() {
            // default ratio 50%: with start->job = 40 the haulable may be at most 20 cells from the start
            var limits = Defaults();
            var startToJobSq = 40f * 40f;
            Assert.Equal(RangeCheck.Pass, TripRules.CheckStartToThingRange(20f * 20f, startToJobSq, limits));
            Assert.Equal(RangeCheck.RatioLimit, TripRules.CheckStartToThingRange(21f * 21f, startToJobSq, limits));
        }

        [Fact]
        public void StartToThing_WhenBothLimitsAreExceeded_TheAbsoluteLimitIsReported() {
            var limits = Defaults();
            Assert.Equal(RangeCheck.AbsoluteLimit, TripRules.CheckStartToThingRange(35f * 35f, 40f * 40f, limits));
        }

        // ---- early total-trip check (before any storage is known) ----------------------------------------------------------

        [Fact]
        public void EarlyTotalTrip_IsStrict() {
            // start->thing + thing->job vs 170% of start->job (default): 17 + 17 == 1.7 * 20 -> allowed; anything more -> rejected
            Assert.False(TripRules.ExceedsTotalTripBeforeStorage(17f, 17f, 20f, 1.7f));
            Assert.True(TripRules.ExceedsTotalTripBeforeStorage(17f, 17.01f, 20f, 1.7f));
        }

        // ---- store -> job --------------------------------------------------------------------------------------------------

        [Fact]
        public void StoreToJob_AbsoluteAndRatioLimits() {
            var limits = Defaults();                       // 50 cells, 60% of the original trip
            var startToJobSq = 100f * 100f;
            Assert.Equal(RangeCheck.Pass, TripRules.CheckStoreToJobRange(50f * 50f, startToJobSq, limits));
            Assert.Equal(RangeCheck.AbsoluteLimit, TripRules.CheckStoreToJobRange(51f * 51f, startToJobSq, limits));
            Assert.Equal(RangeCheck.RatioLimit, TripRules.CheckStoreToJobRange(31f * 31f, 50f * 50f, limits)); // 60% of 50 = 30
        }

        // ---- new legs & total trip -----------------------------------------------------------------------------------------

        [Fact]
        public void NewLegs_ExcludeTheThingToStoreLeg() {
            // 10 to the haulable + 10 from storage to the job == the original 20-cell trip at 100%: allowed.
            Assert.False(TripRules.ExceedsNewLegs(10f, 10f, 20f, 1.0f));
            Assert.True(TripRules.ExceedsNewLegs(10f, 10.01f, 20f, 1.0f));
        }

        [Fact]
        public void TotalTrip_IncludesAllThreeLegs() {
            // 10 + 20 + 10 = 40 vs 170% of 20 = 34 -> rejected; 10 + 14 + 10 = 34 -> allowed
            Assert.True(TripRules.ExceedsTotalTrip(10f, 20f, 10f, 20f, 1.7f));
            Assert.False(TripRules.ExceedsTotalTrip(10f, 14f, 10f, 20f, 1.7f));
        }

        [Fact]
        public void NaNLimits_NeverRejectAnything() {
            // documents float semantics the original relied on (comparisons with NaN are false); Settings.Sanitize keeps NaN out of real configs
            Assert.False(TripRules.ExceedsNewLegs(1f, 1f, 1f, float.NaN));
            Assert.False(TripRules.ExceedsTotalTrip(1f, 1f, 1f, 1f, float.NaN));
        }

        // ---- range expansion (the performance heuristic) -------------------------------------------------------------------

        [Fact]
        public void Expanded_ScalesEveryLimit_CountsThePass_AndLeavesTheOriginalUntouched() {
            var first = Defaults();
            var next  = first.Expanded(2f);
            Assert.Equal(0, first.ExpandCount);
            Assert.Equal(30f, first.StartToThing);
            Assert.Equal(1, next.ExpandCount);
            Assert.Equal(60f, next.StartToThing);
            Assert.Equal(1.0f, next.StartToThingPctOrigTrip);
            Assert.Equal(100f, next.StoreToJob);
            Assert.Equal(1.2f, next.StoreToJobPctOrigTrip, 5);
            Assert.Equal(2, next.Expanded(2f).ExpandCount);
        }

        [Fact]
        public void Expansion_EventuallyAdmitsAnyDistanceOnARealMap_WellWithinTheSearchCap() {
            // Worst realistic case: a job just past vanilla's 3-cell minimum, a haulable across a 1000-cell-wide map.
            // The search gives up after MaxRangeExpansions passes; that cap must never be what stops a legitimate candidate.
            var startToThing = 1000f;
            var startToJob   = 3f;
            var limits       = Defaults();
            var passes       = 0;
            while (TripRules.CheckStartToThingRange(startToThing * startToThing, startToJob * startToJob, limits) != RangeCheck.Pass
                   || TripRules.CheckStoreToJobRange(startToThing * startToThing, startToJob * startToJob, limits) != RangeCheck.Pass) {
                limits = limits.Expanded(SettingsDefaults.HeuristicRangeExpandFactor);
                passes++;
                Assert.True(passes < SettingsDefaults.MaxRangeExpansions, "range expansion cap would cut off a legitimate candidate");
            }
            Assert.InRange(passes, 1, SettingsDefaults.MaxRangeExpansions - 1);
        }

        [Fact]
        public void Expansion_WithTheMinimumAllowedTweakFactor_StillTerminatesWithinTheCap() {
            // [TweakValue] allows 1.1 - 3.0. At 1.1 the growth is slow; the cap (not float overflow) must be what bounds the loop.
            var limits = Defaults();
            for (var i = 0; i < SettingsDefaults.MaxRangeExpansions; i++)
                limits = limits.Expanded(1.1f);
            Assert.Equal(SettingsDefaults.MaxRangeExpansions, limits.ExpandCount);
        }

        // ---- worked examples with the default settings ---------------------------------------------------------------------
        // pawn -> job is 40 cells; default new-legs limit 100% (40), default total-trip limit 170% (68).

        [Fact]
        public void Example_AHaulOnTheWay_IsAccepted() {
            // 10 cells to the haulable, 8 cells to storage (on the way), 25 cells from storage to the job
            Assert.False(TripRules.ExceedsNewLegs(10f, 25f, 40f, SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip));                  // 35 <= 40
            Assert.False(TripRules.ExceedsTotalTrip(10f, 8f, 25f, 40f, SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip));         // 43 <= 68
        }

        [Fact]
        public void Example_AHaulThatPullsThePawnTooFarOffTheirWay_IsRejectedByNewLegs() {
            Assert.True(TripRules.ExceedsNewLegs(19f, 25f, 40f, SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip));                  // 44 > 40
        }

        [Fact]
        public void Example_AHaulWithAFarAwayStorage_IsRejectedByTheTotalTrip() {
            Assert.False(TripRules.ExceedsNewLegs(10f, 25f, 40f, SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip));                 // legs fine: 35 <= 40
            Assert.True(TripRules.ExceedsTotalTrip(10f, 40f, 25f, 40f, SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip));         // 75 > 68
        }
    }
}
