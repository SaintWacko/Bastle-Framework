using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>The sources of bastles that turn up without a content mod making them.
    /// World is the master switch: every other feature also needs it on.</summary>
    public enum BastleWorldFeature
    {
        World,
        WildAnimals,
        AnimalEvents,
        GroupAnimals,
        TraderAnimals,
        RewardAnimals,
        Sighting,
        Pack,
        Rampage,
        HuntQuest,
    }

    /// <summary>
    /// Whether each world source is on in the current game, and how strong the bastles it
    /// makes are. The mod settings are the defaults; a mod can override any feature for
    /// one game (Dungeon Madness does it from its new-colony window), and the override is
    /// saved with that game. Every world hook asks here, never the settings directly.
    /// </summary>
    public static class BastleWorld
    {
        public static bool Enabled(BastleWorldFeature feature)
            => Raw(BastleWorldFeature.World) && (feature == BastleWorldFeature.World || Raw(feature));

        private static bool Raw(BastleWorldFeature feature)
        {
            var game = BastleGameComponent.Instance;
            if (game != null && game.worldOverrides.TryGetValue(feature, out bool v)) return v;
            return FromSettings(feature);
        }

        /// <summary>The settings' value, ignoring any override in the current game.</summary>
        public static bool FromSettings(BastleWorldFeature feature)
        {
            var s = BastleFrameworkMod.Settings;
            switch (feature)
            {
                case BastleWorldFeature.World: return s.worldEnabled;
                case BastleWorldFeature.WildAnimals: return s.wildEnabled;
                case BastleWorldFeature.AnimalEvents: return s.animalEventsEnabled;
                case BastleWorldFeature.GroupAnimals: return s.groupAnimalsEnabled;
                case BastleWorldFeature.TraderAnimals: return s.traderAnimalsEnabled;
                case BastleWorldFeature.RewardAnimals: return s.rewardAnimalsEnabled;
                case BastleWorldFeature.Sighting: return s.sightingEnabled;
                case BastleWorldFeature.Pack: return s.packEnabled;
                case BastleWorldFeature.Rampage: return s.rampageEnabled;
                case BastleWorldFeature.HuntQuest: return s.huntQuestEnabled;
                default: return false;
            }
        }

        /// <summary>The current game's override, or null when it follows the settings.</summary>
        public static bool? GetForGame(BastleWorldFeature feature)
        {
            var game = BastleGameComponent.Instance;
            return game != null && game.worldOverrides.TryGetValue(feature, out bool v) ? v : (bool?)null;
        }

        /// <summary>Overrides a feature for the current game only (null goes back to the
        /// settings). Call it once the game exists: from a new-colony window, after
        /// Current.Game is set, or at any point in play.</summary>
        public static void SetForGame(BastleWorldFeature feature, bool? enabled)
        {
            var game = BastleGameComponent.Instance;
            if (game == null)
            {
                Log.Warning("[Bastle Framework] BastleWorld.SetForGame was called with no game running; ignored.");
                return;
            }
            if (enabled.HasValue) game.worldOverrides[feature] = enabled.Value;
            else game.worldOverrides.Remove(feature);
        }

        public static readonly float[] CurveThreatPoints = { 100f, 500f, 1500f, 3000f, 6000f };

        private static SimpleCurve curve;
        private static int curveVersion = -1;

        /// <summary>Trait points for a bastle with this many threat points behind it, from
        /// the settings' curve (flat past either end), before variance.</summary>
        public static float TraitPointsFor(float threatPoints)
        {
            if (curve == null || curveVersion != BastleSettings.Version)
            {
                var c = new SimpleCurve();
                var pts = BastleFrameworkMod.Settings.worldCurvePoints;
                for (int i = 0; i < CurveThreatPoints.Length; i++) c.Add(CurveThreatPoints[i], pts[i]);
                curve = c;
                curveVersion = BastleSettings.Version;
            }
            return curve.Evaluate(threatPoints);
        }

        /// <summary>TraitPointsFor with the settings' variance rolled in.</summary>
        public static float RollTraitPoints(float threatPoints)
        {
            float v = BastleFrameworkMod.Settings.worldPointVariance;
            return Mathf.Max(0.5f, TraitPointsFor(threatPoints) * Rand.Range(1f - v, 1f + v));
        }

        /// <summary>The raid points a storyteller would use here now; the whole world's
        /// (every colony and caravan together) when there is no map.</summary>
        public static float ThreatPointsNow(Map map)
        {
            try { return StorytellerUtility.DefaultThreatPointsNow(map ?? (IIncidentTarget)Find.World); }
            catch (Exception e)
            {
                Log.ErrorOnce("[Bastle Framework] Could not read threat points; world bastles use the minimum: " + e, 0x42465750);
                return 35f;
            }
        }

        public static bool Eligible(Pawn pawn)
            => pawn?.RaceProps != null && pawn.RaceProps.Animal && !pawn.Dead && BastleUtility.PeekBastle(pawn) == null;

        /// <summary>Rolls hidden traits onto an animal for this many trait points. Never
        /// throws: world hooks run inside map, raid and stock generation, where an exception
        /// would lose far more than the bastle.</summary>
        public static bool MakeBastle(Pawn pawn, float traitPoints)
        {
            try { return !BastleUtility.RollTraits(pawn, traitPoints).NullOrEmpty(); }
            catch (Exception e)
            {
                Log.ErrorOnce("[Bastle Framework] Could not make a world bastle of " + pawn + ": " + e, 0x42465751);
                return false;
            }
        }
    }

    /// <summary>Wild animals, on a new map and as they wander in later. Every caller goes
    /// through SpawnRandomWildAnimalAt, which generates each herd member with
    /// PawnGenerator.GeneratePawn while the map is known here.</summary>
    [HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.SpawnRandomWildAnimalAt))]
    internal static class Patch_WildAnimalSpawner_SpawnRandomWildAnimalAt
    {
        internal static Map spawningOn;

        static void Prefix(Map ___map, out Map __state)
        {
            __state = spawningOn;
            spawningOn = BastleWorld.Enabled(BastleWorldFeature.WildAnimals) ? ___map : null;
        }

        static void Finalizer(Map __state) => spawningOn = __state;
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class Patch_PawnGenerator_GeneratePawn_World
    {
        static void Postfix(Pawn __result)
        {
            var map = Patch_WildAnimalSpawner_SpawnRandomWildAnimalAt.spawningOn;
            if (map == null || !Rand.Chance(BastleFrameworkMod.Settings.wildChance) || !BastleWorld.Eligible(__result)) return;
            var s = BastleFrameworkMod.Settings;
            BastleWorld.MakeBastle(__result, BastleWorld.RollTraitPoints(BastleWorld.ThreatPointsNow(map) * s.wildStrength));
        }
    }
}
