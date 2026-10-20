using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HappyWorld.Core.Art
{
    /// <summary>
    /// A binary glTF (.glb) model read without any engine code, so the city can use real 3D art
    /// (Kenney, CC0) that ships as plain bytes in Resources: no importer, no extra Unity package,
    /// no shader that the Android build could strip. The Unity layer turns this into meshes.
    ///
    /// Everything is converted to Unity's left-handed space on read: X is mirrored, rotations become
    /// (x, -y, -z, w) and triangle winding is flipped, and texture V is flipped (glTF's origin is top-left).
    /// Skinned models (the anime avatars) also bring their skins, per-vertex joints/weights, morph targets
    /// and the file's "extras" (humanoid bone map, spring bones), see <see cref="GlbSkin"/>.
    /// </summary>
    public sealed class GlbModel
    {
        public readonly List<GlbNode> Nodes = new List<GlbNode>();
        public readonly List<int> Roots = new List<int>();
        public readonly List<GlbMesh> Meshes = new List<GlbMesh>();
        public readonly List<GlbClip> Clips = new List<GlbClip>();
        public readonly List<GlbSkin> Skins = new List<GlbSkin>();
        /// <summary>The file's top-level "extras" object (empty when it has none).</summary>
        public JObject Extras = new JObject();
        /// <summary>File name of the first image (e.g. "colormap.png"); null when the model is untextured.</summary>
        public string ImageName;

        public GlbClip Clip(string name)
        {
            foreach (var c in Clips) if (c.Name == name) return c;
            return null;
        }

        public int NodeIndex(string name)
        {
            for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Name == name) return i;
            return -1;
        }

        /// <summary>Axis-aligned bounds of every mesh in model space (rest pose), for scaling and placement.</summary>
        public void Bounds(out float[] min, out float[] max)
        {
            min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            max = new[] { float.MinValue, float.MinValue, float.MinValue };
            foreach (var r in Roots) BoundsWalk(r, Mat4.Identity, min, max);
        }

        private void BoundsWalk(int ni, Mat4 parent, float[] min, float[] max)
        {
            var n = Nodes[ni];
            var m = parent * Mat4.Trs(n.T, n.R, n.S);
            if (n.Mesh >= 0)
                foreach (var p in Meshes[n.Mesh].Primitives)
                    for (int i = 0; i < p.Positions.Length; i += 3)
                    {
                        var v = m.Point(p.Positions[i], p.Positions[i + 1], p.Positions[i + 2]);
                        for (int k = 0; k < 3; k++)
                        {
                            if (v[k] < min[k]) min[k] = v[k];
                            if (v[k] > max[k]) max[k] = v[k];
                        }
                    }
            foreach (var c in n.Children) BoundsWalk(c, m, min, max);
        }

        // ---------- reading ----------

        public static GlbModel Read(byte[] glb)
        {
            if (glb == null || glb.Length < 20) throw new InvalidDataException("not a glb file");
            if (BitConverter.ToUInt32(glb, 0) != 0x46546C67) throw new InvalidDataException("bad glb magic");
            int offset = 12;
            JObject json = null;
            int binStart = -1, binLength = 0;
            while (offset + 8 <= glb.Length)
            {
                int len = (int)BitConverter.ToUInt32(glb, offset);
                uint type = BitConverter.ToUInt32(glb, offset + 4);
                if (offset + 8 + len > glb.Length) throw new InvalidDataException("truncated glb chunk");
                if (type == 0x4E4F534A) json = JObject.Parse(Encoding.UTF8.GetString(glb, offset + 8, len));
                else if (type == 0x004E4942) { binStart = offset + 8; binLength = len; }
                offset += 8 + len;
            }
            if (json == null) throw new InvalidDataException("glb has no JSON chunk");
            return new Reader(json, glb, binStart, binLength).Build();
        }

        private sealed class Reader
        {
            private readonly JObject _j;
            private readonly byte[] _b;
            private readonly int _bin, _binLen;

            public Reader(JObject j, byte[] b, int bin, int binLen)
            {
                _j = j;
                _b = b;
                _bin = bin;
                _binLen = binLen;
            }

            public GlbModel Build()
            {
                var m = new GlbModel();
                var images = _j["images"] as JArray;
                if (images != null && images.Count > 0)
                {
                    var uri = (string)images[0]["uri"];
                    if (uri != null) m.ImageName = uri.Substring(uri.LastIndexOfAny(new[] { '/', '\\' }) + 1);
                }
                var meshes = _j["meshes"] as JArray ?? new JArray();
                foreach (JObject mj in meshes)
                {
                    var mesh = new GlbMesh { Name = (string)mj["name"] };
                    if (mj["extras"]?["targetNames"] is JArray names) foreach (var t in names) mesh.TargetNames.Add((string)t);
                    foreach (JObject pj in (JArray)mj["primitives"]) mesh.Primitives.Add(ReadPrimitive(pj));
                    m.Meshes.Add(mesh);
                }
                var nodes = _j["nodes"] as JArray ?? new JArray();
                foreach (JObject nj in nodes) m.Nodes.Add(ReadNode(nj));
                var scenes = _j["scenes"] as JArray;
                if (scenes != null && scenes.Count > 0)
                {
                    var scene = scenes[(int?)_j["scene"] ?? 0];
                    foreach (var r in (JArray)scene["nodes"]) m.Roots.Add((int)r);
                }
                else
                {
                    var isChild = new bool[m.Nodes.Count];
                    foreach (var n in m.Nodes) foreach (var c in n.Children) isChild[c] = true;
                    for (int i = 0; i < isChild.Length; i++) if (!isChild[i]) m.Roots.Add(i);
                }
                var skins = _j["skins"] as JArray;
                if (skins != null)
                    foreach (JObject sj in skins)
                    {
                        var joints = ((JArray)sj["joints"]).ToInts();
                        foreach (var ji in joints)
                            if (ji < 0 || ji >= m.Nodes.Count) throw new InvalidDataException("skin joint out of range");
                        m.Skins.Add(new GlbSkin { Joints = joints });
                    }
                foreach (var n in m.Nodes)
                    if (n.Skin >= m.Skins.Count) throw new InvalidDataException("node skin out of range");
                if (_j["extras"] is JObject extras) m.Extras = extras;
                var anims = _j["animations"] as JArray;
                if (anims != null) foreach (JObject aj in anims) m.Clips.Add(ReadClip(aj));
                return m;
            }

            private GlbNode ReadNode(JObject nj)
            {
                var n = new GlbNode { Name = (string)nj["name"] ?? "", Mesh = (int?)nj["mesh"] ?? -1, Skin = (int?)nj["skin"] ?? -1 };
                var children = nj["children"] as JArray;
                n.Children = children == null ? new int[0] : children.ToInts();
                var t = Floats(nj["translation"], 0, 0, 0);
                var r = Floats(nj["rotation"], 0, 0, 0, 1);
                var s = Floats(nj["scale"], 1, 1, 1);
                n.T = new[] { -t[0], t[1], t[2] };
                n.R = new[] { r[0], -r[1], -r[2], r[3] };
                n.S = s;
                if (nj["matrix"] != null) throw new InvalidDataException("node matrices are not supported; export with TRS");
                return n;
            }

            private GlbPrimitive ReadPrimitive(JObject pj)
            {
                var mode = (int?)pj["mode"] ?? 4;
                if (mode != 4) throw new InvalidDataException("only triangle lists are supported");
                var attrs = (JObject)pj["attributes"];
                var p = new GlbPrimitive { Material = (int?)pj["material"] ?? -1 };
                p.Positions = ReadFloats((int)attrs["POSITION"], 3);
                for (int i = 0; i < p.Positions.Length; i += 3) p.Positions[i] = -p.Positions[i];
                if (attrs["NORMAL"] != null)
                {
                    p.Normals = ReadFloats((int)attrs["NORMAL"], 3);
                    for (int i = 0; i < p.Normals.Length; i += 3) p.Normals[i] = -p.Normals[i];
                }
                if (attrs["TEXCOORD_0"] != null)
                {
                    p.Uvs = ReadFloats((int)attrs["TEXCOORD_0"], 2);
                    for (int i = 1; i < p.Uvs.Length; i += 2) p.Uvs[i] = 1f - p.Uvs[i];
                }
                int vertexCount = p.Positions.Length / 3;
                if (attrs["JOINTS_0"] != null && attrs["WEIGHTS_0"] != null)
                {
                    var j = ReadFloats((int)attrs["JOINTS_0"], 4);
                    p.Joints = new int[j.Length];
                    for (int i = 0; i < j.Length; i++) p.Joints[i] = (int)j[i];
                    p.Weights = ReadFloats((int)attrs["WEIGHTS_0"], 4);
                    if (p.Joints.Length != vertexCount * 4 || p.Weights.Length != vertexCount * 4) throw new InvalidDataException("joints/weights do not match the vertices");
                }
                if (pj["targets"] is JArray targets)
                    foreach (JObject tj in targets)
                    {
                        var d = tj["POSITION"] != null ? ReadFloats((int)tj["POSITION"], 3) : new float[vertexCount * 3];
                        if (d.Length != vertexCount * 3) throw new InvalidDataException("morph target does not match the vertices");
                        for (int i = 0; i < d.Length; i += 3) d[i] = -d[i];
                        p.Targets.Add(d);
                    }
                int[] idx;
                if (pj["indices"] != null) idx = ReadInts((int)pj["indices"]);
                else
                {
                    idx = new int[vertexCount];
                    for (int i = 0; i < vertexCount; i++) idx[i] = i;
                }
                if (idx.Length % 3 != 0) throw new InvalidDataException("index count is not a multiple of 3");
                for (int i = 0; i < idx.Length; i += 3)
                {
                    if (idx[i] >= vertexCount || idx[i + 1] >= vertexCount || idx[i + 2] >= vertexCount) throw new InvalidDataException("index out of range");
                    var tmp = idx[i + 1]; // mirroring X flips the winding; swap to keep faces pointing out
                    idx[i + 1] = idx[i + 2];
                    idx[i + 2] = tmp;
                }
                p.Indices = idx;
                var mat = pj["material"] != null ? _j["materials"]?[(int)pj["material"]] : null;
                var factor = mat?["pbrMetallicRoughness"]?["baseColorFactor"] as JArray;
                if (mat != null)
                {
                    p.MaterialName = (string)mat["name"];
                    p.MaterialExtras = mat["extras"] as JObject;
                    var tex = (int?)mat["pbrMetallicRoughness"]?["baseColorTexture"]?["index"];
                    var src = tex != null ? (int?)_j["textures"]?[tex.Value]?["source"] : null;
                    var uri = src != null ? (string)_j["images"]?[src.Value]?["uri"] : null;
                    if (uri != null) p.Image = uri.Substring(uri.LastIndexOfAny(new[] { '/', '\\' }) + 1);
                }
                if (factor != null) p.BaseColor = new[] { (float)factor[0], (float)factor[1], (float)factor[2], (float)factor[3] };
                return p;
            }

            private GlbClip ReadClip(JObject aj)
            {
                var clip = new GlbClip { Name = (string)aj["name"] ?? "" };
                var samplers = (JArray)aj["samplers"];
                foreach (JObject cj in (JArray)aj["channels"])
                {
                    var target = (JObject)cj["target"];
                    if (target["node"] == null) continue;
                    var path = (string)target["path"];
                    int width = path == "rotation" ? 4 : path == "translation" || path == "scale" ? 3 : 0;
                    if (width == 0) continue; // morph weights: not used by our art
                    var sj = (JObject)samplers[(int)cj["sampler"]];
                    var interp = (string)sj["interpolation"] ?? "LINEAR";
                    var ch = new GlbChannel
                    {
                        Node = (int)target["node"],
                        Path = path,
                        Width = width,
                        Step = interp == "STEP",
                        Times = ReadFloats((int)sj["input"], 1),
                    };
                    var values = ReadFloats((int)sj["output"], width);
                    if (interp == "CUBICSPLINE")
                    {
                        // keep only the value of each (in-tangent, value, out-tangent) triple; sampled linearly
                        var v = new float[ch.Times.Length * width];
                        for (int k = 0; k < ch.Times.Length; k++) Array.Copy(values, (k * 3 + 1) * width, v, k * width, width);
                        values = v;
                    }
                    for (int k = 0; k < ch.Times.Length; k++)
                    {
                        if (path == "translation") values[k * 3] = -values[k * 3];
                        if (path == "rotation") { values[k * 4 + 1] = -values[k * 4 + 1]; values[k * 4 + 2] = -values[k * 4 + 2]; }
                    }
                    ch.Values = values;
                    if (ch.Times.Length > 0 && ch.Times[ch.Times.Length - 1] > clip.Duration) clip.Duration = ch.Times[ch.Times.Length - 1];
                    clip.Channels.Add(ch);
                }
                return clip;
            }

            private static float[] Floats(JToken t, params float[] fallback)
            {
                var a = t as JArray;
                if (a == null) return fallback;
                var r = new float[a.Count];
                for (int i = 0; i < r.Length; i++) r[i] = (float)a[i];
                return r;
            }

            private void View(int accessor, out JObject acc, out int start, out int stride, out int count, out int width, out int comp)
            {
                acc = (JObject)_j["accessors"][accessor];
                if (acc["sparse"] != null) throw new InvalidDataException("sparse accessors are not supported");
                count = (int)acc["count"];
                width = Width((string)acc["type"]);
                comp = (int)acc["componentType"];
                var bv = (JObject)_j["bufferViews"][(int)acc["bufferView"]];
                if (((int?)bv["buffer"] ?? 0) != 0 || _bin < 0) throw new InvalidDataException("only the glb's own buffer is supported");
                start = _bin + ((int?)bv["byteOffset"] ?? 0) + ((int?)acc["byteOffset"] ?? 0);
                int size = CompSize(comp);
                stride = (int?)bv["byteStride"] ?? 0;
                if (stride == 0) stride = size * width;
                int end = start + (count - 1) * stride + size * width;
                if (count > 0 && end > _bin + _binLen) throw new InvalidDataException("accessor runs past the buffer");
            }

            private float[] ReadFloats(int accessor, int expectWidth)
            {
                JObject acc;
                int start, stride, count, width, comp;
                View(accessor, out acc, out start, out stride, out count, out width, out comp);
                if (width != expectWidth) throw new InvalidDataException("unexpected accessor type");
                var normalized = (bool?)acc["normalized"] ?? false;
                var r = new float[count * width];
                int size = CompSize(comp);
                for (int i = 0; i < count; i++)
                    for (int k = 0; k < width; k++)
                    {
                        int o = start + i * stride + k * size;
                        float v;
                        switch (comp)
                        {
                            case 5126: v = BitConverter.ToSingle(_b, o); break;
                            case 5121: v = normalized ? _b[o] / 255f : _b[o]; break;
                            case 5123: v = normalized ? BitConverter.ToUInt16(_b, o) / 65535f : BitConverter.ToUInt16(_b, o); break;
                            case 5120: v = normalized ? Math.Max((sbyte)_b[o] / 127f, -1f) : (sbyte)_b[o]; break;
                            case 5122: v = normalized ? Math.Max(BitConverter.ToInt16(_b, o) / 32767f, -1f) : BitConverter.ToInt16(_b, o); break;
                            default: throw new InvalidDataException("unsupported float component type " + comp);
                        }
                        r[i * width + k] = v;
                    }
                return r;
            }

            private int[] ReadInts(int accessor)
            {
                JObject acc;
                int start, stride, count, width, comp;
                View(accessor, out acc, out start, out stride, out count, out width, out comp);
                var r = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int o = start + i * stride;
                    switch (comp)
                    {
                        case 5121: r[i] = _b[o]; break;
                        case 5123: r[i] = BitConverter.ToUInt16(_b, o); break;
                        case 5125: r[i] = (int)BitConverter.ToUInt32(_b, o); break;
                        default: throw new InvalidDataException("unsupported index type " + comp);
                    }
                }
                return r;
            }

            private static int Width(string type)
            {
                switch (type)
                {
                    case "SCALAR": return 1;
                    case "VEC2": return 2;
                    case "VEC3": return 3;
                    case "VEC4": return 4;
                    default: throw new InvalidDataException("unsupported accessor type " + type);
                }
            }

            private static int CompSize(int comp)
            {
                switch (comp)
                {
                    case 5120: case 5121: return 1;
                    case 5122: case 5123: return 2;
                    case 5125: case 5126: return 4;
                    default: throw new InvalidDataException("unsupported component type " + comp);
                }
            }
        }
    }

    public sealed class GlbNode
    {
        public string Name;
        public int Mesh = -1;
        /// <summary>Index into <see cref="GlbModel.Skins"/> when this node's mesh is skinned, else -1.</summary>
        public int Skin = -1;
        public int[] Children = new int[0];
        /// <summary>Rest pose in Unity space: translation xyz, rotation quaternion xyzw, scale xyz.</summary>
        public float[] T, R, S;
    }

    public sealed class GlbMesh
    {
        public string Name;
        public readonly List<GlbPrimitive> Primitives = new List<GlbPrimitive>();
        /// <summary>Morph target names (mesh extras "targetNames"), same order as each primitive's Targets.</summary>
        public readonly List<string> TargetNames = new List<string>();
    }

    /// <summary>A skin: JOINTS_0 values index <see cref="Joints"/>, which are node indices.</summary>
    public sealed class GlbSkin
    {
        public int[] Joints;
    }

    public sealed class GlbPrimitive
    {
        public float[] Positions, Normals, Uvs;
        public int[] Indices;
        public int Material = -1;
        /// <summary>4 joints and 4 weights per vertex when skinned, else null.</summary>
        public int[] Joints;
        public float[] Weights;
        /// <summary>Morph targets: position deltas (xyz per vertex).</summary>
        public readonly List<float[]> Targets = new List<float[]>();
        public string MaterialName;
        /// <summary>File name of the base colour texture ("female_face.png"), null when untextured.</summary>
        public string Image;
        public JObject MaterialExtras;
        /// <summary>The material's base colour factor (RGBA), multiplied with the texture.</summary>
        public float[] BaseColor = { 1, 1, 1, 1 };
        public int VertexCount => Positions.Length / 3;
    }

    /// <summary>A node animation (Kenney characters animate rigid parts, no skinning).</summary>
    public sealed class GlbClip
    {
        public string Name;
        public float Duration;
        public readonly List<GlbChannel> Channels = new List<GlbChannel>();
    }

    public sealed class GlbChannel
    {
        public int Node;
        /// <summary>"translation", "rotation" or "scale".</summary>
        public string Path;
        public int Width;
        public bool Step;
        public float[] Times, Values;

        /// <summary>Value at time t (clamped to the keys); rotations are normalised lerps along the short arc.</summary>
        public void Sample(float t, float[] into)
        {
            int n = Times.Length;
            if (n == 0) return;
            if (n == 1 || t <= Times[0]) { Array.Copy(Values, 0, into, 0, Width); return; }
            if (t >= Times[n - 1]) { Array.Copy(Values, (n - 1) * Width, into, 0, Width); return; }
            int hi = 1;
            while (Times[hi] < t) hi++;
            int lo = hi - 1;
            if (Step) { Array.Copy(Values, lo * Width, into, 0, Width); return; }
            float span = Times[hi] - Times[lo];
            float u = span > 0 ? (t - Times[lo]) / span : 0f;
            int a = lo * Width, b = hi * Width;
            if (Width == 4)
            {
                float dot = 0;
                for (int k = 0; k < 4; k++) dot += Values[a + k] * Values[b + k];
                float sign = dot < 0 ? -1f : 1f;
                float len = 0;
                for (int k = 0; k < 4; k++)
                {
                    into[k] = Values[a + k] + (sign * Values[b + k] - Values[a + k]) * u;
                    len += into[k] * into[k];
                }
                len = (float)Math.Sqrt(len);
                if (len > 1e-6f) for (int k = 0; k < 4; k++) into[k] /= len;
                return;
            }
            for (int k = 0; k < Width; k++) into[k] = Values[a + k] + (Values[b + k] - Values[a + k]) * u;
        }
    }

    internal static class JArrayExt
    {
        public static int[] ToInts(this JArray a)
        {
            var r = new int[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = (int)a[i];
            return r;
        }
    }

    /// <summary>Tiny 4x4 matrix (row-major) used only to compute bounds outside the engine.</summary>
    internal struct Mat4
    {
        private float[] _m;

        public static Mat4 Identity => new Mat4 { _m = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 } };

        public static Mat4 Trs(float[] t, float[] q, float[] s)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            var r = new float[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y),
            };
            return new Mat4
            {
                _m = new float[]
                {
                    r[0] * s[0], r[1] * s[1], r[2] * s[2], t[0],
                    r[3] * s[0], r[4] * s[1], r[5] * s[2], t[1],
                    r[6] * s[0], r[7] * s[1], r[8] * s[2], t[2],
                    0, 0, 0, 1,
                },
            };
        }

        public static Mat4 operator *(Mat4 a, Mat4 b)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float sum = 0;
                    for (int k = 0; k < 4; k++) sum += a._m[i * 4 + k] * b._m[k * 4 + j];
                    r[i * 4 + j] = sum;
                }
            return new Mat4 { _m = r };
        }

        public float[] Point(float x, float y, float z) => new[]
        {
            _m[0] * x + _m[1] * y + _m[2] * z + _m[3],
            _m[4] * x + _m[5] * y + _m[6] * z + _m[7],
            _m[8] * x + _m[9] * y + _m[10] * z + _m[11],
        };
    }
}
