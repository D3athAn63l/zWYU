// SPDX-License-Identifier: AGPL-3.0-or-later
using Xunit;

namespace zWYU.Tests
{
    /// <summary>The defaults of While You're Up 4.0.4 (Settings.ExposeData). Changing one is a behavior change and must be documented.</summary>
    public class SettingsDefaultsTests
    {
        [Fact]
        public void OpportunityDefaults_AreTheOriginals() {
            Assert.True(SettingsDefaults.Enabled);
            Assert.Equal(PathCheckerMode.Default, SettingsDefaults.Opportunity_PathChecker);
            Assert.Equal(30f, SettingsDefaults.Opportunity_MaxStartToThing);
            Assert.Equal(0.5f, SettingsDefaults.Opportunity_MaxStartToThingPctOrigTrip);
            Assert.Equal(50f, SettingsDefaults.Opportunity_MaxStoreToJob);
            Assert.Equal(0.6f, SettingsDefaults.Opportunity_MaxStoreToJobPctOrigTrip);
            Assert.Equal(1.7f, SettingsDefaults.Opportunity_MaxTotalTripPctOrigTrip);
            Assert.Equal(1.0f, SettingsDefaults.Opportunity_MaxNewLegsPctOrigTrip);
            Assert.Equal(25, SettingsDefaults.Opportunity_MaxStartToThingRegionLookCount);
            Assert.Equal(25, SettingsDefaults.Opportunity_MaxStoreToJobRegionLookCount);
            Assert.True(SettingsDefaults.Opportunity_ToStockpiles);
            Assert.True(SettingsDefaults.Opportunity_AutoBuildings);
            Assert.False(SettingsDefaults.Opportunity_TweakVanilla);
            Assert.Equal(2f, SettingsDefaults.HeuristicRangeExpandFactor);
        }

        [Fact]
        public void HaulBeforeCarryDefaults_AreTheOriginals() {
            Assert.True(SettingsDefaults.HaulBeforeCarry_Supplies);
            Assert.True(SettingsDefaults.HaulBeforeCarry_Bills);
            Assert.True(SettingsDefaults.HaulBeforeCarry_ToEqualPriority);
            Assert.True(SettingsDefaults.HaulBeforeCarry_ToStockpiles);
            Assert.True(SettingsDefaults.HaulBeforeCarry_AutoBuildings);
        }

        [Fact]
        public void PathCheckerNames_ArePersistedInConfigFiles_SoTheyMustNotChange() {
            Assert.Equal(new[] { "Vanilla", "Default", "Pathfinding" }, System.Enum.GetNames(typeof(PathCheckerMode)));
        }

        [Fact]
        public void SafetyValves_AreSane() {
            Assert.InRange(SettingsDefaults.MaxRangeExpansions, 10, 200);
            Assert.InRange(SettingsDefaults.MaxPathfindingCandidates, 1, 100);
        }
    }
}
