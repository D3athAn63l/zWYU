// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Adapted from CodeOptimist's RimWorld library (Extensions.cs)
//   https://github.com/CodeOptimist/rimworld-codeoptimist-library  @ 892b8b107a15b4123c8f547c3a4e23b0ce7dc264
//   Copyright (C) 2020  Christopher S. Galpin
// Modified for zWYU: types made internal (this assembly is standalone; nothing is merged/exposed),
// and `ModTranslate` now takes its key prefix from `Gui.modId` as before but tolerates missing keys.
// See NOTICE and docs/LIBRARY_AUDIT.md.

using System.Runtime.CompilerServices;
using Verse;

namespace CodeOptimist
{
    internal static class Extensions
    {
        // `Resolve()` supports colored text e.g. `<Threat>text</Threat>`
        public static string ModTranslate(this string key, params NamedArgument[] args) => $"{Gui.modId}_{key}".Translate(args).Resolve();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Squared(this float val) => val * val;
    }
}
