using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    public enum TitanRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
    }

    public static class TitanRarityUtility
    {
        public const int Count = 5;

        public static string Label(this TitanRarity r) => ("TF_Rarity_" + r).Translate();

        public static float Weight(this TitanRarity r) => TitanFrameworkMod.Settings.rarityWeights[(int)r];
        public static float Strength(this TitanRarity r) => TitanFrameworkMod.Settings.rarityStrength[(int)r];
        public static float Points(this TitanRarity r) => TitanFrameworkMod.Settings.rarityPoints[(int)r];

        public static TitanRarity Clamp(int r) => (TitanRarity)Mathf.Clamp(r, 0, Count - 1);
    }

    /// <summary>
    /// What changes when a rarity-scaling trait rolls at minRarity or higher. Every field
    /// left out keeps the trait's own value; a field that is set replaces it whole (list
    /// the full set of abilities, not just the new one). When several overrides apply,
    /// the one with the highest minRarity wins.
    ///
    /// An override's stage is scaled from its own minRarity, so write it with the numbers
    /// it should have at exactly that rarity.
    /// </summary>
    public class TitanRarityOverride
    {
        public TitanRarity minRarity = TitanRarity.Rare;

        [MustTranslate] public string label;
        [MustTranslate] public string description;
        [MustTranslate] public string namePrefix;
        [MustTranslate] public string nameSuffix;

        public HediffStage stage;
        public List<TitanTraitHediff> hediffs;
        public List<AbilityDef> abilities;
        public ThinkTreeDef aiThinkTree;
        public TitanDietProps diet;
        public List<TitanOverlay> overlays;
        public List<EffecterDef> effecters;
        public List<TitanFlames> flames;
        public TitanGlow glow;
        public float? healthScaleFactor;
        public float? drawSizeFactor;
        public TitanRetaliation retaliation;
        public TitanLifesteal lifesteal;
        public TitanProducts products;
        public TitanDeflect deflect;
        public float? marketValuePercent;
        public float? marketValueOffset;
    }

    /// <summary>
    /// Scales a trait's stage for its rarity, the same way Entad Framework scales its
    /// ranges: offsets are multiplied by the strength, factors move away from 1 by it
    /// (f becomes 1 + (f - 1) x strength), so a x0.8 damage factor at strength 2 is x0.6.
    /// Factors never go below MinFactor; TitanTraitDef.FitsAt leaves out every rarity
    /// where that floor would be hit.
    /// </summary>
    public static class TitanStageScaler
    {
        public const float MinFactor = 0.05f;

        private static readonly Func<object, object> Clone =
            (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>),
                AccessTools.Method(typeof(object), "MemberwiseClone"));

        public static float Factor(float f, float strength) => Mathf.Max(MinFactor, 1f + (f - 1f) * strength);

        /// <summary>A scaled shallow copy. Returns the original when strength is 1.</summary>
        public static HediffStage Scale(HediffStage src, float strength)
        {
            if (src == null || Mathf.Abs(strength - 1f) < 0.0001f) return src;
            var s = (HediffStage)Clone(src);
            s.painFactor = Factor(src.painFactor, strength);
            s.painOffset = src.painOffset * strength;
            s.totalBleedFactor = Factor(src.totalBleedFactor, strength);
            if (src.naturalHealingFactor >= 0f) s.naturalHealingFactor = Factor(src.naturalHealingFactor, strength);
            if (src.regeneration >= 0f) s.regeneration = src.regeneration * strength;
            s.hungerRateFactor = Factor(src.hungerRateFactor, strength);
            s.hungerRateFactorOffset = src.hungerRateFactorOffset * strength;
            s.restFallFactor = Factor(src.restFallFactor, strength);
            s.restFallFactorOffset = src.restFallFactorOffset * strength;
            s.fertilityFactor = Factor(src.fertilityFactor, strength);
            s.foodPoisoningChanceFactor = Factor(src.foodPoisoningChanceFactor, strength);
            s.socialFightChanceFactor = Factor(src.socialFightChanceFactor, strength);
            s.opinionOfOthersFactor = Factor(src.opinionOfOthersFactor, strength);
            if (src.statOffsets != null)
            {
                s.statOffsets = new List<StatModifier>(src.statOffsets.Count);
                foreach (var m in src.statOffsets) s.statOffsets.Add(new StatModifier { stat = m.stat, value = m.value * strength });
            }
            if (src.statFactors != null)
            {
                s.statFactors = new List<StatModifier>(src.statFactors.Count);
                foreach (var m in src.statFactors) s.statFactors.Add(new StatModifier { stat = m.stat, value = Factor(m.value, strength) });
            }
            if (src.damageFactors != null)
            {
                s.damageFactors = new List<DamageFactor>(src.damageFactors.Count);
                foreach (var d in src.damageFactors) s.damageFactors.Add(new DamageFactor { damageDef = d.damageDef, factor = Factor(d.factor, strength) });
            }
            if (src.capMods != null)
            {
                s.capMods = new List<PawnCapacityModifier>(src.capMods.Count);
                foreach (var c in src.capMods)
                {
                    var n = (PawnCapacityModifier)Clone(c);
                    n.offset = c.offset * strength;
                    n.postFactor = Factor(c.postFactor, strength);
                    s.capMods.Add(n);
                }
            }
            return s;
        }

        /// <summary>The smallest factor below 1 in the stage (1 when it has none), for
        /// checking how far it can scale before it hits the floor.</summary>
        public static float LowestFactor(HediffStage s)
        {
            if (s == null) return 1f;
            float low = 1f;
            void F(float f) { if (f >= 0f && f < low) low = f; }
            F(s.painFactor); F(s.totalBleedFactor); F(s.naturalHealingFactor); F(s.hungerRateFactor);
            F(s.restFallFactor); F(s.fertilityFactor); F(s.foodPoisoningChanceFactor);
            F(s.socialFightChanceFactor); F(s.opinionOfOthersFactor);
            if (s.statFactors != null) foreach (var m in s.statFactors) F(m.value);
            if (s.damageFactors != null) foreach (var d in s.damageFactors) F(d.factor);
            if (s.capMods != null) foreach (var c in s.capMods) F(c.postFactor);
            return low;
        }
    }
}
