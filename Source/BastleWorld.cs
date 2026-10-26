using System;
using System.Collections.Generic;
using System.Linq;
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
        public static float ThreatPointsNow(Map map) => ThreatPointsNow((IIncidentTarget)map);

        /// <summary>As ThreatPointsNow(map), for a map, a caravan or the world.</summary>
        public static float ThreatPointsNow(IIncidentTarget target)
        {
            try { return StorytellerUtility.DefaultThreatPointsNow(target ?? Find.World); }
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

        /// <summary>As MakeBastle, but the copy of a pack leader: same traits, one discovery
        /// group.</summary>
        public static bool CopyPackLeader(Pawn leader, Pawn pawn)
        {
            try { return BastleUtility.CopyBastle(leader, pawn, linkDiscovery: true) != null; }
            catch (Exception e)
            {
                Log.ErrorOnce("[Bastle Framework] Could not copy a world bastle onto " + pawn + ": " + e, 0x42465752);
                return false;
            }
        }

        /// <summary>Whoever offers a bastle (a trader, a quest giver) knows part of it: a
        /// share from none to all of its hidden traits, picked at random, is discovered
        /// without a message.</summary>
        public static void RevealShare(Pawn pawn)
        {
            var bastle = BastleUtility.PeekBastle(pawn);
            if (bastle == null) return;
            var hidden = bastle.TraitsForReading.Where(t => !t.discovered).ToList();
            int known = Rand.RangeInclusive(0, hidden.Count);
            foreach (var t in hidden.InRandomOrder())
            {
                if (known-- <= 0) break;
                t.Discover(notify: false);
            }
        }
    }
}
