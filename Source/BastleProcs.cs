using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BastleFramework
{
    /// <summary>What sets a proc off. Everything is about the creature with the trait.</summary>
    public enum BastleProcEvent
    {
        /// <summary>Its damage got through to a pawn. Each damage event is one hit.</summary>
        Hit,
        /// <summary>It took damage that got through (armour stopping all of it doesn't count).</summary>
        TakeDamage,
        /// <summary>It killed a pawn.</summary>
        Kill,
        /// <summary>It was downed.</summary>
        Downed,
        /// <summary>It finished eating something.</summary>
        Eat,
    }

    /// <summary>Which hits count. Melee: dealt from an adjacent cell by something that isn't a
    /// shot or a blast. Everything else is Ranged.</summary>
    public enum BastleAttackKind { Any, Melee, Ranged }

    /// <summary>What a scaling trait's rarity does to a proc's chance and cooldown (its
    /// amounts always scale). Chance: for offensive procs, kept in check by how often they go
    /// off. Cooldown: for defensive ones and lifesavers, kept in check by the wait.</summary>
    public enum BastleProcScaling { None, Chance, Cooldown }

    /// <summary>Who an area proc reaches, as seen from the creature.</summary>
    [Flags]
    public enum BastleProcTargets
    {
        Self = 1,
        /// <summary>Its own faction and factions allied to it; for a wild one, wild creatures of its kind.</summary>
        Allies = 2,
        Enemies = 4,
    }

    /// <summary>Where an area proc is centred: on the creature, or on the other pawn (the one
    /// hit, the attacker, or where the pawn it killed fell).</summary>
    public enum BastleProcCenter { Self, Other }

    public enum BastleProcCastTarget { Self, Other, OtherCell }

    /// <summary>An ability cast by a proc, at once, with no job, warmup, range or cost, through
    /// a throwaway Ability that is never added to the creature (so its own abilities and
    /// cooldowns are untouched). What the ability itself refuses (a target it can't take, an
    /// effect comp's own check) still counts.</summary>
    public class BastleProcCast
    {
        public AbilityDef ability;
        public BastleProcCastTarget target = BastleProcCastTarget.Other;
    }

    /// <summary>
    /// "When X happens, do Y" on a trait: a stun on a hit, a frost blast when struck, a heal
    /// once a day when downed. Listed in BastleTraitDef.procs (or a rarity override's). Amounts
    /// scale with the trait's rarity strength and the Procs strength setting; chance or
    /// cooldown only as rarityScales says.
    ///
    /// Gating happens inside the event, the effects one tick later (BastleScheduler): events
    /// arrive inside vanilla loops over a pawn's hediffs, where adding hediffs, moving pawns or
    /// dealing damage is unsafe. Damage a proc deals never sets off another proc, thorns or
    /// lifesteal (BastleCombatUtility.DealExtraDamage).
    /// </summary>
    public class BastleProc
    {
        public BastleProcEvent on;

        // ---- when

        /// <summary>Hit and TakeDamage only.</summary>
        public BastleAttackKind attack = BastleAttackKind.Any;

        /// <summary>Hit and Kill: the other pawn is hostile to the creature. TakeDamage: dealt by
        /// something hostile to it. Off: anyone, including a hunted animal or a tamer.</summary>
        public bool hostileOnly = true;

        /// <summary>TakeDamage: only while its health is under this fraction after the hit. 0: any.</summary>
        public float healthBelow;

        /// <summary>Hit, TakeDamage, Kill: the other pawn is at least this many tiles away.</summary>
        public float minDistance;

        public float chance = 1f;

        /// <summary>Game seconds (60 ticks) or hours before it can go off again. Set one.</summary>
        public FloatRange cooldownSeconds = FloatRange.Zero;
        public FloatRange cooldownHours = FloatRange.Zero;

        public BastleProcScaling rarityScales;

        /// <summary>With toOther: after it lands on a pawn, each repeat on that pawn within this
        /// many seconds of the last one is adaptFactor as strong as the one before, until it
        /// would fall under a quarter. adaptFactor 0: the pawn is immune for that long. Keeps a
        /// stun on every bite from holding an attacker in place for good.</summary>
        public float adaptSeconds;
        public float adaptFactor;
        public const float MinAdaptStrength = 0.25f;

        // ---- whom

        /// <summary>The effects go to the other pawn (the one hit, the attacker) instead of the
        /// creature. Without a radius only.</summary>
        public bool toOther;

        /// <summary>With a radius, every effect goes to each pawn that targets picks within that
        /// many tiles of the centre, instead of one pawn.</summary>
        public float radius;
        public BastleProcTargets targets = BastleProcTargets.Enemies;
        public BastleProcCenter center = BastleProcCenter.Self;
        public bool blockedByWalls = true;

        // ---- effects (amounts are at the trait's own rarity)

        public HediffDef hediff;
        /// <summary>Severity to set (at least), scaled. Below 0: the hediff's own.</summary>
        public float severity = -1f;
        /// <summary>How long the hediff lasts, scaled; needs HediffComp_Disappears. 0: its own duration.</summary>
        public float hediffSeconds;
        /// <summary>Hit points healed, worst injuries first.</summary>
        public float heal;
        /// <summary>Fraction of the food need filled.</summary>
        public float food;
        public float stunSeconds;
        public DamageDef damageDef;
        public float damage;
        public float armorPenetration = -1f;
        public float igniteChance;
        /// <summary>Dealt on top, to mechanoids only (EMP).</summary>
        public DamageDef mechanoidDamageDef;
        public float mechanoidDamage;
        /// <summary>Started on each pawn reached, with mentalStateChance (scaled like the
        /// amounts). Animals get animalMentalState instead when it is set. Mechanoids and
        /// entities never do.</summary>
        public MentalStateDef mentalState;
        public MentalStateDef animalMentalState;
        public float mentalStateChance = 1f;
        /// <summary>The mental state chance is multiplied by the pawn's psychic sensitivity.</summary>
        public bool psychic;
        /// <summary>Thrown this many tiles straight away from the centre, then stunned 1 to 3
        /// seconds where it lands (vanilla's stunning flyer). Not the downed, anyone carrying
        /// someone, or anything bigger than pushMaxBodySize.</summary>
        public float pushDistance;
        public float pushMaxBodySize = 2f;
        /// <summary>Skipped to a random spot this many tiles away (chaos skip). Not anything
        /// bigger than skipMaxBodySize, as vanilla's skip.</summary>
        public FloatRange skipDistance = FloatRange.Zero;
        public float skipMaxBodySize = 3.5f;
        /// <summary>A cloud of blinding smoke this many tiles across, at the centre.</summary>
        public float smokeRadius;
        public List<BastleProcCast> casts;

        // ---- look

        public FleckDef fleck;
        /// <summary>Below 0: sized from the radius (or 1 without one).</summary>
        public float fleckScale = -1f;
        public SoundDef sound;
        /// <summary>Floating text over each pawn reached ("staggered").</summary>
        [MustTranslate] public string text;
        /// <summary>Played when it goes off. on is ignored (they're Proc visuals).</summary>
        public List<BastleEventVisual> visuals;

        /// <summary>Going off where the player can see it reveals the trait.</summary>
        public bool discovers = true;

        public bool IsArea => radius > 0f;
        public bool HasCooldown => cooldownSeconds.max > 0f || cooldownHours.max > 0f;
        public bool HasOther => on == BastleProcEvent.Hit || on == BastleProcEvent.TakeDamage || on == BastleProcEvent.Kill;

        internal bool HasPawnEffect => hediff != null || heal > 0f || food > 0f || stunSeconds > 0f
            || (damageDef != null && damage > 0f) || (mechanoidDamageDef != null && mechanoidDamage > 0f)
            || igniteChance > 0f || mentalState != null || pushDistance > 0f || skipDistance.max > 0f;

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (!HasPawnEffect && casts.NullOrEmpty() && smokeRadius <= 0f) yield return $"{owner}: a proc on {on} has no effect";
            if (chance <= 0f || chance > 1f) yield return $"{owner}: proc chance must be above 0 and at most 1";
            if (cooldownSeconds.max > 0f && cooldownHours.max > 0f) yield return $"{owner}: set cooldownSeconds or cooldownHours, not both";
            if (rarityScales == BastleProcScaling.Chance && chance >= 1f) yield return $"{owner}: rarityScales Chance needs a chance below 1";
            if (rarityScales == BastleProcScaling.Cooldown && !HasCooldown) yield return $"{owner}: rarityScales Cooldown needs a cooldown";
            if (attack != BastleAttackKind.Any && on != BastleProcEvent.Hit && on != BastleProcEvent.TakeDamage)
                yield return $"{owner}: attack only applies to Hit and TakeDamage procs";
            if (healthBelow != 0f && on != BastleProcEvent.TakeDamage) yield return $"{owner}: healthBelow only applies to TakeDamage procs";
            if ((toOther || center == BastleProcCenter.Other || minDistance > 0f) && !HasOther)
                yield return $"{owner}: toOther, center Other and minDistance need a Hit, TakeDamage or Kill proc";
            if (toOther && IsArea) yield return $"{owner}: toOther can't go with a radius";
            // A Kill proc's effects run a tick later, when the victim is a corpse: only its cell is left.
            if (on == BastleProcEvent.Kill && toOther) yield return $"{owner}: a Kill proc can't use toOther (the victim is dead by then); use a radius with center Other";
            if (adaptSeconds > 0f && !toOther) yield return $"{owner}: adaptSeconds needs toOther";
            if (adaptFactor < 0f || adaptFactor >= 1f) yield return $"{owner}: adaptFactor must be at least 0 and below 1";
            if ((damageDef != null) != (damage > 0f)) yield return $"{owner}: damageDef and damage go together";
            if ((mechanoidDamageDef != null) != (mechanoidDamage > 0f)) yield return $"{owner}: mechanoidDamageDef and mechanoidDamage go together";
            if (!toOther && !IsArea && (damageDef != null || mechanoidDamageDef != null || igniteChance > 0f || pushDistance > 0f || skipDistance.max > 0f || mentalState != null))
                yield return $"{owner}: damage, fire, mental states, pushes and skips on the creature itself would be a drawback written as a proc; use toOther or a radius";
            if (hediffSeconds > 0f && (hediff == null || hediff.CompProps<HediffCompProperties_Disappears>() == null))
                yield return $"{owner}: hediffSeconds needs a hediff with HediffComp_Disappears";
            if (IsArea && targets == 0) yield return $"{owner}: proc targets is empty";
            if (!casts.NullOrEmpty())
            {
                if (IsArea) yield return $"{owner}: casts can't go on a proc with a radius (the ability has its own area)";
                foreach (var c in casts)
                {
                    if (c?.ability == null) { yield return $"{owner}: a proc cast has no ability"; continue; }
                    if (c.ability.groupDef != null) yield return $"{owner}: proc cast {c.ability.defName} has a cooldown group, which would put the creature's own abilities on cooldown";
                    if (c.target != BastleProcCastTarget.Self && !HasOther) yield return $"{owner}: proc cast target {c.target} needs a Hit, TakeDamage or Kill proc";
                    if (on == BastleProcEvent.Kill && c.target == BastleProcCastTarget.Other) yield return $"{owner}: a Kill proc can't cast at Other (the victim is dead by then); use OtherCell";
                    // A proc cast runs the ability's effect comps only, never its verb (no projectile, no jump).
                    if (c.ability.comps.NullOrEmpty()) yield return $"{owner}: proc cast {c.ability.defName} has no effect comps, so casting it does nothing (its verb never runs)";
                }
                if ((on == BastleProcEvent.Hit || on == BastleProcEvent.TakeDamage) && !HasCooldown && chance >= 1f)
                    yield return $"{owner}: a Hit or TakeDamage proc with casts needs a chance below 1 or a cooldown";
            }
        }
    }

    /// <summary>The throwaway ability a proc's cast goes through.</summary>
    public class BastleProcAbility : Ability
    {
        public BastleProcAbility(Pawn pawn, AbilityDef def) : base(pawn, def) { }
    }

    /// <summary>Fires procs. Entry points are called from the bastle hediff's notifications
    /// and from the kill hook, so a pawn that isn't a bastle never gets here.</summary>
    public static class BastleProcs
    {
        private class Firing
        {
            public BastleTrait trait;
            public int index;
            public BastleProc proc;
            public Thing other;
            public IntVec3 otherCell;
            public float power;
            public int previousReady;
        }

        // Per target, trait and proc: the tick it last landed and how strong it was then.
        // Not saved: a load forgets an attacker's adaptation, which only lets one through early.
        private static readonly Dictionary<long, (int tick, float strength)> adaptation = new Dictionary<long, (int, float)>();

        private static readonly List<Pawn> affected = new List<Pawn>();

        internal static void Clear() => adaptation.Clear();

        private static BastleSettings S => BastleFrameworkMod.Settings;

        private static bool? anyKill;

        /// <summary>Some trait anywhere has a Kill proc; otherwise the kill hook leaves at once.</summary>
        internal static bool AnyKillProcs
        {
            get
            {
                if (anyKill == null)
                {
                    anyKill = false;
                    foreach (var d in DefDatabase<BastleTraitDef>.AllDefsListForReading)
                    {
                        if (HasKill(d.procs)) anyKill = true;
                        if (d.rarityOverrides != null)
                            foreach (var o in d.rarityOverrides)
                                if (HasKill(o.procs)) anyKill = true;
                    }
                }
                return anyKill.Value;
            }
        }

        private static bool HasKill(List<BastleProc> procs)
        {
            if (procs == null) return false;
            foreach (var p in procs) if (p.on == BastleProcEvent.Kill) return true;
            return false;
        }

        internal static void OnHit(Hediff_Bastle bastle, Thing victim, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            if (!S.procsEnabled || BastleCombatUtility.InExtraDamage || !(victim is Pawn) || result == null || result.totalDamageDealt <= 0f) return;
            Fire(bastle, BastleProcEvent.Hit, victim, dinfo);
        }

        internal static void OnTakeDamage(Hediff_Bastle bastle, DamageInfo dinfo, float dealt)
        {
            if (!S.procsEnabled || BastleCombatUtility.InExtraDamage || dealt <= 0f) return;
            Fire(bastle, BastleProcEvent.TakeDamage, dinfo.Instigator, dinfo);
        }

        internal static void OnEvent(Hediff_Bastle bastle, BastleProcEvent ev, Thing other = null)
        {
            if (!S.procsEnabled) return;
            Fire(bastle, ev, other, null);
        }

        private static void Fire(Hediff_Bastle bastle, BastleProcEvent ev, Thing other, DamageInfo? dinfo)
        {
            var pawn = bastle.pawn;
            if (pawn == null || !pawn.Spawned) return;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var procs = traits[i].Procs;
                if (procs == null) continue;
                for (int j = 0; j < procs.Count; j++)
                    if (procs[j].on == ev) TryFire(traits[i], j, procs[j], other, dinfo);
            }
        }

        private static void TryFire(BastleTrait trait, int index, BastleProc p, Thing other, DamageInfo? dinfo)
        {
            var pawn = trait.Pawn;
            if (!Matches(p, pawn, other, dinfo)) return;
            int now = Find.TickManager.TicksGame;
            int ready = trait.ProcReadyTick(index);
            if (ready > now) return;
            float strength = trait.ProcStrength;
            float adapt = 1f;
            long key = 0;
            if (p.adaptSeconds > 0f && p.toOther && other is Pawn target)
            {
                key = Gen.HashCombineInt(target.thingIDNumber, trait.def.shortHash) * 31L + index;
                adapt = AdaptStrength(key, p, now);
                if (adapt <= 0f) return;
            }
            if (!Rand.Chance(EffectiveChance(p, strength))) return;
            int cooldown = CooldownTicks(p, strength);
            if (cooldown > 0) trait.SetProcReadyTick(index, now + cooldown);
            if (key != 0)
            {
                if (adaptation.Count > 4096) adaptation.Clear();
                adaptation[key] = (now, adapt);
            }
            var f = new Firing
            {
                trait = trait,
                index = index,
                proc = p,
                other = other,
                otherCell = other != null && other.MapHeld == pawn.Map ? other.PositionHeld : IntVec3.Invalid,
                power = strength * S.procStrengthFactor * adapt,
                previousReady = ready,
            };
            BastleScheduler.NextTick(() => Execute(f));
        }

        private static bool Matches(BastleProc p, Pawn pawn, Thing other, DamageInfo? dinfo)
        {
            if (p.attack != BastleAttackKind.Any)
            {
                if (dinfo == null) return false;
                bool melee = p.on == BastleProcEvent.Hit
                    ? BastleCombatUtility.IsMelee(dinfo.Value, other, out _)
                    : BastleCombatUtility.IsMelee(dinfo.Value, pawn, out _);
                if (melee != (p.attack == BastleAttackKind.Melee)) return false;
            }
            if (p.healthBelow > 0f && pawn.health.summaryHealth.SummaryHealthPercent >= p.healthBelow) return false;
            if (p.hostileOnly && p.HasOther && (other == null || other == pawn || !other.HostileTo(pawn))) return false;
            if (p.minDistance > 0f && (other == null || other.MapHeld != pawn.Map || other.PositionHeld.DistanceTo(pawn.Position) < p.minDistance)) return false;
            return true;
        }

        private static float AdaptStrength(long key, BastleProc p, int now)
        {
            if (!adaptation.TryGetValue(key, out var last) || now - last.tick > p.adaptSeconds * GenTicks.TicksPerRealSecond) return 1f;
            float next = last.strength * p.adaptFactor;
            return next < BastleProc.MinAdaptStrength ? 0f : next;
        }

        /// <summary>The chance a matching event sets it off, with the rarity and the settings.</summary>
        public static float EffectiveChance(BastleProc p, float strength)
        {
            float c = p.chance * (p.rarityScales == BastleProcScaling.Chance ? strength : 1f) * S.procChanceFactor;
            return Mathf.Min(1f, c);
        }

        public static int CooldownTicks(BastleProc p, float strength)
        {
            if (!p.HasCooldown) return 0;
            float ticks = p.cooldownSeconds.max > 0f
                ? p.cooldownSeconds.RandomInRange * GenTicks.TicksPerRealSecond
                : p.cooldownHours.RandomInRange * GenDate.TicksPerHour;
            if (p.rarityScales == BastleProcScaling.Cooldown && strength > 0.0001f) ticks /= strength;
            return Mathf.Max(1, Mathf.RoundToInt(ticks * S.procCooldownFactor));
        }

        private static void Refund(Firing f)
        {
            // Nothing went off (nobody in reach, a cast refused its target), so the wait isn't spent.
            if (f.trait.ProcReadyTick(f.index) > f.previousReady) f.trait.SetProcReadyTick(f.index, f.previousReady);
        }

        private static void Execute(Firing f)
        {
            var trait = f.trait;
            var pawn = trait.Pawn;
            var p = f.proc;
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;
            var bastle = BastleUtility.PeekBastle(pawn);
            if (bastle == null || !bastle.Traits.Contains(trait)) return;
            var map = pawn.Map;
            Thing other = f.other;
            bool otherHere = other != null && other.Spawned && other.Map == map;
            IntVec3 center = p.center == BastleProcCenter.Other ? (otherHere ? other.Position : f.otherCell) : pawn.Position;
            if (!center.IsValid || !center.InBounds(map)) { Refund(f); return; }

            affected.Clear();
            if (p.IsArea) CollectArea(pawn, center, map, p);
            else if (p.toOther) { if (otherHere && other is Pawn op && !op.Dead) affected.Add(op); }
            else if (p.HasPawnEffect) affected.Add(pawn);

            List<(Ability ability, LocalTargetInfo target)> casts = null;
            if (!p.casts.NullOrEmpty()) casts = PrepareCasts(pawn, p, other, otherHere, f.otherCell);

            bool any = affected.Count > 0 || p.smokeRadius > 0f || casts != null;
            if (!any) { Refund(f); return; }

            // Farthest first: a nearer pawn thrown first could find its line blocked by one
            // still standing behind it.
            if (p.pushDistance > 0f && affected.Count > 1)
                affected.SortByDescending(a => (a.Position - center).LengthHorizontalSquared);
            var reached = affected.ToArray();
            affected.Clear();
            pushTaken.Clear();
            for (int i = 0; i < reached.Length; i++)
            {
                try { ApplyTo(reached[i], pawn, p, f.power, center); }
                catch (Exception e) { Log.ErrorOnce($"[Bastle Framework] proc of {trait.def.defName} failed on {reached[i]}: {e}", Gen.HashCombineInt(trait.def.shortHash, 0x9A0C)); }
            }
            pushTaken.Clear();
            if (p.smokeRadius > 0f) GasUtility.AddGas(center, map, GasType.BlindSmoke, p.smokeRadius);
            if (casts != null)
                foreach (var (ability, target) in casts)
                {
                    if (!pawn.Spawned || pawn.Dead) break;
                    if (target.HasThing && !target.Thing.Spawned) continue;
                    try { ability.Activate(target, LocalTargetInfo.Invalid); }
                    catch (Exception e) { Log.ErrorOnce($"[Bastle Framework] proc cast {ability.def.defName} of {trait.def.defName} failed: {e}", Gen.HashCombineInt(ability.def.shortHash, 0x9A0D)); }
                }

            bool seen = map == Find.CurrentMap && center.ShouldSpawnMotesAt(map) && !center.Fogged(map);
            if (seen && p.fleck != null)
                FleckMaker.Static(center.ToVector3Shifted(), map, p.fleck, p.fleckScale > 0f ? p.fleckScale : p.IsArea ? p.radius + 1f : 1f);
            p.sound?.PlayOneShot(new TargetInfo(center, map));
            if (!p.visuals.NullOrEmpty())
                BastleEventVisuals.PlayProc(trait, p, pawn, otherHere ? other : null, p.center == BastleProcCenter.Other ? center : f.otherCell, reached);
            if (p.discovers && !trait.discovered && BastleUtility.PlayerCanObserve(pawn)) trait.Discover();
        }

        private static void CollectArea(Pawn source, IntVec3 center, Map map, BastleProc p)
        {
            var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a.Dead || !a.Position.InHorDistOf(center, p.radius)) continue;
                if (!InTargets(source, a, p.targets)) continue;
                if (p.blockedByWalls && a.Position != center && !GenSight.LineOfSight(center, a.Position, map, skipFirstCell: true)) continue;
                affected.Add(a);
            }
        }

        private static bool InTargets(Pawn source, Pawn a, BastleProcTargets t)
        {
            if (a == source) return (t & BastleProcTargets.Self) != 0;
            if (a.HostileTo(source)) return (t & BastleProcTargets.Enemies) != 0;
            if ((t & BastleProcTargets.Allies) == 0) return false;
            var f = source.Faction;
            return f != null
                ? a.Faction == f || (a.Faction != null && a.Faction.RelationKindWith(f) == FactionRelationKind.Ally)
                : a.Faction == null && a.def == source.def;
        }

        private static readonly HashSet<IntVec3> pushTaken = new HashSet<IntVec3>();

        private static void ApplyTo(Pawn a, Pawn source, BastleProc p, float power, IntVec3 center)
        {
            if (a.Dead || !a.Spawned) return;
            var map = a.Map;
            bool landed = false;
            if (p.hediff != null) landed |= GiveHediff(a, p, power);
            if (p.heal > 0f) { BastleCombatUtility.HealInjuries(a, p.heal * power); landed = true; }
            if (p.food > 0f && a.needs?.food != null) { a.needs.food.CurLevelPercentage += p.food * power; landed = true; }
            if (p.stunSeconds > 0f && a.stances?.stunner != null)
            {
                a.stances.stunner.StunFor(Mathf.Max(1, Mathf.RoundToInt(p.stunSeconds * power * GenTicks.TicksPerRealSecond)), source);
                landed = true;
            }
            if (p.damageDef != null && p.damage > 0f)
            {
                BastleCombatUtility.DealExtraDamage(a, new DamageInfo(p.damageDef, p.damage * power, p.armorPenetration, -1f, source));
                landed = true;
            }
            if (p.mechanoidDamageDef != null && p.mechanoidDamage > 0f && a.RaceProps.IsMechanoid && !a.Dead)
            {
                BastleCombatUtility.DealExtraDamage(a, new DamageInfo(p.mechanoidDamageDef, p.mechanoidDamage * power, -1f, -1f, source));
                landed = true;
            }
            if (p.igniteChance > 0f && a.Spawned && !a.Dead && Rand.Chance(p.igniteChance)) { a.TryAttachFire(Rand.Range(0.2f, 0.4f), source); landed = true; }
            if (p.mentalState != null && !a.Dead) landed |= TryMentalState(a, source, p, power);
            if (p.skipDistance.max > 0f && !a.Dead && a.Spawned) landed |= TrySkip(a, p);
            if (p.pushDistance > 0f && !a.Dead && a.Spawned) landed |= TryPush(a, center, p);
            if (landed && !p.text.NullOrEmpty() && a.Spawned && map == Find.CurrentMap && a.Position.ShouldSpawnMotesAt(map) && !a.Position.Fogged(map))
                MoteMaker.ThrowText(a.DrawPos, map, p.text);
        }

        private static bool GiveHediff(Pawn a, BastleProc p, float power)
        {
            var h = a.health.hediffSet.GetFirstHediffOfDef(p.hediff);
            bool fresh = h == null;
            if (fresh) h = a.health.AddHediff(p.hediff);
            if (h == null) return false;
            if (p.severity > 0f) h.Severity = Mathf.Max(fresh ? 0f : h.Severity, p.severity * power);
            var d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null)
            {
                if (p.hediffSeconds > 0f)
                {
                    // SetDuration sets the total too, so the remaining-time bar and a later
                    // refresh both work from this length rather than the XML fallback.
                    int ticks = Mathf.RoundToInt(p.hediffSeconds * power * GenTicks.TicksPerRealSecond);
                    d.SetDuration(fresh ? ticks : Mathf.Max(d.ticksToDisappear, ticks));
                }
                else if (!fresh) d.ResetElapsedTicks();
            }
            return true;
        }

        private static bool TryMentalState(Pawn a, Pawn source, BastleProc p, float power)
        {
            var r = a.RaceProps;
            if (r.IsMechanoid || r.IsAnomalyEntity || a.Downed || a.InMentalState || a.mindState?.mentalStateHandler == null) return false;
            var state = r.Animal && p.animalMentalState != null ? p.animalMentalState : p.mentalState;
            float chance = p.mentalStateChance * power * (p.psychic ? a.GetStatValue(StatDefOf.PsychicSensitivity) : 1f);
            if (!Rand.Chance(chance)) return false;
            return a.mindState.mentalStateHandler.TryStartMentalState(state, forceWake: true, otherPawn: source);
        }

        private static bool TrySkip(Pawn a, BastleProc p)
        {
            if (a.BodySize > p.skipMaxBodySize || a.roping?.IsRoped == true) return false;
            var map = a.Map;
            var from = a.Position;
            float min = p.skipDistance.min, max = p.skipDistance.max;
            if (!CellFinder.TryFindRandomCellNear(from, map, Mathf.CeilToInt(max),
                    c => c.Standable(map) && !c.Fogged(map) && c.DistanceTo(from) >= min && c.GetFirstPawn(map) == null, out var to))
                return false;
            FleckMaker.Static(from, map, FleckDefOf.PsycastSkipFlashEntry);
            SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(from, map));
            a.Position = to;
            a.Notify_Teleported();
            FleckMaker.Static(to, map, FleckDefOf.PsycastSkipInnerExit);
            // Psycast_Skip_Exit is a Royalty def; without Royalty its DefOf field is null and
            // PlayOneShot logs an error on every skip. The entry sound is Core.
            (ModsConfig.RoyaltyActive ? SoundDefOf.Psycast_Skip_Exit : SoundDefOf.Psycast_Skip_Entry).PlayOneShot(new TargetInfo(to, map));
            return true;
        }

        private static bool TryPush(Pawn a, IntVec3 center, BastleProc p)
        {
            if (a.Downed || a.BodySize > p.pushMaxBodySize || a.carryTracker?.CarriedThing != null || a.roping?.IsRoped == true) return false;
            var map = a.Map;
            Vector3 dir = (a.Position - center).ToVector3();
            if (dir.sqrMagnitude < 0.01f) dir = Vector3Utility.RandomHorizontalOffset(1f);
            dir.Normalize();
            Vector3 start = a.Position.ToVector3Shifted();
            for (float d = p.pushDistance; d >= 1f; d -= 1f)
            {
                IntVec3 c = (start + dir * d).ToIntVec3();
                if (c == a.Position || !c.InBounds(map) || c.Fogged(map) || !c.Standable(map) || pushTaken.Contains(c) || c.GetFirstPawn(map) != null) continue;
                if (!GenSight.LineOfSight(a.Position, c, map, skipFirstCell: true)) continue;
                pushTaken.Add(c);
                a.rotationTracker.FaceCell(center);
                var flyer = PawnFlyer.MakeFlyer(ThingDefOf.PawnFlyer_Stun, a, c, null, null);
                if (flyer == null) return false;
                GenSpawn.Spawn(flyer, c, map);
                return true;
            }
            return false;
        }

        // Each cast is checked as the ability itself would check it, minus range and sight. A
        // cast that can't go off is left out; with none left, the proc doesn't fire.
        private static List<(Ability, LocalTargetInfo)> PrepareCasts(Pawn caster, BastleProc p, Thing other, bool otherHere, IntVec3 otherCell)
        {
            List<(Ability, LocalTargetInfo)> list = null;
            foreach (var c in p.casts)
            {
                LocalTargetInfo target = LocalTargetInfo.Invalid;
                switch (c.target)
                {
                    case BastleProcCastTarget.Self: target = caster; break;
                    case BastleProcCastTarget.Other: if (otherHere && !(other is Pawn op && op.Dead)) target = other; break;
                    case BastleProcCastTarget.OtherCell: if (otherCell.IsValid) target = otherCell; break;
                }
                if (!target.IsValid || !target.Cell.InBounds(caster.Map)) continue;
                try
                {
                    var tp = c.ability.verbProperties?.targetParams;
                    if (tp != null)
                    {
                        if (target.HasThing && !tp.CanTarget(target.Thing))
                        {
                            if (!tp.canTargetLocations) continue;
                            target = target.Cell;
                        }
                        else if (!target.HasThing && !tp.canTargetLocations) continue;
                    }
                    var ability = new BastleProcAbility(caster, c.ability);
                    // EffectComps first: CanApplyOn reads the list that getter fills.
                    var comps = ability.EffectComps;
                    if (!ability.CanApplyOn(target)) continue;
                    bool ok = true;
                    if (comps != null)
                        for (int i = 0; i < comps.Count && ok; i++) ok = comps[i].Valid(target, false);
                    if (!ok) continue;
                    (list ?? (list = new List<(Ability, LocalTargetInfo)>())).Add((ability, target));
                }
                catch (Exception e)
                {
                    Log.ErrorOnce($"[Bastle Framework] checking proc cast {c.ability.defName} failed: {e}", Gen.HashCombineInt(c.ability.shortHash, 0x9A0E));
                }
            }
            return list;
        }
    }

    /// <summary>Kill procs. Called from Pawn.Kill while the victim is still spawned.</summary>
    [HarmonyLib.HarmonyPatch(typeof(RecordsUtility), nameof(RecordsUtility.Notify_PawnKilled))]
    public static class Patch_RecordsUtility_Notify_PawnKilled
    {
        public static void Postfix(Pawn killed, Pawn killer)
        {
            // A kill made by a proc's own damage doesn't set off another proc: a Kill proc with
            // area damage would otherwise chain through a crowd.
            if (killer == null || killer == killed || BastleCombatUtility.InExtraDamage || !BastleProcs.AnyKillProcs) return;
            try
            {
                var bastle = BastleUtility.PeekBastle(killer);
                if (bastle != null) BastleProcs.OnEvent(bastle, BastleProcEvent.Kill, killed);
            }
            catch (Exception e) { Log.ErrorOnce($"[Bastle Framework] kill proc check failed: {e}", 0x9A0F); }
        }
    }
}
