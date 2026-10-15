using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 10: home DTOs (JSON captured from the real backend) and the decorate-my-home editor.</summary>
    public class HomeTests
    {
        // PUT /homes/me/layout and GET /homes/me/furniture from a dev backend, 15 Oct 2026
        private const string SaveJson = @"{""replayed"":false,""placed"":2,""moved"":0,""removed"":0,""version"":1,""home"":{""homeId"":""598e4a73-b0f3-4178-ba70-44b5a44afbe7"",""owner"":{""userId"":""48bfe769-34a5-4ad9-9c5c-a20e183f6f61"",""displayName"":""Player2968"",""level"":1},""isMine"":true,""isFriend"":false,""claimed"":true,""claimedAt"":""2026-10-15T03:49:03.242Z"",""plot"":{""plotNumber"":0,""street"":0,""slot"":0,""x"":4,""y"":24,""door"":{""x"":4,""y"":21.8},""facing"":""S""},""visitPolicy"":""FRIENDS"",""gridWidth"":8,""gridHeight"":8,""interior"":{""width"":12,""height"":12,""spawn"":{""x"":6,""y"":0.8}},""layoutVersion"":1,""furniture"":[{""id"":""9bd340b5-74e3-44be-8f17-ef75ee6d52a7"",""itemId"":""furn_wood_chair"",""x"":0,""y"":0,""rotation"":0,""footprint"":[1,1]},{""id"":""08992d46-fa5b-4914-a7e3-492770ab94fc"",""itemId"":""furn_wood_table"",""x"":3,""y"":3,""rotation"":90,""footprint"":[2,1]}]}}";
        private const string BagJson = @"[{""itemId"":""furn_wood_chair"",""nameKey"":""item.furn_wood_chair.name"",""footprint"":[1,1],""inBag"":0,""placed"":1},{""itemId"":""furn_wood_table"",""nameKey"":""item.furn_wood_table.name"",""footprint"":[2,1],""inBag"":0,""placed"":1},{""itemId"":""furn_plant_pot"",""nameKey"":""item.furn_plant_pot.name"",""footprint"":[1,1],""inBag"":2,""placed"":0},{""itemId"":""furn_single_bed"",""nameKey"":""item.furn_single_bed.name"",""footprint"":[1,2],""inBag"":1,""placed"":0}]";

        private static HomeView Home => JsonConvert.DeserializeObject<SaveLayoutResult>(SaveJson).home;
        private static List<FurnitureBagItem> Bag => JsonConvert.DeserializeObject<List<FurnitureBagItem>>(BagJson);
        private static HomeLayoutEditor NewEditor() => new HomeLayoutEditor(Home, Bag);

        [Fact]
        public void Parses_a_saved_home_and_the_furniture_bag()
        {
            var r = JsonConvert.DeserializeObject<SaveLayoutResult>(SaveJson);
            Assert.Equal(2, r.placed);
            Assert.Equal(1, r.version);
            var h = r.home;
            Assert.True(h.isMine && h.claimed);
            Assert.Equal("FRIENDS", h.visitPolicy);
            Assert.Equal(0, h.plot.plotNumber);
            Assert.Equal(21.8f, h.plot.door.y, 3);
            Assert.Equal(12f, h.interior.width);
            Assert.Equal(0.8f, h.interior.spawn.y, 3);
            var table = h.furniture.Single(f => f.itemId == "furn_wood_table");
            Assert.Equal(new[] { 2, 1 }, table.footprint);
            Assert.Equal(90, table.rotation);
            Assert.Equal(2, Bag.Single(b => b.itemId == "furn_plant_pot").inBag);
        }

        [Fact]
        public void Reads_the_realtime_home_state()
        {
            var json = @"{""ownerId"":""a"",""ownerName"":""บ้านต้น"",""isMine"":false,""room"":""home:a"",""interior"":{""width"":12,""height"":12,""spawn"":{""x"":6,""y"":0.8}},""layoutVersion"":3,""you"":{""x"":6,""y"":0.8},""players"":[{""userId"":""a"",""displayName"":""ต้น"",""level"":2,""avatar"":{},""x"":3,""y"":4,""dir"":0}]}";
            var s = JObject.Parse(json).ToObject<HomeState>();
            Assert.Equal("บ้านต้น", s.ownerName);
            Assert.Equal(3, s.layoutVersion);
            Assert.Single(s.players);
            Assert.Equal(12f, s.interior.height);
        }

        [Fact]
        public void A_turned_piece_swaps_width_and_depth()
        {
            var e = NewEditor();
            var table = e.Pieces.Single(p => p.ItemId == "furn_wood_table");
            Assert.Equal((1, 2), (table.W, table.H)); // footprint [2,1] turned 90
            Assert.True(table.Covers(3, 4));
            Assert.False(table.Covers(4, 3));
            Assert.Same(table, e.PieceAt(3, 4));
        }

        [Fact]
        public void Placing_from_the_bag_and_saving_sends_only_the_layout()
        {
            var e = NewEditor();
            Assert.False(e.Dirty);
            Assert.True(e.TakeFromBag("furn_single_bed"));
            Assert.Equal(0, e.InBag("furn_single_bed"));
            Assert.False(e.TakeFromBag("furn_plant_pot")); // one piece in hand at a time
            e.MoveHeld(6, 0);
            Assert.True(e.HeldFits);
            Assert.True(e.Drop());
            Assert.True(e.Dirty);

            var body = e.ToRequest();
            Assert.Equal(3, body.Count);
            var bed = body.Single(p => p.itemId == "furn_single_bed");
            Assert.Null(bed.id); // comes out of the bag on Save
            Assert.Equal((6, 0, 0), (bed.x, bed.y, bed.rotation));
            Assert.NotNull(body.Single(p => p.itemId == "furn_wood_chair").id);
            Assert.Equal(1, e.BaseVersion);
            Assert.Null(HomeLayoutEditor.FindProblem(e.Pieces, e.GridWidth, e.GridHeight));
            // the JSON the server gets: same field names as saveHomeLayoutSchema
            var json = JArray.FromObject(body).ToString(Formatting.None);
            Assert.Contains("\"itemId\":\"furn_single_bed\"", json);
        }

        [Fact]
        public void A_piece_that_overlaps_or_sticks_out_shows_red_and_cannot_be_dropped()
        {
            var e = NewEditor();
            e.TakeFromBag("furn_plant_pot");
            e.MoveHeld(0, 0); // the chair stands there
            Assert.False(e.HeldFits);
            Assert.False(e.Drop());
            e.MoveHeld(3, 3); // the turned table covers (3,3) and (3,4)
            Assert.False(e.HeldFits);
            e.MoveHeld(3, 5);
            Assert.True(e.HeldFits);
            // moving is clamped inside the walls
            e.MoveHeld(50, -4);
            Assert.Equal((7, 0), (e.Held.X, e.Held.Y));
        }

        [Fact]
        public void Turning_a_long_piece_at_the_wall_steps_it_back_inside()
        {
            var e = NewEditor();
            e.TakeFromBag("furn_single_bed"); // 1x2
            e.MoveHeld(7, 6);
            Assert.Equal((7, 6), (e.Held.X, e.Held.Y));
            e.RotateHeld(); // now 2x1: x=7 would stick out
            Assert.Equal(90, e.Held.Rotation);
            Assert.Equal(6, e.Held.X);
            e.RotateHeld();
            e.RotateHeld();
            e.RotateHeld();
            Assert.Equal(0, e.Held.Rotation);
        }

        [Fact]
        public void Moving_keeps_the_id_and_cancel_puts_things_back()
        {
            var e = NewEditor();
            var chairId = e.Pieces.Single(p => p.ItemId == "furn_wood_chair").Id;
            Assert.True(e.PickUpAt(0, 0));
            e.MoveHeld(5, 5);
            e.RotateHeld();
            e.CancelHeld(); // changed my mind: back where it was
            var chair = e.Pieces.Single(p => p.ItemId == "furn_wood_chair");
            Assert.Equal((0, 0, 0), (chair.X, chair.Y, chair.Rotation));
            Assert.False(e.Dirty);

            Assert.True(e.PickUpAt(0, 0));
            e.MoveHeld(7, 7);
            Assert.True(e.Drop());
            var moved = e.ToRequest().Single(p => p.itemId == "furn_wood_chair");
            Assert.Equal(chairId, moved.id);
            Assert.Equal((7, 7), (moved.x, moved.y));

            e.TakeFromBag("furn_plant_pot");
            e.CancelHeld();
            Assert.Equal(2, e.InBag("furn_plant_pot"));
        }

        [Fact]
        public void Putting_a_placed_piece_away_removes_it_from_the_layout_and_adds_it_to_the_bag()
        {
            var e = NewEditor();
            Assert.True(e.PickUpAt(3, 4));
            e.PutHeldInBag();
            Assert.Equal(1, e.InBag("furn_wood_table"));
            Assert.DoesNotContain(e.ToRequest(), p => p.itemId == "furn_wood_table");
            Assert.True(e.Dirty);
            Assert.Contains("furn_wood_table", e.BagItems);
        }

        [Fact]
        public void Grid_cells_and_world_points_line_up_with_the_server_room()
        {
            HomePresenter.CellAt(0.1f, 0.1f, out var x, out var y);
            Assert.Equal((0, 0), (x, y));
            HomePresenter.CellAt(11.9f, 4.6f, out x, out y);
            Assert.Equal((7, 3), (x, y));
            HomePresenter.CellAt(-0.2f, 3f, out x, out y);
            Assert.Equal(-1, x);
            var table = HomePresenter.ToPiece(Home.furniture.Single(f => f.itemId == "furn_wood_table")); // (3,3) turned: 1x2
            HomePresenter.PieceCenter(table, out var cx, out var cz);
            Assert.Equal((5.25f, 6f), (cx, cz));
            Assert.True(HomePresenter.AtExit(6f, 0.4f, 12f));
            Assert.False(HomePresenter.AtExit(6f, 0.8f, 12f)); // where you arrive is not the exit
            Assert.False(HomePresenter.AtExit(1f, 0.3f, 12f));
        }

        [Fact]
        public void Door_prompts_and_signs_depend_on_who_lives_there()
        {
            var owner = new HomeOwner { userId = "u", displayName = "ต้น" };
            Assert.Null(HomePresenter.DoorPrompt(new DistrictPlot { taken = false }));
            Assert.Equal("เข้าบ้าน", HomePresenter.DoorPrompt(new DistrictPlot { taken = true, isMine = true, owner = owner }));
            Assert.Equal("เยี่ยมบ้าน ต้น", HomePresenter.DoorPrompt(new DistrictPlot { taken = true, owner = owner, canVisit = true }));
            Assert.Contains("ยังไม่เปิด", HomePresenter.DoorPrompt(new DistrictPlot { taken = true, owner = owner }));
            Assert.Null(HomePresenter.DoorPrompt(new DistrictPlot { taken = true, owner = null })); // blocked: nameless, no door
            Assert.Equal("♥ บ้าน ต้น", HomePresenter.PlotSign(new DistrictPlot { taken = true, owner = owner, isFriend = true }));
            Assert.Equal("ที่ว่าง", HomePresenter.PlotSign(new DistrictPlot()));
            Assert.Equal("ถนนที่ 1 บ้านเลขที่ 3", HomePresenter.Address(new HomePlot { street = 0, slot = 2 }));
            Assert.Equal(new[] { "ส่วนตัว", "เฉพาะเพื่อน", "ทุกคน" }, HomePresenter.Policies.Select(HomePresenter.PolicyName));
        }

        [Fact]
        public void Walking_into_a_home_replaces_the_people_around_me()
        {
            var city = new CityState { MyUserId = "me" };
            city.ApplyZoneState(new ZoneState { zone = "HOMES", room = "zone:HOMES:ADULT_18_PLUS:0", players = { new RemotePlayerInfo { userId = "street" } } });
            Assert.NotNull(city.Get("street"));
            city.ApplyHomeState(new HomeState { ownerId = "o", room = "home:o", you = new ZonePoint { x = 6, y = 0.8f }, players = { new RemotePlayerInfo { userId = "o", x = 3, y = 3 }, new RemotePlayerInfo { userId = "me" } } });
            Assert.Equal(CityState.HomeZone, city.Zone);
            Assert.Equal("home:o", city.Room);
            Assert.Null(city.Get("street"));
            Assert.NotNull(city.Get("o"));
            Assert.Equal(1, city.Count);
        }

        [Fact]
        public void Layout_rules_match_the_server()
        {
            HomeLayoutEditor.Piece P(int x, int y, int rot, int w = 1, int h = 1) => new HomeLayoutEditor.Piece { ItemId = "x", X = x, Y = y, Rotation = rot, FootW = w, FootH = h };
            Assert.Null(HomeLayoutEditor.FindProblem(new[] { P(0, 0, 0, 1, 2), P(3, 3, 0, 2, 1), P(7, 7, 270) }, 8, 8));
            Assert.Equal("OUT_OF_BOUNDS", HomeLayoutEditor.FindProblem(new[] { P(7, 0, 0, 2, 1) }, 8, 8));
            Assert.Equal("OUT_OF_BOUNDS", HomeLayoutEditor.FindProblem(new[] { P(2, 7, 0, 1, 2) }, 8, 8));
            Assert.Null(HomeLayoutEditor.FindProblem(new[] { P(0, 7, 90, 1, 2) }, 8, 8));
            Assert.Equal("OVERLAP", HomeLayoutEditor.FindProblem(new[] { P(2, 2, 0, 2, 1), P(3, 2, 0) }, 8, 8));
            Assert.Equal("BAD_ROTATION", HomeLayoutEditor.FindProblem(new[] { P(1, 1, 45) }, 8, 8));
            Assert.Equal("TOO_MANY", HomeLayoutEditor.FindProblem(Enumerable.Range(0, 41).Select(i => P(i % 8, i / 8, 0)).ToList(), 8, 8));
        }
    }
}
