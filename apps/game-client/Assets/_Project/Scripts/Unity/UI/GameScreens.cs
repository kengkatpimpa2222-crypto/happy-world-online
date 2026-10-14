using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using Avatar = HappyWorld.Core.Api.Avatar; // UnityEngine also has an Avatar type
using HappyWorld.Core.Localization;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using UnityEngine;
using UnityEngine.UI;

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
        private float _lastSaleToast = -999f;

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
        }

        // ---------- full screens ----------

        private RectTransform NewScreen()
        {
            UiKit.Clear(_screen);
            _hud.gameObject.SetActive(false);
            UiKit.Clear(_modalLayer);
            var bg = UiKit.Box("Bg", _screen, new Color(0.62f, 0.85f, 1f)).rectTransform.Place(Vector2.zero, Vector2.one);
            var card = UiKit.Box("Card", bg, UiKit.Panel).rectTransform.Place(new Vector2(0.25f, 0.06f), new Vector2(0.75f, 0.94f));
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

            var top = UiKit.Box("TopBar", _hud, new Color(1, 1, 1, 0.85f)).rectTransform.Place(new Vector2(0, 1), new Vector2(0.36f, 1), new Vector2(16, -200), new Vector2(0, -16));
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

            var buttons = new (string key, Action open)[]
            {
                ("ui.profile", ShowProfile),
                ("ui.friends", ShowFriends),
                ("ui.quests", ShowQuests),
                ("ui.bag", ShowBag),
                ("ui.store", ShowStore),
                ("ui.shop", ShowShop),
                ("ui.go_market", () => _s.Run(() => _s.JoinZoneAsync("MARKET"))),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                UiKit.Button(_hud, Texts.T(b.key), UiKit.Primary, () => b.open(), 30).GetComponent<RectTransform>()
                    .Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-230, -110 - i * 104), new Vector2(-16, -16 - i * 104));
            }
            UiKit.Button(_hud, Texts.T("ui.chat"), UiKit.Good, ShowChat, 36).GetComponent<RectTransform>()
                .Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-300, 40), new Vector2(-40, 170));
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
        public void RefreshHud()
        {
            var p = _s.Profile;
            if (p != null) _nameText.text = p.displayName + "  Lv." + p.level;
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

        // ---------- modal ----------

        /// <summary>Opens a centred panel. Returns a scroll list (list=true) or the bare panel.</summary>
        private RectTransform OpenModal(string title, bool list = true)
        {
            UiKit.Clear(_modalLayer);
            var dim = UiKit.Box("Dim", _modalLayer, new Color(0, 0, 0, 0.45f)).rectTransform.Place(Vector2.zero, Vector2.one);
            var panel = UiKit.Box("Panel", dim, UiKit.Panel).rectTransform.Place(new Vector2(0.18f, 0.06f), new Vector2(0.82f, 0.94f));
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
                UiKit.RowButton(UiKit.Row(list, "ออกจากระบบ\n<size=22>บัญชีผู้เล่นทั่วไปจะเข้าคืนไม่ได้ถ้ายังไม่ผูกอีเมล</size>", 110), "ออก", UiKit.Danger,
                    () => _s.Run(_s.LogoutAsync));
            });
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

            UiKit.Row(list, "<size=24>รายรับรวม " + b.finance.totalRevenue + " · รายจ่ายรวม (ซื้อของ+อัปเกรด) " + b.finance.totalExpense + " · กำไรสุทธิ " + b.finance.profit + " ทอง</size>", 70);

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
                var row = UiKit.Row(list,
                    "<b>" + name + "</b>  ขาย " + price + " ทอง <size=22>(ราคาตลาด " + c.basePrice + " · ต้นทุน " + cost + " · " + (margin > 0 ? "กำไร " + margin : "<color=#C0392B>ขาดทุน " + (-margin) + "</color>") + "/ชิ้น)</size>\n" +
                    "<size=24>คลัง " + storage + "/" + b.stockCapacity + " · บนแผง " + shelf + "/" + st.shelfCapacity + (p != null && p.onStall ? "" : " · ยังไม่ได้วางขาย") + "</size>", 120);
                UiKit.RowButton(row, "-1", UiKit.Muted, () => SetPrice(b.id, code, price - 1), 70);
                UiKit.RowButton(row, "+1", UiKit.Muted, () => SetPrice(b.id, code, price + 1), 70);
                var key = IdempotencyKeys.New();
                UiKit.RowButton(row, "ซื้อเข้า 10", UiKit.Primary, () => _s.Run(async () =>
                {
                    await _s.Api.RestockAsync(b.id, code, 10, key);
                    RefreshShop(list, b.id);
                }), 160);
                var room = Math.Min(st.shelfCapacity - shelf, storage);
                if (room > 0) UiKit.RowButton(row, "ขึ้นแผง +" + Math.Min(5, room), UiKit.Good, () => SetShelf(list, b.id, code, shelf + Math.Min(5, room)), 160);
                if (shelf > 0) UiKit.RowButton(row, "เก็บลง", UiKit.Muted, () => SetShelf(list, b.id, code, 0), 120);
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
            var box = UiKit.Box("Toast", _toastLayer, new Color(0.1f, 0.1f, 0.15f, 0.88f)).rectTransform;
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
