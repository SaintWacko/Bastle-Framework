using HarmonyLib;
using Verse;

namespace TitanFramework
{
    public class TitanFrameworkMod : Mod
    {
        public TitanFrameworkMod(ModContentPack content) : base(content)
        {
            new Harmony("saintwacko.TitanFramework").PatchAll();
        }
    }
}
