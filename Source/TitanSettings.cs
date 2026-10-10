using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>Player-tunable numbers. Read through TitanFrameworkMod.Settings.</summary>
    public class TitanSettings : ModSettings
    {
        // Lights and effects
        public bool lightsEnabled = true;
        public int lightFollowCells = 1;
        public float lightRadiusFactor = 1f;
        public bool effectsEnabled = true;

        // Traits
        public float inheritChanceFactor = 1f;
        public bool discoveryMessages = true;
        public bool abilityAI = true;
        public float valuePercentPerPoint = 0.2f;
        public float valueFlatPerPoint = 100f;
        public float mysteryTradeBonus = 0.25f;
        public float healthPerPoint = 0.1f;

        // Generation and rarity (indexed by TitanRarity)
        public static readonly float[] DefaultRarityWeights = { 50f, 25f, 12f, 5f, 3f };
        public static readonly float[] DefaultRarityStrength = { 1f, 1.25f, 1.5f, 2f, 3f };
        public static readonly float[] DefaultRarityPoints = { 1f, 2f, 4f, 7f, 11f };
        public static readonly FloatRange DefaultPointTarget = new FloatRange(2f, 6f);
        public float[] rarityWeights = (float[])DefaultRarityWeights.Clone();
        public float[] rarityStrength = (float[])DefaultRarityStrength.Clone();
        public float[] rarityPoints = (float[])DefaultRarityPoints.Clone();
        public FloatRange pointTarget = DefaultPointTarget;
        public float overshootStrictness = 1f;
        public int maxTraits = 5;
        public float drawbackChance = 0.3f;
        public int maxDrawbacks = 1;
        public float rarityShiftChance = 0.2f;

        /// <summary>Bumped whenever the settings are saved, so caches built from them
        /// (each trait's scaled stage) know to rebuild.</summary>
        public static int Version;

        // Materials
        public float materialArmorFactor = 0.5f;
        public float materialDamageStrength = 1f;
        public float fireWeaknessStrength = 1f;
        public float butcherYieldPerBodySize = 40f;
        public float smallVolumeMultiplier = 10f;
        public bool materialMarketValue = true;
        public float materialNutritionPerUnit = 0.05f;

        public void Reset()
        {
            lightsEnabled = true;
            lightFollowCells = 1;
            lightRadiusFactor = 1f;
            effectsEnabled = true;
            inheritChanceFactor = 1f;
            discoveryMessages = true;
            abilityAI = true;
            valuePercentPerPoint = 0.2f;
            valueFlatPerPoint = 100f;
            mysteryTradeBonus = 0.25f;
            healthPerPoint = 0.1f;
            rarityWeights = (float[])DefaultRarityWeights.Clone();
            rarityStrength = (float[])DefaultRarityStrength.Clone();
            rarityPoints = (float[])DefaultRarityPoints.Clone();
            pointTarget = DefaultPointTarget;
            overshootStrictness = 1f;
            maxTraits = 5;
            drawbackChance = 0.3f;
            maxDrawbacks = 1;
            rarityShiftChance = 0.2f;
            materialArmorFactor = 0.5f;
            materialDamageStrength = 1f;
            fireWeaknessStrength = 1f;
            butcherYieldPerBodySize = 40f;
            smallVolumeMultiplier = 10f;
            materialMarketValue = true;
            materialNutritionPerUnit = 0.05f;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lightsEnabled, "lightsEnabled", true);
            Scribe_Values.Look(ref lightFollowCells, "lightFollowCells", 1);
            Scribe_Values.Look(ref lightRadiusFactor, "lightRadiusFactor", 1f);
            Scribe_Values.Look(ref effectsEnabled, "effectsEnabled", true);
            Scribe_Values.Look(ref inheritChanceFactor, "inheritChanceFactor", 1f);
            Scribe_Values.Look(ref discoveryMessages, "discoveryMessages", true);
            Scribe_Values.Look(ref abilityAI, "abilityAI", true);
            Scribe_Values.Look(ref valuePercentPerPoint, "valuePercentPerPoint", 0.2f);
            Scribe_Values.Look(ref valueFlatPerPoint, "valueFlatPerPoint", 100f);
            Scribe_Values.Look(ref mysteryTradeBonus, "mysteryTradeBonus", 0.25f);
            Scribe_Values.Look(ref healthPerPoint, "healthPerPoint", 0.1f);
            for (int i = 0; i < TitanRarityUtility.Count; i++)
            {
                var r = (TitanRarity)i;
                Scribe_Values.Look(ref rarityWeights[i], "rarityWeight" + r, DefaultRarityWeights[i]);
                Scribe_Values.Look(ref rarityStrength[i], "rarityStrength" + r, DefaultRarityStrength[i]);
                Scribe_Values.Look(ref rarityPoints[i], "rarityPoints" + r, DefaultRarityPoints[i]);
                rarityWeights[i] = Mathf.Max(0f, rarityWeights[i]);
                rarityStrength[i] = Mathf.Max(0.1f, rarityStrength[i]);
                rarityPoints[i] = Mathf.Max(0.1f, rarityPoints[i]);
            }
            Scribe_Values.Look(ref pointTarget, "pointTarget", DefaultPointTarget);
            Scribe_Values.Look(ref overshootStrictness, "overshootStrictness", 1f);
            Scribe_Values.Look(ref maxTraits, "maxTraits", 5);
            Scribe_Values.Look(ref drawbackChance, "drawbackChance", 0.3f);
            Scribe_Values.Look(ref maxDrawbacks, "maxDrawbacks", 1);
            Scribe_Values.Look(ref rarityShiftChance, "rarityShiftChance", 0.2f);
            Version++;
            Scribe_Values.Look(ref materialArmorFactor, "materialArmorFactor", 0.5f);
            Scribe_Values.Look(ref materialDamageStrength, "materialDamageStrength", 1f);
            Scribe_Values.Look(ref fireWeaknessStrength, "fireWeaknessStrength", 1f);
            Scribe_Values.Look(ref butcherYieldPerBodySize, "butcherYieldPerBodySize", 40f);
            Scribe_Values.Look(ref smallVolumeMultiplier, "smallVolumeMultiplier", 10f);
            Scribe_Values.Look(ref materialMarketValue, "materialMarketValue", true);
            Scribe_Values.Look(ref materialNutritionPerUnit, "materialNutritionPerUnit", 0.05f);
        }

        // ---- window --------------------------------------------------------------

        private enum Tab { Lights, Traits, Materials }
        private static Tab tab = Tab.Lights;

        public void DoWindowContents(Rect inRect)
        {
            // TabDrawer draws the strip above the rect it is given, so the body starts one
            // tab height down; the bottom 40 px hold the reset button.
            var body = new Rect(inRect.x, inRect.y + TabDrawer.TabHeight, inRect.width, inRect.height - TabDrawer.TabHeight - 40f);
            var tabs = new List<TabRecord>
            {
                new TabRecord("TF_Settings_LightsSection".Translate(), () => tab = Tab.Lights, tab == Tab.Lights),
                new TabRecord("TF_Settings_TraitsSection".Translate(), () => tab = Tab.Traits, tab == Tab.Traits),
                new TabRecord("TF_Settings_MaterialsSection".Translate(), () => tab = Tab.Materials, tab == Tab.Materials),
            };
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);

            var l = new Listing_Standard();
            l.Begin(body.ContractedBy(12f));
            if (tab == Tab.Lights) DoLights(l);
            else if (tab == Tab.Traits) DoTraits(l);
            else DoMaterials(l);
            l.End();

            var reset = new Rect(inRect.xMax - 160f, inRect.yMax - 32f, 160f, 32f);
            if (Widgets.ButtonText(reset, "TF_Settings_Reset".Translate()))
                Reset();
        }

        private void DoLights(Listing_Standard l)
        {
            Check(l, "TF_Settings_Lights", ref lightsEnabled, "TF_Settings_LightsHint");
            float follow = lightFollowCells;
            Slider(l, "TF_Settings_LightFollow", ref follow, 1f, 5f, "TF_Settings_LightFollowValue".Translate(Mathf.RoundToInt(follow)), "TF_Settings_LightFollowHint", 1f);
            lightFollowCells = Mathf.RoundToInt(follow);
            Slider(l, "TF_Settings_LightRadius", ref lightRadiusFactor, 0.25f, 2f, lightRadiusFactor.ToStringPercent(), null, 0.05f);
            Check(l, "TF_Settings_Effects", ref effectsEnabled, "TF_Settings_EffectsHint");
        }

        private void DoTraits(Listing_Standard l)
        {
            Slider(l, "TF_Settings_Inherit", ref inheritChanceFactor, 0f, 2f, inheritChanceFactor.ToStringPercent(), "TF_Settings_InheritHint", 0.05f);
            Check(l, "TF_Settings_DiscoveryMessages", ref discoveryMessages, null);
            Check(l, "TF_Settings_AbilityAI", ref abilityAI, "TF_Settings_AbilityAIHint");
            Slider(l, "TF_Settings_HealthPerPoint", ref healthPerPoint, 0f, 0.5f, "+" + healthPerPoint.ToStringPercent(), "TF_Settings_HealthPerPointHint", 0.01f);
            Slider(l, "TF_Settings_ValuePercent", ref valuePercentPerPoint, 0f, 1f, "+" + valuePercentPerPoint.ToStringPercent(), "TF_Settings_ValuePercentHint", 0.01f);
            Slider(l, "TF_Settings_ValueFlat", ref valueFlatPerPoint, 0f, 500f, "+" + valueFlatPerPoint.ToStringMoney(), "TF_Settings_ValueFlatHint", 5f);
            Slider(l, "TF_Settings_Mystery", ref mysteryTradeBonus, 0f, 1f, "+" + mysteryTradeBonus.ToStringPercent(), "TF_Settings_MysteryHint", 0.05f);
        }

        private void DoMaterials(Listing_Standard r)
        {
            Slider(r, "TF_Settings_Armor", ref materialArmorFactor, 0f, 1.5f, materialArmorFactor.ToStringPercent(),
                "TF_Settings_ArmorHint".Translate((0.9f * materialArmorFactor).ToStringPercent()), 0.05f);
            Slider(r, "TF_Settings_Damage", ref materialDamageStrength, 0f, 2f, materialDamageStrength.ToStringPercent(), "TF_Settings_DamageHint", 0.05f);
            Slider(r, "TF_Settings_Fire", ref fireWeaknessStrength, 0f, 2f, fireWeaknessStrength.ToStringPercent(),
                "TF_Settings_FireHint".Translate((1f + 0.5f * fireWeaknessStrength).ToStringByStyle(ToStringStyle.FloatMaxTwo),
                    Mathf.Max(0f, 1f - 0.5f * fireWeaknessStrength).ToStringByStyle(ToStringStyle.FloatMaxTwo)), 0.05f);
            Slider(r, "TF_Settings_Butcher", ref butcherYieldPerBodySize, 0f, 200f, Mathf.RoundToInt(butcherYieldPerBodySize).ToString(),
                "TF_Settings_ButcherHint".Translate(Mathf.RoundToInt(2.15f * butcherYieldPerBodySize)), 1f);
            Slider(r, "TF_Settings_Precious", ref smallVolumeMultiplier, 1f, 20f, "x" + Mathf.RoundToInt(smallVolumeMultiplier), "TF_Settings_PreciousHint", 1f);
            Check(r, "TF_Settings_MarketValue", ref materialMarketValue, "TF_Settings_MarketValueHint");
            Slider(r, "TF_Settings_EatMaterial", ref materialNutritionPerUnit, 0f, 0.5f, materialNutritionPerUnit.ToStringByStyle(ToStringStyle.FloatMaxTwo),
                materialNutritionPerUnit > 0f
                    ? "TF_Settings_EatMaterialHint".Translate(Mathf.CeilToInt(1f / materialNutritionPerUnit)).ToString()
                    : "TF_Settings_EatMaterialOffHint".Translate().ToString(), 0.01f);
        }

        private static void Hint(Listing_Standard l, string text)
        {
            if (text.NullOrEmpty()) return;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            l.Label(text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);
        }

        private static void Check(Listing_Standard l, string key, ref bool value, string hintKey)
        {
            l.CheckboxLabeled(key.Translate(), ref value);
            if (hintKey != null) Hint(l, hintKey.Translate());
        }

        /// <param name="hint">Either a Keyed key or an already-translated string.</param>
        private static void Slider(Listing_Standard l, string key, ref float value, float min, float max, string shown, string hint, float roundTo)
        {
            var row = l.GetRect(Text.LineHeight);
            Widgets.Label(row.LeftPart(0.75f), key.Translate());
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(row.RightPart(0.25f), shown);
            Text.Anchor = TextAnchor.UpperLeft;
            value = l.Slider(value, min, max);
            value = Mathf.Clamp(GenMath.RoundTo(value, roundTo), min, max);
            if (hint != null) Hint(l, hint.CanTranslate() ? hint.Translate().ToString() : hint);
        }
    }
}
