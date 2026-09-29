// SPDX-License-Identifier: AGPL-3.0-or-later
//
// REPLACES the original's IL transpiler on Pawn_JobTracker.TryOpportunisticJob (OpportunityDetour.cs, WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin.
//
// What the original transpiler implemented (the gameplay semantic):
//   "When vanilla has decided that a pawn about to start a job may look for an opportunistic haul - i.e. after ALL of vanilla's own
//    preconditions - run WYU's search INSTEAD of vanilla's built-in haulable loop, and return its result."
// The original located the spot by "3 instructions before the listerHaulables field load" and returned early from there.
// That is exactly the kind of offset-anchored IL surgery that broke with every vanilla refactor (1.6 even changed the method's
// signature to `TryOpportunisticJob(Job finalizerJob, Job job)`).
//
// The 1.6 hook, with no IL manipulation, using the same anchor point expressed as behavior:
//   1. Prefix on TryOpportunisticJob        : remember (pawn, job) in an explicit, non-reentrant context.
//   2. Prefix on ListerHaulables.ThingsPotentiallyNeedingHauling
//                                           : vanilla calls this exactly once, immediately after its preconditions pass and right before
//                                             its loop. While the context is armed we run our search there and hand vanilla an EMPTY
//                                             collection, so vanilla's own loop does nothing.
//   3. Finalizer on TryOpportunisticJob     : if vanilla returned null and we found a haul, return ours; always clear the context
//                                             (a finalizer runs even when an exception unwinds, so the context can never leak).
// Failure modes all degrade to vanilla: patch cannot apply -> feature off; anchor missing -> feature off (verified at startup);
// our search throws -> the collection is left untouched so vanilla's loop runs; the hook is never reached -> nothing happens.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    /// <summary>Explicit state of one in-flight Pawn_JobTracker.TryOpportunisticJob call. Not reentrant, never outlives the call.</summary>
    internal static class OpportunityContext
    {
        public static bool Active;   // between our TryOpportunisticJob prefix and finalizer
        public static bool SelfTest; // startup self-test in progress: the anchor prefix only proves it is reached (see OpportunityAnchor.SelfTest)
        public static bool Armed;    // still waiting for vanilla to reach ThingsPotentiallyNeedingHauling
        public static Pawn Pawn;
        public static Job  Job;      // the vanilla job the pawn is about to start
        public static Job  Result;   // the haul our search found, delivered by the finalizer

        // Observability (shown in the settings window at Summary/Verbose): if `Entered` keeps rising while `Engaged` stays 0 in normal play,
        // the ThingsPotentiallyNeedingHauling anchor is not being reached and the opportunity feature is silently inert.
        public static int Entered;
        public static int Engaged;
        public static int Found;

        public static void Reset() {
            Active = false;
            Armed  = false;
            Pawn   = null;
            Job    = null;
            Result = null;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    internal static class TryOpportunisticJob_Patch
    {
        [HarmonyPrefix]
        static void Enter(Pawn ___pawn, Job job) {
            if (!Features.IsAvailable(Feature.OpportunityHook)) return;
            var settings = ZwyuMod.Settings;
            if (settings == null || !settings.Enabled || ___pawn == null || job == null) return;

            if (OpportunityContext.Active) {
                // TryOpportunisticJob re-entering itself would mean a vanilla or mod change we did not anticipate; do nothing rather than nest.
                Diag.WarnOnce(Diag.Opportunity, "TryOpportunisticJob re-entered while a zWYU search context was active; ignoring the inner call.");
                return;
            }

            OpportunityContext.Active = true;
            OpportunityContext.Armed  = true;
            OpportunityContext.Pawn   = ___pawn;
            OpportunityContext.Job    = job;
            OpportunityContext.Result = null;
            OpportunityContext.Entered++;
        }

        // Finalizer, not Postfix: it must run (and clear the context) even if vanilla or another mod throws inside the method.
        [HarmonyFinalizer]
        static Exception Exit(Exception __exception, ref Job __result) {
            if (!OpportunityContext.Active) return __exception;

            var found = OpportunityContext.Result;
            if (found != null && __exception == null && __result == null)
                __result = found;
            else if (found != null && Diag.Summary)
                Diag.Decision(Diag.Opportunity, $"discarding selected haul {Diag.Describe(found)}: TryOpportunisticJob {(__exception != null ? "threw" : "returned another job")}");

            OpportunityContext.Reset();
            return __exception; // never swallow anything
        }
    }

    [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.ThingsPotentiallyNeedingHauling))]
    internal static class ThingsPotentiallyNeedingHauling_Patch
    {
        static readonly Thing[] NoThings = new Thing[0];

        [HarmonyPrefix]
        static bool RunOpportunitySearch(ref ICollection<Thing> __result) {
            if (!OpportunityContext.Armed) return true; // every other caller in the game: untouched

            OpportunityContext.Armed = false; // fire once; our own search calls this very method
            OpportunityContext.Engaged++;

            if (OpportunityContext.SelfTest) { // startup self-test: just prove the hook is reached; no pawn, no search
                __result = NoThings;
                return false;
            }

            try {
                var found = OpportunityEngine.Run(OpportunityContext.Pawn, OpportunityContext.Job);
                OpportunityContext.Result = found;
                if (found != null) OpportunityContext.Found++;
            } catch (Exception e) {
                OpportunityContext.Result = null;
                Features.RecordFault(Feature.OpportunityHook, nameof(ThingsPotentiallyNeedingHauling_Patch), e);
                return true; // leave vanilla's collection alone: normal RimWorld behavior for this call
            }

            __result = NoThings; // vanilla's loop has nothing to iterate; the finalizer delivers our result
            return false;
        }
    }

    /// <summary>Startup verification that the vanilla anchor the hook depends on is still where we expect it, and that it really fires.</summary>
    internal static class OpportunityAnchor
    {
        /// <summary>
        /// Checks (by reading vanilla's IL, not by patching it) that TryOpportunisticJob still has the shape the hook assumes:
        /// parameters (Job finalizerJob, Job job) and a call to ListerHaulables.ThingsPotentiallyNeedingHauling.
        /// Returns null if fine, otherwise the reason the feature cannot work.
        /// </summary>
        /// <summary>
        /// Runtime proof that the ListerHaulables patch is actually invoked on THIS runtime. A method as small as
        /// ThingsPotentiallyNeedingHauling ("return haulables;") could in principle be inlined by the JIT into its callers, in which
        /// case a Harmony patch on it is silently bypassed and the opportunity feature would be inert without any error.
        /// This calls it once, in a self-test context, right after patching: if the prefix did not run, the feature is disabled with an
        /// explicit reason instead of being silently dead. Returns null if the hook fires.
        /// </summary>
        public static string SelfTest() {
            var (entered, engaged, found) = (OpportunityContext.Entered, OpportunityContext.Engaged, OpportunityContext.Found);
            try {
                var lister = new ListerHaulables(null);
                OpportunityContext.SelfTest = true;
                OpportunityContext.Active   = true;
                OpportunityContext.Armed    = true;
                lister.ThingsPotentiallyNeedingHauling();
                return OpportunityContext.Armed
                    ? "the ListerHaulables.ThingsPotentiallyNeedingHauling patch was NOT invoked by a direct call (the runtime may have inlined the method); the haulable-search hook cannot engage."
                    : null;
            } catch (Exception e) {
                return "hook self-test threw: " + e.Message;
            } finally {
                OpportunityContext.SelfTest = false;
                OpportunityContext.Reset();
                (OpportunityContext.Entered, OpportunityContext.Engaged, OpportunityContext.Found) = (entered, engaged, found);
            }
        }

        public static string Verify() {
            try {
                var toj = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob));
                if (toj == null) return "Pawn_JobTracker.TryOpportunisticJob no longer exists.";

                var ps = toj.GetParameters();
                if (ps.Length != 2 || ps[0].ParameterType != typeof(Job) || ps[1].ParameterType != typeof(Job) || ps[1].Name != "job")
                    return "Pawn_JobTracker.TryOpportunisticJob has a different signature than (Job finalizerJob, Job job): "
                           + $"({string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name))}).";

                var lister = AccessTools.DeclaredMethod(typeof(ListerHaulables), nameof(ListerHaulables.ThingsPotentiallyNeedingHauling));
                if (lister == null || !typeof(ICollection<Thing>).IsAssignableFrom(lister.ReturnType))
                    return "ListerHaulables.ThingsPotentiallyNeedingHauling no longer exists or no longer returns an ICollection<Thing>.";

                var calls = PatchProcessor.GetOriginalInstructions(toj).Any(ci => ci.Calls(lister));
                if (!calls)
                    return "vanilla's TryOpportunisticJob no longer calls ListerHaulables.ThingsPotentiallyNeedingHauling, so there is no point to hook the haulable search.";

                return null;
            } catch (Exception e) {
                return "could not inspect vanilla TryOpportunisticJob: " + e.Message;
            }
        }
    }
}
