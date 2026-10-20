using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;
using HappyWorld.Unity.Art;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Builds a zone from GET /city data. Houses, trees, bushes, flags and clouds are Kenney low-poly models
    /// (<see cref="ModelLibrary"/>); if a model is missing the old primitive stands in, so the city always builds.
    /// Server (x, y) maps to Unity (x, 0, y); the layout stays server-driven.
    /// </summary>
    public static class CityBuilder
    {
        public sealed class PortalInfo
        {
            public string To;
            public Vector3 Position;
        }

        private static readonly Dictionary<string, Color> Ground = new Dictionary<string, Color>
        {
            ["PLAZA"] = new Color(0.55f, 0.78f, 0.45f),
            ["HOMES"] = new Color(0.62f, 0.82f, 0.55f),
            ["MARKET"] = new Color(0.86f, 0.76f, 0.56f),
            ["ACTIVITY"] = new Color(0.50f, 0.72f, 0.85f),
            ["MINIGAME_GATE"] = new Color(0.80f, 0.65f, 0.88f),
            // Phase 12
            ["COUNTRYSIDE"] = new Color(0.58f, 0.80f, 0.38f),
            ["INDUSTRIAL"] = new Color(0.70f, 0.70f, 0.68f),
        };

        private static Material _baseMat;
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        /// <summary>
        /// Coloured material on "Legacy Shaders/Diffuse", which ProjectSetup adds to Always Included Shaders.
        /// The primitive's own Default-Material (Standard) is not referenced by any asset, so the build strips
        /// Standard and everything rendered magenta on device (Phase 4.5); Diffuse is also far cheaper on mobile.
        /// </summary>
        public static Material Mat(Color c)
        {
            if (_baseMat == null)
            {
                var shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader != null) _baseMat = new Material(shader);
                else
                {
                    Debug.LogWarning("[HappyWorld] Legacy Shaders/Diffuse missing from build; using primitive material");
                    var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _baseMat = probe.GetComponent<Renderer>().sharedMaterial;
                    Object.Destroy(probe);
                }
            }
            Material m;
            if (Cache.TryGetValue(c, out m)) return m;
            m = new Material(_baseMat) { color = c };
            Cache[c] = m;
            return m;
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color c, string name = null)
        {
            var go = GameObject.CreatePrimitive(t);
            if (name != null) go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            return go;
        }

        /// <param name="keepClear">spots (workplaces, Phase 12) that random trees and houses stay away from</param>
        public static List<PortalInfo> Build(Zone zone, Transform root, IEnumerable<Vector3> keepClear = null)
        {
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
            float w = zone.width, h = zone.height;
            Color g;
            if (!Ground.TryGetValue(zone.id, out g)) g = Color.gray;

            // ground (Unity plane is 10x10)
            var ground = Prim(PrimitiveType.Plane, root, new Vector3(w / 2, 0, h / 2), new Vector3(w / 10f, 1, h / 10f), g, "Ground");
            ground.layer = 0;
            ground.GetComponent<Renderer>().sharedMaterial = GroundMat(zone.id, g, w, h);

            // low fences on the edges
            var fence = new Color(0.93f, 0.86f, 0.72f); // light picket fence
            Prim(PrimitiveType.Cube, root, new Vector3(w / 2, 0.4f, -0.25f), new Vector3(w, 0.8f, 0.5f), fence);
            Prim(PrimitiveType.Cube, root, new Vector3(w / 2, 0.4f, h + 0.25f), new Vector3(w, 0.8f, 0.5f), fence);
            Prim(PrimitiveType.Cube, root, new Vector3(-0.25f, 0.4f, h / 2), new Vector3(0.5f, 0.8f, h), fence);
            Prim(PrimitiveType.Cube, root, new Vector3(w + 0.25f, 0.4f, h / 2), new Vector3(0.5f, 0.8f, h), fence);

            var portals = new List<PortalInfo>();
            foreach (var p in zone.portals)
            {
                var pos = new Vector3(p.x, 0, p.y);
                var ring = Prim(PrimitiveType.Cylinder, root, pos + Vector3.up * 0.05f, new Vector3(2.4f, 0.05f, 2.4f), new Color(1f, 0.85f, 0.2f), "Portal_" + p.to);
                Object.Destroy(ring.GetComponent<Collider>());
                Label(root, pos + Vector3.up * 2.2f, Texts.T("zone." + p.to.ToLowerInvariant()), 0.18f, Color.white);
                foreach (var side in new[] { -1.6f, 1.6f })
                    ModelLibrary.Spawn("city/flag", root, pos + new Vector3(side, 0, side * 0.3f), 2.4f, side < 0 ? 200f : 160f);
                portals.Add(new PortalInfo { To = p.to, Position = pos });
            }

            // decoration: deterministic per zone, kept away from spawn and portals
            var rnd = new System.Random(zone.id.GetHashCode());
            var spawn = new Vector3(zone.spawn.x, 0, zone.spawn.y);
            int placed = 0;
            for (int tries = 0; tries < 200 && placed < (int)(w * h / 120); tries++)
            {
                var pos = new Vector3(2 + (float)rnd.NextDouble() * (w - 4), 0, 2 + (float)rnd.NextDouble() * (h - 4));
                if (Vector3.Distance(pos, spawn) < 6) continue;
                bool nearPortal = false;
                foreach (var p in portals) if (Vector3.Distance(pos, p.Position) < 5) nearPortal = true;
                if (nearPortal) continue;
                bool nearWork = false;
                if (keepClear != null) foreach (var c in keepClear) if (Vector3.Distance(pos, c) < 10) nearWork = true;
                if (nearWork) continue;
                // Market: stalls (x 7..38) and the y=20 walkway NPC customers use come from the server, so keep
                // random houses/trees out of that area (on device a house stood in the walkway, Phase 5 QA).
                if (zone.id == "MARKET" && pos.x > 6) continue;
                // Homes (Phase 10): the street and its twelve plots are built by HomeDistrictView
                if (zone.id == "HOMES" && pos.z > 9f && pos.z < 28.5f && pos.x < 35f) continue;
                var roll = rnd.NextDouble();
                // the Countryside is fields and trees, the Industrial zone warehouses and few trees
                if (zone.id == "COUNTRYSIDE") roll *= 0.75;
                if (zone.id == "INDUSTRIAL") roll = 0.4 + roll * 0.6;
                if (roll < 0.45) Tree(root, pos, rnd);
                else if (roll < 0.6) Bushes(root, pos, rnd);
                else House(root, pos, rnd);
                placed++;
            }
            Clouds(root, w, h, rnd);
            return portals;
        }

        /// <summary>Random decoration sits in a "Deco" group so city projects (Phase 7) can make room.</summary>
        private static Transform Deco(Transform root, Vector3 pos)
        {
            var g = new GameObject("Deco").transform;
            g.SetParent(root, false);
            g.localPosition = pos;
            return g;
        }

        private static void Tree(Transform root, Vector3 pos, System.Random rnd)
        {
            var d = Deco(root, pos);
            var yaw = (float)rnd.NextDouble() * 360f;
            // a pine, or a little grove on its own lawn tile
            var tree = rnd.NextDouble() < 0.6
                ? ModelLibrary.Spawn("arena/tree", d, Vector3.zero, 3.2f + (float)rnd.NextDouble() * 1.4f, yaw)
                : ModelLibrary.SpawnScaled(rnd.NextDouble() < 0.5 ? "city/grass-trees" : "city/grass-trees-tall", d, Vector3.zero, 3.2f, yaw);
            if (tree != null)
            {
                var trunk = d.gameObject.AddComponent<CapsuleCollider>(); // solid trunk, like the primitive trees
                trunk.radius = tree.name == "tree" ? 0.4f : 1.2f;
                trunk.height = 3f;
                trunk.center = Vector3.up * 1.5f;
                return;
            }
            Prim(PrimitiveType.Cylinder, d, Vector3.up * 0.75f, new Vector3(0.3f, 0.75f, 0.3f), new Color(0.45f, 0.30f, 0.2f));
            Prim(PrimitiveType.Sphere, d, Vector3.up * 2f, new Vector3(1.6f, 1.6f, 1.6f), new Color(0.25f, 0.6f, 0.3f));
        }

        private static readonly string[] Houses = { "city/building-small-a", "city/building-small-b", "city/building-small-d", "city/building-small-a", "city/building-garage", "city/building-small-c" };

        private static void House(Transform root, Vector3 pos, System.Random rnd)
        {
            var d = Deco(root, pos);
            // Kenney houses are 1x1 tiles: scale = footprint. Turned in 90 degree steps so streets look tidy.
            var key = Houses[rnd.Next(Houses.Length)];
            if (ModelLibrary.SpawnScaled(key, d, Vector3.zero, 3.4f, rnd.Next(4) * 90f) != null)
            {
                var box = d.gameObject.AddComponent<BoxCollider>(); // houses are solid, like the primitive ones
                Vector3 min, max;
                ModelLibrary.Bounds(key, out min, out max);
                box.center = new Vector3(0, (max.y - min.y) * 3.4f / 2f, 0);
                box.size = new Vector3(3.2f, (max.y - min.y) * 3.4f, 3.2f);
                return;
            }
            var colors = new[] { new Color(1f, 0.8f, 0.7f), new Color(0.7f, 0.85f, 1f), new Color(1f, 0.95f, 0.7f), new Color(0.85f, 1f, 0.8f) };
            var body = colors[rnd.Next(colors.Length)];
            Prim(PrimitiveType.Cube, d, Vector3.up * 1.25f, new Vector3(3f, 2.5f, 3f), body);
            var roof = Prim(PrimitiveType.Cube, d, Vector3.up * 2.9f, new Vector3(2.3f, 2.3f, 3.2f), new Color(0.8f, 0.35f, 0.3f));
            roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
        }

        private static void Bushes(Transform root, Vector3 pos, System.Random rnd)
        {
            var d = Deco(root, pos);
            int n = 2 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3((float)rnd.NextDouble() * 2f - 1f, 0, (float)rnd.NextDouble() * 2f - 1f);
                if (ModelLibrary.Spawn(i % 2 == 0 ? "city/bush" : "city/bush-small", d, off, 0.7f + (float)rnd.NextDouble() * 0.5f, (float)rnd.NextDouble() * 360f) == null)
                    Prim(PrimitiveType.Sphere, d, off + Vector3.up * 0.35f, Vector3.one * 0.8f, new Color(0.3f, 0.65f, 0.35f));
            }
            foreach (var c in d.GetComponentsInChildren<Collider>()) Object.Destroy(c); // walk through bushes
        }

        /// <summary>A few slow clouds high above the zone (cheap: a handful of models, one material).</summary>
        private static void Clouds(Transform root, float w, float h, System.Random rnd)
        {
            var sky = new GameObject("Clouds").transform;
            sky.SetParent(root, false);
            for (int i = 0; i < 5; i++)
            {
                var c = ModelLibrary.Spawn("city/cloud", sky, new Vector3((float)rnd.NextDouble() * w, 14f + (float)rnd.NextDouble() * 5f, (float)rnd.NextDouble() * h + 6f), 1.6f + (float)rnd.NextDouble(), (float)rnd.NextDouble() * 360f);
                if (c == null) return;
                c.transform.localScale = Vector3.Scale(c.transform.localScale, new Vector3(4f, 1f, 2.5f));
                c.AddComponent<Drift>().Width = w;
            }
        }

        private static readonly Dictionary<string, Material> GroundMats = new Dictionary<string, Material>();

        /// <summary>
        /// Ground with soft grass/paving speckles drawn in code (no texture asset): reads as a lawn or a
        /// market square instead of a flat colour, and tiles cheaply over the whole zone.
        /// </summary>
        private static Material GroundMat(string zoneId, Color baseColor, float w, float h)
        {
            Material m;
            if (GroundMats.TryGetValue(zoneId, out m)) return m;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Ground_" + zoneId };
            var rnd = new System.Random(zoneId.GetHashCode());
            var px = new Color[size * size];
            bool paved = zoneId == "MARKET" || zoneId == "MINIGAME_GATE" || zoneId == "INDUSTRIAL";
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = baseColor * (0.94f + 0.08f * (float)rnd.NextDouble());
                    if (paved && (x % 16 == 0 || y % 16 == 0)) c *= 0.88f; // paving joints
                    px[y * size + x] = c;
                }
            for (int i = 0; i < 70; i++) // little tufts / pebbles
            {
                int x = rnd.Next(size), y = rnd.Next(size);
                var tuft = paved ? baseColor * 0.85f : Color.Lerp(baseColor, new Color(0.25f, 0.55f, 0.25f), 0.45f);
                px[y * size + x] = tuft;
                px[y * size + (x + 1) % size] = tuft;
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            m = new Material(Mat(Color.white)) { mainTexture = tex, color = Color.white, mainTextureScale = new Vector2(w / 4f, h / 4f) };
            GroundMats[zoneId] = m;
            return m;
        }

        public static TextMesh Label(Transform parent, Vector3 pos, string text, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = UI.UiKit.Font;
            tm.GetComponent<MeshRenderer>().sharedMaterial = UI.UiKit.Font.material;
            tm.fontSize = 48;
            tm.characterSize = size * 0.25f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.AddComponent<Billboard>();
            return tm;
        }
    }

    /// <summary>Moves a cloud slowly across the sky and wraps it around the zone.</summary>
    public sealed class Drift : MonoBehaviour
    {
        public float Width = 60f;
        public float Speed = 0.6f;

        private void Update()
        {
            var p = transform.localPosition;
            p.x += Speed * Time.deltaTime;
            if (p.x > Width + 10f) p.x = -10f;
            transform.localPosition = p;
        }
    }

    /// <summary>Keeps a label facing the camera.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
