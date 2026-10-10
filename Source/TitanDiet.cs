using System;
using System.Collections.Generic;
using System.Linq;
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
    ///                                    it. Unset: unchanged. Removing Corpse (on any
    ///                                    trait) always stops hunting, whatever hunts says
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
                var p = t.Diet;
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
            // Removing Corpse stops hunting even when another trait says it hunts:
            // vanilla's hunt job eats the kill without checking the diet.
            if (d != null && (d.remove & FoodTypeFlags.Corpse) != 0) d.hunts = false;
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

        /// <summary>The race's food types with this diet applied.</summary>
        public FoodTypeFlags Apply(FoodTypeFlags race) => (race | add) & ~remove;

        /// <summary>Specific things it eats beyond its food types, as one list.</summary>
        public string ExtraFoodsLabel()
        {
            var labels = new List<string>();
            foreach (var t in edible) labels.Add(t.label);
            foreach (var t in nonFood.Keys) labels.Add(t.label);
            return labels.Count > 0 ? string.Join(", ", labels) : null;
        }

        /// <summary>Whether this trait's diet is what lets the creature eat the food: an
        /// extraFoods thing, or an added food type its race doesn't already eat.</summary>
        public static bool TraitAdds(TitanDietProps p, ThingDef food, Pawn pawn)
        {
            if (p == null || food == null) return false;
            var type = food.ingestible?.foodType ?? FoodTypeFlags.None;
            // Ordinary food the race eats anyway says nothing about the trait.
            bool raceEats = food.IsNutritionGivingIngestible && (type & pawn.RaceProps.foodType) != 0;
            if (p.extraFoods != null && !raceEats)
                foreach (var f in p.extraFoods)
                    if (f.thing == food) return true;
            return (type & p.addFoodTypes & ~pawn.RaceProps.foodType) != 0;
        }

        /// <summary>Whether this trait's diet refuses a food the creature's race would eat.</summary>
        public static bool TraitRefuses(TitanDietProps p, ThingDef food, Pawn pawn)
        {
            if (p == null || food?.ingestible == null) return false;
            if ((food.ingestible.foodType & pawn.RaceProps.foodType) == 0) return false;
            if (p.refuseFoods != null && p.refuseFoods.Contains(food)) return true;
            return (food.ingestible.foodType & p.removeFoodTypes) != 0;
        }

        /// <summary>Tooltip lines for one trait's diet, without bullets.</summary>
        public static IEnumerable<string> Describe(TitanDietProps p)
        {
            if (p == null) yield break;
            if (p.addFoodTypes != 0) yield return "TF_DietAdds".Translate(p.addFoodTypes.ToHumanString());
            if (p.removeFoodTypes != 0) yield return "TF_DietRemoves".Translate(p.removeFoodTypes.ToHumanString());
            if (!p.extraFoods.NullOrEmpty())
            {
                var labels = new List<string>();
                foreach (var f in p.extraFoods)
                    if (f.thing != null && (f.thing.IsNutritionGivingIngestible || f.nutrition > 0f)) labels.Add(f.thing.label);
                if (labels.Count > 0) yield return "TF_DietAdds".Translate(string.Join(", ", labels));
            }
            if (!p.refuseFoods.NullOrEmpty())
            {
                var labels = new List<string>();
                foreach (var f in p.refuseFoods)
                    if (f != null) labels.Add(f.label);
                if (labels.Count > 0) yield return "TF_DietRefuses".Translate(string.Join(", ", labels));
            }
            if (p.hunts == true && (p.removeFoodTypes & FoodTypeFlags.Corpse) == 0) yield return "TF_DietHunts".Translate();
            else if (p.hunts == false || (p.removeFoodTypes & FoodTypeFlags.Corpse) != 0) yield return "TF_DietNoHunt".Translate();
        }
    }

    /// <summary>The info card's Diet line shows the creature's own diet: its race's,
    /// changed by its material and discovered traits. Undiscovered traits stay out.</summary>
    [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.SpecialDisplayStats))]
    internal static class Patch_RaceSpecialDisplayStats_Diet
    {
        static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, RaceProperties __instance, StatRequest req)
        {
            var pawn = req.Pawn ?? req.Thing as Pawn;
            var titan = pawn == null ? null : TitanUtility.PeekTitan(pawn);
            TitanDiet diet = null;
            if (titan != null && !(pawn.IsMutant && pawn.mutant.Def.overrideFoodType))
            {
                var known = new List<TitanTrait>();
                foreach (var t in titan.Traits)
                    if (t.discovered) known.Add(t);
                diet = TitanDiet.Build(known, titan.Material);
            }
            string dietLabel = diet == null ? null : "Diet".Translate().CapitalizeFirst();
            foreach (var e in __result)
            {
                if (diet != null && e.category == StatCategoryDefOf.PawnFood && e.LabelCap == dietLabel)
                {
                    string value = diet.Apply(__instance.foodType).ToHumanString().CapitalizeFirst();
                    string extra = diet.ExtraFoodsLabel();
                    if (extra != null) value = value.NullOrEmpty() ? extra.CapitalizeFirst() : value + ", " + extra;
                    string desc = "Stat_Race_Diet_Desc".Translate(value) + "\n\n" + "TF_DietCardNote".Translate();
                    yield return new StatDrawEntry(StatCategoryDefOf.PawnFood, "Diet".Translate(), value, desc, e.DisplayPriorityWithinCategory);
                    continue;
                }
                yield return e;
            }
        }
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
                null, 0, TitanDietUtility.MaxRegions(getter, forceScanWholeMap));
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
            var titan = TitanUtility.PeekTitan(pawn);
            var d = titan?.Diet;
            var food = pawn.needs?.food;
            if (d == null || food == null) return;
            if (d.nonFood.Count > 0 && food.CurCategory >= MinCategory(__instance) && food.CurLevelPercentage <= MaxLevelPercentage(__instance))
                __result = TitanDietUtility.EatNonFoodJob(pawn, d);
            if (__result == null && food.CurCategory == HungerCategory.Starving)
                TitanDietUtility.TryDiscoverRefusal(pawn, titan);
        }
    }

    public static class TitanDietUtility
    {
        private static readonly Func<Pawn, bool, int> maxRegions = MakeMaxRegions();

        // Falls back to vanilla's plain animal limit if a game update renames the method,
        // rather than breaking every food search through a failed static constructor.
        private static Func<Pawn, bool, int> MakeMaxRegions()
        {
            var m = AccessTools.Method(typeof(FoodUtility), "GetMaxRegionsToScan", new[] { typeof(Pawn), typeof(bool) });
            if (m != null)
                try { return AccessTools.MethodDelegate<Func<Pawn, bool, int>>(m); } catch { }
            Log.Warning("[Titan Framework] FoodUtility.GetMaxRegionsToScan not found; titan food searches use vanilla's default limits (100 regions for colony animals, 30 for others).");
            return (p, all) => p.RaceProps.Humanlike || all ? -1 : (p.Faction == Faction.OfPlayer ? 100 : 30);
        }

        internal static int MaxRegions(Pawn getter, bool forceScanWholeMap) => maxRegions(getter, forceScanWholeMap);

        private const float RefusalRadius = 25f;
        private const int RefusalCheckInterval = 250;

        /// <summary>A starving titan that found nothing to eat: if food one of its hidden
        /// traits refuses is within reach, that trait is revealed. Throttled per creature,
        /// and only searched when some hidden trait could be revealed this way.</summary>
        internal static void TryDiscoverRefusal(Pawn pawn, Hediff_Titan titan)
        {
            int now = Find.TickManager.TicksGame;
            if (now < titan.nextRefusalCheck) return;
            titan.nextRefusalCheck = now + RefusalCheckInterval;

            List<TitanTrait> pending = null;
            foreach (var t in titan.TraitsForReading)
            {
                var p = t.Diet;
                if (t.discovered || p == null || !t.def.DiscoveredBy(TitanDiscoveryTrigger.StarvesNearRefusedFood)) continue;
                if (p.refuseFoods.NullOrEmpty() && p.removeFoodTypes == 0) continue;
                (pending ?? (pending = new List<TitanTrait>())).Add(t);
            }
            if (pending == null || !TitanUtility.PlayerCanObserve(pawn)) return;

            Thing refused = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSource), PathEndMode.Touch, TraverseParms.For(pawn),
                RefusalRadius, x => x.def.ingestible != null && !x.IsForbidden(pawn) && x.IngestibleNow && WithinRope(pawn, x)
                    && pending.Any(t => TitanDiet.TraitRefuses(t.Diet, x.def, pawn)));
            if (refused == null) return;
            foreach (var t in pending)
                if (TitanDiet.TraitRefuses(t.Diet, refused.def, pawn))
                    t.TryDiscoverBy(TitanDiscoveryTrigger.StarvesNearRefusedFood);
        }

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
                    var eaten = t.def;
                    t.SplitOff(n).Destroy();
                    if (pawn.needs?.food != null) pawn.needs.food.CurLevel += n * per;
                    TitanUtility.GetTitan(pawn)?.Notify_Ate(eaten);
                },
                defaultCompleteMode = ToilCompleteMode.Instant,
            };
        }
    }
}
