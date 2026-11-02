using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// A moving effect masked to the creature's own silhouette and drawn on the body's
    /// mesh, over its texture: flames crawling up a burning coat (BastleSurface_Flow), a
    /// sheen sweeping across polished scales (BastleSurface_Sheen), glints
    /// (BastleSurface_Glints). Frames are made at runtime from the alpha of whatever
    /// texture the body shows (BastleShape), so they need no art and fit modded animals,
    /// flying frames and swimming sprites included. Ported from Entad Framework's
    /// weapon flow, sheen and glints.
    /// </summary>
    public abstract class BastleSurface
    {
        /// <summary>Layer relative to the body (or head). Overlays sit at 1 by default.</summary>
        public float layer = 1.5f;

        /// <summary>Humanlikes only: also draw it on the head.</summary>
        public bool includeHead = true;

        /// <summary>Show before the trait is discovered (null: the trait's rule, then the setting).</summary>
        public bool? beforeIdentified;

        [Unsaved] private PawnRenderNodeProperties bodyProps;
        [Unsaved] private PawnRenderNodeProperties headProps;

        internal PawnRenderNodeProperties PropsFor(bool head)
        {
            if (head) return headProps ?? (headProps = MakeProps(PawnRenderNodeTagDefOf.Head));
            return bodyProps ?? (bodyProps = MakeProps(PawnRenderNodeTagDefOf.Body));
        }

        private PawnRenderNodeProperties MakeProps(PawnRenderNodeTagDef parent)
        {
            var p = new PawnRenderNodeProperties_BastleSurface
            {
                surface = this,
                head = parent == PawnRenderNodeTagDefOf.Head,
                debugLabel = "Bastle surface",
                nodeClass = typeof(PawnRenderNode_BastleSurface),
                workerClass = typeof(PawnRenderNodeWorker_BastleSurface),
                parentTagDef = parent,
                pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any,
                rotDrawMode = RotDrawMode.Fresh,
                baseLayer = (parent == PawnRenderNodeTagDefOf.Head ? 50f : 0f) + layer,
            };
            p.EnsureInitialized();
            return p;
        }

        public abstract void Draw(BastleSurfaceDraw d);

        public virtual IEnumerable<string> ConfigErrors() { yield break; }
    }

    /// <summary>One surface's draw this frame: the body's mesh and matrix as the render
    /// tree computed them, and the shape of the texture the body is showing.</summary>
    [StaticConstructorOnStartup]
    public class BastleSurfaceDraw
    {
        public BastleShape shape;
        public Mesh mesh;
        public Matrix4x4 matrix;
        /// <summary>The mesh's size in cells, whether its UVs are mirrored (facing west),
        /// and its back lift (vanilla raises a pawn mesh's top edge so heads sort over bodies).</summary>
        public Vector2 size;
        public bool flipped;
        public float lift;
        /// <summary>The matrix's own scale (drawSizeFactor, life stage), on top of the mesh size.</summary>
        public float scale;
        public int seed;
        public int ticks;

        internal static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public void DrawFrame(Material mat, float alpha)
        {
            if (alpha <= 0f) return;
            block.SetColor(ShaderPropertyIDs.Color, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            Graphics.DrawMesh(mesh, matrix, mat, 0, null, 0, block);
        }

        /// <summary>
        /// A spot on the body's opaque pixels in world space, on the lifted mesh. Vanilla's
        /// lifted quad (MeshMakerPlanes.NewPlaneMesh) is two triangles split on the
        /// bottom-left to top-right diagonal: the top edge at the full lift, bottom left at
        /// 0, bottom right at 0.4 of it.
        /// </summary>
        public Vector3 Point(int index)
        {
            Vector2 p = shape.points[(index & 0x7FFFFFFF) % shape.points.Length];
            float x = flipped ? -p.x : p.x;
            float y = p.y;
            float up = lift <= 0f ? 0f : y >= x ? lift * (y + 0.5f) : lift * (0.4f * x + 0.6f * y + 0.5f);
            return matrix.MultiplyPoint3x4(new Vector3(x * size.x, up, y * size.y));
        }
    }

    /// <summary>
    /// What a body texture looks like, read back from the GPU once per texture. Textures
    /// the game loads aren't readable from the CPU, so the texture is blitted into a small
    /// render texture and read from there.
    /// </summary>
    public class BastleShape
    {
        // Entad uses 48 for items; creatures cover more of the screen, and 64 keeps the
        // edge of the mask from bleeding a soft rim past a large animal's outline.
        public const int Size = 64;
        private const int MaxPoints = 64;

        /// <summary>The texture's alpha, Size x Size from the bottom row up.</summary>
        public float[] mask;
        /// <summary>Spots on its opaque pixels, as fractions of the texture from its centre.</summary>
        public Vector2[] points;

        private static readonly Dictionary<Texture, BastleShape> cache = new Dictionary<Texture, BastleShape>();

        public static BastleShape For(Texture tex)
        {
            if (tex == null) return null;
            if (cache.TryGetValue(tex, out var shape)) return shape;
            try { shape = Read(tex); }
            catch (System.Exception e)
            {
                Log.ErrorOnce($"[Bastle Framework] couldn't read the shape of texture {tex.name}: {e}", tex.GetInstanceID());
                shape = null;
            }
            cache[tex] = shape;
            return shape;
        }

        private static BastleShape Read(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D read = null;
            Color32[] pixels;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(Size, Size, TextureFormat.ARGB32, false);
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                pixels = read.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) Object.Destroy(read);
            }

            var mask = new float[Size * Size];
            var spots = new List<Vector2>();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float a = pixels[y * Size + x].a / 255f;
                    mask[y * Size + x] = a;
                    if (a > 0.5f) spots.Add(new Vector2((x + 0.5f) / Size - 0.5f, (y + 0.5f) / Size - 0.5f));
                }
            if (spots.Count == 0) spots.Add(Vector2.zero);
            // An even spread over the body rather than its first rows, then shuffled so
            // consecutive indices land far apart.
            var chosen = new Vector2[Mathf.Min(MaxPoints, spots.Count)];
            for (int i = 0; i < chosen.Length; i++) chosen[i] = spots[(int)((long)i * spots.Count / chosen.Length)];
            for (int i = chosen.Length - 1; i > 0; i--)
            {
                int j = (i * 7919 + 13) % (i + 1);
                (chosen[i], chosen[j]) = (chosen[j], chosen[i]);
            }
            return new BastleShape { mask = mask, points = chosen };
        }

        private static readonly Dictionary<Mesh, (Vector2 size, bool flipped, float lift)> meshInfo = new Dictionary<Mesh, (Vector2, bool, float)>();

        /// <summary>Size, mirroring and back lift of a pawn mesh, read once per mesh. The
        /// tree hands the west-facing side a mesh with its UVs mirrored.</summary>
        internal static (Vector2 size, bool flipped, float lift) MeshInfo(Mesh mesh)
        {
            if (meshInfo.TryGetValue(mesh, out var info)) return info;
            Vector3 b = mesh.bounds.size;
            bool flipped = false;
            if (mesh.isReadable)
            {
                var verts = mesh.vertices;
                var uvs = mesh.uv;
                if (verts.Length > 0 && uvs.Length == verts.Length) flipped = verts[0].x < 0f != uvs[0].x < 0.5f;
            }
            // The bounds' height is the back lift: a flat mesh has none.
            info = (new Vector2(b.x, b.z), flipped, b.y);
            meshInfo[mesh] = info;
            return info;
        }

        internal static Material MakeFrame(Color32[] colors, string name, Shader shader)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.ARGB32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(colors);
            tex.Apply(false, true);
            return new Material(shader) { mainTexture = tex, name = name };
        }

        internal static int Mix(int h)
        {
            uint x = (uint)h;
            x ^= x >> 16;
            x *= 0x85EBCA6B;
            x ^= x >> 13;
            x *= 0xC2B2AE35;
            x ^= x >> 16;
            return (int)x;
        }
    }

    /// <summary>
    /// Soft patches of colour crawling up the creature, masked to its shape: flames over a
    /// burning coat, or frost or smoke in other colours. A looping set of frames is made
    /// once per texture and the two either side of the moment are cross-faded. The pattern
    /// is 3D value noise, scrolled up a whole number of wraps per loop and moved through
    /// its third axis one wrap per loop, so the patches change shape as they rise and the
    /// loop has no seam. Entad's first version only scrolled 2D noise, and the same lick
    /// climbed one side over and over.
    /// </summary>
    public class BastleSurface_Flow : BastleSurface
    {
        public Color color = new Color(1f, 0.45f, 0.08f, 0.5f);
        /// <summary>The brightest part of each patch blends towards this (fire's yellow core); alpha unused.</summary>
        public Color coreColor = new Color(1f, 0.85f, 0.4f, 1f);
        /// <summary>Noise above threshold shows, and from coreThreshold up it turns to the core colour.</summary>
        public float threshold = 0.5f;
        public float coreThreshold = 0.78f;
        /// <summary>Noise cells across and up the texture: more across than up gives taller
        /// patches, like flames. Most sprites fill only the middle of their texture.</summary>
        public int cellsAcross = 7;
        public int cellsUp = 4;
        /// <summary>Cells the shapes change through per loop; more changes faster.</summary>
        public int evolveCells = 3;
        /// <summary>Ticks per loop, how many times the pattern rises the texture's height in
        /// it, and frames in the loop. Keep the rise per frame to about a sixteenth of the
        /// texture or less (frames at least 16 x scrolls), or the cross-fade shows double images.</summary>
        public int loopTicks = 360;
        public int scrolls = 2;
        public int frames = 32;
        public float pulseSpeed;
        public float pulseMin = 1f;
        /// <summary>Added light (fire, sparks), or blended colour (frost, smoke, anything that
        /// has to show on a white coat).</summary>
        public bool additive = true;

        private readonly Dictionary<BastleShape, Material[]> frameCache = new Dictionary<BastleShape, Material[]>();

        public override IEnumerable<string> ConfigErrors()
        {
            if (frames < 2 || frames > 64) yield return "surface flow frames must be from 2 to 64";
            if (loopTicks < 1) yield return "surface flow loopTicks must be at least 1";
            if (cellsAcross < 1 || cellsUp < 1 || evolveCells < 1) yield return "surface flow cellsAcross, cellsUp and evolveCells must be at least 1";
            if (scrolls < 0) yield return "surface flow scrolls must not be negative";
            if (threshold < 0f || threshold >= 1f) yield return "surface flow threshold must be from 0 to below 1";
            if (coreThreshold > 1f) yield return "surface flow coreThreshold must be at most 1 (1 = no core)";
        }

        public override void Draw(BastleSurfaceDraw d)
        {
            var mats = FramesFor(d.shape);
            if (mats == null) return;
            int seed = Gen.HashCombineInt(d.seed, 0xF10);
            float at = (d.ticks + ((seed >> 4) & 0xFFFF)) % loopTicks / (float)loopTicks * mats.Length;
            int a = (int)at % mats.Length;
            int b = (a + 1) % mats.Length;
            float f = at - (int)at;
            float strength = color.a;
            if (pulseSpeed > 0f)
            {
                // Wrapped in double: a float of the raw tick count gets too coarse years into a game.
                float cycle = (float)((d.ticks / 60.0 * pulseSpeed + (seed & 0xFF) / 255f) % 1.0);
                strength *= Mathf.Lerp(pulseMin, 1f, (Mathf.Sin(cycle * Mathf.PI * 2f) + 1f) * 0.5f);
            }
            d.DrawFrame(mats[a], strength * (1f - f));
            // Added light sums to full strength; two blended layers cover less than their
            // sum, so the second is raised to keep the blend at full strength between frames.
            d.DrawFrame(mats[b], additive ? strength * f : strength * f / Mathf.Max(0.0001f, 1f - strength * (1f - f)));
        }

        private Material[] FramesFor(BastleShape shape)
        {
            if (frameCache.TryGetValue(shape, out var mats)) return mats;
            // Cached before building, so a build that throws fails once rather than every frame.
            frameCache[shape] = null;
            if (shape.mask != null && frames >= 2)
            {
                const int n = BastleShape.Size;
                float[] mask = shape.mask;
                mats = new Material[frames];
                var colors = new Color32[n * n];
                Shader shader = additive ? ShaderDatabase.MoteGlow : ShaderDatabase.Transparent;
                for (int k = 0; k < frames; k++)
                {
                    float t = k / (float)frames;
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float alpha = mask[y * n + x];
                            if (alpha <= 0f) { colors[y * n + x] = default; continue; }
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                            float rise = v - t * scrolls;
                            // The fine octave's time is offset half a cell: smoothstep stops at
                            // whole cells, and the two octaves shouldn't pause on the same beat.
                            float value = 0.65f * Noise(u * cellsAcross, rise * cellsUp, t * evolveCells, cellsAcross, cellsUp, evolveCells, 17)
                                + 0.35f * Noise(u * cellsAcross * 2, rise * cellsUp * 2, t * evolveCells * 2 + 0.5f, cellsAcross * 2, cellsUp * 2, evolveCells * 2, 91);
                            float show = Mathf.Clamp01((value - threshold) / (1f - threshold));
                            show = show * show * (3f - 2f * show);
                            float core = coreThreshold < 1f ? Mathf.Clamp01((value - coreThreshold) / (1f - coreThreshold)) : 0f;
                            Color c = Color.Lerp(color, coreColor, core);
                            c.a = show * alpha;
                            colors[y * n + x] = c;
                        }
                    mats[k] = BastleShape.MakeFrame(colors, "BastleFlow_" + k, shader);
                }
            }
            frameCache[shape] = mats;
            return mats;
        }

        /// <summary>Smooth value noise on a grid that wraps every wrapX by wrapY by wrapZ cells.</summary>
        private static float Noise(float x, float y, float z, int wrapX, int wrapY, int wrapZ, int salt)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float fx = Smooth(x - x0), fy = Smooth(y - y0), fz = Smooth(z - z0);
            float Layer(int zc) => Mathf.Lerp(
                Mathf.Lerp(Cell(x0, y0, zc, wrapX, wrapY, wrapZ, salt), Cell(x0 + 1, y0, zc, wrapX, wrapY, wrapZ, salt), fx),
                Mathf.Lerp(Cell(x0, y0 + 1, zc, wrapX, wrapY, wrapZ, salt), Cell(x0 + 1, y0 + 1, zc, wrapX, wrapY, wrapZ, salt), fx), fy);
            return Mathf.Lerp(Layer(z0), Layer(z0 + 1), fz);
        }

        private static float Smooth(float f) => f * f * (3f - 2f * f);

        private static float Cell(int x, int y, int z, int wrapX, int wrapY, int wrapZ, int salt)
        {
            x = (x % wrapX + wrapX) % wrapX;
            y = (y % wrapY + wrapY) % wrapY;
            z = (z % wrapZ + wrapZ) % wrapZ;
            return (BastleShape.Mix(Gen.HashCombineInt(Gen.HashCombineInt(x * 7349 + salt, y * 3517), z * 9241)) & 0xFFFF) / 65535f;
        }
    }

    /// <summary>
    /// A bright band sweeping across the creature every few seconds, for a polished
    /// surface. Frames of the band masked to the body's shape are made once per texture,
    /// and the two either side of the band's position are cross-faded. Runs on game
    /// ticks, so it freezes while paused. Additive, so it barely shows on a white coat.
    /// </summary>
    public class BastleSurface_Sheen : BastleSurface
    {
        public Color color = new Color(1f, 1f, 1f, 0.55f);
        /// <summary>One sweep per period (picked per creature from this range), lasting sweepTicks.</summary>
        public IntRange periodTicks = new IntRange(240, 420);
        public int sweepTicks = 45;
        /// <summary>Width of the band as a fraction of the texture, and its slant from vertical in degrees.</summary>
        public float width = 0.16f;
        public float angle = 30f;
        public int frames = 16;

        private readonly Dictionary<BastleShape, Material[]> frameCache = new Dictionary<BastleShape, Material[]>();

        public BastleSurface_Sheen() { layer = 2f; }

        public override IEnumerable<string> ConfigErrors()
        {
            if (sweepTicks < 1) yield return "surface sheen sweepTicks must be at least 1";
            if (periodTicks.min <= sweepTicks) yield return "surface sheen periodTicks must be longer than sweepTicks";
            if (frames < 2 || frames > 64) yield return "surface sheen frames must be from 2 to 64";
            if (width <= 0f) yield return "surface sheen width must be above 0";
        }

        public override void Draw(BastleSurfaceDraw d)
        {
            int seed = Gen.HashCombineInt(d.seed, 0x5EE7);
            int period = Mathf.Max(1, periodTicks.min + (seed & 0x7FFFFFFF) % Mathf.Max(1, periodTicks.max - periodTicks.min + 1));
            int phase = (d.ticks + ((seed >> 8) & 0xFFFF)) % period;
            if (phase >= sweepTicks) return;
            var mats = FramesFor(d.shape);
            if (mats == null) return;
            float at = phase / (float)sweepTicks * (mats.Length - 1);
            int a = Mathf.Min((int)at, mats.Length - 2);
            float f = at - a;
            d.DrawFrame(mats[a], color.a * (1f - f));
            d.DrawFrame(mats[a + 1], color.a * f);
        }

        private Material[] FramesFor(BastleShape shape)
        {
            if (frameCache.TryGetValue(shape, out var mats)) return mats;
            frameCache[shape] = null;
            if (shape.mask != null && frames >= 2)
            {
                const int n = BastleShape.Size;
                float[] mask = shape.mask;
                float rad = angle * Mathf.Deg2Rad;
                // Across the band: x and z of the texture's square, turned by the slant.
                float ax = Mathf.Cos(rad), az = -Mathf.Sin(rad);
                float reach = 0.5f * (Mathf.Abs(ax) + Mathf.Abs(az)) + width;
                mats = new Material[frames];
                var colors = new Color32[n * n];
                Color32 tint = color;
                for (int k = 0; k < frames; k++)
                {
                    float centre = Mathf.Lerp(-reach, reach, k / (float)(frames - 1));
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f;
                            float dist = Mathf.Abs(u * ax + v * az - centre) / width;
                            // A soft band with a brighter core.
                            float band = dist >= 1f ? 0f : (1f - dist) * (1f - dist) * 0.7f + (dist < 0.3f ? 0.3f * (1f - dist / 0.3f) : 0f);
                            colors[y * n + x] = new Color32(tint.r, tint.g, tint.b, (byte)(Mathf.Clamp01(band * mask[y * n + x]) * 255f));
                        }
                    mats[k] = BastleShape.MakeFrame(colors, "BastleSheen_" + k, ShaderDatabase.MoteGlow);
                }
            }
            frameCache[shape] = mats;
            return mats;
        }
    }

    /// <summary>
    /// Brief star-shaped twinkles on the body. Each slot shows one glint per period at a
    /// new spot on the body's opaque pixels, sized to the creature. Drawn on the body's
    /// lifted mesh rather than as flecks: flecks sit over everything, so a glint on a
    /// humanlike's shoulder would show over its head.
    /// </summary>
    public class BastleSurface_Glints : BastleSurface
    {
        [NoTranslate] public string texPath = "Things/Mote/BF_Glint";
        public Color color = new Color(0.92f, 0.96f, 1f, 0.9f);
        public int count = 2;
        /// <summary>Each slot shows one glint per period, lasting glintTicks.</summary>
        public IntRange periodTicks = new IntRange(90, 200);
        public int glintTicks = 24;
        /// <summary>Size at the brightest, as a fraction of the body mesh's height.</summary>
        public FloatRange size = new FloatRange(0.13f, 0.21f);

        [Unsaved] private Material material;

        public BastleSurface_Glints() { layer = 2.5f; }

        public override IEnumerable<string> ConfigErrors()
        {
            if (texPath.NullOrEmpty()) yield return "surface glints need a texPath";
            if (glintTicks < 1) yield return "surface glints glintTicks must be at least 1";
            if (periodTicks.min <= glintTicks) yield return "surface glints periodTicks must be longer than glintTicks";
        }

        public override void Draw(BastleSurfaceDraw d)
        {
            if (d.shape.points.NullOrEmpty()) return;
            material = material ?? MaterialPool.MatFrom(texPath, ShaderDatabase.MoteGlow, Color.white);
            var block = BastleSurfaceDraw.block;
            for (int i = 0; i < count; i++)
            {
                int seed = Gen.HashCombineInt(d.seed, i * 6151 + 29);
                int period = Mathf.Max(glintTicks + 1, periodTicks.min + (seed & 0x7FFFFFFF) % Mathf.Max(1, periodTicks.max - periodTicks.min + 1));
                int t = d.ticks + ((seed >> 8) & 0xFFFF);
                int phase = t % period;
                if (phase >= glintTicks) continue;
                float u = phase / (float)glintTicks;
                float strength = Mathf.Sin(u * Mathf.PI);
                int spot = BastleShape.Mix(seed ^ (t / period) * 0x2C1B3C6D);
                Vector3 pos = d.Point(spot);
                pos.y += 0.0001f;
                float s = Mathf.Lerp(size.min, size.max, ((spot >> 12) & 0xFF) / 255f) * strength * d.size.y * d.scale;
                Color c = color;
                c.a *= strength;
                block.SetColor(ShaderPropertyIDs.Color, c);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis(u * 45f + (spot & 0x3F), Vector3.up), new Vector3(s, 1f, s)), material, 0, null, 0, block);
            }
        }
    }

    /// <summary>
    /// Lightning crackling between spots on the body: each slot may show one short arc per
    /// interval, between two of the body's opaque points, for a few ticks. Lengths and width
    /// are fractions of the body mesh's height, so a rat's sparks fit a rat. Picked from game
    /// ticks and the creature's id, so nothing is stored and a pause freezes them.
    /// </summary>
    public class BastleSurface_Arcs : BastleSurface
    {
        [NoTranslate] public string texFolder = "Things/Mote/BF_Arc";
        public Color color = new Color(0.7f, 0.85f, 1f, 0.9f);
        public int count = 2;
        public int intervalTicks = 30;
        public float chance = 0.6f;
        public int arcTicks = 5;
        public float width = 0.14f;
        public float minLength = 0.15f;
        public float maxLength = 0.5f;

        [Unsaved] private List<Material> materials;

        public BastleSurface_Arcs() { layer = 2.6f; }

        public override IEnumerable<string> ConfigErrors()
        {
            if (texFolder.NullOrEmpty()) yield return "surface arcs need a texFolder";
            if (arcTicks < 1) yield return "surface arcs arcTicks must be at least 1";
            if (minLength <= 0f || maxLength < minLength) yield return "surface arcs need 0 < minLength <= maxLength";
        }

        public override void Draw(BastleSurfaceDraw d)
        {
            if (d.shape.points.NullOrEmpty()) return;
            var mats = materials ?? (materials = BastleEventDraw.LoadFrames(texFolder, color));
            if (mats.Count == 0) return;
            int interval = Mathf.Max(arcTicks + 1, intervalTicks);
            float size = d.size.y * d.scale;
            float min = minLength * size, max = maxLength * size;
            for (int i = 0; i < count; i++)
            {
                int slot = Gen.HashCombineInt(d.seed, i * 7919 + 31);
                int t = d.ticks + (slot & 0xFFFF);
                if (t % interval >= arcTicks) continue;
                int seed = BastleShape.Mix(slot ^ (t / interval) * 0x2C1B3C6D);
                if ((seed & 0xFFFF) / 65536f >= chance) continue;
                Vector3 a = d.Point(seed);
                Vector3 b = a;
                for (int k = 1; k <= 6; k++)
                {
                    b = d.Point(BastleShape.Mix(Gen.HashCombineInt(seed, k)));
                    float len = (b - a).MagnitudeHorizontal();
                    if (len >= min && len <= max) break;
                    // Too long: pull the far end back along the line rather than give up on the arc.
                    if (len > max) { b = a + (b - a) * (max / len); break; }
                }
                BastleEventDraw.DrawArc(mats[((seed >> 4) & 0x7FFFFFFF) % mats.Count], a, b, width * size, Mathf.Max(a.y, b.y) + 0.0002f);
            }
        }
    }

    public class PawnRenderNodeProperties_BastleSurface : PawnRenderNodeProperties_BastleBodyCopy
    {
        public BastleSurface surface;
    }

    public class PawnRenderNode_BastleSurface : PawnRenderNode_BastleBodyCopy
    {
        public PawnRenderNode_BastleSurface(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        public BastleSurface Surface => ((PawnRenderNodeProperties_BastleSurface)props).surface;

        // Only the texture is read from these, so the body's own graphics serve as they are.
        protected override Graphic Adapt(Graphic source) => source;
    }

    public class PawnRenderNodeWorker_BastleSurface : PawnRenderNodeWorker_BastleBodyCopy
    {
        /// <summary>The tree never draws this node itself: a request with no material is
        /// skipped by its draw loop, but PostDraw still runs with the mesh and matrix.</summary>
        public override Material GetFinalizedMaterial(PawnRenderNode node, PawnDrawParms parms) => null;

        public override void AppendDrawRequests(PawnRenderNode node, PawnDrawParms parms, List<PawnGraphicDrawRequest> requests)
        {
            if (parms.Portrait) return;
            var mesh = node.GetMesh(parms);
            if (mesh != null) requests.Add(new PawnGraphicDrawRequest(node, mesh));
        }

        private static readonly BastleSurfaceDraw draw = new BastleSurfaceDraw();

        public override void PostDraw(PawnRenderNode node, PawnDrawParms parms, Mesh mesh, Matrix4x4 matrix)
        {
            base.PostDraw(node, parms, mesh, matrix);
            if (mesh == null || !BastleFrameworkMod.Settings.surfacesEnabled) return;
            if (!(node is PawnRenderNode_BastleSurface s) || s.Surface == null) return;
            // Zoomed out, vanilla draws humanlikes from a picture it renders at the map's
            // origin (PawnRenderer.RenderCache): anything drawn then lands in the map's corner.
            if (parms.DrawNow || parms.flags.FlagSet(PawnRenderFlags.Cache) || parms.flags.FlagSet(PawnRenderFlags.Invisible)) return;
            // The texture the body shows this frame, for this facing and graphic state.
            var shape = BastleShape.For(base.GetFinalizedMaterial(node, parms)?.mainTexture);
            if (shape == null) return;
            var info = BastleShape.MeshInfo(mesh);
            Vector3 lossy = matrix.lossyScale;
            var d = draw;
            d.shape = shape;
            d.mesh = mesh;
            d.matrix = matrix;
            d.size = info.size;
            d.flipped = info.flipped;
            d.lift = info.lift;
            d.scale = (lossy.x + lossy.z) * 0.5f;
            d.seed = Gen.HashCombineInt(parms.pawn.thingIDNumber, 0x3E57);
            d.ticks = Find.TickManager.TicksGame;
            try { s.Surface.Draw(d); }
            catch (System.Exception e)
            {
                Log.ErrorOnce($"[Bastle Framework] surface {s.Surface.GetType().Name} failed on {parms.pawn}: {e}", Gen.HashCombineInt(s.Surface.GetHashCode(), 0x51));
            }
        }
    }
}
