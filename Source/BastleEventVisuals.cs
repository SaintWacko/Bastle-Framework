using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>What a one-off visual plays on. Hit: the creature's damage got through to a
    /// pawn. Struck: it took damage from someone. Heal: one of its injuries healed. Dodge: it
    /// dodged a melee attack. Deflect: it turned a hit aside (BastleTraitDef.deflect). Proc: a
    /// proc went off (only in BastleProc.visuals, where on is ignored).</summary>
    public enum BastleVisualEvent { Hit, Struck, Heal, Dodge, Deflect, Proc }

    /// <summary>Self: the creature. Other: the pawn hit, the attacker, the shooter, or the pawn
    /// killed (where it fell). Affected: each pawn a proc reached.</summary>
    public enum BastleVisualSpot { Self, Other, Affected }

    /// <summary>The event a visual plays on. One shared instance: events are raised on the
    /// main thread and a visual never raises another.</summary>
    public class BastleVisualContext
    {
        public Map map;
        public Pawn self;
        public Thing other;
        public IntVec3 otherCell = IntVec3.Invalid;
        public IList<Pawn> affected;

        internal void Reset(Map map, Pawn self)
        {
            this.map = map;
            this.self = self;
            other = null;
            otherCell = IntVec3.Invalid;
            affected = null;
        }

        /// <summary>Every visible spot of the kind: on this map, not fogged, not an invisible pawn.</summary>
        public void Spots(BastleVisualSpot spot, List<(Vector3 pos, TargetInfo target)> into)
        {
            into.Clear();
            switch (spot)
            {
                case BastleVisualSpot.Self: AddThing(self, into); break;
                case BastleVisualSpot.Other:
                    if (other != null && other.Spawned && other.Map == map) AddThing(other, into);
                    else if (otherCell.IsValid) AddPos(otherCell.ToVector3Shifted(), new TargetInfo(otherCell, map), into);
                    break;
                case BastleVisualSpot.Affected:
                    if (affected == null) AddThing(self, into);
                    else for (int i = 0; i < affected.Count; i++) AddThing(affected[i], into);
                    break;
            }
        }

        private void AddThing(Thing thing, List<(Vector3, TargetInfo)> into)
        {
            if (thing == null || !thing.Spawned || thing.Map != map) return;
            if (thing is Pawn p && p.IsHiddenFromPlayer()) return;
            AddPos(thing.DrawPos, new TargetInfo(thing), into);
        }

        private void AddPos(Vector3 pos, TargetInfo target, List<(Vector3, TargetInfo)> into)
        {
            IntVec3 cell = pos.ToIntVec3();
            if (!cell.InBounds(map) || !cell.ShouldSpawnMotesAt(map, false) || cell.Fogged(map)) return;
            into.Add((pos, target));
        }
    }

    /// <summary>
    /// A visual played once when something happens to the creature, as against the ones drawn
    /// all the time (overlays, surfaces, effecters). Listed in BastleTraitDef.eventVisuals or a
    /// proc's visuals. Spawned on game ticks inside the event, never per frame, so nothing
    /// piles up while paused. Ported from Entad Framework's event visuals.
    /// </summary>
    public abstract class BastleEventVisual
    {
        public BastleVisualEvent on;
        public BastleVisualSpot at = BastleVisualSpot.Self;
        public float chance = 1f;
        /// <summary>At most once per this many ticks per creature, so a hail of hits doesn't stack flashes.</summary>
        public int minIntervalTicks;
        /// <summary>Hit and Struck: which hits count.</summary>
        public BastleAttackKind attack = BastleAttackKind.Any;
        /// <summary>Only when the other pawn is living flesh (a vampiric drink, not a bite on a mech).</summary>
        public bool otherFleshOnly;
        /// <summary>Play before the trait is discovered (null: the trait's rule, then the setting).
        /// Proc visuals ignore it: the proc identifies its trait.</summary>
        public bool? beforeIdentified;

        private static int nextId;
        [Unsaved] private int id;
        internal int Id => id != 0 ? id : (id = ++nextId);

        protected static readonly List<(Vector3 pos, TargetInfo target)> spotsA = new List<(Vector3, TargetInfo)>();
        protected static readonly List<(Vector3 pos, TargetInfo target)> spotsB = new List<(Vector3, TargetInfo)>();

        public abstract void Play(BastleVisualContext ctx);

        public virtual IEnumerable<string> ConfigErrors(string owner)
        {
            if (chance <= 0f || chance > 1f) yield return $"{owner}: event visual chance must be above 0 and at most 1";
            if (at == BastleVisualSpot.Affected && on != BastleVisualEvent.Proc) yield return $"{owner}: visual spot Affected only exists on proc visuals";
        }

        protected static void Throw(FleckDef fleck, Vector3 pos, Map map, float scale, float angle, float speed, float rotationRate, Color color, float rotation)
        {
            var data = FleckMaker.GetDataStatic(pos, map, fleck, scale);
            data.velocityAngle = angle;
            data.velocitySpeed = speed;
            data.rotationRate = rotationRate;
            data.rotation = rotation;
            if (color != Color.white) data.instanceColor = color;
            map.flecks.CreateFleck(data);
        }
    }

    /// <summary>A burst of flecks at the spot: a flash, sparks, a healing pulse.</summary>
    public class BastleVisual_Flecks : BastleEventVisual
    {
        public FleckDef fleck;
        public IntRange count = new IntRange(1, 1);
        public FloatRange scale = new FloatRange(0.5f, 0.7f);
        /// <summary>Direction (0 = up the screen) and speed in cells per second.</summary>
        public FloatRange angle = new FloatRange(0f, 360f);
        public FloatRange speed = FloatRange.Zero;
        public FloatRange rotationRate = FloatRange.Zero;
        public FloatRange rotation = new FloatRange(0f, 360f);
        /// <summary>Radius around the spot the flecks start in.</summary>
        public float spread;
        public Color color = Color.white;

        public override void Play(BastleVisualContext ctx)
        {
            ctx.Spots(at, spotsA);
            foreach (var (pos, _) in spotsA)
            {
                int n = count.RandomInRange;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = spread > 0f ? pos + Rand.InsideUnitCircleVec3 * spread : pos;
                    Throw(fleck, p, ctx.map, scale.RandomInRange, angle.RandomInRange, speed.RandomInRange, rotationRate.RandomInRange, color, rotation.RandomInRange);
                }
            }
        }

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (var e in base.ConfigErrors(owner)) yield return e;
            if (fleck == null) yield return $"{owner}: flecks visual needs a fleck";
        }
    }

    /// <summary>A vanilla effecter at the spot, triggered once (the sparks and clank armour makes
    /// when a blow glances off). Instant effecters only: it is cleaned up straight away.</summary>
    public class BastleVisual_Effecter : BastleEventVisual
    {
        public EffecterDef effecter;
        /// <summary>The effecter's second target, which aims sprays.</summary>
        public BastleVisualSpot toward = BastleVisualSpot.Other;

        public override void Play(BastleVisualContext ctx)
        {
            ctx.Spots(at, spotsA);
            if (spotsA.Count == 0) return;
            ctx.Spots(toward, spotsB);
            foreach (var (_, target) in spotsA)
            {
                var e = effecter.Spawn();
                e.Trigger(target, spotsB.Count > 0 ? spotsB[0].target : target);
                e.Cleanup();
            }
        }

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (var e in base.ConfigErrors(owner)) yield return e;
            if (effecter == null) yield return $"{owner}: effecter visual needs an effecter";
        }
    }

    /// <summary>Lightning zaps: around the spot, or with 'to', a jagged bolt from the spot to
    /// each 'to' spot (a thunderclap from its victim to everyone it stunned).</summary>
    public class BastleVisual_Arcs : BastleEventVisual
    {
        [NoTranslate] public string texFolder = "Things/Mote/BF_Arc";
        public Color arcColor = new Color(0.7f, 0.85f, 1f, 0.9f);
        public IntRange count = new IntRange(2, 3);
        public FloatRange reach = new FloatRange(0.4f, 0.9f);
        public float width = 0.18f;
        public int arcTicks = 6;
        public BastleVisualSpot? to;
        /// <summary>Pieces of a bolt to a 'to' spot, each bent sideways by up to jag cells.</summary>
        public int segments = 3;
        public float jag = 0.3f;
        /// <summary>A spark at the far end of each zap.</summary>
        public FleckDef fleck;
        public FloatRange fleckScale = new FloatRange(0.1f, 0.16f);

        [Unsaved] private List<Material> materials;

        public override void Play(BastleVisualContext ctx)
        {
            var mats = materials ?? (materials = BastleEventDraw.LoadFrames(texFolder, arcColor));
            if (mats.Count == 0) return;
            ctx.Spots(at, spotsA);
            if (spotsA.Count == 0) return;
            if (to.HasValue)
            {
                ctx.Spots(to.Value, spotsB);
                foreach (var (a, _) in spotsA)
                    foreach (var (b, _) in spotsB)
                        if ((b - a).MagnitudeHorizontalSquared() > 0.04f) Bolt(ctx.map, mats, a, b);
                return;
            }
            foreach (var (pos, _) in spotsA)
            {
                int n = count.RandomInRange;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = pos + Rand.InsideUnitCircleVec3 * 0.15f;
                    Vector3 b = a + Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * new Vector3(reach.RandomInRange, 0f, 0f);
                    BastleEventDraw.AddZap(ctx.map, a, b, mats.RandomElement(), width, arcTicks);
                    Spark(ctx.map, b);
                }
            }
        }

        private void Bolt(Map map, List<Material> mats, Vector3 a, Vector3 b)
        {
            int n = Mathf.Max(1, segments);
            Vector3 dir = b - a;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized;
            Vector3 last = a;
            for (int i = 1; i <= n; i++)
            {
                Vector3 next = i == n ? b : Vector3.Lerp(a, b, i / (float)n) + side * Rand.Range(-jag, jag);
                BastleEventDraw.AddZap(map, last, next, mats.RandomElement(), width, arcTicks);
                last = next;
            }
            Spark(map, b);
        }

        private void Spark(Map map, Vector3 pos)
        {
            if (fleck != null) Throw(fleck, pos, map, fleckScale.RandomInRange, Rand.Range(0f, 360f), Rand.Range(0.2f, 0.6f), 0f, Color.white, Rand.Range(0f, 360f));
        }

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (var e in base.ConfigErrors(owner)) yield return e;
            if (arcTicks < 1) yield return $"{owner}: arcs visual arcTicks must be at least 1";
            if (to == BastleVisualSpot.Affected && on != BastleVisualEvent.Proc) yield return $"{owner}: visual spot Affected only exists on proc visuals";
        }
    }

    /// <summary>Flecks flowing from one spot to another, fading as they arrive. 'at' isn't used.</summary>
    public class BastleVisual_Stream : BastleEventVisual
    {
        public FleckDef fleck;
        public BastleVisualSpot from = BastleVisualSpot.Other;
        public BastleVisualSpot to = BastleVisualSpot.Self;
        public IntRange count = new IntRange(10, 14);
        public FloatRange scale = new FloatRange(0.45f, 0.65f);
        public Color color = Color.white;
        /// <summary>How far along the path the flecks start (0 = all at the source), so they arrive one after another.</summary>
        public float lead = 0.5f;
        public float spread = 0.2f;
        /// <summary>Tiles per second. Each fleck's life ends where the target stood when it was
        /// thrown: a thrown fleck whose air time runs out skids on, which reads as the stream
        /// flying past the pawn.</summary>
        public FloatRange speed = new FloatRange(7f, 9f);

        public override void Play(BastleVisualContext ctx)
        {
            ctx.Spots(from, spotsA);
            if (spotsA.Count == 0) return;
            ctx.Spots(to, spotsB);
            foreach (var (a, _) in spotsA)
                foreach (var (b, _) in spotsB)
                {
                    if ((b - a).MagnitudeHorizontalSquared() < 0.09f) continue;
                    int n = count.RandomInRange;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 start = Vector3.Lerp(a, b, Rand.Range(0f, lead)) + Rand.InsideUnitCircleVec3 * spread;
                        Vector3 path = b - start;
                        float dist = Mathf.Sqrt(path.MagnitudeHorizontalSquared());
                        if (dist < 0.05f) continue;
                        // Slower over a short hop, so the shortest life the fleck has still ends at the target.
                        float v = Mathf.Min(speed.RandomInRange, dist / Mathf.Max(0.05f, fleck.fadeInTime + fleck.fadeOutTime));
                        var data = FleckMaker.GetDataStatic(start, ctx.map, fleck, scale.RandomInRange);
                        data.velocityAngle = path.AngleFlat();
                        data.velocitySpeed = v;
                        data.rotation = Rand.Range(0f, 360f);
                        data.solidTimeOverride = Mathf.Max(0f, dist / v - fleck.fadeInTime - fleck.fadeOutTime);
                        if (color != Color.white) data.instanceColor = color;
                        ctx.map.flecks.CreateFleck(data);
                    }
                }
        }

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (var e in base.ConfigErrors(owner)) yield return e;
            if (fleck == null) yield return $"{owner}: stream visual needs a fleck";
            if (speed.min <= 0f) yield return $"{owner}: stream speed must be above 0";
        }
    }

    /// <summary>Faded copies of the creature sliding off it: a dodge that leaves the blow
    /// hitting an image. Plays on the pawn at the spot, moving away from the other spot.</summary>
    public class BastleVisual_Afterimage : BastleEventVisual
    {
        public int count = 3;
        public FloatRange distance = new FloatRange(0.8f, 1.1f);
        public int lifeTicks = 30;
        public Color color = new Color(0.8f, 0.88f, 1f, 0.55f);
        /// <summary>East or west only: a copy sliding north or south is drawn over or under the
        /// pawn and barely shows.</summary>
        public bool horizontal = true;

        public override void Play(BastleVisualContext ctx)
        {
            ctx.Spots(at, spotsA);
            if (spotsA.Count == 0) return;
            ctx.Spots(at == BastleVisualSpot.Other ? BastleVisualSpot.Self : BastleVisualSpot.Other, spotsB);
            foreach (var (pos, target) in spotsA)
            {
                if (!(target.Thing is Pawn pawn)) continue;
                Vector3 dir = spotsB.Count > 0 ? pos - spotsB[0].pos : Vector3.zero;
                dir.y = 0f;
                if (horizontal)
                    dir = new Vector3(dir.x > 0.3f ? 1f : dir.x < -0.3f ? -1f : Rand.Bool ? 1f : -1f, 0f, 0f);
                else
                {
                    dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward;
                    dir = new Vector3(-dir.z, 0f, dir.x) * (Rand.Bool ? 1f : -1f);
                }
                float d = distance.RandomInRange;
                for (int i = 0; i < count; i++)
                {
                    float share = (i + 1) / (float)count;
                    Color c = color;
                    c.a *= 1f - i / (float)(count + 1);
                    BastleEventDraw.AddAfterimage(pawn, pos, dir * (d * share / Mathf.Max(1, lifeTicks)), lifeTicks, c);
                }
            }
        }

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (var e in base.ConfigErrors(owner)) yield return e;
            if (count < 1 || lifeTicks < 1) yield return $"{owner}: afterimage visual needs count and lifeTicks of at least 1";
        }
    }

    /// <summary>Glowing strands writhing around the line between two spots, twisting round each
    /// other and meeting at the far end, the head racing ahead and the tail following it in
    /// (life drawn out of a victim, madness poured into one). A fleck can't bend, so these are
    /// drawn every frame as short stretched segments of one soft strip. 'at' isn't used.</summary>
    public class BastleVisual_Ribbons : BastleEventVisual
    {
        public BastleVisualSpot from = BastleVisualSpot.Self;
        public BastleVisualSpot to = BastleVisualSpot.Other;
        public Color color = Color.white;
        public IntRange strands = new IntRange(3, 3);
        /// <summary>How far a strand swings off the line, as a fraction of the distance, capped in
        /// tiles. Each strand is two sine waves under an envelope pinned at both ends.</summary>
        public float bulge = 0.14f;
        public float maxBulge = 0.8f;
        public FloatRange turns = new FloatRange(0.8f, 1.8f);
        public FloatRange spin = new FloatRange(5f, 10f);
        public float chaos = 0.45f;
        public float width = 0.32f;
        public float coreWidth = 0.09f;
        public float coreWhiten = 0.55f;
        /// <summary>Tiles per second for the head; the flight never takes under minTravel seconds.</summary>
        public float speed = 14f;
        public float minTravel = 0.12f;
        /// <summary>Length of the lit stretch behind the head, as a fraction of the path.</summary>
        public float tail = 0.3f;
        public float stagger = 0.05f;

        public override void Play(BastleVisualContext ctx)
        {
            ctx.Spots(from, spotsA);
            if (spotsA.Count == 0) return;
            ctx.Spots(to, spotsB);
            foreach (var (a, ta) in spotsA)
                foreach (var (b, tb) in spotsB)
                {
                    float dist = Mathf.Sqrt((b - a).MagnitudeHorizontalSquared());
                    if (dist < 0.3f) continue;
                    int n = strands.RandomInRange;
                    var s = new BastleEventDraw.Strand[n];
                    float amp = Mathf.Min(dist * bulge, maxBulge);
                    float around = Mathf.PI * 2f / n;
                    for (int k = 0; k < n; k++)
                    {
                        float side = Rand.Bool ? 1f : -1f;
                        s[k] = new BastleEventDraw.Strand
                        {
                            amp = amp * Rand.Range(0.7f, 1.15f),
                            freq = turns.RandomInRange * Mathf.PI * 2f,
                            phase = k * around + Rand.Range(-0.6f, 0.6f),
                            spin = spin.RandomInRange * side,
                            freq2 = turns.RandomInRange * 2.3f * Mathf.PI * 2f,
                            phase2 = Rand.Range(0f, Mathf.PI * 2f),
                            spin2 = -spin.RandomInRange * side * 1.4f,
                            delay = k * stagger + Rand.Range(0f, stagger * 0.5f),
                        };
                    }
                    BastleEventDraw.AddRibbon(new BastleEventDraw.Ribbon
                    {
                        def = this,
                        map = ctx.map,
                        fromThing = ta.HasThing ? ta.Thing : null,
                        toThing = tb.HasThing ? tb.Thing : null,
                        fromPos = a,
                        toPos = b,
                        strands = s,
                        travel = Mathf.Max(minTravel, dist / speed),
                    });
                }
        }

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (var e in base.ConfigErrors(owner)) yield return e;
            if (speed <= 0f || tail <= 0f) yield return $"{owner}: ribbon speed and tail must be above 0";
            if (strands.min < 1) yield return $"{owner}: ribbons need at least one strand";
        }
    }

    /// <summary>Faded copies left behind while the creature runs (BastleTraitDef.afterimages).</summary>
    public class BastleAfterimages
    {
        public int intervalTicks = 5;
        public int lifeTicks = 22;
        public Color color = new Color(1f, 0.85f, 0.55f, 0.45f);
    }

    /// <summary>Plays event visuals. Every entry point leaves after one settings read while
    /// they are off, and after one null check per trait for a creature that has none.</summary>
    public static class BastleEventVisuals
    {
        public static bool On => BastleFrameworkMod.Settings.eventEffectsEnabled;

        private static readonly BastleVisualContext ctx = new BastleVisualContext();

        // Last tick each (creature, visual) played, for minIntervalTicks. Cleared when large:
        // that lets one flash through early, nothing worse.
        private static readonly Dictionary<long, int> lastPlayed = new Dictionary<long, int>();

        internal static void Clear() => lastPlayed.Clear();

        private static bool? anyHeal, anyDodge;

        internal static bool AnyHealVisuals => anyHeal ?? (anyHeal = AnyFor(BastleVisualEvent.Heal)).Value;
        internal static bool AnyDodgeVisuals => anyDodge ?? (anyDodge = AnyFor(BastleVisualEvent.Dodge)).Value;

        private static bool AnyFor(BastleVisualEvent ev)
        {
            bool Has(List<BastleEventVisual> list)
            {
                if (list != null) foreach (var v in list) if (v.on == ev) return true;
                return false;
            }
            foreach (var d in DefDatabase<BastleTraitDef>.AllDefsListForReading)
            {
                if (Has(d.eventVisuals)) return true;
                if (d.rarityOverrides != null) foreach (var o in d.rarityOverrides) if (Has(o.eventVisuals)) return true;
            }
            return false;
        }

        /// <summary>An event on the creature: every trait's visuals for it that show now (ShowsVisual).</summary>
        internal static void Play(Hediff_Bastle bastle, BastleVisualEvent ev, Thing other, DamageInfo? dinfo = null)
        {
            if (!On) return;
            var pawn = bastle.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map != Find.CurrentMap) return;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                var list = t.EventVisuals;
                if (list == null) continue;
                for (int j = 0; j < list.Count; j++)
                {
                    var v = list[j];
                    if (v.on != ev || !t.ShowsVisual(v.beforeIdentified) || !Passes(v, pawn, other, dinfo, ev)) continue;
                    ctx.Reset(pawn.Map, pawn);
                    ctx.other = other;
                    if (other != null && other.MapHeld == pawn.Map) ctx.otherCell = other.PositionHeld;
                    Run(pawn, v);
                }
            }
        }

        /// <summary>A proc went off. Its visuals show whether the trait is known or not: the
        /// proc reveals it when the player sees it.</summary>
        internal static void PlayProc(BastleTrait trait, BastleProc p, Pawn pawn, Thing other, IntVec3 otherCell, IList<Pawn> affected)
        {
            if (!On || !pawn.Spawned || pawn.Map != Find.CurrentMap) return;
            for (int i = 0; i < p.visuals.Count; i++)
            {
                ctx.Reset(pawn.Map, pawn);
                ctx.other = other;
                ctx.otherCell = otherCell;
                ctx.affected = affected;
                Run(pawn, p.visuals[i]);
            }
        }

        private static bool Passes(BastleEventVisual v, Pawn pawn, Thing other, DamageInfo? dinfo, BastleVisualEvent ev)
        {
            if (v.otherFleshOnly && !(other is Pawn op && op.RaceProps.IsFlesh)) return false;
            if (v.attack != BastleAttackKind.Any && dinfo.HasValue && (ev == BastleVisualEvent.Hit || ev == BastleVisualEvent.Struck))
            {
                bool melee = ev == BastleVisualEvent.Hit
                    ? BastleCombatUtility.IsMelee(dinfo.Value, other, out _)
                    : BastleCombatUtility.IsMelee(dinfo.Value, pawn, out _);
                if (melee != (v.attack == BastleAttackKind.Melee)) return false;
            }
            return true;
        }

        private static void Run(Pawn pawn, BastleEventVisual v)
        {
            int now = Find.TickManager.TicksGame;
            long key = ((long)pawn.thingIDNumber << 20) ^ v.Id;
            if (v.minIntervalTicks > 0)
            {
                if (lastPlayed.TryGetValue(key, out int last) && now - last < v.minIntervalTicks) return;
                if (lastPlayed.Count > 2048) lastPlayed.Clear();
                lastPlayed[key] = now;
            }
            // Seeded, so what is on screen doesn't move the game's random stream.
            Rand.PushState(Gen.HashCombineInt((int)key, now));
            try
            {
                if (v.chance >= 1f || Rand.Chance(v.chance)) v.Play(ctx);
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce($"[Bastle Framework] {v.on} visual {v.GetType().Name} failed: {e}", Gen.HashCombineInt(v.Id, 0x5EE7));
            }
            finally { Rand.PopState(); }
        }
    }

    /// <summary>
    /// What event visuals leave on screen for a moment and draw every frame: zaps (stretched
    /// arc sprites), ribbons and afterimages. Drawn by MapComponent_BastleVisuals for the map in
    /// view. Zaps and afterimages age by game tick, ribbons in real time while unpaused, so a
    /// pause holds all of them where they are. Each list is capped for the world map, where
    /// nothing draws (and so nothing expires ribbons) while procs on a colony map keep adding.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BastleEventDraw
    {
        internal static List<Material> LoadFrames(string folder, Color color)
        {
            var materials = new List<Material>();
            foreach (var tex in ContentFinder<Texture2D>.GetAllInFolder(folder))
                materials.Add(MaterialPool.MatFrom(tex, ShaderDatabase.MoteGlow, color));
            if (materials.Count == 0) Log.ErrorOnce($"[Bastle Framework] no textures in arc folder '{folder}'", folder.GetHashCode());
            return materials;
        }

        internal static void Clear()
        {
            zaps.Clear();
            ribbons.Clear();
            ghosts.Clear();
        }

        // ---- zaps

        private class Zap
        {
            public Map map;
            public Vector3 a, b;
            public Material mat;
            public float width;
            public int until;
        }

        private static readonly List<Zap> zaps = new List<Zap>();
        private const int MaxZaps = 96;

        internal static void AddZap(Map map, Vector3 a, Vector3 b, Material mat, float width, int ticks)
        {
            if (zaps.Count >= MaxZaps) zaps.RemoveAt(0);
            zaps.Add(new Zap { map = map, a = a, b = b, mat = mat, width = width, until = Find.TickManager.TicksGame + ticks });
        }

        /// <summary>An arc sprite stretched from a to b.</summary>
        internal static void DrawArc(Material mat, Vector3 a, Vector3 b, float width, float y, MaterialPropertyBlock block = null)
        {
            Vector3 dir = b - a;
            float length = Mathf.Sqrt(dir.MagnitudeHorizontalSquared());
            if (length < 0.05f) return;
            float deg = -Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            Vector3 mid = (a + b) * 0.5f;
            mid.y = y;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(mid, Quaternion.AngleAxis(deg, Vector3.up), new Vector3(length * 1.15f, 1f, width)), mat, 0, null, 0, block);
        }

        // ---- ribbons

        internal struct Strand
        {
            public float amp, freq, phase, spin, freq2, phase2, spin2, delay;
        }

        internal class Ribbon
        {
            public BastleVisual_Ribbons def;
            public Map map;
            // Ends follow their pawns while they stay on the map, else keep the last spot.
            public Thing fromThing, toThing;
            public Vector3 fromPos, toPos;
            public Strand[] strands;
            public float travel;
            public float age;
        }

        private static readonly List<Ribbon> ribbons = new List<Ribbon>();
        private const int MaxRibbons = 32;
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private static Material ribbonMat;

        internal static void AddRibbon(Ribbon r)
        {
            if (ribbons.Count >= MaxRibbons) ribbons.RemoveAt(0);
            ribbons.Add(r);
        }

        // ---- afterimages

        private class Ghost
        {
            public Pawn pawn;
            public Map map;
            public Vector3 pos;
            public Vector3 driftPerTick;
            public Rot4 rot;
            public int start;
            public int life;
            public Color color;
        }

        private static readonly List<Ghost> ghosts = new List<Ghost>();
        // Portraits come from a pool vanilla reuses, so a material made for one stays valid for
        // whoever is in it next.
        private static readonly Dictionary<RenderTexture, Material> ghostMats = new Dictionary<RenderTexture, Material>();
        private static readonly List<RenderTexture> deadTextures = new List<RenderTexture>();
        // Per cell the portrait covers: Entad's 160 pixels over a person's two cells. A fixed
        // 160 spread over a big animal's three or four cells drew it visibly softer than the
        // animal beside it.
        private const int PortraitPixelsPerCell = 80;
        private const int MaxPortraitPixels = 320;
        // More than this on screen and the oldest go first: a running pack shouldn't render a
        // portrait each.
        private const int MaxGhosts = 48;

        public static void AddAfterimage(Pawn pawn, Vector3 pos, Vector3 driftPerTick, int lifeTicks, Color color)
        {
            if (!pawn.Spawned || pawn.Map != Find.CurrentMap) return;
            if (ghosts.Count >= MaxGhosts) ghosts.RemoveAt(0);
            ghosts.Add(new Ghost
            {
                pawn = pawn,
                map = pawn.Map,
                pos = pos,
                driftPerTick = driftPerTick,
                rot = pawn.Rotation,
                start = Find.TickManager.TicksGame,
                life = lifeTicks,
                color = color,
            });
        }

        // ---- per frame

        internal static void Draw(Map map)
        {
            if (zaps.Count > 0) DrawZaps(map);
            if (ribbons.Count > 0) DrawRibbons(map);
            if (ghosts.Count > 0) DrawGhosts(map);
        }

        private static void DrawZaps(Map map)
        {
            int now = Find.TickManager.TicksGame;
            float y = AltitudeLayer.MoteOverhead.AltitudeFor();
            for (int i = zaps.Count - 1; i >= 0; i--)
            {
                var z = zaps[i];
                if (now >= z.until || z.map != map) { zaps.RemoveAt(i); continue; }
                DrawArc(z.mat, z.a, z.b, z.width, y);
            }
        }

        private static void DrawRibbons(Map map)
        {
            if (ribbonMat == null) ribbonMat = MaterialPool.MatFrom("Things/Mote/BF_Ribbon", ShaderDatabase.MoteGlow);
            float dt = Find.TickManager.Paused ? 0f : Mathf.Min(Time.deltaTime, 0.1f);
            float y = AltitudeLayer.MoteOverhead.AltitudeFor();
            for (int i = ribbons.Count - 1; i >= 0; i--)
            {
                var r = ribbons[i];
                // One lasts under a second, so one for another map is left over from a map
                // switch and would otherwise replay on return.
                if (r.map != map) { ribbons.RemoveAt(i); continue; }
                r.age += dt;
                Follow(r.fromThing, map, ref r.fromPos);
                Follow(r.toThing, map, ref r.toPos);
                bool alive = false;
                for (int k = 0; k < r.strands.Length; k++)
                    alive |= DrawStrand(r, r.strands[k], y);
                if (!alive) ribbons.RemoveAt(i);
            }
        }

        private static void Follow(Thing thing, Map map, ref Vector3 pos)
        {
            if (thing != null && thing.Spawned && thing.Map == map) pos = thing.DrawPos;
        }

        private static bool DrawStrand(Ribbon r, Strand s, float y)
        {
            var d = r.def;
            float a = r.age - s.delay;
            if (a <= 0f) return true;
            float head = Mathf.Clamp01(a / r.travel);
            float tail = Mathf.Clamp01((a - d.tail * r.travel) / r.travel);
            if (tail >= 1f) return false;
            float span = head - tail;
            if (span <= 0.001f) return true;

            Vector3 from = r.fromPos, to = r.toPos;
            Vector3 line = to - from;
            line.y = 0f;
            float dist = line.magnitude;
            if (dist < 0.01f) return false;
            Vector3 perp = new Vector3(-line.z, 0f, line.x) / dist;
            int steps = Mathf.Clamp(Mathf.CeilToInt(span * dist * 5f), 3, 40);

            Color core = Color.Lerp(d.color, Color.white, d.coreWhiten);
            Vector3 prev = Point(from, line, perp, tail, s, d, r.travel);
            for (int i = 1; i <= steps; i++)
            {
                float t = Mathf.Lerp(tail, head, i / (float)steps);
                Vector3 p = Point(from, line, perp, t, s, d, r.travel);
                // Brightest just behind the head, fading out along the tail.
                float u = (i - 0.5f) / steps;
                float fade = Mathf.Clamp01(u * 2.2f) * Mathf.Clamp01((1f - u) * 10f);
                Segment(prev, p, y, d.width, d.color, fade * 0.75f);
                Segment(prev, p, y + 0.001f, d.coreWidth, core, fade);
                prev = p;
            }
            return true;
        }

        // Pinned at both ends, so every strand leaves and lands on its pawn. The waves shift
        // with the time the head passed each point, so a point's place is fixed once the head
        // has gone by: the tail follows the head's path instead of the whole strand writhing.
        private static Vector3 Point(Vector3 from, Vector3 line, Vector3 perp, float t, Strand s, BastleVisual_Ribbons d, float travel)
        {
            float age = t * travel;
            float envelope = Mathf.Sin(t * Mathf.PI);
            float off = s.amp * envelope * (Mathf.Sin(s.freq * t + s.phase + s.spin * age)
                + d.chaos * Mathf.Sin(s.freq2 * t + s.phase2 + s.spin2 * age));
            return from + line * t + perp * off;
        }

        private static void Segment(Vector3 a, Vector3 b, float y, float width, Color color, float alpha)
        {
            Vector3 dir = b - a;
            float length = Mathf.Sqrt(dir.MagnitudeHorizontalSquared());
            if (length < 0.001f || alpha <= 0.01f) return;
            float deg = -Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            Vector3 mid = (a + b) * 0.5f;
            mid.y = y;
            color.a *= alpha;
            block.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(mid, Quaternion.AngleAxis(deg, Vector3.up), new Vector3(length * 1.04f, 1f, width)), ribbonMat, 0, null, 0, block);
        }

        // Each afterimage is the pawn's portrait (the picture the colonist bar and the card
        // use), drawn as a translucent quad where the pawn was. The portrait is cached per pawn
        // and facing and only re-rendered when its looks change; vanilla drops it a second
        // after the last request, so nothing outlives the effect.
        private static void DrawGhosts(Map map)
        {
            int now = Find.TickManager.TicksGame;
            float y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var g = ghosts[i];
                int age = now - g.start;
                if (age >= g.life || g.pawn.Destroyed || g.map != map) { ghosts.RemoveAt(i); continue; }
                if (!g.pawn.Spawned || g.pawn.IsHiddenFromPlayer()) continue;
                Vector3 pos = g.pos + g.driftPerTick * age;
                IntVec3 cell = pos.ToIntVec3();
                if (!cell.InBounds(map) || cell.Fogged(map)) continue;
                float cells = Cells(g.pawn);
                float pixels = Mathf.Min(MaxPortraitPixels, Mathf.Round(cells * PortraitPixelsPerCell));
                var rt = PortraitsCache.Get(g.pawn, new Vector2(pixels, pixels), g.rot, default, 2f / cells,
                    supersample: false, compensateForUIScale: false, healthStateOverride: PawnHealthState.Mobile);
                if (rt == null) continue;
                if (!ghostMats.TryGetValue(rt, out var mat))
                {
                    PruneGhostMats();
                    mat = new Material(ShaderDatabase.Transparent) { mainTexture = rt };
                    ghostMats[rt] = mat;
                }
                Color c = g.color;
                c.a *= 1f - age / (float)g.life;
                block.SetColor(ShaderPropertyIDs.Color, c);
                pos.y = y;
                float quad = cells * (BastleUtility.PeekBastle(g.pawn)?.DrawScale ?? 1f);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(quad, 1f, quad)), mat, 0, null, 0, block);
            }
        }

        // Vanilla destroys every portrait texture when it clears its cache, and hands out new ones after.
        private static void PruneGhostMats()
        {
            foreach (var pair in ghostMats)
                if (pair.Key == null) deadTextures.Add(pair.Key);
            foreach (var rt in deadTextures)
            {
                Object.Destroy(ghostMats[rt]);
                ghostMats.Remove(rt);
            }
            deadTextures.Clear();
        }

        // The square the portrait covers, in cells: two for a person (vanilla's portrait camera
        // at zoom 1), more for a big animal so it isn't cut off. The portrait draws the creature
        // at its kind's size (the draw size patch skips portraits), so the bastle's own draw
        // size goes on the quad instead, in DrawGhosts.
        private static float Cells(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike) return 2f;
            Vector2 size = pawn.ageTracker?.CurKindLifeStage?.bodyGraphicData?.drawSize ?? Vector2.one;
            return Mathf.Max(2f, Mathf.Max(size.x, size.y) * 1.1f);
        }
    }

    /// <summary>Heal visuals (Regenerating's pulse). Natural, tended and regenerated healing
    /// all end here, several injuries in one tick, so a heal visual wants minIntervalTicks.
    /// Leaves after a static read unless some trait has heal visuals.</summary>
    [HarmonyPatch(typeof(Hediff_Injury), nameof(Hediff_Injury.Heal))]
    public static class Patch_InjuryHeal_Visuals
    {
        public static void Postfix(Hediff_Injury __instance, float amount)
        {
            if (amount <= 0f || !BastleEventVisuals.On || !BastleEventVisuals.AnyHealVisuals) return;
            var pawn = __instance.pawn;
            if (pawn == null || !pawn.Spawned) return;
            // A visual must never cut short the healing that called it.
            try
            {
                var bastle = BastleUtility.PeekBastle(pawn);
                if (bastle != null) BastleEventVisuals.Play(bastle, BastleVisualEvent.Heal, null);
            }
            catch (System.Exception e) { Log.ErrorOnce($"[Bastle Framework] heal visuals failed: {e}", 0x5EEA); }
        }
    }

    /// <summary>Dodge visuals. Vanilla only picks the dodge sound in the branch where the
    /// target dodged.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttack), "SoundDodge")]
    public static class Patch_MeleeDodge_Visuals
    {
        public static void Postfix(Verb_MeleeAttack __instance, Thing target)
        {
            if (!(target is Pawn pawn) || !BastleEventVisuals.On || !BastleEventVisuals.AnyDodgeVisuals) return;
            try
            {
                var bastle = BastleUtility.PeekBastle(pawn);
                if (bastle != null) BastleEventVisuals.Play(bastle, BastleVisualEvent.Dodge, __instance.CasterPawn);
            }
            catch (System.Exception e) { Log.ErrorOnce($"[Bastle Framework] dodge visuals failed: {e}", 0x5EEB); }
        }
    }
}
