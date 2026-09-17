using System;
using System.Collections.Generic;
using TCGCollector.Save;
using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>One entry in the fixed daily quest catalog. Plain data, not a ScriptableObject -
    /// no in-editor authoring needed, just the hardcoded quests below.</summary>
    [Serializable]
    public class QuestDefinition
    {
        public string questId;
        public string title;
        public int target;
        public int rewardCurrency;
        public int rewardBoosters;

        public QuestDefinition(string questId, string title, int target, int rewardCurrency, int rewardBoosters)
        {
            this.questId = questId;
            this.title = title;
            this.target = target;
            this.rewardCurrency = rewardCurrency;
            this.rewardBoosters = rewardBoosters;
        }
    }

    /// <summary>A quest's definition plus today's live progress - what the UI actually reads.</summary>
    public readonly struct QuestStatus
    {
        public readonly QuestDefinition Definition;
        public readonly int Progress;
        public readonly bool Claimed;

        public QuestStatus(QuestDefinition definition, int progress, bool claimed)
        {
            Definition = definition;
            Progress = progress;
            Claimed = claimed;
        }

        public bool IsComplete => Progress >= Definition.target;
    }

    /// <summary>
    /// Two simple, fixed daily quests - open a booster, and log in that day. "daily_login" pays a
    /// bonus booster only; "open_booster" also pays a small currency amount, giving a brand-new
    /// player (currency starts at 0) a guaranteed trickle of currency before they pull any dupes.
    /// Quests reset every calendar day (UTC).
    ///
    /// Shares its PlayerSaveData instance with CollectionManager, so a single
    /// CollectionManager.Save() call persists both collection and quest state together.
    /// </summary>
    public class QuestManager
    {
        public static readonly List<QuestDefinition> Catalog = new List<QuestDefinition>
        {
            new QuestDefinition("open_booster", "Ouvre un booster", 1, 20, 1),
            new QuestDefinition("daily_login", "Connecte-toi", 1, 0, 1),
        };

        /// <summary>Consecutive daily logins needed to trigger the streak bonus below.</summary>
        public const int StreakGoalDays = 7;

        /// <summary>Bonus boosters granted once the streak reaches StreakGoalDays - on top of
        /// whatever the day's quests themselves pay out.</summary>
        public const int StreakRewardBoosters = 10;

        private readonly PlayerSaveData _saveData;
        private readonly CollectionManager _collection;

        public QuestManager(PlayerSaveData saveData, CollectionManager collection)
        {
            _saveData = saveData;
            _collection = collection;
            EnsureTodaysQuests();
        }

        /// <summary>Current consecutive-day login streak, for the UI (see QuestScreen).</summary>
        public int LoginStreakDays => _saveData.loginStreakDays;

        /// <summary>Resets quest progress the first time this is called on a new calendar day.
        /// Called defensively at the top of every public method to avoid stale progress.
        /// "daily_login" starts already at its target - just running the game that day completes
        /// it.</summary>
        private void EnsureTodaysQuests()
        {
            string today = TodayIso();
            if (_saveData.quests.questsDateIso == today) return;

            UpdateLoginStreak(today);

            _saveData.quests.questsDateIso = today;
            _saveData.quests.entries.Clear();
            foreach (var def in Catalog)
            {
                int startingProgress = def.questId == "daily_login" ? def.target : 0;
                _saveData.quests.entries.Add(new QuestProgressEntry
                {
                    questId = def.questId,
                    progress = startingProgress,
                    claimed = false
                });
            }
        }

        /// <summary>Advances the consecutive-day login streak: +1 if the last recorded day was
        /// exactly yesterday, resets to 1 otherwise. Every StreakGoalDays (7) days grants
        /// StreakRewardBoosters (10) bonus boosters and restarts the cycle. Only called once per
        /// new calendar day.</summary>
        private void UpdateLoginStreak(string today)
        {
            string yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
            _saveData.loginStreakDays = _saveData.lastLoginDateIso == yesterday ? _saveData.loginStreakDays + 1 : 1;
            _saveData.lastLoginDateIso = today;

            if (_saveData.loginStreakDays >= StreakGoalDays)
            {
                _collection.AddBonusFreeBoosters(StreakRewardBoosters);
                _saveData.loginStreakDays = 0;

                // Saved immediately (unlike other mutations here) - an automatic reward with no
                // claim button is too easy to silently lose otherwise.
                _collection.Save();
            }
        }

        private QuestProgressEntry FindEntry(string questId)
        {
            foreach (var entry in _saveData.quests.entries)
            {
                if (entry.questId == questId) return entry;
            }
            return null;
        }

        /// <summary>Call once per booster opened (see GameManager.OpenBooster).</summary>
        public void RegisterBoosterOpened()
        {
            EnsureTodaysQuests();
            var entry = FindEntry("open_booster");
            if (entry != null && !entry.claimed && entry.progress < 1)
                entry.progress = 1;
        }

        /// <summary>No longer tracks anything - kept as a no-op so GameManager.OpenBooster doesn't
        /// need to change its call site.</summary>
        public void RegisterPullResults(List<CollectionManager.CardPullResult> results)
        {
        }

        /// <summary>Today's quests with live progress, in catalog order - what the UI binds to.</summary>
        public List<QuestStatus> GetTodaysQuests()
        {
            EnsureTodaysQuests();
            var list = new List<QuestStatus>();
            foreach (var def in Catalog)
            {
                var entry = FindEntry(def.questId);
                int progress = entry != null ? entry.progress : 0;
                bool claimed = entry != null && entry.claimed;
                list.Add(new QuestStatus(def, progress, claimed));
            }
            return list;
        }

        /// <summary>Pays out a completed quest's reward once. Returns false (no-op) if not yet
        /// complete or already claimed today. Does not save to disk - caller should call
        /// CollectionManager.Save() afterwards.</summary>
        public bool TryClaimReward(string questId)
        {
            EnsureTodaysQuests();
            var entry = FindEntry(questId);
            if (entry == null || entry.claimed) return false;

            QuestDefinition def = null;
            foreach (var candidate in Catalog)
            {
                if (candidate.questId == questId) { def = candidate; break; }
            }
            if (def == null || entry.progress < def.target) return false;

            entry.claimed = true;
            if (def.rewardCurrency > 0) _collection.AddCurrency(def.rewardCurrency);
            if (def.rewardBoosters > 0) _collection.AddBonusFreeBoosters(def.rewardBoosters);
            return true;
        }

        private static string TodayIso() => DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
}
