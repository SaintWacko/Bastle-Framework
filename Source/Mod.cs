using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
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
            // Harmony is applied in BastlePatcher, after defs load: see there.
        }

        public override string SettingsCategory() => "Bastle Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        /// <summary>Runs when the settings window closes. Cached material stages, lights
        /// and effecters on existing bastles are rebuilt from the new values.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Current.Game == null) return;
            foreach (var p in RimWorld.PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                BastleUtility.GetBastle(p)?.SettingsChanged();
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
    internal static class BastlePatcher
    {
        static BastlePatcher()
        {
            new Harmony("saintwacko.BastleFramework").PatchAll();
            ShowTraitAbilitiesUndrafted();
        }

        // Pawn_AbilityTracker.GetGizmos only shows an ability on an undrafted pawn when its
        // def says displayGizmoWhileUndrafted, and animals can never be drafted, so a
        // trained animal bastle's buttons stayed hidden (test run #3). An ability a trait
        // grants gets the flag when it comes from the same mod as the trait; another mod's
        // ability (vanilla's, say) is shared with pawns that aren't bastles, so it's left
        // as its author wrote it. Colonists see the flagged buttons undrafted too, the way
        // they see most psycasts.
        private static void ShowTraitAbilitiesUndrafted()
        {
            foreach (var trait in DefDatabase<BastleTraitDef>.AllDefsListForReading)
            {
                Show(trait, trait.abilities);
                if (trait.rarityOverrides != null)
                    foreach (var o in trait.rarityOverrides) Show(trait, o.abilities);
            }
        }

        private static void Show(BastleTraitDef trait, List<AbilityDef> abilities)
        {
            if (abilities == null) return;
            foreach (var ability in abilities)
                if (ability != null && ability.modContentPack == trait.modContentPack)
                    ability.displayGizmoWhileUndrafted = true;
        }
    }
}
