using System;
using System.Collections.Generic;
using HappyWorld.Core.Art;
using UnityEngine;

namespace HappyWorld.Unity.Art
{
    /// <summary>
    /// Turns the bundled Kenney models (Resources/Art/Models/&lt;kit&gt;/&lt;name&gt;.bytes, CC0, see ART_CREDITS.md)
    /// into GameObjects. Meshes are built once per model and shared; materials use "Legacy Shaders/Diffuse"
    /// (always included by ProjectSetup) with the kit's colormap, so nothing can be stripped from the build.
    /// A missing or broken model returns null and the caller keeps its primitive look: art never blocks play.
    /// </summary>
    public static class ModelLibrary
    {
        private sealed class Entry
        {
            public GlbModel Model;
            public Mesh[] Meshes;
            public Texture2D Texture;
            public Vector3 Min, Max;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly Dictionary<Texture2D, Dictionary<Color, Material>> Mats = new Dictionary<Texture2D, Dictionary<Color, Material>>();
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Spawns model <paramref name="key"/> ("city/building-small-a") under parent. <paramref name="height"/> &gt; 0
        /// scales it uniformly to that height (negative = use -height as the scale); the model's lowest point sits on the parent's y. Returns null if unavailable.
        /// </summary>
        public static GameObject Spawn(string key, Transform parent, Vector3 localPos, float height = 0f, float yaw = 0f, IDictionary<string, Color> tints = null, Color? tintAll = null)
        {
            var e = Get(key);
            if (e == null) return null;
            var root = new GameObject(key.Substring(key.IndexOf('/') + 1));
            root.transform.SetParent(parent, false);
            var size = e.Max.y - e.Min.y;
            var scale = height < 0f ? -height : height > 0f && size > 0.0001f ? height / size : 1f;
            root.transform.localPosition = localPos + Vector3.up * (-e.Min.y * scale);
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            root.transform.localScale = Vector3.one * scale;
            var nodes = new Transform[e.Model.Nodes.Count];
            foreach (var r in e.Model.Roots) Build(e, r, root.transform, nodes, tints, tintAll);
            var holder = root.AddComponent<ModelInstance>();
            holder.Model = e.Model;
            holder.Nodes = nodes;
            return root;
        }

        /// <summary>Spawns at a fixed uniform scale (Kenney tiles are 1x1 units, so scale = footprint).</summary>
        public static GameObject SpawnScaled(string key, Transform parent, Vector3 localPos, float scale, float yaw = 0f, IDictionary<string, Color> tints = null, Color? tintAll = null) =>
            Spawn(key, parent, localPos, -Mathf.Abs(scale), yaw, tints, tintAll);

        /// <summary>Model-space bounds (rest pose), e.g. to fit a sign on top of a building.</summary>
        public static bool Bounds(string key, out Vector3 min, out Vector3 max)
        {
            var e = Get(key);
            min = e != null ? e.Min : Vector3.zero;
            max = e != null ? e.Max : Vector3.zero;
            return e != null;
        }

        public static bool Has(string key) => Get(key) != null;

        private static void Build(Entry e, int index, Transform parent, Transform[] nodes, IDictionary<string, Color> tints, Color? tintAll)
        {
            var n = e.Model.Nodes[index];
            var go = new GameObject(n.Name.Length > 0 ? n.Name : "node" + index);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(n.T[0], n.T[1], n.T[2]);
            t.localRotation = new Quaternion(n.R[0], n.R[1], n.R[2], n.R[3]);
            t.localScale = new Vector3(n.S[0], n.S[1], n.S[2]);
            nodes[index] = t;
            if (n.Mesh >= 0)
            {
                go.AddComponent<MeshFilter>().sharedMesh = e.Meshes[n.Mesh];
                var mr = go.AddComponent<MeshRenderer>();
                Color tint;
                if (tints == null || !tints.TryGetValue(n.Name, out tint)) tint = tintAll ?? Color.white;
                var mats = new Material[e.Model.Meshes[n.Mesh].Primitives.Count];
                for (int i = 0; i < mats.Length; i++)
                {
                    var bc = e.Model.Meshes[n.Mesh].Primitives[i].BaseColor;
                    mats[i] = Material(e.Texture, tint * new Color(bc[0], bc[1], bc[2], 1f));
                }
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            foreach (var c in n.Children) Build(e, c, t, nodes, tints, tintAll);
        }

        /// <summary>One shared material per (texture, tint): keeps draw calls batchable.</summary>
        public static Material Material(Texture2D tex, Color tint)
        {
            if (tex == null) return World.CityBuilder.Mat(tint);
            Dictionary<Color, Material> byColor;
            if (!Mats.TryGetValue(tex, out byColor)) Mats[tex] = byColor = new Dictionary<Color, Material>();
            Material m;
            if (byColor.TryGetValue(tint, out m)) return m;
            m = new Material(World.CityBuilder.Mat(Color.white)) { mainTexture = tex, color = tint };
            byColor[tint] = m;
            return m;
        }

        private static Entry Get(string key)
        {
            Entry e;
            if (Cache.TryGetValue(key, out e)) return e;
            if (Missing.Contains(key)) return null;
            try
            {
                var asset = Resources.Load<TextAsset>("Art/Models/" + key);
                if (asset == null) throw new Exception("not in Resources");
                var model = GlbModel.Read(asset.bytes);
                e = new Entry { Model = model, Meshes = new Mesh[model.Meshes.Count] };
                for (int i = 0; i < model.Meshes.Count; i++) e.Meshes[i] = ToMesh(model.Meshes[i]);
                float[] min, max;
                model.Bounds(out min, out max);
                e.Min = new Vector3(min[0], min[1], min[2]);
                e.Max = new Vector3(max[0], max[1], max[2]);
                if (model.ImageName != null)
                {
                    var kit = key.Substring(0, Math.Max(0, key.IndexOf('/')));
                    e.Texture = Texture(kit + "/" + System.IO.Path.GetFileNameWithoutExtension(model.ImageName));
                }
                Cache[key] = e;
                return e;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HappyWorld] model " + key + " unavailable, using primitives: " + ex.Message);
                Missing.Add(key);
                return null;
            }
        }

        private static Texture2D Texture(string path)
        {
            Texture2D tex;
            if (Textures.TryGetValue(path, out tex)) return tex;
            tex = Resources.Load<Texture2D>("Art/Models/" + path);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point; // Kenney colormaps are flat colour cells: crisp, no bleeding
                tex.wrapMode = TextureWrapMode.Clamp;
            }
            Textures[path] = tex;
            return tex;
        }

        private static Mesh ToMesh(GlbMesh gm)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subs = new List<int[]>();
            foreach (var p in gm.Primitives)
            {
                int baseIndex = verts.Count;
                for (int i = 0; i < p.VertexCount; i++)
                {
                    verts.Add(new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]));
                    normals.Add(p.Normals != null ? new Vector3(p.Normals[i * 3], p.Normals[i * 3 + 1], p.Normals[i * 3 + 2]) : Vector3.up);
                    uvs.Add(p.Uvs != null ? new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]) : Vector2.zero);
                }
                var idx = new int[p.Indices.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = p.Indices[i] + baseIndex;
                subs.Add(idx);
            }
            var mesh = new Mesh { name = gm.Name ?? "glb" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            if (gm.Primitives.Exists(p => p.Normals == null)) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Remembers which Transform is which glTF node, for <see cref="NodeAnimator"/>.</summary>
    public sealed class ModelInstance : MonoBehaviour
    {
        public GlbModel Model;
        public Transform[] Nodes;

        public Transform Node(string name)
        {
            var i = Model.NodeIndex(name);
            return i >= 0 ? Nodes[i] : null;
        }
    }

    /// <summary>Plays the model's own node animations ("idle", "walk", "jump" on the Kenney character).</summary>
    public sealed class NodeAnimator : MonoBehaviour
    {
        private ModelInstance _inst;
        private GlbClip _clip;
        private float _t;
        private readonly float[] _v = new float[4];
        public float Speed = 1f;
        public string Current => _clip != null ? _clip.Name : null;

        public static NodeAnimator Attach(GameObject model)
        {
            if (model == null) return null;
            var inst = model.GetComponent<ModelInstance>();
            if (inst == null || inst.Model.Clips.Count == 0) return null;
            var a = model.AddComponent<NodeAnimator>();
            a._inst = inst;
            return a;
        }

        public void Play(string name, float speed = 1f)
        {
            Speed = speed;
            if (_clip != null && _clip.Name == name) return;
            var c = _inst.Model.Clip(name);
            if (c == null) return;
            _clip = c;
            _t = 0f;
        }

        private void Update()
        {
            if (_clip == null || _clip.Duration <= 0f) return;
            _t = (_t + Time.deltaTime * Speed) % _clip.Duration;
            foreach (var ch in _clip.Channels)
            {
                var node = ch.Node < _inst.Nodes.Length ? _inst.Nodes[ch.Node] : null;
                if (node == null) continue;
                ch.Sample(_t, _v);
                if (ch.Width == 4) node.localRotation = new Quaternion(_v[0], _v[1], _v[2], _v[3]);
                else if (ch.Path == "translation") node.localPosition = new Vector3(_v[0], _v[1], _v[2]);
                else node.localScale = new Vector3(_v[0], _v[1], _v[2]);
            }
        }
    }
}
