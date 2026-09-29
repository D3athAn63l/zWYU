// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Replaces the original mod's ThingFilter-clone based "ModFilter" (CodeOptimist library SettingsThingFilter, which the public library
// snapshot does not even contain in its WYU-4.0.4 form). A storage-building allow-list only needs a set of def names.
// The behavior (per-building permit/deny, auto-managed defaults vs manual list) is the original's; the implementation is new.

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace zWYU
{
    /// <summary>A set of allowed storage-building defs, persisted by defName (so it can be read before defs are loaded).</summary>
    internal sealed class StorageBuildingFilter : IExposable
    {
        readonly HashSet<string> allowed = new HashSet<string>();

        /// <summary>True once the user (or a first-time copy of the defaults) has populated this manual list.</summary>
        public bool Initialized { get; private set; }

        public int Count => allowed.Count;

        public bool Allows(ThingDef def) => def != null && allowed.Contains(def.defName);

        public bool Allows(string defName) => defName != null && allowed.Contains(defName);

        public void SetAllow(ThingDef def, bool allow) {
            if (def == null) return;
            if (allow) allowed.Add(def.defName);
            else allowed.Remove(def.defName);
            Initialized = true;
        }

        public void CopyFrom(StorageBuildingFilter other) {
            allowed.Clear();
            allowed.UnionWith(other.allowed);
            Initialized = true;
        }

        public void Clear() {
            allowed.Clear();
            Initialized = false;
        }

        public void ExposeData() {
            var initialized = Initialized;
            Scribe_Values.Look(ref initialized, "initialized", false);

            List<string> names = Scribe.mode == LoadSaveMode.Saving ? allowed.OrderBy(x => x).ToList() : null;
            Scribe_Collections.Look(ref names, "allowedDefs", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars) {
                allowed.Clear();
                if (names != null)
                    allowed.UnionWith(names.Where(n => !n.NullOrEmpty()));
                Initialized = initialized;
            }
        }
    }
}
