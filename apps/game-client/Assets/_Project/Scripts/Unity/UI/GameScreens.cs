using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using Avatar = HappyWorld.Core.Api.Avatar; // UnityEngine also has an Avatar type
using HappyWorld.Core.Localization;
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
        private Text _nameText, _moneyText, _zoneText;

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

            var top = UiKit.Box("TopBar", _hud, new Color(1, 1, 1, 0.85f)).rectTransform.Place(new Vector2(0, 1), new Vector2(0.36f, 1), new Vector2(16, -150), new Vector2(0, -16));
            _nameText = UiKit.Label(top, "", 32);
            _nameText.rectTransform.Place(new Vector2(0, 0.5f), Vector2.one, new Vector2(20, 0), new Vector2(-10, 0));
            _moneyText = UiKit.Label(top, "", 32);
            _moneyText.rectTransform.Place(Vector2.zero, new Vector2(1, 0.5f), new Vector2(20, 0), new Vector2(-10, 0));

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
            });
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

        private void ShowShop()
        {
            var list = OpenModal(Texts.T("ui.shop"));
            Loading(list);
            _s.Run(async () =>
            {
                var all = await _s.Api.GetBusinessesAsync();
                UiKit.Clear(list);
                if (all.Count == 0) { UiKit.Row(list, "ยังไม่มีร้าน"); return; }
                var b = all[0];
                UiKit.Row(list, b.name + "  Lv." + b.level + "/" + b.maxLevel + "\n<size=24>รายรับ " + b.finance.totalRevenue + " · รายจ่าย " + b.finance.totalExpense + " · กำไร " + b.finance.profit + "</size>", 110);
                foreach (var p in b.products)
                {
                    var code = p.productCode;
                    var row = UiKit.Row(list, code + "  สต็อก " + p.quantity + "/" + b.stockCapacity + "\n<size=24>ต้นทุน " + p.unitCost + " · ขาย " + p.sellPrice + " (" + p.minPrice + "–" + p.maxPrice + ")</size>", 110);
                    UiKit.RowButton(row, "-1", UiKit.Muted, () => SetPrice(b.id, code, p.sellPrice - 1), 80);
                    UiKit.RowButton(row, "+1", UiKit.Muted, () => SetPrice(b.id, code, p.sellPrice + 1), 80);
                    var key = IdempotencyKeys.New();
                    UiKit.RowButton(row, Texts.T("ui.restock") + " 5", UiKit.Primary, () => _s.Run(async () =>
                    {
                        await _s.Api.RestockAsync(b.id, code, 5, key);
                        ShowShop();
                    }), 200);
                }
                UiKit.RowButton(UiKit.Row(list, "ลูกค้ามาซื้อตามราคาและสต็อก"), Texts.T("ui.sell"), UiKit.Good, () => _s.Run(async () =>
                {
                    var r = await _s.Api.SellAsync(b.id);
                    var tip = r.lines.SelectMany(l => l.tipsText).FirstOrDefault();
                    Toast("ขายได้ " + r.totals.unitsSold + " ชิ้น กำไร " + r.totals.profit + (tip != null ? "\n" + tip : ""));
                    ShowShop();
                }), 300);
                if (b.upgradeCost.HasValue)
                    UiKit.RowButton(UiKit.Row(list, "อัปเกรดร้าน (" + Texts.T("ui.gold") + " " + b.upgradeCost + ")"), "อัปเกรด", UiKit.Primary, () => _s.Run(async () =>
                    {
                        await _s.Api.UpgradeBusinessAsync(b.id, b.level);
                        ShowShop();
                    }));
            });
        }

        private void SetPrice(string businessId, string code, int price)
        {
            _s.Run(async () =>
            {
                await _s.Api.SetPriceAsync(businessId, code, price);
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
