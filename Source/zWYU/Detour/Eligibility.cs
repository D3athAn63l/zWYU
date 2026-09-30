// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Pawn eligibility for a detour. Derived from the guard clauses scattered through While You're Up 4.0.4
// (`AmBleeding`, the caravan-job list, `BeforeSupplyDetour_Job`'s work-tag check, `AlreadyHauling`), Copyright (C) 2020 Christopher S. Galpin.

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace zWYU
{
    internal static class Eligibility
    {
        // Vanilla decides who may take an opportunistic job in a private static method. We call vanilla's own rule when it exists
        // (so future vanilla changes apply automatically) and fall back to an equivalent copy of the 1.6 rule otherwise.
        static Func<Pawn, bool> vanillaCanTakeOpportunisticJob;
        static bool             resolvedVanillaRule;

        static JobDef[] prepareCaravanJobDefs;

        /// <summary>
        /// Full pawn check for detours that do NOT pass through vanilla's TryOpportunisticJob preconditions (construction supplies).
        /// Mirrors what vanilla requires of an opportunistic haul so zWYU never hauls in a situation vanilla itself would forbid.
        /// </summary>
        public static bool PawnMayDetour(Pawn pawn) {
            if (pawn == null || !pawn.Spawned || pawn.Map == null) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;
            if ((int)pawn.RaceProps.intelligence < (int)Intelligence.Humanlike) return false;
            if (!VanillaCanTakeOpportunisticJob(pawn)) return false;
            if (pawn.WorkTagIsDisabled(WorkTags.ManualDumb | WorkTags.Hauling | WorkTags.AllWork)) return false;
            if (ModsConfig.BiotechActive && pawn.IsWorkTypeDisabledByAge(WorkTypeDefOf.Hauling, out _)) return false;
            if (PickUpAndHaulCompat.HasPendingInventoryHaul(pawn)) return false;
            return true;
        }

        /// <summary>Vanilla: not drafted, not downed, not subhuman, not in a mental state, not burning, not rebelling.</summary>
        static bool VanillaCanTakeOpportunisticJob(Pawn pawn) {
            if (!resolvedVanillaRule) {
                resolvedVanillaRule = true;
                try {
                    var method = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), "CanPawnTakeOpportunisticJob", new[] { typeof(Pawn) });
                    if (method != null && method.IsStatic && method.ReturnType == typeof(bool))
                        vanillaCanTakeOpportunisticJob = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), method);
                } catch (Exception e) {
                    Diag.WarnOnce(Diag.Compat, "could not bind vanilla Pawn_JobTracker.CanPawnTakeOpportunisticJob; using an equivalent copy. " + e.Message);
                }
            }
            if (vanillaCanTakeOpportunisticJob != null)
                return vanillaCanTakeOpportunisticJob(pawn);

            if (pawn.Drafted || pawn.Downed) return false;
            if (ModsConfig.AnomalyActive && pawn.IsSubhuman) return false;
            if (pawn.InMentalState || pawn.IsBurning()) return false;
            if (SlaveRebellionUtility.IsRebelling(pawn)) return false;
            return true;
        }

        /// <summary>zWYU skips ordinary opportunities while bleeding (original :Bleeding) - but still allows ingredient/supply hauls, see callers.</summary>
        public static bool IsBleeding(Pawn pawn) => pawn.health.hediffSet.BleedRateTotal > 0.001f;

        /// <summary>zWYU skips opportunities while preparing a caravan (original v1.1.0/v2.3.0/v3.2.0).</summary>
        public static bool IsCaravanPreparationJob(JobDef def) {
            prepareCaravanJobDefs ??= new[] {
                JobDefOf.PrepareCaravan_CollectAnimals, JobDefOf.PrepareCaravan_GatherAnimals,
                JobDefOf.PrepareCaravan_GatherDownedPawns, JobDefOf.PrepareCaravan_GatherItems,
            };
            return Array.IndexOf(prepareCaravanJobDefs, def) >= 0;
        }
    }
}
