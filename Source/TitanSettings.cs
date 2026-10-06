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

        // Materials
        public float materialArmorFactor = 0.5f;
        public float materialDamageStrength = 1f;
        public float fireWeaknessStrength = 1f;
        public float butcherYieldPerBodySize = 40f;
        public float smallVolumeMultiplier = 10f;
        public bool materialMarketValue = true;

        public void Reset()
        {
            lightsEnabled = true;
            lightFollowCells = 1;
            lightRadiusFactor = 1f;
            effectsEnabled = true;
            inheritChanceFactor = 1f;
            discoveryMessages = true;
            materialArmorFactor = 0.5f;
            materialDamageStrength = 1f;
            fireWeaknessStrength = 1f;
            butcherYieldPerBodySize = 40f;
            smallVolumeMultiplier = 10f;
            materialMarketValue = true;
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
            Scribe_Values.Look(ref materialArmorFactor, "materialArmorFactor", 0.5f);
            Scribe_Values.Look(ref materialDamageStrength, "materialDamageStrength", 1f);
            Scribe_Values.Look(ref fireWeaknessStrength, "fireWeaknessStrength", 1f);
            Scribe_Values.Look(ref butcherYieldPerBodySize, "butcherYieldPerBodySize", 40f);
            Scribe_Values.Look(ref smallVolumeMultiplier, "smallVolumeMultiplier", 10f);
            Scribe_Values.Look(ref materialMarketValue, "materialMarketValue", true);
        }

        // ---- window --------------------------------------------------------------

        public void DoWindowContents(Rect inRect)
        {
            const float gap = 24f;
            float colW = (inRect.width - gap) / 2f;
            var left = new Rect(inRect.x, inRect.y, colW, inRect.height - 40f);
            var right = new Rect(inRect.x + colW + gap, inRect.y, colW, inRect.height - 40f);

            var l = new Listing_Standard();
            l.Begin(left);
            Section(l, "TF_Settings_LightsSection");
            Check(l, "TF_Settings_Lights", ref lightsEnabled, "TF_Settings_LightsHint");
            float follow = lightFollowCells;
            Slider(l, "TF_Settings_LightFollow", ref follow, 1f, 5f, "TF_Settings_LightFollowValue".Translate(Mathf.RoundToInt(follow)), "TF_Settings_LightFollowHint", 1f);
            lightFollowCells = Mathf.RoundToInt(follow);
            Slider(l, "TF_Settings_LightRadius", ref lightRadiusFactor, 0.25f, 2f, lightRadiusFactor.ToStringPercent(), null, 0.05f);
            Check(l, "TF_Settings_Effects", ref effectsEnabled, "TF_Settings_EffectsHint");
            l.Gap(16f);
            Section(l, "TF_Settings_TraitsSection");
            Slider(l, "TF_Settings_Inherit", ref inheritChanceFactor, 0f, 2f, inheritChanceFactor.ToStringPercent(), "TF_Settings_InheritHint", 0.05f);
            Check(l, "TF_Settings_DiscoveryMessages", ref discoveryMessages, null);
            l.End();

            var r = new Listing_Standard();
            r.Begin(right);
            Section(r, "TF_Settings_MaterialsSection");
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
            r.End();

            var reset = new Rect(inRect.xMax - 160f, inRect.yMax - 32f, 160f, 32f);
            if (Widgets.ButtonText(reset, "TF_Settings_Reset".Translate()))
                Reset();
        }

        private static void Section(Listing_Standard l, string key)
        {
            Text.Font = GameFont.Medium;
            l.Label(key.Translate());
            Text.Font = GameFont.Small;
            l.GapLine(6f);
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
