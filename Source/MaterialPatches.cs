using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TitanFramework
{
    // Construct bodies. Every patch here starts with a registry lookup, which is a
    // single weak-table miss for ordinary pawns.

    /// <summary>A creature made of stone leaves no blood.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodFilth))]
    public static class Patch_Pawn_HealthTracker_DropBloodFilth
    {
        public static bool Prefix(Pawn ___pawn) => !TitanUtility.IsConstruct(___pawn);
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodSmear))]
    public static class Patch_Pawn_HealthTracker_DropBloodSmear
    {
        public static bool Prefix(Pawn ___pawn) => !TitanUtility.IsConstruct(___pawn);
    }

    /// <summary>A construct's corpse doesn't rot. CompRottable.disabled is public and
    /// saved, so it is set once when the corpse is made.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.MakeCorpse), new[] { typeof(Building_Grave), typeof(bool), typeof(float) })]
    public static class Patch_Pawn_MakeCorpse
    {
        public static void Postfix(Pawn __instance, Corpse __result)
        {
            if (__result == null || !TitanUtility.IsConstruct(__instance)) return;
            var rot = __result.TryGetComp<CompRottable>();
            if (rot != null) rot.disabled = true;
        }
    }

    /// <summary>Nobody eats a steel wolf.</summary>
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.IngestibleNow), MethodType.Getter)]
    public static class Patch_Corpse_IngestibleNow
    {
        public static void Postfix(Corpse __instance, ref bool __result)
        {
            if (__result && TitanUtility.IsConstruct(__instance.InnerPawn)) __result = false;
        }
    }

    /// <summary>Butchering yields the material. Meat and leather are already zero through
    /// the material stage's MeatAmount and LeatherAmount factors.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ButcherProducts))]
    public static class Patch_Pawn_ButcherProducts
    {
        public static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result, Pawn __instance, float efficiency)
        {
            foreach (var t in __result) yield return t;
            var stuff = TitanUtility.GetMaterial(__instance);
            if (stuff == null) yield break;
            int count = TitanMaterialUtility.ButcherCount(__instance, stuff, efficiency);
            while (count > 0)
            {
                var thing = ThingMaker.MakeThing(stuff);
                thing.stackCount = System.Math.Min(count, stuff.stackLimit);
                count -= thing.stackCount;
                yield return thing;
            }
        }
    }
}
