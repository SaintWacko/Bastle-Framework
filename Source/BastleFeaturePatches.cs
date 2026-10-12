using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Draw size (BastleTraitDef.drawSizeFactor). Every render node's matrix is built
    /// from the draw parms' root matrix each frame, so scaling that one matrix scales the
    /// whole creature, offsets included, around its centre. Runs on the parallel pre-draw
    /// threads: PeekBastle plus a plain float read. Portraits and the humanlike render
    /// cache draw into fixed-size frames and are left alone.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class Patch_PawnRenderer_GetDrawParms
    {
        public static void Postfix(ref PawnDrawParms __result)
        {
            var pawn = __result.pawn;
            if (pawn == null || pawn.RaceProps.Humanlike) return;
            if ((__result.flags & (PawnRenderFlags.Portrait | PawnRenderFlags.Cache)) != 0) return;
            var bastle = BastleUtility.PeekBastle(pawn);
            if (bastle == null) return;
            float s = bastle.DrawScale;
            if (s == 1f) return;
            __result.matrix *= Matrix4x4.Scale(new Vector3(s, 1f, s));
        }
    }

    /// <summary>Deflection (BastleTraitDef.deflect). Pawn.PreApplyDamage calls this base
    /// method (thing comps) first and only runs the health tracker's step when nothing
    /// absorbed the hit, so a deflected hit causes no stagger, stun, clamor or thoughts.
    /// It also comes before shield belts, which are checked in that later step. Runs for
    /// every damaged thing with comps, so non-pawns leave after one type check.</summary>
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.PreApplyDamage))]
    public static class Patch_ThingWithComps_PreApplyDamage
    {
        public static void Postfix(ThingWithComps __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            if (absorbed || !(__instance is Pawn pawn)) return;
            var bastle = BastleUtility.GetBastle(pawn);
            if (bastle != null && BastleCombatUtility.TryDeflect(bastle, dinfo)) absorbed = true;
        }
    }

    /// <summary>Per-creature wool (BastleTraitDef.products). Only read when shearing.</summary>
    [HarmonyPatch(typeof(CompShearable), "ResourceDef", MethodType.Getter)]
    public static class Patch_CompShearable_ResourceDef
    {
        public static void Postfix(CompShearable __instance, ref ThingDef __result)
        {
            var bastle = BastleUtility.GetBastle(__instance.parent as Pawn);
            if (bastle == null) return;
            foreach (var t in bastle.Traits)
                if (t.Products?.woolDef != null) __result = t.Products.woolDef;
        }
    }

    [HarmonyPatch(typeof(CompShearable), "ResourceAmount", MethodType.Getter)]
    public static class Patch_CompShearable_ResourceAmount
    {
        public static void Postfix(CompShearable __instance, ref int __result)
        {
            var bastle = BastleUtility.GetBastle(__instance.parent as Pawn);
            if (bastle == null) return;
            float f = 1f;
            foreach (var t in bastle.Traits)
                if (t.Products != null) f *= t.Products.woolAmountFactor;
            if (f != 1f) __result = Mathf.Max(1, Mathf.RoundToInt(__result * f));
        }
    }

    [HarmonyPatch(typeof(CompMilkable), "ResourceDef", MethodType.Getter)]
    public static class Patch_CompMilkable_ResourceDef
    {
        public static void Postfix(CompMilkable __instance, ref ThingDef __result)
        {
            var bastle = BastleUtility.GetBastle(__instance.parent as Pawn);
            if (bastle == null) return;
            foreach (var t in bastle.Traits)
                if (t.Products?.milkDef != null) __result = t.Products.milkDef;
        }
    }

    [HarmonyPatch(typeof(CompMilkable), "ResourceAmount", MethodType.Getter)]
    public static class Patch_CompMilkable_ResourceAmount
    {
        public static void Postfix(CompMilkable __instance, ref int __result)
        {
            var bastle = BastleUtility.GetBastle(__instance.parent as Pawn);
            if (bastle == null) return;
            float f = 1f;
            foreach (var t in bastle.Traits)
                if (t.Products != null) f *= t.Products.milkAmountFactor;
            if (f != 1f) __result = Mathf.Max(1, Mathf.RoundToInt(__result * f));
        }
    }
}
