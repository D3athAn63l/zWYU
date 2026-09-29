// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Settings window. Ported from `SettingsWindow` / `Dialog_ModSettings` patches in While You're Up's Settings.cs (WYU 4.0.4),
// Copyright (C) 2020 Christopher S. Galpin. Same tabs and same widgets. Not ported: the Pick Up And Haul+ tab/switch (PUAH+ is not
// part of this baseline), the HugsLib/vanilla Dialog_ModSettings Harmony patches (the vanilla ModSettings entry points are enough:
// DrawSpecialHauls is read straight from vanilla's debug flag), and the write-into-Common-Sense's-settings conflict resolution.
// New: a status area that says which features are disabled and why, plus hook counters at debug levels.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeOptimist;
using RimWorld;
using UnityEngine;
using Verse;

namespace zWYU
{
    internal static class SettingsWindow
    {
        enum Tab { Opportunity, OpportunityAdvanced, BeforeCarry }

        static Tab tab = Tab.Opportunity;
        static readonly List<TabRecord> tabsList = new List<TabRecord>(3);

        static readonly StorageFilterPanelState opportunityPanel = new StorageFilterPanelState();
        static readonly StorageFilterPanelState beforeCarryPanel = new StorageFilterPanelState();

        static ZwyuSettings Settings => ZwyuMod.Settings;

        public static void DoWindowContents(Rect windowRect) {
            var settings = Settings;

            var top = new Listing_Standard { ColumnWidth = (float)Math.Round((windowRect.width - 17 * 2) / 3) };
            top.Begin(windowRect);
            top.DrawBool(ref settings.Enabled, nameof(settings.Enabled));
            top.NewColumn();
            var draw = settings.DrawSpecialHauls;
            top.DrawBool(ref draw, nameof(settings.DrawSpecialHauls));
            settings.DrawSpecialHauls = draw;
            top.NewColumn();
            using (new DrawContext { LabelPct = 0.4f })
                top.DrawEnum(settings.DebugLevel, nameof(settings.DebugLevel), v => settings.DebugLevel = v, Text.LineHeight);
            var headerHeight = Mathf.Max(top.MaxColumnHeightSeen, top.CurHeight);
            top.End();

            var notices = BuildNotices();
            var noticeHeight = 0f;
            if (notices.Length > 0) {
                var noticeRect = new Rect(windowRect.x, windowRect.y + headerHeight + 4f, windowRect.width, 0f);
                noticeHeight = Text.CalcHeight(notices, windowRect.width);
                noticeRect.height = noticeHeight;
                using (new DrawContext { TextFont = GameFont.Tiny })
                    Widgets.Label(noticeRect, notices);
                noticeHeight += 4f;
            }

            tabsList.Clear();
            tabsList.Add(new TabRecord("Opportunity_Tab".ModTranslate(), () => tab = Tab.Opportunity, tab == Tab.Opportunity));
            if (settings.Opportunity_TweakVanilla)
                tabsList.Add(new TabRecord("OpportunityAdvanced_Tab".ModTranslate(), () => tab = Tab.OpportunityAdvanced, tab == Tab.OpportunityAdvanced));
            else if (tab == Tab.OpportunityAdvanced)
                tab = Tab.Opportunity;
            tabsList.Add(new TabRecord("HaulBeforeCarry_Tab".ModTranslate(), () => tab = Tab.BeforeCarry, tab == Tab.BeforeCarry));

            var tabRect = windowRect.AtZero(); // top left of windowRect, because we're drawing inside it
            tabRect.yMin   += headerHeight + noticeHeight + 12f + 30f; // gap & room for tab label row
            tabRect.height -= 12f + 30f;                                // room for bottom gap & restore button
            Widgets.DrawMenuSection(tabRect);
            TabDrawer.DrawTabs(tabRect, tabsList, 1, null);
            tabsList.Clear();

            var inner = tabRect.ContractedBy(17f);
            inner.y  += 0f;
            switch (tab) {
                case Tab.Opportunity:         DrawOpportunityTab(inner, settings);         break;
                case Tab.OpportunityAdvanced: DrawOpportunityAdvancedTab(inner, settings); break;
                case Tab.BeforeCarry:         DrawBeforeCarryTab(inner, settings);         break;
            }

            var bottom = windowRect.AtZero();
            bottom.yMin += tabRect.yMax + 6f;
            var restore = new Listing_Standard { ColumnWidth = (float)Math.Round((windowRect.width - 17 * 2) / 3) };
            restore.Begin(bottom);
            if (Widgets.ButtonText(restore.GetRect(30f), "RestoreToDefaultSettings".Translate())) {
                settings.RestoreDefaults();
                opportunityPanel.Search.filter.Text = string.Empty;
                beforeCarryPanel.Search.filter.Text = string.Empty;
            }
            restore.End();
        }

        static void DrawOpportunityTab(Rect inner, ZwyuSettings settings) {
            var listing = new Listing_Standard { ColumnWidth = (float)Math.Round((inner.width - 17) / 2) };
            listing.Begin(inner);
            listing.Label("Opportunity_Intro".ModTranslate());
            listing.Gap();

            using (new DrawContext { LabelPct = 0.25f })
                listing.DrawEnum(settings.Opportunity_PathChecker, nameof(settings.Opportunity_PathChecker), v => settings.Opportunity_PathChecker = v, Text.LineHeight * 2);
            listing.Gap();
            listing.DrawBool(ref settings.Opportunity_TweakVanilla, nameof(settings.Opportunity_TweakVanilla));

            listing.NewColumn();
            listing.Label("Opportunity_Tab".ModTranslate());
            listing.GapLine();
            var manual = !settings.Opportunity_AutoBuildings;
            listing.DrawBool(ref manual, nameof(settings.Opportunity_AutoBuildings));
            settings.Opportunity_AutoBuildings = !manual;
            listing.Gap(4f);

            // what we Began on, minus CurHeight, minus 2 lines
            var panelRect = listing.GetRect(inner.height - listing.CurHeight - Text.LineHeight * 2);
            StorageFilterPanel.Draw(panelRect, settings.Opportunity_BuildingFilter, editable: !settings.Opportunity_AutoBuildings, opportunityPanel);

            listing.GapLine();
            listing.DrawBool(ref settings.Opportunity_ToStockpiles, nameof(settings.Opportunity_ToStockpiles));
            listing.End();
        }

        static void DrawOpportunityAdvancedTab(Rect inner, ZwyuSettings settings) {
            const float labelPct = 0.75f;
            var listing = new Listing_Standard();
            listing.Begin(inner);

            listing.Label("OpportunityAdvanced_Text1".ModTranslate());
            using (new DrawContext { TextAnchor = TextAnchor.MiddleRight, LabelPct = labelPct }) {
                listing.DrawPercent(ref settings.Opportunity_MaxNewLegsPctOrigTrip, nameof(settings.Opportunity_MaxNewLegsPctOrigTrip));
                listing.DrawPercent(ref settings.Opportunity_MaxTotalTripPctOrigTrip, nameof(settings.Opportunity_MaxTotalTripPctOrigTrip));
            }

            listing.Gap();
            listing.GapLine();
            listing.Gap();

            listing.Label("OpportunityAdvanced_Text2".ModTranslate());
            using (new DrawContext { TextAnchor = TextAnchor.MiddleRight, LabelPct = labelPct }) {
                listing.DrawFloat(ref settings.Opportunity_MaxStartToThing, nameof(settings.Opportunity_MaxStartToThing));
                listing.DrawFloat(ref settings.Opportunity_MaxStoreToJob, nameof(settings.Opportunity_MaxStoreToJob));
                listing.DrawPercent(ref settings.Opportunity_MaxStartToThingPctOrigTrip, nameof(settings.Opportunity_MaxStartToThingPctOrigTrip));
                listing.DrawPercent(ref settings.Opportunity_MaxStoreToJobPctOrigTrip, nameof(settings.Opportunity_MaxStoreToJobPctOrigTrip));
            }

            listing.Gap();
            listing.GapLine();
            listing.Gap();

            listing.Label("OpportunityAdvanced_Text3".ModTranslate());
            using (new DrawContext { TextAnchor = TextAnchor.MiddleRight, LabelPct = labelPct }) {
                listing.DrawInt(ref settings.Opportunity_MaxStartToThingRegionLookCount, nameof(settings.Opportunity_MaxStartToThingRegionLookCount));
                listing.DrawInt(ref settings.Opportunity_MaxStoreToJobRegionLookCount, nameof(settings.Opportunity_MaxStoreToJobRegionLookCount));
            }

            listing.End();
        }

        static void DrawBeforeCarryTab(Rect inner, ZwyuSettings settings) {
            var listing = new Listing_Standard { ColumnWidth = (float)Math.Round((inner.width - 17) / 2) };
            listing.Begin(inner);
            listing.Label("HaulBeforeCarry_Intro".ModTranslate());
            listing.DrawBool(ref settings.HaulBeforeCarry_Supplies, nameof(settings.HaulBeforeCarry_Supplies));
            listing.DrawBool(ref settings.HaulBeforeCarry_Bills, nameof(settings.HaulBeforeCarry_Bills));
            listing.Gap();
            listing.Label("HaulBeforeCarry_EqualPriority".ModTranslate());
            listing.DrawBool(ref settings.HaulBeforeCarry_ToEqualPriority, nameof(settings.HaulBeforeCarry_ToEqualPriority));

            listing.NewColumn();
            listing.Label("HaulBeforeCarry_Tab".ModTranslate());
            listing.GapLine();
            var manual = !settings.HaulBeforeCarry_AutoBuildings;
            listing.DrawBool(ref manual, nameof(settings.HaulBeforeCarry_AutoBuildings));
            settings.HaulBeforeCarry_AutoBuildings = !manual;
            listing.Gap(4f);

            var panelRect = listing.GetRect(inner.height - listing.CurHeight - Text.LineHeight * 2);
            StorageFilterPanel.Draw(panelRect, settings.HaulBeforeCarry_BuildingFilter, editable: !settings.HaulBeforeCarry_AutoBuildings, beforeCarryPanel);

            listing.GapLine();
            listing.DrawBool(ref settings.HaulBeforeCarry_ToStockpiles, nameof(settings.HaulBeforeCarry_ToStockpiles));
            listing.End();
        }

        /// <summary>Status lines under the header: disabled features (always), compat notes, and hook counters at debug levels.</summary>
        static string BuildNotices() {
            var sb = new StringBuilder();

            foreach (var disabled in Features.DisabledFeatures())
                sb.AppendLine("<color=#ff8080>" + "FeatureDisabled".ModTranslate(disabled.Key.ToString().Named("FEATURE"), disabled.Value.Named("REASON")) + "</color>");

            if (PickUpAndHaulCompat.Detected)
                sb.AppendLine("<color=#999999>" + "PickUpAndHaul_Note".ModTranslate() + "</color>");

            if (Settings.DebugLevel != DebugLevel.Off) {
                sb.AppendLine("<color=#999999>" + "Diagnostics_Counters".ModTranslate(
                    OpportunityContext.Entered.Named("ENTERED"), OpportunityContext.Engaged.Named("ENGAGED"), OpportunityContext.Found.Named("FOUND")) + "</color>");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
