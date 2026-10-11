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
    /// Traits granted by the material count like any other. Utility traits' points don't
    /// count, so a pack beast isn't secretly tougher. Each healthScaleFactor is
    /// floored at 0.25 before it multiplies, and so is the total.
    /// </summary>
    public static class TitanHealth
    {
        // Drawback traits can lower health, but never below this fraction.
        private const float MinFactor = 0.25f;

        /// <param name="discoveredOnly">For display: hidden traits stay out of the
        /// numbers, like everywhere else on the card.</param>
        public static float Factor(List<TitanTrait> traits, bool discoveredOnly = false)
        {
            float points = 0f, scale = 1f;
            foreach (var t in traits)
            {
                if (discoveredOnly && !t.discovered) continue;
                if (!t.def.IsUtility) points += TitanFrameworkMod.Settings.healthPerPoint * t.Points;
                // Floored per trait, so one harsh trait can't wipe out every other bonus.
                scale *= Mathf.Max(MinFactor, t.HealthScaleFactor);
            }
            return Mathf.Max(MinFactor, Mathf.Max(0f, 1f + points) * scale);
        }

        /// <summary>The trait's own healthScaleFactor as a line ("Health multiplier: x1.3"),
        /// or null when it has none. Worded apart from the total, which also counts points.</summary>
        public static string DescribeTrait(TitanTrait t)
        {
            float f = Mathf.Max(MinFactor, t.HealthScaleFactor);
            if (Mathf.Abs(f - 1f) < 0.005f) return null;
            return "TF_TraitHealthLine".Translate("x" + f.ToStringByStyle(ToStringStyle.FloatMaxTwo));
        }

        /// <summary>One tooltip line for the whole titan ("Health: +30%"), counting
        /// discovered traits only, or null when they don't change it.</summary>
        public static string DescribeTotal(List<TitanTrait> traits)
        {
            float change = Factor(traits, discoveredOnly: true) - 1f;
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
