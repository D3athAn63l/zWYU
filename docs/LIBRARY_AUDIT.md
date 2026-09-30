# CodeOptimist library: what While You're Up actually needs

Original: <https://github.com/CodeOptimist/rimworld-codeoptimist-library> @ `892b8b1` (AGPL-3.0-or-later, © 2020 Christopher S. Galpin).
The original mod referenced it as a project and ILRepack-merged it (internalized) into its own DLL.

**Finding worth knowing:** WYU 4.0.4 (`1c54e7c`, Aug 2023) does **not** build from the public library snapshot (Jun 2023). It uses
`NonThingFilter`, `Listing_TreeNonThingFilter`, `NonThingFilter_LoadingContext` (the library only has the older `SettingsThingFilter` variants) and
`WeakDictionary`, none of which exist in the public library (they **are** present in the original's shipped 1.4 / 1.5 DLLs, which contain a newer library build; see `PORTING_NOTES.md` §12.1), and its translation files use `Label_*/Tooltip_*` keys where the library's `Gui` expects `SettingTitle_*/SettingDesc_*`.
So the "merge the upstream library" route was not available even before RimWorld 1.6; the dependency had to be re-derived from *use*.

## Dependency graph (used by WYU → library)

| Library file / API | Used by WYU for | Decision |
|---|---|---|
| `Gui.cs`: `DrawContext`, `Title/Desc`, `DrawBool/Float/Percent/Int/Enum` | every settings widget | **Retained** → `Source/zWYU/Lib/CodeOptimist/Gui.cs`, adapted (internal; `Label_/Tooltip_` key convention; missing tooltip ⇒ empty; enum name guard). Header credits the original. |
| `Extensions.cs`: `ModTranslate`, `Squared` | keyed translations; squared ranges | **Retained** → `Lib/CodeOptimist/Extensions.cs` (`Squared` is now also expressed locally in the pure `TripRules`). |
| `Patch.cs`: `Patch.Continue()/Halt()` | readability sugar in PUAH prefixes | **Not needed** (PUAH+ not in baseline; nothing uses it). |
| `Transpiler.cs`: `Transpiler`, `CodeInstructionComparer`, `TranspilerHelper` | the two IL transpilers; `ReplaceTypes` for the ThingFilter clones | **Not needed** — both transpilers replaced by semantic hooks (see PORTING_NOTES §4). |
| `SettingsThingFilter.cs`, `Listing_SettingsTreeThingFilter.cs` (+ the non-public `NonThingFilter*` variants WYU 4.0.4 really used) | storage-building allow-list UI via ~35 reverse-patched clones of `ThingFilter` / `Listing_TreeThingFilter` | **Replaced** by `Settings/StorageBuildingFilter` + `StorageFilterPanel` (a defName set and a small checkbox list). Reverse-patch cloning of vanilla UI classes is the most version-fragile thing in the stack. |
| `Diagnostics.cs`, `Toil.cs`, DEBUG-only `StartJob` prefix | developer tracing (patches every method of a mod) | **Not carried.** Replaced by `Diag` (levels, subsystem tags, feature gating). |
| `WeakDictionary<,>` (not in the public library) | per-pawn detour state | **Replaced** by `PawnSlot<T>` over `ConditionalWeakTable` (explicit, non-enumerable, no sweeping needed). |

## What is in the repository, and why

```
Source/zWYU/Lib/CodeOptimist/Gui.cs          adapted copy, AGPL header, "Modified for zWYU" list
Source/zWYU/Lib/CodeOptimist/Extensions.cs   adapted copy, AGPL header
```

Two small files, internal to `zWYU.dll`; no separate library DLL, nothing to install, nothing merged at build time (so no ILRepack step, and no `CodeOptimist.dll` that could collide with another mod that ships the same helpers).
Everything else the original took from the library is either unnecessary in this baseline or was replaced by code that is simpler than what it replaced — not removed for the sake of file count, and not copied "just in case".

Attribution is preserved in three places: the file headers, [`NOTICE`](../NOTICE) (author, repository, exact commit, AGPL grant, the original's note to Ludeon), and the About metadata/README. A repository test (`CodeAdaptedFromTheLibrary_NamesItsOrigin`) fails if the headers lose the author or repository reference.
