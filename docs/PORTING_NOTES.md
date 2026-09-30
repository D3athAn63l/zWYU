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
| 1 | `Pawn_JobTracker.TryOpportunisticJob` · **Transpiler** (insert a return at *"3 instructions before the `listerHaulables` field load"*) | When vanilla's preconditions pass, run WYU's search instead of vanilla's loop | **Same shape as 1.4**: `(Job finalizerJob, Job job)`, the early `return finalizerJob` and the private `CanPawnTakeOpportunisticJob` are already in the 1.4 binaries. **1.5** added `Downed` and *Anomaly* `IsMutant` to that private rule and made the loop container-aware (`TryFindBestBetterStorageFor` + `HaulToContainerJob`); **1.6** renamed `IsMutant` → `IsSubhuman`, iterates an `ICollection`, and uses `PawnCanAutomaticallyHaulFast_NewTemp` and `CurrentStoragePriorityOf(item, job.playerForced)` (§3) | **Replaced** — Prefix + `ThingsPotentiallyNeedingHauling` Prefix + Finalizer (§4) |
| 2 | `WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor` · **Transpiler** (splice after `FindAvailableNearbyResources`, using a closure field `need`, IL local `job`/`foundRes`) | Before returning the delivery job, consider hauling the resource nearer the site | Exists; **restructured in 1.5** (identical shape in 1.6): closures (`need`, `foundRes` captured), `forced`, a loop over `TotalMaterialCost()` with the current need computed separately; `resourcesAvailable` now private | **Replaced** — Postfix on the *result* (§4) |
| 3 | `WorkGiver_Scanner.HasJobOnThing` · Postfix (clear temp detour) | Discard side effects of "is there a job?" scans | Exists, but in 1.6 **the construction givers override `HasJobOnThing` (they do not in the 1.4 or 1.5 binaries, where the base `JobOnThing(...) != null` ran `ResourceDeliverJobFor`)**, so the original patch — still effective through the shipped 1.5 build — would never run for them | **Removed** — no side effects to clear by design (§5); scans are short-circuited instead |
| 4 | `JobDriver_HaulToCell.MakeNewToils` · Postfix + `AddFinishAction(() => …)` | End the detour when the haul job ends | `MakeNewToils` still a protected iterator; **`AddFinishAction` takes `Action<JobCondition>`** (since 1.5) | **Replaced** — `Notify_Starting` Postfix registers the finish action |
| 5 | `JobDriver_HaulToCell.GetReport` · Postfix | "on the way to X" / "closer to X" | Unchanged | Kept |
| 6 | `Pawn_JobTracker.ClearQueuedJobs` · Postfix | Clear detour state | Unchanged | Kept (bookkeeping only) |
| 7 | `Pawn.Destroy` · Postfix | Free per-pawn state | Exists | **`Pawn.DeSpawn`** Prefix instead — also covers leaving the map |
| 8 | `JobUtility.TryStartErrorRecoverJob` · Prefix ("offer support" log) | Diagnose *"started 10 jobs in one tick"* | Signature `(Pawn, string, Exception, JobDriver)` | Kept as an actionable diagnostic |
| 9 | `Dialog_ModSettings` / HugsLib `Dialog_VanillaModSettings` ctor + `DoWindowContents` · Postfix | Sync the *draw* setting with vanilla's debug flag; resolve the Common Sense conflict by *editing its settings* | Exist | **Not ported** — the vanilla `Mod` entry points suffice; sync is a property; Common Sense is read, never written (§6) |
| 10 | `StoreUtility.TryFindBestBetterStoreCellFor` · Prefix (detour-aware storage when inside PUAH) | Route-aware storage for PUAH hauls | Same signature; the `…ForWorker` split already exists in 1.4 (`parent.Accepts`); 1.5 uses `Settings.AllowedToAccept`; **1.6** adds the faction / `HaulDestinationEnabled` gate on each slot group | **PUAH-only → not ported.** The core search re-derives from the 1.6 worker (§3) |
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

Each row says which version introduced the change; all of it is checked against the 1.4.8418 and 1.5.9102 game assemblies and the 1.6 assembly (§12). **Much of what looks like "1.6 porting" happened at 1.5**, which the original's last shipped build (4.0.6) had already been adapted to.

| Area | 1.4 (what WYU 4.0.4 assumed) | 1.5 → 1.6 | Adaptation |
|---|---|---|---|
| `TryOpportunisticJob` | `(Job finalizerJob, Job job)`, `finalizerJob` returned first, private `CanPawnTakeOpportunisticJob` (drafted / mental state / burning / rebelling), Biotech age check — **already identical in the 1.4 binaries**; the loop is `for` over a `List<Thing>` and cell-storage-only (`TryFindBestBetterStoreCellFor` → `HaulToCellStorageJob`) | **1.5:** `CanPawnTakeOpportunisticJob` also rejects `Downed` and Anomaly `IsMutant`; storage via `TryFindBestBetterStorageFor` (cell **or** container, `HaulToContainerJob` for containers). **1.6:** `IsMutant` → `IsSubhuman`; `foreach` over an `ICollection<Thing>`; `PawnCanAutomaticallyHaulFast_NewTemp`; `CurrentStoragePriorityOf(item, job.playerForced)` | Hook anchor unchanged in spirit, re-derived from 1.6's IL (§4); vanilla's own preconditions are reused, not copied (the supply path calls vanilla's private rule via a delegate, with an equivalent fallback) |
| **HaulToCell / HaulToContainer are `allowOpportunisticPrefix=true`** | **Already true in 1.4** (verified in the 1.4 Core `Jobs_Work.xml`; so are `DoBill`, `Goto`, `GotoWander`) — not a 1.6 change | Same in 1.5 (Core XML) and 1.6 | A haul job the mod issues is itself offered another detour the moment it starts. That is why the original needed its same-frame guard (`CatchLoop_Job`). zWYU prevents it **by identity** (`DetourTracker.IsDetourJob`), not by timing |
| Pathfinding | `pathFinder.FindPath(...)`. **Verified in the 1.4 binaries:** failure is `PawnPath.NotFound` with cost **−1** (`Found => totalCostInt >= 0f`), and a path whose start already satisfies the end mode is a *found* path of cost **0**. The original tested `TotalCost == 0` → **its check rejected the already-there path, and did not reject `NotFound`** (a −1 fell through into the ratio arithmetic; the pawn→thing and thing→store legs are reachability-checked earlier in the same search by `PawnCanAutomaticallyHaulFast` and the storage search, which kept those rare, but nothing earlier checks the store→job or pawn→job legs). The `== 0` test was therefore almost certainly meant as *"no path"* but implemented *"zero-length path"* | **1.5: unchanged** (`FindPath`, `NotFound` −1 — so the shipped 4.0.6 behaves exactly like 4.0.4 here). **1.6:** `FindPath` removed; synchronous `FindPathNow(start, LocalTargetInfo, TraverseParms, tuning, peMode)`; failure is `PawnPath.NotFound` (cost −1, `Found == false`); an **empty** path (`path.Length == 0`, which includes start-already-satisfies) is returned as `NotFound` | `PathCosts.TryGet`: tests `Found` (the evident intent: *no path ⇒ fail*); "start already satisfies the end mode" is decided first with `ReachabilityImmediate` and costs 0 and is **accepted** (the original rejected it — see deviation 9); destination always passed as a cell (the original's `IndexOutOfRange` fix). `FindPathNow` also force-completes scheduled async path jobs → see risks |
| Storage search | `TryFindBestBetterStoreCellFor` already split into a private `…ForWorker` (acceptance `slotGroup.parent.Accepts(t)`); WYU copied that body | **1.5:** acceptance is `slotGroup.Settings.AllowedToAccept(t)`, `ISlotGroup`. **1.6:** additionally, per slot group, `(parent is not a Thing or parent.Faction == faction) && parent.HaulDestinationEnabled` | `StorageSearch` re-derived from the 1.6 version so zWYU never picks storage vanilla itself would refuse |
| Non-cell storage | Vanilla's own opportunistic loop was **cell-only** in the 1.4 binaries (`TryFindBestBetterStoreCellFor` → `HaulToCellStorageJob`); `TryFindBestBetterStorageFor` existed but the loop did not use it. WYU replaced the loop and so was cell-only too | Vanilla's loop is container-aware **since 1.5** (`TryFindBestBetterStorageFor`, `HaulToContainerJob`) | zWYU, like the original, replaces the loop and chooses **cell storage only**. Consequence: **while zWYU's opportunity feature is on, vanilla's opportunistic haul-into-container behavior is suppressed** — exactly as the shipped 1.5 original (4.0.6) already did (a deviation from *vanilla ≥ 1.5*, not from *WYU*) |
| `ResourceDeliverJobFor` | linear, `need` and `foundRes` as plain locals; the loop is `foreach need in c.MaterialsNeeded()` — the **remaining** materials, so `need.count` was the current need (verified in the 1.4 binaries; `IConstructible` there has only `MaterialsNeeded()` / `EntityToBuildStuff()`, no `forced` parameter, no `IHaulEnroute`); vanilla already trimmed `resourcesAvailable` (`RemoveRange` + `Remove(foundRes)`) before building the job, as in 1.6 | **Since 1.5 — and 1.6 is the same, apart from a per-tick "no reachable resource" cache and the danger/`forced` arguments of the resource search:** closures around `need` and `foundRes`; `forced` parameter; `CanUseCarriedResource`; **the loop runs over `TotalMaterialCost()` (the WHOLE cost) and computes the current need separately (`num` = `GetSpaceRemainingWithEnroute` for `IHaulEnroute`, else `ThingCountNeeded`)**; `FindNearbyNeeders` may return a *remove-floor* job (in 1.4 too); the candidate list `resourcesAvailable` is **trimmed before the job is built** (in 1.4 too); the collector `FindAvailableNearbyResources` is byte-identical in 1.5 and 1.6. **Consequence for the original: the shipped 4.0.6 on 1.5 reads `need.count` from the closure, i.e. the whole material cost, so it compared the largest stack against the total cost and not against what is still needed** (§12.2) | Postfix that recomputes the current need with those same two APIs and re-runs vanilla's own collector for the full candidate list (§4); **`forced` is respected** (the original transpiler ignored it) |
| `HasJobOnThing` | base `WorkGiver_Scanner.HasJobOnThing` = `JobOnThing(...) != null`; the construction givers did **not** override it (verified), so "is there a job?" ran `ResourceDeliverJobFor` and the original had to postfix the base method to undo side effects | **1.6 only:** construction givers override it and call `ResourceDeliverJobFor` directly (no override in 1.5) | see patch 3; zWYU marks these scans (Prefix + Finalizer) so its postfix never runs inside them |
| `JobDriver.AddFinishAction` | `Action` (verified) | `Action<JobCondition>` since 1.5 | finish action receives the end condition (used for the diagnostic) |
| `ListerHaulables.ThingsPotentiallyNeedingHauling` | `List<Thing>` (also in 1.5) | `ICollection<Thing>` in 1.6 (a live `HashSet`) | The search copies it into a reusable buffer; the hook returns an empty array |
| Debug attributes | `Verse.TweakValue`, `Verse.DebugAction` (verified) | moved to `LudeonTK` since 1.5 | `using LudeonTK` |
| `TabDrawer.DrawTabs` | `(Rect, List<TabRecord>, int rows)` (verified) | generic, extra `float? maxTabWidth` since 1.5 | one extra `null` |
| Ideology/Biotech/Anomaly/Odyssey | — | more pawn kinds (mechs, subhumans, slaves) | Supply detours apply only to player-faction humanlike pawns that vanilla itself would let take an opportunistic job |

Not changed and re-verified in 1.6: `HaulAIUtility.HaulToCellStorageJob`, `PawnCanAutomaticallyHaulFast` (kept as a thin wrapper over `_NewTemp`),
`StoreUtility.CurrentStoragePriorityOf` (gained an optional `forced` argument), `IsGoodStoreCell`, `MassUtility.WillBeOverEncumberedAfterPickingUp`, `WithinRegions`, `DoBill` job shape
(`targetQueueB` + `countQueue`), `JobDriver_HaulToCell` toils (including `haulOpportunisticDuplicates`).

## 4. Why the transpilers were replaced, and by what

> **What gameplay semantic was each patch implementing?**

**`TryOpportunisticJob` transpiler.** *"After vanilla has decided this pawn may look for an opportunistic haul, run WYU's search **instead of** vanilla's haulable loop and return its result."*
The safest 1.6 hook that preserves exactly that, with no IL edits:

1. Prefix on `TryOpportunisticJob`: the invocation that finds **no active context acquires it** — stores `(pawn, job)` in `OpportunityContext` and *arms* it — and returns an **ownership token** through Harmony's `__state`. A nested (re-entrant) invocation is counted (`Depth`) but never acquires, and gets the *ignored* token.
2. Vanilla calls `ListerHaulables.ThingsPotentiallyNeedingHauling()` **exactly once, immediately after all its preconditions pass** (verified in the 1.6 IL). A Prefix there, while armed, runs the search and hands vanilla an **empty collection** — so vanilla's loop does nothing. Every other caller in the game is untouched (the context is not armed).
3. A **Finalizer** on `TryOpportunisticJob` receives the same token. **Only the invocation that acquired the context (token *owner*) may deliver its result and clear it**; an ignored nested invocation's finalizer touches nothing, and a finalizer whose prefix never ran (token 0) does nothing at all. Finalizers run when an exception unwinds, so the owner's context cannot leak, and the exception is always returned unchanged.
4. While a nested invocation is in flight (`Depth != OwnerDepth`) the lister prefix does not fire, so a nested call keeps vanilla's own loop and cannot consume the owner's armed search. (This state is three integers and two references; it is not a scheduler.)

Why not copy vanilla's preconditions into a Prefix? They are ~15 checks that Ludeon changes between versions (1.6's private rule gained `Downed` and *Anomaly-subhuman* checks that the 1.4 binaries do not have, and the loop's storage step changed to the container-aware search); a copy would go stale and let zWYU haul in situations vanilla forbids. Why not a Postfix alone? Vanilla's loop would already have returned its own job, losing WYU's storage choice and path checks.

Safety nets, all degrading to vanilla: anchor verified at startup by reading vanilla's IL (`OpportunityAnchor.Verify`: signature, and that it still calls the lister method — reading, not patching); a runtime **self-test** proves the lister patch is actually reached on the running runtime (a tiny method can be inlined by a JIT, bypassing any patch — that would otherwise be a *silent* failure); runtime exceptions in the search leave vanilla's collection alone so vanilla's loop runs; repeated faults switch the feature off.

**`ResourceDeliverJobFor` transpiler.** *"Before returning the delivery job, consider hauling the LARGEST nearby stack of the selected resource closer — but only if that stack is larger than what is CURRENTLY needed."* The original spliced in right after `FindAvailableNearbyResources`, so it saw two things:

* `need`, whose `count` is **how much of that material is still needed** (verified against the 1.4 binaries: the loop ran over `c.MaterialsNeeded()`, the *remaining* materials, so this matches the rule's intent, "only if there are extras"; **from 1.5 on `need.count` is the whole cost** — the shipped 1.5 original therefore drifted from that rule by accident, §12.2);
* `resourcesAvailable`, the **full** candidate list vanilla had just collected — the selected stack first, then same-def stacks within 5 cells until a carrying load is reached. (Vanilla trimmed that list after the splice point in 1.4 as well, so the original saw the untrimmed list; the trimmed one is what the finished job holds.)

From 1.5 on (1.6 is the same) neither survives into the returned job, which is why an earlier version of this port was wrong (§11):

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
`MassUtility` "no extras" hack, "extras only" rules for supplies/ingredients (for supplies: largest stack of the FULL nearby list, strictly larger than the CURRENT need — obtained differently from 1.4, same semantics as verified in the 1.4 binaries, §4), skip when bleeding (ordinary opportunities; ingredients/supplies still allowed) / preparing a caravan, all setting names and defaults, the *Vanilla / Default / Pathfinding* modes,
the debug drawing colors, job-report wording, stockpile/building filters and their per-mod defaults.

**Deviations from the original (each deliberate):**

1. **PUAH+ not ported** (§7). `UsePickUpAndHaulPlus`, the PUAH tab, unload ordering and the priority hack are absent. PUAH coexists.
2. **Common Sense**: the original un-ticked *Common Sense's* option in Common Sense's own settings file. zWYU never edits another mod's settings; while Common Sense's *"haul ingredients for a bill"* is on, zWYU simply skips its own bill detours. Read-only, fail-safe.
3. **Loop guard**: game ticks and armed-on-issue only (§5).
4. **Supply detours respect `forced`** (the original also produced detour jobs for right-click "prioritize delivering" menu evaluation) and **never run inside `HasJobOnThing`**.
5. **Supply detours are limited to pawns vanilla would let take an opportunistic job** (player faction, humanlike, capable, not drafted/downed/…). The original only checked work tags.
6. **Job identity blocks nested detours** (HaulToCell/HaulToContainer are prefix-capable in 1.4 too; the original used its same-frame guard for this, §5).
7. **Supply detours check `PawnCanAutomaticallyHaulFast` on the stack they will haul** (the bill path always did; the original supply path did not), so an unreservable/unreachable neighbour stack declines the detour instead of failing the haul job's pre-toil reservations.
8. **Bill target**: the first valid ingredient; the original's `FirstOrDefault() ??` yielded an invalid target for an empty queue.
9. **Path cost `Found` semantics — an intent-faithful deviation in both directions (§3).** The original rejected a candidate whose leg had `TotalCost == 0` (an *already-there* path, e.g. an item next to the pawn, or a stockpile cell next to the item) and did **not** reject an unfound path (cost −1, which flowed into the ratio checks). zWYU rejects an unfound path and accepts a zero-cost leg. Reproducing the original literally would mean *rejecting adjacent legs* and *accepting unreachable ones*, and in 1.6 the literal `== 0` could never fire for a found path at all (empty paths are `NotFound`). This is the one place where the baseline knowingly departs from what the original's code did rather than what it evidently meant; it affects only the *Default* and *Pathfinding* path modes, and only for adjacent legs (which the original rejected) and for store→job / pawn→job legs with no path (which the original let through).
10. **One termination guard** (new): at most `MaxRangeExpansions = 40` range-expansion passes per search (the original ended by float overflow). It is **not** a candidate budget: a unit test proves it cannot cut off a legitimate candidate on a 1000-cell map at the default, minimum (1.1) and maximum (3.0) tweak factors, and `SearchLoopTests` prove that candidates beyond the 12th, 40th, even 2000th are still examined. There is **no limit on how many candidates are examined or pathfound**, in any mode (an earlier version of this port capped *Pathfind during search* at 12 pathfound candidates; that changed behavior and was removed, §11).
11. **Storage-building filters** use a defName allow-list, persisted by name, instead of a cloned `ThingFilter` tree. Behavior (per-building, per-mod grouping, auto vs manual, per-mod defaults) is the same; the on-disk format is new. Config files from the original are not read (new package id ⇒ new file).
12. New setting: **Debug logging** (Off/Summary/Verbose). Removed: the PUAH switches.
13. `Testing.cs` → *Make colony (zWYU)* (renamed, same content).
14. **Supply extras compare with the remaining need** (the 4.0.4 / 1.4 rule) — not with the total cost the shipped 4.0.6 accidentally used on 1.5 (§12.2).
15. **Supply detours are decided on the finished delivery job**, not mid-method: when vanilla returns a remove-floor / install job the postfix declines (the original could have hauled first); when the builder already carries the needed material vanilla's `foundRes` is the carried stack (see risk 4 and `T-5.9`).

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
4. **Construction.** The supply hook depends on vanilla's delivery job being a `HaulToContainer` with `targetA` = the selected resource, and on two private vanilla members bound by reflection (`FindAvailableNearbyResources`, `resourcesAvailable`); startup verifies them and disables the feature with a reason if they moved. Re-running the collector costs one extra radial scan (~5-cell radius) per real supply decision that passed every cheap check; `HasJobOnThing` scans, forced orders and ineligible pawns never reach it. **Partial constructions and enroute deliveries (`IHaulEnroute`) are the cases to watch at runtime** — they are exactly where the current need differs from the total cost. Also watch a builder that **already carries** the needed material (queued orders): `foundRes` is then the carried stack, which is not spawned, so a detour is created only if a *different*, larger nearby stack qualifies — vanilla then drops the carried stack before the haul. This is a 1.5+ vanilla situation the 1.4-based original never faced; if it shows up as a problem the fix is a one-line decline in `TryCreateForSupply` (`pawn.carryTracker.CarriedThing == foundRes`).
5. **Synchronous pathfinding, uncapped.** `FindPathNow` force-completes any scheduled async path jobs. *Default* mode pathfinds at most one candidate per search (up to 4 queries; a failure ends the search). *Pathfind during search* pathfinds **every** candidate that survives the cheap checks, **without any candidate limit** (baseline fidelity): in a large colony with many haulables that cannot be reached the cost is unbounded per search. The per-search Summary line reports `path checks N (Q synchronous path queries, T ms)` so the cost is measurable; bounding it is future optimization work, deliberately not done here.
6. **Cell storage only** (see §3): while the opportunity feature is on, vanilla 1.6's own opportunistic haul-into-container is not used, so an item vanilla 1.6 would send to a higher-priority *container* may be hauled to a lower-priority cell store first (or not at all). This matches the original, whose vanilla (1.4) baseline had no container opportunism.
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

## 12. Checked against the RimWorld 1.4 binaries

The first version of these notes described the 1.4 side from the original's source and from memory; the 1.4 assemblies (`Assembly-CSharp` 1.4.8418) were supplied afterwards and used to check every "1.4 did X" statement. Nothing in the code changed because of it except comments; three statements in these notes were **wrong** and are corrected above.

| Claim | Result |
|---|---|
| `TryOpportunisticJob` got its `(Job finalizerJob, Job job)` signature and `finalizerJob` early return in 1.6 | **Wrong** — both are already in 1.4, as are `CanPawnTakeOpportunisticJob` and the Biotech age check. 1.6 added `Downed` + *Anomaly-subhuman* to the private rule, the `ICollection` loop and the container-aware storage step. |
| The original's `cost == 0` failure test matches the old `NotFound` path having no cost | **Wrong** — 1.4 `NotFound` has cost −1 (`Found => totalCostInt >= 0f`); cost 0 is a *found* zero-length path. The original's test rejected already-there legs and let unfound ones through. Deviation 9 states exactly what zWYU does instead. |
| "Non-cell storage: WYU ignored containers, same in 1.4" | **Imprecise** — vanilla 1.4's own loop was cell-only, so there was nothing to ignore; container opportunism arrived in **1.5** (checked in the 1.5 assembly) and zWYU, like the original's loop replacement (already in the shipped 1.5 build), suppresses it while enabled. |
| `need.count` in the original is the remaining need (was "as established in review") | **Confirmed** — the 1.4 loop is `foreach need in c.MaterialsNeeded()`; `IConstructible` has no `ThingCountNeeded`; no `IHaulEnroute` in 1.4. Vanilla also trimmed `resourcesAvailable` after the splice point in 1.4, so the original saw the untrimmed list. |
| The original patched `WorkGiver_Scanner.HasJobOnThing` because the construction givers inherit it | **Confirmed** — no override in 1.4; the base method is `JobOnThing(...) != null`. In 1.6 the construction givers override it, which is why the original patch cannot be ported as-is (patch 3). |
| `AddFinishAction(Action)`, `TweakValue`/`DebugAction` in `Verse`, 3-argument `TabDrawer.DrawTabs` | **Confirmed** (all changed in 1.6 as listed in §3). |
| Vanilla 1.4 `HaulToCell` / `HaulToContainer` job defs have `allowOpportunisticPrefix` | **Verified in the 1.4 Core XML: `true`** for both (and for `DoBill`, `Goto`, `GotoWander`). My earlier statement that this was new in 1.6 was **wrong** (a fourth correction); it explains the original's same-frame guard. |

Still not verified in any binary: behavior inside a running game (both versions).

### 12.1 Checked against the original's released binaries and the 1.4 Core XML

Supplied afterwards: the original mod's shipped package (`JobsOfOpportunity.dll` 1.1–1.3, `WhileYoureUp.dll` for 1.4 = **4.0.4.466** and for 1.5 = **4.0.6.5399**), the complete 1.4 `Managed` folder (same `Assembly-CSharp` as before) and the 1.4 `Core` folder. Decompiled and compared:

| Question | Result |
|---|---|
| Is the source baseline (`v4.0.4`, `1c54e7c`) what shipped for 1.4? | **Yes** — the 1.4 DLL is 4.0.4.466; its transpilers, `CanHaul`, `GetPathCost` (`== 0`), settings defaults (30 / 50% / 50 / 60% / 170% / 100% / 25 / 25, *Default* path mode, all boxes on, `HeuristicRangeExpandFactor 2`) match the source and `SettingsDefaults`. |
| Where exactly did the `TryOpportunisticJob` transpiler splice? | In the real 1.4 IL, `ldfld Map::listerHaulables` is `IL_00e2`; the splice is **3 instructions earlier**, at `IL_00d7` — the first instruction of `pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling()` and immediately after the `!pawn.Spawned` check. Everything before it (drafted / mental state / age / `WorkTagIsDisabled` / distance < 3 / spawned) still ran in vanilla; from there the original returned its own result (`Opportunity_Job`, possibly `null`) and **vanilla's loop never ran**. zWYU's lister-call Prefix returns an empty collection at the same point in the flow: same preconditions, same replacement of the loop. |
| Does the original's same-frame guard really arm on an empty search? (deviation 3) | **Yes** — `CatchLoop_Job(pawn, job, caller)` sets `lastPawn` / `lastFrameCount` unconditionally and returns `job` (possibly `null`); `AlreadyHauling` then refuses for that pawn in that frame. |
| Does the shipped original test `pathCost == 0`? (deviation 9) | **Yes**, in both the 1.4 and 1.5 builds (`if (pathCost == 0f) return false;` for all four legs). |
| What did the 1.5 build (4.0.6) change relative to 4.0.4? | Only API adaptations: `AddFinishAction(Action<JobCondition>)`, `LudeonTK`, `TabDrawer.DrawTabs<TabRecord>(…, 200f)`, `QuickSearchWidget.OnGUI(rect, null, null)`, `new ThingCount(thing, count, false)`, `Frame.ThingCountNeeded(def) <= 0` in the PUAH+ storage rule (was `Frame.cachedMaterialsNeeded…Contains`, so `Frame.ThingCountNeeded` exists by 1.5), and a **re-anchored supply transpiler** that finds the closure fields `need` and `foundRes` by field load instead of by the closure's `newobj`. The `TryOpportunisticJob` transpiler, the path checks, the settings and the loop guard are the same logic. |
| Which value did `need` hold in the shipped 1.5 original? | **Resolved by the 1.5 assembly (§12.2): the whole material cost.** 1.5's `ResourceDeliverJobFor` already loops over `TotalMaterialCost()` and computes the current need separately, so the shipped 4.0.6 compared the largest stack against the total, not the remaining need. zWYU implements the 4.0.4 / 1.4 rule (remaining need, strict `>`), which is also what the rule's stated intent and the PR review require. |
| CodeOptimist library | The shipped DLLs contain `NonThingFilter`, `Listing_TreeNonThingFilter`, `NonThingFilter_LoadingContext`, `WeakDictionary` (+ its `Cull` patch), `Defer`, `DrawContext`, `CodeInstructionComparer`, `TranspilerHelper` — code **newer than / absent from the public library snapshot** (Jun 2023), confirming `LIBRARY_AUDIT.md`'s finding directly from the binary. |
| PUAH add-on | The shipped DLL's PUAH patches are the ones in the 4.0.4 source (`WorkGiver_HaulToInventory`, `JobDriver_UnloadYourHauledInventory`, `StorageSettings.Priority`, …). PUAH itself is not in the package. Nothing here changes the *coexist, don't integrate* decision (§7). |

### 12.2 Checked against the RimWorld 1.5 binaries (1.5.9102) and the 1.5 Core XML

The 1.5 game files were the last input. They make the version history of each behavior exact:

| Item | 1.4 | 1.5 | 1.6 |
|---|---|---|---|
| `TryOpportunisticJob` shape, `finalizerJob` return | yes | yes | yes |
| `CanPawnTakeOpportunisticJob` extras | drafted, mental/burning, rebelling | + `Downed`, + Anomaly `IsMutant` | `IsMutant` → `IsSubhuman` |
| Opportunistic loop | `for` over `List<Thing>`; **cell-only** | same list loop; **container-aware** (`TryFindBestBetterStorageFor`, `HaulToContainerJob`) | `foreach` over `ICollection<Thing>`; `PawnCanAutomaticallyHaulFast_NewTemp`; `CurrentStoragePriorityOf(item, playerForced)` |
| `allowOpportunisticPrefix` on `HaulToCell` / `HaulToContainer` / `DoBill` | true | true | true |
| Pathfinding | `FindPath`, `NotFound` cost −1 | same | `FindPathNow`, empty path = `NotFound` |
| Storage search | private `…ForWorker` with `parent.Accepts(t)` | `Settings.AllowedToAccept(t)` | + faction / `HaulDestinationEnabled` gate per slot group |
| `ResourceDeliverJobFor` | loop over `MaterialsNeeded()` (remaining); no closures, no `forced` | **closures, `forced`, loop over `TotalMaterialCost()` + separate `num`, `CanUseCarriedResource`** | identical to 1.5 + "no reachable resource" tick cache and danger/`forced` in the resource search |
| `FindAvailableNearbyResources` | — | — | byte-identical in 1.5 and 1.6 |
| `IConstructible` | `MaterialsNeeded`, `EntityToBuildStuff` | `TotalMaterialCost`, `ThingCountNeeded`, `EntityToBuildStuff` | same |
| `HasJobOnThing` on the construction givers | not overridden | not overridden | **overridden** |
| `AddFinishAction`, `LudeonTK`, `DrawTabs` shapes | `Action`, `Verse`, 3-arg | `Action<JobCondition>`, `LudeonTK`, generic 4-arg | same as 1.5 |
| `ThingsPotentiallyNeedingHauling` | `List<Thing>` | `List<Thing>` | `ICollection<Thing>` |

**What this means for the original.** The last shipped original (4.0.6, RimWorld 1.5):

* still splices `TryOpportunisticJob` at the same place and behaves like 4.0.4 there, including `== 0` path tests on a `FindPath` whose `NotFound` is −1;
* **suppresses vanilla's new container opportunism** in the same way zWYU does;
* **compared the largest nearby stack against the WHOLE material cost** for construction supplies. That is an accident of the 1.5 restructure (its transpiler was re-anchored mechanically), not a design decision: 4.0.4's rule was "extras beyond what is still needed". So the first version of this port, which used the total cost, was in fact faithful to the *last shipped binary* but not to the rule — and the review fixed it toward the rule. **The baseline zWYU implements is the 4.0.4 / 1.4 rule.** If a reference that reproduces the 1.5 behavior literally is ever wanted, it is a one-line change in `SupplyRules`' caller (`currentNeed` → total cost) and must be a deliberate, documented choice;
* had its base-`HasJobOnThing` patch still effective (1.5 construction givers do not override it).

Corner cases that the *post-hoc* postfix sees differently from the original's mid-method splice (both are rare, neither is a failure mode; see §6.14–15 and the runtime plan):

* the splice sits **before** `FindNearbyNeeders`, so the original could return its haul-closer job where vanilla would return a remove-floor job; zWYU's postfix sees the final job and declines when it is not a delivery (§4);
* `CanUseCarriedResource` (1.5+): when the builder already carries the material, `foundRes` is the carried thing.

