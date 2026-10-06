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
            new Harmony("saintwacko.TitanFramework").PatchAll();
        }

        public override string SettingsCategory() => "Titan Framework";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        /// <summary>Runs when the settings window closes. Cached material stages, lights
        /// and effecters on existing titans are rebuilt from the new values.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Current.Game == null) return;
            foreach (var p in RimWorld.PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                TitanUtility.GetTitan(p)?.SettingsChanged();
        }
    }
}
