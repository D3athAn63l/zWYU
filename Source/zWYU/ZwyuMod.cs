// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Mod entry point. Derived from While You're Up's `Mod` class (Mod.cs, WYU 4.0.4), Copyright (C) 2020 Christopher S. Galpin.
// The original mod class also held the whole PUAH/CommonSense reflection table, `harmony.PatchAll()` and the Hugs settings hooks;
// here those responsibilities are split into Compat/*, PatchBootstrap and Settings/*.

using System;
using CodeOptimist;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace zWYU
{
    /// <summary>zWYU: While You're Up for RimWorld 1.6.</summary>
    public sealed class ZwyuMod : Mod
    {
        /// <summary>Prefix of this mod's Keyed translations ("zWYU_Label_Enabled" ...).</summary>
        internal const string ModId = "zWYU";

        internal static ZwyuMod      Instance;
        internal static ZwyuSettings Settings;

        internal static readonly Harmony Harmony = new Harmony("D3athAn63l.zWYU");

        public ZwyuMod(ModContentPack content) : base(content) {
            Instance = this;
            Gui.modId = ModId;

            try {
                Settings = GetSettings<ZwyuSettings>();
                Settings.Sanitize();
            } catch (Exception e) {
                // A broken config must never stop the game from loading: fall back to defaults.
                Diag.Error(Diag.Startup, "could not read zWYU settings; using defaults.", e);
                Settings = new ZwyuSettings();
            }

            PickUpAndHaulCompat.Init();
            CommonSenseCompat.Init();
            PatchBootstrap.Apply(Harmony);

            Diag.Info(Diag.Startup, PatchBootstrap.StatusLine());
        }

        // name in "Mod options" and top of settings window
        public override string SettingsCategory() => "zWYU (While You're Up)";

        public override void DoSettingsWindowContents(Rect inRect) => SettingsWindow.DoWindowContents(inRect);
    }
}
