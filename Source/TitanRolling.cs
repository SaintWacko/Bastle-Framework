using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace TitanFramework
{
    /// <summary>Options for TitanUtility.RollTraits. Every field left null uses the mod
    /// settings. A request is never modified, so a content mod can keep and reuse one.</summary>
    public class TitanRollRequest
    {
        /// <summary>Point target range. The target is a minimum: traits are rolled until the
        /// new traits' points reach it, and the last one may go over.</summary>
        public FloatRange? points;

        /// <summary>How strongly a trait that would overshoot the target is held back: it
        /// keeps (points left / its cost) to this power of its weight. 0 = a pure minimum.</summary>
        public float? overshootStrictness;

        /// <summary>Most traits to add.</summary>
        public int? maxTraits;

        /// <summary>Chance the creature may take drawbacks (negative-point traits), and how many.
        /// Both apply per roll: the main roll and the utility roll each check the chance, so a
        /// creature can end up with up to twice maxDrawbacks.</summary>
        public float? drawbackChance;
        public int? maxDrawbacks;

        /// <summary>Relative weight of each rarity, indexed by TitanRarity. Null: the settings'.</summary>
        public float[] rarityWeights;

        /// <summary>Only these rarities. Null or empty: all.</summary>
        public HashSet<TitanRarity> allowedRarities;

        /// <summary>Extra filter, e.g. d => d.tags != null && d.tags.Contains("Fire").
        /// Applies to both the main roll and the utility roll. When it passes no trait
        /// outside the utility set, the utility roll always happens, on the full target.</summary>
        public Predicate<TitanTraitDef> validator;

        /// <summary>Chance of the separate utility roll (traits tagged Utility), made after
        /// the main roll with whatever trait slots are left.</summary>
        public float? utilityChance;

        /// <summary>The utility roll's point target as a fraction of the main target,
        /// never less than one Common trait's points.</summary>
        public float? utilityBudget;
    }

    /// <summary>
    /// The point-target roller, ported from Entad Framework. Each pick is a (trait, rarity)
    /// pair: a rarity by its weight, then a trait among those that can roll at it, by
    /// commonality. Then the weights are adjusted:
    ///  - a pair costing more than the points left keeps (left / cost)^strictness of its
    ///    weight, so a small target can still roll a big trait, just rarely;
    ///  - a pair much cheaper than the target per remaining trait slot is made less likely,
    ///    so a big target buys rarer traits instead of filling the trait cap with Commons;
    ///  - a pair that would leave the target out of reach of the slots left is skipped,
    ///    and when every pair would be, the costliest trait is taken.
    /// A creature allowed drawbacks takes one as soon as one fits after its first real
    /// trait, as long as the drawback leaves a slot to pay its points back. The trait cap
    /// is the only thing that can stop a creature short of its target.
    /// Utility traits (tagged Utility) are kept out of that roll. Afterwards, with
    /// utilityChance, the same roller runs again over utility traits alone, on a smaller
    /// target and only in the slots the main roll left, so they never cost a creature
    /// combat strength. Drawbacks tagged Utility roll there too, with their own drawback
    /// chance, so a drawback with no effect in a fight never buys a combat trait.
    /// </summary>
    public static class TitanRolling
    {
        private const float Epsilon = 0.001f;

        private struct Pair
        {
            public TitanTraitDef def;
            public TitanRarity rarity;
        }

        internal static List<TitanTrait> Roll(Pawn pawn, TitanRollRequest request, float? fixedPoints)
        {
            var added = new List<TitanTrait>();
            if (pawn?.health == null) return added;
            var s = TitanFrameworkMod.Settings;

            float budget = fixedPoints ?? (request.points ?? s.pointTarget).RandomInRange;
            int cap = Math.Max(1, request.maxTraits ?? s.maxTraits);
            float drawbackChance = request.drawbackChance ?? s.drawbackChance;
            int maxDrawbacks = request.maxDrawbacks ?? s.maxDrawbacks;
            int drawbacks = Rand.Chance(drawbackChance) ? maxDrawbacks : 0;
            var validator = request.validator;

            RollPool(pawn, request, budget, cap, drawbacks, d => !d.IsUtility && (validator == null || validator(d)), added);

            // A validator that only lets utility traits through (a "tame-worthy" roll) leaves
            // the main pool empty: then they roll on the full target, without the chance.
            bool onlyUtility = added.Count == 0 && validator != null
                && !DefDatabase<TitanTraitDef>.AllDefsListForReading.Any(d => !d.IsUtility && d.commonality > 0f && validator(d));
            if (added.Count < cap && (onlyUtility || Rand.Chance(request.utilityChance ?? s.utilityChance)))
            {
                float utilityBudget = onlyUtility ? budget
                    : Math.Max(budget * Math.Max(0f, request.utilityBudget ?? s.utilityBudget), TitanRarity.Common.Points());
                var utility = new List<TitanTrait>();
                // Utility drawbacks (Restless, Barren...) get their own chance, and the
                // points they pay back only buy utility traits, never combat strength.
                int utilityDrawbacks = Rand.Chance(drawbackChance) ? maxDrawbacks : 0;
                RollPool(pawn, request, utilityBudget, cap - added.Count, utilityDrawbacks, d => d.IsUtility && (validator == null || validator(d)), utility);
                added.AddRange(utility);
            }
            return added;
        }

        private static void RollPool(Pawn pawn, TitanRollRequest request, float budget, int cap, int drawbacksLeft,
            Predicate<TitanTraitDef> filter, List<TitanTrait> added)
        {
            var s = TitanFrameworkMod.Settings;
            // Never zero, so a zero target still gets one trait.
            float remaining = Math.Max(budget, 0.1f);
            float strictness = Math.Max(0f, request.overshootStrictness ?? s.overshootStrictness);
            float[] weights = s.rarityWeights;
            if (request.rarityWeights != null)
            {
                if (request.rarityWeights.Length >= TitanRarityUtility.Count) weights = request.rarityWeights;
                else Log.WarningOnce($"[Titan Framework] TitanRollRequest.rarityWeights needs {TitanRarityUtility.Count} entries (one per rarity); using the settings' weights.", 0x7A11 ^ 0x5EED);
            }
            var allowed = request.allowedRarities;

            bool hasRealTrait = false;
            while (added.Count < cap)
            {
                if (remaining <= Epsilon && !(drawbacksLeft > 0 && hasRealTrait)) break;
                var candidates = DefDatabase<TitanTraitDef>.AllDefsListForReading
                    .Where(d => d.commonality > 0f && filter(d) && TitanUtility.CanAddTrait(pawn, d))
                    .ToList();
                if (candidates.Count == 0) break;

                int slotsLeft = cap - added.Count;
                float perSlot = remaining / slotsLeft;
                float left = remaining;
                var pairs = Pairs(candidates, weights, allowed);
                float best = MaxPoints(pairs, p => !p.def.IsDrawback);
                Pair? pick = null;

                // A drawback needs a real trait before it and a free slot after it.
                if (drawbacksLeft > 0 && hasRealTrait && slotsLeft > 1)
                {
                    // ...and a real trait that can share the creature with it, to pay it back.
                    var real = pairs.Where(p => !p.def.IsDrawback).ToList();
                    pick = PickWhere(pairs, weights, p =>
                    {
                        if (!p.def.IsDrawback) return 0f;
                        float after = left - p.def.PointsAt(p.rarity);
                        float bestWith = MaxPoints(real, q => !q.def.ConflictsWith(p.def));
                        return bestWith > 0f && after > Epsilon && after <= (slotsLeft - 1) * bestWith + Epsilon ? 1f : 0f;
                    });
                    if (pick != null) drawbacksLeft--;
                }
                if (pick == null && remaining <= Epsilon) break;
                if (pick == null)
                {
                    float reach = (slotsLeft - 1) * best;
                    pick = PickWhere(pairs, weights, p =>
                    {
                        if (p.def.IsDrawback) return 0f;
                        float c = p.def.PointsAt(p.rarity);
                        if (left - c > reach + Epsilon) return 0f;
                        if (c > left + Epsilon) return (float)Math.Pow(left / c, strictness);
                        if (c >= perSlot) return 1f;
                        float f = c / perSlot;
                        return Math.Max(0.05f, f * f);
                    });
                    // Out of reach whatever is picked: take the biggest trait there is.
                    if (pick == null && best > 0f)
                        pick = PickWhere(pairs, weights, p => !p.def.IsDrawback && p.def.PointsAt(p.rarity) >= best - Epsilon ? 1f : 0f);
                }
                if (pick == null) break;

                var trait = TitanUtility.AddTrait(pawn, pick.Value.def, rarity: pick.Value.rarity);
                if (trait == null) break;
                added.Add(trait);
                if (!pick.Value.def.IsDrawback) hasRealTrait = true;
                remaining -= trait.Points;
            }
        }

        private static List<Pair> Pairs(List<TitanTraitDef> candidates, float[] weights, HashSet<TitanRarity> allowed)
        {
            var pairs = new List<Pair>();
            foreach (var d in candidates)
            {
                var hi = d.MaxRarity;
                for (var r = d.rarity; r <= hi; r++)
                {
                    if (!d.CanAppearAt(r)) continue;
                    if (allowed != null && allowed.Count > 0 && !allowed.Contains(r)) continue;
                    if (weights[(int)r] <= 0f) continue;
                    pairs.Add(new Pair { def = d, rarity = r });
                }
            }
            return pairs;
        }

        private static float MaxPoints(List<Pair> pairs, Func<Pair, bool> where)
        {
            float best = 0f;
            foreach (var p in pairs)
                if (where(p)) best = Math.Max(best, p.def.PointsAt(p.rarity));
            return best;
        }

        // The rarity's weight shared out among its pairs by commonality, then scaled by
        // pairWeight (0 leaves a pair out). A rarity with no pair left drops out, so its
        // share goes to the others.
        private static Pair? PickWhere(List<Pair> pairs, float[] weights, Func<Pair, float> pairWeight)
        {
            var sums = new float[TitanRarityUtility.Count];
            var pw = new float[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                pw[i] = pairWeight(pairs[i]);
                if (pw[i] > 0f) sums[(int)pairs[i].rarity] += pairs[i].def.commonality;
            }
            float total = 0f;
            var final = new float[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pw[i] <= 0f) continue;
                var p = pairs[i];
                final[i] = weights[(int)p.rarity] * p.def.commonality / sums[(int)p.rarity] * pw[i];
                total += final[i];
            }
            if (total <= 0f) return null;
            float roll = Rand.Value * total;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (final[i] <= 0f) continue;
                roll -= final[i];
                if (roll <= 0f) return pairs[i];
            }
            for (int i = pairs.Count - 1; i >= 0; i--)
                if (final[i] > 0f) return pairs[i];
            return null;
        }
    }
}
