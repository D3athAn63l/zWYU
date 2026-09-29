// SPDX-License-Identifier: AGPL-3.0-or-later
// New for the zWYU port. See the csproj header.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

namespace zWYU.PatchAudit
{
    // A patch that can never apply: used to prove the failure path disables exactly one feature and explains itself.
    [HarmonyPatch("Verse.Pawn", "ThisMethodDoesNotExist")]
    static class BogusPatch
    {
        [HarmonyPrefix]
        static void Prefix() { }
    }

    static class Program
    {
        static int failures;

        static void Check(bool ok, string what) {
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
            if (!ok) failures++;
        }

        static int Main(string[] args) {
            string managed = Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR");
            string modDll  = null;
            for (var i = 0; i < args.Length; i++) {
                if (args[i] == "--managed" && i + 1 < args.Length) managed = args[++i];
                else if (args[i] == "--mod" && i + 1 < args.Length) modDll = args[++i];
            }
            if (string.IsNullOrEmpty(managed) || !Directory.Exists(managed)) {
                Console.Error.WriteLine("Pass --managed <RimWorld Managed dir> (or set RIMWORLD_MANAGED_DIR).");
                return 2;
            }
            modDll ??= FindModDll();
            if (modDll == null || !File.Exists(modDll)) {
                Console.Error.WriteLine("zWYU.dll not found; build Source/zWYU first (or pass --mod <path>).");
                return 2;
            }

            var probeDirs = new[] { managed, Path.GetDirectoryName(Path.GetFullPath(modDll)) };
            AssemblyLoadContext.Default.Resolving += (context, name) => {
                foreach (var dir in probeDirs) {
                    var candidate = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                }
                return null;
            };

            Console.WriteLine($"managed: {managed}");
            Console.WriteLine($"mod:     {modDll}");

            var mod = Assembly.LoadFrom(modDll);
            var asmCsharp = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            Console.WriteLine($"game:    {asmCsharp.GetName().Name} {asmCsharp.GetName().Version}");

            Type T(string name) => mod.GetType(name, throwOnError: true);
            var diag      = T("zWYU.Diag");
            var features  = T("zWYU.Features");
            var feature   = T("zWYU.Feature");
            var bootstrap = T("zWYU.PatchBootstrap");
            var anchor    = T("zWYU.OpportunityAnchor");
            var logKind   = T("zWYU.LogKind");

            // capture zWYU's own log output instead of touching Unity's logger
            var captured = new List<string>();
            diag.GetField("Sink", BindingFlags.Public | BindingFlags.Static).SetValue(null, BuildSink(logKind, captured));

            bool IsAvailable(string featureName) =>
                (bool)features.GetMethod("IsAvailable", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { Enum.Parse(feature, featureName) });

            Console.WriteLine();
            Console.WriteLine("1. vanilla anchors");
            var problem = (string)anchor.GetMethod("Verify", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            Check(problem == null, "OpportunityAnchor.Verify(): " + (problem ?? "TryOpportunisticJob(Job finalizerJob, Job job) still calls ListerHaulables.ThingsPotentiallyNeedingHauling"));

            Console.WriteLine();
            Console.WriteLine("2. apply every patch to the real assembly");
            const string harmonyId = "zWYU.audit";
            var harmony = new Harmony(harmonyId);
            try {
                bootstrap.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { harmony });
            } catch (TargetInvocationException e) {
                Check(false, "PatchBootstrap.Apply threw: " + e.InnerException);
            }
            foreach (var f in new[] { "OpportunityHook", "SupplyHook", "LifecycleTracking" })
                Check(IsAvailable(f), $"feature {f} available after patching (includes the startup hook self-test)");
            Check(!captured.Any(c => c.Contains("NOT invoked")), "the ListerHaulables hook self-test proved the patch is reached on this runtime");
            Check(!captured.Any(c => c.StartsWith("Error")), "no error was logged while patching" + (captured.Any(c => c.StartsWith("Error")) ? ":\n" + string.Join("\n", captured.Where(c => c.StartsWith("Error"))) : ""));

            var expected = new (string type, string method, string kinds)[] {
                ("RimWorld.ListerHaulables", "ThingsPotentiallyNeedingHauling", "P"),
                ("Verse.AI.Pawn_JobTracker", "TryOpportunisticJob", "PF"),
                ("Verse.AI.Pawn_JobTracker", "ClearQueuedJobs", "S"),
                ("RimWorld.WorkGiver_ConstructDeliverResources", "ResourceDeliverJobFor", "S"),
                ("RimWorld.WorkGiver_ConstructDeliverResourcesToBlueprints", "HasJobOnThing", "PF"),
                ("RimWorld.WorkGiver_ConstructDeliverResourcesToFrames", "HasJobOnThing", "PF"),
                ("Verse.AI.JobDriver_HaulToCell", "Notify_Starting", "S"),
                ("Verse.AI.JobDriver_HaulToCell", "GetReport", "S"),
                ("Verse.Pawn", "DeSpawn", "P"),
                ("Verse.AI.JobUtility", "TryStartErrorRecoverJob", "P"),
            };

            Console.WriteLine();
            Console.WriteLine("3. patch map (expected == actual)");
            var ours = new Dictionary<string, string>();
            foreach (var method in Harmony.GetAllPatchedMethods()) {
                var info = Harmony.GetPatchInfo(method);
                var kinds = string.Concat(
                    info.Prefixes.Any(p => p.owner == harmonyId) ? "P" : "",
                    info.Postfixes.Any(p => p.owner == harmonyId) ? "S" : "",
                    info.Transpilers.Any(p => p.owner == harmonyId) ? "T" : "",
                    info.Finalizers.Any(p => p.owner == harmonyId) ? "F" : "");
                if (kinds.Length > 0)
                    ours[$"{method.DeclaringType.FullName}.{method.Name}"] = kinds;
            }
            foreach (var (type, method, kinds) in expected) {
                ours.TryGetValue($"{type}.{method}", out var actual);
                Check(actual == kinds, $"{type}.{method}: expected [{kinds}] actual [{actual ?? "unpatched"}]");
            }
            var unexpected = ours.Keys.Except(expected.Select(e => $"{e.type}.{e.method}")).ToList();
            Check(unexpected.Count == 0, "no unexpected patched methods" + (unexpected.Count > 0 ? ": " + string.Join(", ", unexpected) : ""));
            Check(!ours.Values.Any(k => k.Contains('T')), "no IL transpilers are used anywhere");

            Console.WriteLine();
            Console.WriteLine("4. failure safety: a patch that cannot apply disables only its own feature");
            captured.Clear();
            var tryPatch = features.GetMethod("TryPatch", BindingFlags.Public | BindingFlags.Static);
            var applied = (bool)tryPatch.Invoke(null, new object[] { harmony, typeof(BogusPatch), Enum.Parse(feature, "CommonSenseGuard") });
            Check(!applied, "TryPatch(BogusPatch) reports failure instead of throwing");
            Check(!IsAvailable("CommonSenseGuard"), "the owning feature (CommonSenseGuard) is now disabled");
            Check(IsAvailable("OpportunityHook") && IsAvailable("SupplyHook") && IsAvailable("LifecycleTracking"), "the other features are untouched");
            Check(captured.Any(c => c.StartsWith("Error") && c.Contains("DISABLED") && c.Contains("BogusPatch")), "the log names the feature and the affected patch");

            Console.WriteLine();
            Console.WriteLine("5. dynamic wiring: the patched vanilla methods behave as designed when they actually run");
            RunDynamicChecks(mod, asmCsharp, harmony, harmonyId, T);

            harmony.UnpatchAll(harmonyId);
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "PATCH AUDIT PASSED" : $"PATCH AUDIT FAILED ({failures} failed check(s))");
            return failures == 0 ? 0 : 1;
        }


        static void RunDynamicChecks(Assembly mod, Assembly game, Harmony harmony, string harmonyId, Func<string, Type> T) {
            // The hooks were unpatched by check 4's failure test only for their own feature, so re-apply everything cleanly first.
            harmony.UnpatchAll(harmonyId);
            var features  = T("zWYU.Features");
            var bootstrap = T("zWYU.PatchBootstrap");
            foreach (var name in new[] { "OpportunityHook", "SupplyHook", "LifecycleTracking", "CommonSenseGuard" }) {
                var disabledField = features.GetField("disabled", BindingFlags.NonPublic | BindingFlags.Static);
                var disabled = (bool[])disabledField.GetValue(null);
                disabled[(int)Enum.Parse(T("zWYU.Feature"), name)] = false;
            }
            bootstrap.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { harmony });

            var ctx      = T("zWYU.OpportunityContext");
            var settingsType = T("zWYU.ZwyuSettings");
            var modType  = T("zWYU.ZwyuMod");
            object Get(string field) => ctx.GetField(field, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            void Set(string field, object value) => ctx.GetField(field, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);

            // enable the mod with default settings
            modType.GetField("Settings", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, Activator.CreateInstance(settingsType));

            var pawnType   = game.GetType("Verse.Pawn", true);
            var jobType    = game.GetType("Verse.AI.Job", true);
            var listerType = game.GetType("RimWorld.ListerHaulables", true);
            var trackerType = game.GetType("Verse.AI.Pawn_JobTracker", true);

            object Uninit(Type t) => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
            object MakeUnspawnedPawn() {
                var pawn = Uninit(pawnType);
                // Thing.mapIndexOrState < 0 means "not spawned", which is what makes OpportunityEngine.Run return immediately.
                var f = game.GetType("Verse.Thing", true).GetField("mapIndexOrState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f.SetValue(pawn, Convert.ChangeType(-1, f.FieldType));
                return pawn;
            }

            // (a) ThingsPotentiallyNeedingHauling: untouched for everybody else, intercepted exactly once inside an armed context
            var lister = Activator.CreateInstance(listerType, new object[] { null });
            var listerMethod = listerType.GetMethod("ThingsPotentiallyNeedingHauling");
            var realSet = listerType.GetField("haulables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lister);

            Set("Armed", false);
            var untouched = listerMethod.Invoke(lister, null);
            Check(ReferenceEquals(untouched, realSet), "ThingsPotentiallyNeedingHauling outside a search returns vanilla's own collection");

            var pawnObj = MakeUnspawnedPawn();
            var jobObj  = Uninit(jobType);
            var engagedBefore = (int)Get("Engaged");
            Set("Pawn", pawnObj);
            Set("Job", jobObj);
            Set("Active", true);
            Set("Armed", true);
            var intercepted = (System.Collections.ICollection)listerMethod.Invoke(lister, null);
            Check(!ReferenceEquals(intercepted, realSet) && intercepted.Count == 0, "inside an armed search vanilla's loop is handed an empty collection");
            Check(!(bool)Get("Armed") && (int)Get("Engaged") == engagedBefore + 1, "the search fires exactly once (Armed cleared, Engaged counter +1)");
            Check(Get("Result") == null, "a search that finds nothing leaves Result null");
            var secondCall = listerMethod.Invoke(lister, null);
            Check(ReferenceEquals(secondCall, realSet), "a second call in the same TryOpportunisticJob (or our own search) sees vanilla's collection");
            ctx.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

            // (b) the TryOpportunisticJob finalizer contract, called directly
            var patchType = T("zWYU.TryOpportunisticJob_Patch");
            var exit = patchType.GetMethod("Exit", BindingFlags.NonPublic | BindingFlags.Static);
            var fakeFound = Uninit(jobType);

            object[] ExitArgs(Exception ex, object current) => new object[] { ex, current };
            void Arm(object found) {
                Set("Active", true); Set("Armed", false); Set("Pawn", pawnObj); Set("Job", jobObj); Set("Result", found);
            }

            Arm(fakeFound);
            var args1 = ExitArgs(null, null);
            var ret1 = exit.Invoke(null, args1);
            Check(ret1 == null && ReferenceEquals(args1[1], fakeFound), "finalizer: vanilla returned null + we found a haul -> our haul is returned");
            Check(!(bool)Get("Active") && Get("Pawn") == null && Get("Job") == null && Get("Result") == null, "finalizer: context cleared after delivering");

            Arm(fakeFound);
            var vanillaJob = Uninit(jobType);
            var args2 = ExitArgs(null, vanillaJob);
            exit.Invoke(null, args2);
            Check(ReferenceEquals(args2[1], vanillaJob), "finalizer: a job another mod/vanilla already returned is never overwritten");
            Check(!(bool)Get("Active"), "finalizer: context cleared even when not delivering");

            Arm(fakeFound);
            var boom = new InvalidOperationException("boom");
            var args3 = ExitArgs(boom, null);
            var ret3 = exit.Invoke(null, args3);
            Check(ReferenceEquals(ret3, boom) && args3[1] == null, "finalizer: an exception is passed through unchanged and no haul is delivered");
            Check(!(bool)Get("Active") && Get("Result") == null, "finalizer: context cleared on the exception path");

            // (c) end to end through the real patched TryOpportunisticJob: vanilla throws (uninitialized pawn), zWYU must not leak state
            var tracker = Uninit(trackerType);
            trackerType.GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tracker, pawnObj);
            var enteredBefore = (int)Get("Entered");
            Exception thrown = null;
            try {
                trackerType.GetMethod("TryOpportunisticJob").Invoke(tracker, new object[] { null, jobObj });
            } catch (TargetInvocationException e) {
                thrown = e.InnerException;
            }
            Check(thrown != null, "real patched TryOpportunisticJob: vanilla's own exception still propagates (zWYU never swallows) [" + thrown?.GetType().Name + "]");
            Check((int)Get("Entered") == enteredBefore + 1, "real patched TryOpportunisticJob: the prefix ran (Entered counter +1)");
            Check(!(bool)Get("Active") && !(bool)Get("Armed") && Get("Pawn") == null && Get("Job") == null && Get("Result") == null,
                "real patched TryOpportunisticJob: context fully cleared after the exception unwound");

            // (d) disabled mod: the prefix must not even arm the context
            var settings = modType.GetField("Settings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            settingsType.GetField("Enabled").SetValue(settings, false);
            enteredBefore = (int)Get("Entered");
            try { trackerType.GetMethod("TryOpportunisticJob").Invoke(tracker, new object[] { null, jobObj }); } catch (TargetInvocationException) { }
            Check((int)Get("Entered") == enteredBefore && !(bool)Get("Active"), "with the mod disabled in settings the context is never armed");
            settingsType.GetField("Enabled").SetValue(settings, true);
        }

        // Builds an Action<LogKind, string, int> for an enum type that only exists in the loaded mod assembly.
        static Delegate BuildSink(Type logKind, List<string> captured) {
            var kind = System.Linq.Expressions.Expression.Parameter(logKind, "kind");
            var text = System.Linq.Expressions.Expression.Parameter(typeof(string), "text");
            var once = System.Linq.Expressions.Expression.Parameter(typeof(int), "once");
            Action<string, string> add = (k, t) => captured.Add($"{k}: {t}");
            var call = System.Linq.Expressions.Expression.Call(
                System.Linq.Expressions.Expression.Constant(add), add.GetType().GetMethod("Invoke"),
                System.Linq.Expressions.Expression.Call(kind, typeof(object).GetMethod("ToString")), text);
            return System.Linq.Expressions.Expression.Lambda(typeof(Action<,,>).MakeGenericType(logKind, typeof(string), typeof(int)), call, kind, text, once).Compile();
        }

        static string FindModDll() {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir)) {
                var candidate = Path.Combine(dir, "1.6", "Assemblies", "zWYU.dll");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
