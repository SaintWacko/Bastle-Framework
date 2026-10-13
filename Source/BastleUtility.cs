using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Public API for content mods. Everything a dungeon mod needs goes through here:
    /// making a bastle, adding/removing/rolling traits, discovery, inheritance.
    /// </summary>
    public static class BastleUtility
    {
        // Pawn -> bastle hediff, so hot patches (Pawn.HealthScale runs hundreds of times a
        // frame, labels every draw) never walk the hediff list.
        // Copy-on-write: writes (rare: a bastle gained, lost or loaded) build a new
        // dictionary and swap the reference; reads never lock and never see a dictionary
        // mid-change, so the parallel pre-draw threads can read it too. It replaced a
        // ConditionalWeakTable, whose reads take a lock under Mono and were most of the
        // HealthScale postfix's cost. Keys are compared by reference: Thing's hash is its
        // ID, which two games' pawns can share.
        // The keys are strong, so entries are dropped on PostRemoved, pruned by
        // BastleGameComponent, and cleared when a game starts or loads.
        private sealed class RefComparer : IEqualityComparer<Pawn>
        {
            public bool Equals(Pawn a, Pawn b) => ReferenceEquals(a, b);
            public int GetHashCode(Pawn p) => RuntimeHelpers.GetHashCode(p);
        }
        // Declared before registry: static initialisers run in source order.
        private static readonly RefComparer Comparer = new RefComparer();

        private static volatile Dictionary<Pawn, Hediff_Bastle> registry = NewRegistry(null);

        private static Dictionary<Pawn, Hediff_Bastle> NewRegistry(Dictionary<Pawn, Hediff_Bastle> from) =>
            from == null ? new Dictionary<Pawn, Hediff_Bastle>(Comparer) : new Dictionary<Pawn, Hediff_Bastle>(from, Comparer);

        internal static void Register(Pawn pawn, Hediff_Bastle bastle)
        {
            if (pawn == null) return;
            if (registry.TryGetValue(pawn, out var old) && old == bastle) return;
            var next = NewRegistry(registry);
            next[pawn] = bastle;
            registry = next;
        }

        internal static void Unregister(Pawn pawn)
        {
            if (pawn == null || !registry.ContainsKey(pawn)) return;
            var next = NewRegistry(registry);
            next.Remove(pawn);
            registry = next;
        }

        /// <summary>A new game or a load: the previous game's pawns go.</summary>
        internal static void ClearRegistry() => registry = NewRegistry(null);

        /// <summary>Drops destroyed pawns and stale entries. Main thread only.</summary>
        internal static void PruneRegistry()
        {
            List<Pawn> dead = null;
            foreach (var kv in registry)
                if (kv.Key.Discarded || !kv.Value.StillIn(kv.Key))
                    (dead ?? (dead = new List<Pawn>())).Add(kv.Key);
            if (dead == null) return;
            var next = NewRegistry(registry);
            foreach (var p in dead) next.Remove(p);
            registry = next;
        }

        public static Hediff_Bastle GetBastle(Pawn pawn)
        {
            if (pawn == null || !registry.TryGetValue(pawn, out var t)) return null;
            // Pawn_HealthTracker.Reset (pawn generation retries) clears the hediff set
            // without calling PostRemoved, which can leave a stale entry behind. The
            // check only runs for registered bastles, so ordinary pawns never pay it.
            if (!t.StillIn(pawn))
            {
                Unregister(pawn);
                return null;
            }
            return t;
        }

        public static bool IsBastle(Pawn pawn) => GetBastle(pawn) != null;

        /// <summary>GetBastle without the stale-entry cleanup, safe to call from the
        /// parallel pre-draw threads: it only reads the current registry snapshot, while
        /// GetBastle may Unregister, which swaps the snapshot and belongs on the main thread.</summary>
        internal static Hediff_Bastle PeekBastle(Pawn pawn)
        {
            if (pawn == null || !registry.TryGetValue(pawn, out var t)) return null;
            return t.StillIn(pawn) ? t : null;
        }

        public static Hediff_Bastle GetOrMakeBastle(Pawn pawn)
        {
            var bastle = GetBastle(pawn);
            if (bastle != null) return bastle;
            bastle = (Hediff_Bastle)HediffMaker.MakeHediff(BastleDefOf.BF_Bastle, pawn);
            pawn.health.AddHediff(bastle);
            return bastle;
        }

        /// <summary>Can this trait go on this pawn right now (filters, duplicates, exclusions)?</summary>
        public static bool CanAddTrait(Pawn pawn, BastleTraitDef def)
        {
            if (pawn == null || def == null || !def.AllowedOn(pawn)) return false;
            var bastle = GetBastle(pawn);
            if (bastle == null) return true;
            // A creature made of stone has no meat, blood or pain for such a trait to work on.
            if (def.disallowConstructs && bastle.IsConstruct) return false;
            foreach (var t in bastle.TraitsForReading)
                if (t.def.ConflictsWith(def)) return false;
            // A trait that only grants abilities adds nothing when another trait already
            // grants them all (leaping on an Epic swift creature).
            if (def.OnlyGrantsAbilities
                && def.abilities.TrueForAll(a => bastle.TraitsForReading.Any(t => t.Abilities != null && t.Abilities.Contains(a))))
                return false;
            return true;
        }

        /// <summary>Adds a trait, making the pawn a bastle if needed. discovered=true
        /// starts it identified; visible traits are always identified.
        /// Returns null if the trait cannot be added.
        /// Do not call this (or RemoveTrait) from a BastleTrait's Notify_PawnUsedVerb,
        /// Notify_PawnPostApplyDamage or PostDiscovered: vanilla is iterating the
        /// pawn's hediffs at that point and adding or removing one throws. Queue the
        /// change and apply it from TickInterval instead.</summary>
        /// <param name="rarity">For traits that scale with rarity; clamped to what the
        /// trait can be. Null: the def's own rarity.</param>
        public static BastleTrait AddTrait(Pawn pawn, BastleTraitDef def, bool discovered = false, bool force = false, BastleRarity? rarity = null)
        {
            if (pawn?.health == null || def == null) return null;
            if (!force && !CanAddTrait(pawn, def)) return null;
            if (force && GetBastle(pawn)?.HasTrait(def) == true) return null;
            return GetOrMakeBastle(pawn).AddTraitInternal(def, discovered, rarity: rarity);
        }

        public static bool RemoveTrait(Pawn pawn, BastleTraitDef def)
        {
            var bastle = GetBastle(pawn);
            var trait = bastle?.GetTrait(def);
            if (trait == null) return false;
            bastle.RemoveTraitInternal(trait);
            return true;
        }

        /// <summary>Removes every trait and the bastle hediff itself.</summary>
        public static void ClearBastle(Pawn pawn)
        {
            var bastle = GetBastle(pawn);
            if (bastle != null) pawn.health.RemoveHediff(bastle);
        }

        public static void DiscoverAll(Pawn pawn, bool notify = false)
        {
            var bastle = GetBastle(pawn);
            if (bastle == null) return;
            foreach (var t in bastle.TraitsForReading.ToList()) t.Discover(notify);
        }

        /// <summary>Rolls traits until the creature's new traits reach a point target
        /// rolled from request.points (the settings' range by default). The target is a
        /// minimum; see BastleRolling for how it is met. Traits the creature already has
        /// are kept and block conflicting rolls, but don't count toward the target.</summary>
        public static List<BastleTrait> RollTraits(Pawn pawn, BastleRollRequest request = null)
            => BastleRolling.Roll(pawn, request ?? new BastleRollRequest(), null);

        /// <summary>As RollTraits(pawn, request), with a fixed point target.</summary>
        public static List<BastleTrait> RollTraits(Pawn pawn, float points, BastleRollRequest request = null)
            => BastleRolling.Roll(pawn, request ?? new BastleRollRequest(), points);

        /// <summary>
        /// Gives a newborn traits from its parents. Each heritable trait carried by a
        /// parent passes with that trait's inheritChance per carrying parent. An
        /// inherited trait starts discovered if it was discovered on either parent.
        /// </summary>
        public static void InheritTraits(Pawn child, Pawn mother, Pawn father)
        {
            var m = GetBastle(mother);
            var f = GetBastle(father);
            if (child == null || (m == null && f == null)) return;

            // Material first, so disallowConstructs traits are then refused on a construct.
            // From whichever parent has one; a coin flip when both do.
            ThingDef mm = m?.Material, fm = f?.Material;
            ThingDef mat = mm != null && fm != null ? (Rand.Bool ? mm : fm) : mm ?? fm;
            if (mat != null) SetMaterial(child, mat);

            var carriers = new Dictionary<BastleTraitDef, int>();
            var known = new HashSet<BastleTraitDef>();
            var rarities = new Dictionary<BastleTraitDef, List<BastleRarity>>();
            void Count(Hediff_Bastle t)
            {
                if (t == null) return;
                foreach (var tr in t.TraitsForReading)
                {
                    // Material traits come with the material, not on their own.
                    if (!tr.def.heritable || tr.fromMaterial) continue;
                    carriers.TryGetValue(tr.def, out int n);
                    carriers[tr.def] = n + 1;
                    if (tr.discovered) known.Add(tr.def);
                    if (!rarities.TryGetValue(tr.def, out var list)) rarities[tr.def] = list = new List<BastleRarity>();
                    list.Add(tr.Rarity);
                }
            }
            Count(m);
            Count(f);

            // Random order so exclusion conflicts between the two parents' traits
            // are not always settled in the mother's favour.
            foreach (var kv in carriers.InRandomOrder())
            {
                float chance = 1f - Mathf.Pow(1f - Mathf.Clamp01(kv.Key.inheritChance * BastleFrameworkMod.Settings.inheritChanceFactor), kv.Value);
                if (Rand.Chance(chance))
                    AddTrait(child, kv.Key, discovered: known.Contains(kv.Key), rarity: InheritedRarity(kv.Key, rarities[kv.Key]));
            }
        }

        /// <summary>
        /// Makes <paramref name="to"/> a copy of <paramref name="from"/>'s bastle: the same
        /// material and every trait at the same rarity, with each trait's own state copied
        /// through BastleTrait.CopyStateFrom. Whatever bastle state <paramref name="to"/> had is
        /// replaced; if <paramref name="from"/> is not a bastle, <paramref name="to"/> stops being one.
        /// Traits are copied as they are, without the usual race and conflict checks, so the
        /// copy matches even when the target is a different kind of creature. Two exceptions:
        /// a trait that refuses constructs is left off a copy that has a material, and a
        /// material trait the copy's own traits exclude is not granted.
        /// Do not call this from a BastleTrait's Notify_PawnUsedVerb,
        /// Notify_PawnPostApplyDamage or PostDiscovered: vanilla is iterating the pawn's
        /// hediffs at that point and adding or removing one throws. Queue it and apply it
        /// from TickInterval instead.
        /// </summary>
        /// <param name="includeDiscovery">Also copy which traits are discovered. Otherwise
        /// the copy starts with only visible traits known. No messages are sent either way.</param>
        /// <returns>The copy's bastle hediff, or null if there was nothing to copy.</returns>
        public static Hediff_Bastle CopyBastle(Pawn from, Pawn to, bool includeDiscovery = false)
        {
            if (to?.health == null || from == to) return GetBastle(to);
            var source = GetBastle(from);
            ClearBastle(to);
            if (source == null || source.TraitsForReading.Count == 0 && source.Material == null) return null;

            var bastle = GetOrMakeBastle(to);
            var copies = new List<(BastleTrait copy, BastleTrait src)>();

            // Own traits before the material: a material trait that the source never got
            // because one of these excludes it is then skipped on the copy as well.
            foreach (var src in source.TraitsForReading)
            {
                if (src.fromMaterial || bastle.HasTrait(src.def)) continue;
                // SetMaterialInternal below would only remove it again.
                if (source.Material != null && src.def.disallowConstructs) continue;
                copies.Add((bastle.AddTraitInternal(src.def, includeDiscovery && src.discovered, rarity: src.Rarity, exactRarity: true), src));
            }

            if (source.Material != null)
            {
                bastle.SetMaterialInternal(source.Material);
                foreach (var src in source.TraitsForReading)
                {
                    if (!src.fromMaterial) continue;
                    var copy = bastle.GetTrait(src.def);
                    if (copy == null || !copy.fromMaterial) continue;
                    // Material traits are always added at their def's rarity, on both creatures.
                    if (includeDiscovery && src.discovered && !copy.discovered)
                    {
                        copy.discovered = true;
                        bastle.Notify_TraitDiscovered(copy);
                    }
                    copies.Add((copy, src));
                }
            }

            foreach (var (copy, src) in copies) copy.CopyStateFrom(src);
            // Copied state can change what a trait shows (VisualsActive is virtual).
            bastle.VisualsChanged();
            return bastle;
        }

        /// <summary>A carrying parent's rarity (either one's when both carry it), shifted
        /// one step up or down at the settings' chance. Clamped to what the trait can be.</summary>
        private static BastleRarity InheritedRarity(BastleTraitDef def, List<BastleRarity> parents)
        {
            int r = (int)parents.RandomElement();
            if (def.scalesWithRarity && Rand.Chance(BastleFrameworkMod.Settings.rarityShiftChance))
                r += Rand.Bool ? 1 : -1;
            return def.ClampRarity(BastleRarityUtility.Clamp(r));
        }

        // ---- materials -----------------------------------------------------------

        public static ThingDef GetMaterial(Pawn pawn) => GetBastle(pawn)?.Material;

        public static bool IsConstruct(Pawn pawn) => GetBastle(pawn)?.IsConstruct ?? false;

        /// <summary>Makes the creature out of a stuff (any stuff is accepted here; only
        /// rolling is limited to metal, stone and wood). Pass null to make it flesh again.
        /// Makes the pawn a bastle if needed.</summary>
        public static bool SetMaterial(Pawn pawn, ThingDef stuff)
        {
            if (pawn?.health == null) return false;
            if (stuff != null && !stuff.IsStuff) return false;
            if (stuff == null)
            {
                GetBastle(pawn)?.SetMaterialInternal(null);
                return true;
            }
            GetOrMakeBastle(pawn).SetMaterialInternal(stuff);
            return true;
        }

        /// <summary>Picks a metal, stone or wood stuff weighted by the stuff's commonality
        /// (and the BastleMaterialExtension's) and applies it. Returns the stuff, or null if
        /// nothing passed the validator.</summary>
        /// <param name="validator">Extra filter, e.g. s => !BastleMaterialUtility.IsFlammable(s).</param>
        public static ThingDef RollMaterial(Pawn pawn, Predicate<ThingDef> validator = null)
        {
            var candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => BastleMaterialUtility.IsRollable(d) && (validator == null || validator(d)));
            if (!candidates.TryRandomElementByWeight(BastleMaterialUtility.RollWeight, out var pick)) return null;
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
    public static class BastleDefOf
    {
        public static HediffDef BF_Bastle;
        public static JobDef BF_EatNonFood;

        static BastleDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(BastleDefOf));
    }
}
