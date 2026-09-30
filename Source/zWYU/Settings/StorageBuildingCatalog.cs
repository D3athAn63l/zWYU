// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Derived from While You're Up's SettingsWindow.ResetFilters (Settings.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin:
// the per-mod default allow-lists for storage buildings. Package-id lists are the original's (contributed by ZzZombo and others).

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace zWYU
{
    /// <summary>
    /// Knows every storage-building def in the running game (grouped by providing mod) and the auto-managed default allow-lists.
    /// Built lazily, on first use after defs have loaded (never from the mod constructor, which runs before defs exist).
    /// </summary>
    internal static class StorageBuildingCatalog
    {
        // Denied for opportunities by default: storing has a delay, so not really "opportunistic".
        static readonly HashSet<string> OpportunityDenyPackageIds = new HashSet<string> {
            "lwm.deepstorage", // LWM's Deep Storage
        };

        // Determined (by the original author and contributors) to be actual containers, not merely repurposing Building_Storage.
        // These are the only mods whose storage is used, by default, for "haul extra supplies/ingredients closer".
        static readonly HashSet<string> ContainerPackageIds = new HashSet<string> {
            "buddy1913.expandedstorageboxes",      // Buddy's Expanded Storage Boxes
            "im.skye.rimworld.deepstorageplus",    // Deep Storage Plus
            "jangodsoul.simplestorage",            // [JDS] Simple Storage
            "jangodsoul.simplestorage.ref",        // [JDS] Simple Storage - Refrigeration
            "ludeon.rimworld",                     // Core
            "lwm.deepstorage",                     // LWM's Deep Storage
            "mlie.displaycases",                   // Display Cases (Continued)
            "mlie.eggincubator",                   // Egg Incubator
            "mlie.extendedstorage",                // Extended Storage (Continued)
            "mlie.fireextinguisher",               // Fire Extinguisher (Continued)
            "mlie.functionalvanillaexpandedprops", // Functional Vanilla Expanded Props (Continued)
            "mlie.tobesdiningroom",                // Tobe's Dining Room (Continued)
            "ogliss.thewhitecrayon.quarry",        // Quarry
            "primitivestorage.velcroboy333",       // Primitive Storage
            "proxyer.smallshelf",                  // Small Shelf
            "rimfridge.kv.rw",                     // [KV] RimFridge
            "sixdd.littlestorage2",                // Little Storage 2
            "skullywag.extendedstorage",           // Extended Storage
            "solaris.furniturebase",               // GloomyFurniture
            "vanillaexpanded.vfecore",             // Vanilla Furniture Expanded
            "vanillaexpanded.vfeart",              // Vanilla Furniture Expanded - Art
            "vanillaexpanded.vfefarming",          // Vanilla Furniture Expanded - Farming
            "vanillaexpanded.vfespacer",           // Vanilla Furniture Expanded - Spacer Module
            "vanillaexpanded.vfesecurity",         // Vanilla Furniture Expanded - Security
        };

        static List<ThingDef>          allStorageDefs;
        static StorageBuildingFilter   opportunityDefaults;
        static StorageBuildingFilter   haulBeforeCarryDefaults;

        /// <summary>All defs whose thing class is (a subclass of) Building_Storage.</summary>
        public static List<ThingDef> AllStorageDefs {
            get {
                if (allStorageDefs == null) {
                    allStorageDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(d => d.thingClass != null && typeof(Building_Storage).IsAssignableFrom(d.thingClass))
                        .ToList();
                }
                return allStorageDefs;
            }
        }

        public static string ModNameOf(ThingDef def) => def.modContentPack?.Name ?? "?";

        /// <summary>Auto-managed default for opportunistic hauls: everything except known slow-storage mods.</summary>
        public static StorageBuildingFilter OpportunityDefaults {
            get {
                if (opportunityDefaults == null) {
                    opportunityDefaults = new StorageBuildingFilter();
                    foreach (var def in AllStorageDefs) {
                        if (!OpportunityDenyPackageIds.Contains(def.modContentPack?.PackageId ?? string.Empty))
                            opportunityDefaults.SetAllow(def, true);
                    }
                }
                return opportunityDefaults;
            }
        }

        /// <summary>Auto-managed default for haul-before-carry: only mods known to provide real containers.</summary>
        public static StorageBuildingFilter HaulBeforeCarryDefaults {
            get {
                if (haulBeforeCarryDefaults == null) {
                    haulBeforeCarryDefaults = new StorageBuildingFilter();
                    foreach (var def in AllStorageDefs) {
                        if (ContainerPackageIds.Contains(def.modContentPack?.PackageId ?? string.Empty))
                            haulBeforeCarryDefaults.SetAllow(def, true);
                    }
                }
                return haulBeforeCarryDefaults;
            }
        }
    }
}
