using System;
using System.Collections.Generic;
using TCGCollector.Data;
using TCGCollector.Save;
using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>Owns the player's collection state at runtime (loaded from / saved to disk via
    /// SaveManager). Plain C# class held by GameManager, not a MonoBehaviour, so it's easy to unit
    /// test.</summary>
    public class CollectionManager
    {
        public readonly struct CardPullResult
        {
            public readonly CardData Card;
            public readonly bool WasNew;
            /// <summary>Small automatic bonus for pulling this as a duplicate (rarity.rank + 1 po,
            /// see AddCards). Always 0 for a new card - not the card's real sell value, see
            /// GetSystemBuybackPrice / MarketplaceService for that.</summary>
            public readonly int CurrencyEarned;

            public CardPullResult(CardData card, bool wasNew, int currencyEarned)
            {
                Card = card;
                WasNew = wasNew;
                CurrencyEarned = currencyEarned;
            }
        }

        private readonly CardDatabase _database;
        private readonly PlayerSaveData _saveData;
        private readonly Dictionary<string, int> _ownedLookup = new Dictionary<string, int>();

        /// <summary>Unopened boosters banked per Set (see PlayerSaveData.bankedBoosters) - keyed
        /// via SetKey below.</summary>
        private readonly Dictionary<string, int> _bankedBoosterLookup = new Dictionary<string, int>();

        /// <summary>Takes an already-loaded PlayerSaveData rather than loading its own copy, so it
        /// shares the same in-memory instance with other systems (e.g. QuestManager) - two
        /// independently-loaded copies would silently overwrite each other's changes on save.</summary>
        public CollectionManager(CardDatabase database, PlayerSaveData saveData)
        {
            _database = database;
            _saveData = saveData;

            foreach (var entry in _saveData.ownedCards)
                _ownedLookup[entry.cardId] = entry.count;

            foreach (var entry in _saveData.bankedBoosters)
                _bankedBoosterLookup[entry.setId] = entry.count;
        }

        public bool IsNew(CardData card) =>
            card != null && !string.IsNullOrEmpty(card.cardId) && !_ownedLookup.ContainsKey(card.cardId);

        public int GetOwnedCount(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.cardId)) return 0;
            return _ownedLookup.TryGetValue(card.cardId, out var count) ? count : 0;
        }

        /// <summary>Current currency balance (earned by auto-selling duplicates - see AddCards).</summary>
        public int Currency => _saveData.currency;

        /// <summary>Bonus boosters earned from quests - separate from the once-per-day free slot
        /// (see HasClaimedFreeBoosterToday), so they stack independently.</summary>
        public int BonusFreeBoosters => _saveData.bonusFreeBoosters;

        /// <summary>Adds the cards from an opened booster to the collection. Duplicates stay owned
        /// and grant a small automatic bonus (+1 po per rarity rank, see CardRarity.rank) - real
        /// value comes from the Marketplace or system buyback instead (see MarketplaceService /
        /// TrySellToSystem). Does not save to disk - call Save() once after.</summary>
        public List<CardPullResult> AddCards(IEnumerable<CardData> cards)
        {
            var results = new List<CardPullResult>();

            foreach (var card in cards)
            {
                if (card == null || string.IsNullOrEmpty(card.cardId))
                {
                    Debug.LogWarning("[CollectionManager] Skipped a card with no cardId set.");
                    continue;
                }

                bool wasNew = !_ownedLookup.ContainsKey(card.cardId);
                _ownedLookup[card.cardId] = GetOwnedCount(card) + 1;

                int currencyEarned = 0;
                if (!wasNew && card.rarity != null)
                {
                    currencyEarned = Mathf.Max(1, card.rarity.rank + 1);
                    _saveData.currency += currencyEarned;
                }

                results.Add(new CardPullResult(card, wasNew, currencyEarned));
            }

            return results;
        }

        /// <summary>Rachat immediat par le jeu (transaction purement locale, pas de Cloud Code -
        /// voir CloudSyncService pour pourquoi c'est correct ici mais pas pour la Marketplace),
        /// a prix bas et fixe (voir GetSystemBuybackPrice). Vend jusqu'a <paramref name="quantity"/>
        /// exemplaires d'UNE carte, mais garde TOUJOURS au moins un exemplaire - le dernier
        /// exemplaire ne peut se vendre que via la Marketplace. Returns false si la carte est
        /// inconnue, quantity <= 0, ou pas assez de doublons disponibles. Ne sauvegarde pas seule;
        /// call Save() apres.</summary>
        public bool TrySellToSystem(string cardId, int quantity, out int totalEarned)
        {
            totalEarned = 0;
            if (string.IsNullOrEmpty(cardId) || quantity <= 0) return false;

            var card = _database.GetById(cardId);
            if (card == null) return false;

            if (!_ownedLookup.TryGetValue(cardId, out var owned)) return false;

            int maxSellable = owned - 1;
            if (quantity > maxSellable) return false;

            totalEarned = GetSystemBuybackPrice(card) * quantity;
            _ownedLookup[cardId] = owned - quantity;
            _saveData.currency += totalEarned;
            return true;
        }

        /// <summary>Prix de rachat pour UN exemplaire - 15% sous rarity.sellValue, arrondi vers le
        /// bas, minimum 1 po. Utilise par l'onglet "Rachat" (ShopScreen) et TrySellToSystem.</summary>
        public static int GetSystemBuybackPrice(CardData card)
        {
            if (card == null || card.rarity == null) return 0;
            return Mathf.Max(1, Mathf.FloorToInt(card.rarity.sellValue * 0.85f));
        }

        /// <summary>Grants currency directly, with no card involved - used for quest rewards.
        /// Does not save to disk on its own; call Save() afterwards.</summary>
        public void AddCurrency(int amount)
        {
            _saveData.currency += Mathf.Max(0, amount);
        }

        /// <summary>Attempts to spend currency. Returns false and makes no change if the balance
        /// is insufficient (no partial spends). Does not save to disk; call Save() afterwards.</summary>
        public bool TrySpendCurrency(int amount)
        {
            if (amount < 0) amount = 0;
            if (_saveData.currency < amount) return false;

            _saveData.currency -= amount;
            return true;
        }

        /// <summary>Grants bonus boosters - used for quest rewards (see QuestManager.TryClaimReward).
        /// Does not save to disk on its own; call Save() afterwards.</summary>
        public void AddBonusFreeBoosters(int amount)
        {
            _saveData.bonusFreeBoosters += Mathf.Max(0, amount);
        }

        /// <summary>Attempts to spend one bonus booster. Returns false if none are available.
        /// Does not save to disk on its own; call Save() afterwards.</summary>
        public bool TrySpendBonusFreeBooster()
        {
            if (_saveData.bonusFreeBoosters <= 0) return false;
            _saveData.bonusFreeBoosters--;
            return true;
        }

        /// <summary>Stable string key for a CardSet in the banked-booster lookup - falls back to
        /// the asset name if setId was left empty.</summary>
        private static string SetKey(CardSet set) => string.IsNullOrEmpty(set.setId) ? set.name : set.setId;

        /// <summary>How many boosters of this Set are currently banked/unopened - credited when a
        /// Display is bought "one by one" instead of revealed all at once. Drained via
        /// TryConsumeBankedBooster through the normal open-booster flow.</summary>
        public int GetBankedBoosterCount(CardSet set)
        {
            if (set == null) return 0;
            return _bankedBoosterLookup.TryGetValue(SetKey(set), out var count) ? count : 0;
        }

        /// <summary>Adds banked boosters for a Set. Does not save to disk on its own; call Save()
        /// afterwards.</summary>
        public void AddBankedBoosters(CardSet set, int amount)
        {
            if (set == null || amount <= 0) return;

            string key = SetKey(set);
            _bankedBoosterLookup.TryGetValue(key, out var current);
            _bankedBoosterLookup[key] = current + amount;
        }

        /// <summary>Attempts to consume one banked booster for this Set. Returns false if none are
        /// banked. Does not save to disk on its own; call Save() afterwards.</summary>
        public bool TryConsumeBankedBooster(CardSet set)
        {
            if (set == null) return false;

            string key = SetKey(set);
            if (!_bankedBoosterLookup.TryGetValue(key, out var count) || count <= 0) return false;

            _bankedBoosterLookup[key] = count - 1;
            return true;
        }

        /// <summary>Every distinct card currently owned, with its count - used by the Marketplace's
        /// sell picker and the Boutique's "Rachat" tab. Snapshot at call time, not a live view.</summary>
        public List<(string cardId, int count)> GetOwnedCardEntries()
        {
            var result = new List<(string, int)>();
            foreach (var kv in _ownedLookup)
                if (kv.Value > 0) result.Add((kv.Key, kv.Value));
            return result;
        }

        /// <summary>Rebuilds _ownedLookup/_bankedBoosterLookup from _saveData's lists - needed after
        /// something replaces the shared PlayerSaveData's fields in place (currently only
        /// CloudSyncService, via PlayerSaveData.CopyFrom).</summary>
        public void RebuildLookupsFromSaveData()
        {
            _ownedLookup.Clear();
            foreach (var entry in _saveData.ownedCards)
                _ownedLookup[entry.cardId] = entry.count;

            _bankedBoosterLookup.Clear();
            foreach (var entry in _saveData.bankedBoosters)
                _bankedBoosterLookup[entry.setId] = entry.count;
        }

        public void Save()
        {
            _saveData.ownedCards.Clear();
            foreach (var kv in _ownedLookup)
                _saveData.ownedCards.Add(new OwnedCardEntry { cardId = kv.Key, count = kv.Value });

            _saveData.bankedBoosters.Clear();
            foreach (var kv in _bankedBoosterLookup)
                if (kv.Value > 0)
                    _saveData.bankedBoosters.Add(new BankedBoosterEntry { setId = kv.Key, count = kv.Value });

            SaveManager.Save(_saveData);
        }

        /// <summary>Wipes the save back to a fresh new-player state (owned cards, currency, bonus
        /// boosters, banked boosters, daily claim flag, quest progress). Test-only, used by the
        /// Profile screen's "Reset" button (destructive, irreversible). Saves to disk immediately.</summary>
        public void ResetAll()
        {
            _ownedLookup.Clear();
            _bankedBoosterLookup.Clear();
            _saveData.ownedCards.Clear();
            _saveData.bankedBoosters.Clear();
            _saveData.currency = 0;
            _saveData.bonusFreeBoosters = 0;
            _saveData.lastFreeBoosterDateIso = "";
            _saveData.quests.questsDateIso = "";
            _saveData.quests.entries.Clear();

            SaveManager.Save(_saveData);
        }

        /// <summary>Distinct cards owned vs total cards that exist in this set.</summary>
        public (int owned, int total) GetSetCompletionCounts(CardSet set)
        {
            var cardsInSet = _database.GetCardsInSet(set);
            int owned = 0;
            foreach (var card in cardsInSet)
            {
                if (_ownedLookup.ContainsKey(card.cardId)) owned++;
            }
            return (owned, cardsInSet.Count);
        }

        public float GetSetCompletionRatio(CardSet set)
        {
            var (owned, total) = GetSetCompletionCounts(set);
            return total == 0 ? 0f : (float)owned / total;
        }

        /// <summary>Distinct cards owned vs. total cards across the entire database. Used for the
        /// Profile screen's global summary.</summary>
        public (int owned, int total) GetOverallCompletionCounts()
        {
            int owned = 0;
            int total = 0;
            foreach (var card in _database.allCards)
            {
                if (card == null || string.IsNullOrEmpty(card.cardId)) continue;
                total++;
                if (_ownedLookup.ContainsKey(card.cardId)) owned++;
            }
            return (owned, total);
        }

        // --- Daily free booster ---

        public bool HasClaimedFreeBoosterToday()
        {
            return _saveData.lastFreeBoosterDateIso == TodayIso();
        }

        public void MarkFreeBoosterClaimedToday()
        {
            _saveData.lastFreeBoosterDateIso = TodayIso();
        }

        private static string TodayIso() => DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
}