using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TitanFramework
{
    /// <summary>
    /// A trait's diet (TitanTraitDef.diet). Works per creature, on top of its race:
    ///   addFoodTypes / removeFoodTypes   food type flags, e.g. "Meat, Corpse"
    ///   refuseFoods                      specific foods it never eats
    ///   extraFoods                       specific things it also eats. Ordinary food is
    ///                                    eaten the normal way; anything that isn't food
    ///                                    (stone chunks, steel) is eaten by its own job
    ///                                    and gives `nutrition` per unit
    ///   hunts                            true makes it hunt like a predator, false stops
    ///                                    it. Unset: unchanged, except removing Corpse
    ///                                    stops hunting
    /// Refusing behaves like a normal animal's diet: a refused food is never picked, and
    /// a creature with nothing it can eat starves.
    /// </summary>
    public class TitanDietProps
    {
        public FoodTypeFlags addFoodTypes;
        public FoodTypeFlags removeFoodTypes;
        public List<ThingDef> refuseFoods;
        public List<TitanExtraFood> extraFoods;
        public bool? hunts;
    }

    public class TitanExtraFood
    {
        public ThingDef thing;
        /// <summary>Nutrition per unit. Only used for things that aren't food.</summary>
        public float nutrition;
    }

    /// <summary>A titan's merged diet: every trait's, plus its own material.</summary>
    public class TitanDiet
    {
        public FoodTypeFlags add;
        public FoodTypeFlags remove;
        public readonly HashSet<ThingDef> refuse = new HashSet<ThingDef>();
        public readonly HashSet<ThingDef> edible = new HashSet<ThingDef>();
        /// <summary>Things that aren't food, with nutrition per unit.</summary>
        public readonly Dictionary<ThingDef, float> nonFood = new Dictionary<ThingDef, float>();
        public bool? hunts;

        /// <summary>Null when nothing changes the creature's diet.</summary>
        public static TitanDiet Build(List<TitanTrait> traits, ThingDef material)
        {
            TitanDiet d = null;
            foreach (var t in traits)
            {
                var p = t.def.diet;
                if (p == null) continue;
                d = d ?? new TitanDiet();
                d.add |= p.addFoodTypes;
                d.remove |= p.removeFoodTypes;
                if (p.refuseFoods != null) d.refuse.AddRange(p.refuseFoods);
                if (p.extraFoods != null)
                    foreach (var f in p.extraFoods)
                    {
                        if (f.thing == null) continue;
                        if (f.thing.IsNutritionGivingIngestible) d.edible.Add(f.thing);
                        else if (f.nutrition > 0f)
                            d.nonFood[f.thing] = d.nonFood.TryGetValue(f.thing, out var n) ? Mathf.Max(n, f.nutrition) : f.nutrition;
                    }
                // Any trait that says "doesn't hunt" wins over one that says "hunts".
                if (p.hunts != null) d.hunts = d.hunts == false ? false : p.hunts;
            }
            float own = TitanFrameworkMod.Settings.materialNutritionPerUnit;
            if (material != null && own > 0f && !material.IsNutritionGivingIngestible)
            {
                d = d ?? new TitanDiet();
                if (!d.nonFood.ContainsKey(material)) d.nonFood[material] = own;
            }
            if (d != null && d.hunts == null && (d.remove & FoodTypeFlags.Corpse) != 0) d.hunts = false;
            return d;
        }

        public bool Refuses(ThingDef food)
        {
            if (refuse.Contains(food)) return true;
            if (edible.Contains(food)) return false;
            return food.ingestible != null && (food.ingestible.foodType & remove) != 0;
        }

        public bool Adds(ThingDef food)
            => edible.Contains(food) || (food.ingestible != null && (food.ingestible.foodType & add) != 0);

        public bool AddsPlants => (add & (FoodTypeFlags.Plant | FoodTypeFlags.Tree)) != 0;
    }

    // ---- patches ------------------------------------------------------------------

    /// <summary>The per-creature food check every eating, foraging and feeding path goes
    /// through (WillEat calls it). The race's own diet stays the base.</summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodIsSuitable))]
    internal static class Patch_FoodIsSuitable
    {
        static void Postfix(Pawn p, ThingDef food, ref bool __result)
        {
            var d = TitanUtility.PeekTitan(p)?.Diet;
            if (d == null || food == null) return;
            bool ok = __result;
            if (!ok && d.Adds(food) && p.needs?.food != null && p.RaceProps.EatsFood
                && food.ingestible != null && food.ingestible.preferability != FoodPreferability.Undefined
                && !(p.DevelopmentalStage.Baby() && !food.ingestible.babiesCanIngest))
                ok = true;
            if (ok && d.Refuses(food)) ok = false;
            __result = ok;
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.IsPotentiallyPredator))]
    internal static class Patch_IsPotentiallyPredator
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            var h = TitanUtility.PeekTitan(pawn)?.Diet?.hunts;
            if (h != null) __result = h.Value;
        }
    }

    /// <summary>Vanilla only searches plants for races that eat them, reading the race's
    /// food type directly. For a titan whose diet adds plants, look for one when the
    /// normal search found nothing.</summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap))]
    internal static class Patch_BestFoodSourceOnMap
    {
        static void Postfix(Pawn getter, Pawn eater, bool allowPlant, bool allowForbidden, bool forceScanWholeMap,
            ref ThingDef foodDef, ref Thing __result)
        {
            if (__result != null || !allowPlant || getter?.Map == null || eater == null) return;
            var d = TitanUtility.PeekTitan(eater)?.Diet;
            if (d == null || !d.AddsPlants) return;
            if ((eater.RaceProps.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.Tree)) != 0) return;

            Thing plant = GenClosest.ClosestThingReachable(getter.Position, getter.Map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSource), PathEndMode.Touch, TraverseParms.For(getter),
                9999f, t => t is Plant && t.IngestibleNow && eater.WillEat(t, getter)
                    && (allowForbidden || !t.IsForbidden(getter)) && getter.CanReserve(t)
                    && TitanDietUtility.WithinRope(getter, t),
                null, 0, forceScanWholeMap ? -1 : 30);
            if (plant == null) return;
            __result = plant;
            foodDef = FoodUtility.GetFinalIngestibleDef(plant);
        }
    }

    /// <summary>When a hungry titan finds no food, it eats things on its diet that
    /// aren't food (its own material, stone chunks...).</summary>
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    internal static class Patch_JobGiver_GetFood
    {
        private static readonly AccessTools.FieldRef<JobGiver_GetFood, HungerCategory> MinCategory =
            AccessTools.FieldRefAccess<JobGiver_GetFood, HungerCategory>("minCategory");
        private static readonly AccessTools.FieldRef<JobGiver_GetFood, float> MaxLevelPercentage =
            AccessTools.FieldRefAccess<JobGiver_GetFood, float>("maxLevelPercentage");

        static void Postfix(JobGiver_GetFood __instance, Pawn pawn, ref Job __result)
        {
            if (__result != null || pawn?.Map == null) return;
            var d = TitanUtility.PeekTitan(pawn)?.Diet;
            if (d == null || d.nonFood.Count == 0) return;
            var food = pawn.needs?.food;
            if (food == null || food.CurCategory < MinCategory(__instance) || food.CurLevelPercentage > MaxLevelPercentage(__instance)) return;
            __result = TitanDietUtility.EatNonFoodJob(pawn, d);
        }
    }

    public static class TitanDietUtility
    {
        private static readonly Func<Pawn, bool, int> MaxRegions =
            AccessTools.MethodDelegate<Func<Pawn, bool, int>>(AccessTools.Method(typeof(FoodUtility), "GetMaxRegionsToScan"));

        /// <summary>Vanilla's food search keeps a roped animal within 8 cells of its anchor.</summary>
        public static bool WithinRope(Pawn getter, Thing t)
            => getter.roping == null || !getter.roping.IsRoped || t.PositionHeld.InHorDistOf(getter.roping.RopedTo.Cell, 8f);

        public static Job EatNonFoodJob(Pawn pawn, TitanDiet d)
        {
            // Same limit vanilla's private FoodUtility.GetMaxRegionsToScan gives the food search.
            int maxRegions = MaxRegions(pawn, false);
            Thing best = null;
            float bestDist = float.MaxValue;
            float bestNutrition = 0f;
            foreach (var kv in d.nonFood)
            {
                Thing t = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(kv.Key),
                    PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                    x => !x.IsForbidden(pawn) && !x.IsBurning() && pawn.CanReserve(x) && WithinRope(pawn, x),
                    null, 0, maxRegions);
                if (t == null) continue;
                float dist = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (dist < bestDist) { best = t; bestDist = dist; bestNutrition = kv.Value; }
            }
            if (best == null) return null;
            var need = pawn.needs.food;
            int count = Mathf.Clamp(Mathf.CeilToInt((need.MaxLevel - need.CurLevel) / bestNutrition), 1, best.stackCount);
            var job = JobMaker.MakeJob(TitanDefOf.TF_EatNonFood, best);
            job.count = count;
            return job;
        }
    }

    /// <summary>Walks to the thing, chews for a while, then consumes job.count units and
    /// adds their nutrition. The thing's def is never changed into food.</summary>
    public class JobDriver_TitanEatNonFood : JobDriver
    {
        private const int TicksPerUnit = 15;
        private const int MinTicks = 300, MaxTicks = 1500;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, job.count, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            int ticks = Mathf.Clamp(job.count * TicksPerUnit, MinTicks, MaxTicks);
            var chew = Toils_General.Wait(ticks, TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return chew;
            yield return new Toil
            {
                initAction = () =>
                {
                    var t = job.targetA.Thing;
                    var d = TitanUtility.GetTitan(pawn)?.Diet;
                    if (t == null || t.Destroyed || d == null || !d.nonFood.TryGetValue(t.def, out float per)) return;
                    int n = Math.Min(job.count, t.stackCount);
                    if (n <= 0) return;
                    t.SplitOff(n).Destroy();
                    if (pawn.needs?.food != null) pawn.needs.food.CurLevel += n * per;
                },
                defaultCompleteMode = ToilCompleteMode.Instant,
            };
        }
    }
}
