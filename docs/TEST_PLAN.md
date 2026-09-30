# zWYU runtime test plan (RimWorld 1.6)

The code compiles, every patch applies to the real 1.6 assembly, and the pure rules are unit-tested (see README → *Automated checks*).
**None of that proves it works in the running game.** This checklist is for the owner's first real-world pass. Nothing here needs special tooling beyond
RimWorld's Dev mode.

## 0. Setup

* Mods, in order: **Harmony**, **zWYU** (add *Pick Up And Haul* for §8). Do not load the original *While You're Up*.
* Options → Mod settings → *zWYU (While You're Up)* → set **Debug logging = Summary** for the whole session
  (Verbose only for a single reproduction). Enable **Draw colored path detours** (or Dev mode → Visibility → *Draw Opportunistic Jobs*).
* Have the log ready (`Player.log`, or the in-game log window with Dev mode → *Open log*). Everything zWYU prints starts with `[zWYU]`.
* Handy Dev-mode tools: *Spawn thing*, *Destroy*, *Forbid/Unforbid* (the padlock on items), *Toggle god mode*, *Add hediff* (for bleeding), *Autotests → Make colony (zWYU)*, *Tick* speed controls.
* Use a **new colony** for most tests, plus **one existing save** for §1.

**Reading the report text** on a pawn's info tab / hover: *"Hauling steel to Stockpile **on the way to** Bill Bench."* = an **Opportunity**;
*"…, headed **closer to** Wall."* = **haul-before-carry** (supplies/ingredients). Plain vanilla text = an ordinary haul.

**Not runtime-testable by hand:** ownership of the search context under nested/re-entrant `TryOpportunisticJob` calls (vanilla never re-enters it) is covered by `Source/zWYU.PatchAudit` (§5), including a genuinely nested call through the patched game method.

**What a healthy Summary log looks like** (one pawn, one opportunity):

```
[zWYU][Opportunity] Alice#123 about to do GotoWander(...)#… searching 14 haulables (target (80,0,42), path checker Default)
[zWYU][Opportunity] SELECTED Opportunity owner=Alice#123 haul=Steelx35#456@(55,0,40) -> store (60,0,41) (Stockpile 1), destination=…, originalJob=…, decided@…
[zWYU][Opportunity] Alice#123: selected. evaluated 3, range expansions 0, path checks 1, 0.41 ms
[zWYU][Lifecycle] STARTED Opportunity owner=Alice#123 …
[zWYU][Lifecycle] detour completed; vanilla resumes the original job … from the job queue: Opportunity …
```

**Hook counters** (settings window, visible while logging is on): `opportunity checks seen N, search engaged M, haul found K`.
Healthy: N and M grow together during normal play (M ≤ N, because vanilla rejects some checks before the search). **If N grows and M stays 0, the haulable-search hook is not being reached: fail T-1.3 and report.**

For each item: ☐ pass · ✗ fail (attach the log + what the pawn was doing) · n/a.

---

## 1. Baseline

| ID | Steps | Expected |
|---|---|---|
| T-1.1 | Start the game with Harmony + zWYU. | Main menu with **no red errors**. Log contains exactly one `[zWYU][Startup] loaded (RimWorld 1.6 port). Opportunities/bills: hooked. Construction supplies: hooked. Job reports/lifecycle: hooked. …`. **Any `DISABLED` is a failure to report** (it names the patch and reason). |
| T-1.2 | Start a **new colony** (any scenario). Let it run ~2 in-game days at 3×. | No red errors mentioning zWYU or "Harmony"/patch exceptions. |
| T-1.3 | With Summary logging, send a colonist somewhere ≥ 20 cells away (draft is *not* enough — use a work order such as mining/chopping/constructing far away, or let them wander to work). | `searching N haulables` lines appear. Counters in the settings window: seen > 0 **and engaged > 0**. |
| T-1.4 | **Load an existing save** (one made without zWYU). | Loads; no red errors; pawns behave normally; nothing zWYU-related in the log until pawns start jobs. |
| T-1.5 | Open the settings window; change some values on every tab (sliders/percent boxes on *Advanced*, checkboxes, path mode, both filter lists, switch *Edit manually* on/off); close; **restart the game**; reopen. | Values persist. *Restore to default settings* restores the documented defaults (30 / 50% / 50 / 60% / 170% / 100% / 25 / 25, Default path mode, all boxes on). No errors when the filter list scrolls, searches, or when a mod adds storage buildings. |
| T-1.6 | Toggle **Enabled** off mid-game. | Opportunity/bill/supply detours stop immediately (no restart); job reports return to vanilla; no errors. Toggle on: they return. |
| T-1.7 | Set Debug logging back to **Off**. | No `[zWYU]` decision lines any more (errors would still print). |

## 2. Normal opportunistic hauling

Arrange: a stockpile (Normal or higher) and **loose items** (e.g. `Spawn thing` → steel, wood, components) lying in the open *not* in a stockpile, near the line between a colonist and a distant job.

| ID | Steps | Expected |
|---|---|---|
| T-2.1 | Colonist at A, work target (e.g. a tree to chop, a mining designation, a bench) ~40–60 cells away at B; a loose stack ~10 cells from A toward B; stockpile close to B or the stack. Let the colonist take the job. | Report reads *"Hauling … on the way to …"*; the pawn walks to the stack, hauls it to the stockpile, **then continues to B** and does the original job (log: `STARTED …` → `detour completed; vanilla resumes the original job`). |
| T-2.2 | Same, with the colored-path debug on. | Lines: red = original path, green = new legs; cells flash (start green, haulable cyan, storage orange, job red). |
| T-2.3 | Repeat with several loose stacks near the route. | One stack per decision; after the haul, vanilla resumes the job and may pick another opportunity (chains are vanilla-consistent). **No pawn takes two hauls in the same tick** (log: never two `SELECTED` for one pawn at the same tick). |
| T-2.4 | Switch **Path checking** between *Vanilla*, *Default*, *Pathfinding* and repeat T-2.1. | All three work. *Pathfinding* finds hauls *Default* rejects only for candidates that need a real path check; watch frame rate on a large map. |
| T-2.5 | Tweak **Advanced** limits (e.g. total trip 100%) and repeat. | Fewer/farther opportunities respectively; log summary shows `Reject events: …` reasons that match. |
| T-2.6 | **Pathfind during search, many failing candidates:** scatter 15+ loose stacks that pass the cheap checks but are unreachable/behind walls (path check fails), and put one perfectly reachable stack on the route. | The reachable stack is still found — **there is no limit on how many candidates are examined**. The Summary line shows `path checks N (Q synchronous path queries, T ms)`; expect cost to grow with N (a documented, deliberately unoptimized concern). |
| T-2.7 | **Default** mode with the same scatter, where the *first* candidate examined fails its single pathfind. | The whole search ends with no opportunity (`stopped: PathUnreachable …`) — the original "pathfind once after search" behavior. |

## 3. Negative cases (expect **no** detour, normal behavior, no errors)

Log check for each: the summary line shows the reason under `Reject events: …` (or Verbose shows the candidate).

| ID | Setup | Expected |
|---|---|---|
| T-3.1 | Haulable so far off route that start→thing→store→job exceeds the limits. | No detour. `TotalTrip`/`NewLegs`/range reasons. |
| T-3.2 | **Forbidden** item (padlock). | Not hauled. `Forbidden`. |
| T-3.3 | Item **reserved** by another pawn (someone already hauling it). | Not hauled. `ReservedByOther`. |
| T-3.4 | **Unreachable** item (walled off / behind a locked door area). | Not hauled. `CannotAutomaticallyHaul` (or `PathUnreachable`); no "can't reach" red errors. |
| T-3.5 | **No valid storage** (no stockpile accepting it, or all Unstored). | Not hauled. `NoBetterStorage`. |
| T-3.6 | **Storage fills** while a detour is running (pre-fill the stockpile; or set its priority to Unstored mid-haul). | The haul job fails/adjusts the vanilla way; the pawn then continues normal behavior; no standing/looping pawn; nothing stuck in the pawn's hands. Log: `detour ABORTED (…)` or a completed haul to another cell. |
| T-3.7 | **Item disappears** mid-detour (Dev *Destroy*, or another pawn takes it). | Job ends; original job resumes or vanilla thinks anew; log `ABORTED (Incompletable…)`; no reservation warnings piling up. |

## 4. Job interruption (start a detour, then interfere)

| ID | Action during the detour haul | Expected |
|---|---|---|
| T-4.1 | **Draft** the pawn. | Haul job ends; pawn stands drafted; log `ABORTED`; no zWYU error. After **undrafting** the pawn resumes normal work (no permanent detour state; a later job may get a fresh detour). |
| T-4.2 | Give a **manual order** (right-click) that replaces the job. | Order runs; log `ABORTED`, and `original job was discarded`. |
| T-4.3 | **Down** the pawn (Dev *Damage*). | Haul ends; pawn is downed normally; no errors when rescued/recovered. |
| T-4.4 | Give the pawn heavy **bleeding** (*Add hediff* → wound/`Cut` with high bleed) *before* they take a far job. | **No opportunity** (log: bleeding). Ingredient/supply "closer" hauls are still allowed while bleeding (§5/§6). |
| T-4.5 | **Destroy the original target** (e.g. the tree/bench they were headed to) during the detour. | After the haul the pawn thinks normally; no error, no pawn stuck "standing". |
| T-4.6 | Put the pawn into a **mental break** (Dev *Mental state*). | Detour ends via vanilla; no zWYU error. |
| T-4.7 | Pause/unpause and change game speed repeatedly during a detour. | No effect. |
| T-4.8 | Save and **reload** during a detour. | Pawn continues the haul as an ordinary haul (state is not saved); no errors; afterwards normal detours resume. |

## 5. Construction

| ID | Steps | Expected |
|---|---|---|
| T-5.1 | Blueprint a wall 40+ cells from a **large stack** (e.g. 300 steel) lying near the builder, with a stockpile between them (or near the wall). A builder with Construction and (for the test) Hauling *disabled*. | Report *"…, headed **closer to** Wall."* The builder first hauls the stack (extras included) to storage nearer the site, then delivers and builds. Supplies get delivered; the wall gets built. |
| T-5.2 | Same, but the largest nearby stack is **no larger than what is still needed** (for an untouched blueprint that is the whole cost). | **No** haul-closer (the "only if there are extras" rule is strict: stack must be **larger than** the current need). Vanilla delivery. |
| T-5.3 | **Partial materials — the decision uses what is STILL needed, not the total cost.** Pick a construction whose material total is well above a stack size you can spawn (e.g. a large steel building), let a builder deliver until **M** remain (read *Materials needed* on the frame), then have a *different* builder about to fetch it with a **loose stack S in the open, M < S ≤ total cost**, storage between it and the frame, and no larger stack nearby. | **Haul-closer happens** ("…, headed closer to Frame"): S > M. *(The first version of this port wrongly compared S to the total cost and would have declined.)* Log (Verbose): `supply candidate … vs current need M`. |
| T-5.3b | Same, but adjust so that **S == M exactly**. | **No** haul-closer (strict `>`): log `no extras … largest of N nearby stacks is M, current need M`. |
| T-5.3c | **Several stacks near the selected resource:** the closest stack is small, a **larger stack** lies within ~5 cells of it (and vanilla would not need it to fill the builder's load). | The **larger** stack is the one hauled toward the site ("largest of N nearby stacks" in the Verbose log), not the closest/selected one. |
| T-5.3d | A frame that needs **two materials** (e.g. steel + components), one already complete. | Each material is handled in turn; only the still-needed material can trigger a detour; no stuck states; construction completes. |
| T-5.4 | **Construction completes/canceled while the pawn is choosing** (deconstruct/cancel the blueprint right as a builder is assigned; use *Tick* stepping). | No error; the pawn moves on. |
| T-5.5 | Order a **right-click "Prioritize delivering"** on a blueprint. | The player's order is never redirected into a detour (forced orders are excluded). |
| T-5.6 | Install/reinstall (minified furniture) a building. | Vanilla behavior (install jobs are excluded). |
| T-5.7 | Turn **Haul extra construction supplies closer** off. | Behavior returns to vanilla for supplies immediately. |
| T-5.8 | **Two builders deliver to the same frame** at once (vanilla tracks in-flight deliveries: `IHaulEnroute`). | No errors; detours are judged against the space *remaining with enroute deliveries*, so a builder whose material is already being delivered by someone else is not sent to haul extras for it. |

## 6. Bills

| ID | Steps | Expected |
|---|---|---|
| T-6.1 | A workbench 40+ cells from a large stack of its ingredient (e.g. 500 steel for a bill needing 100), storage between; a crafter who will take the bill. | *"…, headed closer to Bench."* hauling extra to storage near the bench, then the bill is worked. |
| T-6.2 | Ingredient stack **not larger** than what the bill takes. | No haul-closer. |
| T-6.3 | The ingredient **disappears / becomes reserved** between planning and the haul. | Job fails cleanly; the bill job is rebuilt by vanilla; no "Standing" loop. |
| T-6.4 | **Suspend / delete the bill** mid-detour. | After the haul, the crafter picks other work; no errors. |
| T-6.5 | With **Common Sense** loaded: turn on its *"haul ingredients for a bill"* option. | zWYU logs it yields (`bill-ingredient detours are yielding to Common Sense`) and does no bill detours; both mods do not fight. Turn it off: zWYU resumes. |

## 7. Storage

| ID | Setup | Expected |
|---|---|---|
| T-7.1 | Two stockpiles, different priority (Important / Normal); an item outside both. | The higher-priority storage is chosen when it satisfies the limits (the storage nearer to the *midway* point among equal priority). |
| T-7.2 | A **shelf** (building storage) as the only accepting storage. | Used for opportunities (default allows Core buildings). Toggle the building off in the *Edit manually* list → not used. |
| T-7.3 | **Equal-priority** case for supplies/ingredients: item already in a Normal stockpile far from the site; another Normal stockpile next to the site. | With *Haul extra resources closer from same-priority storage* **on**: hauled to the near stockpile ("closer to"). **Off**: not. It never bounces back and forth (each haul is strictly closer to the destination). |
| T-7.4 | *Stockpiles* checkbox off (Opportunities tab). | Stockpiles are no longer opportunistic destinations; shelves still are. |
| T-7.5 | A mod's storage (e.g. LWM Deep Storage, if you have it) | Default: not used for opportunities (slow storing). Allow it manually → used. |

## 8. Pick Up And Haul (coexistence only)

| ID | Steps | Expected |
|---|---|---|
| T-8.1 | **Without** PUAH: everything above. | Works. |
| T-8.2 | **With** PUAH: log has `[zWYU][Compat] Pick Up And Haul detected. Coexistence only…`; settings window shows the PUAH note. | No red errors at startup. |
| T-8.3 | PUAH haulers work as before (they gather and unload). | PUAH behaves as it does alone; zWYU does not alter its jobs. |
| T-8.4 | A pawn carrying PUAH inventory (mid-haul, before unloading) is given a far job. | **No zWYU detour** for that pawn until unloaded (Verbose log: `still carrying Pick Up And Haul inventory`). |
| T-8.5 | After a zWYU detour, inspect the pawn's inventory (gear tab). | **No leftovers** from zWYU (its hauls carry in hands). No duplicate hauling ownership: the same item never has both a zWYU haul and a PUAH job. |
| T-8.6 | Uninstall/disable PUAH and load the same save. | Works (PUAH optional). |

## 9. Stress sanity

1. Save. Dev mode → *Debug actions → Autotests → **Make colony (zWYU)***. (30 colonists with hauling disabled, all benches with bills, one of every item, 7 stockpiles; colored paths on.)
2. Run at Normal, then Fast/Superfast for ~3 in-game days with Summary logging.

Watch for (any is a failure to report with the log):

* pawns **stuck standing** (report *"standing"* / idle) that never resume;
* **repeated jobs** — the same pawn getting a haul every tick; vanilla's *"started 10 jobs in one tick/…jobs in 10 ticks"* errors (if one appears, the log line right before it starts `vanilla is recovering …` and includes the zWYU plan);
* items dropped in a loop (haul → drop → haul the same item);
* reservation warnings/errors from `ReservationManager` accumulating;
* frame-time spikes correlated with `[zWYU][Opportunity]` lines (see the `… ms` at the end of each summary; in *Pathfinding* mode 10s of ms is expected on huge maps).

Expected: many opportunities and haul-closer trips, all ending in `completed` or a clean `ABORTED`, no errors.

## 10. Reporting

For any failure: the log excerpt (`[zWYU]` lines around the event with Summary or Verbose on), the pawn's job report text, the mod list, and which test ID.
`Player.log` is at `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log` (Windows), `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log` (Linux).
