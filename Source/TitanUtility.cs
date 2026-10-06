using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Public API for content mods. Everything a dungeon mod needs goes through here:
    /// making a titan, adding/removing/rolling traits, discovery, inheritance.
    /// </summary>
    public static class TitanUtility
    {
        // Pawn -> titan hediff. Weak keys, so destroyed pawns are collected normally.
        // Kept so the name patch (called every time a label is drawn) never has to
        // walk the hediff list.
        private static readonly ConditionalWeakTable<Pawn, Hediff_Titan> registry = new ConditionalWeakTable<Pawn, Hediff_Titan>();

        internal static void Register(Pawn pawn, Hediff_Titan titan)
        {
            if (pawn == null) return;
            registry.Remove(pawn);
            registry.Add(pawn, titan);
        }

        internal static void Unregister(Pawn pawn)
        {
            if (pawn != null) registry.Remove(pawn);
        }

        public static Hediff_Titan GetTitan(Pawn pawn)
        {
            if (pawn == null || !registry.TryGetValue(pawn, out var t)) return null;
            // Pawn_HealthTracker.Reset (pawn generation retries) clears the hediff set
            // without calling PostRemoved, which can leave a stale entry behind. The
            // check only runs for registered titans, so ordinary pawns never pay it.
            if (!pawn.health.hediffSet.hediffs.Contains(t))
            {
                Unregister(pawn);
                return null;
            }
            return t;
        }

        public static bool IsTitan(Pawn pawn) => GetTitan(pawn) != null;

        /// <summary>GetTitan without the stale-entry cleanup, safe to call from the
        /// parallel pre-draw threads (ConditionalWeakTable reads are thread-safe; the
        /// Remove in GetTitan is not safe to race).</summary>
        internal static Hediff_Titan PeekTitan(Pawn pawn)
        {
            if (pawn == null || !registry.TryGetValue(pawn, out var t)) return null;
            return t.pawn == pawn && t.pawn.health?.hediffSet?.hediffs.Contains(t) == true ? t : null;
        }

        public static Hediff_Titan GetOrMakeTitan(Pawn pawn)
        {
            var titan = GetTitan(pawn);
            if (titan != null) return titan;
            titan = (Hediff_Titan)HediffMaker.MakeHediff(TitanDefOf.TF_Titan, pawn);
            pawn.health.AddHediff(titan);
            return titan;
        }

        /// <summary>Can this trait go on this pawn right now (filters, duplicates, exclusions)?</summary>
        public static bool CanAddTrait(Pawn pawn, TitanTraitDef def)
        {
            if (pawn == null || def == null || !def.AllowedOn(pawn)) return false;
            var titan = GetTitan(pawn);
            if (titan == null) return true;
            // A creature made of stone has no meat, blood or pain for such a trait to work on.
            if (def.disallowConstructs && titan.IsConstruct) return false;
            foreach (var t in titan.TraitsForReading)
                if (t.def.ConflictsWith(def)) return false;
            return true;
        }

        /// <summary>Adds a trait, making the pawn a titan if needed. discovered=true
        /// starts it identified; visible traits are always identified.
        /// Returns null if the trait cannot be added.
        /// Do not call this (or RemoveTrait) from a TitanTrait's Notify_PawnUsedVerb,
        /// Notify_PawnPostApplyDamage or PostDiscovered: vanilla is iterating the
        /// pawn's hediffs at that point and adding or removing one throws. Queue the
        /// change and apply it from TickInterval instead.</summary>
        public static TitanTrait AddTrait(Pawn pawn, TitanTraitDef def, bool discovered = false, bool force = false)
        {
            if (pawn?.health == null || def == null) return null;
            if (!force && !CanAddTrait(pawn, def)) return null;
            if (force && GetTitan(pawn)?.HasTrait(def) == true) return null;
            return GetOrMakeTitan(pawn).AddTraitInternal(def, discovered);
        }

        public static bool RemoveTrait(Pawn pawn, TitanTraitDef def)
        {
            var titan = GetTitan(pawn);
            var trait = titan?.GetTrait(def);
            if (trait == null) return false;
            titan.RemoveTraitInternal(trait);
            return true;
        }

        /// <summary>Removes every trait and the titan hediff itself.</summary>
        public static void ClearTitan(Pawn pawn)
        {
            var titan = GetTitan(pawn);
            if (titan != null) pawn.health.RemoveHediff(titan);
        }

        public static void DiscoverAll(Pawn pawn, bool notify = false)
        {
            var titan = GetTitan(pawn);
            if (titan == null) return;
            foreach (var t in titan.TraitsForReading.ToList()) t.Discover(notify);
        }

        /// <summary>
        /// Rolls traits weighted by commonality, spending pointBudget. Keeps rolling
        /// while points remain and something fits; a drawback (negative cost) gives
        /// points back. Existing traits on the pawn are kept and still block
        /// conflicting rolls, but do not count against the budget.
        /// </summary>
        /// <param name="validator">Extra filter, e.g. d => d.tags.Contains("Fire").</param>
        public static List<TitanTrait> RollTraits(Pawn pawn, float pointBudget, int maxTraits = 99,
            Predicate<TitanTraitDef> validator = null)
        {
            var added = new List<TitanTrait>();
            float remaining = pointBudget;
            var all = DefDatabase<TitanTraitDef>.AllDefsListForReading;
            while (remaining > 0f && added.Count < maxTraits)
            {
                float budget = remaining;
                var candidates = all.Where(d => d.commonality > 0f && d.pointCost <= budget
                    && (validator == null || validator(d)) && CanAddTrait(pawn, d));
                if (!candidates.TryRandomElementByWeight(d => d.commonality, out var pick)) break;
                var trait = AddTrait(pawn, pick);
                if (trait == null) break;
                added.Add(trait);
                remaining -= pick.pointCost;
            }
            return added;
        }

        /// <summary>
        /// Gives a newborn traits from its parents. Each heritable trait carried by a
        /// parent passes with that trait's inheritChance per carrying parent. An
        /// inherited trait starts discovered if it was discovered on either parent.
        /// </summary>
        public static void InheritTraits(Pawn child, Pawn mother, Pawn father)
        {
            var m = GetTitan(mother);
            var f = GetTitan(father);
            if (child == null || (m == null && f == null)) return;

            // Material first, so flesh-only traits are then refused on a construct.
            // From whichever parent has one; a coin flip when both do.
            ThingDef mm = m?.Material, fm = f?.Material;
            ThingDef mat = mm != null && fm != null ? (Rand.Bool ? mm : fm) : mm ?? fm;
            if (mat != null) SetMaterial(child, mat);

            var carriers = new Dictionary<TitanTraitDef, int>();
            var known = new HashSet<TitanTraitDef>();
            void Count(Hediff_Titan t)
            {
                if (t == null) return;
                foreach (var tr in t.TraitsForReading)
                {
                    // Material traits come with the material, not on their own.
                    if (!tr.def.heritable || tr.fromMaterial) continue;
                    carriers.TryGetValue(tr.def, out int n);
                    carriers[tr.def] = n + 1;
                    if (tr.discovered) known.Add(tr.def);
                }
            }
            Count(m);
            Count(f);

            // Random order so exclusion conflicts between the two parents' traits
            // are not always settled in the mother's favour.
            foreach (var kv in carriers.InRandomOrder())
            {
                float chance = 1f - Mathf.Pow(1f - Mathf.Clamp01(kv.Key.inheritChance), kv.Value);
                if (Rand.Chance(chance))
                    AddTrait(child, kv.Key, discovered: known.Contains(kv.Key));
            }
        }

        // ---- materials -----------------------------------------------------------

        public static ThingDef GetMaterial(Pawn pawn) => GetTitan(pawn)?.Material;

        public static bool IsConstruct(Pawn pawn) => GetTitan(pawn)?.IsConstruct ?? false;

        /// <summary>Makes the creature out of a stuff (any stuff is accepted here; only
        /// rolling is limited to metal, stone and wood). Pass null to make it flesh again.
        /// Makes the pawn a titan if needed.</summary>
        public static bool SetMaterial(Pawn pawn, ThingDef stuff)
        {
            if (pawn?.health == null) return false;
            if (stuff != null && !stuff.IsStuff) return false;
            if (stuff == null)
            {
                GetTitan(pawn)?.SetMaterialInternal(null);
                return true;
            }
            GetOrMakeTitan(pawn).SetMaterialInternal(stuff);
            return true;
        }

        /// <summary>Picks a metal, stone or wood stuff weighted by the stuff's commonality
        /// (and the TitanMaterialExtension's) and applies it. Returns the stuff, or null if
        /// nothing passed the validator.</summary>
        /// <param name="validator">Extra filter, e.g. s => !TitanMaterialUtility.IsFlammable(s).</param>
        public static ThingDef RollMaterial(Pawn pawn, Predicate<ThingDef> validator = null)
        {
            var candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => TitanMaterialUtility.IsRollable(d) && (validator == null || validator(d)));
            if (!candidates.TryRandomElementByWeight(TitanMaterialUtility.RollWeight, out var pick)) return null;
            return SetMaterial(pawn, pick) ? pick : null;
        }

        /// <summary>Discovery only counts when the player could see it happen.</summary>
        public static bool PlayerCanObserve(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.Spawned) return !pawn.Position.Fogged(pawn.Map);
            // A corpse lying in view counts for death-time discovery.
            if (pawn.Dead && pawn.Corpse != null && pawn.Corpse.Spawned)
                return !pawn.Corpse.Position.Fogged(pawn.Corpse.Map);
            return false;
        }

        public static void EnsureAbilityTracker(Pawn pawn)
        {
            if (pawn.abilities == null) pawn.abilities = new Pawn_AbilityTracker(pawn);
        }
    }

    [DefOf]
    public static class TitanDefOf
    {
        public static HediffDef TF_Titan;

        static TitanDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(TitanDefOf));
    }
}
