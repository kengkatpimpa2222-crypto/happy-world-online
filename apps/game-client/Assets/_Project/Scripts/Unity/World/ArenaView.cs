using System.Collections;
using System.Collections.Generic;
using HappyWorld.Core.World;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// The Splash Arena (Phase 13): a fenced lawn with a red and a blue base, the fighters with a team ring under
    /// their feet, and water balloons that arc from thrower to target and splash. Server (x, y) is Unity (x, 0, y),
    /// like the city. Purely visual: hits, soaks and the score come from the server through <see cref="ArenaMatch"/>.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        public static readonly Color Red = new Color(0.89f, 0.33f, 0.33f);
        public static readonly Color Blue = new Color(0.24f, 0.55f, 0.99f);
        private static readonly Color Water = new Color(0.55f, 0.85f, 1f, 0.8f);

        private readonly Dictionary<string, AvatarView> _views = new Dictionary<string, AvatarView>();
        private readonly Dictionary<string, TextMesh> _status = new Dictionary<string, TextMesh>();
        private Transform _players;
        private Transform _me;
        private ArenaMatch _match;

        public Vector3 Center { get; private set; }

        public static ArenaView Create(Transform root, ArenaMatch match, Transform localPlayer)
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            var go = new GameObject("SplashArena");
            go.transform.SetParent(root, false);
            var v = go.AddComponent<ArenaView>();
            v._match = match;
            v._me = localPlayer;
            v.Build(match.Field.width, match.Field.height);
            v.SpawnFighters();
            return v;
        }

        private void Build(float w, float h)
        {
            Center = new Vector3(w / 2, 0, h / 2);
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, -0.05f, h / 2), new Vector3(w, 0.1f, h), new Color(0.56f, 0.82f, 0.47f), "Lawn");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, -0.12f, h / 2), new Vector3(w + 14f, 0.1f, h + 14f), new Color(0.93f, 0.86f, 0.66f), "Sand");
            NoCollider(CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, 0.01f, h / 2), new Vector3(0.15f, 0.02f, h), Color.white, "MidLine"));
            Base(new Vector3(3, 0, h / 2), Red, "ฐานทีมแดง");
            Base(new Vector3(w - 3, 0, h / 2), Blue, "ฐานทีมฟ้า");

            // a low picket fence just outside the field (the server keeps everyone inside anyway)
            var fence = new Color(1f, 0.97f, 0.9f);
            const float t = 0.15f, fh = 0.6f;
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, fh / 2, -t), new Vector3(w + 2 * t, fh, t), fence, "FenceS");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w / 2, fh / 2, h + t), new Vector3(w + 2 * t, fh, t), fence, "FenceN");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(-t, fh / 2, h / 2), new Vector3(t, fh, h), fence, "FenceW");
            CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(w + t, fh / 2, h / 2), new Vector3(t, fh, h), fence, "FenceE");

            // decoration outside the fence: water tanks and beach umbrellas
            for (int i = 0; i < 4; i++)
            {
                var x = w * (0.2f + 0.2f * i);
                NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, transform, new Vector3(x, 0.6f, h + 2.5f), new Vector3(1.2f, 0.6f, 1.2f), new Color(0.4f, 0.75f, 0.95f), "Tank"));
                NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, transform, new Vector3(x + 2f, 1.1f, -2.5f), new Vector3(0.08f, 1.1f, 0.08f), Color.white, "Pole"));
                NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, transform, new Vector3(x + 2f, 2.2f, -2.5f), new Vector3(1.8f, 0.05f, 1.8f), i % 2 == 0 ? Red : Blue, "Umbrella"));
            }
            CityBuilder.Label(transform, new Vector3(w / 2, 3.2f, h + 1.5f), "ศึกลูกโป่งน้ำ", 0.25f, new Color(0.2f, 0.45f, 0.75f));

            _players = new GameObject("Fighters").transform;
            _players.SetParent(transform, false);
        }

        private void Base(Vector3 at, Color c, string label)
        {
            NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, transform, at + new Vector3(0, 0.02f, 0), new Vector3(4f, 0.02f, 4f), c * 0.9f + Color.white * 0.1f, "Base"));
            NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, transform, at + new Vector3(0, 1.5f, 3.2f), new Vector3(0.1f, 1.5f, 0.1f), Color.white, "FlagPole"));
            NoCollider(CityBuilder.Prim(PrimitiveType.Cube, transform, at + new Vector3(0.45f, 2.6f, 3.2f), new Vector3(0.9f, 0.55f, 0.05f), c, "Flag"));
            CityBuilder.Label(transform, at + new Vector3(0, 0.8f, -2.6f), label, 0.14f, Color.white);
        }

        private void SpawnFighters()
        {
            foreach (var f in _match.Fighters)
            {
                var color = f.Team == "RED" ? Red : Blue;
                Transform host;
                if (f.IsMe)
                {
                    host = _me;
                }
                else
                {
                    var v = AvatarView.Create(_players, f.Id, f.Name, 1);
                    v.ApplyAvatar(f.Avatar);
                    v.SetName((f.IsBot ? "[บอท] " : "") + f.Name, 1, false);
                    v.transform.position = new Vector3(f.X, 0.05f, f.Y);
                    _views[f.Id] = v;
                    host = v.transform;
                }
                if (host == null) continue;
                var ring = NoCollider(CityBuilder.Prim(PrimitiveType.Cylinder, host, new Vector3(0, 0.03f, 0), new Vector3(1.3f, 0.01f, 1.3f), color, "TeamRing"));
                ring.name = "ArenaRing";
                _status[f.Id] = CityBuilder.Label(host, new Vector3(0, 3.3f, 0), "", 0.14f, Water);
            }
        }

        /// <summary>Called every frame: follow the model, show who is soaked, hide whoever walked out.</summary>
        public void Sync(float dt)
        {
            _match.Step(dt);
            foreach (var f in _match.Fighters)
            {
                TextMesh st;
                if (_status.TryGetValue(f.Id, out st))
                    st.text = f.Left ? "" : f.Soaked ? "เปียกโชก!" : f.Hits > 0 ? new string('~', f.Hits) : "";
                AvatarView v;
                if (!_views.TryGetValue(f.Id, out v)) continue;
                if (f.Left)
                {
                    v.gameObject.SetActive(false);
                    continue;
                }
                var target = new Vector3(f.X, f.Soaked ? -0.6f : 0.05f, f.Y); // a soaked player sinks into a puddle
                var delta = target - v.transform.position;
                v.transform.position = Vector3.Lerp(v.transform.position, target, f.Soaked ? 4f * dt : 1f);
                if (new Vector2(delta.x, delta.z).sqrMagnitude > 0.0004f)
                    v.transform.rotation = Quaternion.LookRotation(new Vector3(delta.x, 0, delta.z));
            }
        }

        /// <summary>Where a fighter stands now (the local player's own transform for me).</summary>
        public Vector3 PositionOf(string id)
        {
            var f = _match.Get(id);
            if (f != null && f.IsMe && _me != null) return _me.position;
            AvatarView v;
            if (_views.TryGetValue(id, out v)) return v.transform.position;
            return f != null ? new Vector3(f.X, 0, f.Y) : Center;
        }

        public string FighterUnder(Collider c)
        {
            var v = c != null ? c.GetComponentInParent<AvatarView>() : null;
            return v != null && _views.ContainsKey(v.UserId) ? v.UserId : null;
        }

        /// <summary>A balloon arcs from thrower to target; on a hit it splashes, otherwise it bursts on the grass beside them.</summary>
        public void Throw(string by, string target, bool hit, bool soaked)
        {
            var from = PositionOf(by) + Vector3.up * 1.5f;
            var to = PositionOf(target) + Vector3.up * (hit ? 1.2f : 0.05f);
            if (!hit) to += new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
            var color = _match.Get(by)?.Team == "RED" ? Red : Blue;
            StartCoroutine(Fly(from, to, color, soaked));
        }

        private IEnumerator Fly(Vector3 from, Vector3 to, Color team, bool big)
        {
            var balloon = NoCollider(CityBuilder.Prim(PrimitiveType.Sphere, transform, from, Vector3.one * 0.35f, Color.Lerp(team, Water, 0.5f), "Balloon"));
            const float time = 0.35f;
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                var k = t / time;
                balloon.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.2f);
                yield return null;
            }
            Destroy(balloon);
            var splash = NoCollider(CityBuilder.Prim(PrimitiveType.Sphere, transform, to, Vector3.one * 0.3f, Water, "Splash"));
            var max = big ? 2.4f : 1.2f;
            for (float t = 0; t < 0.3f; t += Time.deltaTime)
            {
                splash.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, max, t / 0.3f);
                yield return null;
            }
            Destroy(splash);
        }

        /// <summary>Takes the team ring and status label off the local player when the match is over.</summary>
        public void Dispose()
        {
            if (_me != null)
            {
                foreach (Transform child in _me)
                    if (child.name == "ArenaRing") Destroy(child.gameObject);
                TextMesh st;
                if (_match.MyId != null && _status.TryGetValue(_match.MyId, out st) && st != null) Destroy(st.gameObject);
            }
            Destroy(gameObject);
        }

        private static GameObject NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            return go;
        }
    }
}
