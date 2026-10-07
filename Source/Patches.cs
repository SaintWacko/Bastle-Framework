using HarmonyLib;
using RimWorld;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Adds discovered traits' prefixes/suffixes to a titan's kind label ("venomous wolf").
    /// Hot path: called whenever a label is drawn, so the body is one weak-table lookup
    /// for ordinary pawns and a cached string concat for titans.
    /// Named pawns (tamed animals with names, colonists) show their name in most places;
    /// the kind label still carries the affixes wherever the game shows the kind.
    /// </summary>
    [HarmonyPatch(typeof(GenLabel), nameof(GenLabel.BestKindLabel),
        new[] { typeof(Pawn), typeof(bool), typeof(bool), typeof(bool), typeof(int) })]
    public static class Patch_GenLabel_BestKindLabel
    {
        public static void Postfix(Pawn pawn, ref string __result)
        {
            var titan = TitanUtility.GetTitan(pawn);
            if (titan != null) __result = titan.DecorateKindLabel(__result);
        }
    }

    /// <summary>
    /// Inheritance. Vanilla animal births and hatchings both go through
    /// PawnUtility.TrySpawnHatchedOrBornPawn, but that method is only told the mother
    /// (or the egg), and the parent relations are added after it returns. So the two
    /// callers record both parents first, and the spawn patch reads them.
    /// Humanlike births (Biotech's PregnancyUtility.ApplyBirthOutcome, for both
    /// mothers and growth vats) record the genetic parents the same way. That method
    /// only spawns the baby through TrySpawnHatchedOrBornPawn when no doctor carries
    /// it straight to the mother, so its postfix covers the carried case.
    /// </summary>
    internal static class BirthContext
    {
        public static bool active;
        public static bool applied;
        public static Pawn mother;
        public static Pawn father;

        public static void Set(Pawn m, Pawn f) { active = true; applied = false; mother = m; father = f; }
        public static void Clear() { active = false; applied = false; mother = null; father = null; }
    }

    [HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome))]
    public static class Patch_PregnancyUtility_ApplyBirthOutcome
    {
        public static void Prefix(Pawn geneticMother, Pawn father) => BirthContext.Set(geneticMother, father);

        // The baby was carried by a doctor instead of spawned. Stillborn babies get nothing.
        public static void Postfix(Thing __result)
        {
            if (BirthContext.active && !BirthContext.applied && __result is Pawn baby && !baby.Dead)
                TitanUtility.InheritTraits(baby, BirthContext.mother, BirthContext.father);
        }

        public static void Finalizer() => BirthContext.Clear();
    }

    [HarmonyPatch(typeof(Hediff_Pregnant), nameof(Hediff_Pregnant.DoBirthSpawn))]
    public static class Patch_Hediff_Pregnant_DoBirthSpawn
    {
        public static void Prefix(Pawn mother, Pawn father) => BirthContext.Set(mother, father);

        public static void Finalizer() => BirthContext.Clear();
    }

    [HarmonyPatch(typeof(CompHatcher), nameof(CompHatcher.Hatch))]
    public static class Patch_CompHatcher_Hatch
    {
        // Mirrors Hatch: otherParent only counts as a parent when the genders differ.
        public static void Prefix(CompHatcher __instance)
        {
            var mother = __instance.hatcheeParent;
            var other = __instance.otherParent;
            if (other != null && mother != null && mother.gender == other.gender) other = null;
            BirthContext.Set(mother, other);
        }

        public static void Finalizer() => BirthContext.Clear();
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
    public static class Patch_PawnUtility_TrySpawnHatchedOrBornPawn
    {
        // Prefix, so the newborn already has its traits (and name) when it spawns.
        public static void Prefix(Pawn pawn)
        {
            if (!BirthContext.active || pawn == null) return;
            BirthContext.applied = true;
            TitanUtility.InheritTraits(pawn, BirthContext.mother, BirthContext.father);
        }
    }
}
