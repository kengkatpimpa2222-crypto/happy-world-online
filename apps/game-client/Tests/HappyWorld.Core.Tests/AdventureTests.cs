using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using Newtonsoft.Json;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 9: exploring and crafting DTOs (JSON captured from the real backend) and the trip story.</summary>
    public class AdventureTests
    {
        // POST /expeditions/:id/claim and GET /crafting from a dev backend (EXPEDITION_TIME_SCALE=0.02), 14 Oct 2026
        private const string ClaimJson = @"{""id"": ""ce0b11a9-9ea2-491e-8748-539107963d3a"", ""site"": ""ORCHARD"", ""plan"": ""BOLD"", ""explorerLevel"": 1, ""startedAt"": ""2026-10-14T21:44:30.908Z"", ""endsAt"": ""2026-10-14T21:44:34.908Z"", ""secondsLeft"": 0, ""ready"": false, ""claimed"": true, ""eventChances"": {""MONKEYS"": 0.45, ""RAIN_SHOWER"": 0.55, ""LOST_PUPPY"": 0.65}, ""result"": {""loot"": {""MANGO"": 1, ""COCONUT"": 2, ""STICKY_RICE"": 2}, ""events"": [{""lost"": {""STICKY_RICE"": 2}, ""event"": ""MONKEYS"", ""gained"": {}, ""success"": false}, {""lost"": {""STICKY_RICE"": 1}, ""event"": ""RAIN_SHOWER"", ""gained"": {}, ""success"": false}], ""gathered"": {""MANGO"": 1, ""COCONUT"": 2, ""STICKY_RICE"": 5}, ""playerXp"": 5, ""explorerXp"": 10, ""playerLevel"": null, ""explorerLeveledUp"": false, ""explorerLevelAfter"": 1}, ""replayed"": false, ""skills"": {""EXPLORER"": {""skill"": ""EXPLORER"", ""level"": 1, ""xp"": 10, ""levelXp"": 0, ""nextLevelXp"": 40}, ""CRAFTER"": {""skill"": ""CRAFTER"", ""level"": 1, ""xp"": 0, ""levelXp"": 0, ""nextLevelXp"": 40}}, ""materials"": {""MANGO"": 1, ""COCONUT"": 2, ""STICKY_RICE"": 2, ""JASMINE"": 0, ""MARIGOLD"": 0, ""WILD_HONEY"": 0, ""LEMONGRASS"": 0, ""HERB"": 0, ""BEESWAX"": 0}}";
        private const string CraftingJson = @"{""recipes"": [{""recipe"": ""MANGO_STICKY_RICE"", ""productCode"": ""MANGO_STICKY_RICE"", ""inputs"": {""MANGO"": 1, ""STICKY_RICE"": 1, ""COCONUT"": 1}, ""outputUnits"": 3, ""feeGold"": 6, ""crafterLevel"": 1, ""unlocked"": true, ""canMake"": 1, ""basePrice"": 35, ""minPrice"": 20, ""maxPrice"": 70}, {""recipe"": ""JASMINE_GARLAND"", ""productCode"": ""JASMINE_GARLAND"", ""inputs"": {""JASMINE"": 3, ""MARIGOLD"": 1}, ""outputUnits"": 2, ""feeGold"": 4, ""crafterLevel"": 1, ""unlocked"": true, ""canMake"": 0, ""basePrice"": 28, ""minPrice"": 15, ""maxPrice"": 60}, {""recipe"": ""HERBAL_BALM"", ""productCode"": ""HERBAL_BALM"", ""inputs"": {""HERB"": 2, ""LEMONGRASS"": 1, ""BEESWAX"": 1}, ""outputUnits"": 2, ""feeGold"": 8, ""crafterLevel"": 2, ""unlocked"": false, ""canMake"": 0, ""basePrice"": 45, ""minPrice"": 25, ""maxPrice"": 90}], ""materials"": {""MANGO"": 1, ""COCONUT"": 2, ""STICKY_RICE"": 2, ""JASMINE"": 0, ""MARIGOLD"": 0, ""WILD_HONEY"": 0, ""LEMONGRASS"": 0, ""HERB"": 0, ""BEESWAX"": 0}, ""crafter"": {""skill"": ""CRAFTER"", ""level"": 1, ""xp"": 0, ""levelXp"": 0, ""nextLevelXp"": 40}}";

        [Fact]
        public void Parses_a_claimed_trip_with_events_loot_and_skills()
        {
            var t = JsonConvert.DeserializeObject<ExpeditionTrip>(ClaimJson);
            Assert.Equal("ORCHARD", t.site);
            Assert.Equal("BOLD", t.plan);
            Assert.True(t.claimed);
            Assert.Equal(2, t.result.events.Count);
            Assert.Equal("MONKEYS", t.result.events[0].@event);
            Assert.False(t.result.events[0].success);
            Assert.Equal(2, t.result.events[0].lost["STICKY_RICE"]);
            Assert.Equal(new[] { 1, 2, 2 }, new[] { t.result.loot["MANGO"], t.result.loot["COCONUT"], t.result.loot["STICKY_RICE"] });
            Assert.Equal(10, t.skills["EXPLORER"].xp);
            Assert.Equal(40, t.skills["EXPLORER"].nextLevelXp);
            Assert.Null(t.result.playerLevel);
            Assert.Equal(0.45f, t.eventChances["MONKEYS"], 3);
        }

        [Fact]
        public void Tells_the_trip_as_a_story_in_Thai()
        {
            var t = JsonConvert.DeserializeObject<ExpeditionTrip>(ClaimJson);
            var lines = AdventurePresenter.Story(t);
            Assert.Contains("สวนผลไม้ริมคลอง", lines[0]);
            Assert.Contains("ไปแบบบุก", lines[0]);
            Assert.Equal("ระหว่างทางเก็บได้ มะม่วง 1 · มะพร้าว 2 · ข้าวเหนียว 5", lines[1]);
            Assert.StartsWith("✗ ฝูงลิงไวกว่า", lines[2]);
            Assert.EndsWith("(-ข้าวเหนียว 2)", lines[2]);
            Assert.Contains("ได้กลับบ้าน: มะม่วง 1 · มะพร้าว 2 · ข้าวเหนียว 2", lines[4]);
            Assert.Equal("XP นักสำรวจ +10 · XP ตัวละคร +5", lines[5]);
            Assert.Equal("-", AdventurePresenter.Bag(null));
        }

        [Fact]
        public void Every_event_code_has_its_own_lines()
        {
            var codes = new[] { "MONKEYS", "RAIN_SHOWER", "LOST_PUPPY", "WILD_BEES", "FOG", "BUTTERFLIES", "BUFFALO_BRIDGE", "SLIPPERY_ROCKS", "HERB_DOCTOR", "HIVE_IN_TREE" };
            foreach (var c in codes)
            {
                var ok = AdventurePresenter.EventLine(new ExpeditionEventOutcome { @event = c, success = true });
                var bad = AdventurePresenter.EventLine(new ExpeditionEventOutcome { @event = c, success = false });
                Assert.NotEqual("✓ " + c, ok);
                Assert.NotEqual("✗ " + c, bad);
            }
            foreach (var m in AdventurePresenter.MaterialOrder) Assert.NotEqual(m, AdventurePresenter.MaterialName(m));
        }

        [Fact]
        public void Parses_recipes_and_marks_missing_materials()
        {
            var o = JsonConvert.DeserializeObject<CraftingOverview>(CraftingJson);
            Assert.Equal(3, o.recipes.Count);
            var rice = o.recipes.First(r => r.recipe == "MANGO_STICKY_RICE");
            Assert.True(rice.unlocked);
            Assert.Equal(1, rice.canMake);
            Assert.Equal(3, rice.outputUnits);
            Assert.False(o.recipes.First(r => r.recipe == "HERBAL_BALM").unlocked);
            Assert.Equal("มะม่วง 1 (มี 1) + มะพร้าว 1 (มี 2) + ข้าวเหนียว 1 (มี 2)", AdventurePresenter.Inputs(rice, o.materials));
            var garland = o.recipes.First(r => r.recipe == "JASMINE_GARLAND");
            Assert.Contains("<color=#C0392B>ดอกมะลิ 3 (มี 0)</color>", AdventurePresenter.Inputs(garland, o.materials));
            Assert.Equal("ข้าวเหนียวมะม่วง", MarketNpcPlanner.ProductName("MANGO_STICKY_RICE"));
            Assert.Equal("พวง", MarketNpcPlanner.ProductUnit("JASMINE_GARLAND"));
            Assert.Equal("เหลือ 2:05", AdventurePresenter.TimeLeft(125));
            Assert.Equal("ถึงแล้ว", AdventurePresenter.TimeLeft(0));
        }
    }
}
