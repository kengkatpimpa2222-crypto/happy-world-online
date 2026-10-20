using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using Avatar = HappyWorld.Core.Api.Avatar; // UnityEngine also has an Avatar type
using HappyWorld.Core.Localization;
using HappyWorld.Core.Minigames;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using UnityEngine;
using UnityEngine.UI;
using HappyWorld.Unity.Minigames;

namespace HappyWorld.Unity.UI
{
    /// <summary>
    /// All screens, built in code. Every action goes through GameSession.Run so server errors show as Thai toasts.
    /// Prices, rewards and balances shown here always come from the server's response, never from client maths.
    /// </summary>
    public sealed class GameScreens
    {
        private readonly GameSession _s;
        private readonly RectTransform _root, _screen, _hud, _modalLayer, _toastLayer;
        private RectTransform _banner;
        private Text _nameText, _moneyText, _zoneText, _cashText;
        private Text _festivalText;
        private Text _dailyText;
        private Image _dailyBg;
        private Text _cityText;
        private Image _festivalBg;
        private float _lastSaleToast = -999f;
        // Phase 10: homes
        private Button _prompt;
        private Text _promptText;
        private string _promptShown;
        private Action _promptAction;
        private RectTransform _homeBar, _decorate, _bagRow, _heldRow;
        private Text _decorateHint;
        private Button _saveButton;
        // Phase 9.1: live trip countdown on the HUD button and the explore screen
        private Text _exploreText, _tripStatus, _tripButtonText;
        private Button _tripButton;
        private Image _tripButtonBg;
        private double _tripTickAt;
        private string _exploreLabel;

        public VirtualJoystick Joystick { get; private set; }
        public bool ModalOpen => _modalLayer.childCount > 0;

        private static readonly string[] BodyColors = { "#F5C6A5", "#E0A47A", "#B97A56", "#8D5524", "#6FA8DC", "#93C47D", "#F6B26B", "#C27BA0" };

        public GameScreens(GameSession session)
        {
            _s = session;
            var canvas = UiKit.CreateCanvas();
            UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
            _root = (RectTransform)canvas.transform;
            _hud = UiKit.Rect("Hud", _root).Place(Vector2.zero, Vector2.one);
            _screen = UiKit.Rect("Screen", _root).Place(Vector2.zero, Vector2.one);
            _modalLayer = UiKit.Rect("Modals", _root).Place(Vector2.zero, Vector2.one);
            _toastLayer = UiKit.Rect("Toasts", _root).Place(Vector2.zero, Vector2.one);
            BuildHud();
            _hud.gameObject.SetActive(false);
            _s.Trips.Arrived += OnTripArrived;
        }

        // ---------- full screens ----------

        private RectTransform NewScreen()
        {
            UiKit.Clear(_screen);
            _hud.gameObject.SetActive(false);
            UiKit.Clear(_modalLayer);
            var bg = UiKit.Box("Bg", _screen, new Color(0.62f, 0.85f, 1f)).rectTransform.Place(Vector2.zero, Vector2.one);
            var card = UiKit.Box("Card", bg, UiKit.Panel).Round(true, 1.4f).rectTransform.Place(new Vector2(0.25f, 0.06f), new Vector2(0.75f, 0.94f));
            return card;
        }

        public void ShowLoading(string text)
        {
            var card = NewScreen();
            UiKit.Label(card, text, 44, TextAnchor.MiddleCenter).rectTransform.Place(Vector2.zero, Vector2.one);
        }

        public void ShowLogin(string error)
        {
            var card = NewScreen();
            UiKit.Label(card, "Happy World Online", 64, TextAnchor.MiddleCenter, UiKit.Primary).rectTransform
                .Place(new Vector2(0, 0.82f), new Vector2(1, 0.97f));
            UiKit.Label(card, Texts.T("ui.age_band") + "\n<size=26>เราเก็บแค่ช่วงอายุ เพื่อให้เล่นกับเพื่อนวัยเดียวกันอย่างปลอดภัย</size>", 36, TextAnchor.MiddleCenter)
                .rectTransform.Place(new Vector2(0.05f, 0.64f), new Vector2(0.95f, 0.8f));

            string selected = null;
            var bands = new[] { AgeBand.Under13, AgeBand.Teen, AgeBand.Adult };
            var labels = new[] { "ui.age.under13", "ui.age.teen", "ui.age.adult" };
            var buttons = new List<Image>();
            Text status = null;
            for (int i = 0; i < 3; i++)
            {
                var band = bands[i];
                var b = UiKit.Button(card, Texts.T(labels[i]), UiKit.Muted, () => { }, 34);
                b.GetComponent<RectTransform>().Place(new Vector2(0.06f + i * 0.3f, 0.5f), new Vector2(0.34f + i * 0.3f, 0.61f));
                var img = (Image)b.targetGraphic;
                buttons.Add(img);
                b.onClick.AddListener(() =>
                {
                    selected = band;
                    foreach (var x in buttons) x.color = UiKit.Muted;
                    img.color = UiKit.Primary;
                    status.text = "";
                });
            }

            InputField server = null;
            if (Application.isEditor || Debug.isDebugBuild)
            {
                UiKit.Label(card, "Server (dev)", 26, TextAnchor.MiddleLeft, UiKit.Muted).rectTransform
                    .Place(new Vector2(0.06f, 0.4f), new Vector2(0.94f, 0.46f));
                server = UiKit.Input(card, "http://192.168.1.10:3000", 120);
                server.text = GameSession.ServerUrl;
                server.GetComponent<RectTransform>().Place(new Vector2(0.06f, 0.31f), new Vector2(0.94f, 0.4f));
            }

            status = UiKit.Label(card, error ?? "", 30, TextAnchor.MiddleCenter, UiKit.Danger);
            status.rectTransform.Place(new Vector2(0.05f, 0.21f), new Vector2(0.95f, 0.3f));

            var play = UiKit.Button(card, Texts.T("ui.play"), UiKit.Good, () =>
            {
                if (selected == null)
                {
                    status.text = "กรุณาเลือกช่วงอายุก่อน";
                    return;
                }
                if (server != null && server.text.Trim().Length > 0) GameSession.ServerUrl = server.text;
                _s.Run(() => _s.NewGuestAsync(selected));
            }, 44);
            play.GetComponent<RectTransform>().Place(new Vector2(0.25f, 0.06f), new Vector2(0.75f, 0.18f));
        }

        public void ShowCharacterCreator(PlayerProfile profile, List<InventoryItem> inventory, bool firstTime)
        {
            var card = firstTime ? NewScreen() : OpenModal(Texts.T("ui.profile"), false);
            if (!firstTime) UiKit.Clear(card);
            UiKit.Label(card, Texts.T("ui.create_character"), 50, TextAnchor.MiddleCenter, UiKit.Primary).rectTransform
                .Place(new Vector2(0, 0.88f), new Vector2(1, 0.98f));

            UiKit.Label(card, Texts.T("ui.name"), 30).rectTransform.Place(new Vector2(0.06f, 0.8f), new Vector2(0.94f, 0.86f));
            var name = UiKit.Input(card, Texts.T("ui.name"), 16);
            name.text = profile.displayName;
            name.GetComponent<RectTransform>().Place(new Vector2(0.06f, 0.71f), new Vector2(0.94f, 0.8f));

            var avatar = new Avatar
            {
                bodyColor = profile.avatar != null && profile.avatar.bodyColor != null ? profile.avatar.bodyColor : BodyColors[0],
                outfitItemId = profile.avatar != null ? profile.avatar.outfitItemId : null,
                hairItemId = profile.avatar != null ? profile.avatar.hairItemId : null,
                accessoryItemId = profile.avatar != null ? profile.avatar.accessoryItemId : null,
            };

            UiKit.Label(card, Texts.T("ui.color"), 30).rectTransform.Place(new Vector2(0.06f, 0.63f), new Vector2(0.94f, 0.69f));
            var swatches = new List<Outline>();
            for (int i = 0; i < BodyColors.Length; i++)
            {
                var hex = BodyColors[i];
                Color c;
                ColorUtility.TryParseHtmlString(hex, out c);
                var b = UiKit.Button(card, "", c, () => { }, 20);
                b.GetComponent<RectTransform>().Place(new Vector2(0.06f + i * 0.111f, 0.53f), new Vector2(0.155f + i * 0.111f, 0.62f));
                var o = b.gameObject.AddComponent<Outline>();
                o.effectDistance = new Vector2(5, 5);
                o.effectColor = hex == avatar.bodyColor ? UiKit.Ink : Color.clear;
                swatches.Add(o);
                b.onClick.AddListener(() =>
                {
                    avatar.bodyColor = hex;
                    foreach (var s in swatches) s.effectColor = Color.clear;
                    o.effectColor = UiKit.Ink;
                });
            }

            // clothes: only items the player owns (the server rejects anything else)
            Cycler(card, 0.42f, "ชุด", inventory, "OUTFIT", avatar.outfitItemId, v => avatar.outfitItemId = v);
            Cycler(card, 0.31f, "ทรงผม", inventory, "HAIR", avatar.hairItemId, v => avatar.hairItemId = v);
            Cycler(card, 0.20f, "เครื่องประดับ", inventory, "ACCESSORY", avatar.accessoryItemId, v => avatar.accessoryItemId = v);

            var save = UiKit.Button(card, firstTime ? Texts.T("ui.play") : Texts.T("ui.save"), UiKit.Good, () =>
                _s.Run(() => _s.SaveCharacterAsync(name.text.Trim(), avatar, firstTime)), 40);
            save.GetComponent<RectTransform>().Place(new Vector2(0.25f, 0.04f), new Vector2(0.75f, 0.15f));
        }

        private static void Cycler(RectTransform card, float y, string label, List<InventoryItem> inv, string category, string current, Action<string> set)
        {
            var options = new List<string> { null };
            options.AddRange(inv.Where(i => i.category == category).Select(i => i.itemId));
            int idx = Math.Max(0, options.IndexOf(current));
            UiKit.Label(card, label, 30).rectTransform.Place(new Vector2(0.06f, y), new Vector2(0.3f, y + 0.09f));
            var value = UiKit.Label(card, "", 30, TextAnchor.MiddleCenter);
            value.rectTransform.Place(new Vector2(0.42f, y), new Vector2(0.82f, y + 0.09f));
            Action show = () => value.text = options[idx] == null ? "ไม่ใส่" : Texts.T(options[idx]);
            show();
            UiKit.Button(card, "◀", UiKit.Primary, () => { idx = (idx + options.Count - 1) % options.Count; set(options[idx]); show(); })
                .GetComponent<RectTransform>().Place(new Vector2(0.32f, y + 0.01f), new Vector2(0.4f, y + 0.08f));
            UiKit.Button(card, "▶", UiKit.Primary, () => { idx = (idx + 1) % options.Count; set(options[idx]); show(); })
                .GetComponent<RectTransform>().Place(new Vector2(0.84f, y + 0.01f), new Vector2(0.92f, y + 0.08f));
        }

        // ---------- HUD ----------

        private void BuildHud()
        {
            Joystick = VirtualJoystick.Create(_hud);

            var top = UiKit.Box("TopBar", _hud, new Color(1, 1, 1, 0.85f)).Round(true).rectTransform.Place(new Vector2(0, 1), new Vector2(0.36f, 1), new Vector2(16, -200), new Vector2(0, -16));
            _nameText = UiKit.Label(top, "", 32);
            _nameText.rectTransform.Place(new Vector2(0, 0.66f), Vector2.one, new Vector2(20, 0), new Vector2(-10, 0));
            _moneyText = UiKit.Label(top, "", 32);
            _moneyText.rectTransform.Place(new Vector2(0, 0.33f), new Vector2(1, 0.66f), new Vector2(20, 0), new Vector2(-10, 0));
            // Phase 5: Gold waiting in the shop's cash box (tap to manage the shop)
            var cash = UiKit.Button(top, "", new Color(1f, 0.85f, 0.35f, 0.9f), ShowShop, 28);
            cash.GetComponent<RectTransform>().Place(Vector2.zero, new Vector2(1, 0.33f), new Vector2(10, 6), new Vector2(-10, -2));
            _cashText = cash.GetComponentInChildren<Text>();
            _cashText.color = UiKit.Ink;

            _zoneText = UiKit.Label(_hud, "", 36, TextAnchor.UpperCenter, Color.white);
            _zoneText.gameObject.AddComponent<Shadow>();
            _zoneText.rectTransform.Place(new Vector2(0.38f, 0.9f), new Vector2(0.62f, 0.98f));

            // Phase 7: Bangkok time of day, weather and season (tap for the forecast and the city's projects)
            _cityText = UiKit.Label(_hud, "", 26, TextAnchor.UpperCenter, Color.white);
            _cityText.gameObject.AddComponent<Shadow>();
            _cityText.rectTransform.Place(new Vector2(0.34f, 0.765f), new Vector2(0.66f, 0.815f));
            _cityText.raycastTarget = true; // labels are not tappable by default
            var cityTap = _cityText.gameObject.AddComponent<Button>();
            cityTap.onClick.AddListener(ShowCity);

            // Phase 11: daily check-in and missions, right under the money bar (a big touch target, never a pop-up)
            var dailyBtn = UiKit.Button(_hud, Texts.T("ui.daily"), new Color(1f, 0.93f, 0.7f, 0.95f), ShowDaily, 28);
            dailyBtn.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(0.36f, 1), new Vector2(16, -296), new Vector2(0, -212));
            _dailyBg = dailyBtn.GetComponent<Image>();
            _dailyText = dailyBtn.GetComponentInChildren<Text>();
            _dailyText.color = UiKit.Ink;

            // Phase 6: this week's city festival (tap for goals and rewards)
            var fest = UiKit.Button(_hud, Texts.T("ui.festival"), new Color(1f, 0.84f, 0.31f, 0.92f), ShowFestival, 26);
            fest.GetComponent<RectTransform>().Place(new Vector2(0.36f, 0.82f), new Vector2(0.64f, 0.895f));
            _festivalBg = fest.GetComponent<Image>();
            _festivalText = fest.GetComponentInChildren<Text>();
            _festivalText.color = UiKit.Ink;

            var buttons = new (string key, Action open)[]
            {
                ("ui.profile", ShowProfile),
                ("ui.friends", ShowFriends),
                ("ui.quests", ShowQuests),
                ("ui.bag", ShowBag),
                ("ui.store", ShowStore),
                ("ui.shop", ShowShop),
                ("ui.home", ShowHome),
                ("ui.explore", ShowExplore),
                ("ui.city", ShowCity),
                ("ui.go_market", () => _s.Run(() => _s.JoinZoneAsync("MARKET"))),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                var hb = UiKit.Button(_hud, Texts.T(b.key), UiKit.Primary, () => b.open(), 30);
                hb.GetComponent<RectTransform>()
                    .Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-230, -96 - i * 86), new Vector2(-16, -16 - i * 86)); // 10 buttons clear the chat button
                if (b.key == "ui.explore") _exploreText = hb.GetComponentInChildren<Text>();
            }
            UiKit.Button(_hud, Texts.T("ui.chat"), UiKit.Good, ShowChat, 36).GetComponent<RectTransform>()
                .Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-300, 40), new Vector2(-40, 170));

            // Phase 10: "go in / walk out" when standing at a door (E on PC)
            _prompt = UiKit.Button(_hud, "", UiKit.Accent, () => _promptAction?.Invoke(), 32);
            _prompt.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-260, 200), new Vector2(260, 300));
            _promptText = _prompt.GetComponentInChildren<Text>();
            _prompt.gameObject.SetActive(false);

            // Phase 10: inside a home: decorate (my home) and walk out
            _homeBar = UiKit.Rect("HomeBar", _hud).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-330, 40), new Vector2(330, 150));
            UiKit.Button(_homeBar, Texts.T("ui.decorate"), UiKit.Good, () => _s.Run(_s.StartDecoratingAsync), 30).GetComponent<RectTransform>()
                .Place(new Vector2(0, 0), new Vector2(0.49f, 1));
            UiKit.Button(_homeBar, "ออกจากบ้าน", UiKit.Muted, () => _s.Run(_s.LeaveHomeAsync), 30).GetComponent<RectTransform>()
                .Place(new Vector2(0.51f, 0), new Vector2(1, 1));
            _homeBar.gameObject.SetActive(false);
        }

        // ---------- first home (Phase 10) ----------

        /// <summary>The door button: shown with text, hidden with null. Only rebuilt when the text changes.</summary>
        public void SetPrompt(string text, Action action)
        {
            _promptAction = action;
            if (text == _promptShown) return;
            _promptShown = text;
            _prompt.gameObject.SetActive(text != null);
            if (text == null) return;
            _promptText.text = text + (action != null && !Application.isMobilePlatform ? "  [E]" : "");
            _prompt.GetComponent<Image>().color = action != null ? UiKit.Accent : UiKit.Muted;
        }

        public void SetHomeMode(bool inHome, bool mine)
        {
            _homeBar.gameObject.SetActive(inHome);
            _homeBar.GetChild(0).gameObject.SetActive(mine); // only the owner decorates
        }

        /// <summary>My home: claim it, its address, who may visit, go home / decorate, and friends' homes.</summary>
        public void ShowHome()
        {
            var list = OpenModal(Texts.T("ui.home"));
            Loading(list);
            _s.Run(async () =>
            {
                var h = await _s.LoadMyHomeAsync();
                UiKit.Clear(list);
                if (!h.claimed)
                {
                    UiKit.Row(list, "ทุกคนได้บ้านหลังแรกฟรีในย่านบ้าน พร้อมเตียง โต๊ะ และกระถางต้นไม้ไว้แต่งบ้าน", 120);
                    UiKit.RowButton(UiKit.Row(list, "<b>บ้านหลังแรกของคุณ</b>"), "รับบ้าน", UiKit.Good, () =>
                    {
                        CloseModal();
                        _s.Run(_s.ClaimHomeAsync);
                    }, 200);
                    return;
                }
                UiKit.Row(list, "<b>" + HomePresenter.Address(h.plot) + "</b>\n<size=24>เฟอร์นิเจอร์ในบ้าน " + h.furniture.Count + " ชิ้น</size>");
                var go = UiKit.Row(list, _s.CurrentHome != null && _s.CurrentHome.isMine ? "คุณอยู่ในบ้านตอนนี้" : "เดินกลับบ้าน");
                if (_s.CurrentHome != null && _s.CurrentHome.isMine)
                    UiKit.RowButton(go, Texts.T("ui.decorate"), UiKit.Good, () => { CloseModal(); _s.Run(_s.StartDecoratingAsync); }, 200);
                else
                    UiKit.RowButton(go, Texts.T("ui.go_home"), UiKit.Good, () => { CloseModal(); _s.Run(() => _s.EnterHomeAsync(_s.Profile.userId)); }, 200);

                UiKit.Row(list, "<b>ใครเข้าบ้านได้บ้าง</b>  <size=24>(ตอนนี้: " + HomePresenter.PolicyName(h.visitPolicy) + ")</size>", 70);
                foreach (var policy in HomePresenter.Policies)
                {
                    var pol = policy;
                    var row = UiKit.Row(list, HomePresenter.PolicyName(pol) + "\n<size=22>" + HomePresenter.PolicyBlurb(pol) + "</size>");
                    if (pol == h.visitPolicy) UiKit.RowButton(row, "✓ ใช้อยู่", UiKit.Muted, () => { }, 160);
                    else UiKit.RowButton(row, "เลือก", UiKit.Primary, () => _s.Run(async () =>
                    {
                        await _s.Api.SetHomeVisitPolicyAsync(pol);
                        Toast("ตั้งเป็น " + HomePresenter.PolicyName(pol) + " แล้ว");
                        ShowHome();
                    }), 160);
                }

                var friends = await _s.Api.GetFriendsAsync();
                UiKit.Row(list, "<b>บ้านเพื่อน</b>", 60);
                if (friends.Count == 0) UiKit.Row(list, "<size=24>มีเพื่อนแล้วจะไปเยี่ยมบ้านเพื่อนได้ (ถ้าเพื่อนเปิดให้เยี่ยม)</size>", 70);
                foreach (var f in friends)
                {
                    var row = UiKit.Row(list, f.displayName + "  Lv." + f.level);
                    if (!f.canVisitHome)
                    {
                        UiKit.RowButton(row, "ยังไม่เปิด", UiKit.Muted, () => { }, 160);
                        continue;
                    }
                    var fid = f.userId;
                    UiKit.RowButton(row, "ไปเยี่ยม", UiKit.Primary, () =>
                    {
                        CloseModal();
                        _s.Run(() => _s.EnterHomeAsync(fid));
                    }, 160);
                }
            });
        }

        // ---- decorate toolbar ----

        public void ShowDecorate()
        {
            CloseModal();
            HideDecorate();
            Joystick.gameObject.SetActive(false);
            _homeBar.gameObject.SetActive(false);
            _decorate = UiKit.Box("Decorate", _hud, new Color(1, 1, 1, 0.92f)).Round(true).rectTransform
                .Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 16), new Vector2(-16, 330));
            _decorateHint = UiKit.Label(_decorate, "", 28);
            _decorateHint.rectTransform.Place(new Vector2(0, 0.78f), new Vector2(0.62f, 1), new Vector2(24, 0), new Vector2(0, -6));
            _saveButton = UiKit.Button(_decorate, "บันทึก", UiKit.Good, () => _s.Run(_s.SaveDecoratingAsync), 30);
            _saveButton.GetComponent<RectTransform>().Place(new Vector2(0.64f, 0.74f), new Vector2(0.82f, 1), new Vector2(0, 0), new Vector2(-6, -8));
            UiKit.Button(_decorate, "ยกเลิก", UiKit.Muted, () => _s.StopDecorating(true), 30).GetComponent<RectTransform>()
                .Place(new Vector2(0.82f, 0.74f), new Vector2(1, 1), new Vector2(6, 0), new Vector2(-12, -8));
            _bagRow = HRow(_decorate, new Vector2(0, 0.38f), new Vector2(1, 0.72f));
            _heldRow = HRow(_decorate, new Vector2(0, 0.02f), new Vector2(1, 0.36f));
            RefreshDecorate();
        }

        public void HideDecorate()
        {
            if (_decorate != null) UnityEngine.Object.Destroy(_decorate.gameObject);
            _decorate = null;
            Joystick.gameObject.SetActive(true);
            if (_s.CurrentHome != null) SetHomeMode(true, _s.CurrentHome.isMine);
        }

        /// <summary>Redraws the bag (what can be placed) and the buttons for the piece in hand.</summary>
        public void RefreshDecorate()
        {
            var e = _s.Editor;
            if (_decorate == null || e == null) return;
            _decorateHint.text = _s.Saving ? "กำลังบันทึก..." : HomePresenter.EditHint(e);
            _decorateHint.color = e.Held != null && !e.HeldFits ? UiKit.Danger : UiKit.Ink;
            _saveButton.interactable = !_s.Saving && e.Dirty;
            UiKit.Clear(_bagRow);
            UiKit.Clear(_heldRow);
            var items = e.BagItems.ToList();
            if (items.Count == 0) UiKit.Label(_bagRow, "<size=24>กระเป๋าเฟอร์นิเจอร์ว่าง ซื้อเพิ่มได้ที่ร้านค้า</size>", 24).gameObject.AddComponent<LayoutElement>().preferredWidth = 700;
            foreach (var id in items)
            {
                var itemId = id;
                var fp = e.Footprint(itemId);
                RowButton(_bagRow, Texts.T(itemId) + " ×" + e.InBag(itemId) + "\n<size=20>" + fp[0] + "×" + fp[1] + " ช่อง</size>", e.Held == null ? UiKit.Primary : UiKit.Muted, () =>
                {
                    if (_s.Editor != null && _s.Editor.TakeFromBag(itemId)) _s.EditorChanged();
                }, 230);
            }
            if (e.Held == null)
            {
                UiKit.Label(_heldRow, "<size=24>แตะของในห้องเพื่อหยิบขึ้นมาย้าย หมุน หรือเก็บ</size>", 24).gameObject.AddComponent<LayoutElement>().preferredWidth = 900;
                return;
            }
            UiKit.Label(_heldRow, "ในมือ: " + Texts.T(e.Held.ItemId), 28).gameObject.AddComponent<LayoutElement>().preferredWidth = 330;
            RowButton(_heldRow, "หมุน ⟳", UiKit.Primary, () => { _s.Editor?.RotateHeld(); _s.EditorChanged(); }, 170);
            RowButton(_heldRow, "วาง", e.HeldFits ? UiKit.Good : UiKit.Muted, () =>
            {
                if (_s.Editor == null) return;
                if (!_s.Editor.Drop()) Toast(Texts.T("HOME_PLACEMENT_INVALID"));
                _s.EditorChanged();
            }, 150);
            RowButton(_heldRow, "เก็บเข้ากระเป๋า", UiKit.Accent, () => { _s.Editor?.PutHeldInBag(); _s.EditorChanged(); }, 230);
            RowButton(_heldRow, "ไม่เอาแล้ว", UiKit.Muted, () => { _s.Editor?.CancelHeld(); _s.EditorChanged(); }, 180);
        }

        private static RectTransform HRow(Transform parent, Vector2 min, Vector2 max)
        {
            var view = UiKit.Rect("HScroll", parent).Place(min, max, new Vector2(16, 0), new Vector2(-16, 0));
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f); // catches drags for scrolling
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.vertical = false;
            var content = UiKit.Rect("Content", view);
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var h = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.padding = new RectOffset(4, 4, 6, 6);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = view;
            return content;
        }

        private static void RowButton(RectTransform row, string text, Color color, Action onClick, float width)
        {
            var b = UiKit.Button(row, text, color, onClick, 26);
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
        }

        public void ShowHud()
        {
            UiKit.Clear(_screen);
            UiKit.Clear(_modalLayer);
            _hud.gameObject.SetActive(true);
            RefreshHud();
        }

        public void SetZone(string name) => _zoneText.text = name;

        // Currency is spelled out (ทอง/เพชร): the bundled Thai font has no emoji glyphs, so 🪙/💎 rendered as
        // nothing on Android and prices showed as bare numbers (Phase 4.5).
        /// <summary>Quest and festival rewards can level the player up; the HUD reads the cached profile, so
        /// copy the new level in (it kept showing "Lv.1" after the server said 2, Phase 6 QA).</summary>
        private void ApplyLevel(int? level)
        {
            if (level.HasValue && _s.Profile != null) _s.Profile.level = level.Value;
        }

        public void RefreshHud()
        {
            var p = _s.Profile;
            if (p != null) _nameText.text = p.displayName + "  Lv." + p.level;
            _s.RefreshDaily(); // most actions that move a mission end here (sell, collect, craft, claim)
            _s.Run(async () =>
            {
                var w = await _s.Api.GetWalletAsync();
                _moneyText.text = Texts.T("ui.gold") + " " + w.Gold + "   " + Texts.T("ui.gems") + " " + w.Gems;
                var shops = await _s.Api.GetBusinessesAsync();
                if (shops.Count > 0) SetCashBox(shops[0].stall);
            });
        }

        private void SetCashBox(ShopStall st)
        {
            if (st == null) return;
            var state = st.status == "OPEN" ? "ร้านเปิดอยู่" : st.status == "PAUSED" ? "ร้านพักขาย" : "ร้านปิด";
            _cashText.text = state + " · ลิ้นชัก " + st.cashBox + " ทอง";
        }

        /// <summary>shop:updated from the server: customers bought something at my stall.</summary>
        public void OnShopUpdated(ShopUpdated u)
        {
            _cashText.text = "ร้านเปิดอยู่ · ลิ้นชัก " + u.cashBox + " ทอง";
            if (u.unitsSold > 0 && Time.time - _lastSaleToast > 45f && !ModalOpen)
            {
                _lastSaleToast = Time.time;
                Toast("ลูกค้าซื้อของที่ร้าน " + u.unitsSold + " ชิ้น (+" + u.revenue + " ทอง เข้าลิ้นชัก)", "ดูร้าน", ShowShop);
            }
        }

        // ---------- city festival (Phase 6) ----------

        /// <summary>Updates the HUD festival button from GET /festival.</summary>
        public void SetFestival(FestivalState st)
        {
            if (_festivalText == null || st == null || st.current == null) return;
            var f = st.current;
            float r, g, b;
            FestivalPresenter.ParseColor(f.color, out r, out g, out b);
            _festivalBg.color = f.status == "UPCOMING" ? new Color(1f, 1f, 1f, 0.85f) : new Color(r, g, b, 0.92f);
            var claimable = f.ClaimableCount() + (st.previous != null ? st.previous.ClaimableCount() : 0);
            _festivalText.text = FestivalPresenter.Banner(f, _s.ServerNow) + (claimable > 0 ? "  <b>มีรางวัล!</b>" : "");
        }

        public void ShowFestival()
        {
            var list = OpenModal(Texts.T("ui.festival"));
            Loading(list);
            _s.Run(async () =>
            {
                var st = await _s.Api.GetFestivalAsync();
                _s.OnFestival(st);
                UiKit.Clear(list);
                FillFestival(list, st.current, true);
                if (st.previous != null)
                {
                    UiKit.Row(list, "<b>รางวัลจากเทศกาลที่แล้ว</b> (รับได้ถึงก่อนเทศกาลหน้าจบ)", 70);
                    FillFestival(list, st.previous, false);
                }
            });
        }

        private void FillFestival(RectTransform list, FestivalInfo f, bool full)
        {
            var now = _s.ServerNow;
            var name = FestivalPresenter.Name(f);
            var when = f.status == "UPCOMING" ? "เริ่มวันศุกร์ · " + FestivalPresenter.TimeLeft(f.startsAt - now)
                : f.Active ? "กำลังจัดงาน! จบ" + FestivalPresenter.TimeLeft(f.endsAt - now) : "จบแล้ว";
            UiKit.Row(list, "<b>" + name + "</b>\n<size=24>" + when + "</size>", 110);

            if (full)
            {
                var featured = string.Join("  ·  ", f.featured.Select(x => MarketNpcPlanner.ProductName(x.productCode) + " ลูกค้าอยากได้ x" + x.demand));
                var shopRow = UiKit.Row(list, "<size=26>ของเทศกาล: " + featured + "\nขายของเทศกาลที่แผงในตลาด ทุกชิ้นช่วยทั้งเมือง</size>", 110);
                UiKit.RowButton(shopRow, "จัดร้าน", UiKit.Good, ShowShop, 150);
            }

            // the city's progress, as a bar toward the next tier
            var next = f.NextTier();
            var cityRow = UiKit.Row(list, "<size=26>ทั้งเมืองขายของเทศกาลแล้ว <b>" + f.city.points + "</b> ชิ้น · ผู้ช่วย " + f.city.contributors + " คน" +
                (next != null ? " · ขั้นต่อไปที่ " + next.target : " · ครบทุกขั้นแล้ว!") + "</size>", 100);
            var track = UiKit.Box("Track", cityRow, new Color(0.88f, 0.88f, 0.9f)).rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 10), new Vector2(-20, 26));
            float r, g, b;
            FestivalPresenter.ParseColor(f.color, out r, out g, out b);
            UiKit.Box("Fill", track, new Color(r, g, b)).rectTransform.Place(Vector2.zero, new Vector2(FestivalPresenter.Progress(f), 1));

            foreach (var t in f.tiers)
            {
                var reward = Texts.T("ui.gold") + " " + t.rewardGold + "  XP " + t.rewardXp + (t.rewardItemId != null ? "  + " + Texts.T(t.rewardItemId) : "");
                var state = t.claimed ? "✓ " + Texts.T("ui.festival_claimed") : t.reached ? "<color=#2E8B3E>เมืองทำสำเร็จ!</color>" : "เป้า " + t.target + " ชิ้น";
                var row = UiKit.Row(list, "ขั้น " + t.tier + " " + Texts.T("festival.tier." + t.tier) + "  <size=24>" + state + "</size>\n<size=24>" + reward + "</size>", 110);
                var tier = t.tier;
                var key = f.key;
                if (t.claimable)
                    UiKit.RowButton(row, Texts.T("ui.festival_claim"), UiKit.Good, () => _s.Run(async () =>
                    {
                        var res = await _s.Api.ClaimFestivalAsync(key, tier);
                        Toast("ได้รับ " + Texts.T("ui.gold") + " " + res.rewards.gold + "  XP " + res.rewards.xp +
                              (res.rewards.itemId != null ? "  และ " + Texts.T(res.rewards.itemId) : "") + (res.level.HasValue ? "  เลเวล " + res.level : ""));
                        ApplyLevel(res.level);
                        RefreshHud();
                        ShowFestival();
                    }), 170);
            }

            var me = f.me.points >= f.me.minToClaim
                ? "คุณช่วยเมืองไปแล้ว <b>" + f.me.points + "</b> ชิ้น รับรางวัลทุกขั้นที่เมืองทำสำเร็จได้"
                : "คุณช่วยไปแล้ว " + f.me.points + " ชิ้น ขายของเทศกาลให้ครบ " + f.me.minToClaim + " ชิ้นเพื่อรับรางวัลของเมือง";
            UiKit.Row(list, "<size=26>" + me + "</size>", 80);

            if (full && f.top.Count > 0)
            {
                UiKit.Row(list, "<b>ผู้ช่วยแห่งเมือง</b>", 60);
                foreach (var h in f.top)
                    UiKit.Row(list, "<size=26>" + h.rank + ". " + FestivalPresenter.HelperName(h) + " · " + h.points + " ชิ้น</size>", 60);
            }
        }

        // ---------- daily rewards & missions (Phase 11) ----------

        /// <summary>Updates the HUD button from the last GET /daily.</summary>
        public void SetDaily(DailyState st)
        {
            if (_dailyText == null) return;
            _dailyText.text = DailyPresenter.HudLabel(st);
            _dailyBg.color = st != null && st.claimable > 0 ? new Color(1f, 0.8f, 0.3f, 0.97f) : new Color(1f, 0.93f, 0.7f, 0.95f);
        }

        /// <summary>Check-in track (7 tiles), today's 3 missions and this week's missions. All times are the server's.</summary>
        public void ShowDaily()
        {
            var list = OpenModal(Texts.T("ui.daily"));
            Loading(list);
            _s.Run(async () =>
            {
                var st = await _s.Api.GetDailyAsync();
                _s.OnDaily(st);
                if (list == null) return; // a newer ShowDaily replaced this modal meanwhile
                UiKit.Clear(list);
                FillDaily(list, st);
            });
        }

        private void FillDaily(RectTransform list, DailyState st)
        {
            var c = st.checkin;
            var now = _s.DailyNow;

            // check-in: headline + button, then the 7 tiles
            var head = UiKit.Row(list, "<b>" + DailyPresenter.CheckinHeadline(c) + "</b>", 100);
            if (!c.claimedToday)
                UiKit.RowButton(head, Texts.T("ui.checkin"), UiKit.Good, () => _s.Run(async () =>
                {
                    try
                    {
                        var r = await _s.Api.CheckinAsync();
                        Toast(DailyPresenter.CheckinToast(r));
                    }
                    finally
                    {
                        RefreshHud();
                        ShowDaily(); // also after CHECKIN_ALREADY_CLAIMED (checked in on another device): show the truth
                    }
                }), 200);
            var tiles = UiKit.Row(list, "", 168);
            tiles.GetChild(0).gameObject.SetActive(false); // the row's own label: tiles fill the row instead
            foreach (var step in c.track)
            {
                var color = step.state == "CLAIMED" ? new Color(0.82f, 0.93f, 0.84f) : step.state == "TODAY" ? new Color(1f, 0.85f, 0.42f) : Color.white;
                var tile = UiKit.Box("Day" + step.step, tiles, color).Round(false, 0.6f);
                var le = tile.gameObject.AddComponent<LayoutElement>();
                le.flexibleWidth = 1;
                le.minWidth = 60;
                var t = UiKit.Label(tile.transform, DailyPresenter.StepTile(step), 26, TextAnchor.MiddleCenter);
                t.resizeTextForBestFit = true; // long Thai item names on narrow phones
                t.resizeTextMinSize = 14;
                t.resizeTextMaxSize = 26;
                t.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            }
            UiKit.Row(list, "<size=24>" + DailyPresenter.NoPressureNote + "</size>", 64);

            FillMissions(list, "ภารกิจวันนี้", DailyPresenter.ResetLine(st.daily.endsAt, now, false), st.daily.missions);
            FillMissions(list, "ภารกิจสัปดาห์นี้", DailyPresenter.ResetLine(st.weekly.endsAt, now, true), st.weekly.missions);
        }

        private void FillMissions(RectTransform list, string title, string resetLine, List<Mission> missions)
        {
            UiKit.Row(list, "<b>" + title + "</b>  <size=24>" + resetLine + "</size>", 70);
            foreach (var m in DailyPresenter.Sorted(missions))
            {
                var when = m.fromPreviousPeriod ? "  <size=22><color=#C0392B>ของรอบที่แล้ว รับก่อนหมดเวลา</color></size>" : "";
                var reward = DailyPresenter.RewardText(m.rewards.gold, m.rewards.item, m.rewards.xp);
                var row = UiKit.Row(list, DailyPresenter.Title(m) + "  <size=24>" + DailyPresenter.ProgressText(m) + "</size>" + when +
                                          "\n<size=24>" + reward + "</size>", 120);
                var track = UiKit.Box("Track", row, new Color(0.88f, 0.88f, 0.9f)).rectTransform;
                track.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                track.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 8), new Vector2(-230, 20));
                UiKit.Box("Fill", track, m.Claimed ? UiKit.Muted : UiKit.Good).rectTransform.Place(Vector2.zero, new Vector2(DailyPresenter.Progress(m), 1));
                if (m.CanClaim)
                {
                    var periodKey = m.periodKey;
                    var id = m.id;
                    UiKit.RowButton(row, Texts.T("ui.claim"), UiKit.Good, () => _s.Run(async () =>
                    {
                        try
                        {
                            var r = await _s.Api.ClaimMissionAsync(periodKey, id);
                            Toast(DailyPresenter.ClaimToast(r));
                            ApplyLevel(r.level);
                        }
                        finally
                        {
                            RefreshHud();
                            ShowDaily();
                        }
                    }), 200);
                }
            }
        }

        // ---------- living city (Phase 7) ----------

        /// <summary>HUD line under the zone name: "14:05 ช่วงบ่าย · ฝนตก · หน้าฝน".</summary>
        public void SetCityLine(string line)
        {
            if (_cityText != null && _cityText.text != line) _cityText.text = line;
        }

        public void ShowCity()
        {
            var list = OpenModal(Texts.T("ui.city"));
            Loading(list);
            _s.Run(async () =>
            {
                var now = await _s.Api.GetCityNowAsync();
                var g = await _s.Api.GetCityGrowthAsync();
                _s.OnCityNow(now);
                _s.OnCityGrowth(g);
                UiKit.Clear(list);
                FillCity(list, now, g);
            });
        }

        private void FillCity(RectTransform list, CityNow n, CityGrowth g)
        {
            // time and weather: what it means for the shop right now
            UiKit.Row(list, "<b>" + CityLifePresenter.HudLine(n, n.hour) + "</b>\n<size=24>" + n.tipTh + "</size>", 110);
            var sells = n.demand.Where(d => d.demand >= 1.2f).Select(d => MarketNpcPlanner.ProductName(d.productCode)).ToList();
            var busy = n.busyTypes.Select(MarketNpcPlanner.CustomerName).ToList();
            var line = (sells.Count > 0 ? "ตอนนี้ขายดี: " + string.Join(", ", sells) : "อากาศแบบนี้ไม่มีของที่ขายดีเป็นพิเศษ") +
                       (busy.Count > 0 ? "\nช่วงนี้มีลูกค้าเยอะ: " + string.Join(", ", busy) : "");
            var shopRow = UiKit.Row(list, "<size=24>" + line + "\n" + CityLifePresenter.ForecastLine(n, 3) + "</size>", 120);
            UiKit.RowButton(shopRow, "จัดร้าน", UiKit.Good, ShowShop, 150);

            // the city's projects
            UiKit.Row(list, "<b>เมืองของเราเติบโต</b>\n<size=24>ทุกชิ้นที่ขายได้ในตลาด = 1 แต้มสร้างเมือง · ทั้งเมืองมี " + g.points + " แต้ม · สร้างแล้ว " + g.builtCount + "/" + g.projects.Count + "</size>", 100);
            foreach (var p in g.projects)
            {
                var state = p.Done ? "<color=#2E8B3E>สร้างเสร็จแล้ว</color>" : p.Building ? "กำลังสร้าง " + p.progress + "/" + p.cost : "<color=#888888>รอคิว (" + p.cost + " แต้ม)</color>";
                var row = UiKit.Row(list, "<b>" + CityLifePresenter.ProjectName(p) + "</b>  <size=24>" + state + " · " + Texts.T("zone." + p.zone.ToLowerInvariant()) + "</size>\n<size=24>" + p.effectTh + "</size>", 110);
                if (p.Building)
                {
                    var track = UiKit.Box("Track", row, new Color(0.88f, 0.88f, 0.9f)).rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 6), new Vector2(-20, 20));
                    UiKit.Box("Fill", track, new Color(0.95f, 0.65f, 0.2f)).rectTransform.Place(Vector2.zero, new Vector2(CityLifePresenter.Progress(p), 1));
                }
            }
            UiKit.Row(list, "<size=26>คุณช่วยสร้างเมืองไปแล้ว <b>" + g.me.total + "</b> แต้ม" + (g.current != null ? " (โครงการนี้ " + g.me.points + ")" : "") + "</size>", 70);
            if (g.top.Count > 0)
            {
                UiKit.Row(list, "<b>ช่างใหญ่ของเมือง</b>", 60);
                foreach (var h in g.top)
                    UiKit.Row(list, "<size=26>" + h.rank + ". " + FestivalPresenter.HelperName(h) + " · " + h.points + " แต้ม</size>", 60);
            }
        }

        // ---------- modal ----------

        /// <summary>Opens a centred panel. Returns a scroll list (list=true) or the bare panel.</summary>
        private RectTransform OpenModal(string title, bool list = true)
        {
            UiKit.Clear(_modalLayer);
            var dim = UiKit.Box("Dim", _modalLayer, new Color(0, 0, 0, 0.45f)).rectTransform.Place(Vector2.zero, Vector2.one);
            var panel = UiKit.Box("Panel", dim, UiKit.Panel).Round(true, 1.4f).rectTransform.Place(new Vector2(0.18f, 0.06f), new Vector2(0.82f, 0.94f));
            UiKit.Label(panel, title, 44, TextAnchor.MiddleLeft, UiKit.Primary).rectTransform
                .Place(new Vector2(0, 0.88f), Vector2.one, new Vector2(30, 0), new Vector2(-200, 0));
            UiKit.Button(panel, "✕ " + Texts.T("ui.close"), UiKit.Muted, CloseModal, 28).GetComponent<RectTransform>()
                .Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-190, -90), new Vector2(-16, -16));
            var body = UiKit.Rect("Body", panel).Place(Vector2.zero, new Vector2(1, 0.88f), new Vector2(16, 16), new Vector2(-16, 0));
            return list ? UiKit.ScrollList(body) : body;
        }

        public void CloseModal()
        {
            UiKit.Clear(_modalLayer);
            RefreshHud();
        }

        private void Loading(RectTransform list) => UiKit.Row(list, Texts.T("ui.loading"));

        // ---------- panels ----------

        private void ShowProfile()
        {
            var list = OpenModal(Texts.T("ui.profile"));
            _s.Run(async () =>
            {
                var p = await _s.Api.GetProfileAsync();
                UiKit.Clear(list);
                UiKit.Row(list, p.displayName + "   " + Texts.T("ui.level") + " " + p.level);
                UiKit.Row(list, "XP " + p.xp + " / " + p.xpForNextLevel);
                UiKit.Row(list, Texts.T("ui.friend_code") + ":  <b>" + p.friendCode + "</b>\n<size=24>บอกรหัสนี้ให้เพื่อนเพื่อเพิ่มเพื่อน</size>", 120);
                UiKit.RowButton(UiKit.Row(list, "แต่งตัวละครใหม่"), "แก้ไข", UiKit.Primary, () =>
                    _s.Run(async () => _s_ShowCreator(p, await _s.Api.GetInventoryAsync())));
                SoundRow(list, "เพลงประกอบ", () => Audio.GameAudio.MusicVolume, v => Audio.GameAudio.MusicVolume = v, 0.55f);
                SoundRow(list, "เสียงเอฟเฟกต์", () => Audio.GameAudio.SfxVolume, v => Audio.GameAudio.SfxVolume = v, 0.9f);
                UiKit.RowButton(UiKit.Row(list, "ออกจากระบบ\n<size=22>บัญชีผู้เล่นทั่วไปจะเข้าคืนไม่ได้ถ้ายังไม่ผูกอีเมล</size>", 110), "ออก", UiKit.Danger,
                    () => _s.Run(_s.LogoutAsync));
            });
        }

        /// <summary>On/off for music or sound effects (saved on this device).</summary>
        private static void SoundRow(RectTransform list, string label, Func<float> get, Action<float> set, float onValue)
        {
            var row = UiKit.Row(list, label);
            Button b = null;
            Action paint = () =>
            {
                var on = get() > 0f;
                b.GetComponentInChildren<Text>().text = on ? "เปิดอยู่" : "ปิดอยู่";
                b.GetComponent<Image>().color = on ? UiKit.Good : UiKit.Muted;
            };
            b = UiKit.RowButton(row, "", UiKit.Good, () =>
            {
                set(get() > 0f ? 0f : onValue);
                paint();
            }, 170);
            paint();
        }

        private void _s_ShowCreator(PlayerProfile p, List<InventoryItem> inv) => ShowCharacterCreator(p, inv, false);

        public void ShowFriends()
        {
            var list = OpenModal(Texts.T("ui.friends"));
            Loading(list);
            _s.Run(async () =>
            {
                var incoming = await _s.Api.GetIncomingRequestsAsync();
                var friends = await _s.Api.GetFriendsAsync();
                var blocked = await _s.Api.GetBlockedAsync();
                UiKit.Clear(list);

                var addRow = UiKit.Row(list, Texts.T("ui.friend_code"));
                var code = UiKit.Input(addRow, "ABC123", 12);
                code.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
                UiKit.RowButton(addRow, Texts.T("ui.add_friend"), UiKit.Good, () => _s.Run(async () =>
                {
                    var r = await _s.Api.SendFriendRequestByCodeAsync(code.text);
                    Toast(r.autoAccepted ? Texts.T("ui.now_friends") : Texts.T("ui.request_sent"));
                    ShowFriends();
                }));

                if (incoming.Count > 0) UiKit.Row(list, "<b>" + Texts.T("ui.incoming") + "</b>", 60);
                foreach (var req in incoming)
                {
                    var row = UiKit.Row(list, req.from.displayName + "  Lv." + req.from.level);
                    var id = req.requestId;
                    UiKit.RowButton(row, Texts.T("ui.accept"), UiKit.Good, () => _s.Run(async () => { await _s.Api.AcceptFriendRequestAsync(id); ShowFriends(); }), 140);
                    UiKit.RowButton(row, Texts.T("ui.decline"), UiKit.Muted, () => _s.Run(async () => { await _s.Api.DeclineFriendRequestAsync(id); ShowFriends(); }), 140);
                }

                UiKit.Row(list, "<b>" + Texts.T("ui.friends") + " (" + friends.Count + ")</b>", 60);
                if (friends.Count == 0) UiKit.Row(list, "ยังไม่มีเพื่อน แตะผู้เล่นในเมืองหรือใส่รหัสเพื่อนด้านบน");
                foreach (var f in friends.OrderByDescending(f => f.online))
                {
                    var where = f.online ? Texts.T("ui.online") + (f.zone != null ? " · " + Texts.T("zone." + f.zone.ToLowerInvariant()) : "") : Texts.T("ui.offline");
                    var row = UiKit.Row(list, f.displayName + "  Lv." + f.level + "\n<size=24>" + where + "</size>");
                    var fid = f.userId;
                    if (f.online && _s.Rt != null)
                        UiKit.RowButton(row, "ชวนมา", UiKit.Primary, () => _s.Run(async () =>
                        {
                            var ack = await _s.Rt.InviteFriendAsync(fid, "JOIN_ZONE");
                            Toast(ack.Ok ? "ส่งคำชวนแล้ว" : Texts.Error(ack.ErrorCode, ack.ErrorMessage));
                        }), 140);
                    UiKit.RowButton(row, "ไปบ้าน", UiKit.Good, () => { CloseModal(); _s.Run(() => _s.EnterHomeAsync(fid)); }, 130);
                    UiKit.RowButton(row, "ลบ", UiKit.Muted, () => _s.Run(async () => { await _s.Api.RemoveFriendAsync(fid); ShowFriends(); }), 100);
                }

                if (blocked.Count > 0) UiKit.Row(list, "<b>บล็อกไว้</b>", 60);
                foreach (var b in blocked)
                {
                    var bid = b.playerId;
                    UiKit.RowButton(UiKit.Row(list, b.displayName), "เลิกบล็อก", UiKit.Muted,
                        () => _s.Run(async () => { await _s.Api.UnblockAsync(bid); ShowFriends(); }));
                }
            });
        }

        public void ShowPlayerActions(RemotePlayer p)
        {
            var list = OpenModal(p.DisplayName + "  Lv." + p.Level);
            if (!p.IsFriend)
                UiKit.RowButton(UiKit.Row(list, "ขอเป็นเพื่อน"), Texts.T("ui.add_friend"), UiKit.Good, () => _s.Run(async () =>
                {
                    var r = await _s.Api.SendFriendRequestAsync(p.UserId);
                    Toast(r.autoAccepted ? Texts.T("ui.now_friends") : Texts.T("ui.request_sent"));
                    CloseModal();
                }));
            else UiKit.Row(list, "♥ เป็นเพื่อนกันแล้ว");
            // Phase 10: the server decides whether their home is open to me
            UiKit.RowButton(UiKit.Row(list, "ไปเยี่ยมบ้าน"), "ไปเลย", UiKit.Primary, () =>
            {
                CloseModal();
                _s.Run(() => _s.EnterHomeAsync(p.UserId));
            });
            UiKit.RowButton(UiKit.Row(list, "ไม่อยากเห็นผู้เล่นนี้อีก"), Texts.T("ui.block"), UiKit.Danger, () => _s.Run(async () =>
            {
                await _s.Api.BlockAsync(p.UserId);
                _s.HidePlayer(p.UserId);
                Toast(Texts.T("ui.blocked_done"));
                CloseModal();
            }));
            UiKit.RowButton(UiKit.Row(list, "แจ้งผู้ดูแลเรื่องพฤติกรรม"), Texts.T("ui.report"), UiKit.Danger, () => ShowReport(p));
        }

        private void ShowReport(RemotePlayer p)
        {
            var list = OpenModal(Texts.T("ui.report") + ": " + p.DisplayName);
            UiKit.Row(list, "<size=26>เลือกเหตุผล ระบบจะบล็อกผู้เล่นนี้ให้ด้วย</size>", 70);
            var reasons = new (string code, string text)[]
            {
                (ReportReason.InappropriateName, "ชื่อไม่เหมาะสม"),
                (ReportReason.Harassment, "ก่อกวน / กลั่นแกล้ง"),
                (ReportReason.AskingPersonalInfo, "ถามข้อมูลส่วนตัว"),
                (ReportReason.Spam, "สแปม"),
                (ReportReason.Cheating, "โกง"),
                (ReportReason.Other, "อื่น ๆ"),
            };
            foreach (var r in reasons)
            {
                var code = r.code;
                UiKit.RowButton(UiKit.Row(list, r.text), Texts.T("ui.report"), UiKit.Danger, () => _s.Run(async () =>
                {
                    await _s.Api.ReportAsync(p.UserId, code, "CITY", alsoBlock: true);
                    _s.HidePlayer(p.UserId);
                    Toast(Texts.T("ui.reported_done"));
                    CloseModal();
                }));
            }
        }

        private void ShowQuests()
        {
            var list = OpenModal(Texts.T("ui.quests"));
            Loading(list);
            _s.Run(async () =>
            {
                var quests = await _s.Api.GetQuestsAsync();
                UiKit.Clear(list);
                foreach (var q in quests.OrderBy(q => q.progress != null && q.progress.status == "CLAIMED"))
                {
                    var status = q.progress == null ? "" : q.progress.status == "CLAIMED" ? "✓ รับแล้ว"
                        : q.progress.value + "/" + q.objective.target;
                    var reward = Texts.T("ui.gold") + " " + q.rewards.gold + (q.rewards.gems > 0 ? "  " + Texts.T("ui.gems") + " " + q.rewards.gems : "") + "  XP " + q.rewards.xp;
                    var row = UiKit.Row(list, Texts.T(q.id) + "  <size=24>" + status + "</size>\n<size=24>" + reward + "</size>", 110);
                    var id = q.id;
                    if (q.CanClaim)
                        UiKit.RowButton(row, Texts.T("ui.claim"), UiKit.Good, () => _s.Run(async () =>
                        {
                            var r = await _s.Api.ClaimQuestAsync(id);
                            Toast("ได้รับ " + Texts.T("ui.gold") + " " + r.rewards.gold + (r.rewards.gems > 0 ? " " + Texts.T("ui.gems") + " " + r.rewards.gems : "") + (r.level.HasValue ? "  เลเวล " + r.level : ""));
                            ApplyLevel(r.level);
                            RefreshHud();
                            ShowQuests();
                        }));
                    else if (q.canStart && q.progress == null)
                        UiKit.RowButton(row, Texts.T("ui.start"), UiKit.Primary, () => _s.Run(async () => { await _s.Api.StartQuestAsync(id); ShowQuests(); }));
                }
            });
        }

        private void ShowBag()
        {
            var list = OpenModal(Texts.T("ui.bag"));
            Loading(list);
            _s.Run(async () =>
            {
                var w = await _s.Api.GetWalletAsync();
                var inv = await _s.Api.GetInventoryAsync();
                UiKit.Clear(list);
                UiKit.Row(list, Texts.T("ui.gold") + " " + w.Gold + "     " + Texts.T("ui.gems") + " " + w.Gems);
                foreach (var i in inv) UiKit.Row(list, Texts.T(i.itemId) + "  ×" + i.quantity, 70);
                if (w.recentTransactions.Count > 0) UiKit.Row(list, "<b>รายการล่าสุด</b>", 60);
                foreach (var t in w.recentTransactions.Take(10))
                    UiKit.Row(list, "<size=24>" + t.type + "  " + (t.amount > 0 ? "+" : "") + t.amount + " " + t.currency + "  → " + t.balanceAfter + "</size>", 60);
            });
        }

        private void ShowStore()
        {
            var list = OpenModal(Texts.T("ui.store"));
            Loading(list);
            _s.Run(async () =>
            {
                var catalog = await _s.Api.GetCatalogAsync();
                UiKit.Clear(list);
                UiKit.Row(list, "<size=24>" + Texts.T("ui.sim_note") + "</size>", 60);
                foreach (var c in catalog)
                {
                    var owned = c.ownedQuantity > 0 ? "  <size=22>(มี " + c.ownedQuantity + ")</size>" : "";
                    var row = UiKit.Row(list, Texts.T(c.itemId) + owned);
                    var id = c.itemId;
                    if (c.ownedQuantity > 0 && !c.stackable) continue;
                    if (c.priceGold.HasValue) UiKit.RowButton(row, Texts.T("ui.gold") + " " + c.priceGold, UiKit.Good, () => Buy(id, "GOLD"), 170);
                    if (c.priceGems.HasValue) UiKit.RowButton(row, Texts.T("ui.gems") + " " + c.priceGems, UiKit.Primary, () => Buy(id, "GEMS"), 170);
                }
            });
        }

        private void Buy(string itemId, string currency)
        {
            var key = IdempotencyKeys.New(); // one key per tap: a retried request can never charge twice
            _s.Run(async () =>
            {
                var r = await _s.Api.BuyItemAsync(itemId, 1, currency, key);
                Toast("ซื้อ " + Texts.T(itemId) + " แล้ว  (เหลือ " + r.balanceAfter + ")");
                ShowStore();
                RefreshHud(); // HUD balance otherwise stays stale until the modal is closed
            });
        }

        /// <summary>
        /// Living shop manager (Phase 5): storage → stall → NPC customers → cash box → wallet.
        /// Every number comes from the server's Business response; the client never computes money.
        /// </summary>
        public void ShowShop()
        {
            var list = OpenModal(Texts.T("ui.shop"));
            Loading(list);
            _s.Run(async () =>
            {
                var all = await _s.Api.GetBusinessesAsync();
                UiKit.Clear(list);
                if (all.Count == 0) { UiKit.Row(list, "ยังไม่มีร้าน"); return; }
                FillShop(list, all[0]);
            });
        }

        private void FillShop(RectTransform list, Business b)
        {
            var st = b.stall ?? new ShopStall();
            SetCashBox(st);
            var statusText = st.status == "OPEN" ? "<color=#2E8B3E>เปิดขายอยู่</color>" : st.status == "PAUSED" ? "<color=#C77C00>พักขาย</color>" : "<color=#888888>ปิดร้าน</color>";
            var where = st.slot.HasValue ? " · แผงที่ " + (st.slot.Value + 1) + " ในตลาด" : st.status == "CLOSED" ? "" : " · ตลาดเต็ม ร้านยังขายได้แต่ไม่มีแผงบนแผนที่";
            var head = UiKit.Row(list, "<b>" + b.name + "</b>  Lv." + b.level + "/" + b.maxLevel + "\n<size=24>" + statusText + where + "</size>", 110);
            UiKit.RowButton(head, "ตั้งชื่อร้าน", UiKit.Muted, () => ShowRenameShop(b), 190);

            // cash box: money from NPC customers waiting to be collected
            var cash = UiKit.Row(list, "เงินในลิ้นชัก <b>" + st.cashBox + " ทอง</b>\n<size=24>ลูกค้าทั้งหมด " + st.totalCustomers + " คน · ขายไปแล้ว " + b.totalUnitsSold + " ชิ้น</size>", 110);
            if (st.cashBox > 0)
            {
                var key = IdempotencyKeys.New(); // one key per tap: a retried request can never pay twice
                UiKit.RowButton(cash, "เก็บเงิน", UiKit.Good, () => _s.Run(async () =>
                {
                    var r = await _s.Api.CollectShopAsync(b.id, key);
                    Toast("เก็บเงินเข้ากระเป๋า " + r.collected + " ทอง");
                    RefreshHud();
                    UiKit.Clear(list);
                    FillShop(list, r.business);
                }), 190);
            }
            UiKit.RowButton(cash, "รายงาน", UiKit.Primary, () => _s.Run(async () => ShowShopReport(await _s.Api.GetShopReportAsync(b.id), false)), 160);

            UiKit.RowButton(UiKit.Row(list, "<size=26>" + Texts.T("ui.crafting") + ": เอาวัตถุดิบจากการออกสำรวจมาทำสินค้าพิเศษที่ซื้อไม่ได้ ขายได้ราคาดี</size>", 80),
                Texts.T("ui.crafting"), UiKit.Accent, () => ShowCrafting(b.id), 190);

            UiKit.Row(list, "<size=24>รายรับรวม " + b.finance.totalRevenue + " · รายจ่ายรวม (ซื้อของ+ค่าแรงงานฝีมือ+อัปเกรด) " + b.finance.totalExpense + " · กำไรสุทธิ " + b.finance.profit + " ทอง</size>", 70);

            // open / pause / close
            var ctrl = UiKit.Row(list, "<size=26>แผงมี " + st.shelfSlots + " ช่อง (ใช้ " + st.slotsUsed + ") · ช่องละไม่เกิน " + st.shelfCapacity + " ชิ้น\nลูกค้าเฉลี่ย " + st.customersPerMinute.ToString("0.#") + " คน/นาที · ขายเองตอนออฟไลน์ได้ถึง 8 ชม.</size>", 110);
            if (st.status != "OPEN") UiKit.RowButton(ctrl, "เปิดร้าน", UiKit.Good, () => SetStatus(list, b.id, "OPEN"), 160);
            if (st.status == "OPEN") UiKit.RowButton(ctrl, "พักขาย", UiKit.Muted, () => SetStatus(list, b.id, "PAUSED"), 150);
            if (st.status != "CLOSED") UiKit.RowButton(ctrl, "ปิดร้าน", UiKit.Danger, () => SetStatus(list, b.id, "CLOSED"), 150);
            if (st.slot.HasValue && _s.CurrentZone != null && _s.CurrentZone.id != "MARKET")
                UiKit.RowButton(ctrl, "ไปที่แผง", UiKit.Primary, () => { CloseModal(); _s.Run(() => _s.JoinZoneAsync("MARKET")); }, 160);

            // products: storage, stall, price
            foreach (var c in b.catalog)
            {
                var name = MarketNpcPlanner.ProductName(c.productCode);
                if (!c.unlocked)
                {
                    UiKit.Row(list, "<color=#888888>" + name + " (ล็อก) · ปลดล็อกเมื่อร้าน Lv." + c.unlockLevel + "</color>", 70);
                    continue;
                }
                var p = b.products.FirstOrDefault(x => x.productCode == c.productCode);
                int storage = p != null ? p.quantity : 0, shelf = p != null ? p.shelfQuantity : 0, price = p != null ? p.sellPrice : c.basePrice;
                var cost = p != null && p.avgUnitCost > 0 ? p.avgUnitCost : c.unitCost;
                var margin = price - cost;
                var code = c.productCode;
                var hint = FestivalPresenter.ProductHint(_s.Festival != null ? _s.Festival.current : null, code);
                if (hint.Length > 0) name += " <color=#D35400><size=22>" + hint + "</size></color>";
                var weatherHint = CityLifePresenter.ProductHint(_s.CityNow, code);
                if (weatherHint.Length > 0) name += " <color=#2471A3><size=22>" + weatherHint + "</size></color>";
                var row = UiKit.Row(list,
                    "<b>" + name + "</b>  ขาย " + price + " ทอง <size=22>(ราคาตลาด " + c.basePrice + " · ต้นทุน " + cost + " · " + (margin > 0 ? "กำไร " + margin : "<color=#C0392B>ขาดทุน " + (-margin) + "</color>") + "/ชิ้น)</size>\n" +
                    "<size=24>คลัง " + storage + "/" + b.stockCapacity + " · บนแผง " + shelf + "/" + st.shelfCapacity + (p != null && p.onStall ? "" : " · ยังไม่ได้วางขาย") + "</size>", 120);
                UiKit.RowButton(row, "-1", UiKit.Muted, () => SetPrice(b.id, code, price - 1), 70);
                UiKit.RowButton(row, "+1", UiKit.Muted, () => SetPrice(b.id, code, price + 1), 70);
                var key = IdempotencyKeys.New();
                if (c.craftOnly)
                    UiKit.RowButton(row, Texts.T("ui.crafting"), UiKit.Accent, () => ShowCrafting(b.id), 160); // Phase 9: made from expedition materials
                else
                    UiKit.RowButton(row, "ซื้อเข้า 10", UiKit.Primary, () => _s.Run(async () =>
                    {
                        await _s.Api.RestockAsync(b.id, code, 10, key);
                        RefreshHud(); // restock is paid from the wallet
                        RefreshShop(list, b.id);
                    }), 160);
                // Both shelf buttons are always drawn (greyed out when unusable) so they never shift under the
                // player's finger: on device a second tap on "+5" landed on "เก็บลง" and emptied the stall (Phase 5 QA).
                var room = Math.Min(st.shelfCapacity - shelf, storage);
                var add = UiKit.RowButton(row, "ขึ้นแผง +" + Math.Max(0, Math.Min(5, room)), UiKit.Good, () => SetShelf(list, b.id, code, shelf + Math.Min(5, room)), 160);
                add.interactable = room > 0;
                var remove = UiKit.RowButton(row, "เก็บลง", UiKit.Muted, () => SetShelf(list, b.id, code, 0), 120);
                remove.interactable = shelf > 0;

                // hands-on way to fill the storage: a short minigame whose stars decide how many units it makes
                var game = MinigameRules.ForProduct(code);
                if (game != null)
                    UiKit.RowButton(UiKit.Row(list, "<size=24>" + Texts.T("ui.make") + ": " + MinigameRules.Title(game, code) + " · เล่นให้ได้ดาว ได้" + MarketNpcPlanner.ProductName(code) + "เข้าคลังฟรี</size>", 70),
                        Texts.T("ui.make"), UiKit.Accent, () => PlayMinigame(b, code), 150);
            }

            if (b.upgradeCost.HasValue)
                UiKit.RowButton(UiKit.Row(list, "อัปเกรดร้านเป็น Lv." + (b.level + 1) + " (" + Texts.T("ui.gold") + " " + b.upgradeCost + ")\n<size=24>ลูกค้ามากขึ้น แผงใหญ่ขึ้น ปลดล็อกสินค้าใหม่</size>", 110), "อัปเกรด", UiKit.Primary, () => _s.Run(async () =>
                {
                    await _s.Api.UpgradeBusinessAsync(b.id, b.level);
                    RefreshHud();
                    RefreshShop(list, b.id);
                }));
            UiKit.Row(list, "<size=22>" + Texts.T("ui.sim_note") + "</size>", 60);
        }

        private void RefreshShop(RectTransform list, string businessId)
        {
            _s.Run(async () =>
            {
                var b = await _s.Api.GetBusinessAsync(businessId);
                if (list == null) return; // modal closed meanwhile
                UiKit.Clear(list);
                FillShop(list, b);
            });
        }

        private void SetShelf(RectTransform list, string businessId, string code, int quantity)
        {
            _s.Run(async () =>
            {
                var b = await _s.Api.SetShelfAsync(businessId, code, quantity);
                if (list == null) return;
                UiKit.Clear(list);
                FillShop(list, b);
            });
        }

        private void SetStatus(RectTransform list, string businessId, string status)
        {
            _s.Run(async () =>
            {
                var b = await _s.Api.SetShopStatusAsync(businessId, status);
                Toast(status == "OPEN" ? "เปิดร้านแล้ว ลูกค้าจะเริ่มเดินมาที่แผงในตลาด" : status == "PAUSED" ? "พักขายแล้ว ลูกค้าจะไม่แวะมาจนกว่าจะเปิดใหม่" : "ปิดร้านแล้ว");
                _s.RefreshMarket();
                if (list == null) return;
                UiKit.Clear(list);
                FillShop(list, b);
            });
        }

        private void ShowRenameShop(Business b)
        {
            var panel = OpenModal("ตั้งชื่อร้าน", false);
            UiKit.Label(panel, "<size=26>ชื่อร้านจะแสดงให้ผู้เล่นวัยเดียวกันเห็นในตลาด ห้ามใส่เบอร์โทร ลิงก์ หรือคำไม่สุภาพ</size>", 30, TextAnchor.MiddleLeft)
                .rectTransform.Place(new Vector2(0.04f, 0.7f), new Vector2(0.96f, 0.9f));
            var input = UiKit.Input(panel, "เช่น ร้านน้ำมะนาวสดใส", 20);
            input.text = b.name;
            input.GetComponent<RectTransform>().Place(new Vector2(0.04f, 0.5f), new Vector2(0.96f, 0.64f));
            UiKit.Button(panel, Texts.T("ui.save"), UiKit.Good, () => _s.Run(async () =>
            {
                await _s.Api.RenameShopAsync(b.id, input.text.Trim());
                Toast("ตั้งชื่อร้านแล้ว");
                _s.RefreshMarket();
                ShowShop();
            }), 36).GetComponent<RectTransform>().Place(new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.34f));
        }

        /// <summary>"While you were away" report. Shown after sign-in when there is news, or from the shop.</summary>
        // ---------- exploring and crafting (Phase 9) ----------

        /// <summary>
        /// Expeditions: pick a place and a plan, come back when the trip is over (it keeps going while the app is
        /// closed), read what happened. The server rolls everything; this screen only tells the story.
        /// </summary>
        public void ShowExplore()
        {
            var list = OpenModal(Texts.T("ui.explore"));
            Loading(list);
            _s.Run(async () =>
            {
                var o = await _s.Api.GetExpeditionsAsync();
                UiKit.Clear(list);
                FillExplore(list, o);
            });
        }

        private void FillExplore(RectTransform list, ExpeditionOverview o)
        {
            _tripStatus = null;
            o.skills.TryGetValue("EXPLORER", out var ex);
            o.skills.TryGetValue("CRAFTER", out var cr);
            UiKit.Row(list, SkillLine(ex) + "\n" + SkillLine(cr), 100);
            UiKit.Row(list, "<size=24>วัตถุดิบของฉัน: " + AdventurePresenter.Bag(o.materials) + "</size>", 80);

            if (o.active != null)
            {
                var a = o.active;
                // the line and the button follow the tracker every quarter second (TickTrip): no "รีเฟรช" needed
                var row = UiKit.Row(list, "<b>กำลังเดินทาง:</b> " + AdventurePresenter.SiteName(a.site) + " · " + AdventurePresenter.PlanName(a.plan), 110);
                var label = row.GetComponentInChildren<Text>(); // the row's own label (the button is added after)
                _exploreLabel = label.text;
                _tripStatus = label;
                _tripButton = UiKit.RowButton(row, "ดูผล", UiKit.Good, OnTripButton, 170);
                _tripButtonText = _tripButton.GetComponentInChildren<Text>();
                _tripButtonBg = _tripButton.GetComponent<Image>();
            }
            // the freshest server state there is (after the row exists, so an arrival shows here, not as a toast)
            _s.Trips.Apply(o.active, GameSession.Clock);
            _tripTickAt = 0;
            TickTrip(GameSession.Clock);

            foreach (var site in o.sites)
            {
                var name = AdventurePresenter.SiteName(site.site);
                if (!site.unlocked)
                {
                    UiKit.Row(list, "<color=#888888>" + name + " (ล็อก) · ต้องเป็นนักสำรวจเลเวล " + site.explorerLevel + "</color>", 70);
                    continue;
                }
                var mins = site.seconds >= 60 ? (site.seconds / 60) + " นาที" : site.seconds + " วินาที";
                var row = UiKit.Row(list, "<b>" + name + "</b>  <size=24>" + mins + " · เจอเหตุการณ์ " + site.eventCount + " อย่าง</size>\n<size=24>" + AdventurePresenter.SiteBlurb(site.site) +
                    (site.firstVisit ? "  <color=#B9770E>★ ไปครั้งแรกได้ของครบชุด</color>" : "") + "</size>", 110);
                var info = site;
                var go = UiKit.RowButton(row, "ออกเดินทาง", UiKit.Primary, () => ShowPlanPicker(info), 190);
                go.interactable = o.active == null;
            }
            if (o.lastClaimed != null)
            {
                UiKit.Row(list, "<b>ทริปล่าสุด</b>", 60);
                foreach (var line in AdventurePresenter.Story(o.lastClaimed)) UiKit.Row(list, "<size=24>" + line + "</size>", 70);
            }
        }

        /// <summary>
        /// Phase 9.1: called every frame by GameSession; four times a second it redraws the trip countdown on
        /// the HUD button and, when the explore screen is open, its status line and button.
        /// </summary>
        public void TickTrip(double now)
        {
            if (now < _tripTickAt) return;
            _tripTickAt = now + 0.25;
            var t = _s.Trips;
            if (_exploreText != null)
            {
                var badge = AdventurePresenter.HudBadge(t, now);
                _exploreText.text = Texts.T("ui.explore") + (badge.Length > 0 ? " <size=22>" + badge + "</size>" : "");
            }
            if (_tripStatus == null) return; // the explore screen is closed (destroyed Unity objects compare equal to null)
            _tripStatus.text = _exploreLabel + "\n<size=24>" + AdventurePresenter.TripStatus(t, now) + "</size>";
            string text;
            Color color;
            bool on;
            if (t.Claiming) { text = "กำลังเปิด..."; color = UiKit.Muted; on = false; }
            else if (t.Phase == TripPhase.Ready) { text = "ดูผล"; color = UiKit.Good; on = true; }
            else if (t.Offline && t.SecondsLeft(now) == 0) { text = "ลองใหม่"; color = UiKit.Accent; on = true; }
            else { text = "เดินทางอยู่"; color = UiKit.Muted; on = false; }
            _tripButtonText.text = text;
            _tripButtonBg.color = color;
            _tripButton.interactable = on;
        }

        private void OnTripButton()
        {
            var t = _s.Trips;
            if (t.Phase != TripPhase.Ready)
            {
                t.Resync(GameSession.Clock); // "ลองใหม่"
                _tripTickAt = 0;
                return;
            }
            if (!t.TryBeginClaim()) return; // already claiming: a second tap does nothing
            var id = t.Trip.id;
            _tripTickAt = 0;
            _s.Run(async () =>
            {
                ExpeditionTrip trip;
                try
                {
                    trip = await _s.Api.ClaimExpeditionAsync(id);
                }
                catch (ApiException e)
                {
                    t.ClaimFailed(e.Code, e.Status, GameSession.Clock); // network: still "ดูผล", retrying is safe
                    _tripTickAt = 0;
                    throw; // GameSession.Run shows it as a Thai toast
                }
                t.ClaimSucceeded();
                if (trip.result != null && trip.result.playerLevel.HasValue) ApplyLevel(trip.result.playerLevel);
                ShowExpeditionStory(trip);
            });
        }

        /// <summary>The server confirmed the trip is back (also right after opening the app when it came back while closed).</summary>
        private void OnTripArrived(ExpeditionTrip t)
        {
            _tripTickAt = 0;
            if (_tripStatus != null || !_hud.gameObject.activeSelf) return; // the explore screen already shows "ดูผล"
            Audio.GameAudio.Sfx("sfx_coin", 0.6f);
            Toast("กลับจาก" + AdventurePresenter.SiteName(t.site) + "แล้ว มาดูว่าได้อะไรมาบ้าง", "ดูผล", ShowExplore);
        }

        private static string SkillLine(SkillView v)
        {
            if (v == null) return "";
            var next = v.nextLevelXp.HasValue ? " (" + v.xp + "/" + v.nextLevelXp + " XP)" : " (สูงสุด)";
            return AdventurePresenter.SkillName(v.skill) + " เลเวล " + v.level + "<size=24>" + next + "</size>";
        }

        private void ShowPlanPicker(ExpeditionSiteInfo info)
        {
            var site = info.site;
            var list = OpenModal(AdventurePresenter.SiteName(site));
            UiKit.Row(list, "<size=26>เลือกวิธีเดินทาง ระหว่างทางจะเจอเหตุการณ์ที่ผ่านได้หรือพลาดได้ นักสำรวจเก่งขึ้นก็ผ่านง่ายขึ้น</size>", 90);
            var hint = AdventurePresenter.FirstVisitHint(info);
            if (hint != null) UiKit.Row(list, "<size=24>★ " + hint + "</size>", 70);
            var go = new List<Button>();
            foreach (var plan in new[] { "CAREFUL", "STEADY", "BOLD" })
            {
                var p = plan;
                var row = UiKit.Row(list, "<b>" + AdventurePresenter.PlanName(p) + "</b>\n<size=24>" + AdventurePresenter.PlanBlurb(p) + "</size>", 110);
                go.Add(UiKit.RowButton(row, "ไปเลย", p == "BOLD" ? UiKit.Accent : UiKit.Good, () =>
                {
                    foreach (var b in go) b.interactable = false; // one trip at a time: a double tap must not send two starts
                    _s.Run(async () =>
                    {
                        try
                        {
                            var t = await _s.Api.StartExpeditionAsync(site, p);
                            _s.Trips.Apply(t, GameSession.Clock);
                            Toast("ออกเดินทางไป" + AdventurePresenter.SiteName(t.site) + "แล้ว " + AdventurePresenter.TimeLeft(t.secondsLeft));
                            ShowExplore();
                        }
                        finally
                        {
                            foreach (var b in go) if (b != null) b.interactable = true;
                        }
                    });
                }, 150));
            }
            UiKit.RowButton(UiKit.Row(list, ""), "ย้อนกลับ", UiKit.Muted, ShowExplore, 160);
        }

        private void ShowExpeditionStory(ExpeditionTrip t)
        {
            var list = OpenModal("กลับจากการสำรวจ");
            foreach (var line in AdventurePresenter.Story(t)) UiKit.Row(list, line, 90);
            var row = UiKit.Row(list, "<size=24>เอาวัตถุดิบไปทำสินค้าในงานฝีมือ แล้ววางขายที่ร้านได้เลย</size>", 80);
            UiKit.RowButton(row, Texts.T("ui.crafting"), UiKit.Accent, () => ShowCrafting(null), 170);
            UiKit.RowButton(row, "สำรวจต่อ", UiKit.Primary, ShowExplore, 160);
            Audio.GameAudio.Sfx("sfx_coin", 0.7f);
        }

        /// <summary>Crafting: materials + a small Gold fee become craft-only goods in the shop's storage.</summary>
        private void ShowCrafting(string businessId)
        {
            var list = OpenModal(Texts.T("ui.crafting"));
            Loading(list);
            _s.Run(async () =>
            {
                if (businessId == null)
                {
                    var shops = await _s.Api.GetBusinessesAsync();
                    if (shops.Count == 0) { UiKit.Clear(list); UiKit.Row(list, "ยังไม่มีร้าน"); return; }
                    businessId = shops[0].id;
                }
                var o = await _s.Api.GetCraftingAsync();
                UiKit.Clear(list);
                UiKit.Row(list, SkillLine(o.crafter) + "\n<size=24>วัตถุดิบ: " + AdventurePresenter.Bag(o.materials) + "</size>", 100);
                foreach (var r in o.recipes)
                {
                    var name = AdventurePresenter.RecipeName(r.recipe);
                    if (!r.unlocked)
                    {
                        UiKit.Row(list, "<color=#888888>" + name + " (ล็อก) · ต้องเป็นช่างฝีมือเลเวล " + r.crafterLevel + "</color>", 70);
                        continue;
                    }
                    var row = UiKit.Row(list, "<b>" + name + "</b> ได้ " + r.outputUnits + " " + MarketNpcPlanner.ProductUnit(r.productCode) + " · ค่าแรง " + r.feeGold + " ทอง · ราคาตลาด " + r.basePrice +
                        "\n<size=22>" + AdventurePresenter.Inputs(r, o.materials) + "</size>", 120);
                    var recipe = r.recipe;
                    var id = businessId;
                    var one = IdempotencyKeys.New();
                    var all = IdempotencyKeys.New();
                    var times = Math.Min(10, r.canMake);
                    Button b1 = null, bAll = null;
                    b1 = UiKit.RowButton(row, "ทำ 1", UiKit.Good, () => Craft(id, recipe, 1, one, b1, bAll), 120);
                    b1.interactable = r.canMake > 0;
                    bAll = UiKit.RowButton(row, "ทำ " + Math.Max(1, times), UiKit.Accent, () => Craft(id, recipe, Math.Max(1, times), all, b1, bAll), 120);
                    bAll.interactable = times > 1;
                }
                UiKit.RowButton(UiKit.Row(list, "<size=24>วัตถุดิบหามาจากการออกสำรวจ</size>", 70), Texts.T("ui.explore"), UiKit.Primary, ShowExplore, 190);
            });
        }

        /// <summary>
        /// The key is fixed per button until the screen is redrawn, so a retry after a timeout is the same craft
        /// (the server does it once). Both buttons are disabled while it is on its way (Phase 9.1).
        /// </summary>
        private void Craft(string businessId, string recipe, int times, string key, params Button[] row)
        {
            foreach (var b in row) if (b != null) b.interactable = false;
            _s.Run(async () =>
            {
                CraftResult r;
                try
                {
                    r = await _s.Api.CraftAsync(businessId, recipe, times, key);
                }
                catch (Exception)
                {
                    foreach (var b in row) if (b != null) b.interactable = true; // same key: pressing again is safe
                    throw;
                }
                if (r.replayed)
                {
                    Toast("ทำ" + AdventurePresenter.RecipeName(recipe) + "รอบนี้เสร็จไปแล้ว ไม่ได้หักวัตถุดิบหรือค่าแรงซ้ำ");
                    ShowCrafting(businessId);
                    return;
                }
                ApplyLevel(r.level);
                Toast("ทำ" + AdventurePresenter.RecipeName(recipe) + "ได้ " + r.unitsMade + " " + MarketNpcPlanner.ProductUnit(r.productCode) + " เข้าคลังร้านแล้ว" +
                    (r.crafterLeveledUp ? "  ช่างฝีมือเลเวล " + r.crafter.level + "!" : ""), "ดูร้าน", ShowShop);
                RefreshHud(); // the workshop fee is paid from the wallet
                ShowCrafting(businessId);
            });
        }

        private void PlayMinigame(Business b, string code)
        {
            UiKit.Clear(_modalLayer);
            MinigameScreen.Open(_s, _modalLayer, b, code, _ => { RefreshHud(); ShowShop(); });
        }

        public void ShowShopReport(ShopReport r, bool welcomeBack)
        {
            var list = OpenModal(welcomeBack ? "ระหว่างที่คุณไม่อยู่..." : "รายงานร้าน");
            var profit = r.profit >= 0 ? "กำไร <b>" + r.profit + "</b> ทอง" : "<color=#C0392B>ขาดทุน " + (-r.profit) + " ทอง</color>";
            UiKit.Row(list, "<b>" + r.shopName + "</b>\n<size=24>ตั้งแต่ " + r.since.ToLocalTime().ToString("d MMM HH:mm") + "</size>", 100);
            if (!r.hasNews)
            {
                UiKit.Row(list, "ยังไม่มีลูกค้าใหม่ตั้งแต่ครั้งที่แล้ว");
            }
            else
            {
                UiKit.Row(list, "ลูกค้าแวะมา <b>" + r.customers + "</b> คน · ซื้อ <b>" + r.buyers + "</b> คน\nขายได้ " + r.unitsSold + " ชิ้น · รายรับ " + r.revenue + " · ต้นทุนของ " + r.costOfGoods + " · " + profit, 110);
                UiKit.Row(list, "<size=26>ลูกค้าที่พลาดไป: แพงเกินงบ " + r.lostTooExpensive + " คน · ของหมด " + r.lostSoldOut + " คน · หาของที่ร้านไม่มี " + r.lostNotSelling + " คน</size>", 80);
                if (r.bestSeller != null) UiKit.Row(list, "ขายดีที่สุด: <b>" + MarketNpcPlanner.ProductName(r.bestSeller) + "</b>", 70);
                foreach (var p in r.products.Where(x => x.wanted > 0 || x.onStall))
                    UiKit.Row(list, "<size=24>" + MarketNpcPlanner.ProductName(p.productCode) + (p.price.HasValue ? " (" + p.price + " ทอง)" : " (ไม่ได้วางขาย)") + ": ขาย " + p.unitsSold + " · กำไร " + p.profit +
                        " · คนอยากได้ " + p.wanted + " · แพงไป " + p.lostTooExpensive + " · หมด " + p.lostSoldOut + (p.onStall ? " · เหลือบนแผง " + p.shelfLeft : "") + "</size>", 80);
            }
            foreach (var t in r.tips) UiKit.Row(list, "<color=#2D6CDF>คำแนะนำ:</color> " + t.textTh, 90);
            var actions = UiKit.Row(list, r.cashBox > 0 ? "เงินรออยู่ในลิ้นชัก " + r.cashBox + " ทอง" : "", 100);
            if (r.cashBox > 0)
            {
                var key = IdempotencyKeys.New();
                UiKit.RowButton(actions, "เก็บเงิน", UiKit.Good, () => _s.Run(async () =>
                {
                    var c = await _s.Api.CollectShopAsync(r.businessId, key);
                    Toast("เก็บเงินเข้ากระเป๋า " + c.collected + " ทอง");
                    RefreshHud(); // the welcome-back report collected into the wallet; HUD showed the old total (Phase 5 QA)
                    ShowShop();
                }), 180);
            }
            UiKit.RowButton(actions, "จัดร้าน", UiKit.Primary, ShowShop, 160);
            // seen: the next report starts from here (sales after `until` stay for next time)
            if (r.untilIso != null) _s.Run(() => _s.Api.AckShopReportAsync(r.businessId, r.untilIso));
        }

        /// <summary>Tapping a stall in the market.</summary>
        public void ShowStallInfo(MarketStall s)
        {
            var list = OpenModal(s.name);
            UiKit.Row(list, "โดย " + s.ownerName + " · ร้าน Lv." + s.level + " · " + (s.status == "OPEN" ? "เปิดขาย" : "พักขาย"), 80);
            foreach (var p in s.products)
                UiKit.Row(list, MarketNpcPlanner.ProductName(p.productCode) + "  " + p.price + " ทอง  " + (p.shelfQuantity > 0 ? "เหลือ " + p.shelfQuantity : "หมด"), 70);
            if (s.isMine)
            {
                UiKit.RowButton(UiKit.Row(list, "นี่คือร้านของคุณ"), "จัดร้าน", UiKit.Primary, ShowShop);
                return;
            }
            UiKit.RowButton(UiKit.Row(list, "ชมร้านนี้"), "ร้านสวยจัง", UiKit.Good, () => { CloseModal(); _s.Run(() => _s.SendPresetAsync("NICE_SHOP")); });
            UiKit.RowButton(UiKit.Row(list, "ชื่อร้านไม่เหมาะสม"), Texts.T("ui.report"), UiKit.Danger, () => _s.Run(async () =>
            {
                await _s.Api.ReportAsync(s.ownerId, "INAPPROPRIATE_NAME", "MARKET_STALL");
                Toast(Texts.T("ui.reported_done"));
                CloseModal();
            }));
        }

        private void SetPrice(string businessId, string code, int price)
        {
            _s.Run(async () =>
            {
                await _s.Api.SetPriceAsync(businessId, code, price);
                _s.RefreshMarket();
                ShowShop();
            });
        }

        private void ShowChat()
        {
            var list = OpenModal(Texts.T("ui.chat"));
            UiKit.Row(list, "<size=24>เกมนี้ใช้ข้อความสำเร็จรูปเท่านั้น เพื่อความปลอดภัยของทุกคน</size>", 60);
            if (_s.Layout == null) return;
            foreach (var m in _s.Layout.presetMessages)
            {
                var id = m.id;
                UiKit.RowButton(UiKit.Row(list, m.textTh, 80), "ส่ง", UiKit.Good, () =>
                {
                    CloseModal();
                    _s.Run(() => _s.SendPresetAsync(id));
                }, 140);
            }
        }

        // ---------- toasts & banner ----------

        public void Toast(string text, string actionLabel = null, Action action = null)
        {
            Audio.GameAudio.Sfx("sfx_pop", 0.6f);
            var box = UiKit.Box("Toast", _toastLayer, new Color(0.1f, 0.1f, 0.15f, 0.88f)).Round(true).rectTransform;
            int n = _toastLayer.childCount - 1;
            box.Place(new Vector2(0.3f, 0), new Vector2(0.7f, 0), new Vector2(0, 30 + n * 120), new Vector2(0, 140 + n * 120));
            var label = UiKit.Label(box, text, 30, TextAnchor.MiddleCenter, Color.white);
            label.rectTransform.Place(Vector2.zero, new Vector2(actionLabel != null ? 0.72f : 1, 1), new Vector2(16, 0), new Vector2(-16, 0));
            if (actionLabel != null)
                UiKit.Button(box, actionLabel, UiKit.Good, () => { UnityEngine.Object.Destroy(box.gameObject); action(); }, 28)
                    .GetComponent<RectTransform>().Place(new Vector2(0.74f, 0.15f), new Vector2(0.98f, 0.85f));
            UnityEngine.Object.Destroy(box.gameObject, actionLabel != null ? 8f : 3.5f);
        }

        public void ShowBanner(string text, Action retry)
        {
            HideBanner();
            _banner = UiKit.Box("Banner", _root, new Color(0.9f, 0.45f, 0.3f, 0.95f)).rectTransform
                .Place(new Vector2(0.3f, 1), new Vector2(0.7f, 1), new Vector2(0, -120), new Vector2(0, -10));
            UiKit.Label(_banner, text, 30, TextAnchor.MiddleCenter, Color.white).rectTransform
                .Place(Vector2.zero, new Vector2(retry != null ? 0.7f : 1, 1));
            if (retry != null)
                UiKit.Button(_banner, Texts.T("ui.reconnect"), UiKit.Good, retry, 26).GetComponent<RectTransform>()
                    .Place(new Vector2(0.72f, 0.15f), new Vector2(0.98f, 0.85f));
        }

        public void HideBanner()
        {
            if (_banner != null) UnityEngine.Object.Destroy(_banner.gameObject);
            _banner = null;
        }
    }
}
