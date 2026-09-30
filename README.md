# zWYU — While You're Up, for RimWorld 1.6

**Opportunistic hauling.** While a colonist is already walking somewhere useful, they take a worthwhile haul along the way.
zWYU is a RimWorld 1.6 modernization of [*While You're Up*](https://github.com/CodeOptimist/rimworld-while-youre-up)
(a.k.a. *Jobs of Opportunity*) by Christopher S. Galpin (CodeOptimist). **It is a derived port, not an unrelated mod** — see
[Attribution & license](#attribution--license).

> **Status: baseline, ready for runtime testing.** It builds against RimWorld 1.6, every Harmony patch is verified to apply to the
> real 1.6 assembly, and the pure rules are unit-tested — but it has **not yet been played in RimWorld**. The runtime test checklist is in
> [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md). This is deliberately **not** an optimization project and **not** a Pick Up And Haul
> integration; it is the trustworthy reference implementation those later steps will be measured against.

## What it does

| Feature | In game | How it works (unchanged from the original) |
|---|---|---|
| **Opportunities** | job report: *"Hauling X to Y **on the way to** Z."* | Vanilla is about to start a job that has somewhere to go. zWYU may first send the pawn to haul a nearby item to storage, if start → item → storage → job stays within configured limits. Vanilla queues the original job first, so it resumes afterwards. |
| **Supplies** | *"…, headed **closer to** the blueprint."* | A builder fetching construction supplies first hauls them (plus extras) to storage nearer the site, when that shortens the carry. |
| **Ingredients** | *"…, headed **closer to** the workbench."* | Same for bill ingredients. |
| Path checking | settings | *Vanilla* (ratios + region count) · *Default* (one real pathfind after the search; if it fails, no opportunity) · *Pathfinding* (real pathfinding per candidate). |
| Storage filters | settings | Per-building allow-lists, stockpile switches, auto-managed defaults (slow-storage mods such as LWM's Deep Storage are excluded for opportunities). |

Skipped: ordinary opportunities while bleeding or preparing a caravan; anything vanilla itself forbids (drafted, downed, mental state, player-forced orders…).

## What is *not* in this baseline

* **Pick Up And Haul+ enhancements** (multi-item hauls, efficient unloading order, same-priority-storage hack). PUAH itself keeps working; zWYU coexists
  (it starts no detour for a pawn that still carries PUAH-hauled inventory). Reasons and details: [`docs/PORTING_NOTES.md`](docs/PORTING_NOTES.md#pick-up-and-haul).
* Any optimization, caching, spatial indexing, or route planning (see *Future optimization candidates* in the notes — observations only).
* Hauling into non-cell containers (vanilla's `HaulToContainer` destinations). The original never did either — its vanilla (1.4) had no container opportunism to replace. In 1.6 vanilla does, so **while zWYU's opportunity feature is on, vanilla's own container opportunism is not used** (details: [`docs/PORTING_NOTES.md`](docs/PORTING_NOTES.md), §3).

## Install

Requires the **Harmony** mod. Pick Up And Haul and Common Sense are optional.

* **From CI:** open the latest *Build* workflow run and download the `zWYU-mod` artifact; unzip so you have `RimWorld/Mods/zWYU/About/About.xml`.
* **From source:** [build](#build) it, then copy `About/`, `Languages/`, `1.6/` (and `LICENSE`, `NOTICE`) into `RimWorld/Mods/zWYU/`.

Do **not** enable it together with the original *While You're Up* / *Jobs of Opportunity* (`CodeOptimist.JobsOfOpportunity`); the metadata marks them incompatible.

## Settings & diagnostics

Options → Mod settings → *zWYU (While You're Up)*.

* **Enabled** — toggle everything without restarting.
* **Draw colored path detours** — same as vanilla's *Dev mode → Visibility → Draw Opportunistic Jobs*.
* **Debug logging** — `Off` (default) · `Summary` (one line per decision: *considered → selected/none → started → ended*, with reject-reason totals and timing) · `Verbose` (adds one line per rejected candidate).
  With any level on, the settings window also shows hook counters (see the test plan for how to read them).
* If a hook cannot be applied or a vanilla method changed, **that feature switches itself off**, logs *which patch and why*, and says so in the settings window. Vanilla behavior is untouched.
* Dev mode → *Debug actions → Autotests → **Make colony (zWYU)*** builds a stress-test colony (30 colonists with hauling disabled, every workbench with bills, every item, 7 stockpiles). **Save first** — it wipes the map's pawns.

## Build

Requirements: .NET SDK 8+ (targets `net472`, the game's runtime, so no Windows or Mono needed). Nothing from Ludeon is stored in this repository.

```sh
# 1. default: reference assemblies from the Krafs.Rimworld.Ref NuGet package — nothing to install
dotnet build Source/zWYU/zWYU.csproj -c Release

# 2. compile against your actual game assemblies instead
dotnet build Source/zWYU/zWYU.csproj -c Release -p:RimWorldManagedDir="<RimWorld>/RimWorldWin64_Data/Managed"
#    (or set the RIMWORLD_MANAGED_DIR environment variable; never commit a path)
```

Output: `1.6/Assemblies/zWYU.dll` (git-ignored). Harmony is compile-only; at runtime the Harmony mod provides it.

### Automated checks

```sh
dotnet test Source/zWYU.Tests            # rules, defaults, licensing/metadata/translation/no-transpiler invariants (no game needed; runs in CI)

# needs the game's Managed folder; proves every patch applies to the REAL assembly and the hooks behave (60+ checks: anchors, every patch, the patch map, failure isolation, nested/re-entrant ownership, supply plumbing)
dotnet build Source/zWYU/zWYU.csproj -c Release -p:RimWorldManagedDir="<Managed>"
dotnet run --project Source/zWYU.PatchAudit -c Release -- --managed "<Managed>"
```

## Repository layout

```
About/  Languages/  1.6/Assemblies/         the mod itself (the DLL is a build output)
Source/zWYU/                                 the mod
  Detour/      TripRules (pure geometry) · OpportunitySearch · StorageSearch · BeforeCarry · DetourTracker (state lifecycle)
  Patches/     the Harmony hooks (prefix / postfix / finalizer only; PatchBootstrap applies them feature by feature)
  Settings/    settings, defaults, storage-building filters, settings window
  Compat/      optional Pick Up And Haul + Common Sense (isolated, fail-safe)
  Diagnostics/ logging + feature gating   Lib/CodeOptimist/  the few upstream helpers actually needed
Source/zWYU.Tests/        unit + repository-invariant tests      Source/zWYU.PatchAudit/  real-assembly patch audit tool
docs/  PORTING_NOTES.md  LIBRARY_AUDIT.md  TEST_PLAN.md
```

## Attribution & license

zWYU is a **derived work** of *While You're Up* and uses parts of the *CodeOptimist* RimWorld library, both by
**Christopher S. Galpin (CodeOptimist)**, © 2020, licensed **GNU AGPL-3.0-or-later**. zWYU is therefore also **AGPL-3.0-or-later**
(modifications © 2026 D3athAn63l). Full text in [`LICENSE`](LICENSE); provenance, exact upstream commits, and the original author's note to
Ludeon are in [`NOTICE`](NOTICE). This repository is the complete corresponding source of the distributed `zWYU.dll`.

The original Steam Workshop item is a different project: its PublishedFileId is deliberately not reused, and zWYU has its own package id (`D3athAn63l.zWYU`).
RimWorld and its assemblies are © Ludeon Studios; Harmony © Andreas Pardeike (MIT).
