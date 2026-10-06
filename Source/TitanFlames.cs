using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// Flickering fire sprites scattered over the creature's body, like vanilla's
    /// burning-pawn fire but tinted, smaller and steady. Positions are fractions of the
    /// body mesh, picked per creature from its id so they don't move between frames,
    /// and they follow the body through movement, facing and animation.
    /// </summary>
    public class TitanFlames
    {
        /// <summary>Folder of flicker frames. Defaults to vanilla's fire.</summary>
        [NoTranslate] public string texFolder = "Things/Special/Fire";

        public int count = 3;

        /// <summary>Sprite size as a fraction of the body mesh height.</summary>
        public FloatRange size = new FloatRange(0.3f, 0.45f);

        /// <summary>Where flames sit on the mesh, as fractions of its width and height
        /// from the centre. Sprites don't fill their mesh, so keep these inside ±0.3.</summary>
        public FloatRange x = new FloatRange(-0.25f, 0.25f);
        public FloatRange z = new FloatRange(-0.05f, 0.15f);

        /// <summary>Facing north or south, bodies are narrower: x is multiplied by this.</summary>
        public float verticalFacingWidth = 0.5f;

        public Color color = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>Ticks per flicker frame. Vanilla fire uses 15.</summary>
        public int ticksPerFrame = 12;

        /// <summary>Layer relative to the body. Positive draws in front.</summary>
        public float layer = 2f;

        [Unsaved] private PawnRenderNodeProperties props;
        [Unsaved] private List<Material> mats;

        internal PawnRenderNodeProperties Props
        {
            get
            {
                if (props == null)
                {
                    props = new PawnRenderNodeProperties_TitanFlames
                    {
                        flames = this,
                        debugLabel = "Titan flames",
                        nodeClass = typeof(PawnRenderNode_TitanFlames),
                        workerClass = typeof(PawnRenderNodeWorker_TitanFlames),
                        parentTagDef = PawnRenderNodeTagDefOf.Body,
                        pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any,
                        rotDrawMode = RotDrawMode.Fresh,
                        useGraphic = false,
                        baseLayer = layer,
                    };
                    props.EnsureInitialized();
                }
                return props;
            }
        }

        /// <summary>Loaded on first draw (main thread), never during def loading.</summary>
        internal List<Material> Materials
        {
            get
            {
                if (mats == null)
                {
                    mats = new List<Material>();
                    foreach (var tex in ContentFinder<Texture2D>.GetAllInFolder(texFolder))
                        mats.Add(MaterialPool.MatFrom(tex, ShaderDatabase.TransparentPostLight, color));
                    if (mats.Count == 0)
                        Log.ErrorOnce($"[Titan Framework] No textures in flame folder '{texFolder}'.", texFolder.GetHashCode());
                }
                return mats;
            }
        }
    }

    public class PawnRenderNodeProperties_TitanFlames : PawnRenderNodeProperties
    {
        public TitanFlames flames;
    }

    public class PawnRenderNode_TitanFlames : PawnRenderNode
    {
        public float meshWidth = 1f, meshHeight = 1f;

        public PawnRenderNode_TitanFlames(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        public TitanFlames Flames => ((PawnRenderNodeProperties_TitanFlames)props).flames;

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            // Only used to know the body's size; nothing is drawn with it.
            if (pawn.RaceProps.Humanlike)
            {
                meshWidth = meshHeight = HumanlikeMeshPoolUtility.HumanlikeBodyWidthForPawn(pawn);
            }
            else
            {
                var data = pawn.ageTracker?.CurKindLifeStage?.bodyGraphicData;
                if (data != null) { meshWidth = data.drawSize.x; meshHeight = data.drawSize.y; }
            }
            return MeshPool.GetMeshSetForSize(meshWidth, meshHeight);
        }
    }

    public class PawnRenderNodeWorker_TitanFlames : PawnRenderNodeWorker
    {
        public override void AppendDrawRequests(PawnRenderNode node, PawnDrawParms parms, List<PawnGraphicDrawRequest> requests)
        {
            // A request with no material is skipped by the tree's draw loop, but PostDraw
            // still runs for it with the node's computed matrix. That is where we draw.
            if (parms.Portrait || !TitanVisualsUtility.IsLiveTitan(parms)) return;
            requests.Add(new PawnGraphicDrawRequest(node));
        }

        public override void PostDraw(PawnRenderNode node, PawnDrawParms parms, Mesh mesh, Matrix4x4 matrix)
        {
            base.PostDraw(node, parms, mesh, matrix);
            if (!(node is PawnRenderNode_TitanFlames n) || n.Flames == null) return;
            if (parms.flags.FlagSet(PawnRenderFlags.Invisible)) return;
            var f = n.Flames;
            var mats = f.Materials;
            if (mats.Count == 0) return;

            Pawn pawn = parms.pawn;
            int id = pawn.thingIDNumber;
            int ticks = Find.TickManager.TicksGame;
            float widthMul = parms.facing.IsHorizontal ? 1f : f.verticalFacingWidth;
            float sign = parms.facing == Rot4.West ? -1f : 1f;

            for (int i = 0; i < f.count; i++)
            {
                // Stable per creature and flame, so positions don't jump between frames.
                int seed = Gen.HashCombineInt(id, i * 7919 + 17);
                float fx = Mathf.Lerp(f.x.min, f.x.max, Rand.ValueSeeded(seed)) * widthMul * sign;
                float fz = Mathf.Lerp(f.z.min, f.z.max, Rand.ValueSeeded(seed + 1));
                float s = Mathf.Lerp(f.size.min, f.size.max, Rand.ValueSeeded(seed + 2)) * n.meshHeight;

                // Each flame flickers on its own clock, with the slight wobble vanilla fire has.
                int frameIndex = (ticks + (seed & 0xFFFF)) / Mathf.Max(1, f.ticksPerFrame);
                var mat = mats[((frameIndex ^ seed) & 0x7fffffff) % mats.Count];
                Vector3 wobble = GenRadial.RadialPattern[frameIndex % GenRadial.RadialPattern.Length].ToVector3()
                                 / GenRadial.MaxRadialPatternRadius * 0.03f;

                // Fire sprites are drawn with their base on the point, so they rise from it.
                Vector3 local = new Vector3(fx * n.meshWidth, 0f, fz * n.meshHeight + s * 0.4f);
                Vector3 pos = matrix.MultiplyPoint3x4(local) + wobble;
                pos.y += 0.002f * (i + 1);

                GenDraw.DrawMeshNowOrLater(MeshPool.plane10,
                    Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1f, s)), mat, parms.DrawNow);
            }
        }
    }
}
