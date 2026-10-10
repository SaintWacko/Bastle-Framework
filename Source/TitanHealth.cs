using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Titans are tougher than their kind. Each trait multiplies the creature's health
    /// scale (which sets every body part's hit points, the lethal damage threshold and
    /// how fast injuries heal) by (1 + healthPerPoint × pointCost) × healthScaleFactor.
    /// Hidden traits count: a creature that takes too many hits to drop is a clue.
    /// Traits granted by the material skip the per-point part; the material's armor is
    /// its toughness. Their own healthScaleFactor still applies, since a content mod
    /// set it on purpose.
    /// </summary>
    public static class TitanHealth
    {
        // Drawback traits can lower health, but never below this fraction.
        private const float MinFactor = 0.25f;

        public static float TraitFactor(TitanTrait t)
        {
            float perPoint = t.fromMaterial ? 1f : 1f + TitanFrameworkMod.Settings.healthPerPoint * t.def.pointCost;
            // Clamped per trait, so one harsh drawback can't zero out every other bonus.
            return Mathf.Max(MinFactor, perPoint * t.def.healthScaleFactor);
        }

        public static float Factor(List<TitanTrait> traits)
        {
            float f = 1f;
            foreach (var t in traits) f *= TraitFactor(t);
            return Mathf.Max(MinFactor, f);
        }

        /// <summary>Tooltip line ("Health: +20%"), or null when the trait doesn't change it.</summary>
        public static string Describe(TitanTrait t)
        {
            float change = TraitFactor(t) - 1f;
            if (Mathf.Abs(change) < 0.005f) return null;
            return "TF_HealthLine".Translate((change > 0f ? "+" : "") + change.ToStringPercent());
        }
    }

    /// <summary>Called for every body part's max health, every injury's pain and
    /// bleeding, so it only peeks at the titan cache.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.HealthScale), MethodType.Getter)]
    internal static class Patch_Pawn_HealthScale
    {
        static void Postfix(Pawn __instance, ref float __result)
        {
            var titan = TitanUtility.PeekTitan(__instance);
            if (titan != null) __result *= titan.HealthFactor;
        }
    }
}
