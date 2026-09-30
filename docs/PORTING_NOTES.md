# Porting notes: While You're Up 4.0.4 (RimWorld 1.4) → zWYU (RimWorld 1.6)

Software archaeology first, port second. This document records what the original did, what changed in RimWorld 1.6, what was kept,
what was deliberately changed, and what is still uncertain. It is the reference for the later optimization / zPUAH-integration audit.

* Upstream baseline: `rimworld-while-youre-up` @ `1c54e7c` (v4.0.4) and `rimworld-codeoptimist-library` @ `892b8b1`.
* Verified against: RimWorld **1.6.9676.17735** (`Assembly-CSharp.dll` decompiled and read in full for every touched method),
  and the `Krafs.Rimworld.Ref` 1.6.4871 reference assemblies (compiles against both). Harmony 2.4.1.
* Pick Up And Haul source consulted for API shape: the 1.6 sources in the owner's `zPUAH` repository (read-only; nothing depends on it).

---

## 1. What the original mod is

WYU is three behaviors plus a large, optional Pick Up And Haul (PUAH) integration:

1. **Opportunities** — replace vanilla's `TryOpportunisticJob` haulable loop with a richer search (midway storage, ratio limits,
   optional real pathfinding).
2. **Haul before carry** — for construction supplies (`ResourceDeliverJobFor`) and bill ingredients (`DoBill` job), first haul the material to storage
   that is closer to where it is needed.
3. **Settings / UI** — ~25 settings, per-storage-building filters (a cloned `ThingFilter` tree from the CodeOptimist library), Common Sense conflict handling.
4. **PUAH+** (≈40% of the code) — swap the HaulToCell detours for PUAH's multi-item `HaulToInventory` job and patch PUAH/vanilla internals so PUAH obeys the detour limits, unloads in closest order, etc.

Detour state was one mutable `BaseDetour` record per pawn in a `WeakDictionary`, with a "same frame" loop guard.

## 2. Patch inventory: every patch/transpiler in the original, and its 1.6 disposition

| # | Original patch (target · type) | Gameplay semantic | 1.6 status of the target | zWYU |
|---|---|---|---|---|
| 1 | `Pawn_JobTracker.TryOpportunisticJob` · **Transpiler** (insert a return at *"3 instructions before the `listerHaulables` field load"*) | When vanilla's preconditions pass, run WYU's search instead of vanilla's loop | **Signature changed** to `(Job finalizerJob, Job job)`; new early `return finalizerJob`; private `CanPawnTakeOpportunisticJob`; extra checks (age, subhuman, rebelling) | **Replaced** — Prefix + `ThingsPotentiallyNeedingHauling` Prefix + Finalizer (§4) |
| 2 | `WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor` · **Transpiler** (splice after `FindAvailableNearbyResources`, using a closure field `need`, IL local `job`/`foundRes`) | Before returning the delivery job, consider hauling the resource nearer the site | Exists; **restructured** around closures (`need`, `foundRes` captured), gains `forced`; `resourcesAvailable` now private | **Replaced** — Postfix on the *result* (§4) |
| 3 | `WorkGiver_Scanner.HasJobOnThing` · Postfix (clear temp detour) | Discard side effects of "is there a job?" scans | Exists, but **the construction givers override `HasJobOnThing` without calling base**, so this patch would never run for them | **Removed** — no side effects to clear by design (§5); scans are short-circuited instead |
| 4 | `JobDriver_HaulToCell.MakeNewToils` · Postfix + `AddFinishAction(() => …)` | End the detour when the haul job ends | `MakeNewToils` still a protected iterator; **`AddFinishAction` now takes `Action<JobCondition>`** | **Replaced** — `Notify_Starting` Postfix registers the finish action |
| 5 | `JobDriver_HaulToCell.GetReport` · Postfix | "on the way to X" / "closer to X" | Unchanged | Kept |
| 6 | `Pawn_JobTracker.ClearQueuedJobs` · Postfix | Clear detour state | Unchanged | Kept (bookkeeping only) |
| 7 | `Pawn.Destroy` · Postfix | Free per-pawn state | Exists | **`Pawn.DeSpawn`** Prefix instead — also covers leaving the map |
| 8 | `JobUtility.TryStartErrorRecoverJob` · Prefix ("offer support" log) | Diagnose *"started 10 jobs in one tick"* | Signature `(Pawn, string, Exception, JobDriver)` | Kept as an actionable diagnostic |
| 9 | `Dialog_ModSettings` / HugsLib `Dialog_VanillaModSettings` ctor + `DoWindowContents` · Postfix | Sync the *draw* setting with vanilla's debug flag; resolve the Common Sense conflict by *editing its settings* | Exist | **Not ported** — the vanilla `Mod` entry points suffice; sync is a property; Common Sense is read, never written (§6) |
| 10 | `StoreUtility.TryFindBestBetterStoreCellFor` · Prefix (detour-aware storage when inside PUAH) | Route-aware storage for PUAH hauls | Same signature, **body refactored** into `…ForWorker` with new faction/`HaulDestinationEnabled`/`Settings.AllowedToAccept` checks | **PUAH-only → not ported.** The core search re-derives from the 1.6 worker (§3) |
| 11–17 | PUAH: `WorkGiver_HaulToInventory` `HasJobOnThing`/`JobOnThing`/`AllocateThingAtCell`/`TryFindBestBetterStoreCellFor` pre/postfixes; `JobDriver_UnloadYourHauledInventory` `MakeNewToils`, `FirstUnloadableThing`; `JobDriver.GetReport` | PUAH+ integration | PUAH 1.6 changed shape (container `StoreTarget`s, `skipThings`, public statics) | **Deferred** (§7) |
| 18 | `StorageSettings.Priority` getter · Postfix, `ListerHaulables.ThingsPotentiallyNeedingHauling` · Postfix | The "same-priority storage" hack for PUAH | `ThingsPotentiallyNeedingHauling` now returns `ICollection<Thing>` (was `List<Thing>`) | **Deferred** with PUAH (patching a hot property to fake priorities is the wrong tool for a baseline) |
| 19 | Library: ~35 `[HarmonyReversePatch]` clones of `ThingFilter`/`Listing_TreeThingFilter`, a `Listing_TreeThingFilter.DoThingDef` prefix, a `Log.Error` prefix | Reuse the vanilla filter tree UI for *storage buildings* | Very fragile across versions; the public library snapshot doesn't even contain the variant WYU 4.0.4 uses | **Dropped** — a purpose-built list (§8) |
| 20 | Library: `Diagnostics` (patches every method of a mod to trace calls), `Pawn_JobTracker.StartJob` prefix in `DEBUG` builds | Developer tracing | — | **Dropped** (replaced by `Diag`, §9) |

**zWYU patches 10 vanilla methods (in 9 patch classes) and uses 0 IL transpilers** (asserted in CI by `RepositoryInvariantTests`, and against the real assembly by `PatchAudit`).

### The 1.6 patch map

| Vanilla target (1.6) | Type | Purpose | If it cannot be applied / is not reached |
|---|---|---|---|
| `Pawn_JobTracker.TryOpportunisticJob(Job, Job)` | Prefix + Finalizer | Open / always close the search context | `OpportunityHook` feature off (vanilla loop runs) |
| `ListerHaulables.ThingsPotentiallyNeedingHauling()` | Prefix | The anchor: vanilla calls it right after its preconditions; run the search there and give vanilla an empty list | `OpportunityHook` off; startup **self-test** detects a bypassed patch (e.g. JIT inlining) |
| `WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor(Pawn, IConstructible, bool, bool)` | Postfix | Replace vanilla's delivery job by a haul-closer job. Takes only `targetA` (the selected resource) from the result; **current need and the full nearby-stack list are recomputed with vanilla's own APIs** (§4). Startup verifies the pieces this depends on (`SupplyAnchor.Verify`) | `SupplyHook` off; vanilla's job is returned untouched |
| `WorkGiver_ConstructDeliverResourcesToBlueprints/ToFrames.HasJobOnThing` | Prefix + Finalizer | Mark "is-there-a-job" scans so the postfix skips them | part of `SupplyHook` |
| `JobDriver_HaulToCell.Notify_Starting()` | Postfix | PENDING → ACTIVE; register finish action | `LifecycleTracking` off (detours still work; no reports/diagnostics) |
| `JobDriver_HaulToCell.GetReport()` | Postfix | "on the way to" / "closer to" job report | same |
| `Pawn_JobTracker.ClearQueuedJobs(bool)` | Postfix | Note the queued original job is gone | same |
| `Pawn.DeSpawn(DestroyMode)` | Prefix | Drop per-pawn state (death, map change, caravan) | same |
| `JobUtility.TryStartErrorRecoverJob(...)` | Prefix | Log the detour involved in a job error | same |

Exactly this list is asserted by `PatchAudit` against the real game assembly.

## 3. RimWorld 1.6 semantic changes that mattered

| Area | 1.4 (what WYU assumed) | 1.6 | Adaptation |
|---|---|---|---|
| `TryOpportunisticJob` | `(Job job)`, one path | `(Job finalizerJob, Job job)`; returns `finalizerJob` first; `CanPawnTakeOpportunisticJob`; Biotech age check; anomaly/rebelling exclusions | Hook re-anchored (§4); vanilla's own preconditions are reused, not copied (the supply path calls vanilla's private rule via a delegate, with an equivalent fallback) |
| **HaulToCell / HaulToContainer are `allowOpportunisticPrefix=true`** | (not relied upon) | Both job defs opt in | A haul job zWYU issues would itself be offered another detour the moment it starts. Now prevented **by identity** (`DetourTracker.IsDetourJob`), not by timing (original: same-frame guard) |
| Pathfinding | `pathFinder.FindPath(...)`; the original treats a path cost of `0` as *failure* (consistent with the old `NotFound` path having no cost; not re-verifiable without 1.4 binaries) | `FindPath` removed; synchronous `FindPathNow(start, LocalTargetInfo, TraverseParms, tuning, peMode)`; failure is `PawnPath.NotFound` with **negative** cost; a zero-length path is *not found* | `PathCosts.TryGet`: tests `Found`; "start already satisfies the end mode" is decided first with `ReachabilityImmediate` and costs 0 (the original's `== 0` test rejected it); destination always passed as a cell (the original's `IndexOutOfRange` fix). `FindPathNow` also force-completes scheduled async path jobs → see risks |
| Storage search | `TryFindBestBetterStoreCellFor` body copied by WYU | Body split into `…ForWorker`; only groups with `HaulDestinationEnabled` and `parent.Faction == faction`; acceptance via `slotGroup.Settings.AllowedToAccept` | `StorageSearch` re-derived from the 1.6 worker so zWYU never picks storage vanilla itself would refuse |
| Non-cell storage | `TryFindBestBetterStorageFor` may return a container; WYU ignored containers | Same (plus `HaulToContainerJob`) | Unchanged: opportunistic hauls go to **cell storage only** (documented deviation from *vanilla*, not from WYU) |
| `ResourceDeliverJobFor` | linear, `need` and `foundRes` as plain locals | closures around both; `forced` parameter; `FindNearbyNeeders` may return a *remove-floor* job; **the loop now runs over `TotalMaterialCost()` (the WHOLE cost) and computes the current need separately (`num` = `GetSpaceRemainingWithEnroute` for `IHaulEnroute`, else `ThingCountNeeded`)**; the candidate list `resourcesAvailable` is **trimmed before the job is built** | Postfix that recomputes the current need with those same two APIs and re-runs vanilla's own collector for the full candidate list (§4); **`forced` is respected** (the original transpiler ignored it) |
| `HasJobOnThing` | base implementation called `JobOnThing` | Construction givers override it and call `ResourceDeliverJobFor` directly | see patch 3 |
| `JobDriver.AddFinishAction` | `Action` | `Action<JobCondition>` | finish action receives the end condition (used for the diagnostic) |
| `ListerHaulables.ThingsPotentiallyNeedingHauling` | `List<Thing>` | `ICollection<Thing>` (a live `HashSet`) | The search copies it into a reusable buffer; the hook returns an empty array |
| Debug attributes | `Verse.TweakValue`, `DebugAction`, `AllowedGameStates` | moved to `LudeonTK` | `using LudeonTK` |
| `TabDrawer.DrawTabs` | 3 args | extra `float? maxTabWidth` | one extra `null` |
| Ideology/Biotech/Anomaly/Odyssey | — | more pawn kinds (mechs, subhumans, slaves) | Supply detours apply only to player-faction humanlike pawns that vanilla itself would let take an opportunistic job |

Not changed and re-verified in 1.6: `HaulAIUtility.HaulToCellStorageJob`, `PawnCanAutomaticallyHaulFast` (kept as a thin wrapper over `_NewTemp`),
`StoreUtility.CurrentStoragePriorityOf/IsGoodStoreCell`, `MassUtility.WillBeOverEncumberedAfterPickingUp`, `WithinRegions`, `DoBill` job shape
(`targetQueueB` + `countQueue`), `JobDriver_HaulToCell` toils (including `haulOpportunisticDuplicates`).

## 4. Why the transpilers were replaced, and by what

> **What gameplay semantic was each patch implementing?**

**`TryOpportunisticJob` transpiler.** *"After vanilla has decided this pawn may look for an opportunistic haul, run WYU's search **instead of** vanilla's haulable loop and return its result."*
The safest 1.6 hook that preserves exactly that, with no IL edits:

1. Prefix on `TryOpportunisticJob`: the invocation that finds **no active context acquires it** — stores `(pawn, job)` in `OpportunityContext` and *arms* it — and returns an **ownership token** through Harmony's `__state`. A nested (re-entrant) invocation is counted (`Depth`) but never acquires, and gets the *ignored* token.
2. Vanilla calls `ListerHaulables.ThingsPotentiallyNeedingHauling()` **exactly once, immediately after all its preconditions pass** (verified in the 1.6 IL). A Prefix there, while armed, runs the search and hands vanilla an **empty collection** — so vanilla's loop does nothing. Every other caller in the game is untouched (the context is not armed).
3. A **Finalizer** on `TryOpportunisticJob` receives the same token. **Only the invocation that acquired the context (token *owner*) may deliver its result and clear it**; an ignored nested invocation's finalizer touches nothing, and a finalizer whose prefix never ran (token 0) does nothing at all. Finalizers run when an exception unwinds, so the owner's context cannot leak, and the exception is always returned unchanged.
4. While a nested invocation is in flight (`Depth != OwnerDepth`) the lister prefix does not fire, so a nested call keeps vanilla's own loop and cannot consume the owner's armed search. (This state is three integers and two references; it is not a scheduler.)

Why not copy vanilla's preconditions into a Prefix? They are ~15 checks that Ludeon changes between versions (1.6's include an Anomaly-subhuman rule, which cannot have existed when WYU 4.0.4 was written against 1.4, and the whole method was restructured around `finalizerJob`); a copy would go stale and let zWYU haul in situations vanilla forbids. Why not a Postfix alone? Vanilla's loop would already have returned its own job, losing WYU's storage choice and path checks.

Safety nets, all degrading to vanilla: anchor verified at startup by reading vanilla's IL (`OpportunityAnchor.Verify`: signature, and that it still calls the lister method — reading, not patching); a runtime **self-test** proves the lister patch is actually reached on the running runtime (a tiny method can be inlined by a JIT, bypassing any patch — that would otherwise be a *silent* failure); runtime exceptions in the search leave vanilla's collection alone so vanilla's loop runs; repeated faults switch the feature off.

**`ResourceDeliverJobFor` transpiler.** *"Before returning the delivery job, consider hauling the LARGEST nearby stack of the selected resource closer — but only if that stack is larger than what is CURRENTLY needed."* The original spliced in right after `FindAvailableNearbyResources`, so it saw two things:

* `need`, whose `count` is **how much of that material is still needed** (in 1.4 the loop ran over the *remaining* materials; the 1.4 binaries are not available here, this is as established in review and matches the rule's intent, "only if there are extras");
* `resourcesAvailable`, the **full** candidate list vanilla had just collected — the selected stack first, then same-def stacks within 5 cells until a carrying load is reached.

In 1.6 neither survives into the returned job, which is why an earlier version of this port was wrong (§11):

| Input | 1.6 reality | How zWYU obtains it now |
|---|---|---|
| **Current need** | The loop is `foreach need in c.TotalMaterialCost()` — the WHOLE cost, including what is already delivered. The current need is a *separate* value, `num = (forced \|\| c is not IHaulEnroute) ? c.ThingCountNeeded(def) : enroute.GetSpaceRemainingWithEnroute(def, pawn)`. | The postfix computes the same expression for the resource vanilla selected (`job.targetA.Thing.def`); `forced` is already excluded. **Never `TotalMaterialCost()`.** Example: cost 100, delivered 90 ⇒ need 10; a stack of 20 has extras. |
| **Candidate stacks** | `FindAvailableNearbyResources(Thing, Pawn, out int)` fills the private static `resourcesAvailable`; vanilla then `RemoveRange`s and `Remove(foundRes)`s it before building the job. The job's `targetA` + `targetQueueA` is therefore a **subset** — a larger stack that vanilla did not need for the carrying load is not in it. | `SupplyNearbyResources` re-runs vanilla's **own** collector for the selected resource and reads the list it fills (bound once at startup by reflection; the list is cleared at the start of every use, is used nowhere else, and vanilla has finished with it). It is vanilla's calculation, not a copy of it, and it needs no capture state that could go stale. |
| **Largest stack** | — | `SupplyRules.IndexOfLargest` over that full list: first maximum wins (vanilla's `MaxBy`). |
| **Extras** | — | `SupplyRules.ExtrasExist(largest, currentNeed)` = `largest > currentNeed`, **strict** (20 vs need 20 ⇒ no extras). |

Alternatives considered: (a) a Prefix/Postfix pair on the private `FindAvailableNearbyResources` capturing its list — rejected: an extra hook on a method vanilla calls on every `HasJobOnThing` scan, plus a capture that would need its own ownership; (b) copying the collector's radial scan (`GenRadial`, `GenAI.CanUseItemForWork`, `MaxStackSpaceEver`, the `5f` radius) — rejected: a copy silently diverges when vanilla changes; (c) a transpiler — not needed. The startup check (`SupplyAnchor.Verify`) reads vanilla's IL and requires that `ResourceDeliverJobFor` still calls `FindAvailableNearbyResources` and still derives its need from `ThingCountNeeded` / `GetSpaceRemainingWithEnroute`, and that the collector and its list bind; otherwise `SupplyHook` is disabled with a reason. `PatchAudit` re-checks this against the real assembly, including that vanilla still trims the list (the premise for the whole design).
The postfix still declines whenever the returned job is not a `HaulToContainer` delivery (e.g. a remove-floor or install job), when `forced`, inside `HasJobOnThing`, or if the pawn cannot haul the chosen stack.

## 5. Detour state: ownership and lifecycle

The original's per-pawn mutable `BaseDetour` (mostly PUAH bookkeeping) is replaced by `DetourPlan` (immutable: owner pawn, kind, destination, thing, store cell, original job, tick, explanation) and `DetourTracker`:

```
search picks a haul job ──Issue──▶ PENDING ──driver.Notify_Starting──▶ ACTIVE ──job ends (success/fail/cancel)──▶ gone
```

* **Who owns it?** exactly one pawn; stored in weak per-pawn slots (`ConditionalWeakTable`), so nothing can outlive the pawn.
* **What is it?** `plan.ToString()` answers: owner, what is hauled, to where, the real destination, the queued original job, when, and why (distances/ratios).
* **When is it cleared?** ACTIVE: by the haul driver's finish action (any `JobCondition`), on despawn, and self-healing — every read checks `pawn.CurJob.loadID == plan.JobLoadId`. PENDING: replaced by the next decision or garbage-collected; a job that never starts (a *"has a job?"* scan, a rejected pre-toil reservation) leaves nothing to clean.
* **Same-tick loop guard** — kept, but (a) in game ticks, not render frames, and (b) armed **only by an issued detour**. The original also armed it when a search found nothing, which suppressed the opportunity check for the very job vanilla was about to start (notably construction delivery) whenever the supply option was on.
* **Nothing is saved.** After loading, a running haul is simply an ordinary haul.
* Static buffers (haulable list, store-cell cache) are cleared in `finally` after every search.

## 6. Behavior kept, changed, or deviating (explicit list)

**Kept unchanged** (pinned by unit tests where pure): the geometric rules (`TripRules`), the range-expansion heuristic and its `[TweakValue]`, midway storage choice, equal-priority storage for haul-before-carry,
`MassUtility` "no extras" hack, "extras only" rules for supplies/ingredients (for supplies: largest stack of the FULL nearby list, strictly larger than the CURRENT need — obtained differently from 1.4, same semantics, §4), skip when bleeding (ordinary opportunities; ingredients/supplies still allowed) / preparing a caravan, all setting names and defaults, the *Vanilla / Default / Pathfinding* modes,
the debug drawing colors, job-report wording, stockpile/building filters and their per-mod defaults.

**Deviations from the original (each deliberate):**

1. **PUAH+ not ported** (§7). `UsePickUpAndHaulPlus`, the PUAH tab, unload ordering and the priority hack are absent. PUAH coexists.
2. **Common Sense**: the original un-ticked *Common Sense's* option in Common Sense's own settings file. zWYU never edits another mod's settings; while Common Sense's *"haul ingredients for a bill"* is on, zWYU simply skips its own bill detours. Read-only, fail-safe.
3. **Loop guard**: game ticks and armed-on-issue only (§5).
4. **Supply detours respect `forced`** (the original also produced detour jobs for right-click "prioritize delivering" menu evaluation) and **never run inside `HasJobOnThing`**.
5. **Supply detours are limited to pawns vanilla would let take an opportunistic job** (player faction, humanlike, capable, not drafted/downed/…). The original only checked work tags.
6. **Job identity blocks nested detours** (HaulToCell/HaulToContainer are prefix-capable in 1.6).
7. **Supply detours check `PawnCanAutomaticallyHaulFast` on the stack they will haul** (the bill path always did; the original supply path did not), so an unreservable/unreachable neighbour stack declines the detour instead of failing the haul job's pre-toil reservations.
8. **Bill target**: the first valid ingredient; the original's `FirstOrDefault() ??` yielded an invalid target for an empty queue.
9. **Path cost `Found` semantics**; a pawn already touching the target costs 0 instead of rejecting (§3).
10. **One termination guard** (new): at most `MaxRangeExpansions = 40` range-expansion passes per search (the original ended by float overflow). It is **not** a candidate budget: a unit test proves it cannot cut off a legitimate candidate on a 1000-cell map at the default, minimum (1.1) and maximum (3.0) tweak factors, and `SearchLoopTests` prove that candidates beyond the 12th, 40th, even 2000th are still examined. There is **no limit on how many candidates are examined or pathfound**, in any mode (an earlier version of this port capped *Pathfind during search* at 12 pathfound candidates; that changed behavior and was removed, §11).
11. **Storage-building filters** use a defName allow-list, persisted by name, instead of a cloned `ThingFilter` tree. Behavior (per-building, per-mod grouping, auto vs manual, per-mod defaults) is the same; the on-disk format is new. Config files from the original are not read (new package id ⇒ new file).
12. New setting: **Debug logging** (Off/Summary/Verbose). Removed: the PUAH switches.
13. `Testing.cs` → *Make colony (zWYU)* (renamed, same content).

## 7. Pick Up And Haul

**Decision: coexist, don't integrate — in this PR.** Reasons:

* PUAH+ *is* multi-item opportunistic hauling: the original substitutes PUAH's `HaulToInventory` job for the HaulToCell detour and constrains PUAH's own multi-item planning. That is explicitly out of scope for the baseline.
* PUAH's 1.6 internals moved since WYU 4.0.4 (seen in the 1.6 source): destinations are `StoreTarget`s that may be *containers*; `skipThings` alongside `skipCells`; `AllocateThingAtCell` public static; a per-map haulables cache. WYU's reflection table (`AllocateThingAtStoreTarget`, `skipCells` typed `HashSet<IntVec3>`, `takenToInventory` field) would no longer bind, and patching a *hot property* (`StorageSettings.Priority`) plus `StoreUtility.TryFindBestBetterStoreCellFor` globally to steer another mod is exactly the sort of risk a baseline should not take.
* PUAH must stay optional and the base mod must never be at risk from it.

What ships (`Compat/PickUpAndHaulCompat`, feature `PickUpAndHaulGuard`): detection (once, logged), and a guard — **no detour starts for a pawn whose PUAH "hauled to inventory, awaiting unload" set is non-empty** (the original's *"because we may load a game with an incomplete haul"*). It reads `CompHauledToInventory.GetHashSet()` (or the `takenToInventory` field on older PUAH) through a compiled delegate; any reflection failure switches the guard off and logs why.
zWYU's own hauls carry in hands and never touch PUAH's inventory bookkeeping, so there is no shared ownership and no inventory leftovers can be caused by zWYU.
PUAH patches `JobDriver_HaulToCell.MakeNewToils`; zWYU patches `Notify_Starting`/`GetReport` — no overlapping patch targets.

## 8. Known risks (for the runtime tester)

1. **Job lifecycle / reservations.** The haul job is started by vanilla's own `StartJob` recursion, which queues the original job first; zWYU never starts or stops jobs itself. If the haul's pre-toil reservation fails, vanilla logs its usual warning and error-recovers the pawn (as it did for the original mod). The same-tick guard bounds any retry to once per tick per pawn.
2. **Hook engagement on Mono.** Verified on .NET 8 with real Harmony against the real assembly, including the self-test. Not yet verified on Unity's Mono — the in-game counters/self-test exist precisely to surface it.
3. **Bills.** An ingredient stack hauled into a same-def stack in storage can merge and be destroyed, failing the queued `DoBill` job; vanilla then rebuilds the bill job. (Same as the original.)
4. **Construction.** The supply hook depends on vanilla's delivery job being a `HaulToContainer` with `targetA` = the selected resource, and on two private vanilla members bound by reflection (`FindAvailableNearbyResources`, `resourcesAvailable`); startup verifies them and disables the feature with a reason if they moved. Re-running the collector costs one extra radial scan (~5-cell radius) per real supply decision that passed every cheap check; `HasJobOnThing` scans, forced orders and ineligible pawns never reach it. **Partial constructions and enroute deliveries (`IHaulEnroute`) are the cases to watch at runtime** — they are exactly where the current need differs from the total cost.
5. **Synchronous pathfinding, uncapped.** `FindPathNow` force-completes any scheduled async path jobs. *Default* mode pathfinds at most one candidate per search (up to 4 queries; a failure ends the search). *Pathfind during search* pathfinds **every** candidate that survives the cheap checks, **without any candidate limit** (baseline fidelity): in a large colony with many haulables that cannot be reached the cost is unbounded per search. The per-search Summary line reports `path checks N (Q synchronous path queries, T ms)` so the cost is measurable; bounding it is future optimization work, deliberately not done here.
6. **Cell storage only** (see §3): an item vanilla would send to a higher-priority *container* may be hauled to a lower-priority cell store first. Same as the original.
7. **Other mods** patching `TryOpportunisticJob`, `ResourceDeliverJobFor` or `HaulToCell` drivers are untested; the patches are prefix/postfix/finalizer (no transpiler), so ordering conflicts should surface as odd job reports rather than crashes.
8. **Common Sense field name** is bound by reflection to `CommonSense.Settings.hauling_over_bills` as in the original; if it moved, the guard is off and both mods may haul ingredients.

## 9. Diagnostics

`Diag` (logging) at **Off / Summary / Verbose**. Every line is `[zWYU][Subsystem] …` (`Startup`, `Opportunity`, `BeforeCarry`, `Lifecycle`, `Compat`). Summary lines: `searching N haulables` → `SELECTED <plan>` → `STARTED <plan>` → `detour completed|ABORTED (condition); vanilla resumes…`, plus one line per search with counts, the reject-event totals, expansions, path checks **with the number of synchronous path queries and the time they took**, and total milliseconds. Errors and compatibility failures are always logged with the feature and patch name; failed features also appear in red in the settings window. Vanilla's *"10 jobs in one tick"* recovery is annotated with the zWYU plan if one was involved. Hook counters (`seen / engaged / found`) are shown in the settings window whenever logging is on.

## 10. Future optimization candidates (observations only — nothing here was optimized)

* Every opportunistic check **copies the whole map's haulable set** (`ThingsPotentiallyNeedingHauling`) into a list; then evaluates candidates in hash order.
* **Range-expansion passes re-run the full `CanHaul`** (reservation, forbidden, `PawnCanAutomaticallyHaulFast` with reachability) for every remaining candidate on every pass.
* **Storage scanning** is O(cells of every allowed slot group) per candidate (`StorageSearch`), repeated for haul-before-carry and per pass.
* **Real pathfinding**: up to 4 `FindPathNow` per candidate; synchronous; in *Pathfind during search* mode unbounded in the number of candidates (the removed candidate cap was a behavior change, so any future bound must be evaluated as one).
* **Reachability** (`CanReach`) is checked repeatedly per candidate (haul eligibility, `IsGoodStoreCell` per cell).
* **Supply scans**: vanilla evaluates `ResourceDeliverJobFor` per blueprint per pawn per think; for a real (non-forced, non-`HasJobOnThing`) decision the postfix adds vanilla's collector a second time plus a storage search.
* Per-search temporary collections are already reused; the remaining per-decision allocations are the `DetourPlan`, the haul `Job`, and (only when logging) strings.
* First-candidate-wins selection means the result depends on hash-set order; a "best of N" comparison would be a behavior change for the later project to study.

## 11. Review corrections after the first review of PR #1

A review of the first version found three semantic problems; all were fixed in place, the rest of the architecture was approved and left alone.

1. **Construction supply used the wrong inputs** (blocker). *Cause:* the postfix reconstructed the original's decision from the returned delivery job — the "extras" threshold was `TotalMaterialCost()`'s count for the material (the whole cost, so a wall with 90 of 100 steel already delivered needed a stack of more than **100** instead of more than **10**), and the largest stack was picked from `targetA` + `targetQueueA` (a trimmed subset of the list the original inspected). The claim that the returned job "already contains everything the original used" was wrong and has been removed. *Fix:* §4 — current need from vanilla's `ThingCountNeeded` / `GetSpaceRemainingWithEnroute`, full candidate list from vanilla's own collector, strict `>`; `SupplyRules` (pure, unit-tested: partial construction 100/90/need 10/stack 20 ⇒ extras; need 20 vs stack 20 ⇒ none; first-maximum over the full list, contrasted with the trimmed list) and a `PatchAudit` check that drives the production code through the real vanilla collector binding and asserts the 75-stack of `[30, 75, 20]` is chosen.
2. **`OpportunityContext` was not ownership-safe under re-entrancy.** *Cause:* a nested `TryOpportunisticJob` was "ignored" in its prefix, but its finalizer still saw `Active == true` and could hand the outer result to the inner call and reset the outer context. *Fix:* §4 — per-invocation ownership token via `__state`; only the owner delivers/clears; a nested call cannot consume the owner's armed search. *Regression tests* (`PatchAudit` §5): the direct prefix/finalizer sequence (outer acquires → nested prefix does not → nested finalizer leaves Active/Armed/Pawn/Job/Result/Depth intact and receives nothing → outer finalizer delivers and resets; with exceptions; with a prefix that never ran; with the mod disabled) **and** a genuinely nested `TryOpportunisticJob` executed through the patched game method with the same assertions. Confirmed non-vacuous: reintroducing the old behavior makes three of those checks fail.
3. **A semantic candidate cap in *Pathfind during search*.** *Cause:* `MaxPathfindingCandidates = 12` ended the whole search after twelve pathfound candidates, so a valid thirteenth was never considered. *Fix:* the cap, `RejectReason.PathBudgetExhausted` and its `FullStop` were removed, and the loop's control flow was extracted into a pure `SearchLoop` (+ `PathCheckDispatch`) so that "candidate #13+ stays eligible" is a unit test (`SearchLoopTests`: winners at #12, #13, #40, #500, #2001; nothing passes ⇒ all 60 examined). `MaxRangeExpansions` stays as a termination guard only (§6.10). A repository test fails if a candidate budget symbol reappears. Pathfinding cost stays visible in the Summary line instead.
