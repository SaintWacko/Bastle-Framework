using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// What a titan's traits add to its market value. "Base value" is the creature's
    /// value before the framework touches it (vanilla's, age and health included): this
    /// part runs before the material part, so the material never inflates trait prices.
    ///
    /// The same stat feeds three readers, told apart by context:
    ///   colony wealth / raid points   every trait counts, hidden or not
    ///   trade                         discovered traits, plus a flat mystery bonus
    ///                                 when any trait is still unknown
    ///   info card                     discovered traits only
    /// MarketValue isn't a cached stat, so each reader gets a fresh number.
    /// </summary>
    public static class TitanValueUtility
    {
        /// <summary>Never price a creature below this fraction of its base value, however
        /// many drawbacks it has.</summary>
        public const float MinFractionOfBase = 0.1f;

        /// <summary>One trait's price on a creature with this base value.</summary>
        public static float TraitValue(TitanTrait t, float baseValue)
        {
            var o = t.Override;
            float? pct = o?.marketValuePercent ?? t.def.marketValuePercent;
            float? flat = o?.marketValueOffset ?? t.def.marketValueOffset;
            if (pct != null || flat != null)
            {
                // Each value grows with the points from the rarity it was written for: the
                // override's minRarity when it came from the override, else the def's own.
                float defScale = t.def.ValueScaleAt(t.Rarity);
                float overScale = defScale;
                if (o != null)
                {
                    float from = t.def.PointsAt(o.minRarity);
                    overScale = t.def.scalesWithRarity && Mathf.Abs(from) > 0.0001f ? t.Points / from : 1f;
                }
                float p = (pct ?? 0f) * (o?.marketValuePercent != null ? overScale : defScale);
                float f = (flat ?? 0f) * (o?.marketValueOffset != null ? overScale : defScale);
                return baseValue * p + f;
            }
            var s = TitanFrameworkMod.Settings;
            return t.Points * (baseValue * s.valuePercentPerPoint + s.valueFlatPerPoint);
        }

        /// <summary>Sum of trait prices. includeHidden=false leaves undiscovered traits out.</summary>
        public static float TraitsValue(Hediff_Titan titan, float baseValue, bool includeHidden)
        {
            float sum = 0f;
            foreach (var t in titan.Traits)
                if (includeHidden || t.discovered) sum += TraitValue(t, baseValue);
            return sum;
        }

        public static float MysteryValue(float baseValue) => baseValue * TitanFrameworkMod.Settings.mysteryTradeBonus;

        // ---- for content mods (dungeon difficulty, rewards...) ----------------------

        /// <summary>Sum of the creature's trait point costs. 0 for non-titans.</summary>
        public static float TraitPoints(Pawn pawn, bool includeHidden = true)
        {
            var titan = TitanUtility.GetTitan(pawn);
            if (titan == null) return 0f;
            float sum = 0f;
            foreach (var t in titan.Traits)
                if (includeHidden || t.discovered) sum += t.Points;
            return sum;
        }

        /// <summary>The creature's market value before material and traits.</summary>
        public static float BaseValue(Pawn pawn)
            => StatPart_TitanTraits.Instance?.BaseBeforeThisPart(StatRequest.For(pawn)) ?? pawn.MarketValue;

        /// <summary>What the creature's traits add to its value, with the same floor the
        /// stat uses. 0 for non-titans.</summary>
        public static float TraitsValue(Pawn pawn, bool includeHidden = true)
        {
            var titan = TitanUtility.GetTitan(pawn);
            if (titan == null) return 0f;
            float b = BaseValue(pawn);
            return Mathf.Max(TraitsValue(titan, b, includeHidden), b * MinFractionOfBase - b);
        }
    }

    /// <summary>Added to MarketValue's parts at startup, just before the material part.</summary>
    public class StatPart_TitanTraits : StatPart
    {
        internal static StatPart_TitanTraits Instance;

        private enum Mode { Wealth, Trade, Card }

        private static Mode ModeFor(Pawn pawn)
        {
            // TradeSession.trader is never cleared after a trade (nothing calls Close), so
            // the dialog being open is what marks a trade. Checked before the card, so a
            // card opened from the trade screen shows the same price as the trade row.
            if (TradeSession.Active && UnityData.IsInMainThread && Find.WindowStack != null
                && Find.WindowStack.IsOpen<Dialog_Trade>()) return Mode.Trade;
            if (TitanHiding.HidingFor(pawn)) return Mode.Card;
            return Mode.Wealth;
        }

        private static void Compute(StatRequest req, float baseValue, out float traits, out float mystery)
        {
            traits = mystery = 0f;
            if (!(req.Thing is Pawn pawn)) return;
            var titan = TitanUtility.PeekTitan(pawn);
            if (titan == null || titan.Traits.Count == 0) return;
            var mode = ModeFor(pawn);
            traits = TitanValueUtility.TraitsValue(titan, baseValue, includeHidden: mode == Mode.Wealth);
            if (mode == Mode.Trade && titan.AnyHiddenTrait) mystery = TitanValueUtility.MysteryValue(baseValue);
            // Drawbacks can't push the price under the floor.
            float min = baseValue * TitanValueUtility.MinFractionOfBase - baseValue;
            if (traits < min) traits = min;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            Compute(req, val, out float traits, out float mystery);
            val += traits + mystery;
        }

        public override string ExplanationPart(StatRequest req)
        {
            // The explanation doesn't get the running value, so rebuild the base the way
            // the stat does up to this part.
            if (!(req.Thing is Pawn pawn) || TitanUtility.PeekTitan(pawn) == null) return null;
            float baseValue = BaseBeforeThisPart(req);
            Compute(req, baseValue, out float traits, out float mystery);
            string s = null;
            if (traits != 0f) s = "TF_TraitValue".Translate() + ": " + (traits > 0f ? "+" : "") + traits.ToStringMoney();
            if (mystery > 0f) s = (s == null ? "" : s + "\n") + "TF_MysteryValue".Translate() + ": +" + mystery.ToStringMoney();
            return s;
        }

        internal float BaseBeforeThisPart(StatRequest req)
        {
            float val = parentStat.Worker.GetValueUnfinalized(req);
            var parts = parentStat.parts;
            for (int i = 0; i < parts.Count && parts[i] != this; i++)
                parts[i].TransformValue(req, ref val);
            return val;
        }
    }
}
