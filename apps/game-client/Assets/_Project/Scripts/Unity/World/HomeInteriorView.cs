using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// The inside of a home (Phase 10): wooden floor with a faint grid, three walls, the door wall with an opening,
    /// and the furniture. Grid cell (x, y) is HomePresenter.CellSize units wide at Unity (x, 0, y), so the server's
    /// room size (interior.width/height) and the walking bounds are the same numbers.
    /// While decorating it also draws the grid and a green/red ghost of the piece in hand.
    /// </summary>
    public sealed class HomeInteriorView : MonoBehaviour
    {
        private const float Cell = HomePresenter.CellSize;
        private Transform _furniture, _ghost, _gridLines;
        private int _gridW, _gridH;
        private string _ghostKey;
        public Vector3 ExitPoint { get; private set; }
        public Vector3 Center => new Vector3(_gridW * Cell / 2f, 0, _gridH * Cell / 2f);

        public static HomeInteriorView Create(Transform root, int gridW, int gridH, string title)
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            var go = new GameObject("HomeInterior");
            go.transform.SetParent(root, false);
            var v = go.AddComponent<HomeInteriorView>();
            v.Build(gridW, gridH, title);
            return v;
        }

        private void Build(int gw, int gh, string title)
        {
            _gridW = gw;
            _gridH = gh;
            float w = gw * Cell, h = gh * Cell;
            var floor = CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, -0.05f, h / 2), new Vector3(w, 0.1f, h), new Color(0.80f, 0.64f, 0.46f), "Floor");
            floor.GetComponent<Renderer>().sharedMaterial = FloorMat(gw, gh);
            // a little garden around the house so the room doesn't float in the sky colour
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, -0.12f, h / 2 - 2f), new Vector3(w + 8f, 0.1f, h + 8f), new Color(0.55f, 0.78f, 0.45f), "Garden");

            var wall = new Color(0.98f, 0.93f, 0.84f);
            const float wallH = 2.6f, t = 0.25f;
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, wallH / 2, h + t / 2), new Vector3(w + 2 * t, wallH, t), wall, "WallBack");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(-t / 2, wallH / 2, h / 2), new Vector3(t, wallH, h), wall, "WallLeft");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w + t / 2, wallH / 2, h / 2), new Vector3(t, wallH, h), wall, "WallRight");
            // front wall: low (the camera looks over it) with a door gap in the middle
            const float door = 1.8f, low = 0.6f;
            var side = (w - door) / 2f;
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(side / 2, low / 2, -t / 2), new Vector3(side, low, t), wall, "WallFrontL");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w - side / 2, low / 2, -t / 2), new Vector3(side, low, t), wall, "WallFrontR");
            var mat = CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, 0.01f, -0.4f), new Vector3(door, 0.02f, 0.8f), new Color(0.85f, 0.35f, 0.3f), "DoorMat");
            Destroy(mat.GetComponent<Collider>());
            ExitPoint = new Vector3(w / 2, 0, 0.3f);
            CityBuilder.Label(transform, new Vector3(w / 2, 3.4f, h), title, 0.22f, new Color(0.35f, 0.25f, 0.2f));
            CityBuilder.Label(transform, new Vector3(w / 2, 1.2f, -0.6f), "ทางออก", 0.14f, Color.white);

            // soft warm light so the room reads cosy at night too
            var lamp = new GameObject("RoomLight").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(w / 2, 4f, h / 2);
            lamp.type = LightType.Point;
            lamp.range = Mathf.Max(w, h) * 1.2f;
            lamp.intensity = 0.7f;
            lamp.color = new Color(1f, 0.92f, 0.8f);

            _furniture = new GameObject("Furniture").transform;
            _furniture.SetParent(transform, false);
        }

        /// <summary>Redraws the furniture standing in the room.</summary>
        public void ShowFurniture(IEnumerable<HomeLayoutEditor.Piece> pieces)
        {
            for (int i = _furniture.childCount - 1; i >= 0; i--) Destroy(_furniture.GetChild(i).gameObject);
            foreach (var p in pieces) Place(_furniture, p, null);
        }

        public void ShowFurniture(IEnumerable<HomePiece> pieces)
        {
            var list = new List<HomeLayoutEditor.Piece>();
            foreach (var f in pieces) list.Add(HomePresenter.ToPiece(f));
            ShowFurniture(list);
        }

        /// <summary>Decorate mode: grid lines on/off.</summary>
        public void SetGrid(bool on)
        {
            if (!on)
            {
                if (_gridLines != null) Destroy(_gridLines.gameObject);
                _gridLines = null;
                return;
            }
            if (_gridLines != null) return;
            _gridLines = new GameObject("Grid").transform;
            _gridLines.SetParent(transform, false);
            var c = new Color(1f, 1f, 1f, 1f) * 0.92f;
            for (int x = 0; x <= _gridW; x++)
            {
                var l = CityBuilder.Prim(PrimitiveType.Cube, _gridLines, new Vector3(x * Cell, 0.012f, _gridH * Cell / 2), new Vector3(0.04f, 0.01f, _gridH * Cell), c);
                Destroy(l.GetComponent<Collider>());
            }
            for (int y = 0; y <= _gridH; y++)
            {
                var l = CityBuilder.Prim(PrimitiveType.Cube, _gridLines, new Vector3(_gridW * Cell / 2, 0.012f, y * Cell), new Vector3(_gridW * Cell, 0.01f, 0.04f), c);
                Destroy(l.GetComponent<Collider>());
            }
        }

        /// <summary>The piece in hand: drawn where it would go, on a green (fits) or red (doesn't) pad.</summary>
        public void ShowGhost(HomeLayoutEditor.Piece held, bool fits)
        {
            var key = held == null ? null : held.ItemId + ":" + held.X + ":" + held.Y + ":" + held.Rotation + ":" + fits;
            if (key == _ghostKey) return;
            _ghostKey = key;
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            if (held == null) return;
            _ghost = new GameObject("Ghost").transform;
            _ghost.SetParent(transform, false);
            var pad = CityBuilder.Prim(PrimitiveType.Cube, _ghost, Vector3.zero, Vector3.one, fits ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.35f, 0.35f), "Pad");
            HomePresenter.PieceCenter(held, out var cx, out var cz);
            pad.transform.localPosition = new Vector3(cx, 0.03f, cz);
            pad.transform.localScale = new Vector3(held.W * Cell - 0.08f, 0.04f, held.H * Cell - 0.08f);
            Destroy(pad.GetComponent<Collider>());
            var model = Place(_ghost, held, null);
            model.transform.localPosition += Vector3.up * 0.25f; // lifted: it's in your hand
        }

        /// <summary>Grid cell under a screen point (tap/mouse), false when the point is not on the floor.</summary>
        public bool CellUnder(Camera cam, Vector3 screenPoint, out int cx, out int cy)
        {
            cx = cy = -1;
            var ray = cam.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, transform.position);
            if (!plane.Raycast(ray, out var d)) return false;
            var p = transform.InverseTransformPoint(ray.GetPoint(d));
            HomePresenter.CellAt(p.x, p.z, out cx, out cy);
            return cx >= 0 && cy >= 0 && cx < _gridW && cy < _gridH;
        }

        private static GameObject Place(Transform parent, HomeLayoutEditor.Piece p, string name)
        {
            HomePresenter.PieceCenter(p, out var x, out var z);
            var go = new GameObject(name ?? p.ItemId);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, 0, z);
            // models are drawn facing +z (toward the back wall) at rotation 0; turning is clockwise seen from above
            go.transform.localRotation = Quaternion.Euler(0, p.Rotation, 0);
            FurnitureModels.Build(go.transform, p.ItemId, p.FootW * Cell, p.FootH * Cell);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c); // the avatar walks around by grid, never gets stuck
            return go;
        }

        private static Material _floorMat;

        private static Material FloorMat(int gw, int gh)
        {
            if (_floorMat != null) return _floorMat;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "WoodFloor" };
            var rnd = new System.Random(7);
            var px = new Color[size * size];
            var wood = new Color(0.80f, 0.64f, 0.46f);
            for (int y = 0; y < size; y++)
            {
                var board = (y / 16) % 2 == 0 ? 1f : 0.94f;
                for (int x = 0; x < size; x++)
                {
                    var c = wood * board * (0.95f + 0.06f * (float)rnd.NextDouble());
                    if (y % 16 == 0) c *= 0.82f; // board joints
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            _floorMat = new Material(CityBuilder.Mat(Color.white)) { mainTexture = tex, color = Color.white, mainTextureScale = new Vector2(gw / 2f, gh / 2f) };
            return _floorMat;
        }
    }

    /// <summary>
    /// Furniture drawn from primitives (no furniture art in the bundled kits yet). Each builder fills a
    /// footprint of w x d world units centred on the parent, facing +z.
    /// </summary>
    public static class FurnitureModels
    {
        private static GameObject P(Transform t, PrimitiveType k, float x, float y, float z, float sx, float sy, float sz, Color c) =>
            CityBuilder.Prim(k, t, new Vector3(x, y, z), new Vector3(sx, sy, sz), c);

        public static void Build(Transform t, string itemId, float w, float d)
        {
            var wood = new Color(0.62f, 0.42f, 0.26f);
            switch (itemId)
            {
                case "furn_wood_chair":
                    P(t, PrimitiveType.Cube, 0, 0.45f, 0, w * 0.55f, 0.08f, d * 0.55f, wood);
                    P(t, PrimitiveType.Cube, 0, 0.8f, d * 0.25f, w * 0.55f, 0.65f, 0.08f, wood);
                    foreach (var sx in new[] { -1, 1 })
                        foreach (var sz in new[] { -1, 1 })
                            P(t, PrimitiveType.Cube, sx * w * 0.22f, 0.22f, sz * d * 0.22f, 0.07f, 0.44f, 0.07f, wood * 0.85f);
                    break;
                case "furn_wood_table":
                    P(t, PrimitiveType.Cube, 0, 0.75f, 0, w * 0.9f, 0.1f, d * 0.8f, wood);
                    foreach (var sx in new[] { -1, 1 })
                        foreach (var sz in new[] { -1, 1 })
                            P(t, PrimitiveType.Cube, sx * w * 0.4f, 0.36f, sz * d * 0.33f, 0.1f, 0.72f, 0.1f, wood * 0.85f);
                    break;
                case "furn_plant_pot":
                    P(t, PrimitiveType.Cylinder, 0, 0.25f, 0, w * 0.4f, 0.25f, d * 0.4f, new Color(0.78f, 0.42f, 0.3f));
                    P(t, PrimitiveType.Sphere, 0, 0.8f, 0, w * 0.6f, 0.7f, d * 0.6f, new Color(0.3f, 0.65f, 0.35f));
                    break;
                case "furn_single_bed":
                    P(t, PrimitiveType.Cube, 0, 0.2f, 0, w * 0.9f, 0.4f, d * 0.95f, wood);
                    P(t, PrimitiveType.Cube, 0, 0.45f, -d * 0.04f, w * 0.84f, 0.14f, d * 0.82f, new Color(0.55f, 0.75f, 0.95f));
                    P(t, PrimitiveType.Cube, 0, 0.55f, d * 0.36f, w * 0.6f, 0.14f, d * 0.12f, Color.white);
                    P(t, PrimitiveType.Cube, 0, 0.7f, d * 0.46f, w * 0.9f, 0.9f, 0.1f, wood * 0.9f);
                    break;
                case "furn_sofa_cloud":
                    var cloud = new Color(0.93f, 0.96f, 1f);
                    P(t, PrimitiveType.Cube, 0, 0.3f, 0, w * 0.92f, 0.5f, d * 0.8f, cloud);
                    P(t, PrimitiveType.Cube, 0, 0.75f, d * 0.3f, w * 0.92f, 0.6f, d * 0.22f, cloud);
                    foreach (var sx in new[] { -1, 1 }) P(t, PrimitiveType.Sphere, sx * w * 0.42f, 0.6f, 0, 0.5f, 0.5f, d * 0.8f, new Color(0.75f, 0.85f, 1f));
                    break;
                case "furn_fest_beach_umbrella":
                    P(t, PrimitiveType.Cylinder, 0, 1f, 0, 0.08f, 1f, 0.08f, Color.white);
                    P(t, PrimitiveType.Cylinder, 0, 2f, 0, w * 0.95f, 0.08f, d * 0.95f, new Color(1f, 0.55f, 0.35f));
                    break;
                case "furn_fest_bread_oven":
                    P(t, PrimitiveType.Cube, 0, 0.5f, 0, w * 0.8f, 1f, d * 0.8f, new Color(0.75f, 0.5f, 0.35f));
                    P(t, PrimitiveType.Cube, 0, 0.45f, -d * 0.41f, w * 0.45f, 0.35f, 0.04f, new Color(0.2f, 0.12f, 0.08f));
                    break;
                case "furn_fest_flower_arch":
                    foreach (var sx in new[] { -1, 1 }) P(t, PrimitiveType.Cube, sx * w * 0.42f, 1f, 0, 0.12f, 2f, 0.12f, Color.white);
                    P(t, PrimitiveType.Cube, 0, 2f, 0, w * 0.95f, 0.14f, 0.2f, Color.white);
                    for (int i = 0; i < 5; i++) P(t, PrimitiveType.Sphere, -w * 0.4f + i * w * 0.2f, 2.12f, 0, 0.3f, 0.3f, 0.3f, i % 2 == 0 ? new Color(1f, 0.6f, 0.75f) : new Color(1f, 0.85f, 0.3f));
                    break;
                case "furn_fest_food_cart":
                    P(t, PrimitiveType.Cube, 0, 0.7f, 0, w * 0.85f, 0.6f, d * 0.7f, new Color(0.95f, 0.75f, 0.3f));
                    P(t, PrimitiveType.Cube, 0, 1.6f, 0, w * 0.9f, 0.08f, d * 0.8f, new Color(0.9f, 0.3f, 0.3f));
                    foreach (var sx in new[] { -1, 1 }) P(t, PrimitiveType.Cylinder, sx * w * 0.3f, 0.25f, -d * 0.36f, 0.45f, 0.04f, 0.45f, new Color(0.25f, 0.25f, 0.28f)).transform.localRotation = Quaternion.Euler(90, 0, 0);
                    break;
                case "furn_fest_lantern":
                    P(t, PrimitiveType.Cylinder, 0, 0.8f, 0, 0.06f, 0.8f, 0.06f, wood);
                    P(t, PrimitiveType.Sphere, 0, 1.7f, 0, 0.6f, 0.75f, 0.6f, new Color(1f, 0.45f, 0.3f));
                    break;
                default:
                    P(t, PrimitiveType.Cube, 0, 0.4f, 0, w * 0.8f, 0.8f, d * 0.8f, new Color(0.7f, 0.7f, 0.9f));
                    break;
            }
        }
    }
}
