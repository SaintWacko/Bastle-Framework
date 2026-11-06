using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Damage dealt back to an attacker (thorns, a burning body). Fires from the bastle
    /// hediff's damage-taken hook. The amount scales with the trait's rarity strength.
    /// </summary>
    public class BastleRetaliation
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
    public class BastleLifesteal
    {
        /// <summary>Fraction of damage dealt that is healed, before rarity scaling.</summary>
        public float fraction = 0.25f;
        public bool meleeOnly = true;

        /// <summary>Only damage to living flesh heals (not walls, mechs or corpses).</summary>
        public bool fleshOnly = true;
    }

    /// <summary>What the creature gives when shorn or milked, instead of its race's own.
    /// A trait with products only goes on races that are shorn (or milked) at all.</summary>
    public class BastleProducts
    {
        public ThingDef woolDef;
        public float woolAmountFactor = 1f;
        public ThingDef milkDef;
        public float milkAmountFactor = 1f;

        public bool ChangesWool => woolDef != null || woolAmountFactor != 1f;
        public bool ChangesMilk => milkDef != null || milkAmountFactor != 1f;
    }

    /// <summary>A chance to shrug off a hit completely. Shots can be sent back.</summary>
    public class BastleDeflect
    {
        /// <summary>Chance per hit, before rarity scaling (capped at 0.9).</summary>
        public float chance = 0.25f;

        /// <summary>Only shots (isRanged damage), not melee or explosions.</summary>
        public bool rangedOnly = true;

        /// <summary>Chance a deflected shot flies back at the shooter, as the weapon's own
        /// projectile. Not scaled by rarity. The rest glance off in a random direction
        /// and can hit whatever is in their path. Shots with no known projectile just stop.</summary>
        public float reflectChance = 0.4f;
    }

    public static class BastleCombatUtility
    {
        // Two thorned creatures hitting each other would otherwise bounce damage forever.
        // Also set while trait code deals extra damage (DealExtraDamage), so that damage
        // doesn't trigger more of the same.
        private static bool retaliating;

        /// <summary>True while retaliation or DealExtraDamage is being applied.</summary>
        public static bool InExtraDamage => retaliating;

        /// <summary>Damage a trait deals on top of a hit (bonus damage, burns). Doesn't
        /// trigger retaliation, lifesteal, or other extra damage. Don't call it from inside
        /// a damage notification: queue it with BastleScheduler.NextTick.</summary>
        public static void DealExtraDamage(Thing target, DamageInfo dinfo)
        {
            if (target == null || target.Destroyed || retaliating) return;
            retaliating = true;
            try { target.TakeDamage(dinfo); }
            finally { retaliating = false; }
        }

        /// <summary>Called from ThingWithComps.PreApplyDamage. True when a trait deflected the hit.</summary>
        internal static bool TryDeflect(Hediff_Bastle bastle, DamageInfo dinfo)
        {
            // IgnoreArmor: surgery, executions and the like, which shield belts let through too.
            if (dinfo.Def == null || !dinfo.Def.harmsHealth || dinfo.Def.consideredHelpful || dinfo.IgnoreArmor) return false;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                var d = t.Deflect;
                if (d == null || (d.rangedOnly && !dinfo.Def.isRanged)) continue;
                float chance = UnityEngine.Mathf.Min(0.9f, d.chance * t.def.StrengthFor(t.Override?.deflect != null, t.Rarity));
                if (!Rand.Chance(chance)) continue;
                var pawn = bastle.pawn;
                if (pawn.Spawned)
                {
                    FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
                    if (dinfo.Def.isRanged) Redirect(pawn, dinfo, Rand.Chance(d.reflectChance));
                }
                // Discovered first, so a deflection the player sees plays its effects even
                // with looks of unknown traits turned off.
                if (!t.discovered && BastleUtility.PlayerCanObserve(pawn)) t.Discover();
                if (pawn.Spawned) BastleEventVisuals.Play(bastle, BastleVisualEvent.Deflect, dinfo.Instigator, dinfo, t);
                return true;
            }
            return false;
        }

        /// <summary>Sends a deflected shot back at the shooter, or off at a random angle.</summary>
        private static void Redirect(Pawn pawn, DamageInfo dinfo, bool reflect)
        {
            var shooter = dinfo.Instigator;
            // No shooter on this map to aim at: it glances off instead.
            if (shooter == null || !shooter.Spawned || shooter.Map != pawn.Map) reflect = false;
            ThingDef proj = dinfo.Weapon?.Verbs?.FirstOrDefault(v => v.defaultProjectile != null)?.defaultProjectile;
            // Modded weapons (Combat Extended) may fire something that isn't a vanilla
            // Projectile; those hits are still deflected, just not sent anywhere.
            if (proj == null || !typeof(Projectile).IsAssignableFrom(proj.thingClass)) return;
            // Spawning mid-impact is what vanilla's own explosions do; the launch itself
            // waits a tick so nothing hits during this damage call.
            BastleScheduler.NextTick(() =>
            {
                if (!pawn.Spawned) return;
                if (reflect && !shooter.Destroyed && shooter.Spawned && shooter.Map == pawn.Map)
                    LaunchReflected(pawn, shooter, proj);
                else
                    LaunchGlancing(pawn, proj, shooter);
            });
        }

        /// <summary>Fires the shot off at a random angle, 4 to 12 tiles. It can hit any
        /// pawn or object along the way, the bastle's allies, the player's colonists and the
        /// original shooter included.
        /// The shooter stays the intended target (as with vanilla's wild misses), so a stray
        /// hit on a bystander hostile to that shooter counts as crossfire and costs no
        /// goodwill. The IntendedTarget flag only lets Projectile.CanHit take the shooter
        /// when the shot crosses its cell; the shot flies at the cell, so it never homes in.</summary>
        private static void LaunchGlancing(Pawn pawn, ThingDef proj, Thing shooter)
        {
            var map = pawn.Map;
            Vector3 dir = Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward;
            IntVec3 cell = (pawn.DrawPos + dir * Rand.Range(4f, 12f)).ToIntVec3().ClampInsideMap(map);
            if (cell == pawn.Position) return;
            var p = (Projectile)GenSpawn.Spawn(proj, pawn.Position, map);
            LocalTargetInfo intended = shooter != null && shooter.Spawned ? new LocalTargetInfo(shooter) : new LocalTargetInfo(cell);
            p.Launch(pawn, pawn.DrawPos, cell, intended,
                ProjectileHitFlags.All);
        }

        // Accuracy of a reflected shot, as a ShootingAccuracyPawn-style per-tile value.
        // 0.95 is roughly a middling colonist: about 60% on target at 10 tiles.
        private const float ReflectAccuracy = 0.95f;

        /// <summary>Fires the shot back as if the bastle had aimed it, following vanilla's
        /// Verb_LaunchProjectile: miss wild, else hit the shooter's cover, else hit.</summary>
        private static void LaunchReflected(Pawn pawn, Thing shooter, ThingDef proj)
        {
            var map = pawn.Map;
            var p = (Projectile)GenSpawn.Spawn(proj, pawn.Position, map);
            float dist = (shooter.Position - pawn.Position).LengthHorizontal;
            float size = shooter is Pawn sp ? Mathf.Clamp(sp.BodySize, 0.5f, 2f) : 1f;
            float aim = ShotReport.HitFactorFromShooter(null, dist, ReflectAccuracy) * size;

            if (!Rand.Chance(aim))
            {
                // Wild miss: lands near the shooter and can hit whatever is there.
                IntVec3 cell = shooter.Position + GenRadial.RadialPattern[Rand.Range(1, GenRadial.NumCellsInRadius(2.9f))];
                if (!cell.InBounds(map)) cell = shooter.Position;
                var flags = ProjectileHitFlags.NonTargetWorld;
                if (Rand.Chance(0.5f)) flags |= ProjectileHitFlags.NonTargetPawns;
                p.Launch(pawn, pawn.DrawPos, cell, shooter, flags);
                return;
            }
            if (Rand.Chance(CoverUtility.CalculateOverallBlockChance(shooter, pawn.Position, map))
                && CoverUtility.CalculateCoverGiverSet(shooter, pawn.Position, map)
                    .TryRandomElementByWeight(c => c.BlockChance, out var cover))
            {
                p.Launch(pawn, pawn.DrawPos, cover.Thing, shooter, ProjectileHitFlags.NonTargetWorld);
                return;
            }
            p.Launch(pawn, pawn.DrawPos, shooter, shooter, ProjectileHitFlags.IntendedTarget);
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

        internal static void TryRetaliate(BastleTrait trait, DamageInfo dinfo)
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

            // This runs inside the victim's damage notification, with the attacker still in
            // its attack verb: a lethal thorn would kill it mid-cast. Dealt next tick instead,
            // with the guard up then so the attacker's own thorns don't answer.
            float amount = r.amount * trait.RetaliationStrength;
            BastleScheduler.NextTick(() =>
            {
                if (attacker.Dead || !attacker.Spawned) return;
                retaliating = true;
                try
                {
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
            });
        }

        internal static float LifestealFrom(BastleTrait trait, Thing victim, DamageInfo dinfo, DamageWorker.DamageResult result)
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
