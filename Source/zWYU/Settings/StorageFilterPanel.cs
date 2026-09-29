// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Settings-window widget for the storage-building allow-lists. Replaces the original's Listing_TreeModFilter (a ThingFilter tree clone
// from the CodeOptimist library). Same idea: every storage building def, grouped by the mod that provides it, searchable, with
// per-mod and per-building check boxes.

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace zWYU
{
    internal sealed class StorageFilterPanelState
    {
        public Vector2 Scroll;
        public readonly QuickSearchWidget Search = new QuickSearchWidget();
    }

    internal static class StorageFilterPanel
    {
        const float RowHeight   = 24f;
        const float SearchHeight = 24f;

        /// <param name="editable">False while the auto-managed defaults are shown: the boxes are drawn but cannot be changed.</param>
        public static void Draw(Rect rect, StorageBuildingFilter filter, bool editable, StorageFilterPanelState state) {
            state.Search.OnGUI(new Rect(rect.x, rect.y, rect.width, SearchHeight));

            var listRect = new Rect(rect.x, rect.y + SearchHeight + 4f, rect.width, rect.height - SearchHeight - 4f);
            var groups   = StorageBuildingCatalog.AllStorageDefs
                .Where(d => state.Search.filter.Matches(d.LabelCap.ToString()) || state.Search.filter.Matches(StorageBuildingCatalog.ModNameOf(d)))
                .GroupBy(StorageBuildingCatalog.ModNameOf)
                .OrderBy(g => g.Key == "Core" ? 0 : 1)
                .ThenBy(g => g.Key)
                .ToList();

            var rows     = groups.Sum(g => 1 + g.Count());
            var viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(rows * RowHeight, listRect.height));
            Widgets.BeginScrollView(listRect, ref state.Scroll, viewRect);

            var y = 0f;
            foreach (var group in groups) {
                var defs = group.OrderBy(d => d.label).ToList();

                // mod header: tri-state check box for the whole mod
                var allowedCount = defs.Count(filter.Allows);
                var state3       = allowedCount == 0 ? MultiCheckboxState.Off : allowedCount == defs.Count ? MultiCheckboxState.On : MultiCheckboxState.Partial;
                var headerRect   = new Rect(0f, y, viewRect.width, RowHeight);
                Widgets.Label(new Rect(headerRect.x + RowHeight + 4f, headerRect.y, headerRect.width - RowHeight - 4f, RowHeight), $"<b>{group.Key}</b>");
                if (editable) {
                    var next = Widgets.CheckboxMulti(new Rect(headerRect.x, headerRect.y, RowHeight, RowHeight), state3);
                    if (next != state3) {
                        var allow = state3 == MultiCheckboxState.Off; // Off -> everything on; On/Partial -> everything off
                        foreach (var def in defs) filter.SetAllow(def, allow);
                    }
                } else {
                    Widgets.CheckboxMulti(new Rect(headerRect.x, headerRect.y, RowHeight, RowHeight), state3);
                }
                y += RowHeight;

                foreach (var def in defs) {
                    var on = filter.Allows(def);
                    var rowRect = new Rect(RowHeight, y, viewRect.width - RowHeight, RowHeight);
                    if (editable) {
                        var before = on;
                        Widgets.CheckboxLabeled(rowRect, def.LabelCap, ref on);
                        if (on != before) filter.SetAllow(def, on);
                    } else {
                        Widgets.CheckboxLabeled(rowRect, def.LabelCap, ref on, disabled: true);
                    }
                    y += RowHeight;
                }
            }

            Widgets.EndScrollView();
        }
    }
}
