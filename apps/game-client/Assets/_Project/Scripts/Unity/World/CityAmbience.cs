using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Phase 7: the city on Bangkok time. Moves the sun, colours the sky and ambient light by the hour
    /// (<see cref="CityLifePresenter"/>), lights street lamps after dark and makes it rain when the server
    /// says it rains. Everything is primitives and colour changes, cheap enough for low-end phones.
    /// </summary>
    public sealed class CityAmbience : MonoBehaviour
    {
        private Light _sun;
        private Transform _lampRoot;
        private readonly List<Renderer> _bulbs = new List<Renderer>();
        private bool? _lampsOn;
        private Transform _rainRoot;
        private readonly List<Transform> _drops = new List<Transform>();
        private const int DropCount = 90;
        private const float RainBox = 14f;
        private static Material _glowOn, _glowOff;

        /// <summary>The player: rain falls around them.</summary>
        public Transform Target;

        public static CityAmbience Create(Transform parent)
        {
            var go = new GameObject("CityAmbience");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<CityAmbience>();
            a._sun = FindAnyObjectByType<Light>();
            return a;
        }

        /// <summary>Lamp posts for a freshly built zone (old ones go with the zone root).</summary>
        public void BuildLamps(Zone zone, Transform zoneRoot)
        {
            _bulbs.Clear();
            _lampsOn = null;
            if (zone == null) return;
            _lampRoot = new GameObject("StreetLamps").transform;
            _lampRoot.SetParent(zoneRoot, false);
            float w = zone.width, h = zone.height;
            var post = new Color(0.25f, 0.27f, 0.3f);
            var spots = new List<Vector3>();
            for (float x = 8; x < w - 4; x += 12) { spots.Add(new Vector3(x, 0, 3f)); spots.Add(new Vector3(x, 0, h - 3f)); }
            for (float y = 10; y < h - 6; y += 12) { spots.Add(new Vector3(3f, 0, y)); spots.Add(new Vector3(w - 3f, 0, y)); }
            var spawn = new Vector3(zone.spawn.x, 0, zone.spawn.y);
            foreach (var s in spots)
            {
                if (Vector3.Distance(s, spawn) < 4f) continue;
                bool nearPortal = false;
                foreach (var p in zone.portals) if (Vector3.Distance(s, new Vector3(p.x, 0, p.y)) < 3.5f) nearPortal = true;
                if (nearPortal) continue;
                NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, _lampRoot, s + Vector3.up * 1.6f, new Vector3(0.12f, 1.6f, 0.12f), post, "LampPost"));
                var bulb = NoCollider(CityBuilder.Prim(PrimitiveType.Sphere, _lampRoot, s + Vector3.up * 3.35f, Vector3.one * 0.5f, Color.white, "Bulb"));
                _bulbs.Add(bulb.GetComponent<Renderer>());
            }
        }

        /// <summary>Called every frame with the city hour and the current weather.</summary>
        public void Apply(float hour, string weather)
        {
            float r, g, b;
            CityLifePresenter.Sky(hour, weather, out r, out g, out b);
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = new Color(r, g, b);
            CityLifePresenter.Ambient(hour, weather, out r, out g, out b);
            RenderSettings.ambientLight = new Color(r, g, b);
            if (_sun == null) _sun = FindAnyObjectByType<Light>();
            if (_sun != null)
            {
                var day = CityLifePresenter.Daylight(hour);
                _sun.intensity = CityLifePresenter.SunIntensity(hour, weather);
                // the sun travels east to west over the day; at night a pale moon
                var yaw = day > 0f ? Mathf.Lerp(-100f, 100f, Mathf.InverseLerp(5f, 19.5f, hour)) : -30f;
                _sun.transform.rotation = Quaternion.Euler(CityLifePresenter.SunPitch(hour), yaw, 0);
                _sun.color = day > 0f ? Color.Lerp(new Color(1f, 0.72f, 0.5f), Color.white, Mathf.Clamp01(CityLifePresenter.SunPitch(hour) / 35f)) : new Color(0.6f, 0.7f, 1f);
            }
            SetLamps(CityLifePresenter.LampsOn(hour));
            SetRain(weather == "RAIN");
        }

        private void SetLamps(bool on)
        {
            if (_lampsOn == on) return;
            _lampsOn = on;
            if (_glowOn == null)
            {
                var unlit = Shader.Find("Unlit/Color"); // in Always Included Shaders (ProjectSetup)
                _glowOn = unlit != null ? new Material(unlit) { color = new Color(1f, 0.9f, 0.55f) } : CityBuilder.Mat(new Color(1f, 0.9f, 0.55f));
                _glowOff = CityBuilder.Mat(new Color(0.85f, 0.85f, 0.8f));
            }
            foreach (var bulb in _bulbs) if (bulb != null) bulb.sharedMaterial = on ? _glowOn : _glowOff;
        }

        private void SetRain(bool raining)
        {
            if (!raining)
            {
                if (_rainRoot != null) _rainRoot.gameObject.SetActive(false);
                return;
            }
            if (_rainRoot == null)
            {
                _rainRoot = new GameObject("Rain").transform;
                _rainRoot.SetParent(transform, false);
                var c = new Color(0.75f, 0.82f, 0.95f);
                for (int i = 0; i < DropCount; i++)
                {
                    var d = NoCollider(CityBuilder.Prim(PrimitiveType.Cube, _rainRoot, RandomDrop(Vector3.zero, Random.Range(0f, 9f)), new Vector3(0.04f, 0.55f, 0.04f), c, "Drop"));
                    _drops.Add(d.transform);
                }
            }
            _rainRoot.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (_rainRoot == null || !_rainRoot.gameObject.activeSelf) return;
            var centre = Target != null ? Target.position : Vector3.zero;
            var fall = 16f * Time.deltaTime;
            foreach (var d in _drops)
            {
                var p = d.position + new Vector3(0.15f * fall, -fall, 0);
                if (p.y < 0f || Mathf.Abs(p.x - centre.x) > RainBox || Mathf.Abs(p.z - centre.z) > RainBox) p = RandomDrop(centre, 9f);
                d.position = p;
            }
        }

        private static Vector3 RandomDrop(Vector3 centre, float height) =>
            centre + new Vector3(Random.Range(-RainBox, RainBox), height + Random.Range(0f, 2f), Random.Range(-RainBox, RainBox));

        private static GameObject NoCollider(GameObject go)
        {
            Destroy(go.GetComponent<Collider>());
            return go;
        }
    }
}
