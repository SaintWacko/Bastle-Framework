using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Damage dealt back to an attacker (thorns, a burning body). Fires from the titan
    /// hediff's damage-taken hook. The amount scales with the trait's rarity strength.
    /// </summary>
    public class TitanRetaliation
    {
        public DamageDef damageDef;
        public float amount = 5f;
        public float armorPenetration = 0f;

        /// <summary>Chance per hit taken.</summary>
        public float chance = 1f;

        /// <summary>Chance to set the attacker on fire as well (independent of damageDef).</summary>
        public float igniteChance = 0f;

        /// <summary>Only melee hits from an adjacent attacker. Off: anything with an
        /// instigator, including shots (the shooter is hit wherever they stand).</summary>
        public bool meleeOnly = true;
    }

    /// <summary>Heals the creature by a fraction of the damage it deals.</summary>
    public class TitanLifesteal
    {
        /// <summary>Fraction of damage dealt that is healed, before rarity scaling.</summary>
        public float fraction = 0.25f;
        public bool meleeOnly = true;

        /// <summary>Only damage to living flesh heals (not walls, mechs or corpses).</summary>
        public bool fleshOnly = true;
    }

    /// <summary>What the creature gives when shorn or milked, instead of its race's own.
    /// A trait with products only goes on races that are shorn (or milked) at all.</summary>
    public class TitanProducts
    {
        public ThingDef woolDef;
        public float woolAmountFactor = 1f;
        public ThingDef milkDef;
        public float milkAmountFactor = 1f;

        public bool ChangesWool => woolDef != null || woolAmountFactor != 1f;
        public bool ChangesMilk => milkDef != null || milkAmountFactor != 1f;
    }

    public static class TitanCombatUtility
    {
        // Two thorned creatures hitting each other would otherwise bounce damage forever.
        private static bool retaliating;

        public static bool IsMelee(DamageInfo dinfo, Pawn victim, out Pawn attacker)
        {
            attacker = dinfo.Instigator as Pawn;
            if (attacker == null || attacker == victim) return false;
            if (dinfo.Def.isRanged || dinfo.Def.isExplosive) return false;
            return attacker.Spawned && victim.Spawned && attacker.Map == victim.Map
                && attacker.Position.AdjacentTo8WayOrInside(victim.Position);
        }

        internal static void TryRetaliate(TitanTrait trait, DamageInfo dinfo)
        {
            if (retaliating) return;
            var r = trait.Retaliation;
            if (r == null) return;
            var victim = trait.Pawn;
            Pawn attacker;
            if (r.meleeOnly)
            {
                if (!IsMelee(dinfo, victim, out attacker)) return;
            }
            else
            {
                attacker = dinfo.Instigator as Pawn;
                if (attacker == null || attacker == victim || !attacker.Spawned) return;
            }
            if (attacker.Dead || !Rand.Chance(r.chance)) return;

            retaliating = true;
            try
            {
                float amount = r.amount * trait.def.StrengthAt(trait.Rarity);
                if (r.damageDef != null && amount > 0f)
                {
                    var back = new DamageInfo(r.damageDef, amount, r.armorPenetration, -1f, victim,
                        null, null, DamageInfo.SourceCategory.ThingOrUnknown, attacker);
                    attacker.TakeDamage(back);
                }
                if (r.igniteChance > 0f && !attacker.Destroyed && Rand.Chance(r.igniteChance))
                    attacker.TryAttachFire(Rand.Range(0.2f, 0.4f), victim);
            }
            finally
            {
                retaliating = false;
            }
        }

        internal static float LifestealFrom(TitanTrait trait, Thing victim, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            var l = trait.Lifesteal;
            if (l == null || result == null || result.totalDamageDealt <= 0f) return 0f;
            if (l.fleshOnly && !(victim is Pawn p && p.RaceProps.IsFlesh)) return 0f;
            if (l.meleeOnly && !(victim is Pawn vp && IsMelee(dinfo, vp, out _))) return 0f;
            return result.totalDamageDealt * l.fraction * trait.def.StrengthAt(trait.Rarity);
        }

        /// <summary>Heals injuries, worst first, by up to this many hit points.</summary>
        public static void HealInjuries(Pawn pawn, float amount)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead) return;
            var hediffs = pawn.health.hediffSet.hediffs;
            int guard = 20;
            while (amount > 0.01f && guard-- > 0)
            {
                Hediff_Injury worst = null;
                for (int i = 0; i < hediffs.Count; i++)
                    if (hediffs[i] is Hediff_Injury inj && !inj.IsPermanent() && (worst == null || inj.Severity > worst.Severity))
                        worst = inj;
                if (worst == null) return;
                float heal = Mathf.Min(amount, worst.Severity);
                worst.Heal(heal);
                amount -= heal;
            }
        }
    }
}
