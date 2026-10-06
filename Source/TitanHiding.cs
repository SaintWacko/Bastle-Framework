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
    /// exist. The pawn's stat caches are cleared before the card draws (so nested
    /// reads can't serve real cached values) and again after (so gameplay never sees
    /// a value computed without the hidden traits).
    ///
    /// Capacities (moving, manipulation...) feed many card stats, so they are marked
    /// dirty on both sides of the card: the card computes them without hidden traits,
    /// and gameplay recomputes the real ones on its next read.
    ///
    /// Not hidden: pain and bleeding totals (cached by the hediff set, which can't be
    /// dirtied cheaply, so they are warmed with real values first and only read),
    /// effects from a hidden hediff's comps, and hidden hediffs whose class overrides
    /// CurStage without calling base.
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

            // Real totals first, so the cached ones the card reads are the true ones.
            _ = p.health.hediffSet.PainTotal;
            _ = p.health.hediffSet.BleedRateTotal;

            titan.CollectHiddenHediffs(hiddenHediffs);
            cardPawn = p;
            p.health.capacities.Notify_CapacityLevelsDirty();
            // Nested stat reads can serve cached values; drop the real ones gameplay cached.
            ClearStatCaches(p);
            return true;
        }

        internal static void End()
        {
            var p = cardPawn;
            cardPawn = null;
            hiddenHediffs.Clear();
            if (p == null) return;
            p.health.capacities.Notify_CapacityLevelsDirty();
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
            if (UnityData.IsInMainThread && TitanHiding.hiddenHediffs.Contains(__instance)) __result = null;
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
