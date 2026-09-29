// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Adapted from CodeOptimist's RimWorld library (Gui.cs)
//   https://github.com/CodeOptimist/rimworld-codeoptimist-library  @ 892b8b107a15b4123c8f547c3a4e23b0ce7dc264
//   Copyright (C) 2020  Christopher S. Galpin
// Modified for zWYU:
//   * types made internal;
//   * setting labels/tooltips use the `Label_<name>` / `Tooltip_<name>` key convention of the original
//     mod's newer translation files instead of `SettingTitle_` / `SettingDesc_`;
//   * a missing tooltip key yields an empty tooltip instead of a red "missing translation" string;
//   * `DrawEnum` guards against an unknown value name.
// See NOTICE and docs/LIBRARY_AUDIT.md.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace CodeOptimist
{
    internal class DrawContext : IDisposable
    {
        public static float guiLabelPct = 0.5f;

        readonly Color      guiColor;
        readonly TextAnchor textAnchor;
        readonly GameFont   textFont;
        readonly float      labelPct;

        public DrawContext() {
            guiColor   = GUI.color;
            textFont   = Text.Font;
            textAnchor = Text.Anchor;
            labelPct   = guiLabelPct;
        }

        public void Dispose() {
            GUI.color   = guiColor;
            Text.Font   = textFont;
            Text.Anchor = textAnchor;
            guiLabelPct = labelPct;
        }

        public Color GuiColor {
            set => GUI.color = value;
        }

        public GameFont TextFont {
            set => Text.Font = value;
        }

        public TextAnchor TextAnchor {
            set => Text.Anchor = value;
        }

        public float LabelPct {
            set => guiLabelPct = value;
        }
    }

    internal static class Gui
    {
        /// <summary>Key prefix of the mod's Keyed translations, e.g. "zWYU". Set once by the mod constructor.</summary>
        public static string modId;

        public static string Title(this string name) => $"{modId}_Label_{name}".Translate().Resolve();

        public static string Desc(this string name) {
            var key = $"{modId}_Tooltip_{name}";
            return key.TryTranslate(out var text) ? text.Resolve() : string.Empty;
        }

        public static void DrawBool(this Listing_Standard list, ref bool value, string name) {
            list.CheckboxLabeled(name.Title(), ref value, name.Desc());
        }

        static void NumberLabel(this Listing_Standard list, Rect rect, float value, string format, string name, out string buffer) {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 8, rect.height), name.Title());
            buffer = value.ToString(format);
            list.Gap(list.verticalSpacing);

            var tooltip = name.Desc();
            if (!tooltip.NullOrEmpty()) {
                if (Mouse.IsOver(rect))
                    Widgets.DrawHighlight(rect);
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        public static void DrawFloat(this Listing_Standard list, ref float value, string name) {
            var rect = list.GetRect(Text.LineHeight);
            list.NumberLabel(rect.LeftPart(DrawContext.guiLabelPct), value, "f1", name, out var buffer);
            Widgets.TextFieldNumeric(rect.RightPart(1 - DrawContext.guiLabelPct), ref value, ref buffer, 0f, 999f);
        }

        public static void DrawPercent(this Listing_Standard list, ref float value, string name) {
            var rect = list.GetRect(Text.LineHeight);
            list.NumberLabel(rect.LeftPart(DrawContext.guiLabelPct), value * 100, "n0", name, out var buffer);
            Widgets.TextFieldPercent(rect.RightPart(1 - DrawContext.guiLabelPct), ref value, ref buffer, 0f, 10f);
        }

        public static void DrawInt(this Listing_Standard list, ref int value, string name) {
            var rect = list.GetRect(Text.LineHeight);
            list.NumberLabel(rect.LeftPart(DrawContext.guiLabelPct), value, "n0", name, out var buffer);
            Widgets.IntEntry(rect.RightPart(1 - DrawContext.guiLabelPct), ref value, ref buffer);
        }

        public static void DrawEnum<T>(this Listing_Standard list, T value, string name, Action<T> setValue, float height = 30f) {
            var rect    = list.GetRect(height);
            var tooltip = name.Desc();
            if (!tooltip.NullOrEmpty()) {
                if (Mouse.IsOver(rect.LeftPart(DrawContext.guiLabelPct)))
                    Widgets.DrawHighlight(rect);
                TooltipHandler.TipRegion(rect.LeftPart(DrawContext.guiLabelPct), tooltip);
            }

            Widgets.Label(rect.LeftPart(DrawContext.guiLabelPct), name.Title());
            var valueName = Enum.GetName(typeof(T), value) ?? value.ToString();
            if (Widgets.ButtonText(rect.RightPart(1 - DrawContext.guiLabelPct), $"{name}_{valueName}".Title())) {
                var menuOptions = new List<FloatMenuOption>();
                foreach (var enumValue in Enum.GetValues(typeof(T)).Cast<T>()) {
                    var enumValueName = Enum.GetName(typeof(T), enumValue) ?? enumValue.ToString();
                    var captured      = enumValue;
                    menuOptions.Add(new FloatMenuOption($"{name}_{enumValueName}".Title(), () => { setValue(captured); }));
                }

                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }

            list.Gap(list.verticalSpacing);
        }
    }
}
