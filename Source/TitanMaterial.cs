using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Optional tuning for one stuff as a creature material. Every metal, stone and wood
    /// stuff works without this; add it to a StuffDef to change or exclude it:
    /// <code>
    /// &lt;modExtensions&gt;
    ///   &lt;li Class="TitanFramework.TitanMaterialExtension"&gt;
    ///     &lt;commonality&gt;0.5&lt;/commonality&gt;
    ///     &lt;traits&gt;&lt;li&gt;MyMod_Radioactive&lt;/li&gt;&lt;/traits&gt;
    ///   &lt;/li&gt;
    /// &lt;/modExtensions&gt;
    /// </code>
    /// </summary>
    public class TitanMaterialExtension : DefModExtension
    {
        /// <summary>False keeps this stuff out of RollMaterial. SetMaterial still accepts it.</summary>
        public bool allowRolling = true;

        /// <summary>Also lets a stuff outside metal/stone/wood be rolled.</summary>
        public bool forceAllowed;

        /// <summary>Multiplies the stuff's own commonality when rolling.</summary>
        public float commonality = 1f;

        public float armorFactor = 1f;
        public float damageFactor = 1f;
        public float butcherYieldFactor = 1f;

        /// <summary>Traits a creature of this material always has (added on SetMaterial,
        /// removed if the material changes). Uranium could be toxic, gold brittle.</summary>
        public List<TitanTraitDef> traits;

        /// <summary>Free-form tags for content mods, like a trait's tags.</summary>
        public List<string> tags;
    }

    /// <summary>
    /// What a material does to a creature. Everything is derived from the stuff's own
    /// stats so modded metals and stones work without extra XML.
    /// </summary>
    public static partial class TitanMaterialUtility
    {
        // Tunable numbers (armour share, damage and fire strength, butcher yield) are in
        // TitanSettings.
        private static TitanSettings S => TitanFrameworkMod.Settings;

        /// <summary>Stuffs at or above this flammability are tagged Flammable (wood).</summary>
        public const float FlammableThreshold = 0.5f;

        private static StatDef vacuumResistance;
        private static bool vacuumLooked;

        public static TitanMaterialExtension Ext(ThingDef stuff) => stuff?.GetModExtension<TitanMaterialExtension>();

        /// <summary>Metal, stone and wood stuffs, plus any stuff with forceAllowed.</summary>
        public static bool IsMaterialCandidate(ThingDef def)
        {
            if (def?.stuffProps == null || !def.IsStuff) return false;
            var ext = Ext(def);
            if (ext != null && ext.forceAllowed) return true;
            var cats = def.stuffProps.categories;
            return cats != null && (cats.Contains(StuffCategoryDefOf.Metallic)
                || cats.Contains(StuffCategoryDefOf.Stony) || cats.Contains(StuffCategoryDefOf.Woody));
        }

        public static bool IsRollable(ThingDef def) => IsMaterialCandidate(def) && (Ext(def)?.allowRolling ?? true) && RollWeight(def) > 0f;

        public static float RollWeight(ThingDef def) => def.stuffProps.commonality * (Ext(def)?.commonality ?? 1f);

        /// <summary>The adjective used in names: "jade", "steel", "wooden", "granite".</summary>
        public static string Adjective(ThingDef stuff) => stuff.stuffProps.stuffAdjective ?? stuff.label;

        public static float Flammability(ThingDef stuff) => stuff.GetStatValueAbstract(StatDefOf.Flammability);

        public static bool IsFlammable(ThingDef stuff) => Flammability(stuff) >= FlammableThreshold;

        /// <summary>The material's tags: the extension's, plus Flammable for wood-like stuffs.</summary>
        public static bool HasTag(ThingDef stuff, string tag)
        {
            if (stuff == null) return false;
            if (tag == "Flammable" && IsFlammable(stuff)) return true;
            return Ext(stuff)?.tags?.Contains(tag) ?? false;
        }

        /// <summary>Armour power of the stuff. Stone blocks aren't apparel materials and
        /// have no armour stats, so their hit point factor stands in for hardness.</summary>
        public static float ArmorPower(ThingDef stuff, StatDef stat)
        {
            float v = stuff.GetStatValueAbstract(stat);
            if (v > 0f) return v;
            float hp = stuff.stuffProps.statFactors?.GetStatFactorFromList(StatDefOf.MaxHitPoints) ?? 1f;
            float guess = 0.5f * hp;
            return stat == StatDefOf.StuffPower_Armor_Heat ? guess * 0.5f : guess;
        }

        /// <summary>Melee damage multiplier: the average of the stuff's sharp and blunt
        /// multipliers, so wood (0.4 / 0.9) hits softer and jade (1 / 1.5) harder.</summary>
        public static float DamageMultiplier(ThingDef stuff)
        {
            float sharp = stuff.GetStatValueAbstract(StatDefOf.SharpDamageMultiplier);
            float blunt = stuff.GetStatValueAbstract(StatDefOf.BluntDamageMultiplier);
            float m = (sharp + blunt) * 0.5f;
            return Mathf.Max(0f, 1f + (m - 1f) * (Ext(stuff)?.damageFactor ?? 1f) * S.materialDamageStrength);
        }

        /// <summary>Fire and burn damage factor. Flammability 0.5 is neutral; wood (1.0)
        /// takes x1.5 and stone (0) x0.5 at full strength.</summary>
        public static float FireDamageFactor(ThingDef stuff) =>
            Mathf.Max(0f, 1f + (Flammability(stuff) - 0.5f) * S.fireWeaknessStrength);

        /// <summary>Butchered stuff: body size x the per-body-size setting x the extension
        /// factor x efficiency, times the precious multiplier for small-volume stuffs.</summary>
        public static float ButcherAmount(Pawn pawn, ThingDef stuff, float efficiency = 1f)
        {
            float n = pawn.BodySize * S.butcherYieldPerBodySize * (Ext(stuff)?.butcherYieldFactor ?? 1f) * efficiency;
            return stuff.smallVolume ? n * S.smallVolumeMultiplier : n;
        }

        public static int ButcherCount(Pawn pawn, ThingDef stuff, float efficiency = 1f) =>
            Mathf.Max(0, GenMath.RoundRandom(ButcherAmount(pawn, stuff, efficiency)));
    }

    /// <summary>A creature made of jade is worth its jade. A hediff stat offset can't do
    /// this: the pawn market value worker evaluates the race def, not the pawn's
    /// hediffs. Added to MarketValue's parts at startup.</summary>
    public class StatPart_TitanMaterial : StatPart
    {
        private static float Bonus(StatRequest req, out ThingDef stuff, out float amount)
        {
            stuff = null;
            amount = 0f;
            if (!(req.Thing is Pawn pawn)) return 0f;
            stuff = TitanUtility.GetMaterial(pawn);
            if (stuff == null || !TitanFrameworkMod.Settings.materialMarketValue) return 0f;
            amount = TitanMaterialUtility.ButcherAmount(pawn, stuff);
            return amount * stuff.BaseMarketValue;
        }

        public override void TransformValue(StatRequest req, ref float val) => val += Bonus(req, out _, out _);

        public override string ExplanationPart(StatRequest req)
        {
            float bonus = Bonus(req, out var stuff, out float amount);
            if (stuff == null || bonus <= 0f) return null;
            return "TF_MaterialValue".Translate(Mathf.RoundToInt(amount), stuff.label) + ": +" + bonus.ToStringMoney();
        }
    }

    [StaticConstructorOnStartup]
    internal static class TitanMaterialStartup
    {
        static TitanMaterialStartup()
        {
            var stat = StatDefOf.MarketValue;
            if (stat.parts == null) stat.parts = new List<StatPart>();
            // Traits first: they're priced off the creature's value before its material.
            StatPart_TitanTraits.Instance = new StatPart_TitanTraits { parentStat = stat };
            stat.parts.Add(StatPart_TitanTraits.Instance);
            stat.parts.Add(new StatPart_TitanMaterial { parentStat = stat });
        }
    }

    public static partial class TitanMaterialUtility
    {
        /// <summary>
        /// The material's effects as a stage, merged into the titan's stage like a trait's.
        /// Construct body: no pain, no bleeding, no meat or leather, immune to toxins and
        /// toxic gas (and vacuum, with Odyssey). Armour and melee damage from the stuff.
        /// Fire damage and flammability follow the stuff's flammability, so wood burns.
        /// </summary>
        public static HediffStage BuildStage(Pawn pawn, ThingDef stuff)
        {
            var ext = Ext(stuff);
            float armor = S.materialArmorFactor * (ext?.armorFactor ?? 1f);
            float flam = Flammability(stuff);
            float fire = FireDamageFactor(stuff);
            // At reduced fire weakness, ignition also drifts back toward a normal creature's.
            float flamFactor = Mathf.Lerp(1f, Mathf.Clamp(flam, 0f, 2f), Mathf.Min(1f, S.fireWeaknessStrength));

            var s = new HediffStage
            {
                becomeVisible = true,
                painFactor = 0f,
                totalBleedFactor = 0f,
                statOffsets = new List<StatModifier>
                {
                    new StatModifier { stat = StatDefOf.ArmorRating_Sharp, value = ArmorPower(stuff, StatDefOf.StuffPower_Armor_Sharp) * armor },
                    new StatModifier { stat = StatDefOf.ArmorRating_Blunt, value = ArmorPower(stuff, StatDefOf.StuffPower_Armor_Blunt) * armor },
                    new StatModifier { stat = StatDefOf.ArmorRating_Heat, value = ArmorPower(stuff, StatDefOf.StuffPower_Armor_Heat) * armor },
                    new StatModifier { stat = StatDefOf.ToxicResistance, value = 1f },
                    new StatModifier { stat = StatDefOf.ToxicEnvironmentResistance, value = 1f },
                },
                statFactors = new List<StatModifier>
                {
                    new StatModifier { stat = StatDefOf.MeleeDamageFactor, value = DamageMultiplier(stuff) },
                    new StatModifier { stat = StatDefOf.MeatAmount, value = 0f },
                    new StatModifier { stat = StatDefOf.LeatherAmount, value = 0f },
                    new StatModifier { stat = StatDefOf.Flammability, value = flamFactor },
                },
                damageFactors = new List<DamageFactor>
                {
                    new DamageFactor { damageDef = DamageDefOf.Flame, factor = fire },
                    new DamageFactor { damageDef = DamageDefOf.Burn, factor = fire },
                },
            };

            if (!vacuumLooked)
            {
                vacuumLooked = true;
                vacuumResistance = DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
            }
            if (vacuumResistance != null)
                s.statOffsets.Add(new StatModifier { stat = vacuumResistance, value = 1f });
            return s;
        }
    }
}
