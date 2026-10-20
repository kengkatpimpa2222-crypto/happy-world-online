using System;
using System.Collections.Generic;
using HappyWorld.Core.Art;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HappyWorld.Unity.Art
{
    /// <summary>
    /// One avatar part file (Resources/Art/Models/avatar/&lt;key&gt;.bytes: the body's skeleton + face, an outfit,
    /// a hair or a hat) turned into a skinned Mesh and toon materials, built once and shared by every player
    /// who wears it. Bind poses come from the file's own rest skeleton, which build_avatars.py made identical
    /// for every part of a body type, so a part can go onto any character of that body type.
    /// </summary>
    public sealed class AvatarPart
    {
        public GlbModel Model;
        public Mesh Mesh;
        public Material[] Materials;
        /// <summary>Skeleton node names of the mesh's bones, in Mesh bone order.</summary>
        public string[] BoneNames;
        public int MeshNode = -1;
        public JObject Extras => Model.Extras;

        private static readonly Dictionary<string, AvatarPart> Cache = new Dictionary<string, AvatarPart>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static Shader _toon;
        private static bool _toonLooked;

        /// <summary>The part, or null if its file is missing or broken (the caller then keeps its old look).</summary>
        public static AvatarPart Get(string key)
        {
            if (key == null) return null;
            AvatarPart p;
            if (Cache.TryGetValue(key, out p)) return p;
            if (Missing.Contains(key)) return null;
            try
            {
                var asset = Resources.Load<TextAsset>("Art/Models/" + key);
                if (asset == null) throw new Exception("not in Resources");
                p = Build(GlbModel.Read(asset.bytes));
                Resources.UnloadAsset(asset);
                Cache[key] = p;
                return p;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HappyWorld] avatar part " + key + " unavailable: " + ex.Message);
                Missing.Add(key);
                return null;
            }
        }

        private static AvatarPart Build(GlbModel m)
        {
            var part = new AvatarPart { Model = m };
            for (int i = 0; i < m.Nodes.Count; i++) if (m.Nodes[i].Mesh >= 0) part.MeshNode = i;
            if (part.MeshNode < 0) return part; // skeleton only
            var node = m.Nodes[part.MeshNode];
            if (node.Skin < 0) throw new Exception("avatar mesh is not skinned");
            var joints = m.Skins[node.Skin].Joints;
            var gm = m.Meshes[node.Mesh];

            // rest pose of every node in model space, for the bind poses
            var world = new Matrix4x4[m.Nodes.Count];
            foreach (var r in m.Roots) Walk(m, r, Matrix4x4.identity, world);
            part.BoneNames = new string[joints.Length];
            var bind = new Matrix4x4[joints.Length];
            for (int j = 0; j < joints.Length; j++)
            {
                part.BoneNames[j] = m.Nodes[joints[j]].Name;
                bind[j] = world[joints[j]].inverse;
            }

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var subs = new List<int[]>();
            var mats = new List<Material>();
            foreach (var p in gm.Primitives)
            {
                if (p.Joints == null) throw new Exception("primitive without joints");
                int baseIndex = verts.Count;
                for (int i = 0; i < p.VertexCount; i++)
                {
                    verts.Add(new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]));
                    normals.Add(p.Normals != null ? new Vector3(p.Normals[i * 3], p.Normals[i * 3 + 1], p.Normals[i * 3 + 2]) : Vector3.up);
                    uvs.Add(p.Uvs != null ? new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]) : Vector2.zero);
                    weights.Add(Weight(p, i, joints.Length));
                }
                var idx = new int[p.Indices.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = p.Indices[i] + baseIndex;
                subs.Add(idx);
                mats.Add(MaterialFor(p));
            }
            var mesh = new Mesh { name = gm.Name ?? "avatar" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bind;
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            // morph targets (the face's blink and smile), one frame each, zero where a primitive lacks them
            for (int t = 0; t < gm.TargetNames.Count; t++)
            {
                var d = new Vector3[verts.Count];
                int o = 0;
                foreach (var p in gm.Primitives)
                {
                    if (t < p.Targets.Count)
                    {
                        var src = p.Targets[t];
                        for (int i = 0; i < p.VertexCount; i++) d[o + i] = new Vector3(src[i * 3], src[i * 3 + 1], src[i * 3 + 2]);
                    }
                    o += p.VertexCount;
                }
                mesh.AddBlendShapeFrame(gm.TargetNames[t], 100f, d, null, null);
            }
            mesh.RecalculateBounds();
            part.Mesh = mesh;
            part.Materials = mats.ToArray();
            return part;
        }

        private static BoneWeight Weight(GlbPrimitive p, int v, int boneCount)
        {
            var j = p.Joints;
            var w = p.Weights;
            int o = v * 4;
            float sum = w[o] + w[o + 1] + w[o + 2] + w[o + 3];
            if (sum <= 0f) sum = 1f;
            for (int k = 0; k < 4; k++)
                if (j[o + k] < 0 || j[o + k] >= boneCount) throw new Exception("joint index out of range");
            return new BoneWeight
            {
                boneIndex0 = j[o], weight0 = w[o] / sum,
                boneIndex1 = j[o + 1], weight1 = w[o + 1] / sum,
                boneIndex2 = j[o + 2], weight2 = w[o + 2] / sum,
                boneIndex3 = j[o + 3], weight3 = w[o + 3] / sum,
            };
        }

        private static void Walk(GlbModel m, int i, Matrix4x4 parent, Matrix4x4[] world)
        {
            var n = m.Nodes[i];
            var local = Matrix4x4.TRS(new Vector3(n.T[0], n.T[1], n.T[2]), new Quaternion(n.R[0], n.R[1], n.R[2], n.R[3]), new Vector3(n.S[0], n.S[1], n.S[2]));
            world[i] = parent * local;
            foreach (var c in n.Children) Walk(m, c, world[i], world);
        }

        /// <summary>
        /// "HappyWorld/AnimeToon" (Resources/Art/Shaders) with the part's texture and shade colour; skin
        /// materials take the player's skin tone through a MaterialPropertyBlock. Falls back to the always
        /// included cut-out or diffuse shaders, so a stripped shader never makes an avatar vanish.
        /// </summary>
        private static Material MaterialFor(GlbPrimitive p)
        {
            var shade = p.MaterialExtras?["shade"] as JArray;
            var skin = (float?)p.MaterialExtras?["skin"] ?? 0f;
            var key = p.Image + "|" + (shade != null ? shade.ToString(Newtonsoft.Json.Formatting.None) : "") + "|" + skin;
            Material mat;
            if (Mats.TryGetValue(key, out mat)) return mat;
            if (!_toonLooked)
            {
                _toonLooked = true;
                _toon = Shader.Find("HappyWorld/AnimeToon");
                if (_toon == null || !_toon.isSupported)
                {
                    Debug.LogWarning("[HappyWorld] AnimeToon shader unavailable, avatars use a cut-out fallback");
                    _toon = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse");
                    if (_toon == null) _toon = Shader.Find("Legacy Shaders/Diffuse");
                }
            }
            mat = _toon != null ? new Material(_toon) : new Material(World.CityBuilder.Mat(Color.white));
            mat.name = p.MaterialName ?? "avatar";
            if (p.Image != null)
            {
                var tex = Resources.Load<Texture2D>("Art/Models/avatar/" + System.IO.Path.GetFileNameWithoutExtension(p.Image));
                if (tex != null) mat.mainTexture = tex;
            }
            if (shade != null && shade.Count >= 3 && mat.HasProperty("_ShadeColor"))
                mat.SetColor("_ShadeColor", new Color((float)shade[0], (float)shade[1], (float)shade[2], 1f));
            if (mat.HasProperty("_IsSkin")) mat.SetFloat("_IsSkin", skin);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
            Mats[key] = mat;
            return mat;
        }
    }
}
