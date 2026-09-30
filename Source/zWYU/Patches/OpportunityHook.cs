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
//   1. Prefix on TryOpportunisticJob        : the invocation that finds no active context acquires it (pawn, job) and owns it.
//   2. Prefix on ListerHaulables.ThingsPotentiallyNeedingHauling
//                                           : vanilla calls this exactly once, immediately after its preconditions pass and right before
//                                             its loop. While the context is armed we run our search there and hand vanilla an EMPTY
//                                             collection, so vanilla's own loop does nothing.
//   3. Finalizer on TryOpportunisticJob     : if vanilla returned null and we found a haul, return ours; always clear the context
//                                             (a finalizer runs even when an exception unwinds, so the context can never leak).
// Ownership: exactly one invocation of TryOpportunisticJob can own the context. Each invocation carries an explicit token from its Prefix
// to its Finalizer (Harmony `__state`); ONLY the invocation that acquired the context may deliver its result or clear it. A nested
// (re-entrant) invocation is counted (`Depth`) but never acquires, and its Finalizer touches nothing. While a nested invocation is in
// flight the lister hook does not fire, so a nested call keeps vanilla's own loop and cannot consume the owner's armed search.
//
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
    /// <summary>
    /// Explicit state of the ONE Pawn_JobTracker.TryOpportunisticJob invocation that owns the search context. At most one context is active;
    /// it is acquired in that invocation's Prefix (<see cref="Acquire"/>) and released only by that same invocation's Finalizer
    /// (<see cref="Release"/>), identified by the token carried in Harmony's `__state`.
    /// </summary>
    internal static class OpportunityContext
    {
        // Tokens carried from Prefix to Finalizer for one invocation (Harmony `__state`; its default, 0, means "our Prefix did not run").
        public const int TokenNotRun  = 0; // Finalizer must do nothing at all
        public const int TokenIgnored = 1; // our Prefix ran but did not acquire (nested call, or the mod/feature is off): counted, never touches the context
        public const int TokenOwner   = 2; // this invocation acquired the context and alone may deliver its result and clear it

        public static bool Active;   // a context exists (owned by one invocation)
        public static bool Armed;    // still waiting for vanilla to reach ThingsPotentiallyNeedingHauling
        public static bool SelfTest; // startup self-test in progress: the anchor prefix only proves it is reached (see OpportunityAnchor.SelfTest)
        public static Pawn Pawn;
        public static Job  Job;      // the vanilla job the owner's pawn is about to start
        public static Job  Result;   // the haul our search found, delivered by the owner's finalizer

        /// <summary>Number of TryOpportunisticJob invocations whose Prefix ran and whose Finalizer has not yet (owner or not).</summary>
        public static int Depth;

        /// <summary>The <see cref="Depth"/> at which the owner acquired the context. The lister hook fires only when Depth == OwnerDepth (no nested call in flight).</summary>
        public static int OwnerDepth;

        // Observability (shown in the settings window at Summary/Verbose): if `Entered` keeps rising while `Engaged` stays 0 in normal play,
        // the ThingsPotentiallyNeedingHauling anchor is not being reached and the opportunity feature is silently inert.
        public static int Entered;
        public static int Engaged;
        public static int Found;

        /// <summary>
        /// Called from the Prefix of every invocation. Returns the token for its Finalizer. The context is acquired only if
        /// <paramref name="wantContext"/> and no context is active; otherwise the invocation is <see cref="TokenIgnored"/>.
        /// </summary>
        public static int Acquire(Pawn pawn, Job job, bool wantContext) {
            Depth++;
            if (!wantContext) return TokenIgnored;

            if (Active) {
                // TryOpportunisticJob re-entering itself would mean a vanilla or mod change we did not anticipate; do nothing rather than nest.
                Diag.WarnOnce(Diag.Opportunity, "TryOpportunisticJob re-entered while a zWYU search context was active; ignoring the inner call (the outer call keeps the context).");
                return TokenIgnored;
            }

            Active     = true;
            Armed      = true;
            Pawn       = pawn;
            Job        = job;
            Result     = null;
            OwnerDepth = Depth;
            Entered++;
            return TokenOwner;
        }

        /// <summary>
        /// Called from the Finalizer of every invocation with the token its Prefix produced. Only the owner delivers its result and clears the
        /// context; any other invocation leaves the context exactly as it found it. Never swallows the exception it is given.
        /// </summary>
        public static Exception Release(int token, Exception exception, ref Job result) {
            if (token == TokenNotRun) return exception; // our Prefix did not run for this invocation: nothing to undo

            if (Depth > 0) Depth--;
            if (token != TokenOwner) return exception;  // not the owner: must not deliver, must not clear

            var found = Result;
            if (found != null && exception == null && result == null)
                result = found;
            else if (found != null && Diag.Summary)
                Diag.Decision(Diag.Opportunity, $"discarding selected haul {Diag.Describe(found)}: TryOpportunisticJob {(exception != null ? "threw" : "returned another job")}");

            Reset();
            return exception;
        }

        public static void Reset() {
            Active     = false;
            Armed      = false;
            Pawn       = null;
            Job        = null;
            Result     = null;
            OwnerDepth = 0;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    internal static class TryOpportunisticJob_Patch
    {
        [HarmonyPrefix]
        static void Enter(Pawn ___pawn, Job job, out int __state) {
            var settings = ZwyuMod.Settings;
            var want     = Features.IsAvailable(Feature.OpportunityHook) && settings != null && settings.Enabled && ___pawn != null && job != null;
            __state = OpportunityContext.Acquire(___pawn, job, want);
        }

        // Finalizer, not Postfix: it must run (and, for the owner, clear the context) even if vanilla or another mod throws inside the method.
        [HarmonyFinalizer]
        static Exception Exit(Exception __exception, ref Job __result, int __state) => OpportunityContext.Release(__state, __exception, ref __result);
    }

    [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.ThingsPotentiallyNeedingHauling))]
    internal static class ThingsPotentiallyNeedingHauling_Patch
    {
        static readonly Thing[] NoThings = new Thing[0];

        [HarmonyPrefix]
        static bool RunOpportunitySearch(ref ICollection<Thing> __result) {
            if (!OpportunityContext.Armed) return true; // every other caller in the game: untouched

            // A nested TryOpportunisticJob (Depth > OwnerDepth) reaching the lister must keep vanilla's loop and must not consume the owner's search.
            if (OpportunityContext.Depth != OpportunityContext.OwnerDepth) return true;

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
                OpportunityContext.SelfTest   = true;
                OpportunityContext.Active     = true;
                OpportunityContext.Armed      = true;
                OpportunityContext.OwnerDepth = OpportunityContext.Depth; // the self-test is its own owner: no TryOpportunisticJob is in flight
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
