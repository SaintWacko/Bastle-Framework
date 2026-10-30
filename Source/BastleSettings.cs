using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>Player-tunable numbers. Read through BastleFrameworkMod.Settings.</summary>
    public class BastleSettings : ModSettings
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
        public bool untrainedKnownAbilities = true;
        public bool trainedAbilitiesOnOwn = true;
        public float valuePercentPerPoint = 0.2f;
        public float valueFlatPerPoint = 100f;
        public float mysteryTradeBonus = 0.25f;
        public float healthPerPoint = 0.1f;
        public int maxNameTraits = 3;
        public const int MaxNameTraitsLimit = 10;
        public float scaledAttackCap = 12f;
        public const float ScaledAttackNoCap = 40f;

        // Generation and rarity (indexed by BastleRarity)
        public static readonly float[] DefaultRarityWeights = { 50f, 25f, 12f, 5f, 3f };
        public static readonly float[] DefaultRarityStrength = { 1f, 1.25f, 1.5f, 2f, 3f };
        public static readonly float[] DefaultRarityPoints = { 1f, 2f, 4f, 7f, 11f };
        public static readonly FloatRange DefaultPointTarget = new FloatRange(2f, 6f);
        public float[] rarityWeights = (float[])DefaultRarityWeights.Clone();
        public float[] rarityStrength = (float[])DefaultRarityStrength.Clone();
        public float[] rarityPoints = (float[])DefaultRarityPoints.Clone();
        public FloatRange pointTarget = DefaultPointTarget;
        public float overshootStrictness = 1f;
        public int maxTraits = 10;
        public float drawbackChance = 0.3f;
        public int maxDrawbacks = 1;
        public float rarityShiftChance = 0.2f;
        public float utilityChance = 0.4f;
        public float utilityBudget = 0.25f;

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

        // World (BastleWorld.cs). Defaults for new games; a game can override the switches.
        public static readonly float[] DefaultWorldCurvePoints = { 1f, 2f, 4f, 6f, 9f };
        public bool worldEnabled = true;
        public float[] worldCurvePoints = (float[])DefaultWorldCurvePoints.Clone();
        public float worldPointVariance = 0.25f;
        public float wildStrength = 0.5f;
        public float convertedStrength = 0.5f;
        public float offeredStrength = 0.5f;
        public bool wildEnabled = true;
        public float wildChance = 0.025f;
        public bool animalEventsEnabled = true;
        public float animalEventChance = 0.15f;
        public bool groupAnimalsEnabled = true;
        public float groupAnimalChance = 0.15f;
        public int groupAnimalMax = 2;
        public bool traderAnimalsEnabled = true;
        public float traderAnimalChance = 0.1f;
        public bool sightingEnabled = true;
        public float sightingFrequency = 1f;
        public float sightingStrength = 1f;
        public bool packEnabled = true;
        public float packFrequency = 1f;
        public float packTraitShare = 0.35f;
        public bool rampageEnabled = true;
        public float rampageFrequency = 1f;
        public float rampageStrength = 2f;
        public bool huntQuestEnabled = true;
        public float huntQuestFrequency = 1f;
        public float huntStrength = 2f;
        public float huntAlphaShare = 0.35f;
        public bool outbreakQuestEnabled = true;
        public float outbreakQuestFrequency = 1f;
        public float outbreakSize = 2f;
        public float outbreakStrength = 1f;
        public float outbreakAlphaStrength = 1.5f;
        public float outbreakTraitShare = 0.35f;
        public float outbreakGrowth = 0.05f;
        public float outbreakGrowthCap = 2f;
        public float outbreakAllyShare = 0.4f;
        public float outbreakSecondFactionChance = 0.5f;
        public bool outbreakHostileFactions = true;
        public IntRange outbreakSiteDistance = new IntRange(10, 40);
        public const int OutbreakSiteDistanceMin = 2;
        public const int OutbreakSiteDistanceMax = 100;
        public float outbreakFactionHalfDistance = 15f;
        /// <summary>At this slider value nearby factions are not preferred at all.</summary>
        public const float OutbreakFactionHalfDistanceOff = 60f;
        public int outbreakAllyGoodwill = 20;
        public int outbreakFailGoodwill = 10;
        public float outbreakUncontainedDays = 45f;
        public float outbreakSpreadFrequency = 1f;

        public void Reset()
        {
            lightsEnabled = true;
            lightFollowCells = 1;
            lightRadiusFactor = 1f;
            effectsEnabled = true;
            inheritChanceFactor = 1f;
            discoveryMessages = true;
            abilityAI = true;
            untrainedKnownAbilities = true;
            trainedAbilitiesOnOwn = true;
            valuePercentPerPoint = 0.2f;
            valueFlatPerPoint = 100f;
            mysteryTradeBonus = 0.25f;
            healthPerPoint = 0.1f;
            maxNameTraits = 3;
            scaledAttackCap = 12f;
            rarityWeights = (float[])DefaultRarityWeights.Clone();
            rarityStrength = (float[])DefaultRarityStrength.Clone();
            rarityPoints = (float[])DefaultRarityPoints.Clone();
            pointTarget = DefaultPointTarget;
            overshootStrictness = 1f;
            maxTraits = 10;
            drawbackChance = 0.3f;
            maxDrawbacks = 1;
            rarityShiftChance = 0.2f;
            utilityChance = 0.4f;
            utilityBudget = 0.25f;
            materialArmorFactor = 0.5f;
            materialDamageStrength = 1f;
            fireWeaknessStrength = 1f;
            butcherYieldPerBodySize = 40f;
            smallVolumeMultiplier = 10f;
            materialMarketValue = true;
            materialNutritionPerUnit = 0.05f;
            ResetWorld();
        }

        private void ResetWorld()
        {
            worldEnabled = true;
            worldCurvePoints = (float[])DefaultWorldCurvePoints.Clone();
            worldPointVariance = 0.25f;
            wildStrength = 0.5f;
            convertedStrength = 0.5f;
            offeredStrength = 0.5f;
            wildEnabled = true;
            wildChance = 0.025f;
            animalEventsEnabled = true;
            animalEventChance = 0.15f;
            groupAnimalsEnabled = true;
            groupAnimalChance = 0.15f;
            groupAnimalMax = 2;
            traderAnimalsEnabled = true;
            traderAnimalChance = 0.1f;
            sightingEnabled = true;
            sightingFrequency = 1f;
            sightingStrength = 1f;
            packEnabled = true;
            packFrequency = 1f;
            packTraitShare = 0.35f;
            rampageEnabled = true;
            rampageFrequency = 1f;
            rampageStrength = 2f;
            huntQuestEnabled = true;
            huntQuestFrequency = 1f;
            huntStrength = 2f;
            huntAlphaShare = 0.35f;
            outbreakQuestEnabled = true;
            outbreakQuestFrequency = 1f;
            outbreakSize = 2f;
            outbreakStrength = 1f;
            outbreakAlphaStrength = 1.5f;
            outbreakTraitShare = 0.35f;
            outbreakGrowth = 0.05f;
            outbreakGrowthCap = 2f;
            outbreakAllyShare = 0.4f;
            outbreakSecondFactionChance = 0.5f;
            outbreakHostileFactions = true;
            outbreakSiteDistance = new IntRange(10, 40);
            outbreakFactionHalfDistance = 15f;
            outbreakAllyGoodwill = 20;
            outbreakFailGoodwill = 10;
            outbreakUncontainedDays = 45f;
            outbreakSpreadFrequency = 1f;
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
            Scribe_Values.Look(ref untrainedKnownAbilities, "untrainedKnownAbilities", true);
            Scribe_Values.Look(ref trainedAbilitiesOnOwn, "trainedAbilitiesOnOwn", true);
            Scribe_Values.Look(ref valuePercentPerPoint, "valuePercentPerPoint", 0.2f);
            Scribe_Values.Look(ref valueFlatPerPoint, "valueFlatPerPoint", 100f);
            Scribe_Values.Look(ref mysteryTradeBonus, "mysteryTradeBonus", 0.25f);
            Scribe_Values.Look(ref healthPerPoint, "healthPerPoint", 0.1f);
            Scribe_Values.Look(ref maxNameTraits, "maxNameTraits", 3);
            maxNameTraits = Mathf.Clamp(maxNameTraits, 0, MaxNameTraitsLimit);
            Scribe_Values.Look(ref scaledAttackCap, "scaledAttackCap", 12f);
            scaledAttackCap = Mathf.Clamp(scaledAttackCap, 4f, ScaledAttackNoCap);
            for (int i = 0; i < BastleRarityUtility.Count; i++)
            {
                var r = (BastleRarity)i;
                Scribe_Values.Look(ref rarityWeights[i], "rarityWeight" + r, DefaultRarityWeights[i]);
                Scribe_Values.Look(ref rarityStrength[i], "rarityStrength" + r, DefaultRarityStrength[i]);
                Scribe_Values.Look(ref rarityPoints[i], "rarityPoints" + r, DefaultRarityPoints[i]);
                rarityWeights[i] = Mathf.Max(0f, rarityWeights[i]);
                rarityStrength[i] = Mathf.Max(0.1f, rarityStrength[i]);
                rarityPoints[i] = Mathf.Max(0.5f, rarityPoints[i]);
            }
            Scribe_Values.Look(ref pointTarget, "pointTarget", DefaultPointTarget);
            Scribe_Values.Look(ref overshootStrictness, "overshootStrictness", 1f);
            Scribe_Values.Look(ref maxTraits, "maxTraits", 10);
            maxTraits = Mathf.Clamp(maxTraits, 1, MaxTraitsLimit);
            Scribe_Values.Look(ref drawbackChance, "drawbackChance", 0.3f);
            Scribe_Values.Look(ref maxDrawbacks, "maxDrawbacks", 1);
            Scribe_Values.Look(ref rarityShiftChance, "rarityShiftChance", 0.2f);
            Scribe_Values.Look(ref utilityChance, "utilityChance", 0.4f);
            Scribe_Values.Look(ref utilityBudget, "utilityBudget", 0.25f);
            Version++;
            Scribe_Values.Look(ref materialArmorFactor, "materialArmorFactor", 0.5f);
            Scribe_Values.Look(ref materialDamageStrength, "materialDamageStrength", 1f);
            Scribe_Values.Look(ref fireWeaknessStrength, "fireWeaknessStrength", 1f);
            Scribe_Values.Look(ref butcherYieldPerBodySize, "butcherYieldPerBodySize", 40f);
            Scribe_Values.Look(ref smallVolumeMultiplier, "smallVolumeMultiplier", 10f);
            Scribe_Values.Look(ref materialMarketValue, "materialMarketValue", true);
            Scribe_Values.Look(ref materialNutritionPerUnit, "materialNutritionPerUnit", 0.05f);

            Scribe_Values.Look(ref worldEnabled, "worldEnabled", true);
            for (int i = 0; i < DefaultWorldCurvePoints.Length; i++)
            {
                Scribe_Values.Look(ref worldCurvePoints[i], "worldCurve" + i, DefaultWorldCurvePoints[i]);
                worldCurvePoints[i] = Mathf.Max(0f, worldCurvePoints[i]);
            }
            Scribe_Values.Look(ref worldPointVariance, "worldPointVariance", 0.25f);
            Scribe_Values.Look(ref wildStrength, "wildStrength", 0.5f);
            Scribe_Values.Look(ref convertedStrength, "convertedStrength", 0.5f);
            Scribe_Values.Look(ref offeredStrength, "offeredStrength", 0.5f);
            Scribe_Values.Look(ref wildEnabled, "wildEnabled", true);
            Scribe_Values.Look(ref wildChance, "wildChance", 0.025f);
            Scribe_Values.Look(ref animalEventsEnabled, "animalEventsEnabled", true);
            Scribe_Values.Look(ref animalEventChance, "animalEventChance", 0.15f);
            Scribe_Values.Look(ref groupAnimalsEnabled, "groupAnimalsEnabled", true);
            Scribe_Values.Look(ref groupAnimalChance, "groupAnimalChance", 0.15f);
            Scribe_Values.Look(ref groupAnimalMax, "groupAnimalMax", 2);
            Scribe_Values.Look(ref traderAnimalsEnabled, "traderAnimalsEnabled", true);
            Scribe_Values.Look(ref traderAnimalChance, "traderAnimalChance", 0.1f);
            Scribe_Values.Look(ref sightingEnabled, "sightingEnabled", true);
            Scribe_Values.Look(ref sightingFrequency, "sightingFrequency", 1f);
            Scribe_Values.Look(ref sightingStrength, "sightingStrength", 1f);
            sightingStrength = Mathf.Max(0.1f, sightingStrength);
            Scribe_Values.Look(ref packEnabled, "packEnabled", true);
            Scribe_Values.Look(ref packFrequency, "packFrequency", 1f);
            Scribe_Values.Look(ref packTraitShare, "packTraitShare", 0.35f);
            Scribe_Values.Look(ref rampageEnabled, "rampageEnabled", true);
            Scribe_Values.Look(ref rampageFrequency, "rampageFrequency", 1f);
            Scribe_Values.Look(ref rampageStrength, "rampageStrength", 2f);
            rampageStrength = Mathf.Max(0.1f, rampageStrength);
            Scribe_Values.Look(ref huntQuestEnabled, "huntQuestEnabled", true);
            Scribe_Values.Look(ref huntQuestFrequency, "huntQuestFrequency", 1f);
            Scribe_Values.Look(ref huntStrength, "huntStrength", 2f);
            huntStrength = Mathf.Max(0.1f, huntStrength);
            Scribe_Values.Look(ref huntAlphaShare, "huntAlphaShare", 0.35f);
            huntAlphaShare = Mathf.Clamp(huntAlphaShare, 0f, 0.8f);
            Scribe_Values.Look(ref outbreakQuestEnabled, "outbreakQuestEnabled", true);
            Scribe_Values.Look(ref outbreakQuestFrequency, "outbreakQuestFrequency", 1f);
            outbreakQuestFrequency = Mathf.Clamp(outbreakQuestFrequency, 0f, 3f);
            Scribe_Values.Look(ref outbreakSize, "outbreakSize", 2f);
            outbreakSize = Mathf.Clamp(outbreakSize, 1f, 5f);
            Scribe_Values.Look(ref outbreakStrength, "outbreakStrength", 1f);
            outbreakStrength = Mathf.Max(0.1f, outbreakStrength);
            Scribe_Values.Look(ref outbreakAlphaStrength, "outbreakAlphaStrength", 1.5f);
            outbreakAlphaStrength = Mathf.Max(0.1f, outbreakAlphaStrength);
            Scribe_Values.Look(ref outbreakTraitShare, "outbreakTraitShare", 0.35f);
            outbreakTraitShare = Mathf.Clamp(outbreakTraitShare, 0f, 0.8f);
            Scribe_Values.Look(ref outbreakGrowth, "outbreakGrowth", 0.05f);
            outbreakGrowth = Mathf.Clamp(outbreakGrowth, 0f, 0.25f);
            Scribe_Values.Look(ref outbreakGrowthCap, "outbreakGrowthCap", 2f);
            outbreakGrowthCap = Mathf.Clamp(outbreakGrowthCap, 1f, 5f);
            Scribe_Values.Look(ref outbreakAllyShare, "outbreakAllyShare", 0.4f);
            outbreakAllyShare = Mathf.Clamp(outbreakAllyShare, 0.1f, 1.5f);
            Scribe_Values.Look(ref outbreakSecondFactionChance, "outbreakSecondFactionChance", 0.5f);
            outbreakSecondFactionChance = Mathf.Clamp01(outbreakSecondFactionChance);
            Scribe_Values.Look(ref outbreakHostileFactions, "outbreakHostileFactions", true);
            Scribe_Values.Look(ref outbreakSiteDistance, "outbreakSiteDistance", new IntRange(10, 40));
            outbreakSiteDistance.min = Mathf.Clamp(outbreakSiteDistance.min, OutbreakSiteDistanceMin, OutbreakSiteDistanceMax);
            outbreakSiteDistance.max = Mathf.Clamp(outbreakSiteDistance.max, outbreakSiteDistance.min, OutbreakSiteDistanceMax);
            Scribe_Values.Look(ref outbreakFactionHalfDistance, "outbreakFactionHalfDistance", 15f);
            outbreakFactionHalfDistance = Mathf.Clamp(outbreakFactionHalfDistance, 5f, OutbreakFactionHalfDistanceOff);
            Scribe_Values.Look(ref outbreakAllyGoodwill, "outbreakAllyGoodwill", 20);
            outbreakAllyGoodwill = Mathf.Clamp(outbreakAllyGoodwill, 0, 50);
            Scribe_Values.Look(ref outbreakFailGoodwill, "outbreakFailGoodwill", 10);
            outbreakFailGoodwill = Mathf.Clamp(outbreakFailGoodwill, 0, 50);
            Scribe_Values.Look(ref outbreakUncontainedDays, "outbreakUncontainedDays", 45f);
            outbreakUncontainedDays = Mathf.Clamp(outbreakUncontainedDays, 0f, 120f);
            Scribe_Values.Look(ref outbreakSpreadFrequency, "outbreakSpreadFrequency", 1f);
            outbreakSpreadFrequency = Mathf.Clamp(outbreakSpreadFrequency, 0f, 3f);
        }

        // ---- window --------------------------------------------------------------

        private enum Tab { Lights, Traits, Generation, Materials, World, Quests }
        private static Tab tab = Tab.Lights;

        public void DoWindowContents(Rect inRect)
        {
            // TabDrawer draws the strip above the rect it is given, so the body starts one
            // tab height down; the bottom 40 px hold the reset button.
            var body = new Rect(inRect.x, inRect.y + TabDrawer.TabHeight, inRect.width, inRect.height - TabDrawer.TabHeight - 40f);
            var tabs = new List<TabRecord>
            {
                new TabRecord("BF_Settings_LightsSection".Translate(), () => tab = Tab.Lights, tab == Tab.Lights),
                new TabRecord("BF_Settings_TraitsSection".Translate(), () => tab = Tab.Traits, tab == Tab.Traits),
                new TabRecord("BF_Settings_GenerationSection".Translate(), () => tab = Tab.Generation, tab == Tab.Generation),
                new TabRecord("BF_Settings_MaterialsSection".Translate(), () => tab = Tab.Materials, tab == Tab.Materials),
                new TabRecord("BF_Settings_WorldSection".Translate(), () => tab = Tab.World, tab == Tab.World),
                new TabRecord("BF_Settings_QuestsSection".Translate(), () => tab = Tab.Quests, tab == Tab.Quests),
            };
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);

            if (tab == Tab.Generation) DoGeneration(body.ContractedBy(12f));
            else if (tab == Tab.World) DoWorld(body.ContractedBy(12f));
            else if (tab == Tab.Quests) DoQuests(body.ContractedBy(12f));
            else if (tab == Tab.Traits) DoTraits(body.ContractedBy(12f));
            else
            {
                var l = new Listing_Standard();
                l.Begin(body.ContractedBy(12f));
                if (tab == Tab.Lights) DoLights(l);
                else DoMaterials(l);
                l.End();
            }

            var reset = new Rect(inRect.xMax - 160f, inRect.yMax - 32f, 160f, 32f);
            if (Widgets.ButtonText(reset, "BF_Settings_Reset".Translate()))
                Reset();
        }

        private void DoLights(Listing_Standard l)
        {
            Check(l, "BF_Settings_Lights", ref lightsEnabled, "BF_Settings_LightsHint");
            float follow = lightFollowCells;
            Slider(l, "BF_Settings_LightFollow", ref follow, 1f, 5f, "BF_Settings_LightFollowValue".Translate(Mathf.RoundToInt(follow)), "BF_Settings_LightFollowHint", 1f);
            lightFollowCells = Mathf.RoundToInt(follow);
            Slider(l, "BF_Settings_LightRadius", ref lightRadiusFactor, 0.25f, 2f, lightRadiusFactor.ToStringPercent(), null, 0.05f);
            Check(l, "BF_Settings_Effects", ref effectsEnabled, "BF_Settings_EffectsHint");
        }

        // Behaviour on the left, health and value on the right: in one column the hints
        // push the last sliders past the bottom of the settings window.
        private void DoTraits(Rect r)
        {
            const float gap = 18f;
            float half = (r.width - gap) / 2f;

            var l = new Listing_Standard();
            l.Begin(new Rect(r.x, r.y, half, r.height));
            Slider(l, "BF_Settings_Inherit", ref inheritChanceFactor, 0f, 2f, inheritChanceFactor.ToStringPercent(), "BF_Settings_InheritHint", 0.05f);
            Check(l, "BF_Settings_DiscoveryMessages", ref discoveryMessages, null);
            Check(l, "BF_Settings_AbilityAI", ref abilityAI, "BF_Settings_AbilityAIHint");
            Check(l, "BF_Settings_UntrainedKnownAbilities", ref untrainedKnownAbilities, "BF_Settings_UntrainedKnownAbilitiesHint");
            Check(l, "BF_Settings_TrainedAbilitiesOnOwn", ref trainedAbilitiesOnOwn, "BF_Settings_TrainedAbilitiesOnOwnHint");
            float names = maxNameTraits;
            Slider(l, "BF_Settings_MaxNameTraits", ref names, 0f, MaxNameTraitsLimit, Mathf.RoundToInt(names).ToString(), "BF_Settings_MaxNameTraitsHint", 1f);
            maxNameTraits = Mathf.RoundToInt(names);
            l.End();

            l = new Listing_Standard();
            l.Begin(new Rect(r.x + half + gap, r.y, half, r.height));
            Slider(l, "BF_Settings_HealthPerPoint", ref healthPerPoint, 0f, 0.5f, "+" + healthPerPoint.ToStringPercent(), "BF_Settings_HealthPerPointHint", 0.01f);
            Slider(l, "BF_Settings_ValuePercent", ref valuePercentPerPoint, 0f, 1f, "+" + valuePercentPerPoint.ToStringPercent(), "BF_Settings_ValuePercentHint", 0.01f);
            Slider(l, "BF_Settings_ValueFlat", ref valueFlatPerPoint, 0f, 500f, "+" + valueFlatPerPoint.ToStringMoney(), "BF_Settings_ValueFlatHint", 5f);
            Slider(l, "BF_Settings_Mystery", ref mysteryTradeBonus, 0f, 1f, "+" + mysteryTradeBonus.ToStringPercent(), "BF_Settings_MysteryHint", 0.05f);
            Slider(l, "BF_Settings_ScaledAttackCap", ref scaledAttackCap, 4f, ScaledAttackNoCap,
                scaledAttackCap >= ScaledAttackNoCap ? "BF_Settings_ScaledAttackNoCap".Translate().ToString() : Mathf.RoundToInt(scaledAttackCap).ToString(),
                "BF_Settings_ScaledAttackCapHint", 1f);
            l.End();
        }

        private static readonly Color[] RarityColors =
        {
            new Color(0.81f, 0.81f, 0.81f), new Color(0.48f, 0.82f, 0.48f), new Color(0.42f, 0.66f, 1f),
            new Color(0.76f, 0.48f, 1f), new Color(1f, 0.70f, 0.28f),
        };

        private const int MaxTraitsLimit = 15;

        // Three columns of generation controls (target, drawbacks, utility roll), then one
        // column per rarity table. Laid out side by side so the whole tab fits without a
        // scroll view.
        private void DoGeneration(Rect r)
        {
            const float gap = 18f;
            float third = (r.width - 2f * gap) / 3f;

            var left = new Listing_Standard();
            left.Begin(new Rect(r.x, r.y, third, r.height));
            var row = left.GetRect(Text.LineHeight);
            Widgets.Label(row, "BF_Settings_PointTarget".Translate());
            var rangeRect = left.GetRect(32f);
            Widgets.FloatRange(rangeRect, "BF_Settings_PointTarget".GetHashCode(), ref pointTarget, 0f, 30f,
                valueStyle: ToStringStyle.FloatMaxOne, sliderLabelFont: GameFont.Tiny, roundTo: 0.5f);
            Hint(left, "BF_Settings_PointTargetHint".Translate());
            Slider(left, "BF_Settings_Strictness", ref overshootStrictness, 0f, 4f, overshootStrictness.ToStringByStyle(ToStringStyle.FloatMaxOne), "BF_Settings_StrictnessHint", 0.1f);
            float traits = maxTraits;
            Slider(left, "BF_Settings_MaxTraits", ref traits, 1f, MaxTraitsLimit, Mathf.RoundToInt(traits).ToString(), null, 1f);
            maxTraits = Mathf.RoundToInt(traits);
            float leftHeight = left.CurHeight;
            left.End();

            var right = new Listing_Standard();
            right.Begin(new Rect(r.x + third + gap, r.y, third, r.height));
            Slider(right, "BF_Settings_DrawbackChance", ref drawbackChance, 0f, 1f, drawbackChance.ToStringPercent(), null, 0.05f);
            float drawbacks = maxDrawbacks;
            Slider(right, "BF_Settings_MaxDrawbacks", ref drawbacks, 0f, 5f, Mathf.RoundToInt(drawbacks).ToString(), "BF_Settings_DrawbackHint", 1f);
            maxDrawbacks = Mathf.RoundToInt(drawbacks);
            Slider(right, "BF_Settings_RarityShift", ref rarityShiftChance, 0f, 1f, rarityShiftChance.ToStringPercent(), "BF_Settings_RarityShiftHint", 0.05f);
            float rightHeight = right.CurHeight;
            right.End();

            var util = new Listing_Standard();
            util.Begin(new Rect(r.x + 2f * (third + gap), r.y, third, r.height));
            Slider(util, "BF_Settings_UtilityChance", ref utilityChance, 0f, 1f, utilityChance.ToStringPercent(), null, 0.05f);
            Slider(util, "BF_Settings_UtilityBudget", ref utilityBudget, 0f, 1f, utilityBudget.ToStringPercent(), "BF_Settings_UtilityHint", 0.05f);
            float utilHeight = util.CurHeight;
            util.End();

            float top = r.y + Mathf.Max(leftHeight, Mathf.Max(rightHeight, utilHeight)) + 10f;
            var area = new Rect(r.x, top, third, r.yMax - top);
            float totalWeight = rarityWeights.Sum();
            RarityColumn(area, "BF_Settings_RarityWeights", "BF_Settings_RarityWeightsHint", rarityWeights, 0f, 100f, 1f,
                v => Mathf.RoundToInt(v) + " (" + (totalWeight > 0f ? v / totalWeight : 0f).ToStringPercent() + ")");
            area.x += third + gap;
            RarityColumn(area, "BF_Settings_RarityStrength", "BF_Settings_RarityStrengthHint", rarityStrength, 0.1f, 5f, 0.05f,
                v => "x" + v.ToStringByStyle(ToStringStyle.FloatMaxTwo));
            area.x += third + gap;
            RarityColumn(area, "BF_Settings_RarityPoints", "BF_Settings_RarityPointsHint", rarityPoints, 0.5f, 20f, 0.5f,
                v => v.ToStringByStyle(ToStringStyle.FloatMaxOne));
        }

        private static void RarityColumn(Rect area, string titleKey, string hintKey, float[] values, float min, float max, float roundTo, Func<float, string> shown)
        {
            var l = new Listing_Standard();
            l.Begin(area);
            Text.Font = GameFont.Small;
            l.Label("<b>" + titleKey.Translate() + "</b>");
            Hint(l, hintKey.Translate());
            // One row per rarity: name, value, slider. Two rows each would not fit.
            for (int i = 0; i < BastleRarityUtility.Count; i++)
            {
                var row = l.GetRect(24f);
                l.Gap(2f);
                var labelRect = new Rect(row.x, row.y, row.width * 0.3f, row.height);
                var valueRect = new Rect(labelRect.xMax, row.y, row.width * 0.25f, row.height);
                var sliderRect = new Rect(valueRect.xMax + 6f, row.y, row.xMax - valueRect.xMax - 6f, row.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = RarityColors[i];
                Widgets.Label(labelRect, ((BastleRarity)i).Label().CapitalizeFirst());
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Widgets.Label(valueRect, shown(values[i]));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                float v = Widgets.HorizontalSlider(sliderRect, values[i], min, max, middleAlignment: true);
                values[i] = Mathf.Clamp(GenMath.RoundTo(v, roundTo), min, max);
            }
            l.End();
        }

        private void DoMaterials(Listing_Standard r)
        {
            Slider(r, "BF_Settings_Armor", ref materialArmorFactor, 0f, 1.5f, materialArmorFactor.ToStringPercent(),
                "BF_Settings_ArmorHint".Translate((0.9f * materialArmorFactor).ToStringPercent()), 0.05f);
            Slider(r, "BF_Settings_Damage", ref materialDamageStrength, 0f, 2f, materialDamageStrength.ToStringPercent(), "BF_Settings_DamageHint", 0.05f);
            Slider(r, "BF_Settings_Fire", ref fireWeaknessStrength, 0f, 2f, fireWeaknessStrength.ToStringPercent(),
                "BF_Settings_FireHint".Translate((1f + 0.5f * fireWeaknessStrength).ToStringByStyle(ToStringStyle.FloatMaxTwo),
                    Mathf.Max(0f, 1f - 0.5f * fireWeaknessStrength).ToStringByStyle(ToStringStyle.FloatMaxTwo)), 0.05f);
            Slider(r, "BF_Settings_Butcher", ref butcherYieldPerBodySize, 0f, 200f, Mathf.RoundToInt(butcherYieldPerBodySize).ToString(),
                "BF_Settings_ButcherHint".Translate(Mathf.RoundToInt(2.15f * butcherYieldPerBodySize)), 1f);
            Slider(r, "BF_Settings_Precious", ref smallVolumeMultiplier, 1f, 20f, "x" + Mathf.RoundToInt(smallVolumeMultiplier), "BF_Settings_PreciousHint", 1f);
            Check(r, "BF_Settings_MarketValue", ref materialMarketValue, "BF_Settings_MarketValueHint");
            Slider(r, "BF_Settings_EatMaterial", ref materialNutritionPerUnit, 0f, 0.5f, materialNutritionPerUnit.ToStringByStyle(ToStringStyle.FloatMaxTwo),
                materialNutritionPerUnit > 0f
                    ? "BF_Settings_EatMaterialHint".Translate(Mathf.CeilToInt(1f / materialNutritionPerUnit)).ToString()
                    : "BF_Settings_EatMaterialOffHint".Translate().ToString(), 0.01f);
        }

        // Strength curve | animals the game already makes | new events. The per-source
        // explanations sit in tooltips: written out, the middle column runs off the window.
        private void DoWorld(Rect r)
        {
            const float gap = 18f;
            var top = new Listing_Standard();
            top.Begin(r);
            bool masterOn = Source(top, BastleWorldFeature.World, "BF_Settings_WorldEnabled", ref worldEnabled, false, true);
            Hint(top, "BF_Settings_WorldEnabledHint".Translate());
            float used = top.CurHeight + 4f;
            top.End();

            var body = new Rect(r.x, r.y + used, r.width, r.height - used);
            float third = (body.width - 2f * gap) / 3f;
            Color before = GUI.color;
            if (!masterOn) GUI.color = new Color(1f, 1f, 1f, 0.5f);

            var l = new Listing_Standard();
            l.Begin(new Rect(body.x, body.y, third, body.height));
            l.Label("<b>" + "BF_Settings_WorldStrength".Translate() + "</b>");
            Hint(l, "BF_Settings_WorldStrengthHint".Translate());
            DrawWorldCurve(l.GetRect(54f));
            l.Gap(4f);
            for (int i = 0; i < worldCurvePoints.Length; i++)
                Slider(l, "BF_Settings_WorldCurveRow".Translate(BastleWorld.CurveThreatPoints[i].ToString("N0")), ref worldCurvePoints[i], 0f, 20f,
                    worldCurvePoints[i].ToStringByStyle(ToStringStyle.FloatMaxOne), null, 0.5f);
            Slider(l, "BF_Settings_WorldVariance", ref worldPointVariance, 0f, 0.9f, "±" + worldPointVariance.ToStringPercent(), null, 0.05f);
            Slider(l, "BF_Settings_WildStrength", ref wildStrength, 0f, 2f, wildStrength.ToStringPercent(), "BF_Settings_WildStrengthHint", 0.05f);
            Slider(l, "BF_Settings_ConvertedStrength", ref convertedStrength, 0f, 2f, convertedStrength.ToStringPercent(), "BF_Settings_ConvertedStrengthHint", 0.05f);
            Slider(l, "BF_Settings_OfferedStrength", ref offeredStrength, 0f, 2f, offeredStrength.ToStringPercent(), "BF_Settings_OfferedStrengthHint", 0.05f);
            l.End();

            l = new Listing_Standard();
            l.Begin(new Rect(body.x + third + gap, body.y, third, body.height));
            l.Label("<b>" + "BF_Settings_WorldConversion".Translate() + "</b>");
            Hint(l, "BF_Settings_WorldHiddenHint".Translate());
            if (Source(l, BastleWorldFeature.WildAnimals, "BF_Settings_Wild", ref wildEnabled))
                Slider(l, "BF_Settings_ChancePerAnimal", ref wildChance, 0f, 0.25f, wildChance.ToStringPercent("0.#"), null, 0.005f);
            if (Source(l, BastleWorldFeature.AnimalEvents, "BF_Settings_AnimalEvents", ref animalEventsEnabled))
                Slider(l, "BF_Settings_ChancePerEvent", ref animalEventChance, 0f, 1f, animalEventChance.ToStringPercent(), null, 0.01f);
            if (Source(l, BastleWorldFeature.GroupAnimals, "BF_Settings_GroupAnimals", ref groupAnimalsEnabled))
            {
                Slider(l, "BF_Settings_ChancePerGroup", ref groupAnimalChance, 0f, 1f, groupAnimalChance.ToStringPercent(), null, 0.01f);
                float max = groupAnimalMax;
                Slider(l, "BF_Settings_MostPerGroup", ref max, 1f, 10f, Mathf.RoundToInt(max).ToString(), null, 1f);
                groupAnimalMax = Mathf.RoundToInt(max);
            }
            if (Source(l, BastleWorldFeature.TraderAnimals, "BF_Settings_TraderAnimals", ref traderAnimalsEnabled))
                Slider(l, "BF_Settings_ChancePerAnimal", ref traderAnimalChance, 0f, 1f, traderAnimalChance.ToStringPercent(), null, 0.01f);
            l.End();

            l = new Listing_Standard();
            l.Begin(new Rect(body.x + 2f * (third + gap), body.y, third, body.height));
            l.Label("<b>" + "BF_Settings_WorldEvents".Translate() + "</b>");
            Hint(l, "BF_Settings_WorldEventsHint".Translate());
            if (Source(l, BastleWorldFeature.Sighting, "BF_Settings_Sighting", ref sightingEnabled))
            {
                Frequency(l, ref sightingFrequency);
                Slider(l, "BF_Settings_EventStrength", ref sightingStrength, 0.1f, 4f, sightingStrength.ToStringPercent(), null, 0.05f);
            }
            if (Source(l, BastleWorldFeature.Pack, "BF_Settings_Pack", ref packEnabled))
            {
                Frequency(l, ref packFrequency);
                Slider(l, "BF_Settings_PackTraitShare", ref packTraitShare, 0f, 0.8f, packTraitShare.ToStringPercent(), null, 0.05f);
            }
            if (Source(l, BastleWorldFeature.Rampage, "BF_Settings_Rampage", ref rampageEnabled))
            {
                Frequency(l, ref rampageFrequency);
                Slider(l, "BF_Settings_EventStrength", ref rampageStrength, 0.1f, 4f, rampageStrength.ToStringPercent(), null, 0.05f);
            }
            l.End();

            GUI.color = before;
        }

        // Hunt quest | outbreak quest. Moved off the World tab when the outbreak's settings
        // would have run its events column off the window; the World master switch still
        // governs both, so the columns dim while it is off.
        private void DoQuests(Rect r)
        {
            const float gap = 18f;
            var top = new Listing_Standard();
            top.Begin(r);
            Hint(top, "BF_Settings_QuestsHint".Translate());
            float used = top.CurHeight + 4f;
            top.End();

            var body = new Rect(r.x, r.y + used, r.width, r.height - used);
            float third = (body.width - 2f * gap) / 3f;
            Color before = GUI.color;
            if (!(BastleWorld.GetForGame(BastleWorldFeature.World) ?? worldEnabled))
                GUI.color = new Color(1f, 1f, 1f, 0.5f);

            var l = new Listing_Standard();
            l.Begin(new Rect(body.x, body.y, third, body.height));
            if (Source(l, BastleWorldFeature.HuntQuest, "BF_Settings_HuntQuest", ref huntQuestEnabled))
            {
                Frequency(l, ref huntQuestFrequency);
                Slider(l, "BF_Settings_EventStrength", ref huntStrength, 0.1f, 4f, huntStrength.ToStringPercent(), null, 0.05f);
                Slider(l, "BF_Settings_HuntAlphaShare", ref huntAlphaShare, 0f, 0.8f, huntAlphaShare.ToStringPercent(), null, 0.05f);
            }
            l.End();

            // The outbreak takes two columns: one would run off the bottom of the window.
            l = new Listing_Standard();
            l.Begin(new Rect(body.x + third + gap, body.y, third, body.height));
            bool outbreakOn = Source(l, BastleWorldFeature.OutbreakQuest, "BF_Settings_OutbreakQuest", ref outbreakQuestEnabled);
            if (outbreakOn)
            {
                Frequency(l, ref outbreakQuestFrequency);
                SubHead(l, "BF_Settings_OutbreakSection");
                Slider(l, "BF_Settings_OutbreakSize", ref outbreakSize, 1f, 5f, "x" + outbreakSize.ToStringByStyle(ToStringStyle.FloatMaxTwo), null, 0.25f);
                Slider(l, "BF_Settings_OutbreakStrength", ref outbreakStrength, 0.1f, 4f, outbreakStrength.ToStringPercent(), null, 0.05f);
                Slider(l, "BF_Settings_OutbreakAlphaStrength", ref outbreakAlphaStrength, 0.1f, 4f, outbreakAlphaStrength.ToStringPercent(), null, 0.05f);
                Slider(l, "BF_Settings_PackTraitShare", ref outbreakTraitShare, 0f, 0.8f, outbreakTraitShare.ToStringPercent(), null, 0.05f);
                Slider(l, "BF_Settings_OutbreakGrowth", ref outbreakGrowth, 0f, 0.25f, outbreakGrowth.ToStringPercent(), null, 0.01f);
                Slider(l, "BF_Settings_OutbreakGrowthCap", ref outbreakGrowthCap, 1f, 5f, "x" + outbreakGrowthCap.ToStringByStyle(ToStringStyle.FloatMaxTwo), null, 0.25f);
                Rect distLabel = l.GetRect(Text.LineHeight);
                Widgets.Label(distLabel, "BF_Settings_OutbreakSiteDistance".Translate());
                TooltipHandler.TipRegion(distLabel, "BF_Settings_OutbreakSiteDistanceHint".Translate());
                Widgets.IntRange(l.GetRect(32f), 581720394, ref outbreakSiteDistance, OutbreakSiteDistanceMin, OutbreakSiteDistanceMax);
                l.Gap(l.verticalSpacing);
            }
            l.End();

            l = new Listing_Standard();
            l.Begin(new Rect(body.x + 2f * (third + gap), body.y, third, body.height));
            if (outbreakOn)
            {
                SubHead(l, "BF_Settings_OutbreakAlliesSection");
                Slider(l, "BF_Settings_OutbreakAllyShare", ref outbreakAllyShare, 0.1f, 1.5f, outbreakAllyShare.ToStringPercent(), null, 0.05f);
                Slider(l, "BF_Settings_OutbreakSecondFaction", ref outbreakSecondFactionChance, 0f, 1f, outbreakSecondFactionChance.ToStringPercent(), null, 0.05f);
                l.CheckboxLabeled("BF_Settings_OutbreakHostileFactions".Translate(), ref outbreakHostileFactions, "BF_Settings_OutbreakHostileFactionsHint".Translate());
                float halfTop = l.CurHeight;
                Slider(l, "BF_Settings_OutbreakFactionHalfDistance", ref outbreakFactionHalfDistance, 5f, OutbreakFactionHalfDistanceOff,
                    outbreakFactionHalfDistance >= OutbreakFactionHalfDistanceOff ? "BF_Settings_NoPreference".Translate().ToString() : "BF_Settings_Tiles".Translate(Mathf.RoundToInt(outbreakFactionHalfDistance)).ToString(),
                    null, 1f);
                TooltipHandler.TipRegion(new Rect(0f, halfTop, l.ColumnWidth, l.CurHeight - halfTop), "BF_Settings_OutbreakFactionHalfDistanceHint".Translate());
                float allyGoodwill = outbreakAllyGoodwill;
                Slider(l, "BF_Settings_OutbreakAllyGoodwill", ref allyGoodwill, 0f, 50f, "+" + Mathf.RoundToInt(allyGoodwill), null, 1f);
                outbreakAllyGoodwill = Mathf.RoundToInt(allyGoodwill);
                SubHead(l, "BF_Settings_OutbreakFailSection");
                float failGoodwill = outbreakFailGoodwill;
                Slider(l, "BF_Settings_OutbreakFailGoodwill", ref failGoodwill, 0f, 50f, (-Mathf.RoundToInt(failGoodwill)).ToString(), null, 1f);
                outbreakFailGoodwill = Mathf.RoundToInt(failGoodwill);
                Slider(l, "BF_Settings_OutbreakUncontainedDays", ref outbreakUncontainedDays, 0f, 120f,
                    outbreakUncontainedDays > 0f ? Mathf.RoundToInt(outbreakUncontainedDays).ToString() : "BF_Settings_Never".Translate().ToString(), null, 1f);
                Slider(l, "BF_Settings_OutbreakSpread", ref outbreakSpreadFrequency, 0f, 3f, "x" + outbreakSpreadFrequency.ToStringByStyle(ToStringStyle.FloatMaxTwo), null, 0.05f);
            }
            l.End();

            GUI.color = before;
        }

        private static void SubHead(Listing_Standard l, string key)
        {
            l.Gap(4f);
            Text.Font = GameFont.Tiny;
            Color before = GUI.color;
            GUI.color = Color.gray * new Color(1f, 1f, 1f, before.a);
            l.Label(key.Translate().ToString().ToUpper());
            GUI.color = before;
            Text.Font = GameFont.Small;
        }

        // A world source's switch, its explanation in the tooltip; the source's own sliders
        // are only drawn while it is on. While the open game overrides the switch, the box
        // shows the game's value, greyed and locked, with a line saying where to change it;
        // the settings' own value is kept for new games. The sliders show while either the
        // game or the settings has the source on, and stay editable: numbers are never per game. The master switch
        // asks for the game's value instead, since it dims the columns below it.
        private static bool Source(Listing_Standard l, BastleWorldFeature feature, string key, ref bool value, bool tooltip = true, bool returnGameValue = false)
        {
            l.Gap(6f);
            string tip = tooltip ? (key + "Hint").Translate().ToString() : null;
            bool? forGame = BastleWorld.GetForGame(feature);
            if (!forGame.HasValue)
            {
                l.CheckboxLabeled(key.Translate(), ref value, tip);
                return value;
            }
            bool shown = forGame.Value;
            string label = key.Translate();
            Rect row = l.GetRect(Text.CalcHeight(label, l.ColumnWidth));
            if (tip != null) TooltipHandler.TipRegion(row, tip);
            Widgets.CheckboxLabeled(row, label, ref shown, disabled: true);
            l.Gap(l.verticalSpacing);
            string source = BastleWorld.OverrideSourceForGame;
            Hint(l, source != null
                ? "BF_Settings_WorldOverriddenBy".Translate(source).ToString()
                : "BF_Settings_WorldOverridden".Translate().ToString());
            return returnGameValue ? shown : shown || value;
        }

        private static void Frequency(Listing_Standard l, ref float value)
            => Slider(l, "BF_Settings_Frequency", ref value, 0f, 3f, "x" + value.ToStringByStyle(ToStringStyle.FloatMaxTwo), null, 0.05f);

        private void DrawWorldCurve(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.25f));
            Widgets.DrawBox(rect);
            float maxY = Mathf.Max(1f, worldCurvePoints.Max());
            float maxX = BastleWorld.CurveThreatPoints[BastleWorld.CurveThreatPoints.Length - 1];
            Vector2 Point(int i) => new Vector2(
                rect.x + 2f + (rect.width - 4f) * BastleWorld.CurveThreatPoints[i] / maxX,
                rect.yMax - 2f - (rect.height - 4f) * worldCurvePoints[i] / maxY);
            var color = new Color(1f, 0.8f, 0.4f);
            Widgets.DrawLine(new Vector2(rect.x + 2f, Point(0).y), Point(0), color, 2f);
            for (int i = 1; i < worldCurvePoints.Length; i++)
                Widgets.DrawLine(Point(i - 1), Point(i), color, 2f);
        }

        private static void Hint(Listing_Standard l, string text)
        {
            if (text.NullOrEmpty()) return;
            Text.Font = GameFont.Tiny;
            Color before = GUI.color;
            GUI.color = Color.gray * new Color(1f, 1f, 1f, before.a);
            l.Label(text);
            GUI.color = before;
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
            => Slider(l, key.Translate(), ref value, min, max, shown, hint, roundTo);

        private static void Slider(Listing_Standard l, TaggedString label, ref float value, float min, float max, string shown, string hint, float roundTo)
        {
            var row = l.GetRect(Text.LineHeight);
            Widgets.Label(row.LeftPart(0.75f), label);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(row.RightPart(0.25f), shown);
            Text.Anchor = TextAnchor.UpperLeft;
            value = l.Slider(value, min, max);
            value = Mathf.Clamp(GenMath.RoundTo(value, roundTo), min, max);
            if (hint != null) Hint(l, hint.CanTranslate() ? hint.Translate().ToString() : hint);
        }
    }
}
