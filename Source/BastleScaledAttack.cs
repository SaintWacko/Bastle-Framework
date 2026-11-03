using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Put on a trait's hediff that gives attacks through HediffComp_VerbGiver. Its attacks
    /// then hit as hard as the creature's own strongest natural attack (capped by the
    /// scaledAttackCap setting) times factor, times the trait's rarity strength, instead of
    /// the tool's fixed power. A fixed power can't
    /// suit every body: venom fangs written for a wolf made a crow bite harder than a cobra.
    /// The hediff's strongest tool is the one matched to the natural attack; its other tools
    /// keep their power relative to it. extraMeleeDamages scale by the same ratio, since
    /// vanilla runs them through the same damage factor. A tool that sets armorPenetration
    /// keeps that value; leave it unset to have it follow the damage.
    /// </summary>
    public class BastleScaledAttack : DefModExtension
    {
        public float factor = 1f;
    }

    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.GetDamageFactorFor),
        new[] { typeof(Tool), typeof(Pawn), typeof(HediffComp_VerbGiver) })]
    internal static class Patch_GetDamageFactorFor_ScaledAttack
    {
        private static readonly Dictionary<ThingDef, float> bestNatural = new Dictionary<ThingDef, float>();
        private static readonly Dictionary<HediffDef, float> bestGiven = new Dictionary<HediffDef, float>();

        static void Postfix(Tool tool, HediffComp_VerbGiver hediffCompSource, ref float __result)
        {
            if (hediffCompSource == null || tool == null || tool.power <= 0f) return;
            var hediff = hediffCompSource.parent.def;
            var ext = hediff.GetModExtension<BastleScaledAttack>();
            if (ext == null) return;
            var pawn = hediffCompSource.Pawn;
            float natural = BestNaturalPower(pawn.def);
            // A race with no attacks of its own has nothing to scale from: the tool's power stands.
            if (natural <= 0f) return;
            // Capped before rarity so a rarer trait still hits harder on a huge creature.
            float cap = BastleFrameworkMod.Settings.scaledAttackCap;
            if (cap < BastleSettings.ScaledAttackNoCap && natural > cap) natural = cap;
            __result *= ext.factor * natural * TraitStrength(pawn, hediff) / BestGivenPower(hediff, tool);
        }

        private static float BestGivenPower(HediffDef hediff, Tool fallback)
        {
            if (bestGiven.TryGetValue(hediff, out float best)) return best;
            best = fallback.power;
            var giver = hediff.CompProps<HediffCompProperties_VerbGiver>();
            if (giver?.tools != null)
                foreach (var t in giver.tools)
                    if (t.power > best) best = t.power;
            bestGiven[hediff] = best;
            return best;
        }

        private static float BestNaturalPower(ThingDef race)
        {
            if (bestNatural.TryGetValue(race, out float best)) return best;
            best = 0f;
            if (race.tools != null)
                foreach (var t in race.tools)
                    if (t.power > best) best = t.power;
            bestNatural[race] = best;
            return best;
        }

        private static float TraitStrength(Pawn pawn, HediffDef hediff)
        {
            var bastle = BastleUtility.PeekBastle(pawn);
            if (bastle == null) return 1f;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var list = traits[i].Hediffs;
                if (list == null) continue;
                for (int j = 0; j < list.Count; j++)
                    if (list[j].hediff == hediff) return traits[i].HediffStrength;
            }
            return 1f;
        }
    }
}
