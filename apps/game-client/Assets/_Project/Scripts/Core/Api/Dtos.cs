using System;
using System.Collections.Generic;
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
        public List<BusinessProduct> products = new List<BusinessProduct>();
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
}
