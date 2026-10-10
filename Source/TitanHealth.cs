using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Titans are tougher than their kind. The creature's health scale (which sets every
    /// body part's hit points, the lethal damage threshold and how fast injuries heal)
    /// is multiplied by (1 + healthPerPoint × total trait points), then by each trait's
    /// healthScaleFactor. Points are totalled first, so drawbacks cancel bonuses and a
    /// big titan grows linearly rather than compounding.
    /// Hidden traits count: a creature that takes too many hits to drop is a clue.
    /// Traits granted by the material add no points; the material's armor is its
    /// toughness. Their own healthScaleFactor still applies, since a content mod set it
    /// on purpose.
    /// </summary>
    public static class TitanHealth
    {
        // Drawback traits can lower health, but never below this fraction.
        private const float MinFactor = 0.25f;

        /// <summary>This trait's share of the points part: healthPerPoint × pointCost.</summary>
        public static float PointShare(TitanTrait t) =>
            t.fromMaterial ? 0f : TitanFrameworkMod.Settings.healthPerPoint * t.def.pointCost;

        public static float Factor(List<TitanTrait> traits)
        {
            float points = 0f, scale = 1f;
            foreach (var t in traits)
            {
                points += PointShare(t);
                scale *= t.def.healthScaleFactor;
            }
            return Mathf.Max(MinFactor, Mathf.Max(0f, 1f + points) * scale);
        }

        /// <summary>Tooltip line ("Health: +10%", "Health: +10%, ×1.5"), or null when
        /// the trait doesn't change it.</summary>
        public static string Describe(TitanTrait t)
        {
            float share = PointShare(t);
            float scale = t.def.healthScaleFactor;
            bool hasShare = Mathf.Abs(share) >= 0.005f, hasScale = Mathf.Abs(scale - 1f) >= 0.005f;
            if (!hasShare && !hasScale) return null;
            string text = hasShare ? (share > 0f ? "+" : "") + share.ToStringPercent() : null;
            if (hasScale) text = (text == null ? "" : text + ", ") + "×" + scale.ToString("0.##");
            return "TF_HealthLine".Translate(text);
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
