using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Builds a zone from GET /city data with primitives, so the game runs with no art assets.
    /// Server (x, y) maps to Unity (x, 0, y). Swap the primitives for real prefabs later; layout stays server-driven.
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

        public static List<PortalInfo> Build(Zone zone, Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
            float w = zone.width, h = zone.height;
            Color g;
            if (!Ground.TryGetValue(zone.id, out g)) g = Color.gray;

            // ground (Unity plane is 10x10)
            var ground = Prim(PrimitiveType.Plane, root, new Vector3(w / 2, 0, h / 2), new Vector3(w / 10f, 1, h / 10f), g, "Ground");
            ground.layer = 0;

            // low fences on the edges
            var fence = new Color(0.55f, 0.40f, 0.28f);
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
                // Market: stalls (x 7..38) and the y=20 walkway NPC customers use come from the server, so keep
                // random houses/trees out of that area (on device a house stood in the walkway, Phase 5 QA).
                if (zone.id == "MARKET" && pos.x > 6) continue;
                if (rnd.NextDouble() < 0.6) Tree(root, pos);
                else House(root, pos, rnd);
                placed++;
            }
            return portals;
        }

        private static void Tree(Transform root, Vector3 pos)
        {
            Prim(PrimitiveType.Cylinder, root, pos + Vector3.up * 0.75f, new Vector3(0.3f, 0.75f, 0.3f), new Color(0.45f, 0.30f, 0.2f));
            Prim(PrimitiveType.Sphere, root, pos + Vector3.up * 2f, new Vector3(1.6f, 1.6f, 1.6f), new Color(0.25f, 0.6f, 0.3f));
        }

        private static void House(Transform root, Vector3 pos, System.Random rnd)
        {
            var colors = new[] { new Color(1f, 0.8f, 0.7f), new Color(0.7f, 0.85f, 1f), new Color(1f, 0.95f, 0.7f), new Color(0.85f, 1f, 0.8f) };
            var body = colors[rnd.Next(colors.Length)];
            Prim(PrimitiveType.Cube, root, pos + Vector3.up * 1.25f, new Vector3(3f, 2.5f, 3f), body);
            var roof = Prim(PrimitiveType.Cube, root, pos + Vector3.up * 2.9f, new Vector3(2.3f, 2.3f, 3.2f), new Color(0.8f, 0.35f, 0.3f));
            roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
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
