// SPDX-License-Identifier: AGPL-3.0-or-later
// zWYU diagnostics. New code for the zWYU port (the original mod only had ad-hoc Debug.WriteLine calls).

using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    /// <summary>How much decision logging is written to the RimWorld log. Off by default.</summary>
    internal enum DebugLevel
    {
        /// <summary>Only errors and compatibility failures.</summary>
        Off = 0,

        /// <summary>One line per decision: detour selected / none found / started / ended, with reject-reason totals.</summary>
        Summary = 1,

        /// <summary>Additionally one line per rejected candidate. Can be very chatty; for short reproductions.</summary>
        Verbose = 2,
    }

    internal enum LogKind { Message, Warning, Error }

    /// <summary>
    /// Subsystem-tagged logging. Every message is "[zWYU][Subsystem] text".
    /// Callers must guard formatting work with <see cref="Summary"/> / <see cref="Verbose"/> so that the default
    /// (Off) configuration allocates nothing on the hot paths.
    /// </summary>
    internal static class Diag
    {
        public const string Tag = "[zWYU]";

        // Subsystem names used in log lines (kept short so log filtering is easy).
        public const string Startup     = "Startup";
        public const string Opportunity = "Opportunity";
        public const string BeforeCarry = "BeforeCarry";
        public const string Lifecycle   = "Lifecycle";
        public const string Compat      = "Compat";
        public const string Settings    = "Settings";

        /// <summary>
        /// Where log text goes. The default writes to RimWorld's log; the PatchAudit tool swaps in a console sink so the failure paths can be
        /// exercised outside the game. `onceKey` is non-zero for "log only once" messages.
        /// </summary>
        public static Action<LogKind, string, int> Sink = DefaultSink;

        static void DefaultSink(LogKind kind, string text, int onceKey) {
            switch (kind) {
                case LogKind.Warning:
                    if (onceKey != 0) Log.WarningOnce(text, onceKey); else Log.Warning(text);
                    break;
                case LogKind.Error:
                    if (onceKey != 0) Log.ErrorOnce(text, onceKey); else Log.Error(text);
                    break;
                default:
                    Log.Message(text);
                    break;
            }
        }

        public static DebugLevel Level => ZwyuMod.Settings?.DebugLevel ?? DebugLevel.Off;
        public static bool       Summary => Level >= DebugLevel.Summary;
        public static bool       Verbose => Level >= DebugLevel.Verbose;

        /// <summary>Always-on informational line (startup banner, feature status). Use sparingly.</summary>
        public static void Info(string subsystem, string message) => Sink(LogKind.Message, $"{Tag}[{subsystem}] {message}", 0);

        /// <summary>Decision logging; only emitted at <see cref="DebugLevel.Summary"/> or higher. Guard with <c>if (Diag.Summary)</c>.</summary>
        public static void Decision(string subsystem, string message) {
            if (Summary)
                Sink(LogKind.Message, $"{Tag}[{subsystem}] {message}", 0);
        }

        /// <summary>Per-candidate logging; only at <see cref="DebugLevel.Verbose"/>. Guard with <c>if (Diag.Verbose)</c>.</summary>
        public static void Trace(string subsystem, string message) {
            if (Verbose)
                Sink(LogKind.Message, $"{Tag}[{subsystem}] {message}", 0);
        }

        public static void Warn(string subsystem, string message) => Sink(LogKind.Warning, $"{Tag}[{subsystem}] {message}", 0);

        /// <summary>Warning shown once per distinct message (RimWorld's own ErrorOnce de-duplication).</summary>
        public static void WarnOnce(string subsystem, string message) => Sink(LogKind.Warning, $"{Tag}[{subsystem}] {message}", OnceKey(subsystem, message));

        public static void Error(string subsystem, string message, Exception ex = null) =>
            Sink(LogKind.Error, $"{Tag}[{subsystem}] {message}" + (ex != null ? $"\n{ex}" : string.Empty), 0);

        /// <summary>Error shown once per distinct message; for failures that could otherwise repeat every tick.</summary>
        public static void ErrorOnce(string subsystem, string message, Exception ex = null) =>
            Sink(LogKind.Error, $"{Tag}[{subsystem}] {message}" + (ex != null ? $"\n{ex}" : string.Empty), OnceKey(subsystem, message));

        static int OnceKey(string subsystem, string message) {
            var key = ($"{subsystem}:{message}").GetHashCode();
            return key == 0 ? 1 : key;
        }

        // ---- describing things for log lines ---------------------------------------------------------------------------------

        public static string Describe(Pawn pawn) => pawn == null ? "null-pawn" : $"{pawn.LabelShortCap}#{pawn.thingIDNumber}";

        public static string Describe(Thing thing) =>
            thing == null ? "null-thing" : $"{thing.LabelNoCount}x{thing.stackCount}#{thing.thingIDNumber}@{(thing.Spawned ? thing.Position.ToString() : "unspawned")}";

        public static string Describe(Job job) => job == null ? "null-job" : $"{job.def?.defName}({job.targetA},{job.targetB})#{job.loadID}";

        public static string Describe(LocalTargetInfo target) =>
            !target.IsValid ? "invalid-target" : target.HasThing ? $"{target.Thing.LabelShort}@{target.Cell}" : target.Cell.ToString();
    }
}
