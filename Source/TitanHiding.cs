using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Keeps undiscovered traits out of the info card entirely, the way Entad Framework
    /// keeps unrevealed traits off an item's card. While a titan's stats report draws,
    /// its titan hediff answers with a stage built from discovered traits only, and the
    /// hediffs that only hidden traits grant answer with no stage at all. The card's
    /// values and explanations are therefore computed as if the hidden traits did not
    /// exist. Gameplay is untouched: everything the card computed is dropped from the
    /// stat caches as soon as the card finishes drawing.
    ///
    /// Capacities and hediff-set totals (pain, bleeding) have their own caches that
    /// a card read would fill with hidden-free values, so they are warmed with the
    /// real values before hiding starts and the card only ever reads them.
    /// </summary>
    public static class TitanHiding
    {
        // Main thread only: set and cleared around one DrawStatsReport call. A plain
        // static so the hot CurStage postfix pays one null check when no card is open.
        internal static Pawn cardPawn;
        internal static readonly HashSet<Hediff> hiddenHediffs = new HashSet<Hediff>();

        internal static bool HidingFor(Pawn p) => cardPawn != null && cardPawn == p && UnityData.IsInMainThread;

        internal static bool Begin(Thing thing)
        {
            if (cardPawn != null || !(thing is Pawn p)) return false;
            var titan = TitanUtility.GetTitan(p);
            if (titan == null || !titan.AnyHiddenTrait) return false;

            // Real values first, so the cached ones the card reads are the true ones.
            var caps = DefDatabase<PawnCapacityDef>.AllDefsListForReading;
            for (int i = 0; i < caps.Count; i++) p.health.capacities.GetLevel(caps[i]);
            _ = p.health.hediffSet.PainTotal;
            _ = p.health.hediffSet.BleedRateTotal;

            titan.CollectHiddenHediffs(hiddenHediffs);
            titan.cardStage = null;
            ClearStatCaches(p);
            cardPawn = p;
            return true;
        }

        internal static void End()
        {
            var p = cardPawn;
            cardPawn = null;
            hiddenHediffs.Clear();
            if (p == null) return;
            var titan = TitanUtility.GetTitan(p);
            if (titan != null) titan.cardStage = null;
            ClearStatCaches(p);
        }

        private static void ClearStatCaches(Pawn p)
        {
            var stats = DefDatabase<StatDef>.AllDefsListForReading;
            for (int i = 0; i < stats.Count; i++) stats[i].Worker.ClearCacheForThing(p);
        }
    }

    [HarmonyPatch(typeof(StatsReportUtility), nameof(StatsReportUtility.DrawStatsReport), new[] { typeof(Rect), typeof(Thing) })]
    internal static class Patch_DrawStatsReport_Titan
    {
        static void Prefix(Thing thing, out bool __state) => __state = TitanHiding.Begin(thing);

        // Finalizer so hiding always ends, even if drawing throws.
        static void Finalizer(bool __state)
        {
            if (__state) TitanHiding.End();
        }
    }

    /// <summary>Hediffs granted only by hidden traits have no stage while their titan's
    /// card draws. Hediff_Titan overrides CurStage and handles itself.</summary>
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.CurStage), MethodType.Getter)]
    internal static class Patch_HediffCurStage_Hiding
    {
        static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            if (TitanHiding.cardPawn == null) return;
            if (TitanHiding.hiddenHediffs.Contains(__instance) && UnityData.IsInMainThread) __result = null;
        }
    }

    /// <summary>No gizmo for an ability whose only source is an undiscovered trait. The AI
    /// can still cast it, and that reveals the trait.</summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.GizmosVisible))]
    internal static class Patch_AbilityGizmosVisible
    {
        static void Postfix(Ability __instance, ref bool __result)
        {
            if (!__result) return;
            var titan = TitanUtility.GetTitan(__instance.pawn);
            if (titan != null && titan.AbilityOnlyFromHiddenTraits(__instance.def)) __result = false;
        }
    }
}
