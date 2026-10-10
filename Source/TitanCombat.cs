using System.Linq;
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

    /// <summary>A chance to shrug off a hit completely. Shots can be sent back.</summary>
    public class TitanDeflect
    {
        /// <summary>Chance per hit, before rarity scaling (capped at 0.9).</summary>
        public float chance = 0.25f;

        /// <summary>Only shots (isRanged damage), not melee or explosions.</summary>
        public bool rangedOnly = true;

        /// <summary>Fire a deflected shot back at the shooter, as the weapon's own
        /// projectile. Shots with no known projectile are just deflected.</summary>
        public bool reflect = true;
    }

    public static class TitanCombatUtility
    {
        // Two thorned creatures hitting each other would otherwise bounce damage forever.
        // Also set while trait code deals extra damage (DealExtraDamage), so that damage
        // doesn't trigger more of the same.
        private static bool retaliating;

        /// <summary>True while retaliation or DealExtraDamage is being applied.</summary>
        public static bool InExtraDamage => retaliating;

        /// <summary>Damage a trait deals on top of a hit (bonus damage, burns). Doesn't
        /// trigger retaliation, lifesteal, or other extra damage. Don't call it from inside
        /// a damage notification: queue it with TitanScheduler.NextTick.</summary>
        public static void DealExtraDamage(Thing target, DamageInfo dinfo)
        {
            if (target == null || target.Destroyed || retaliating) return;
            retaliating = true;
            try { target.TakeDamage(dinfo); }
            finally { retaliating = false; }
        }

        /// <summary>Called from ThingWithComps.PreApplyDamage. True when a trait deflected the hit.</summary>
        internal static bool TryDeflect(Hediff_Titan titan, DamageInfo dinfo)
        {
            // IgnoreArmor: surgery, executions and the like, which shield belts let through too.
            if (dinfo.Def == null || !dinfo.Def.harmsHealth || dinfo.Def.consideredHelpful || dinfo.IgnoreArmor) return false;
            var traits = titan.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                var d = t.Deflect;
                if (d == null || (d.rangedOnly && !dinfo.Def.isRanged)) continue;
                float chance = UnityEngine.Mathf.Min(0.9f, d.chance * t.def.StrengthFor(t.Override?.deflect != null, t.Rarity));
                if (!Rand.Chance(chance)) continue;
                var pawn = titan.pawn;
                if (pawn.Spawned)
                {
                    FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
                    if (d.reflect && dinfo.Def.isRanged) Reflect(pawn, dinfo);
                }
                if (!t.discovered && TitanUtility.PlayerCanObserve(pawn)) t.Discover();
                return true;
            }
            return false;
        }

        private static void Reflect(Pawn pawn, DamageInfo dinfo)
        {
            var shooter = dinfo.Instigator;
            if (shooter == null || !shooter.Spawned || shooter.Map != pawn.Map) return;
            ThingDef proj = dinfo.Weapon?.Verbs?.FirstOrDefault(v => v.defaultProjectile != null)?.defaultProjectile;
            // Modded weapons (Combat Extended) may fire something that isn't a vanilla
            // Projectile; those hits are still deflected, just not sent back.
            if (proj == null || !typeof(Projectile).IsAssignableFrom(proj.thingClass)) return;
            // Spawning mid-impact is what vanilla's own explosions do; the launch itself
            // waits a tick so nothing hits during this damage call.
            TitanScheduler.NextTick(() =>
            {
                if (!pawn.Spawned || shooter.Destroyed || !shooter.Spawned || shooter.Map != pawn.Map) return;
                var p = (Projectile)GenSpawn.Spawn(proj, pawn.Position, pawn.Map);
                p.Launch(pawn, pawn.DrawPos, shooter, shooter, ProjectileHitFlags.IntendedTarget);
            });
        }

        public static bool IsMelee(DamageInfo dinfo, Thing victim, out Pawn attacker)
        {
            attacker = dinfo.Instigator as Pawn;
            if (attacker == null || attacker == victim) return false;
            if (dinfo.Def.isRanged || dinfo.Def.isExplosive) return false;
            // Held position: a killing blow has already turned the victim into a corpse.
            return attacker.Spawned && victim.MapHeld == attacker.Map
                && attacker.Position.AdjacentTo8WayOrInside(victim.PositionHeld);
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
                float amount = r.amount * trait.RetaliationStrength;
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
            // Thorns and burns dealt back don't feed it.
            if (l == null || retaliating || result == null || result.totalDamageDealt <= 0f) return 0f;
            if (l.fleshOnly && !(victim is Pawn p && p.RaceProps.IsFlesh)) return 0f;
            if (l.meleeOnly && !IsMelee(dinfo, victim, out _)) return 0f;
            return result.totalDamageDealt * l.fraction * trait.LifestealStrength;
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
                    if (hediffs[i] is Hediff_Injury inj && inj.Severity > 0f && !inj.IsPermanent() && (worst == null || inj.Severity > worst.Severity))
                        worst = inj;
                if (worst == null) return;
                float heal = Mathf.Min(amount, worst.Severity);
                worst.Heal(heal);
                amount -= heal;
            }
        }
    }
}
