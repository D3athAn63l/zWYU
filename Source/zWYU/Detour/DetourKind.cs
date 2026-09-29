// SPDX-License-Identifier: AGPL-3.0-or-later
// New for the zWYU port. Replaces the original `DetourType` enum (which also encoded PUAH variants).

namespace zWYU
{
    /// <summary>Why zWYU is diverting a pawn into a haul. Determines the job-report wording and the debug colors.</summary>
    internal enum DetourKind
    {
        /// <summary>"... on the way to X": a haul near the pawn's route to the job they are about to do.</summary>
        Opportunity,

        /// <summary>"..., headed closer to X": hauling a construction supply (plus extras) toward the blueprint/frame that needs it.</summary>
        BeforeCarrySupply,

        /// <summary>"..., headed closer to X": hauling a bill ingredient (plus extras) toward the workbench that needs it.</summary>
        BeforeCarryBill,
    }
}
