// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The geometric acceptance rules of an opportunistic detour, extracted unchanged from While You're Up's `CanHaul()` / `MaxRanges`
// (OpportunityDetour.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin.
//
// This file deliberately references NO RimWorld type: every rule takes plain distances, so it can be unit-tested
// (Source/zWYU.Tests links this very file). Distances are in cells; "Sq" arguments are squared distances, because the
// original compared squares to avoid a Sqrt where it is not summing distances.
//
// Vocabulary (the original's):  start = pawn's position when the job is assigned;  thing = the haulable;
//   store = the storage cell for it;  job = where the pawn is really headed (the job target, or the first bill ingredient).
//   origTrip = start -> job.

namespace zWYU
{
    /// <summary>Why a cheap range check rejected a candidate (used only for diagnostics; the verdict is the same either way).</summary>
    internal enum RangeCheck
    {
        Pass,
        AbsoluteLimit,
        RatioLimit,
    }

    /// <summary>
    /// The "cheap" range limits. They are a performance heuristic, not a correctness rule: a candidate that exceeds them
    /// is retried after the limits have been <see cref="Expanded"/>, so the expensive checks run in the most optimistic order.
    /// </summary>
    internal struct RangeLimits
    {
        public int   ExpandCount;
        public float StartToThing, StartToThingPctOrigTrip;
        public float StoreToJob,   StoreToJobPctOrigTrip;

        public static RangeLimits Initial(float startToThing, float startToThingPct, float storeToJob, float storeToJobPct) => new RangeLimits {
            ExpandCount             = 0,
            StartToThing            = startToThing,
            StartToThingPctOrigTrip = startToThingPct,
            StoreToJob              = storeToJob,
            StoreToJobPctOrigTrip   = storeToJobPct,
        };

        public RangeLimits Expanded(float factor) => new RangeLimits {
            ExpandCount             = ExpandCount + 1,
            StartToThing            = StartToThing * factor,
            StartToThingPctOrigTrip = StartToThingPctOrigTrip * factor,
            StoreToJob              = StoreToJob * factor,
            StoreToJobPctOrigTrip   = StoreToJobPctOrigTrip * factor,
        };
    }

    internal static class TripRules
    {
        static float Sq(float v) => v * v;

        /// <summary>start->thing against the absolute range and the ratio-of-original-trip range.</summary>
        public static RangeCheck CheckStartToThingRange(float startToThingSq, float startToJobSq, in RangeLimits limits) {
            if (startToThingSq > Sq(limits.StartToThing)) return RangeCheck.AbsoluteLimit;
            if (startToThingSq > startToJobSq * Sq(limits.StartToThingPctOrigTrip)) return RangeCheck.RatioLimit;
            return RangeCheck.Pass;
        }

        /// <summary>
        /// Early hard limit, before any storage is looked up: even with a free (zero-length) thing->store leg, start->thing->job
        /// already exceeds the maximum total trip. "If this one exceeds the maximum the later total-trip check certainly will."
        /// </summary>
        public static bool ExceedsTotalTripBeforeStorage(float startToThing, float thingToJob, float startToJob, float maxTotalTripPct) =>
            startToThing + thingToJob > startToJob * maxTotalTripPct;

        /// <summary>store->job against the absolute range and the ratio-of-original-trip range.</summary>
        public static RangeCheck CheckStoreToJobRange(float storeToJobSq, float startToJobSq, in RangeLimits limits) {
            if (storeToJobSq > Sq(limits.StoreToJob)) return RangeCheck.AbsoluteLimit;
            if (storeToJobSq > startToJobSq * Sq(limits.StoreToJobPctOrigTrip)) return RangeCheck.RatioLimit;
            return RangeCheck.Pass;
        }

        /// <summary>
        /// The two "additional legs" of the new trip (start->thing and store->job) against the original trip. The thing->store leg is
        /// excluded on purpose: hauling has to happen anyway, what matters is how much the pawn is pulled off their way.
        /// </summary>
        public static bool ExceedsNewLegs(float startToThing, float storeToJob, float startToJob, float maxNewLegsPct) =>
            startToThing + storeToJob > startToJob * maxNewLegsPct;

        /// <summary>start->thing->store->job against the original trip.</summary>
        public static bool ExceedsTotalTrip(float startToThing, float thingToStore, float storeToJob, float startToJob, float maxTotalTripPct) =>
            startToThing + thingToStore + storeToJob > startToJob * maxTotalTripPct;
    }
}
