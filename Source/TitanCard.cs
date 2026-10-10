using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Info card rows for a titan, in the Basics category beside "Race": its material,
    /// and one "Titan traits" row listing every trait. The row's tooltip gives each
    /// discovered trait's description, effects, diet and market value. Undiscovered
    /// traits show as "???" with nothing else, the same as the hediff tooltip.
    /// </summary>
    [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.SpecialDisplayStats))]
    internal static class Patch_RaceSpecialDisplayStats_Traits
    {
        // Just after vanilla's "Race" row (4205).
        private const int Priority = 4204;

        static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, StatRequest req)
        {
            foreach (var e in __result) yield return e;

            var pawn = req.Pawn ?? req.Thing as Pawn;
            var titan = pawn == null ? null : TitanUtility.PeekTitan(pawn);
            if (titan == null) yield break;

            if (titan.Material != null)
                yield return new StatDrawEntry(StatCategoryDefOf.BasicsPawn, "TF_Card_Material".Translate(),
                    titan.Material.LabelCap, "TF_MaterialTipDesc".Translate(), Priority, null,
                    new[] { new Dialog_InfoCard.Hyperlink(titan.Material) });

            if (titan.Traits.Count == 0) yield break;
            var labels = new List<string>();
            foreach (var t in titan.Traits) labels.Add(t.Label);
            yield return new StatDrawEntry(StatCategoryDefOf.BasicsPawn, "TF_Card_Traits".Translate(),
                string.Join(", ", labels), TraitsTip(pawn, titan), Priority - 1);
        }

        private static string TraitsTip(Pawn pawn, Hediff_Titan titan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TF_Card_TraitsIntro".Translate());
            float baseValue = TitanValueUtility.BaseValue(pawn);
            bool anyHidden = false;
            foreach (var t in titan.Traits)
            {
                sb.AppendLine();
                sb.AppendLine(t.Label.Colorize(t.discovered ? ColoredText.TipSectionTitleColor : ColoredText.SubtleGrayColor));
                if (!t.discovered)
                {
                    anyHidden = true;
                    sb.AppendLine(t.Description);
                    continue;
                }
                if (!t.def.description.NullOrEmpty()) sb.AppendLine(t.def.description);
                if (t.def.stage != null)
                    foreach (StatDrawEntry e in HediffStatsUtility.SpecialDisplayStats(t.def.stage, null))
                        if (e.ShouldDisplay())
                            sb.AppendLine(" - " + e.LabelCap + ": " + e.ValueString);
                foreach (string line in TitanDiet.Describe(t.def.diet))
                    sb.AppendLine(" - " + line);
                string extra = t.TipStringExtra;
                if (!extra.NullOrEmpty()) sb.AppendLine(" - " + extra);
                float value = TitanValueUtility.TraitValue(t.def, baseValue);
                if (value != 0f)
                    sb.AppendLine(" - " + "TF_Card_TraitValue".Translate((value > 0f ? "+" : "") + value.ToStringMoney()));
            }
            string health = TitanHealth.DescribeTotal(titan.Traits);
            if (health != null) sb.AppendLine().AppendLine(health);
            if (anyHidden && TitanFrameworkMod.Settings.mysteryTradeBonus > 0f)
                sb.AppendLine().AppendLine("TF_Card_Mystery".Translate(TitanFrameworkMod.Settings.mysteryTradeBonus.ToStringPercent()));
            return sb.ToString().TrimEndNewlines();
        }
    }
}
