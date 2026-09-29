// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Debug visualization of detours. Ported from the inline drawing code in While You're Up's `Opportunity_Job` / `BeforeCarryDetour_Job`
// (WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin. Drawn only while vanilla's "Draw Opportunistic Jobs" debug view is on
// (kept in sync with the mod setting "Draw colored path detours").

using LudeonTK;
using RimWorld;
using Verse;

namespace zWYU
{
    internal static class DebugDraw
    {
        const int Duration = 600;

        static bool On => DebugViewSettings.drawOpportunisticJobs;

        /// <summary>Opportunity: original path red, new path green.</summary>
        public static void Opportunity(Pawn pawn, Thing thing, IntVec3 storeCell, LocalTargetInfo jobTarget) {
            if (!On) return;
            var label = pawn.LabelShortCap;
            var drawer = pawn.Map.debugDrawer;
            for (var _ = 0; _ < 3; _++) { // for bolder lines
                drawer.FlashCell(pawn.Position,  0.50f, label, Duration); // green:  start
                drawer.FlashCell(thing.Position, 0.62f, label, Duration); // cyan:   haulable
                drawer.FlashCell(storeCell,      0.22f, label, Duration); // orange: storage
                drawer.FlashCell(jobTarget.Cell, 0.0f,  label, Duration); // red:    job (first ingredient if a bill)

                // red: shorter old; green: longer new
                drawer.FlashLine(pawn.Position,  jobTarget.Cell, Duration, SimpleColor.Red);
                drawer.FlashLine(pawn.Position,  thing.Position, Duration, SimpleColor.Green);
                drawer.FlashLine(thing.Position, storeCell,      Duration, SimpleColor.Green);
                drawer.FlashLine(storeCell,      jobTarget.Cell, Duration, SimpleColor.Green);
            }
        }

        /// <summary>Haul-before-carry: original path magenta, new (via storage) cyan.</summary>
        public static void BeforeCarry(Pawn pawn, Thing thing, IntVec3 storeCell, LocalTargetInfo carryTarget) {
            if (!On) return;
            var label = pawn.LabelShortCap;
            var drawer = pawn.Map.debugDrawer;
            for (var _ = 0; _ < 3; _++) {
                drawer.FlashCell(thing.Position,   0.62f, label, Duration); // cyan:   haulable
                drawer.FlashCell(storeCell,        0.22f, label, Duration); // orange: storage
                drawer.FlashCell(carryTarget.Cell, 0.0f,  label, Duration); // red:    where it is needed

                // magenta: shorter old; cyan: longer new
                drawer.FlashLine(thing.Position, carryTarget.Cell, Duration, SimpleColor.Magenta);
                drawer.FlashLine(thing.Position, storeCell,        Duration, SimpleColor.Cyan);
                drawer.FlashLine(storeCell,      carryTarget.Cell, Duration, SimpleColor.Cyan);
            }
        }
    }
}
