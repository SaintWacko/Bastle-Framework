using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BastleFramework
{
    /// <summary>
    /// Vanilla Expanded Framework's weird eaters (CompEatWeirdFood: the leadbelly, the decay
    /// drake...) run their own think tree whose only food job is VEF's JobGiver_GetWeirdFood,
    /// and VEF's prefix stops vanilla's JobGiver_GetFood for them anywhere else. That job finds
    /// its list of things by def and never asks FoodIsSuitable, so without this a weird eater's
    /// trait diet did nothing. VEF is looked up by name and stays optional.
    /// </summary>
    internal static class BastleVEF
    {
        internal static readonly Type WeirdFoodGiver = AccessTools.TypeByName("VEF.AnimalBehaviours.JobGiver_GetWeirdFood");
        internal static readonly Type WeirdFoodComp = AccessTools.TypeByName("VEF.AnimalBehaviours.CompProperties_EatWeirdFood");

        private static readonly Dictionary<ThingDef, bool> weirdEater = new Dictionary<ThingDef, bool>();

        /// <summary>Whether VEF feeds this creature through its own job. Cached per race.</summary>
        internal static bool IsWeirdEater(Pawn pawn)
        {
            if (WeirdFoodComp == null || pawn == null) return false;
            var def = pawn.def;
            if (weirdEater.TryGetValue(def, out bool weird)) return weird;
            weird = false;
            if (def.comps != null)
                for (int i = 0; i < def.comps.Count; i++)
                    if (WeirdFoodComp.IsInstanceOfType(def.comps[i])) { weird = true; break; }
            weirdEater[def] = weird;
            return weird;
        }
    }

    /// <summary>A weird eater's trait diet, applied after VEF's own pick: food a trait refuses
    /// is dropped, and when VEF found nothing it can eat, food its traits add is the fallback.
    /// VEF's choice comes first, so the trait search only runs when VEF's came up empty.</summary>
    [HarmonyPatch]
    internal static class Patch_VEF_GetWeirdFood
    {
        private static AccessTools.FieldRef<object, HungerCategory> minCategory;
        private static AccessTools.FieldRef<object, float> maxLevelPercentage;
        private static AccessTools.FieldRef<object, List<string>> thingsToEat;
        private static MethodInfo findWeirdFood;

        static bool Prepare()
        {
            if (BastleVEF.WeirdFoodGiver == null) return false;
            // A VEF update that renames these would make the fallback fire at any hunger;
            // leave the patch off rather than guess.
            findWeirdFood = AccessTools.Method(BastleVEF.WeirdFoodGiver, "FindWeirdFoodInMap");
            if (AccessTools.Field(BastleVEF.WeirdFoodGiver, "minCategory") == null
                || AccessTools.Field(BastleVEF.WeirdFoodGiver, "maxLevelPercentage") == null
                || BastleVEF.WeirdFoodComp == null || AccessTools.Field(BastleVEF.WeirdFoodComp, "customThingToEat") == null
                || findWeirdFood == null)
            {
                Log.Warning("[Bastle Framework] Vanilla Expanded Framework's JobGiver_GetWeirdFood has changed; trait diets don't apply to its weird eaters.");
                return false;
            }
            minCategory = AccessTools.FieldRefAccess<HungerCategory>(BastleVEF.WeirdFoodGiver, "minCategory");
            maxLevelPercentage = AccessTools.FieldRefAccess<float>(BastleVEF.WeirdFoodGiver, "maxLevelPercentage");
            thingsToEat = AccessTools.FieldRefAccess<List<string>>(BastleVEF.WeirdFoodComp, "customThingToEat");
            return AccessTools.Method(BastleVEF.WeirdFoodGiver, "TryGiveJob") != null;
        }

        static MethodBase TargetMethod() => AccessTools.Method(BastleVEF.WeirdFoodGiver, "TryGiveJob");

        // VEF stops at the first thing on its list it can reach. When a trait refuses that
        // one, carry on down the list as VEF would have, so a refusal works like a normal
        // diet's instead of starving the creature beside the next thing on the list.
        private static Job NextWeirdFoodJob(object giver, Pawn pawn, BastleDiet d)
        {
            var ingestWeird = DefDatabase<JobDef>.GetNamedSilentFail("VEF_IngestWeird");
            if (ingestWeird == null) return null;
            var comps = pawn.AllComps;
            List<string> list = null;
            for (int i = 0; i < comps.Count; i++)
                if (BastleVEF.WeirdFoodComp.IsInstanceOfType(comps[i].props)) { list = thingsToEat(comps[i].props); break; }
            if (list == null) return null;
            foreach (var name in list)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def == null || d.Refuses(def)) continue;
                // Reflection is fine here: this only runs when a trait refuses VEF's pick.
                var thing = findWeirdFood.Invoke(giver, new object[] { def, pawn }) as Thing;
                if (thing == null || !pawn.CanReserve(thing)) continue;
                var job = JobMaker.MakeJob(ingestWeird, thing);
                job.count = 1;
                return job;
            }
            return null;
        }

        static void Postfix(object __instance, Pawn pawn, ref Job __result)
        {
            if (pawn?.Map == null) return;
            var bastle = BastleUtility.PeekBastle(pawn);
            var d = bastle?.Diet;
            if (d == null) return;
            var target = __result?.targetA.Thing;
            if (target != null && d.Refuses(target.def)) __result = NextWeirdFoodJob(__instance, pawn, d);
            if (__result != null) return;

            var food = pawn.needs?.food;
            if (food == null || food.CurCategory < minCategory(__instance) || food.CurLevelPercentage > maxLevelPercentage(__instance)) return;
            __result = BastleDietUtility.EatAddedFoodJob(pawn, d);
            if (__result == null && d.nonFood.Count > 0) __result = BastleDietUtility.EatNonFoodJob(pawn, d);
            if (__result == null && food.CurCategory == HungerCategory.Starving)
                BastleDietUtility.TryDiscoverRefusal(pawn, bastle);
        }
    }
}
