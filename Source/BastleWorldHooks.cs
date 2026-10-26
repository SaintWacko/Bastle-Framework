using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>Marks an incident whose animals may arrive as bastles (Patches/BF_WorldIncidents.xml
    /// adds it to vanilla's). Every animal the incident generates while it runs is converted.</summary>
    public class BastleIncidentExtension : DefModExtension
    {
        /// <summary>Chance per event; below 0 reads the World tab's.</summary>
        public float chance = -1f;
        /// <summary>Scales the event's points before the strength curve.</summary>
        public float pointFactor = 1f;
        /// <summary>One leader is rolled and every other animal is a copy of it in one discovery
        /// group. Off: each animal rolls its own traits.</summary>
        public bool onePack = true;
    }

    /// <summary>On a FactionDef: how its raids, caravans and defenders bring bastle animals.</summary>
    public class BastleFactionExtension : DefModExtension
    {
        public float groupChance = -1f;
        public int maxAnimals = -1;
        public float pointFactor = 1f;
    }

    /// <summary>On a TraderKindDef: how often its stock animals are bastles.</summary>
    public class BastleTraderExtension : DefModExtension
    {
        public float chance = -1f;
        public float pointFactor = 1f;
    }

    // The world hooks share one postfix on PawnGenerator.GeneratePawn: a wild herd or a marked
    // incident sets a scope, and every animal generated inside it is handled here. Outside any
    // scope a generated pawn costs two static reads.
    internal static class BastleWorldScopes
    {
        internal class IncidentScope
        {
            public float points;
            public bool onePack;
            public Pawn leader;
        }

        internal static Map wildMap;
        internal static bool wildConvert;
        internal static IncidentScope incident;
        internal static int groupDepth;
        // Stock animals a trader caravan carries are trader stock, not the caravan's own animals.
        internal static readonly HashSet<Pawn> stockInGroup = new HashSet<Pawn>();

        internal static void Generated(Pawn pawn)
        {
            if (wildMap != null)
            {
                // Map generation inside an incident (a caravan ambush) spawns wild animals too;
                // those are wild, not the event's.
                if (wildConvert && Rand.Chance(BastleFrameworkMod.Settings.wildChance) && BastleWorld.Eligible(pawn))
                    BastleWorld.MakeBastle(pawn, BastleWorld.RollTraitPoints(BastleWorld.ThreatPointsNow(wildMap) * BastleFrameworkMod.Settings.wildStrength));
                return;
            }
            var scope = incident;
            if (scope == null || groupDepth > 0 || !BastleWorld.Eligible(pawn)) return;
            if (scope.onePack && scope.leader != null)
            {
                BastleWorld.CopyPackLeader(scope.leader, pawn);
                return;
            }
            if (BastleWorld.MakeBastle(pawn, BastleWorld.RollTraitPoints(scope.points)) && scope.onePack) scope.leader = pawn;
        }
    }

    /// <summary>Wild animals, on a new map and as they wander in later. Every caller goes
    /// through SpawnRandomWildAnimalAt, which generates each herd member with
    /// PawnGenerator.GeneratePawn while the map is known here.</summary>
    [HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.SpawnRandomWildAnimalAt))]
    internal static class Patch_WildAnimalSpawner_SpawnRandomWildAnimalAt
    {
        static void Prefix(Map ___map, out (Map map, bool convert) __state)
        {
            __state = (BastleWorldScopes.wildMap, BastleWorldScopes.wildConvert);
            BastleWorldScopes.wildMap = ___map;
            BastleWorldScopes.wildConvert = BastleWorld.Enabled(BastleWorldFeature.WildAnimals);
        }

        static void Finalizer((Map map, bool convert) __state)
        {
            BastleWorldScopes.wildMap = __state.map;
            BastleWorldScopes.wildConvert = __state.convert;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class Patch_PawnGenerator_GeneratePawn_World
    {
        static void Postfix(Pawn __result)
        {
            if (BastleWorldScopes.wildMap == null && BastleWorldScopes.incident == null) return;
            BastleWorldScopes.Generated(__result);
        }
    }

    /// <summary>Animal incidents. TryExecute is the one entry every storyteller, quest and
    /// debug firing goes through; the chance is rolled once per event here.</summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    internal static class Patch_IncidentWorker_TryExecute_World
    {
        static void Prefix(IncidentWorker __instance, IncidentParms parms, out BastleWorldScopes.IncidentScope __state)
        {
            __state = BastleWorldScopes.incident;
            var ext = __instance.def?.GetModExtension<BastleIncidentExtension>();
            if (ext == null) return;
            BastleWorldScopes.IncidentScope scope = null;
            if (BastleWorld.Enabled(BastleWorldFeature.AnimalEvents)
                && Rand.Chance(ext.chance >= 0f ? ext.chance : BastleFrameworkMod.Settings.animalEventChance))
            {
                float points = parms.points > 0f ? parms.points : BastleWorld.ThreatPointsNow(parms.target);
                scope = new BastleWorldScopes.IncidentScope
                {
                    points = points * Mathf.Max(0f, ext.pointFactor) * BastleFrameworkMod.Settings.convertedStrength,
                    onePack = ext.onePack,
                };
            }
            // A marked incident that didn't roll still clears an outer one's scope: its animals
            // are its own.
            BastleWorldScopes.incident = scope;
        }

        static void Finalizer(BastleWorldScopes.IncidentScope __state) => BastleWorldScopes.incident = __state;
    }

    /// <summary>Pack and fighting animals in raids, caravans, visitors and settlement
    /// defenders: every group kind goes through this, once per group.</summary>
    [HarmonyPatch(typeof(PawnGroupKindWorker), nameof(PawnGroupKindWorker.GeneratePawns),
        new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool) })]
    internal static class Patch_PawnGroupKindWorker_GeneratePawns_World
    {
        // The trader worker spends parms.points on the trader and its guards as it goes; the
        // animals are read from the group's points as it was asked for.
        static void Prefix(PawnGroupMakerParms parms, out float __state)
        {
            BastleWorldScopes.groupDepth++;
            __state = parms?.points ?? 0f;
        }

        static void Postfix(PawnGroupMakerParms parms, List<Pawn> __result, float __state)
        {
            if (parms?.faction == null || parms.faction.IsPlayer || __result.NullOrEmpty()
                || !BastleWorld.Enabled(BastleWorldFeature.GroupAnimals)) return;
            try
            {
                var s = BastleFrameworkMod.Settings;
                var ext = parms.faction.def.GetModExtension<BastleFactionExtension>();
                if (!Rand.Chance(ext != null && ext.groupChance >= 0f ? ext.groupChance : s.groupAnimalChance)) return;
                var candidates = new List<Pawn>();
                foreach (var p in __result)
                    if (p.Faction == parms.faction && !BastleWorldScopes.stockInGroup.Contains(p) && BastleWorld.Eligible(p))
                        candidates.Add(p);
                if (candidates.Count == 0) return;
                int max = ext != null && ext.maxAnimals >= 0 ? ext.maxAnimals : s.groupAnimalMax;
                int count = Mathf.Min(candidates.Count, Rand.RangeInclusive(1, Mathf.Max(1, max)));
                float points = __state > 0f ? __state : BastleWorld.ThreatPointsNow(Current.Game.FindMap(parms.tile));
                points *= Mathf.Max(0f, ext?.pointFactor ?? 1f) * s.convertedStrength;
                foreach (var p in candidates.InRandomOrder())
                {
                    if (count <= 0) break;
                    if (BastleWorld.MakeBastle(p, BastleWorld.RollTraitPoints(points))) count--;
                }
            }
            // Vanilla only guards pawn creation; a throw here would lose the whole group.
            catch (Exception e) { Log.ErrorOnce("[Bastle Framework] Could not give bastles to a pawn group: " + e, 0x42465753); }
        }

        static void Finalizer()
        {
            if (--BastleWorldScopes.groupDepth <= 0)
            {
                BastleWorldScopes.groupDepth = 0;
                BastleWorldScopes.stockInGroup.Clear();
            }
        }
    }

    /// <summary>Animals for sale, for every trader: caravans, orbital ships, settlements and
    /// visitors all make their stock here.</summary>
    [HarmonyPatch(typeof(ThingSetMaker_TraderStock), "Generate", new[] { typeof(ThingSetMakerParams), typeof(List<Thing>) })]
    internal static class Patch_ThingSetMaker_TraderStock_Generate_World
    {
        static void Postfix(ThingSetMakerParams parms, List<Thing> outThings)
        {
            if (outThings.NullOrEmpty()) return;
            bool inGroup = BastleWorldScopes.groupDepth > 0;
            bool convert = BastleWorld.Enabled(BastleWorldFeature.TraderAnimals);
            if (!inGroup && !convert) return;
            try
            {
                var s = BastleFrameworkMod.Settings;
                var ext = parms.traderDef?.GetModExtension<BastleTraderExtension>();
                float chance = ext != null && ext.chance >= 0f ? ext.chance : s.traderAnimalChance;
                float points = -1f;
                foreach (var t in outThings)
                {
                    if (!(t is Pawn p) || !p.RaceProps.Animal) continue;
                    if (inGroup) BastleWorldScopes.stockInGroup.Add(p);
                    if (!convert || !Rand.Chance(chance) || !BastleWorld.Eligible(p)) continue;
                    // The map the trader visits; settlements and anything without a map read
                    // the whole world's.
                    if (points < 0f)
                        points = BastleWorld.ThreatPointsNow(parms.tile.HasValue ? Current.Game.FindMap(parms.tile.Value) : null)
                            * Mathf.Max(0f, ext?.pointFactor ?? 1f) * s.offeredStrength;
                    if (BastleWorld.MakeBastle(p, BastleWorld.RollTraitPoints(points))) BastleWorld.RevealShare(p);
                }
            }
            catch (Exception e) { Log.ErrorOnce("[Bastle Framework] Could not make bastles of trader stock: " + e, 0x42465754); }
        }
    }
}
