using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Phase 6: the city dresses up for the festival, more with every tier the players reach together
    /// (stage from <see cref="FestivalPresenter.DecorStage"/>): a festival gate and bunting, then lanterns,
    /// then twinkling lights, then fireworks. Built from primitives like the rest of the city.
    /// </summary>
    public sealed class FestivalDecor : MonoBehaviour
    {
        private Zone _zone;
        private int _stage = -1;
        private string _key;
        private Color _color = new Color(1f, 0.84f, 0.31f);
        private Transform _root;
        private readonly List<Transform> _lanterns = new List<Transform>();
        private readonly List<Renderer> _lights = new List<Renderer>();
        private float _nextFirework;

        private static readonly Color[] Party =
        {
            new Color(0.95f, 0.35f, 0.35f), new Color(1f, 0.8f, 0.25f), new Color(0.35f, 0.75f, 0.95f),
            new Color(0.45f, 0.85f, 0.45f), new Color(0.85f, 0.5f, 0.95f),
        };

        public static FestivalDecor Create(Transform zoneRoot, Zone zone)
        {
            var go = new GameObject("FestivalDecor");
            go.transform.SetParent(zoneRoot, false);
            var d = go.AddComponent<FestivalDecor>();
            d._zone = zone;
            return d;
        }

        /// <summary>Rebuilds only when the festival or its stage changed.</summary>
        public void Apply(FestivalInfo f)
        {
            var stage = FestivalPresenter.DecorStage(f);
            var key = f != null ? f.key : null;
            if (stage == _stage && key == _key) return;
            _stage = stage;
            _key = key;
            if (f != null)
            {
                float r, g, b;
                FestivalPresenter.ParseColor(f.color, out r, out g, out b);
                _color = new Color(r, g, b);
            }
            Rebuild(f);
        }

        /// <summary>The city just reached a tier: a big burst right now.</summary>
        public void Celebrate(int tier)
        {
            var c = _zone != null ? new Vector3(_zone.width / 2f, 0, _zone.height / 2f) : Vector3.zero;
            for (int i = 0; i < 2 + tier; i++)
                Firework.Launch(transform, c + new Vector3(Random.Range(-8f, 8f), 0, Random.Range(-8f, 8f)), Party[Random.Range(0, Party.Length)], i * 0.35f);
        }

        private void Rebuild(FestivalInfo f)
        {
            if (_root != null) Destroy(_root.gameObject);
            _lanterns.Clear();
            _lights.Clear();
            if (_stage <= 0 || _zone == null) return;
            _root = new GameObject("Stage" + _stage).transform;
            _root.SetParent(transform, false);
            float w = _zone.width, h = _zone.height;

            // stage 1: a festival gate at the spawn point and bunting along the fences
            var spawn = new Vector3(_zone.spawn.x, 0, _zone.spawn.y);
            var gateAt = spawn + (spawn.z > h / 2 ? Vector3.back : Vector3.forward) * 3f;
            CityBuilder.Prim(PrimitiveType.Cube, _root, gateAt + new Vector3(-3f, 2f, 0), new Vector3(0.35f, 4f, 0.35f), _color);
            CityBuilder.Prim(PrimitiveType.Cube, _root, gateAt + new Vector3(3f, 2f, 0), new Vector3(0.35f, 4f, 0.35f), _color);
            CityBuilder.Prim(PrimitiveType.Cube, _root, gateAt + new Vector3(0, 4.1f, 0), new Vector3(7f, 0.7f, 0.4f), Color.white);
            CityBuilder.Label(_root, gateAt + new Vector3(0, 4.9f, 0), FestivalPresenter.Name(f), 0.2f, _color);
            Strip(_root);

            var poles = new List<Vector3>();
            for (float x = 4; x < w - 2; x += 8) { poles.Add(new Vector3(x, 0, 1.2f)); poles.Add(new Vector3(x, 0, h - 1.2f)); }
            for (float y = 6; y < h - 4; y += 8) { poles.Add(new Vector3(1.2f, 0, y)); poles.Add(new Vector3(w - 1.2f, 0, y)); }
            foreach (var p in poles) CityBuilder.Prim(PrimitiveType.Cylinder, _root, p + Vector3.up * 1.5f, new Vector3(0.12f, 1.5f, 0.12f), new Color(0.5f, 0.38f, 0.28f));
            Bunting(new Vector3(4, 0, 1.2f), new Vector3(w - 4, 0, 1.2f));
            Bunting(new Vector3(4, 0, h - 1.2f), new Vector3(w - 4, 0, h - 1.2f));
            Bunting(new Vector3(1.2f, 0, 6), new Vector3(1.2f, 0, h - 6));
            Bunting(new Vector3(w - 1.2f, 0, 6), new Vector3(w - 1.2f, 0, h - 6));

            // stage 2: lanterns on every pole
            if (_stage >= 2)
                foreach (var p in poles)
                {
                    var l = CityBuilder.Prim(PrimitiveType.Sphere, _root, p + Vector3.up * 3.3f, new Vector3(0.6f, 0.75f, 0.6f), _color, "Lantern");
                    _lanterns.Add(l.transform);
                }

            // stage 3: twinkling lights strung across the zone
            if (_stage >= 3)
            {
                for (float x = 3; x < w - 2; x += 1.5f)
                {
                    var bulb = CityBuilder.Prim(PrimitiveType.Sphere, _root, new Vector3(x, 3.6f + Mathf.Sin(x * 0.4f) * 0.3f, h / 2f), Vector3.one * 0.25f, Party[(int)x % Party.Length], "Light");
                    _lights.Add(bulb.GetComponent<Renderer>());
                }
            }
            Strip(_root);
            _nextFirework = Time.time + 1f;
        }

        private void Bunting(Vector3 from, Vector3 to)
        {
            var len = Vector3.Distance(from, to);
            int n = Mathf.Max(2, (int)(len / 1.2f));
            for (int i = 0; i <= n; i++)
            {
                var pos = Vector3.Lerp(from, to, i / (float)n) + Vector3.up * (2.8f - Mathf.Sin(i / (float)n * Mathf.PI * 4) * 0.25f);
                var flag = CityBuilder.Prim(PrimitiveType.Cube, _root, pos, new Vector3(0.45f, 0.45f, 0.05f), Party[i % Party.Length], "Flag");
                flag.transform.localRotation = Quaternion.LookRotation(to - from) * Quaternion.Euler(0, 90, 45);
            }
        }

        /// <summary>Decorations are not tappable and never block the player.</summary>
        private static void Strip(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>()) Destroy(c);
        }

        private void Update()
        {
            if (_stage <= 0) return;
            var t = Time.time;
            for (int i = 0; i < _lanterns.Count; i++)
                if (_lanterns[i] != null) _lanterns[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f + i) * 8f, 0, Mathf.Cos(t * 1.1f + i) * 8f);
            if (_lights.Count > 0 && Mathf.FloorToInt(t * 2f) % 2 == 0)
            {
                int k = Mathf.FloorToInt(t * 2f);
                for (int i = 0; i < _lights.Count; i++)
                    if (_lights[i] != null) _lights[i].enabled = (i + k) % 3 != 0;
            }
            if (_stage >= 4 && t >= _nextFirework && _zone != null)
            {
                _nextFirework = t + Random.Range(2.5f, 5f);
                Firework.Launch(transform, new Vector3(Random.Range(5f, _zone.width - 5f), 0, Random.Range(5f, _zone.height - 5f)), Party[Random.Range(0, Party.Length)], 0f);
            }
        }
    }

    /// <summary>A rocket that climbs, bursts into sparks and fades. Purely visual.</summary>
    public sealed class Firework : MonoBehaviour
    {
        private Color _color;
        private float _born, _delay;
        private Transform _rocket;
        private readonly List<Transform> _sparks = new List<Transform>();
        private readonly List<Vector3> _velocity = new List<Vector3>();
        private bool _burst;
        private const float Climb = 1.1f, Life = 2.4f;

        public static void Launch(Transform parent, Vector3 ground, Color color, float delay)
        {
            var go = new GameObject("Firework");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ground;
            var f = go.AddComponent<Firework>();
            f._color = color;
            f._born = Time.time;
            f._delay = delay;
            f._rocket = NoCollider(CityBuilder.Prim(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.25f, Color.white, "Rocket")).transform;
            f._rocket.gameObject.SetActive(false);
        }

        private static GameObject NoCollider(GameObject go)
        {
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private void Update()
        {
            var age = Time.time - _born - _delay;
            if (age < 0) return;
            if (!_burst)
            {
                _rocket.gameObject.SetActive(true);
                _rocket.localPosition = Vector3.up * Mathf.Lerp(0.5f, 10f, age / Climb);
                if (age < Climb) return;
                _burst = true;
                var at = _rocket.localPosition;
                Destroy(_rocket.gameObject);
                for (int i = 0; i < 14; i++)
                {
                    var dir = Random.onUnitSphere;
                    var s = NoCollider(CityBuilder.Prim(PrimitiveType.Sphere, transform, at, Vector3.one * 0.3f, _color, "Spark")).transform;
                    _sparks.Add(s);
                    _velocity.Add(dir * Random.Range(3f, 5f));
                }
                return;
            }
            var k = (age - Climb) / (Life - Climb);
            for (int i = 0; i < _sparks.Count; i++)
            {
                _velocity[i] += Vector3.down * 3f * Time.deltaTime;
                _sparks[i].localPosition += _velocity[i] * Time.deltaTime;
                _sparks[i].localScale = Vector3.one * Mathf.Lerp(0.3f, 0.02f, k);
            }
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
