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
            Console.WriteLine("3b. construction-supply inputs (the premises behind the supply hook)");
            var supplyAnchor = T("zWYU.SupplyAnchor");
            var supplyProblem = (string)supplyAnchor.GetMethod("Verify", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            Check(supplyProblem == null, "SupplyAnchor.Verify(): " + (supplyProblem ?? "vanilla derives the current need from ThingCountNeeded / GetSpaceRemainingWithEnroute, calls FindAvailableNearbyResources, and the collector + its list bind"));
            {
                var deliver = asmCsharp.GetType("RimWorld.WorkGiver_ConstructDeliverResources", true)
                    .GetMethod("ResourceDeliverJobFor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var instructions = PatchProcessor.GetOriginalInstructions(deliver);
                bool CallsListMethod(string name) => instructions.Any(ci => ci.operand is MethodInfo mi && mi.Name == name && mi.DeclaringType != null
                    && mi.DeclaringType.IsGenericType && mi.DeclaringType.GetGenericTypeDefinition() == typeof(List<>));
                // This is WHY the delivery job cannot be used to reconstruct what the original inspected: vanilla trims its candidate list first.
                Check(CallsListMethod("RemoveRange") && CallsListMethod("Remove"),
                    "vanilla's ResourceDeliverJobFor still trims the candidate list (RemoveRange/Remove) before building the job, so targetQueueA is only a subset");
                var needProbe = asmCsharp.GetType("RimWorld.IConstructible", true).GetMethod("ThingCountNeeded");
                Check(instructions.Any(ci => ci.operand is MethodInfo mi && mi == needProbe),
                    "vanilla's `num` (current need) comes from IConstructible.ThingCountNeeded, not from TotalMaterialCost's count");
            }

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
            try {
                RunDynamicChecks(mod, asmCsharp, harmony, harmonyId, T);
            } catch (Exception e) {
                // A broken invariant can leave later steps meaningless; that is a failure of the audit, not a crash of the tool.
                Check(false, "dynamic checks aborted by an exception: " + (e is TargetInvocationException tie ? tie.InnerException : e));
            }

            harmony.UnpatchAll(harmonyId);
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "PATCH AUDIT PASSED" : $"PATCH AUDIT FAILED ({failures} failed check(s))");
            return failures == 0 ? 0 : 1;
        }


        // ---- helpers for the audit-owned fake vanilla behavior ------------------------------------------------------------------
        static Func<bool> nestedHook; // invoked from the audit's prefix on CanPawnTakeOpportunisticJob
        static object fakeFoundRes, fakeNearbyA, fakeNearbyB;
        static FieldInfo fakeListField;

        public static bool NestingPrefix() => nestedHook == null || nestedHook();

        public static bool FakeCollect(out int resTotalAvailable) {
            var list = (System.Collections.IList)fakeListField.GetValue(null);
            list.Clear();
            list.Add(fakeFoundRes); // vanilla order: the chosen resource first
            list.Add(fakeNearbyA);
            list.Add(fakeNearbyB);
            resTotalAvailable = 125;
            return false; // skip vanilla's collector: this audit only proves zWYU reads the FULL list through it
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
            void ResetCtx() { ctx.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static).Invoke(null, null); Set("Depth", 0); }

            // enable the mod with default settings
            modType.GetField("Settings", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, Activator.CreateInstance(settingsType));

            var pawnType   = game.GetType("Verse.Pawn", true);
            var jobType    = game.GetType("Verse.AI.Job", true);
            var thingType  = game.GetType("Verse.Thing", true);
            var listerType = game.GetType("RimWorld.ListerHaulables", true);
            var trackerType = game.GetType("Verse.AI.Pawn_JobTracker", true);

            object Uninit(Type t) => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
            object MakeUnspawnedPawn() {
                var pawn = Uninit(pawnType);
                // Thing.mapIndexOrState < 0 means "not spawned", which is what makes OpportunityEngine.Run return immediately.
                var f = thingType.GetField("mapIndexOrState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f.SetValue(pawn, Convert.ChangeType(-1, f.FieldType));
                return pawn;
            }

            var patchType = T("zWYU.TryOpportunisticJob_Patch");
            var enter = patchType.GetMethod("Enter", BindingFlags.NonPublic | BindingFlags.Static);
            var exit  = patchType.GetMethod("Exit", BindingFlags.NonPublic | BindingFlags.Static);
            var pawnObj = MakeUnspawnedPawn();
            var jobObj  = Uninit(jobType);

            // prefix/finalizer as Harmony calls them: Enter(Pawn ___pawn, Job job, out int __state); Exit(Exception, ref Job __result, int __state)
            int Enter(object pawn, object job) {
                var a = new object[] { pawn, job, null };
                enter.Invoke(null, a);
                return (int)a[2];
            }
            (object ret, object result) Exit(Exception ex, object currentResult, int token) {
                var a = new object[] { ex, currentResult, token };
                var ret = exit.Invoke(null, a);
                return (ret, a[1]);
            }

            // (a) ThingsPotentiallyNeedingHauling: untouched for everybody else, intercepted exactly once inside an armed context
            var lister = Activator.CreateInstance(listerType, new object[] { null });
            var listerMethod = listerType.GetMethod("ThingsPotentiallyNeedingHauling");
            var realSet = listerType.GetField("haulables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lister);

            ResetCtx();
            var untouched = listerMethod.Invoke(lister, null);
            Check(ReferenceEquals(untouched, realSet), "ThingsPotentiallyNeedingHauling outside a search returns vanilla's own collection");

            var engagedBefore = (int)Get("Engaged");
            var token0 = Enter(pawnObj, jobObj);
            Check(token0 == 2 && (bool)Get("Active") && (bool)Get("Armed") && (int)Get("Depth") == 1 && (int)Get("OwnerDepth") == 1, "Prefix of the first invocation acquires the context (token Owner, Depth 1)");
            var intercepted = (System.Collections.ICollection)listerMethod.Invoke(lister, null);
            Check(!ReferenceEquals(intercepted, realSet) && intercepted.Count == 0, "inside an armed search vanilla's loop is handed an empty collection");
            Check(!(bool)Get("Armed") && (int)Get("Engaged") == engagedBefore + 1, "the search fires exactly once (Armed cleared, Engaged counter +1)");
            Check(Get("Result") == null, "a search that finds nothing leaves Result null");
            var secondCall = listerMethod.Invoke(lister, null);
            Check(ReferenceEquals(secondCall, realSet), "a second call in the same TryOpportunisticJob (or our own search) sees vanilla's collection");
            Exit(null, null, token0);
            Check(!(bool)Get("Active") && (int)Get("Depth") == 0, "owner's finalizer clears the context and the depth");

            // (b) the TryOpportunisticJob finalizer contract, called directly
            var fakeFound = Uninit(jobType);

            var t1 = Enter(pawnObj, jobObj); Set("Result", fakeFound);
            var (ret1, res1) = Exit(null, null, t1);
            Check(ret1 == null && ReferenceEquals(res1, fakeFound), "finalizer: vanilla returned null + we found a haul -> our haul is returned");
            Check(!(bool)Get("Active") && Get("Pawn") == null && Get("Job") == null && Get("Result") == null && (int)Get("Depth") == 0, "finalizer: context cleared after delivering");

            var t2 = Enter(pawnObj, jobObj); Set("Result", fakeFound);
            var vanillaJob = Uninit(jobType);
            var (_, res2) = Exit(null, vanillaJob, t2);
            Check(ReferenceEquals(res2, vanillaJob), "finalizer: a job another mod/vanilla already returned is never overwritten");
            Check(!(bool)Get("Active"), "finalizer: context cleared even when not delivering");

            var t3 = Enter(pawnObj, jobObj); Set("Result", fakeFound);
            var boom = new InvalidOperationException("boom");
            var (ret3, res3) = Exit(boom, null, t3);
            Check(ReferenceEquals(ret3, boom) && res3 == null, "finalizer: an exception is passed through unchanged and no haul is delivered");
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
            Check(!(bool)Get("Active") && !(bool)Get("Armed") && Get("Pawn") == null && Get("Job") == null && Get("Result") == null && (int)Get("Depth") == 0,
                "real patched TryOpportunisticJob: context fully cleared after the exception unwound");

            // (d) disabled mod: the prefix must not even acquire the context
            var settings = modType.GetField("Settings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            settingsType.GetField("Enabled").SetValue(settings, false);
            enteredBefore = (int)Get("Entered");
            var tDisabled = Enter(pawnObj, jobObj);
            Check(tDisabled == 1 && !(bool)Get("Active") && (int)Get("Entered") == enteredBefore && (int)Get("Depth") == 1, "with the mod disabled in settings the invocation is counted but never acquires the context");
            Exit(null, null, tDisabled);
            Check((int)Get("Depth") == 0, "...and its finalizer restores the depth");
            settingsType.GetField("Enabled").SetValue(settings, true);

            // (e) REGRESSION (review finding 2): nested / re-entrant TryOpportunisticJob ownership, direct prefix/finalizer sequence
            Console.WriteLine("   nested / re-entrant ownership");
            ResetCtx();
            var outerPawn = pawnObj; var outerJob = jobObj;
            var innerPawn = MakeUnspawnedPawn(); var innerJob = Uninit(jobType);
            var outerToken = Enter(outerPawn, outerJob);
            Set("Result", fakeFound); // pretend the owner's search already found a haul
            Check(outerToken == 2 && (bool)Get("Active") && (bool)Get("Armed"), "outer invocation acquired the context (owner)");

            var engagedBeforeNested = (int)Get("Engaged");
            var innerToken = Enter(innerPawn, innerJob);
            Check(innerToken == 1, "nested prefix does NOT acquire ownership (token Ignored)");
            Check((bool)Get("Active") && (bool)Get("Armed") && ReferenceEquals(Get("Pawn"), outerPawn) && ReferenceEquals(Get("Job"), outerJob) && ReferenceEquals(Get("Result"), fakeFound)
                  && (int)Get("Depth") == 2 && (int)Get("OwnerDepth") == 1, "after the nested prefix the outer context is untouched (Active, Armed, Pawn, Job, Result; Depth 2, OwnerDepth 1)");

            var nestedListerCall = listerMethod.Invoke(lister, null);
            Check(ReferenceEquals(nestedListerCall, realSet) && (bool)Get("Armed") && (int)Get("Engaged") == engagedBeforeNested,
                "a nested call reaching ThingsPotentiallyNeedingHauling keeps vanilla's collection and does not consume the owner's armed search");

            var (nestedRet, nestedResult) = Exit(null, null, innerToken);
            Check(nestedRet == null && nestedResult == null, "nested finalizer delivers nothing: the outer result is NOT handed to the inner invocation");
            Check((bool)Get("Active") && (bool)Get("Armed") && ReferenceEquals(Get("Pawn"), outerPawn) && ReferenceEquals(Get("Job"), outerJob) && ReferenceEquals(Get("Result"), fakeFound)
                  && (int)Get("Depth") == 1 && (int)Get("OwnerDepth") == 1, "after the nested finalizer the outer context is STILL intact (Active, Armed, Pawn, Job, Result unchanged; Depth back to 1)");

            var innerBoom = new InvalidOperationException("inner");
            var innerToken2 = Enter(innerPawn, innerJob);
            var (nestedRet2, nestedResult2) = Exit(innerBoom, null, innerToken2);
            Check(ReferenceEquals(nestedRet2, innerBoom) && nestedResult2 == null && (bool)Get("Active") && ReferenceEquals(Get("Result"), fakeFound) && (int)Get("Depth") == 1,
                "a nested invocation that throws: its exception is passed through and the outer context is still intact");

            // the owner can still use its context: its own lister call fires, and its finalizer delivers and clears
            var ownerListerCall = listerMethod.Invoke(lister, null) as System.Collections.ICollection;
            Check(ownerListerCall != null && !ReferenceEquals(ownerListerCall, realSet) && ownerListerCall.Count == 0 && !(bool)Get("Armed"), "once the nested call is gone the owner's lister call fires normally");
            Set("Result", fakeFound);
            var (outerRet, outerResult) = Exit(null, null, outerToken);
            Check(outerRet == null && ReferenceEquals(outerResult, fakeFound), "outer finalizer delivers the outer result");
            Check(!(bool)Get("Active") && !(bool)Get("Armed") && Get("Pawn") == null && Get("Job") == null && Get("Result") == null && (int)Get("Depth") == 0 && (int)Get("OwnerDepth") == 0,
                "outer finalizer fully resets the context");

            // owner + exception, and a finalizer whose prefix never ran
            var oToken = Enter(outerPawn, outerJob); Set("Result", fakeFound);
            var notRun = Exit(null, null, 0);
            Check(notRun.ret == null && notRun.result == null && (bool)Get("Active") && ReferenceEquals(Get("Result"), fakeFound) && (int)Get("Depth") == 1,
                "a finalizer whose prefix never ran (token 0) touches nothing");
            var oBoom = new InvalidOperationException("outer");
            var (oRet, oRes) = Exit(oBoom, null, oToken);
            Check(ReferenceEquals(oRet, oBoom) && oRes == null && !(bool)Get("Active") && (int)Get("Depth") == 0, "outer finalizer with an exception: unchanged exception, nothing delivered, context cleared");

            // (f) the same through the REAL patched method: a genuinely nested TryOpportunisticJob (audit-owned hook inside vanilla's body)
            Console.WriteLine("   real nested TryOpportunisticJob through the patched game method");
            ResetCtx();
            var canTake = AccessTools.Method(trackerType, "CanPawnTakeOpportunisticJob");
            var innerTracker = Uninit(trackerType);
            trackerType.GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(innerTracker, innerPawn);
            var seenInside = new List<string>();
            var ranNested = false;
            nestedHook = () => {
                if (ranNested) return true;
                ranNested = true;
                var depthBefore = (int)Get("Depth");
                var ownerPawn = Get("Pawn");
                try { trackerType.GetMethod("TryOpportunisticJob").Invoke(innerTracker, new object[] { null, innerJob }); }
                catch (TargetInvocationException) { /* vanilla throws on the uninitialized pawn: expected */ }
                seenInside.Add($"depth {depthBefore}->{(int)Get("Depth")}, active={(bool)Get("Active")}, armed={(bool)Get("Armed")}, sameOwnerPawn={ReferenceEquals(Get("Pawn"), ownerPawn)}, sameOwnerJob={ReferenceEquals(Get("Job"), outerJob)}");
                return true;
            };
            harmony.Patch(canTake, prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(NestingPrefix))));
            var outerTracker = Uninit(trackerType);
            trackerType.GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outerTracker, outerPawn);
            Exception outerThrown = null;
            try { trackerType.GetMethod("TryOpportunisticJob").Invoke(outerTracker, new object[] { null, outerJob }); }
            catch (TargetInvocationException e) { outerThrown = e.InnerException; }
            harmony.Unpatch(canTake, HarmonyPatchType.Prefix, harmony.Id);
            nestedHook = null;
            Check(ranNested && seenInside.Count == 1, "the nested TryOpportunisticJob really ran inside the outer one");
            Check(seenInside.Count == 1 && seenInside[0] == "depth 1->1, active=True, armed=True, sameOwnerPawn=True, sameOwnerJob=True",
                "after the real nested call returned, the outer context was intact and depth was restored [" + string.Join("; ", seenInside) + "]");
            Check(outerThrown is NullReferenceException, "the outer call's own vanilla exception still propagates [" + outerThrown?.GetType().Name + "]");
            Check(!(bool)Get("Active") && !(bool)Get("Armed") && Get("Pawn") == null && Get("Job") == null && Get("Result") == null && (int)Get("Depth") == 0 && (int)Get("OwnerDepth") == 0,
                "after the outer call unwound, the context is fully cleared");

            // (g) construction supply: the candidate list is read from vanilla's own collector (FULL list), not from the trimmed job queue
            Console.WriteLine("   construction supply plumbing");
            var giverType = game.GetType("RimWorld.WorkGiver_ConstructDeliverResources", true);
            var collectorMethod = AccessTools.Method(giverType, "FindAvailableNearbyResources");
            fakeListField = AccessTools.Field(giverType, "resourcesAvailable");
            object MakeStack(int count) {
                var t = Uninit(thingType);
                thingType.GetField("stackCount").SetValue(t, count);
                return t;
            }
            fakeFoundRes = MakeStack(30); fakeNearbyA = MakeStack(75); fakeNearbyB = MakeStack(20);
            harmony.Patch(collectorMethod, prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(FakeCollect))));
            var supplyNearby = T("zWYU.SupplyNearbyResources");
            var bindProblem = (string)supplyNearby.GetMethod("Bind", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            Check(bindProblem == null, "SupplyNearbyResources binds vanilla's private collector and list");
            var giver = Uninit(game.GetType("RimWorld.WorkGiver_ConstructDeliverResourcesToFrames", true));
            var largestArgs = new object[] { giver, pawnObj, fakeFoundRes, 0 };
            var largest = supplyNearby.GetMethod("LargestNearbyStack", BindingFlags.Public | BindingFlags.Static).Invoke(null, largestArgs);
            Check(ReferenceEquals(largest, fakeNearbyA) && (int)largestArgs[3] == 3,
                "the largest stack is chosen from the FULL vanilla candidate list [30, 75, 20] -> the 75-stack (3 candidates)");
            // what the rejected reconstruction from the returned job would have seen: targetA (30) + a trimmed targetQueueA ([20])
            var trimmedBest = new[] { 30, 20 }.Max();
            Check(trimmedBest == 30 && (int)thingType.GetField("stackCount").GetValue(largest) == 75, "(contrast) the trimmed job's targetA + targetQueueA would have offered only the 30-stack");
            harmony.Unpatch(collectorMethod, HarmonyPatchType.Prefix, harmony.Id);
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
