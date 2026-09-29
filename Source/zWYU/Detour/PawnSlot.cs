// SPDX-License-Identifier: AGPL-3.0-or-later
// New for the zWYU port. Replaces the original's CodeOptimist `WeakDictionary<Pawn, BaseDetour>` (which is not in the public library).

using System.Runtime.CompilerServices;
using Verse;

namespace zWYU
{
    /// <summary>
    /// One value per pawn, held weakly: the value dies with the pawn even if nobody remembers to remove it,
    /// so per-pawn state can never keep a destroyed pawn (or its map) alive. Not enumerable by design.
    /// </summary>
    internal sealed class PawnSlot<T> where T : class
    {
        readonly ConditionalWeakTable<Pawn, T> table = new ConditionalWeakTable<Pawn, T>();

        public void Set(Pawn pawn, T value) {
            if (pawn == null) return;
            table.Remove(pawn);
            if (value != null)
                table.Add(pawn, value);
        }

        public bool TryGet(Pawn pawn, out T value) {
            value = null;
            return pawn != null && table.TryGetValue(pawn, out value);
        }

        public void Remove(Pawn pawn) {
            if (pawn != null)
                table.Remove(pawn);
        }
    }
}
