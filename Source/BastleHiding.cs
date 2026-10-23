using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Keeps undiscovered traits out of the info card entirely, the way Entad Framework
    /// keeps unrevealed traits off an item's card. While a bastle's stats report draws,
    /// its bastle hediff answers with a stage built from discovered traits only, and the
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
    public static class BastleHiding
    {
        // Main thread only: set and cleared around one DrawStatsReport call. A plain
        // static so the hot CurStage postfix pays one null check when no card is open.
        internal static Pawn cardPawn;
        internal static readonly HashSet<Hediff> hiddenHediffs = new HashSet<Hediff>();

        internal static bool HidingFor(Pawn p) => cardPawn != null && cardPawn == p && UnityData.IsInMainThread;

        internal static bool Begin(Thing thing)
        {
            if (cardPawn != null || !(thing is Pawn p)) return false;
            var bastle = BastleUtility.GetBastle(p);
            if (bastle == null || !bastle.AnyHiddenTrait) return false;

            // Real totals first, so the cached ones the card reads are the true ones.
            _ = p.health.hediffSet.PainTotal;
            _ = p.health.hediffSet.BleedRateTotal;

            bastle.CollectHiddenHediffs(hiddenHediffs);
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
    internal static class Patch_DrawStatsReport_Bastle
    {
        static void Prefix(Thing thing, out bool __state) => __state = BastleHiding.Begin(thing);

        // Finalizer so hiding always ends, even if drawing throws.
        static void Finalizer(bool __state)
        {
            if (__state) BastleHiding.End();
        }
    }

    /// <summary>Hediffs granted only by hidden traits have no stage while their bastle's
    /// card draws. Hediff_Bastle overrides CurStage and handles itself.</summary>
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.CurStage), MethodType.Getter)]
    internal static class Patch_HediffCurStage_Hiding
    {
        static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            if (BastleHiding.cardPawn == null) return;
            if (UnityData.IsInMainThread && BastleHiding.hiddenHediffs.Contains(__instance)) __result = null;
        }
    }

    /// <summary>No gizmo for an ability whose only source is an undiscovered trait (the AI
    /// can still cast it, and that reveals the trait), nor for a known one on an animal
    /// that hasn't learned BF_TraitAbilities.</summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.GizmosVisible))]
    internal static class Patch_AbilityGizmosVisible
    {
        static void Postfix(Ability __instance, ref bool __result)
        {
            if (!__result) return;
            var bastle = BastleUtility.GetBastle(__instance.pawn);
            if (bastle == null) return;
            var control = BastleAbilityTraining.ControlOf(bastle, __instance.pawn, __instance.def);
            if (control == BastleAbilityControl.Hidden || control == BastleAbilityControl.Untrained) __result = false;
        }
    }

    /// <summary>
    /// Vanilla shows an ability's button on an undrafted pawn only when its def sets
    /// displayGizmoWhileUndrafted. Animals can never be drafted, so a trained animal
    /// bastle never saw its buttons (test run #3). This adds the buttons vanilla skipped
    /// for abilities a known trait grants, on bastles that have no drafter: the AbilityDef
    /// itself is shared with every other pawn that has it (a content mod's trait may grant
    /// a vanilla ability), so the def is left as its author wrote it. Pawns that can be
    /// drafted keep vanilla's draft-only buttons.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.GetGizmos))]
    internal static class Patch_AbilityTrackerGizmos_Undrafted
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn_AbilityTracker __instance)
        {
            foreach (var g in __result) yield return g;

            var pawn = __instance.pawn;
            if (pawn == null || pawn.drafter != null) yield break;
            // Mirrors GetGizmos' own tests: who gets buttons at all, and the dev-gizmo branch
            // that already yielded every ability for non-colonist pawns.
            if (!(pawn.IsColonySubhumanPlayerControlled || pawn.IsColonyAnimal)) yield break;
            if (DebugSettings.ShowDevGizmos && !pawn.IsColonistPlayerControlled && !pawn.IsColonyMechPlayerControlled) yield break;
            var bastle = BastleUtility.GetBastle(pawn);
            if (bastle == null) yield break;

            var abilities = __instance.AllAbilitiesForReading;
            for (int i = 0; i < abilities.Count; i++)
            {
                var a = abilities[i];
                if (a.def.displayGizmoWhileUndrafted) continue;
                if (BastleAbilityTraining.ControlOf(bastle, pawn, a.def) != BastleAbilityControl.Player) continue;
                if (!a.GizmosVisible()) continue;
                foreach (var c in a.GetGizmos())
                {
                    // The command is cached on the ability; vanilla resets this on every
                    // yield, and one shown in dev mode before taming would keep "DEV:".
                    if (c is Command_Ability ca) ca.devGizmo = false;
                    yield return c;
                }
            }
        }

        internal static bool Undraftable(Pawn pawn, Ability ability)
        {
            if (pawn == null || pawn.drafter != null || ability.def.displayGizmoWhileUndrafted) return false;
            var bastle = BastleUtility.GetBastle(pawn);
            return bastle != null && BastleAbilityTraining.ControlOf(bastle, pawn, ability.def) == BastleAbilityControl.Player;
        }
    }

    /// <summary>
    /// Vanilla also greys out every ability on an undrafted pawn (disableGizmoWhileUndrafted
    /// defaults on), which would leave the buttons added above unpressable. For those
    /// abilities only, that one reason is dropped and the checks vanilla runs after it
    /// are run here instead.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.GizmoDisabled))]
    internal static class Patch_AbilityGizmoDisabled_Undrafted
    {
        static void Postfix(Ability __instance, ref bool __result, ref string reason)
        {
            if (!__result || !__instance.def.disableGizmoWhileUndrafted) return;
            var pawn = __instance.pawn;
            if (pawn == null || pawn.drafter != null) return;
            // Translated per call, not cached: the language can change mid-session.
            if (reason != "AbilityDisabledUndrafted".Translate()) return;
            if (!Patch_AbilityTrackerGizmos_Undrafted.Undraftable(pawn, __instance)) return;

            if (pawn.DevelopmentalStage.Baby())
                reason = "IsIncapped".Translate(pawn.LabelShort, pawn);
            else if (pawn.Downed)
                reason = "CommandDisabledUnconscious".TranslateWithBackup("CommandCallRoyalAidUnconscious").Formatted(pawn);
            else if (pawn.Deathresting)
                reason = "CommandDisabledDeathresting".Translate(pawn);
            else if (__instance.def.casterMustBeCapableOfViolence && pawn.WorkTagIsDisabled(WorkTags.Violent))
                reason = "IsIncapableOfViolence".Translate(pawn.LabelShort, pawn);
            else if (!__instance.CanQueueCast)
                reason = "AbilityAlreadyQueued".Translate();
            else
            {
                reason = null;
                __result = false;
            }
        }
    }
}
