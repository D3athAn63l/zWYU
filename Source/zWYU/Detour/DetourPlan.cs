// SPDX-License-Identifier: AGPL-3.0-or-later
// New for the zWYU port. Replaces the original mutable per-pawn `BaseDetour` record (whose fields were mostly PUAH bookkeeping)
// with one small, immutable description of a single diversion.

using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    /// <summary>
    /// Everything that answers "why is this pawn hauling right now?":
    /// which pawn owns the detour, what they were really going to do, which haul is being attempted, and when it was decided.
    /// A plan is created when a haul job is chosen (<see cref="DetourTracker.Issue"/>), becomes active when that job's driver starts,
    /// and is dropped when the job ends for any reason. It never outlives the pawn (weak per-pawn slots) and is never saved.
    /// </summary>
    internal sealed class DetourPlan
    {
        /// <summary>The pawn that owns this detour. Exactly one pawn; plans are never shared.</summary>
        public readonly Pawn Pawn;

        public readonly DetourKind Kind;

        /// <summary>Where the pawn is really headed: the vanilla job's target (opportunity) or the blueprint/frame/workbench needing the material.</summary>
        public readonly LocalTargetInfo Destination;

        /// <summary>The thing being hauled.</summary>
        public readonly Thing Thing;

        /// <summary>Some cell of the chosen storage; the haul driver picks the real drop cell on arrival.</summary>
        public readonly IntVec3 StoreCell;

        /// <summary>
        /// Opportunities only: the vanilla job the pawn was about to start. Vanilla queues it first, so it resumes after the haul.
        /// Informational (diagnostics); zWYU never starts or stops it.
        /// </summary>
        public readonly Job OriginalJob;

        /// <summary>Game tick (Find.TickManager.TicksGame) at which the haul was chosen.</summary>
        public readonly int DecidedTick;

        /// <summary>Short human-readable reason, e.g. distances/ratios, for logs.</summary>
        public readonly string Explanation;

        /// <summary><see cref="Job.loadID"/> of the haul job this plan belongs to. Guards against pooled-job reuse and stale slots.</summary>
        public int JobLoadId = -1;

        /// <summary>Set if the queued original job was discarded while the haul was running (e.g. the player gave an order).</summary>
        public bool OriginalJobDiscarded;

        public DetourPlan(Pawn pawn, DetourKind kind, LocalTargetInfo destination, Thing thing, IntVec3 storeCell, Job originalJob, string explanation) {
            Pawn        = pawn;
            Kind        = kind;
            Destination = destination;
            Thing       = thing;
            StoreCell   = storeCell;
            OriginalJob = originalJob;
            Explanation = explanation;
            DecidedTick = Find.TickManager?.TicksGame ?? -1;
        }

        public override string ToString() {
            var map        = Pawn?.MapHeld;
            var storeLabel = map != null && StoreCell.IsValid && StoreCell.InBounds(map) ? StoreCell.GetSlotGroup(map)?.parent?.SlotYielderLabel() : null;
            return $"{Kind} owner={Diag.Describe(Pawn)} haul={Diag.Describe(Thing)} -> store {StoreCell} ({storeLabel ?? "no storage"}), "
                   + $"destination={Diag.Describe(Destination)}, originalJob={(OriginalJob == null ? "none" : Diag.Describe(OriginalJob))}, decided@{DecidedTick}"
                   + (Explanation.NullOrEmpty() ? string.Empty : $" [{Explanation}]");
        }
    }
}
