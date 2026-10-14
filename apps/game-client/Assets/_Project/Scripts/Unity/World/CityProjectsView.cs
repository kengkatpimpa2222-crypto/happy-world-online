using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Phase 7: the public projects players built together stand in the city for good (park, bus stop,
    /// school, office, clock tower), and the one being built shows a scaffold that rises with progress.
    /// Built from primitives at the server's (x, y), like the rest of the city.
    /// </summary>
    public sealed class CityProjectsView : MonoBehaviour
    {
        private Zone _zone;
        private Transform _zoneRoot;
        private string _signature;
        private Transform _root;
        private Transform _clockHand;

        public static CityProjectsView Create(Transform zoneRoot, Zone zone)
        {
            var go = new GameObject("CityProjects");
            go.transform.SetParent(zoneRoot, false);
            var v = go.AddComponent<CityProjectsView>();
            v._zone = zone;
            v._zoneRoot = zoneRoot;
            return v;
        }

        /// <summary>Rebuilds only when what stands in this zone changed.</summary>
        public void Apply(CityGrowth g)
        {
            var here = CityLifePresenter.InZone(g, _zone != null ? _zone.id : null);
            var sig = "";
            foreach (var p in here) sig += p.code + ":" + p.status + ":" + CityLifePresenter.ScaffoldStage(p) + ";";
            if (sig == _signature) return;
            _signature = sig;
            if (_root != null) Destroy(_root.gameObject);
            _clockHand = null;
            _root = new GameObject("Built").transform;
            _root.SetParent(transform, false);
            foreach (var p in here)
            {
                var at = new Vector3(p.x, 0, p.y);
                ClearDecorationAround(at, 5f);
                if (p.Done) Building(p.code, at);
                else Scaffold(p, at);
            }
            foreach (var c in _root.GetComponentsInChildren<Collider>()) Destroy(c); // never blocks the player
        }

        /// <summary>Random trees / houses that happen to stand where a project goes make room for it.</summary>
        private void ClearDecorationAround(Vector3 at, float radius)
        {
            if (_zoneRoot == null) return;
            for (int i = _zoneRoot.childCount - 1; i >= 0; i--)
            {
                var c = _zoneRoot.GetChild(i);
                if (c.name != "Deco") continue;
                var p = c.localPosition;
                if (new Vector2(p.x - at.x, p.z - at.z).magnitude < radius) Destroy(c.gameObject);
            }
        }

        private GameObject P(PrimitiveType t, Vector3 pos, Vector3 scale, Color c) => CityBuilder.Prim(t, _root, pos, scale, c);

        private void Building(string code, Vector3 at)
        {
            var white = new Color(0.95f, 0.95f, 0.92f);
            switch (code)
            {
                case "PARK":
                    P(PrimitiveType.Cylinder, at + Vector3.up * 0.03f, new Vector3(8f, 0.03f, 8f), new Color(0.35f, 0.7f, 0.35f));
                    P(PrimitiveType.Cylinder, at + Vector3.up * 0.25f, new Vector3(2.2f, 0.25f, 2.2f), new Color(0.6f, 0.6f, 0.62f));
                    P(PrimitiveType.Cylinder, at + Vector3.up * 0.45f, new Vector3(1.8f, 0.05f, 1.8f), new Color(0.4f, 0.7f, 0.95f)); // fountain water
                    for (int i = 0; i < 4; i++)
                    {
                        var dir = Quaternion.Euler(0, 45 + i * 90, 0) * Vector3.forward * 3.2f;
                        P(PrimitiveType.Cylinder, at + dir + Vector3.up * 0.7f, new Vector3(0.25f, 0.7f, 0.25f), new Color(0.45f, 0.3f, 0.2f));
                        P(PrimitiveType.Sphere, at + dir + Vector3.up * 1.9f, Vector3.one * 1.4f, new Color(0.3f, 0.65f, 0.35f));
                    }
                    P(PrimitiveType.Cube, at + new Vector3(0, 0.35f, -2.2f), new Vector3(1.6f, 0.15f, 0.5f), new Color(0.6f, 0.4f, 0.25f)); // bench
                    break;
                case "BUS_STOP":
                    P(PrimitiveType.Cube, at + new Vector3(-1.2f, 1.2f, 0), new Vector3(0.12f, 2.4f, 0.12f), new Color(0.3f, 0.3f, 0.35f));
                    P(PrimitiveType.Cube, at + new Vector3(1.2f, 1.2f, 0), new Vector3(0.12f, 2.4f, 0.12f), new Color(0.3f, 0.3f, 0.35f));
                    P(PrimitiveType.Cube, at + new Vector3(0, 2.45f, 0), new Vector3(3f, 0.12f, 1.4f), new Color(0.2f, 0.55f, 0.85f));
                    P(PrimitiveType.Cube, at + new Vector3(0, 0.45f, 0.3f), new Vector3(2.2f, 0.12f, 0.5f), white);
                    P(PrimitiveType.Cylinder, at + new Vector3(1.8f, 1.3f, -0.5f), new Vector3(0.08f, 1.3f, 0.08f), Color.gray);
                    P(PrimitiveType.Cube, at + new Vector3(1.8f, 2.7f, -0.5f), new Vector3(0.7f, 0.5f, 0.06f), new Color(0.2f, 0.55f, 0.85f));
                    break;
                case "SCHOOL":
                    P(PrimitiveType.Cube, at + Vector3.up * 2f, new Vector3(8f, 4f, 4f), new Color(1f, 0.92f, 0.75f));
                    P(PrimitiveType.Cube, at + Vector3.up * 4.4f, new Vector3(8.4f, 0.4f, 4.4f), new Color(0.75f, 0.3f, 0.28f));
                    for (int i = -1; i <= 1; i++) P(PrimitiveType.Cube, at + new Vector3(i * 2.4f, 2.5f, -2.02f), new Vector3(1.2f, 1f, 0.05f), new Color(0.55f, 0.8f, 1f));
                    P(PrimitiveType.Cylinder, at + new Vector3(5f, 2.5f, -1f), new Vector3(0.1f, 2.5f, 0.1f), Color.gray);
                    P(PrimitiveType.Cube, at + new Vector3(5.5f, 4.5f, -1f), new Vector3(1f, 0.6f, 0.05f), new Color(0.9f, 0.25f, 0.3f));
                    break;
                case "OFFICE":
                    P(PrimitiveType.Cube, at + Vector3.up * 4.5f, new Vector3(5f, 9f, 5f), new Color(0.7f, 0.78f, 0.85f));
                    for (int f = 0; f < 4; f++)
                        for (int i = -1; i <= 1; i++)
                            P(PrimitiveType.Cube, at + new Vector3(i * 1.5f, 1.6f + f * 2f, -2.52f), new Vector3(1f, 1.1f, 0.05f), new Color(0.35f, 0.55f, 0.75f));
                    break;
                case "CLOCK_TOWER":
                    P(PrimitiveType.Cube, at + Vector3.up * 4f, new Vector3(2.4f, 8f, 2.4f), new Color(0.9f, 0.82f, 0.65f));
                    var roofT = P(PrimitiveType.Cube, at + Vector3.up * 8.6f, new Vector3(2f, 2f, 2f), new Color(0.7f, 0.3f, 0.25f));
                    roofT.transform.localRotation = Quaternion.Euler(45, 0, 45);
                    var face = P(PrimitiveType.Cylinder, at + new Vector3(0, 6.4f, -1.22f), new Vector3(1.6f, 0.05f, 1.6f), white);
                    face.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    var hand = P(PrimitiveType.Cube, at + new Vector3(0, 6.4f, -1.3f), new Vector3(0.1f, 0.7f, 0.05f), Color.black);
                    _clockHand = hand.transform;
                    break;
                default:
                    P(PrimitiveType.Cube, at + Vector3.up * 1.5f, new Vector3(3f, 3f, 3f), white);
                    break;
            }
        }

        private void Scaffold(CityProject p, Vector3 at)
        {
            var stage = CityLifePresenter.ScaffoldStage(p);
            var pole = new Color(0.55f, 0.55f, 0.58f);
            var plank = new Color(0.75f, 0.6f, 0.35f);
            float half = 2.2f, level = 1.4f;
            foreach (var c in new[] { new Vector3(-half, 0, -half), new Vector3(half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half) })
                P(PrimitiveType.Cube, at + c + Vector3.up * (stage * level / 2f), new Vector3(0.12f, stage * level, 0.12f), pole);
            for (int s = 1; s <= stage; s++)
            {
                P(PrimitiveType.Cube, at + new Vector3(0, s * level, -half), new Vector3(half * 2, 0.1f, 0.5f), plank);
                P(PrimitiveType.Cube, at + new Vector3(0, s * level, half), new Vector3(half * 2, 0.1f, 0.5f), plank);
            }
            // a cone and a sign with the project's name and progress
            P(PrimitiveType.Cylinder, at + new Vector3(-half - 1f, 0.3f, -half - 1f), new Vector3(0.4f, 0.3f, 0.4f), new Color(1f, 0.5f, 0.15f));
            CityBuilder.Label(_root, at + Vector3.up * (stage * level + 1.4f),
                CityLifePresenter.ProjectName(p) + "\n" + (int)(CityLifePresenter.Progress(p) * 100) + "%", 0.16f, new Color(1f, 0.85f, 0.3f));
        }

        private void Update()
        {
            if (_clockHand != null)
                _clockHand.localRotation = Quaternion.Euler(0, 0, -Time.time * 6f); // purely decorative
        }
    }
}
