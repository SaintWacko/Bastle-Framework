using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Info card rows for a bastle, in the Basics category beside "Race": its material,
    /// and one "Bastle traits" row listing every trait. The row's tooltip gives each
    /// discovered trait's description, effects, diet and market value. Undiscovered
    /// traits share a single "???", the same as the hediff tooltip and inspect line.
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
            var bastle = pawn == null ? null : BastleUtility.PeekBastle(pawn);
            if (bastle == null) yield break;

            if (bastle.Material != null)
                yield return new StatDrawEntry(StatCategoryDefOf.BasicsPawn, "BF_Card_Material".Translate(),
                    bastle.Material.LabelCap, "BF_MaterialTipDesc".Translate(), Priority, null,
                    new[] { new Dialog_InfoCard.Hyperlink(bastle.Material) });

            if (bastle.Traits.Count == 0) yield break;
            var labels = new List<string>();
            bool anyHidden = false;
            foreach (var t in bastle.Traits)
            {
                if (t.discovered) labels.Add(t.Label);
                else anyHidden = true;
            }
            if (anyHidden) labels.Add("BF_UnknownTrait".Translate());
            yield return new StatDrawEntry(StatCategoryDefOf.BasicsPawn, "BF_Card_Traits".Translate(),
                string.Join(", ", labels), TraitsTip(pawn, bastle), Priority - 1);
        }

        private static string TraitsTip(Pawn pawn, Hediff_Bastle bastle)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BF_Card_TraitsIntro".Translate());
            float baseValue = BastleValueUtility.BaseValue(pawn);
            bool anyHidden = false;
            foreach (var t in bastle.Traits)
            {
                if (!t.discovered) { anyHidden = true; continue; }
                sb.AppendLine();
                sb.AppendLine(t.Label.Colorize(ColoredText.TipSectionTitleColor));
                if (!t.Description.NullOrEmpty()) sb.AppendLine(t.Description);
                if (t.Stage != null)
                    foreach (StatDrawEntry e in HediffStatsUtility.SpecialDisplayStats(t.Stage, null))
                        if (e.ShouldDisplay())
                            sb.AppendLine(" - " + e.LabelCap + ": " + e.ValueString);
                string traitHealth = BastleHealth.DescribeTrait(t);
                if (traitHealth != null) sb.AppendLine(" - " + traitHealth);
                foreach (string line in BastleDiet.Describe(t.Diet))
                    sb.AppendLine(" - " + line);
                string extra = t.TipStringExtra;
                if (!extra.NullOrEmpty()) sb.AppendLine(" - " + extra);
                float value = BastleValueUtility.TraitValue(t, baseValue);
                if (value != 0f)
                    sb.AppendLine(" - " + "BF_Card_TraitValue".Translate((value > 0f ? "+" : "") + value.ToStringMoney()));
            }
            if (anyHidden)
            {
                sb.AppendLine();
                sb.AppendLine("BF_UnknownTrait".Translate().ToString().Colorize(ColoredText.SubtleGrayColor));
                sb.AppendLine("BF_UnknownTraitDesc".Translate());
            }
            string health = BastleHealth.DescribeTotal(bastle.Traits);
            if (health != null) sb.AppendLine().AppendLine(health);
            if (anyHidden && BastleFrameworkMod.Settings.mysteryTradeBonus > 0f)
                sb.AppendLine().AppendLine("BF_Card_Mystery".Translate(BastleFrameworkMod.Settings.mysteryTradeBonus.ToStringPercent()));
            return sb.ToString().TrimEndNewlines();
        }
    }
}
