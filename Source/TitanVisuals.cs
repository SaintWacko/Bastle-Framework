using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// A generic body overlay: the creature's own body (and head, for humanlikes)
    /// texture, redrawn with a glow shader in a colour, optionally a little larger
    /// (a halo) and pulsing. Works on any body shape with no extra art.
    /// </summary>
    public class TitanOverlay
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
            var p = new PawnRenderNodeProperties_TitanOverlay
            {
                overlay = this,
                head = parent == PawnRenderNodeTagDefOf.Head,
                debugLabel = "Titan overlay",
                nodeClass = typeof(PawnRenderNode_TitanOverlay),
                workerClass = typeof(PawnRenderNodeWorker_TitanOverlay),
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
    public class PawnRenderNodeProperties_TitanOverlay : PawnRenderNodeProperties
    {
        public TitanOverlay overlay;
        public bool head;
    }

    /// <summary>
    /// Picked up automatically by PawnRenderTree (it instantiates every
    /// DynamicPawnRenderNodeSetup subclass), so no Harmony is needed. Runs when a
    /// pawn's render tree is built; Hediff_Titan dirties the tree when the set of
    /// active overlays changes.
    /// </summary>
    public class DynamicPawnRenderNodeSetup_Titan : DynamicPawnRenderNodeSetup
    {
        public override bool HumanlikeOnly => false;

        public override IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> GetDynamicNodes(Pawn pawn, PawnRenderTree tree)
        {
            var titan = TitanUtility.GetTitan(pawn);
            if (titan == null) yield break;
            foreach (var trait in titan.TraitsForReading)
            {
                if (!trait.VisualsActive || trait.def.overlays == null) continue;
                foreach (var overlay in trait.def.overlays)
                {
                    yield return (new PawnRenderNode_TitanOverlay(pawn, overlay.PropsFor(false), tree), null);
                    if (pawn.RaceProps.Humanlike && overlay.includeHead && pawn.health.hediffSet.HasHead)
                        yield return (new PawnRenderNode_TitanOverlay(pawn, overlay.PropsFor(true), tree), null);
                }
            }
        }
    }

    public class PawnRenderNode_TitanOverlay : PawnRenderNode
    {
        public PawnRenderNode_TitanOverlay(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        private PawnRenderNodeProperties_TitanOverlay TProps => (PawnRenderNodeProperties_TitanOverlay)props;
        public TitanOverlay Overlay => TProps.overlay;
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

    public class PawnRenderNodeWorker_TitanOverlay : PawnRenderNodeWorker
    {
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            var offset = base.OffsetFor(node, parms, out pivot);
            if (node is PawnRenderNode_TitanOverlay t && t.Overlay != null && t.Overlay.alignBottom
                && t.Overlay.scale > 1f && !t.TProps_IsHead)
                offset.z += (t.Overlay.scale - 1f) * t.meshHeight * 0.5f;
            return offset;
        }

        public override MaterialPropertyBlock GetMaterialPropertyBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
        {
            var block = base.GetMaterialPropertyBlock(node, material, parms);
            var overlay = (node as PawnRenderNode_TitanOverlay)?.Overlay;
            if (block == null || overlay == null || overlay.pulseSpeed <= 0f || parms.Statue) return block;

            // Per-pawn phase so a pack of flaming wolves doesn't pulse in lockstep.
            float phase = (parms.pawn.thingIDNumber % 97) * 0.37f;
            // Game time, so the pulse freezes with the game when paused.
            float seconds = Find.TickManager.TicksGame / 60f;
            float wave = (Mathf.Sin((seconds * overlay.pulseSpeed + phase) * Mathf.PI * 2f) + 1f) * 0.5f;
            Color c = parms.tint * material.color;
            c.a *= Mathf.Lerp(overlay.pulseMin, 1f, wave);
            block.SetColor(ShaderPropertyIDs.Color, c);
            return block;
        }
    }

    /// <summary>
    /// Ticks titans' effecters every tick (hediff ticks are batched in 1.6, which
    /// would make particles stutter). Only titans with active effecters on this map
    /// are tracked, so a map with none costs one empty loop.
    /// </summary>
    public class MapComponent_TitanVisuals : MapComponent
    {
        private readonly List<Hediff_Titan> tracked = new List<Hediff_Titan>();

        public MapComponent_TitanVisuals(Map map) : base(map) { }

        public void Track(Hediff_Titan titan)
        {
            if (!tracked.Contains(titan)) tracked.Add(titan);
        }

        public override void MapComponentTick()
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var t = tracked[i];
                var p = t.pawn;
                if (p == null || !p.Spawned || p.Map != map || !t.HasActiveEffecters
                    || !p.health.hediffSet.hediffs.Contains(t))
                {
                    t.CleanupEffecters();
                    tracked.RemoveAt(i);
                    continue;
                }
                // Nobody can see it: let the particles stop rather than spawn unseen.
                if (map != Find.CurrentMap || p.Position.Fogged(map)) continue;
                t.EffectersTick();
            }
        }
    }
}
