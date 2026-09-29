// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Ported from While You're Up's Testing.cs ("Make colony (While You're Up)" debug action, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin. Development aid only: it wipes the current map's spawned pawns and area,
// so it lives behind Dev mode -> Debug actions -> Autotests. SAVE YOUR GAME FIRST.
// Builds a 100x50 area holding every workbench (with every recipe queued as a bill), one of every debug-spawnable item,
// 30 colonists with hauling DISABLED (so the only hauling you see is zWYU's) and 7 stockpiles. Ideal for the stress test in docs/TEST_PLAN.md.

using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace zWYU
{
    internal static class ColonyMaker
    {
        [DebugAction("Autotests", "Make colony (zWYU)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void MakeColonyZwyu() {
            var godMode = DebugSettings.godMode;
            DebugSettings.godMode                   = true;
            DebugViewSettings.drawOpportunisticJobs = true;

            Thing.allowDestroyNonDestroyable = true;
            try {
                if (Autotests_ColonyMaker.usedCells == null)
                    Autotests_ColonyMaker.usedCells = new BoolGrid(Autotests_ColonyMaker.Map);
                else
                    Autotests_ColonyMaker.usedCells.ClearAndResizeTo(Autotests_ColonyMaker.Map);
                Autotests_ColonyMaker.overRect = new CellRect(Autotests_ColonyMaker.Map.Center.x - 50, Autotests_ColonyMaker.Map.Center.z - 50, 100, 50);
                Autotests_ColonyMaker.DeleteAllSpawnedPawns();
                GenDebug.ClearArea(Autotests_ColonyMaker.overRect, Find.CurrentMap);

                Autotests_ColonyMaker.Map.wealthWatcher.ForceRecount();

                Autotests_ColonyMaker.TryGetFreeRect(90, 30, out var pawnAndThingRect);
                foreach (var thingDef in from def in DefDatabase<ThingDef>.AllDefs
                         where typeof(Building_WorkTable).IsAssignableFrom(def.thingClass)
                         select def) {
                    if (Autotests_ColonyMaker.TryMakeBuilding(thingDef) is Building_WorkTable workTable) {
                        foreach (var recipe in workTable.def.AllRecipes)
                            workTable.billStack.AddBill(recipe.MakeNewBill());
                    }
                }

                pawnAndThingRect = pawnAndThingRect.ContractedBy(1);
                var itemDefs = (from def in DefDatabase<ThingDef>.AllDefs
                    where DebugThingPlaceHelper.IsDebugSpawnable(def) && def.category == ThingCategory.Item
                    select def).ToList();

                foreach (var itemDef in itemDefs)
                    DebugThingPlaceHelper.DebugSpawn(itemDef, pawnAndThingRect.RandomCell, -1, true);

                const int pawnCount = 30;
                var allWork = Enumerable.Repeat(TimeAssignmentDefOf.Work, 24).ToList();
                for (var i = 0; i < pawnCount; i++) {
                    var pawn = PawnGenerator.GeneratePawn(Faction.OfPlayer.def.basicMemberKind, Faction.OfPlayer);
                    pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.NewColonyOptimism);
                    pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.NewColonyHope);
                    pawn.timetable.times = allWork;

                    foreach (var w in DefDatabase<WorkTypeDef>.AllDefs) {
                        if (!pawn.WorkTypeIsDisabled(w))
                            pawn.workSettings.SetPriority(w, 3);
                    }
                    pawn.workSettings.Disable(WorkTypeDefOf.Hauling);
                    GenSpawn.Spawn(pawn, pawnAndThingRect.RandomCell, Autotests_ColonyMaker.Map);
                }

                var designated = new Designator_ZoneAddStockpile_Resources();
                for (var _ = 0; _ < 7; _++) {
                    Autotests_ColonyMaker.TryGetFreeRect(8, 8, out var stockpileRect);
                    stockpileRect = stockpileRect.ContractedBy(1);
                    designated.DesignateMultiCell(stockpileRect.Cells);
                    ((Zone_Stockpile)Autotests_ColonyMaker.Map.zoneManager.ZoneAt(stockpileRect.CenterCell)).settings.Priority = StoragePriority.Normal;
                }

                Autotests_ColonyMaker.ClearAllHomeArea();
                Autotests_ColonyMaker.FillWithHomeArea(Autotests_ColonyMaker.overRect);
            } finally {
                DebugSettings.godMode            = godMode;
                Thing.allowDestroyNonDestroyable = false;
            }
        }
    }
}
