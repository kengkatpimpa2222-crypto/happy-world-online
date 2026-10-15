using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using HappyWorld.Unity.Art;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Phase 10: the residential street in the HOMES zone, from GET /homes/district. A road runs east-west from the
    /// plaza portal; twelve plots face it. Taken plots get a house and a sign with the owner's name (friends get a
    /// heart, mine stands out), free plots a little fence. Each house has a doormat on the road side where the
    /// player stands to go in. Positions come from the server (shared-contracts plotPosition).
    /// </summary>
    public sealed class HomeDistrictView : MonoBehaviour
    {
        private Transform _plots;
        private string _signature;
        public HomeDistrict District { get; private set; }

        public static HomeDistrictView Create(Transform zoneRoot, Zone zone)
        {
            var go = new GameObject("District");
            go.transform.SetParent(zoneRoot, false);
            var v = go.AddComponent<HomeDistrictView>();
            v.BuildRoad(zone);
            return v;
        }

        private void BuildRoad(Zone zone)
        {
            var asphalt = new Color(0.42f, 0.42f, 0.46f);
            var road = CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(zone.width / 2f, 0.01f, 18.5f), new Vector3(zone.width, 0.02f, 4f), asphalt, "Road");
            Destroy(road.GetComponent<Collider>());
            for (float x = 2f; x < zone.width - 1f; x += 3f)
            {
                var dash = CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(x, 0.025f, 18.5f), new Vector3(1.4f, 0.01f, 0.15f), Color.white);
                Destroy(dash.GetComponent<Collider>());
            }
            foreach (var z in new[] { 16.1f, 20.9f })
            {
                var walk = CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(zone.width / 2f, 0.02f, z), new Vector3(zone.width, 0.04f, 0.8f), new Color(0.85f, 0.83f, 0.78f), "Sidewalk");
                Destroy(walk.GetComponent<Collider>());
            }
            _plots = new GameObject("Plots").transform;
            _plots.SetParent(transform, false);
        }

        /// <summary>Rebuilds the houses when who lives where (or who may visit) changed.</summary>
        public void Apply(HomeDistrict d)
        {
            if (d == null) return;
            District = d;
            var sig = d.street + "|";
            foreach (var p in d.plots) sig += p.plotNumber + ":" + p.taken + ":" + (p.owner != null ? p.owner.displayName : "") + ":" + p.isFriend + ":" + p.canVisit + ";";
            if (sig == _signature) return;
            _signature = sig;
            for (int i = _plots.childCount - 1; i >= 0; i--) Destroy(_plots.GetChild(i).gameObject);
            foreach (var p in d.plots) Plot(p);
        }

        private static readonly string[] HouseModels = { "city/building-small-a", "city/building-small-b", "city/building-small-c", "city/building-small-d" };

        private void Plot(DistrictPlot p)
        {
            var g = new GameObject("Plot_" + p.plotNumber).transform;
            g.SetParent(_plots, false);
            g.localPosition = new Vector3(p.x, 0, p.y);
            var faceRoad = p.facing == "N" ? 0f : 180f; // houses look at the road

            var lawn = CityBuilder.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.015f, 0), new Vector3(4.6f, 0.03f, 4.6f), p.isMine ? new Color(0.98f, 0.86f, 0.55f) : new Color(0.5f, 0.75f, 0.42f), "Lawn");
            Destroy(lawn.GetComponent<Collider>());

            if (!p.taken)
            {
                for (int i = 0; i < 4; i++)
                {
                    var side = Quaternion.Euler(0, i * 90, 0);
                    var rail = CityBuilder.Prim(PrimitiveType.Cube, g, side * new Vector3(0, 0.3f, 2.2f), new Vector3(4.4f, 0.12f, 0.12f), new Color(0.93f, 0.86f, 0.72f));
                    rail.transform.localRotation = side;
                    Destroy(rail.GetComponent<Collider>());
                }
                CityBuilder.Label(g, Vector3.up * 1.2f, HomePresenter.PlotSign(p), 0.14f, new Color(0.3f, 0.3f, 0.3f));
                return;
            }

            var key = HouseModels[Mathf.Abs(p.plotNumber) % HouseModels.Length];
            var house = ModelLibrary.SpawnScaled(key, g, Vector3.zero, 3.4f, faceRoad);
            if (house == null)
            {
                var body = p.isMine ? new Color(1f, 0.85f, 0.6f) : new Color(0.8f, 0.9f, 1f);
                CityBuilder.Prim(PrimitiveType.Cube, g, Vector3.up * 1.25f, new Vector3(3f, 2.5f, 3f), body);
                var roof = CityBuilder.Prim(PrimitiveType.Cube, g, Vector3.up * 2.9f, new Vector3(2.3f, 2.3f, 3.2f), new Color(0.8f, 0.35f, 0.3f));
                roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
            }
            else
            {
                var box = g.gameObject.AddComponent<BoxCollider>(); // solid like the other houses
                box.center = Vector3.up * 1.5f;
                box.size = new Vector3(3.2f, 3f, 3.2f);
            }

            var color = p.isMine ? new Color(1f, 0.55f, 0.2f) : p.isFriend ? new Color(0.95f, 0.35f, 0.55f) : new Color(0.25f, 0.25f, 0.3f);
            CityBuilder.Label(g, Vector3.up * 4.4f, HomePresenter.PlotSign(p), 0.17f, color);

            // doormat on the road side: green when I may go in
            var door = new Vector3(p.door.x - p.x, 0.03f, p.door.y - p.y);
            var mat = CityBuilder.Prim(PrimitiveType.Cube, g, door, new Vector3(1.4f, 0.03f, 0.9f), p.canVisit ? new Color(0.35f, 0.8f, 0.45f) : new Color(0.7f, 0.7f, 0.7f), "DoorMat");
            Destroy(mat.GetComponent<Collider>());
        }

        /// <summary>The plot whose doormat the player stands on (within <paramref name="radius"/>), or null.</summary>
        public DistrictPlot DoorNear(Vector2 pos, float radius = 1.4f)
        {
            if (District == null) return null;
            DistrictPlot best = null;
            var bestD = radius;
            foreach (var p in District.plots)
            {
                if (!p.taken || p.door == null) continue;
                var d = Vector2.Distance(pos, new Vector2(p.door.x, p.door.y));
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
