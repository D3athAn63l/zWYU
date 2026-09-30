// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq;
using Xunit;

namespace zWYU.Tests
{
    /// <summary>
    /// The construction-supply detour's two decisions (original `BeforeSupplyDetour_Job`). Regression pins for the review finding that the 1.6 port
    /// compared against the construction's TOTAL cost and picked the largest stack from the job's TRIMMED queue.
    /// </summary>
    public class SupplyRulesTests
    {
        // ---- A. current remaining need, not total material cost ------------------------------------------------------------

        [Fact]
        public void PartialConstruction_ExtrasAreJudgedAgainstTheCurrentNeed() {
            const int totalCost = 100, alreadySupplied = 90, largestNearbyStack = 20;
            var currentNeed = totalCost - alreadySupplied; // 10 - what vanilla's ThingCountNeeded / GetSpaceRemainingWithEnroute report

            Assert.True(SupplyRules.ExtrasExist(largestNearbyStack, currentNeed), "20 > 10: there are extras, the supply detour may happen");

            // The rejected 1.6-port behavior, kept here as the documented wrong answer: judged against the total cost, the same stack has no extras.
            Assert.False(SupplyRules.ExtrasExist(largestNearbyStack, totalCost), "compare with total cost = the regression; it must not be what the decision uses");
        }

        [Fact]
        public void Extras_RequireAStackStrictlyLargerThanTheNeed() {
            Assert.False(SupplyRules.ExtrasExist(20, 20), "a stack exactly as large as the need has no extras (original rule is `>`, not `>=`)");
            Assert.True(SupplyRules.ExtrasExist(21, 20));
            Assert.False(SupplyRules.ExtrasExist(19, 20));
            Assert.False(SupplyRules.ExtrasExist(0, 1));
        }

        [Fact]
        public void AFullyUnsuppliedBlueprint_NeedsItsWholeCost() {
            // Blueprints report ThingCountNeeded == the whole cost, so for them the current need and the total cost coincide.
            Assert.True(SupplyRules.ExtrasExist(150, 100));
            Assert.False(SupplyRules.ExtrasExist(100, 100));
        }

        // ---- B. the largest stack of the FULL candidate list, not the trimmed job queue -------------------------------------

        [Fact]
        public void LargestStack_IsTakenFromTheFullCandidateList_NotFromTheTrimmedQueue() {
            // Vanilla collected [chosen 30, 75, 20] (chosen resource first), then trimmed what it did not need to fill the carrying load and
            // built a job with targetA = 30 and targetQueueA = [20]. The original inspected the FULL list.
            var full    = new[] { 30, 75, 20 };
            var trimmed = new[] { 30, 20 }; // targetA + targetQueueA of the returned job

            Assert.Equal(1, SupplyRules.IndexOfLargest(full));
            Assert.Equal(75, full[SupplyRules.IndexOfLargest(full)]);
            Assert.Equal(30, trimmed[SupplyRules.IndexOfLargest(trimmed)]); // what the rejected reconstruction would have picked
        }

        [Fact]
        public void LargestStack_FirstMaximumWins_LikeVanillasMaxBy() {
            Assert.Equal(0, SupplyRules.IndexOfLargest(new[] { 40, 40, 10 }));
            Assert.Equal(1, SupplyRules.IndexOfLargest(new[] { 10, 40, 40 }));
        }

        [Fact]
        public void LargestStack_EdgeCases() {
            Assert.Equal(-1, SupplyRules.IndexOfLargest(new int[0]));
            Assert.Equal(0, SupplyRules.IndexOfLargest(new[] { 5 }));
            Assert.Equal(2, SupplyRules.IndexOfLargest(new[] { 0, 1, 2 }));
            Assert.Equal(0, SupplyRules.IndexOfLargest(Enumerable.Repeat(7, 4).ToArray()));
        }

        [Fact]
        public void ExtrasThenDependOnTheLargestOfTheFullList() {
            // 100 total, 90 supplied -> need 10. The chosen stack is 8 (< need) but a 75-stack was among the collected candidates.
            var full = new[] { 8, 75 };
            var most = full[SupplyRules.IndexOfLargest(full)];
            Assert.True(SupplyRules.ExtrasExist(most, 10));
            // judged only on the chosen resource, the extras would have been missed
            Assert.False(SupplyRules.ExtrasExist(full[0], 10));
        }
    }
}
