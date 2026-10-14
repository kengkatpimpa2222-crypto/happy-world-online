using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// DTOs mirror the JSON the NestJS backend actually returns (apps/backend/src/**/*.service.ts).
// Field names are camelCase to match the wire format; Newtonsoft maps them case-insensitively.
namespace HappyWorld.Core.Api
{
    public static class AgeBand
    {
        public const string Under13 = "UNDER_13";
        public const string Teen = "TEEN_13_17";
        public const string Adult = "ADULT_18_PLUS";
    }

    public sealed class AuthResponse
    {
        public string userId;
        public bool isGuest;
        public string accessToken;
        public int accessTokenExpiresIn;
        public string refreshToken;
        public DateTime refreshTokenExpiresAt;
    }

    public sealed class Avatar
    {
        public string bodyColor;
        public string outfitItemId;
        public string hairItemId;
        public string accessoryItemId;
    }

    public sealed class PlayerProfile
    {
        public string userId;
        public string displayName;
        public string friendCode;
        public int level;
        public int xp;
        public int xpForNextLevel;
        public Avatar avatar;
        public string locale;
        public string ageBand;
        public bool isGuest;
    }

    public sealed class WalletTransaction
    {
        public string id;
        public string currency;
        public string type;
        public int amount;
        public int balanceBefore;
        public int balanceAfter;
        public DateTime createdAt;
    }

    public sealed class Wallet
    {
        public Dictionary<string, int> balances = new Dictionary<string, int>();
        public List<WalletTransaction> recentTransactions = new List<WalletTransaction>();

        public int Gold => balances.TryGetValue("GOLD", out var v) ? v : 0;
        public int Gems => balances.TryGetValue("GEMS", out var v) ? v : 0;
    }

    public sealed class InventoryItem
    {
        public string itemId;
        public int quantity;
        public string category;
        public string nameKey;
        public bool isPremium;
    }

    public sealed class CatalogItem
    {
        public string itemId;
        public string category;
        public string nameKey;
        public int? priceGold;
        public int? priceGems;
        public bool isPremium;
        public int ownedQuantity;
        public bool stackable;
    }

    public sealed class StorePurchaseResult
    {
        public bool replayed;
        public string transactionId;
        public string currency;
        public int amount;
        public int balanceAfter;
        public InventoryItem item;
    }

    public sealed class QuestObjective
    {
        public string type;
        public int target;
    }

    public sealed class QuestRewards
    {
        public int gold;
        public int gems;
        public int xp;
        public string itemId;
    }

    public sealed class QuestProgress
    {
        /// <summary>IN_PROGRESS, COMPLETED or CLAIMED</summary>
        public string status;
        public int value;
    }

    public sealed class Quest
    {
        public string id;
        public string category;
        public string titleKey;
        public string descriptionKey;
        public QuestObjective objective;
        public QuestRewards rewards;
        public bool canStart;
        public QuestProgress progress;

        public bool CanClaim => progress != null && progress.status == "COMPLETED";
    }

    public sealed class QuestClaimResult
    {
        public string questId;
        public QuestRewards rewards;
        public int? level;
        public Dictionary<string, int> wallet;
    }

    public sealed class BusinessProduct
    {
        public string productCode;
        public int quantity;
        public int avgUnitCost;
        public int sellPrice;
        public int unitCost;
        public int minPrice;
        public int maxPrice;
        public int basePrice;
        // Phase 5: market stall
        public int shelfQuantity;
        public bool onStall;
        public int unlockLevel = 1;
    }

    /// <summary>Market stall state of the player's shop (Phase 5).</summary>
    public sealed class ShopStall
    {
        public string status = "CLOSED"; // CLOSED | OPEN | PAUSED
        public int? slot;
        public int cashBox;
        public int totalCustomers;
        public int shelfSlots;
        public int shelfCapacity;
        public float customersPerMinute;
        public int slotsUsed;
        public bool IsOpen => status == "OPEN";
    }

    /// <summary>Every product of the game, with the shop level that unlocks it.</summary>
    public sealed class ShopCatalogProduct
    {
        public string productCode;
        public int unitCost;
        public int minPrice;
        public int maxPrice;
        public int basePrice;
        public int unlockLevel;
        public bool unlocked;
    }

    public sealed class BusinessFinance
    {
        public int totalRevenue;
        public int totalExpense;
        public int profit;
    }

    public sealed class Business
    {
        public string id;
        public string name;
        public int level;
        public int maxLevel;
        public int? upgradeCost;
        public int stockCapacity;
        public BusinessFinance finance;
        public int totalUnitsSold;
        public List<BusinessProduct> products = new List<BusinessProduct>();
        public ShopStall stall = new ShopStall();
        public List<ShopCatalogProduct> catalog = new List<ShopCatalogProduct>();
    }

    public sealed class CollectResult
    {
        public bool replayed;
        public int collected;
        public Dictionary<string, int> wallet;
        public Business business;
    }

    public sealed class ShopTip
    {
        public string code;
        public string textTh;
    }

    public sealed class ShopReportProduct
    {
        public string productCode;
        public int unitsSold;
        public int revenue;
        public int costOfGoods;
        public int profit;
        public int wanted;
        public int lostTooExpensive;
        public int lostSoldOut;
        public bool onStall;
        public int shelfLeft;
        public int? price;
    }

    /// <summary>"While you were away" report: GET /businesses/:id/report.</summary>
    public sealed class ShopReport
    {
        public string businessId;
        public string shopName;
        public string status;
        public DateTime since;
        /// <summary>Raw ISO string, sent back unchanged when acknowledging.</summary>
        [JsonProperty("until")] public string untilIso;
        public bool hasNews;
        public int customers;
        public int buyers;
        public int unitsSold;
        public int revenue;
        public int costOfGoods;
        public int profit;
        public int lostTooExpensive;
        public int lostSoldOut;
        public int lostNotSelling;
        public int cashBox;
        public string bestSeller;
        public List<ShopReportProduct> products = new List<ShopReportProduct>();
        public List<ShopTip> tips = new List<ShopTip>();
    }

    // ---------- Phase 6: city festival ----------

    public sealed class FestivalFeatured
    {
        public string productCode;
        /// <summary>how many times more customers want it during the festival</summary>
        public int demand;
    }

    public sealed class FestivalCity
    {
        public int points;
        public int contributors;
        public int tiersReached;
    }

    public sealed class FestivalTier
    {
        public int tier;
        public int target;
        public bool reached;
        public int rewardGold;
        public int rewardXp;
        public string rewardItemId;
        public bool claimed;
        public bool claimable;
    }

    public sealed class FestivalMe
    {
        public int points;
        public int minToClaim;
    }

    public sealed class FestivalHelper
    {
        public int rank;
        /// <summary>null for other players when the viewer is under 13 (child safety)</summary>
        public string displayName;
        public int points;
        public bool isMe;
    }

    public sealed class FestivalInfo
    {
        public string key;
        public string code;
        public string nameKey;
        public string nameTh;
        /// <summary>#RRGGBB decoration colour</summary>
        public string color;
        /// <summary>UPCOMING, ACTIVE or ENDED</summary>
        public string status;
        public DateTime startsAt;
        public DateTime endsAt;
        public List<FestivalFeatured> featured = new List<FestivalFeatured>();
        public string rewardItemId;
        public FestivalCity city = new FestivalCity();
        public List<FestivalTier> tiers = new List<FestivalTier>();
        public FestivalMe me = new FestivalMe();
        public List<FestivalHelper> top = new List<FestivalHelper>();

        public bool Active => status == "ACTIVE";

        /// <summary>Demand multiplier of a product in this festival (1 = not featured).</summary>
        public int DemandOf(string productCode)
        {
            foreach (var f in featured) if (f.productCode == productCode) return f.demand;
            return 1;
        }

        public int ClaimableCount()
        {
            int n = 0;
            foreach (var t in tiers) if (t.claimable) n++;
            return n;
        }

        /// <summary>The next tier the city is working toward, or null when all are reached.</summary>
        public FestivalTier NextTier()
        {
            foreach (var t in tiers) if (!t.reached) return t;
            return null;
        }
    }

    /// <summary>GET /festival</summary>
    public sealed class FestivalState
    {
        public DateTime now;
        public FestivalInfo current;
        /// <summary>last week's festival, only while it still has rewards for this player</summary>
        public FestivalInfo previous;
    }

    /// <summary>POST /festival/claim</summary>
    public sealed class FestivalClaimResult
    {
        public string festivalKey;
        public int tier;
        public QuestRewards rewards;
        public int? level;
        public Dictionary<string, int> wallet = new Dictionary<string, int>();
    }

    public sealed class MarketStallProduct
    {
        public string productCode;
        public int price;
        public int shelfQuantity;
    }

    public sealed class MarketStall
    {
        public int slot;
        public float x;
        public float y;
        /// <summary>+1: customers stand on the larger-y side, -1: smaller-y side.</summary>
        public int face = 1;
        public string businessId;
        public string name;
        public string ownerId;
        public string ownerName;
        public int level;
        public string status;
        public bool isMine;
        public bool isFriend;
        public List<MarketStallProduct> products = new List<MarketStallProduct>();
    }

    public sealed class MyMarketShop
    {
        public string businessId;
        public string status;
        public int? stallSlot;
    }

    /// <summary>GET /market</summary>
    public sealed class MarketState
    {
        public string zone;
        public int slots;
        public int tickSeconds = 60;
        public List<MarketStall> stalls = new List<MarketStall>();
        public MyMarketShop myShop;
    }

    public sealed class SaleTotals
    {
        public int unitsSold;
        public int revenue;
        public int costOfGoods;
        public int profit;
    }

    public sealed class SaleLine
    {
        public string productCode;
        public int unitsSold;
        public int revenue;
        public int profit;
        public List<string> tips = new List<string>();
        public List<string> tipsText = new List<string>();
    }

    public sealed class SaleResult
    {
        public List<SaleLine> lines = new List<SaleLine>();
        public SaleTotals totals;
        public Business business;
    }

    public sealed class PlayerSummary
    {
        public string userId;
        public string displayName;
        public int level;
        public Avatar avatar;
    }

    public sealed class Friend
    {
        public string userId;
        public string displayName;
        public int level;
        public string friendCode;
        public bool online;
        public string zone;
    }

    public sealed class FriendRequestResult
    {
        public string requestId;
        public string status;
        public bool created;
        public bool autoAccepted;
    }

    public sealed class IncomingRequest
    {
        public string requestId;
        public PlayerSummary from;
    }

    public sealed class OutgoingRequest
    {
        public string requestId;
        public PlayerSummary to;
    }

    public sealed class BlockedPlayer
    {
        public string playerId;
        public string displayName;
    }

    public static class ReportReason
    {
        public const string InappropriateName = "INAPPROPRIATE_NAME";
        public const string Harassment = "HARASSMENT";
        public const string Cheating = "CHEATING";
        public const string Spam = "SPAM";
        public const string AskingPersonalInfo = "ASKING_PERSONAL_INFO";
        public const string Other = "OTHER";
    }

    public sealed class ReportResult
    {
        public string reportId;
        public string status;
        public bool created;
        public bool blocked;
    }

    public sealed class ZonePortal
    {
        public string to;
        public float x;
        public float y;
    }

    public sealed class ZonePoint
    {
        public float x;
        public float y;
    }

    public sealed class Zone
    {
        public string id;
        public string nameKey;
        public float width;
        public float height;
        public ZonePoint spawn;
        public List<ZonePortal> portals = new List<ZonePortal>();
        public int capacity;
    }

    public sealed class PresetMessage
    {
        public string id;
        public string textTh;
    }

    public sealed class RealtimeInfo
    {
        public string @namespace;
        public string path;
    }

    public sealed class CityLayout
    {
        public List<Zone> zones = new List<Zone>();
        public List<PresetMessage> presetMessages = new List<PresetMessage>();
        public RealtimeInfo realtime;
    }

    public sealed class RemotePlayerInfo
    {
        public string userId;
        public string displayName;
        public int level;
        public JToken avatar;
        public float x;
        public float y;
        public int dir;
        public bool isFriend;
    }

    public sealed class CityPlayers
    {
        public string zone;
        public bool inZone;
        public List<RemotePlayerInfo> players = new List<RemotePlayerInfo>();
    }
    // ---------- Living city (Phase 7) ----------

    public sealed class CityDemand
    {
        public string productCode;
        /// <summary>how much more (or less) customers want it in this weather</summary>
        public float demand;
    }

    public sealed class CityForecast
    {
        public DateTime at;
        public string weather;
        public string weatherTh;
    }

    /// <summary>GET /city/now — Bangkok time of day, season and weather.</summary>
    public sealed class CityNow
    {
        public DateTime now;
        /// <summary>Bangkok hour 0..23.999</summary>
        public float hour;
        public string clock;
        /// <summary>NIGHT, DAWN, MORNING, NOON, AFTERNOON or EVENING</summary>
        public string phase;
        public string phaseTh;
        /// <summary>HOT, RAINY or COOL</summary>
        public string season;
        public string seasonTh;
        /// <summary>SUNNY, HOT, CLOUDY or RAIN</summary>
        public string weather;
        public string weatherTh;
        public string tipTh;
        /// <summary>how busy the market is compared to an ordinary hour (1 = normal)</summary>
        public float crowd;
        public List<string> busyTypes = new List<string>();
        public List<CityDemand> demand = new List<CityDemand>();
        public DateTime nextChangeAt;
        public List<CityForecast> forecast = new List<CityForecast>();

        public float DemandOf(string productCode)
        {
            foreach (var d in demand) if (d.productCode == productCode) return d.demand;
            return 1f;
        }
    }

    public sealed class CityProject
    {
        public string code;
        public string nameKey;
        public string nameTh;
        public string effectTh;
        public int cost;
        public string zone;
        public float x;
        public float y;
        /// <summary>DONE, BUILDING or LOCKED</summary>
        public string status;
        public int progress;
        public DateTime? completedAt;

        public bool Done => status == "DONE";
        public bool Building => status == "BUILDING";
    }

    public sealed class CityBuilderMe
    {
        /// <summary>points I gave to the project being built</summary>
        public int points;
        /// <summary>points I gave to every project so far</summary>
        public int total;
    }

    /// <summary>GET /city/growth — the city's public projects.</summary>
    public sealed class CityGrowth
    {
        public int points;
        public int builtCount;
        /// <summary>code of the project being built, null when all are done</summary>
        public string current;
        public List<CityProject> projects = new List<CityProject>();
        public CityBuilderMe me = new CityBuilderMe();
        /// <summary>same shape and child-safety rules as the festival helpers</summary>
        public List<FestivalHelper> top = new List<FestivalHelper>();

        public CityProject Current()
        {
            foreach (var p in projects) if (p.Building) return p;
            return null;
        }
    }
}
