using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using HappyWorld.Unity.Art;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// The MARKET zone's living part: player stalls from GET /market and NPC customers from
    /// market:activity. Lives under the zone root, so leaving the zone (CityBuilder.Build) removes it.
    /// Everything shown here comes from the server; the walk is only a replay of what already happened.
    /// </summary>
    public sealed class MarketView : MonoBehaviour
    {
        private readonly Dictionary<int, StallView> _stalls = new Dictionary<int, StallView>();
        private readonly List<NpcView> _npcs = new List<NpcView>();
        private readonly HashSet<string> _seenNpcIds = new HashSet<string>();
        private readonly System.Random _rng = new System.Random();
        private const int MaxNpcs = 40;

        public static MarketView Create(Transform zoneRoot)
        {
            var go = new GameObject("Market");
            go.transform.SetParent(zoneRoot, false);
            return go.AddComponent<MarketView>();
        }

        public IEnumerable<StallView> Stalls => _stalls.Values;

        /// <summary>(Re)builds every stall from the latest GET /market.</summary>
        public void Apply(MarketState state)
        {
            var keep = new HashSet<int>(state.stalls.Select(s => s.slot));
            foreach (var slot in _stalls.Keys.ToList())
            {
                if (keep.Contains(slot)) continue;
                Destroy(_stalls[slot].gameObject);
                _stalls.Remove(slot);
            }
            foreach (var s in state.stalls)
            {
                StallView view;
                if (!_stalls.TryGetValue(s.slot, out view))
                {
                    view = StallView.Create(transform, s);
                    _stalls[s.slot] = view;
                }
                view.Apply(s);
            }
        }

        /// <summary>Customers who visited a stall in the latest minute walk up to it and react.</summary>
        public void OnActivity(MarketActivity a)
        {
            StallView stall;
            if (a == null || !_stalls.TryGetValue(a.slot, out stall)) return; // unknown slot: blocked player or another market
            foreach (var plan in MarketNpcPlanner.Plan(a, stall.Data, Time.time, _rng))
            {
                if (_npcs.Count >= MaxNpcs || !_seenNpcIds.Add(plan.Id)) continue;
                _npcs.Add(NpcView.Create(transform, plan));
            }
        }

        private void Update()
        {
            var now = Time.time;
            for (int i = _npcs.Count - 1; i >= 0; i--)
            {
                var npc = _npcs[i];
                if (npc == null) { _npcs.RemoveAt(i); continue; }
                var phase = npc.Step(now);
                if (phase == NpcPhase.AtStall && !npc.Reacted)
                {
                    npc.Reacted = true;
                    StallView stall;
                    if (_stalls.TryGetValue(npc.Plan.Slot, out stall))
                    {
                        if (npc.Plan.Visit.outcome == "BOUGHT") stall.ShowSale(npc.Plan.Visit.productCode, npc.Plan.Visit.quantity, npc.Plan.CoinPopup);
                        else stall.ShowLostCustomer();
                    }
                }
                if (phase == NpcPhase.Done)
                {
                    Destroy(npc.gameObject);
                    _npcs.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>One market stall: counter, awning, sign with the shop name, goods with price tags.</summary>
    public sealed class StallView : MonoBehaviour
    {
        public MarketStall Data { get; private set; }
        private TextMesh _sign, _owner, _status, _goods;
        private Renderer _awning;
        private readonly List<GameObject> _goodsBoxes = new List<GameObject>();

        private static readonly Color[] Awnings =
        {
            new Color(0.95f, 0.45f, 0.45f), new Color(0.45f, 0.7f, 0.95f), new Color(0.98f, 0.8f, 0.35f),
            new Color(0.55f, 0.85f, 0.55f), new Color(0.8f, 0.6f, 0.95f), new Color(1f, 0.65f, 0.4f),
        };

        public static StallView Create(Transform parent, MarketStall s)
        {
            var root = new GameObject("Stall_" + s.slot);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(s.x, 0, s.y);
            var v = root.AddComponent<StallView>();
            var t = root.transform;
            var wood = new Color(0.6f, 0.42f, 0.28f);
            // counter faces the customers' side (+y or -y)
            CityBuilder.Prim(PrimitiveType.Cube, t, new Vector3(0, 0.55f, s.face * 0.4f), new Vector3(3.2f, 1.1f, 0.9f), wood, "Counter");
            // generous tap target that the player can still walk around (CharacterController ignores triggers)
            var tap = root.AddComponent<BoxCollider>();
            tap.isTrigger = true;
            tap.center = new Vector3(0, 1.5f, 0);
            tap.size = new Vector3(3.8f, 3f, 2.4f);
            foreach (var px in new[] { -1.5f, 1.5f })
                foreach (var pz in new[] { -0.6f, 0.6f })
                {
                    var pole = CityBuilder.Prim(PrimitiveType.Cylinder, t, new Vector3(px, 1.3f, pz), new Vector3(0.12f, 1.3f, 0.12f), wood, "Pole");
                    Destroy(pole.GetComponent<Collider>());
                }
            var awning = CityBuilder.Prim(PrimitiveType.Cube, t, new Vector3(0, 2.65f, 0), new Vector3(3.6f, 0.15f, 1.8f), Awnings[s.slot % Awnings.Length], "Awning");
            Destroy(awning.GetComponent<Collider>());
            v._awning = awning.GetComponent<Renderer>();
            v._sign = CityBuilder.Label(t, new Vector3(0, 3.55f, 0), "", 0.17f, Color.white);
            v._owner = CityBuilder.Label(t, new Vector3(0, 3.15f, 0), "", 0.11f, new Color(1f, 1f, 0.85f));
            v._status = CityBuilder.Label(t, new Vector3(0, 2.95f, s.face * 1.0f), "", 0.11f, new Color(0.7f, 1f, 0.7f));
            v._goods = CityBuilder.Label(t, new Vector3(0, 1.75f, s.face * 1.1f), "", 0.1f, Color.white);
            return v;
        }

        public void Apply(MarketStall s)
        {
            Data = s;
            _sign.text = (s.isMine ? "★ " : s.isFriend ? "♥ " : "") + s.name;
            _owner.text = "โดย " + s.ownerName + "  · ร้าน Lv." + s.level;
            _status.text = s.status == "OPEN" ? "● เปิดขาย" : "● พักขาย";
            _status.color = s.status == "OPEN" ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.8f, 0.5f);
            RefreshGoods();
        }

        private void RefreshGoods()
        {
            foreach (var g in _goodsBoxes) Destroy(g);
            _goodsBoxes.Clear();
            var lines = new List<string>();
            for (int i = 0; i < Data.products.Count; i++)
            {
                var p = Data.products[i];
                lines.Add(MarketNpcPlanner.ProductName(p.productCode) + " " + p.price + " ทอง" + (p.shelfQuantity > 0 ? "  (เหลือ " + p.shelfQuantity + ")" : "  (หมด)"));
                // a little pile of goods per product on the counter, smaller as it sells
                var x = -1.1f + i * (2.2f / Mathf.Max(1, Data.products.Count - 1));
                var h = 0.12f + 0.35f * Mathf.Clamp01(p.shelfQuantity / 20f);
                var box = CityBuilder.Prim(PrimitiveType.Cube, transform, new Vector3(Data.products.Count == 1 ? 0 : x, 1.1f + h / 2, Data.face * 0.4f), new Vector3(0.45f, h, 0.45f), ProductColor(p.productCode), "Goods");
                Destroy(box.GetComponent<Collider>());
                _goodsBoxes.Add(box);
            }
            _goods.text = lines.Count > 0 ? string.Join("\n", lines) : "ยังไม่มีสินค้า";
        }

        /// <summary>A customer bought: show coins and shrink the pile (the next GET /market has the real numbers).</summary>
        public void ShowSale(string productCode, int quantity, int coins)
        {
            var p = Data.products.FirstOrDefault(x => x.productCode == productCode);
            if (p != null) p.shelfQuantity = Mathf.Max(0, p.shelfQuantity - quantity);
            RefreshGoods();
            if (coins > 0)
            {
                FloatingText.Spawn(transform, new Vector3(0, 2.2f, Data.face * 1.2f), "+" + coins + " ทอง", new Color(1f, 0.85f, 0.2f));
                CoinPop.Spawn(transform, new Vector3(0, 1.4f, Data.face * 0.9f));
                // the clink is loud for my own stall and fades with distance for the others
                var cam = Camera.main;
                var d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
                var vol = Data.isMine ? 1f : Mathf.Clamp01(1f - (d - 10f) / 25f) * 0.6f;
                if (vol > 0.05f) Audio.GameAudio.Sfx("sfx_coin", vol, Random.Range(0.95f, 1.1f));
            }
        }

        public void ShowLostCustomer()
        {
            FloatingText.Spawn(transform, new Vector3(0, 2.2f, Data.face * 1.2f), "…", new Color(0.85f, 0.85f, 0.9f));
        }

        public static Color ProductColor(string code)
        {
            switch (code)
            {
                case "LEMONADE": return new Color(1f, 0.95f, 0.45f);
                case "BREAD": return new Color(0.85f, 0.6f, 0.3f);
                case "FLOWER": return new Color(1f, 0.5f, 0.75f);
                case "THAI_TEA": return new Color(0.95f, 0.55f, 0.25f);
                case "MEATBALL": return new Color(0.55f, 0.3f, 0.2f);
                default: return Color.gray;
            }
        }
    }

    /// <summary>An NPC customer: simple primitive body coloured by customer type, label, speech bubble.</summary>
    public sealed class NpcView : MonoBehaviour
    {
        public NpcPlan Plan { get; private set; }
        public bool Reacted;
        private TextMesh _bubble;
        private bool _bubbleShown;

        public static NpcView Create(Transform parent, NpcPlan plan)
        {
            var root = new GameObject("Npc_" + plan.Id);
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<NpcView>();
            v.Plan = plan;
            Color shirt, hair;
            switch (plan.Visit.customerType)
            {
                case "STUDENT": shirt = new Color(0.95f, 0.95f, 0.95f); hair = new Color(0.1f, 0.1f, 0.1f); break;
                case "WORKER": shirt = new Color(0.35f, 0.5f, 0.8f); hair = new Color(0.25f, 0.15f, 0.1f); break;
                case "TOURIST": shirt = new Color(1f, 0.6f, 0.3f); hair = new Color(0.85f, 0.7f, 0.4f); break;
                case "GRANDMA": shirt = new Color(0.65f, 0.45f, 0.75f); hair = new Color(0.85f, 0.85f, 0.85f); break;
                case "FESTIVAL_GOER": shirt = new Color(1f, 0.45f, 0.65f); hair = new Color(0.3f, 0.2f, 0.15f); break;
                default: shirt = Color.white; hair = Color.black; break;
            }
            var t = root.transform;
            var rig = CharacterRig.Create(t);
            if (rig != null)
            {
                foreach (var c in rig.GetComponentsInChildren<Collider>()) Destroy(c);
                rig.transform.localScale *= 0.85f; // customers a touch smaller than players
                rig.Dress(new Color(0.55f, 0.55f, 0.62f), shirt, hair);
                var hatParent = rig.Torso != null ? rig.Torso : t;
                if (plan.Visit.customerType == "FESTIVAL_GOER")
                    Strip(Place(CityBuilder.Prim(PrimitiveType.Cylinder, hatParent, Vector3.zero, Vector3.one, new Color(1f, 0.85f, 0.2f), "PartyHat"), rig.HatLocalPos + Vector3.up * 0.02f, new Vector3(0.12f, 0.1f, 0.12f)));
                if (plan.Visit.customerType == "TOURIST")
                    Strip(Place(CityBuilder.Prim(PrimitiveType.Cylinder, hatParent, Vector3.zero, Vector3.one, new Color(0.95f, 0.9f, 0.6f), "Hat"), rig.HatLocalPos + Vector3.down * 0.08f, new Vector3(0.75f, 0.02f, 0.75f)));
            }
            else
            {
            Strip(CityBuilder.Prim(PrimitiveType.Capsule, t, new Vector3(0, 0.85f, 0), new Vector3(0.65f, 0.42f, 0.65f), new Color(0.45f, 0.45f, 0.5f), "Legs"));
            Strip(CityBuilder.Prim(PrimitiveType.Cube, t, new Vector3(0, 1.0f, 0), new Vector3(0.62f, 0.5f, 0.42f), shirt, "Shirt"));
            Strip(CityBuilder.Prim(PrimitiveType.Sphere, t, new Vector3(0, 1.5f, 0), new Vector3(0.5f, 0.5f, 0.5f), new Color(1f, 0.85f, 0.7f), "Head"));
            Strip(CityBuilder.Prim(PrimitiveType.Sphere, t, new Vector3(0, 1.62f, -0.04f), new Vector3(0.54f, 0.34f, 0.54f), hair, "Hair"));
            if (plan.Visit.customerType == "FESTIVAL_GOER") // party hat
                Strip(CityBuilder.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 1.92f, 0), new Vector3(0.22f, 0.16f, 0.22f), new Color(1f, 0.85f, 0.2f), "PartyHat"));
            if (plan.Visit.customerType == "TOURIST")
                Strip(CityBuilder.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 1.82f, 0), new Vector3(0.7f, 0.03f, 0.7f), new Color(0.95f, 0.9f, 0.6f), "Hat"));
            }
            CityBuilder.Label(t, new Vector3(0, 2.15f, 0), MarketNpcPlanner.CustomerName(plan.Visit.customerType), 0.09f, new Color(0.9f, 0.95f, 1f));
            v._bubble = CityBuilder.Label(t, new Vector3(0, 2.6f, 0), "", 0.12f, new Color(1f, 1f, 0.6f));
            float x, y;
            plan.PositionAt(Time.time, out x, out y);
            t.localPosition = new Vector3(x, 0.05f, y);
            root.SetActive(false); // appears when its walk starts
            return v;
        }

        private static void Strip(GameObject go) => Destroy(go.GetComponent<Collider>()); // NPCs are not tappable

        private static GameObject Place(GameObject go, Vector3 localPos, Vector3 localScale)
        {
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            return go;
        }

        /// <summary>Moves along the planned path; returns the phase.</summary>
        public NpcPhase Step(double now)
        {
            float x, y;
            var phase = Plan.PositionAt(now, out x, out y);
            if (phase == NpcPhase.Waiting) return phase;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            var next = new Vector3(x, 0.05f, y);
            var delta = next - transform.localPosition;
            if (new Vector2(delta.x, delta.z).sqrMagnitude > 0.0001f)
                transform.localRotation = Quaternion.LookRotation(new Vector3(delta.x, 0, delta.z));
            transform.localPosition = next;
            if (phase == NpcPhase.AtStall && !_bubbleShown)
            {
                _bubbleShown = true;
                _bubble.text = Plan.Bubble;
            }
            if (phase == NpcPhase.WalkingOut && _bubbleShown && now > Plan.StartAt + Plan.WalkInSeconds + Plan.WaitSeconds + 1.5)
                _bubble.text = "";
            return phase;
        }
    }

    /// <summary>A spinning gold coin (Kenney model) that hops up from a stall when something sells.</summary>
    public sealed class CoinPop : MonoBehaviour
    {
        private float _born;
        private Vector3 _start;

        public static void Spawn(Transform parent, Vector3 localPos)
        {
            var coin = ModelLibrary.Spawn("city/coin", parent, localPos, 0.6f);
            if (coin == null) return;
            var c = coin.AddComponent<CoinPop>();
            c._born = Time.time;
            c._start = coin.transform.localPosition;
        }

        private void Update()
        {
            var age = Time.time - _born;
            transform.localPosition = _start + Vector3.up * (2.4f * age - 1.6f * age * age); // a little hop
            transform.Rotate(0, 540f * Time.deltaTime, 0, Space.Self);
            var s = transform.localScale;
            if (age > 0.9f) transform.localScale = s * Mathf.Max(0f, 1f - Time.deltaTime * 6f);
            if (age > 1.3f) Destroy(gameObject);
        }
    }

    /// <summary>Text that floats up and disappears (coins, reactions).</summary>
    public sealed class FloatingText : MonoBehaviour
    {
        private TextMesh _text;
        private float _born;
        private const float Life = 1.6f;

        public static void Spawn(Transform parent, Vector3 localPos, string text, Color color)
        {
            var tm = CityBuilder.Label(parent, localPos, text, 0.16f, color);
            var f = tm.gameObject.AddComponent<FloatingText>();
            f._text = tm;
            f._born = Time.time;
        }

        private void Update()
        {
            var age = Time.time - _born;
            transform.localPosition += Vector3.up * Time.deltaTime * 0.8f;
            var c = _text.color;
            c.a = Mathf.Clamp01(1f - age / Life);
            _text.color = c;
            if (age > Life) Destroy(gameObject);
        }
    }
}
