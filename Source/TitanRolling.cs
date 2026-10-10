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

        /// <summary>Chance the creature may take drawbacks (negative-point traits), and how many.</summary>
        public float? drawbackChance;
        public int? maxDrawbacks;

        /// <summary>Relative weight of each rarity, indexed by TitanRarity. Null: the settings'.</summary>
        public float[] rarityWeights;

        /// <summary>Only these rarities. Null or empty: all.</summary>
        public HashSet<TitanRarity> allowedRarities;

        /// <summary>Extra filter, e.g. d => d.tags != null && d.tags.Contains("Fire").</summary>
        public Predicate<TitanTraitDef> validator;
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
            // Never zero, so a zero target still gets one trait.
            float remaining = Math.Max(budget, 0.1f);
            float strictness = Math.Max(0f, request.overshootStrictness ?? s.overshootStrictness);
            int cap = Math.Max(1, request.maxTraits ?? s.maxTraits);
            int drawbacksLeft = Rand.Chance(request.drawbackChance ?? s.drawbackChance) ? (request.maxDrawbacks ?? s.maxDrawbacks) : 0;
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
                    .Where(d => d.commonality > 0f && (request.validator == null || request.validator(d)) && TitanUtility.CanAddTrait(pawn, d))
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
            return added;
        }

        private static List<Pair> Pairs(List<TitanTraitDef> candidates, float[] weights, HashSet<TitanRarity> allowed)
        {
            var pairs = new List<Pair>();
            foreach (var d in candidates)
            {
                var hi = d.MaxRarity;
                for (var r = d.rarity; r <= hi; r++)
                {
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
