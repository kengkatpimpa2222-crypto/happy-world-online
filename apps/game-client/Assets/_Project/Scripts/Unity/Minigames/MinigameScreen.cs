using System;
using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.Minigames;
using HappyWorld.Unity.Audio;
using HappyWorld.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HappyWorld.Unity.Minigames
{
    /// <summary>
    /// A mini-game round on top of everything: intro card → server starts the round (seed) → 3-2-1 →
    /// 30-60 s of play driven by the engine-free rules in Core/Minigames → the score goes to the server,
    /// which answers with stars and the goods it added to the shop. The client never decides the reward.
    /// </summary>
    public sealed class MinigameScreen : MonoBehaviour
    {
        private enum Phase { Intro, Countdown, Playing, Sending, Result }

        private GameSession _s;
        private Business _business;
        private string _product, _game;
        private Action<Business> _onClose;
        private RectTransform _root, _field, _card;
        private Text _title, _scoreText, _comboText, _bigText;
        private Image _timeFill;
        private Phase _phase;
        private float _countdown;
        private int _lastCount = -1;
        private MinigameRound _round;
        private FieldInput _input;

        private CatchGame _catch;
        private GrillGame _grill;
        private PourGame _pour;
        private readonly Dictionary<int, RectTransform> _itemViews = new Dictionary<int, RectTransform>();
        private RectTransform _basket;
        private readonly Image[] _food = new Image[GrillGame.Slots];
        private readonly Image[] _cookBar = new Image[GrillGame.Slots];
        private readonly Image[] _smoke = new Image[GrillGame.Slots];
        private RectTransform _glass, _tea, _band, _stream;

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Color Sky = new Color(1f, 0.93f, 0.8f);

        /// <summary>Opens the round's intro card over <paramref name="layer"/> (the modal layer, so the city ignores input).</summary>
        public static MinigameScreen Open(GameSession s, RectTransform layer, Business business, string productCode, Action<Business> onClose)
        {
            var root = UiKit.Box("Minigame", layer, Sky).rectTransform.Place(Vector2.zero, Vector2.one);
            var m = root.gameObject.AddComponent<MinigameScreen>();
            m._s = s;
            m._business = business;
            m._product = productCode;
            m._game = MinigameRules.ForProduct(productCode);
            m._onClose = onClose;
            m._root = root;
            m.Build();
            m.ShowIntro();
            if (GameAudio.I != null) GameAudio.I.SetOverride("music_minigame");
            return m;
        }

        public static Sprite Sp(string name)
        {
            Sprite sp;
            if (Sprites.TryGetValue(name, out sp)) return sp;
            var tex = Resources.Load<Texture2D>("Art/Minigames/" + name);
            sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            Sprites[name] = sp;
            return sp;
        }

        // ---------- layout ----------

        private void Build()
        {
            // warm kitchen backdrop: wall + counter
            UiKit.Box("Counter", _root, new Color(0.86f, 0.66f, 0.45f)).rectTransform.Place(Vector2.zero, new Vector2(1, 0.16f));
            UiKit.Box("CounterTop", _root, new Color(0.95f, 0.8f, 0.6f)).rectTransform.Place(new Vector2(0, 0.16f), new Vector2(1, 0.19f));

            var top = UiKit.Box("Top", _root, new Color(1f, 1f, 1f, 0.85f)).Round(true).rectTransform.Place(new Vector2(0.02f, 0.86f), new Vector2(0.98f, 0.98f));
            _title = UiKit.Label(top, MinigameRules.Title(_game, _product), 40, TextAnchor.MiddleLeft, UiKit.Ink);
            _title.rectTransform.Place(new Vector2(0, 0), new Vector2(0.4f, 1), new Vector2(24, 0), Vector2.zero);
            _scoreText = UiKit.Label(top, "", 40, TextAnchor.MiddleCenter, UiKit.Accent);
            _scoreText.fontStyle = FontStyle.Bold;
            _scoreText.rectTransform.Place(new Vector2(0.4f, 0), new Vector2(0.62f, 1));
            var timeBg = UiKit.Box("TimeBg", top, new Color(0.9f, 0.9f, 0.92f)).Round(false, 0.5f).rectTransform.Place(new Vector2(0.64f, 0.3f), new Vector2(0.98f, 0.7f));
            _timeFill = UiKit.Box("Time", timeBg, UiKit.Good).Round(false, 0.5f);
            _timeFill.rectTransform.Place(Vector2.zero, Vector2.one);

            _field = UiKit.Rect("Field", _root).Place(new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.84f));
            var hit = UiKit.Box("Input", _field, new Color(0, 0, 0, 0)); // transparent, catches touches
            hit.rectTransform.Place(Vector2.zero, Vector2.one);
            _input = hit.gameObject.AddComponent<FieldInput>();

            _comboText = UiKit.Label(_root, "", 34, TextAnchor.MiddleCenter, new Color(0.85f, 0.35f, 0.1f));
            _comboText.rectTransform.Place(new Vector2(0.35f, 0.78f), new Vector2(0.65f, 0.85f));
            _bigText = UiKit.Label(_root, "", 140, TextAnchor.MiddleCenter, Color.white);
            _bigText.gameObject.AddComponent<Outline>().effectColor = new Color(0.6f, 0.3f, 0.1f);
            _bigText.rectTransform.Place(new Vector2(0.2f, 0.35f), new Vector2(0.8f, 0.65f));
        }

        private RectTransform Card()
        {
            if (_card != null) Destroy(_card.gameObject);
            var dim = UiKit.Box("Dim", _root, new Color(0, 0, 0, 0.35f)).rectTransform.Place(Vector2.zero, Vector2.one);
            _card = dim;
            return UiKit.Box("Card", dim, UiKit.Panel).Round(true, 1.4f).rectTransform.Place(new Vector2(0.27f, 0.1f), new Vector2(0.73f, 0.9f));
        }

        private void CloseCard()
        {
            if (_card != null) Destroy(_card.gameObject);
            _card = null;
        }

        private static Text Line(RectTransform card, string text, float yMin, float yMax, int size = 32, TextAnchor a = TextAnchor.MiddleCenter, Color? c = null)
        {
            var t = UiKit.Label(card, text, size, a, c ?? UiKit.Ink);
            t.rectTransform.Place(new Vector2(0, yMin), new Vector2(1, yMax), new Vector2(30, 0), new Vector2(-30, 0));
            return t;
        }

        private void ShowIntro()
        {
            _phase = Phase.Intro;
            _scoreText.text = "";
            _timeFill.fillAmount = 1f;
            var c = Card();
            Line(c, "<b>" + MinigameRules.Title(_game, _product) + "</b>", 0.86f, 0.97f, 50, TextAnchor.MiddleCenter, UiKit.Accent);
            var icon = UiKit.Box("Icon", c, Color.white);
            icon.sprite = Sp(IntroIcon());
            icon.preserveAspect = true;
            icon.rectTransform.Place(new Vector2(0.38f, 0.64f), new Vector2(0.62f, 0.86f));
            Line(c, MinigameRules.HowTo(_game, _product), 0.45f, 0.64f, 30);
            var stars = MinigameRules.StarScores(_game);
            Line(c, "★ " + stars[0] + "   ★★ " + stars[1] + "   ★★★ " + stars[2] + " คะแนน\n<size=26>ได้ของเข้าคลังร้าน 1 / 4 / 7 / 10 ชิ้น ตามจำนวนดาว (ไม่เสียทอง)</size>", 0.27f, 0.45f, 30);
            UiKit.Button(c, "เริ่มเลย!", UiKit.Good, StartRound, 40).GetComponent<RectTransform>().Place(new Vector2(0.52f, 0.05f), new Vector2(0.92f, 0.2f));
            UiKit.Button(c, "ไว้ทีหลัง", UiKit.Muted, Close, 32).GetComponent<RectTransform>().Place(new Vector2(0.08f, 0.05f), new Vector2(0.48f, 0.2f));
        }

        private string IntroIcon()
        {
            switch (_product)
            {
                case "FLOWER": return "flower_pink";
                case "MEATBALL": return "skewer_gold";
                case "BREAD": return "bread_gold";
                case "THAI_TEA": return "glass";
                default: return "lemon";
            }
        }

        // ---------- round flow ----------

        private void StartRound()
        {
            _s.Run(async () =>
            {
                _round = await _s.Api.StartMinigameAsync(_business.id, _product);
                if (this == null) return;
                CloseCard();
                ClearField();
                var dur = _round.durationSeconds > 0 ? _round.durationSeconds : MinigameRules.Duration(_game);
                _catch = null; _grill = null; _pour = null;
                if (_game == MinigameRules.Catch) { _catch = new CatchGame(_round.seed, dur); BuildCatch(); }
                else if (_game == MinigameRules.Grill) { _grill = new GrillGame(_round.seed, dur); BuildGrill(); }
                else { _pour = new PourGame(_round.seed, dur); BuildPour(); }
                _phase = Phase.Countdown;
                _countdown = 3.2f;
                _lastCount = -1;
            });
        }

        private void Update()
        {
            switch (_phase)
            {
                case Phase.Countdown:
                    _countdown -= Time.deltaTime;
                    var n = Mathf.CeilToInt(_countdown - 0.2f);
                    if (n != _lastCount)
                    {
                        _lastCount = n;
                        _bigText.text = n > 0 ? n.ToString() : "เริ่ม!";
                        GameAudio.Sfx(n > 0 ? "sfx_countdown" : "sfx_go");
                    }
                    _bigText.transform.localScale = Vector3.one * (1f + 0.3f * Mathf.Repeat(_countdown, 1f));
                    if (_countdown <= -0.4f)
                    {
                        _bigText.text = "";
                        _bigText.transform.localScale = Vector3.one;
                        _phase = Phase.Playing;
                    }
                    break;
                case Phase.Playing:
                    Play(Time.deltaTime);
                    break;
            }
        }

        private void Play(float dt)
        {
            int score;
            float time, duration;
            bool finished;
            if (_catch != null)
            {
                _catch.Step(dt, _input.Down || _input.Dragged ? _input.X : _catch.BasketX);
                DrawCatch();
                score = _catch.Score; time = _catch.Time; duration = _catch.Duration; finished = _catch.Finished;
                _comboText.text = _catch.Combo >= 3 ? "คอมโบ x" + _catch.Combo : "";
            }
            else if (_grill != null)
            {
                _grill.Step(dt);
                DrawGrill();
                score = _grill.Score; time = _grill.Time; duration = _grill.Duration; finished = _grill.Finished;
                _comboText.text = _grill.Streak >= 2 ? "สุกพอดีติดกัน x" + _grill.Streak : "";
            }
            else
            {
                _pour.Step(dt, _input.Down);
                DrawPour();
                score = _pour.Score; time = _pour.Time; duration = _pour.Duration; finished = _pour.Finished;
                _comboText.text = "";
            }
            _scoreText.text = score + " คะแนน";
            _timeFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(1f - time / duration), 1f);
            _timeFill.color = time > duration - 10f ? UiKit.Danger : UiKit.Good;
            if (finished) EndRound(score);
        }

        private void EndRound(int score)
        {
            _phase = Phase.Sending;
            _comboText.text = "";
            if (GameAudio.I != null) GameAudio.I.Loop(null);
            _bigText.text = "หมดเวลา!";
            GameAudio.Sfx("sfx_whoosh");
            Send(score);
        }

        private void Send(int score)
        {
            _s.Run(async () =>
            {
                try
                {
                    var r = await _s.Api.FinishMinigameAsync(_round.sessionId, score);
                    if (this == null) return;
                    _bigText.text = "";
                    ShowResult(r);
                }
                catch (Exception)
                {
                    if (this != null) ShowRetry(score);
                    throw; // GameSession.Run shows the error as a toast
                }
            });
        }

        private void ShowRetry(int score)
        {
            _bigText.text = "";
            var c = Card();
            Line(c, "ส่งคะแนนไม่สำเร็จ", 0.6f, 0.8f, 40);
            Line(c, "คะแนนของคุณ " + score + " ยังอยู่ ลองส่งอีกครั้งได้ (ไม่นับซ้ำ)", 0.4f, 0.6f, 30);
            UiKit.Button(c, "ส่งอีกครั้ง", UiKit.Primary, () => { CloseCard(); Send(score); }, 36).GetComponent<RectTransform>().Place(new Vector2(0.52f, 0.08f), new Vector2(0.92f, 0.24f));
            UiKit.Button(c, "ปิด", UiKit.Muted, Close, 32).GetComponent<RectTransform>().Place(new Vector2(0.08f, 0.08f), new Vector2(0.48f, 0.24f));
        }

        private void ShowResult(MinigameResult r)
        {
            _phase = Phase.Result;
            if (r.business != null) _business = r.business;
            if (r.level.HasValue && _s.Profile != null) _s.Profile.level = r.level.Value;
            var c = Card();
            Line(c, r.stars == 3 ? "<b>สุดยอด!</b>" : r.stars == 2 ? "<b>เก่งมาก!</b>" : r.stars == 1 ? "<b>ทำได้ดี!</b>" : "<b>ลองอีกนิดนะ!</b>", 0.86f, 0.97f, 50, TextAnchor.MiddleCenter, UiKit.Accent);
            var row = UiKit.Rect("Stars", c).Place(new Vector2(0.15f, 0.62f), new Vector2(0.85f, 0.86f));
            for (int i = 0; i < 3; i++)
            {
                var st = UiKit.Box("Star", row, Color.white);
                st.sprite = Sp("star_off");
                st.preserveAspect = true;
                st.rectTransform.Place(new Vector2(i / 3f, 0), new Vector2((i + 1) / 3f, 1), new Vector2(8, 0), new Vector2(-8, 0));
                if (i < r.stars) st.gameObject.AddComponent<StarPop>().Delay = 0.35f + i * 0.4f;
            }
            Line(c, r.score + " คะแนน" + (r.score >= r.best && r.score > 0 ? "  <color=#E67E22>(สถิติใหม่!)</color>" : "  <size=26>(ดีที่สุด " + r.best + ")</size>"), 0.52f, 0.62f, 36);
            string goods;
            var name = Core.World.MarketNpcPlanner.ProductName(_product);
            if (r.unitsAwarded > 0) goods = "<b>+" + r.unitsAwarded + " " + name + "</b> เข้าคลังร้านแล้ว (ไม่เสียทอง)" + (r.xpAwarded > 0 ? "  ·  XP +" + r.xpAwarded : "");
            else if (!r.rewarded) goods = "วันนี้ทำของครบ " + r.rewardedPlaysLeft + " รอบแล้ว เล่นต่อเพื่อความสนุกได้ พรุ่งนี้ทำของได้ใหม่";
            else goods = "คลังร้านเต็มแล้ว ลองวางของขึ้นแผงก่อนนะ";
            if (r.stockFull && r.unitsAwarded > 0) goods += "\n<size=26>คลังเต็ม บางชิ้นใส่ไม่ลง</size>";
            Line(c, goods, 0.34f, 0.52f, 30);
            Line(c, "<size=26>วันนี้ยังทำของได้อีก " + r.rewardedPlaysLeft + " รอบ · ของที่ทำเองต้นทุน 0 ทำให้ต้นทุนเฉลี่ยของร้านลดลง</size>", 0.24f, 0.34f, 26, TextAnchor.MiddleCenter, UiKit.Muted);
            UiKit.Button(c, "เล่นอีกรอบ", UiKit.Good, () => { CloseCard(); ClearField(); ShowIntro(); }, 36).GetComponent<RectTransform>().Place(new Vector2(0.52f, 0.05f), new Vector2(0.92f, 0.2f));
            UiKit.Button(c, "กลับไปที่ร้าน", UiKit.Primary, Close, 32).GetComponent<RectTransform>().Place(new Vector2(0.08f, 0.05f), new Vector2(0.48f, 0.2f));
            GameAudio.Sfx(r.stars >= 2 ? "sfx_fanfare" : r.stars == 1 ? "sfx_good" : "sfx_lose");
        }

        private void Close()
        {
            if (GameAudio.I != null) GameAudio.I.SetOverride(null);
            var cb = _onClose;
            var b = _business;
            Destroy(gameObject);
            if (cb != null) cb(b);
        }

        private void OnDestroy()
        {
            if (GameAudio.I != null) GameAudio.I.SetOverride(null);
        }

        private void ClearField()
        {
            for (int i = _field.childCount - 1; i >= 0; i--)
            {
                var ch = _field.GetChild(i);
                if (ch.GetComponent<FieldInput>() == null) Destroy(ch.gameObject);
            }
            _itemViews.Clear();
            _scoreText.text = "";
            _comboText.text = "";
        }

        // ---------- shared effects ----------

        private RectTransform Pic(string sprite, Transform parent, float size)
        {
            var img = UiKit.Box(sprite, parent, Color.white);
            img.sprite = Sp(sprite);
            img.preserveAspect = true;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.sizeDelta = new Vector2(size, size);
            return r;
        }

        private Vector2 FieldPos(float x, float y) => new Vector2(x * _field.rect.width, y * _field.rect.height);

        private void Pop(Vector2 at, string text, Color color)
        {
            var t = UiKit.Label(_field, text, 40, TextAnchor.MiddleCenter, color);
            t.fontStyle = FontStyle.Bold;
            t.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.9f);
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.sizeDelta = new Vector2(420, 80);
            r.anchoredPosition = at;
            t.gameObject.AddComponent<FloatUp>();
        }

        private void Sparkle(Vector2 at)
        {
            var s = Pic("sparkle", _field, 150);
            s.anchoredPosition = at;
            s.gameObject.AddComponent<FloatUp>().Rise = 20f;
        }

        // ---------- catch ----------

        private void BuildCatch()
        {
            _basket = Pic("basket", _field, 230);
        }

        private string CatchSprite(CatchGame.Item it)
        {
            if (it.Bad) return it.Variant % 2 == 0 ? "dry_leaf" : "pebble";
            if (_product == "FLOWER") return it.Variant == 0 ? "flower_pink" : it.Variant == 1 ? "flower_yellow" : "flower_purple";
            return it.Variant == 0 ? "lemon" : it.Variant == 1 ? "ice" : "mint";
        }

        private void DrawCatch()
        {
            _basket.anchoredPosition = FieldPos(_catch.BasketX, CatchGame.BasketLine) + new Vector2(0, -40);
            for (int i = 0; i < _catch.Items.Count; i++)
            {
                var it = _catch.Items[i];
                RectTransform v;
                _itemViews.TryGetValue(i, out v);
                if (it.Live && v == null)
                {
                    v = Pic(CatchSprite(it), _field, it.Bad ? 120 : 140);
                    _itemViews[i] = v;
                }
                if (v == null) continue;
                if (!it.Live) { Destroy(v.gameObject); _itemViews.Remove(i); continue; }
                v.anchoredPosition = FieldPos(it.X, it.Y);
                v.localRotation = Quaternion.Euler(0, 0, Mathf.Sin((Time.time + i) * 3f) * 12f);
            }
            foreach (var e in _catch.Events)
            {
                var at = FieldPos(e.X, CatchGame.BasketLine + 0.12f);
                if (e.Kind == "catch")
                {
                    Pop(at, "+" + e.Points, new Color(0.95f, 0.55f, 0.1f));
                    GameAudio.Sfx("sfx_good", 0.8f, 1f + Mathf.Min(_catch.Combo, 10) * 0.04f);
                    _basket.localScale = new Vector3(1.12f, 0.9f, 1f);
                }
                else if (e.Kind == "bad") { Pop(at, "อุ๊ย!", new Color(0.5f, 0.4f, 0.35f)); GameAudio.Sfx("sfx_miss", 0.7f); }
                else if (e.Kind == "miss") GameAudio.Sfx("sfx_tap", 0.4f);
            }
            _basket.localScale = Vector3.Lerp(_basket.localScale, Vector3.one, Time.deltaTime * 12f);
        }

        // ---------- grill ----------

        private void BuildGrill()
        {
            var raw = _product == "BREAD" ? "bread_raw" : "skewer_raw";
            for (int i = 0; i < GrillGame.Slots; i++)
            {
                int slot = i;
                var x = (i + 0.5f) / GrillGame.Slots;
                var spot = UiKit.Box("Spot", _field, Color.white);
                spot.sprite = Sp("grill_spot");
                spot.preserveAspect = true;
                spot.rectTransform.Place(new Vector2(x - 0.115f, 0.22f), new Vector2(x + 0.115f, 0.72f));
                var btn = spot.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => _grill.Tap(slot));
                var food = UiKit.Box("Food", spot.transform, Color.white);
                food.sprite = Sp(raw);
                food.preserveAspect = true;
                food.raycastTarget = false;
                food.rectTransform.Place(new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f));
                _food[i] = food;
                var smoke = UiKit.Box("Smoke", spot.transform, Color.white);
                smoke.sprite = Sp("smoke");
                smoke.preserveAspect = true;
                smoke.raycastTarget = false;
                smoke.rectTransform.Place(new Vector2(0.2f, 0.6f), new Vector2(0.8f, 1.2f));
                _smoke[i] = smoke;
                // cook meter under the spot: the green part is "golden"
                var meter = UiKit.Box("Meter", _field, new Color(0.3f, 0.25f, 0.2f, 0.25f)).Round(false, 0.4f).rectTransform.Place(new Vector2(x - 0.1f, 0.12f), new Vector2(x + 0.1f, 0.17f));
                var zone = UiKit.Box("Golden", meter, new Color(0.3f, 0.8f, 0.4f, 0.8f)).rectTransform;
                zone.Place(new Vector2(GrillGame.PerfectFrom / GrillGame.BurnAt, 0), new Vector2(GrillGame.PerfectTo / GrillGame.BurnAt, 1));
                var bar = UiKit.Box("Cook", meter, new Color(0.95f, 0.55f, 0.15f));
                bar.rectTransform.Place(Vector2.zero, new Vector2(0, 1));
                _cookBar[i] = bar;
                var hint = UiKit.Label(_field, "แตะเพื่อวาง", 26, TextAnchor.MiddleCenter, UiKit.Muted);
                hint.rectTransform.Place(new Vector2(x - 0.12f, 0.02f), new Vector2(x + 0.12f, 0.1f));
            }
            if (GameAudio.I != null) GameAudio.I.Loop("loop_sizzle");
        }

        private void DrawGrill()
        {
            var baseName = _product == "BREAD" ? "bread_" : "skewer_";
            int busy = 0;
            for (int i = 0; i < GrillGame.Slots; i++)
            {
                var s = _grill.Grill[i];
                var food = _food[i];
                food.enabled = s.Busy;
                _smoke[i].enabled = s.Busy && (s.Burnt || s.Cook(_grill.Time) > GrillGame.GoodTo);
                _smoke[i].color = new Color(1, 1, 1, s.Burnt ? 0.9f : 0.5f);
                var c = s.Cook(_grill.Time);
                _cookBar[i].rectTransform.anchorMax = new Vector2(s.Busy ? Mathf.Clamp01(c / GrillGame.BurnAt) : 0f, 1f);
                if (!s.Busy) continue;
                busy++;
                if (s.Burnt) { food.sprite = Sp(baseName + "burnt"); food.color = Color.white; continue; }
                if (c < GrillGame.RawUntil)
                {
                    food.sprite = Sp(baseName + "raw");
                    food.color = Color.white;
                }
                else
                {
                    food.sprite = Sp(baseName + "gold");
                    // past golden it darkens towards burnt
                    var over = Mathf.InverseLerp(GrillGame.PerfectTo, GrillGame.BurnAt, c);
                    food.color = Color.Lerp(Color.white, new Color(0.55f, 0.45f, 0.4f), over);
                }
                var perfect = c >= GrillGame.PerfectFrom && c <= GrillGame.PerfectTo;
                food.transform.localScale = Vector3.one * (perfect ? 1f + 0.06f * Mathf.Sin(Time.time * 18f) : 1f);
            }
            if (GameAudio.I != null) GameAudio.I.SetLoopVolume(busy > 0 ? 0.25f + 0.1f * busy : 0f);
            foreach (var e in _grill.Events)
            {
                var spot = (RectTransform)_food[e.Index].transform.parent;
                var at = new Vector2(((e.Index + 0.5f) / GrillGame.Slots) * _field.rect.width, 0.78f * _field.rect.height);
                switch (e.Kind)
                {
                    case "perfect": Pop(at, "สุกพอดี! +" + e.Points, new Color(0.2f, 0.6f, 0.2f)); Sparkle(at); GameAudio.Sfx("sfx_good", 1f, 1f + _grill.Streak * 0.05f); break;
                    case "good": Pop(at, "+" + e.Points, new Color(0.95f, 0.55f, 0.1f)); GameAudio.Sfx("sfx_coin", 0.8f); break;
                    case "over": Pop(at, "เกือบไหม้ +" + e.Points, new Color(0.6f, 0.4f, 0.2f)); GameAudio.Sfx("sfx_coin", 0.6f, 0.9f); break;
                    case "raw": Pop(at, "ยังไม่สุก", UiKit.Muted); GameAudio.Sfx("sfx_tap", 0.6f); break;
                    case "burnt": Pop(at, "ไหม้แล้ว! แตะเพื่อทิ้ง", new Color(0.4f, 0.3f, 0.3f)); GameAudio.Sfx("sfx_miss", 0.7f); break;
                    case "place": GameAudio.Sfx("sfx_pop", 0.6f); spot.localScale = Vector3.one; break;
                    case "clear": GameAudio.Sfx("sfx_whoosh", 0.5f); break;
                }
            }
        }

        // ---------- pour ----------

        private void BuildPour()
        {
            var holder = UiKit.Rect("GlassHolder", _field).Place(new Vector2(0.38f, 0.12f), new Vector2(0.62f, 0.8f));
            _glass = holder;
            _tea = UiKit.Box("Tea", holder, new Color(0.93f, 0.52f, 0.2f)).rectTransform; // Thai tea orange
            _tea.Place(new Vector2(0.14f, 0.03f), new Vector2(0.86f, 0.03f));
            _band = UiKit.Box("Band", holder, new Color(0.3f, 0.85f, 0.4f, 0.35f)).rectTransform;
            var glass = UiKit.Box("Glass", holder, Color.white);
            glass.sprite = Sp("glass");
            glass.raycastTarget = false;
            glass.rectTransform.Place(Vector2.zero, Vector2.one);
            _stream = UiKit.Box("Stream", _field, new Color(0.93f, 0.52f, 0.2f)).rectTransform;
            var hint = UiKit.Label(_field, "แตะค้างตรงไหนก็ได้เพื่อเท", 30, TextAnchor.MiddleCenter, UiKit.Muted);
            hint.rectTransform.Place(new Vector2(0.25f, 0.01f), new Vector2(0.75f, 0.09f));
            if (GameAudio.I != null) { GameAudio.I.Loop("loop_pour"); GameAudio.I.SetLoopVolume(0f); }
        }

        private void DrawPour()
        {
            const float bottom = 0.03f, top = 0.95f;
            _tea.anchorMax = new Vector2(0.86f, bottom + (top - bottom) * _pour.Fill);
            float lo = bottom + (top - bottom) * (_pour.Target - _pour.HalfBand), hi = bottom + (top - bottom) * (_pour.Target + _pour.HalfBand);
            _band.Place(new Vector2(0.02f, lo), new Vector2(0.98f, hi));
            _stream.gameObject.SetActive(_pour.Pouring);
            if (_pour.Pouring)
            {
                var gTop = 0.12f + 0.68f * (bottom + (top - bottom) * _pour.Fill);
                _stream.Place(new Vector2(0.495f, gTop), new Vector2(0.505f + 0.004f * Mathf.Sin(Time.time * 40f), 1f));
            }
            if (GameAudio.I != null) GameAudio.I.SetLoopVolume(_pour.Pouring ? 0.6f : 0f);
            foreach (var e in _pour.Events)
            {
                var at = new Vector2(0.5f * _field.rect.width, 0.86f * _field.rect.height);
                switch (e.Kind)
                {
                    case "perfect": Pop(at, "พอดีเป๊ะ! +" + e.Points, new Color(0.2f, 0.6f, 0.2f)); Sparkle(at); GameAudio.Sfx("sfx_star"); break;
                    case "good": Pop(at, "ดีมาก +" + e.Points, new Color(0.95f, 0.55f, 0.1f)); GameAudio.Sfx("sfx_good"); break;
                    case "under": Pop(at, "น้อยไปนิด +" + e.Points, UiKit.Muted); GameAudio.Sfx("sfx_tap"); break;
                    case "over": Pop(at, "เยอะไปนิด +" + e.Points, UiKit.Muted); GameAudio.Sfx("sfx_tap"); break;
                    case "spill": Pop(at, "หกแล้ว!", new Color(0.6f, 0.3f, 0.2f)); GameAudio.Sfx("sfx_miss"); break;
                }
            }
        }
    }

    /// <summary>Where the finger is on the play field (0..1 across) and whether it's down.</summary>
    public sealed class FieldInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public bool Down { get; private set; }
        public bool Dragged { get; private set; }
        public float X { get; private set; } = 0.5f;

        public void OnPointerDown(PointerEventData e) { Down = true; Track(e); }
        public void OnDrag(PointerEventData e) { Dragged = true; Track(e); }
        public void OnPointerUp(PointerEventData e) { Down = false; Dragged = false; }

        private void OnDisable() { Down = false; Dragged = false; }

        private void Track(PointerEventData e)
        {
            var r = (RectTransform)transform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(r, e.position, e.pressEventCamera, out local)) return;
            X = Mathf.Clamp01((local.x - r.rect.xMin) / r.rect.width);
        }
    }

    /// <summary>Floats a label/sprite upward and fades it out.</summary>
    public sealed class FloatUp : MonoBehaviour
    {
        public float Rise = 120f;
        private float _age;
        private Graphic _g;

        private void Awake() => _g = GetComponent<Graphic>();

        private void Update()
        {
            _age += Time.deltaTime;
            ((RectTransform)transform).anchoredPosition += Vector2.up * Rise * Time.deltaTime;
            if (_g != null) { var c = _g.color; c.a = Mathf.Clamp01(1.2f - _age); _g.color = c; }
            if (_age > 1.2f) Destroy(gameObject);
        }
    }

    /// <summary>A result star that pops in (with a ding) after a delay.</summary>
    public sealed class StarPop : MonoBehaviour
    {
        public float Delay;
        private float _t;
        private bool _shown;

        private void Update()
        {
            _t += Time.deltaTime;
            if (!_shown && _t >= Delay)
            {
                _shown = true;
                GetComponent<Image>().sprite = MinigameScreen.Sp("star_on");
                GameAudio.Sfx("sfx_star");
            }
            var k = _shown ? Mathf.Clamp01((_t - Delay) / 0.25f) : 0f;
            transform.localScale = Vector3.one * (_shown ? 1f + 0.35f * Mathf.Sin(k * Mathf.PI) : 1f);
        }
    }
}
