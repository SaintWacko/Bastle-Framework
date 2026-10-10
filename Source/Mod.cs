using HarmonyLib;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    public class TitanFrameworkMod : Mod
    {
        public static TitanSettings Settings { get; private set; }

        public TitanFrameworkMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TitanSettings>();
            // Harmony is applied in TitanPatcher, after defs load: see there.
        }

        public override string SettingsCategory() => "Titan Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        /// <summary>Runs when the settings window closes. Cached material stages, lights
        /// and effecters on existing titans are rebuilt from the new values.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            TitanSettings.Version++;
            if (Current.Game == null) return;
            foreach (var p in RimWorld.PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                TitanUtility.GetTitan(p)?.SettingsChanged();
        }
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
    internal static class TitanPatcher
    {
        static TitanPatcher() => new Harmony("saintwacko.TitanFramework").PatchAll();
    }
}
