using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// A generic body overlay: the creature's own body (and head, for humanlikes)
    /// texture, redrawn with a glow shader in a colour, optionally a little larger
    /// (a halo) and pulsing. Works on any body shape with no extra art.
    /// </summary>
    public class BastleOverlay
    {
        /// <summary>Tint. Alpha is the overall strength.</summary>
        public Color color = new Color(1f, 0.55f, 0.15f, 0.6f);

        /// <summary>Size relative to the body. Above 1 makes a halo around the silhouette.</summary>
        public float scale = 1f;

        /// <summary>Draw layer relative to the body. Negative draws behind it (a rim glow),
        /// positive draws over it (a tint).</summary>
        public float layer = 1f;

        /// <summary>When scale is above 1, shift the overlay up so its bottom edge stays
        /// on the creature's feet and the extra size shows above and to the sides.</summary>
        public bool alignBottom = true;

        /// <summary>Where the creature's feet sit below the mesh centre, as a fraction of
        /// mesh height. Sprites don't fill their mesh, so this is less than 0.5.</summary>
        public float feetDepth = 0.25f;

        /// <summary>Pulses per second. 0 = steady.</summary>
        public float pulseSpeed;

        /// <summary>Strength at the bottom of a pulse, as a fraction of color's alpha.</summary>
        public float pulseMin = 0.6f;

        /// <summary>Humanlikes only: also overlay the head.</summary>
        public bool includeHead = true;

        /// <summary>Defaults to MoteGlow (additive). Transparent gives a flat tint.</summary>
        public ShaderTypeDef shaderType;

        [Unsaved] private PawnRenderNodeProperties bodyProps;
        [Unsaved] private PawnRenderNodeProperties headProps;

        public Shader Shader => shaderType?.Shader ?? ShaderDatabase.MoteGlow;

        internal PawnRenderNodeProperties PropsFor(bool head)
        {
            if (head) return headProps ?? (headProps = MakeProps(PawnRenderNodeTagDefOf.Head));
            return bodyProps ?? (bodyProps = MakeProps(PawnRenderNodeTagDefOf.Body));
        }

        private PawnRenderNodeProperties MakeProps(PawnRenderNodeTagDef parent)
        {
            var p = new PawnRenderNodeProperties_BastleOverlay
            {
                overlay = this,
                head = parent == PawnRenderNodeTagDefOf.Head,
                debugLabel = "Bastle overlay",
                nodeClass = typeof(PawnRenderNode_BastleOverlay),
                workerClass = typeof(PawnRenderNodeWorker_BastleOverlay),
                parentTagDef = parent,
                pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any,
                rotDrawMode = RotDrawMode.Fresh,
                // LayerFor reads baseLayer as an absolute value, so offset from the
                // vanilla layer of the node it decorates (body 0, head 50).
                baseLayer = (parent == PawnRenderNodeTagDefOf.Head ? 50f : 0f) + layer,
                drawSize = new Vector2(scale, scale),
            };
            p.EnsureInitialized();
            return p;
        }
    }

    /// <summary>The node constructor reads its mesh before any field initialiser runs,
    /// so the overlay travels on the props instead of the node.</summary>
    public class PawnRenderNodeProperties_BastleOverlay : PawnRenderNodeProperties
    {
        public BastleOverlay overlay;
        public bool head;
    }

    /// <summary>
    /// Picked up automatically by PawnRenderTree (it instantiates every
    /// DynamicPawnRenderNodeSetup subclass), so no Harmony is needed. Runs when a
    /// pawn's render tree is built; Hediff_Bastle dirties the tree when the set of
    /// active overlays changes.
    /// </summary>
    public class DynamicPawnRenderNodeSetup_Bastle : DynamicPawnRenderNodeSetup
    {
        public override bool HumanlikeOnly => false;

        public override IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> GetDynamicNodes(Pawn pawn, PawnRenderTree tree)
        {
            var bastle = BastleUtility.GetBastle(pawn);
            if (bastle == null) yield break;
            // The statue flag lives on the renderer, which isn't saved: set it each time
            // the tree is built, before any node picks its graphic.
            if (bastle.IsConstruct) bastle.ApplyStatue(dirty: false);
            foreach (var trait in bastle.TraitsForReading)
            {
                if (!trait.VisualsActive) continue;
                if (trait.Flames != null)
                    foreach (var flames in trait.Flames)
                        yield return (new PawnRenderNode_BastleFlames(pawn, flames.Props, tree), null);
                if (trait.Overlays == null) continue;
                foreach (var overlay in trait.Overlays)
                {
                    yield return (new PawnRenderNode_BastleOverlay(pawn, overlay.PropsFor(false), tree), null);
                    if (pawn.RaceProps.Humanlike && overlay.includeHead && pawn.health.hediffSet.HasHead)
                        yield return (new PawnRenderNode_BastleOverlay(pawn, overlay.PropsFor(true), tree), null);
                }
            }
        }
    }

    public class PawnRenderNode_BastleOverlay : PawnRenderNode
    {
        public PawnRenderNode_BastleOverlay(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        private PawnRenderNodeProperties_BastleOverlay TProps => (PawnRenderNodeProperties_BastleOverlay)props;
        public BastleOverlay Overlay => TProps.overlay;
        public bool TProps_IsHead => TProps.head;

        /// <summary>The texture the overlay copies: the same one the body or head node
        /// draws. Read from the pawn rather than the body node, whose graphic may not
        /// be initialised yet when this node is.</summary>
        private static Graphic SourceGraphic(Pawn pawn, bool head)
        {
            if (pawn.RaceProps.Humanlike)
            {
                if (head) return pawn.story?.headType?.GetGraphic(pawn, Color.white);
                string path = pawn.story?.bodyType?.bodyNakedGraphicPath;
                return path == null ? null : GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout);
            }
            var stage = pawn.ageTracker?.CurKindLifeStage;
            if (stage == null) return null;
            // Mirrors PawnRenderNode_AnimalPart.GraphicFor so the glow matches the body.
            if (stage.bodyGraphicData != null && pawn.TryGetAlternate(out var ag, out _))
                return ag.GetGraphic(stage.bodyGraphicData.Graphic);
            var data = pawn.gender == Gender.Female && stage.femaleGraphicData != null ? stage.femaleGraphicData : stage.bodyGraphicData;
            return data?.Graphic;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            var source = SourceGraphic(pawn, TProps.head);
            if (source == null || Overlay == null) return null;
            return source.GetColoredVersion(Overlay.Shader, Overlay.color, Overlay.color);
        }

        /// <summary>
        /// Recoloured copies of the body's state graphics (flying frames, swimming,
        /// stationary), keyed by the same states. Built on the main thread when the tree
        /// initialises, after the body node (the parent initialises its children last),
        /// so the parallel pre-draw only reads them.
        /// </summary>
        protected override IEnumerable<(GraphicStateDef state, Graphic graphic)> StateGraphicsFor(Pawn pawn)
        {
            var body = parent;
            if (body == null || TProps.head || Overlay == null || pawn.RaceProps.Humanlike) yield break;
            var anim = pawn.Drawer.renderer.CurAnimation;
            if (anim != null)
                foreach (var (state, graphic) in anim.GetGraphicStates(body))
                {
                    var g = Recolor(body.ContainsGraphicState(state) ? body.GraphicForState(state) : graphic);
                    if (g != null) yield return (state, g);
                }
            foreach (var state in new[] { GraphicStateDefOf.Swimming, GraphicStateDefOf.Stationary })
            {
                if (state == null || !body.ContainsGraphicState(state)) continue;
                var g = Recolor(body.GraphicForState(state));
                if (g != null) yield return (state, g);
            }
        }

        private Graphic Recolor(Graphic source) =>
            source?.GetColoredVersion(Overlay.Shader, Overlay.color, Overlay.color);

        /// <summary>Height of the mesh in cells, used to keep a scaled overlay's bottom
        /// on the creature's feet. Set from MeshSetFor, which the base constructor calls.</summary>
        public float meshHeight;

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike)
            {
                var set = TProps.head ? HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn)
                                      : HumanlikeMeshPoolUtility.GetHumanlikeBodySetForPawn(pawn);
                meshHeight = TProps.head ? 1.5f : HumanlikeMeshPoolUtility.HumanlikeBodyWidthForPawn(pawn);
                return set;
            }
            var g = SourceGraphic(pawn, false);
            if (g == null) return null;
            meshHeight = g.drawSize.y;
            return MeshPool.GetMeshSetForSize(g.drawSize.x, g.drawSize.y);
        }
    }

    public class PawnRenderNodeWorker_BastleOverlay : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            base.CanDrawNow(node, parms) && BastleVisualsUtility.IsLiveBastle(parms);

        /// <summary>
        /// An animal's body node swaps its texture for graphic states: the frames of the
        /// flying animation (Odyssey birds), and the swimming and stationary sprites. The
        /// overlay copies whatever the body is showing, recoloured, so the glow follows the
        /// wings instead of drawing the standing sprite over a bird in flight. Position and
        /// size need no work: the overlay is the body's child and inherits its transform.
        /// </summary>
        // Deliberately GetGraphic and not GetGraphicState: base ScaleFor also reads
        // GetGraphicState and multiplies by the state's drawSize, which the overlay already
        // inherits from the body, so a flying overlay would be scaled twice.
        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            if (node is PawnRenderNode_BastleOverlay o && !o.TProps_IsHead && o.Overlay != null
                && !parms.pawn.RaceProps.Humanlike && o.parent != null)
            {
                var state = BodyGraphicState(o.parent, parms);
                if (state != null && o.ContainsGraphicState(state))
                    return o.GraphicForState(state);
            }
            return base.GetGraphic(node, parms);
        }

        /// <summary>Mirrors PawnRenderNodeWorker_AnimalBody.GetGraphicState, which is
        /// protected on the body's worker.</summary>
        private static GraphicStateDef BodyGraphicState(PawnRenderNode body, PawnDrawParms parms)
        {
            Pawn pawn = parms.pawn;
            if (body.tree.currentAnimation == null)
            {
                if (pawn.DrawNonHumanlikeSwimmingGraphic) return GraphicStateDefOf.Swimming;
                if (pawn.DrawNonHumanlikeStationaryGraphic) return GraphicStateDefOf.Stationary;
            }
            if (!parms.flags.FlagSet(PawnRenderFlags.Portrait) && body.TryGetAnimationGraphicState(parms, out var state))
                return state;
            return null;
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            var offset = base.OffsetFor(node, parms, out pivot);
            if (node is PawnRenderNode_BastleOverlay t && t.Overlay != null && t.Overlay.alignBottom
                && t.Overlay.scale > 1f && !t.TProps_IsHead)
                offset.z += (t.Overlay.scale - 1f) * t.meshHeight * t.Overlay.feetDepth;
            return offset;
        }

        public override MaterialPropertyBlock GetMaterialPropertyBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
        {
            var block = base.GetMaterialPropertyBlock(node, material, parms);
            var overlay = (node as PawnRenderNode_BastleOverlay)?.Overlay;
            if (block == null || overlay == null) return block;
            // A vanilla statue (CompStatue) shows no glow. A living material bastle is also
            // drawn as a statue, but its glow keeps its own colour rather than the stuff's,
            // which base just wrote into the block.
            Color c = (parms.Statue ? Color.white : parms.tint) * material.color;
            if (overlay.pulseSpeed > 0f)
            {
                // Per-pawn phase so a pack of flaming wolves doesn't pulse in lockstep.
                float phase = (parms.pawn.thingIDNumber % 97) * 0.37f;
                // Game time, so the pulse freezes with the game when paused.
                float seconds = Find.TickManager.TicksGame / 60f;
                float wave = (Mathf.Sin((seconds * overlay.pulseSpeed + phase) * Mathf.PI * 2f) + 1f) * 0.5f;
                c.a *= Mathf.Lerp(overlay.pulseMin, 1f, wave);
            }
            block.SetColor(ShaderPropertyIDs.Color, c);
            return block;
        }
    }

    internal static class BastleVisualsUtility
    {
        /// <summary>Statue rendering is also how vanilla draws sculptures of pawns. Those use
        /// an unspawned stand-in pawn with no bastle material; a material bastle is drawn
        /// with the statue flag too and should keep its trait visuals. A corpse is drawn
        /// through the dead pawn's render tree, so dead pawns are left out too: a pile of
        /// bodies shouldn't glow or burn.</summary>
        public static bool IsLiveBastle(PawnDrawParms parms) =>
            !parms.pawn.Dead && (!parms.Statue || BastleUtility.PeekBastle(parms.pawn)?.IsConstruct == true);
    }

    /// <summary>
    /// Ticks bastles' effecters and lights every tick (hediff ticks are batched in 1.6,
    /// which would make particles stutter and lights lag). Only bastles with effecters
    /// or a light on this map are tracked, so a map with none costs one empty loop.
    /// </summary>
    public class MapComponent_BastleVisuals : MapComponent
    {
        private readonly List<Hediff_Bastle> tracked = new List<Hediff_Bastle>();

        public MapComponent_BastleVisuals(Map map) : base(map) { }

        public void Track(Hediff_Bastle bastle)
        {
            if (!tracked.Contains(bastle)) tracked.Add(bastle);
        }

        /// <summary>Aura rings for selected bastles. Main thread, per frame.</summary>
        public override void MapComponentUpdate()
        {
            if (map != Find.CurrentMap || !WorldRendererUtility.DrawingMap) return;
            var sel = Find.Selector.SelectedObjectsListForReading;
            for (int i = 0; i < sel.Count; i++)
            {
                if (!(sel[i] is Pawn p) || !p.Spawned || p.Map != map) continue;
                var traits = BastleUtility.PeekBastle(p)?.Traits;
                if (traits == null) continue;
                for (int j = 0; j < traits.Count; j++)
                    (traits[j] as BastleTrait_Aura)?.DrawRing();
            }
        }

        public override void MapComponentTick()
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var t = tracked[i];
                var p = t.pawn;
                if (p == null || !p.Spawned || p.Map != map || !t.HasMapVisuals
                    || !p.health.hediffSet.hediffs.Contains(t))
                {
                    // A creature that moved to another map is already tracked there; leave
                    // its effecters and light to that map's component.
                    if (t.TrackedMap == map || t.TrackedMap == null) t.CleanupEffecters();
                    tracked.RemoveAt(i);
                    continue;
                }
                t.GlowTick();
                // Nobody can see it: skip spawning particles nobody would see.
                if (map != Find.CurrentMap || p.Position.Fogged(map)) continue;
                t.EffectersTick();
            }
        }
    }
}
