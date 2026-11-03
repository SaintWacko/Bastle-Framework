using HarmonyLib;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    public class BastleFrameworkMod : Mod
    {
        public static BastleSettings Settings { get; private set; }

        public BastleFrameworkMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BastleSettings>();
            treesBuiltWithHints = Settings.visualsUnidentified;
            // Harmony is applied in BastlePatcher, after defs load: see there.
        }

        public override string SettingsCategory() => "Bastle Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        /// <summary>Runs when the settings window closes. Cached material stages, lights
        /// and effecters on existing bastles are rebuilt from the new values.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            BastleHuntQuest.ApplyFrequency();
            BastleOutbreakQuest.ApplyFrequency();
            // Which looks of unknown traits are in the render tree depends on this one, and
            // rebuilding every bastle's tree is only worth it when it changed.
            bool looksChanged = Settings.visualsUnidentified != treesBuiltWithHints;
            treesBuiltWithHints = Settings.visualsUnidentified;
            if (Current.Game == null) return;
            foreach (var p in RimWorld.PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                BastleUtility.GetBastle(p)?.SettingsChanged(looksChanged);
        }

        private static bool treesBuiltWithHints = true;
    }

    /// <summary>
    /// Patching a method makes Harmony run its type's static constructor. Some vanilla
    /// types fill static tables from DefOf fields there (PregnancyUtility builds a
    /// dictionary keyed by PawnRelationDefOf.Spouse etc.). Patched from the Mod
    /// constructor, that runs before defs load, the keys are null, the type initializer
    /// throws, and the type is broken for the whole session: every pawn generation
    /// then fails (test run #48). StaticConstructorOnStartup runs after defs and DefOfs
    /// are ready, and before any game is loaded, which is early enough for every patch
    /// here.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BastlePatcher
    {
        static BastlePatcher() => new Harmony("saintwacko.BastleFramework").PatchAll();
    }
}
