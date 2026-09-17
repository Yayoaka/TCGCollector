using System;
using System.Collections.Generic;

namespace TCGCollector.Save
{
    /// <summary>One owned card entry. Unity's JsonUtility can't serialize a Dictionary directly,
    /// so the save file stores a flat list of these instead.</summary>
    [Serializable]
    public class OwnedCardEntry
    {
        public string cardId;
        public int count;
    }

    /// <summary>One Set's banked/unopened booster count (flat list, same JsonUtility reason as
    /// ownedCards). Filled when a Display is bought "one by one" (see GameManager.
    /// PurchaseDisplayBanked) and drained via CollectionManager.TryConsumeBankedBooster.</summary>
    [Serializable]
    public class BankedBoosterEntry
    {
        public string setId;
        public int count;
    }

    /// <summary>One quest's progress for the current day. Unity's JsonUtility can't serialize a
    /// Dictionary directly, so - same as ownedCards above - this is a flat list instead.</summary>
    [Serializable]
    public class QuestProgressEntry
    {
        public string questId;
        public int progress;
        public bool claimed;
    }

    /// <summary>The player's daily quest progress (see QuestManager.Catalog) plus the date they
    /// were last reset for. QuestManager wipes and rebuilds entries when questsDateIso no longer
    /// matches "today".</summary>
    [Serializable]
    public class QuestSaveData
    {
        public string questsDateIso = "";
        public List<QuestProgressEntry> entries = new List<QuestProgressEntry>();
    }

    /// <summary>One licence's shop offer for a given day (see ShopManager). cardId is empty when
    /// the licence had nothing eligible to offer - the UI shows a placeholder instead of a buy
    /// button.</summary>
    [Serializable]
    public class ShopOfferEntry
    {
        public string licenseId;
        public string cardId;
    }

    /// <summary>The Boutique's daily rotating offers (one per Licence) plus the date they were
    /// last generated for - same reset pattern as QuestSaveData, driven by ShopManager.</summary>
    [Serializable]
    public class ShopSaveData
    {
        public string shopDateIso = "";
        public List<ShopOfferEntry> entries = new List<ShopOfferEntry>();
    }

    /// <summary>Everything that needs to survive between play sessions, for V0.1.</summary>
    [Serializable]
    public class PlayerSaveData
    {
        public const int CurrentSaveVersion = 1;

        public int saveVersion = CurrentSaveVersion;

        public List<OwnedCardEntry> ownedCards = new List<OwnedCardEntry>();

        /// <summary>ISO-8601 date (yyyy-MM-dd) of the last time the free daily booster was claimed. Empty = never.</summary>
        public string lastFreeBoosterDateIso = "";

        /// <summary>Currency earned by auto-selling duplicates (see CollectionManager.AddCards).</summary>
        public int currency = 0;

        /// <summary>Daily quests progress - see QuestManager.</summary>
        public QuestSaveData quests = new QuestSaveData();

        /// <summary>Bonus boosters earned from quests (see QuestManager.TryClaimReward), usable
        /// even after today's free slot is used. See CollectionManager.BonusFreeBoosters.</summary>
        public int bonusFreeBoosters = 0;

        /// <summary>Consecutive daily login streak (see QuestManager.EnsureTodaysQuests) -
        /// increments once per new day, resets to 1 if a day was skipped. Reaching
        /// QuestManager.StreakGoalDays grants a bonus and resets to 0.</summary>
        public int loginStreakDays = 0;

        /// <summary>ISO-8601 date the streak above was last updated - lets QuestManager tell
        /// whether "today" is new and consecutive with the previous day.</summary>
        public string lastLoginDateIso = "";

        /// <summary>Unopened boosters banked per Set (keyed by CardSet.setId, see
        /// BankedBoosterEntry). Consumed one at a time via
        /// CollectionManager.TryConsumeBankedBooster.</summary>
        public List<BankedBoosterEntry> bankedBoosters = new List<BankedBoosterEntry>();

        /// <summary>The Boutique's daily rotating offers - see ShopManager.</summary>
        public ShopSaveData shop = new ShopSaveData();

        /// <summary>UTC tick count of the last save to disk (stamped automatically by
        /// SaveManager.Save). Used by CloudSyncService to decide which save is newer when local
        /// and cloud disagree - higher value wins.</summary>
        public long lastModifiedUtcTicks = 0;

        /// <summary>Overwrites every field with values from <paramref name="other"/> - used by
        /// CloudSyncService to update the shared PlayerSaveData instance in place when the cloud
        /// save is newer. Caller must also call CollectionManager.RebuildLookupsFromSaveData()
        /// after, since its dictionaries are only built from these lists in the constructor.</summary>
        public void CopyFrom(PlayerSaveData other)
        {
            saveVersion = other.saveVersion;
            ownedCards = other.ownedCards;
            lastFreeBoosterDateIso = other.lastFreeBoosterDateIso;
            currency = other.currency;
            quests = other.quests;
            bonusFreeBoosters = other.bonusFreeBoosters;
            loginStreakDays = other.loginStreakDays;
            lastLoginDateIso = other.lastLoginDateIso;
            bankedBoosters = other.bankedBoosters;
            shop = other.shop;
            lastModifiedUtcTicks = other.lastModifiedUtcTicks;
        }
    }
}
