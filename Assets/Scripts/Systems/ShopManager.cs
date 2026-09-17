using System;
using System.Collections.Generic;
using TCGCollector.Data;
using TCGCollector.Save;
using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>One licence's shop offer for today, as returned by ShopManager.GetTodaysOffers.</summary>
    public readonly struct ShopOffer
    {
        public readonly License License;

        /// <summary>The card offered today for this licence, or null if nothing eligible is left
        /// (all shopEligible cards owned, or none exist) - the UI shows a placeholder instead.</summary>
        public readonly CardData Card;

        /// <summary>True if Card is already owned - can happen mid-day if the player pulls this
        /// exact card from a booster after today's offer was generated. The offer isn't rerolled
        /// early; the UI should just grey it out instead.</summary>
        public readonly bool AlreadyOwned;

        public ShopOffer(License license, CardData card, bool alreadyOwned)
        {
            License = license;
            Card = card;
            AlreadyOwned = alreadyOwned;
        }
    }

    /// <summary>
    /// Daily rotating shop: one offer per Licence, regenerated once per calendar day (UTC). Each
    /// offer is a random card from the HIGHEST shopEligible rarity tier that still has an unowned
    /// candidate across every Set under that Licence (see PickCardForLicense). Buying costs the
    /// card's CardRarity.shopPrice.
    ///
    /// Shares its PlayerSaveData/CollectionManager instances with QuestManager, so a single
    /// CollectionManager.Save() call persists everything together.
    /// </summary>
    public class ShopManager
    {
        private readonly CardDatabase _database;
        private readonly PlayerSaveData _saveData;
        private readonly CollectionManager _collection;

        public ShopManager(CardDatabase database, PlayerSaveData saveData, CollectionManager collection)
        {
            _database = database;
            _saveData = saveData;
            _collection = collection;
            EnsureTodaysOffers();
        }

        private static string TodayIso() => DateTime.UtcNow.ToString("yyyy-MM-dd");

        /// <summary>Regenerates every licence's offer the first time this is called on a new
        /// calendar day - called defensively at the top of every public method below to avoid
        /// acting on a stale offer.</summary>
        private void EnsureTodaysOffers()
        {
            string today = TodayIso();
            if (_saveData.shop.shopDateIso == today) return;

            _saveData.shop.shopDateIso = today;
            _saveData.shop.entries.Clear();

            foreach (var license in _database.GetAllLicenses())
            {
                var card = PickCardForLicense(license);
                _saveData.shop.entries.Add(new ShopOfferEntry
                {
                    licenseId = license.licenseId,
                    cardId = card != null ? card.cardId : ""
                });
            }
        }

        /// <summary>Picks one random unowned, shop-eligible card from the highest rarity rank that
        /// still has a candidate, across every Set under this licence. Returns null if none
        /// exist or all are owned.</summary>
        private CardData PickCardForLicense(License license)
        {
            int bestRank = int.MinValue;
            var bestCandidates = new List<CardData>();

            foreach (var set in _database.GetSetsInLicense(license))
            {
                foreach (var card in _database.GetCardsInSet(set))
                {
                    if (card == null || card.rarity == null || !card.rarity.shopEligible) continue;
                    if (_collection.GetOwnedCount(card) > 0) continue;

                    if (card.rarity.rank > bestRank)
                    {
                        bestRank = card.rarity.rank;
                        bestCandidates.Clear();
                        bestCandidates.Add(card);
                    }
                    else if (card.rarity.rank == bestRank)
                    {
                        bestCandidates.Add(card);
                    }
                }
            }

            if (bestCandidates.Count == 0) return null;
            return bestCandidates[UnityEngine.Random.Range(0, bestCandidates.Count)];
        }

        /// <summary>Re-attempts PickCardForLicense for any licence whose stored offer is currently
        /// empty, and adds an entry for any licence that doesn't have one yet (e.g. added mid-day).
        /// Called every time today's offers are read or a purchase is attempted, not just once at
        /// rollover, so content changes take effect without waiting for tomorrow. Only fills empty
        /// offers - a licence with a real offer keeps it all day, so this can't be used to reroll.</summary>
        private void BackfillEmptyOffers()
        {
            foreach (var license in _database.GetAllLicenses())
            {
                var entry = FindEntry(license.licenseId);
                if (entry == null)
                {
                    entry = new ShopOfferEntry { licenseId = license.licenseId, cardId = "" };
                    _saveData.shop.entries.Add(entry);
                }

                if (!string.IsNullOrEmpty(entry.cardId)) continue;

                var card = PickCardForLicense(license);
                if (card != null) entry.cardId = card.cardId;
            }
        }

        private ShopOfferEntry FindEntry(string licenseId)
        {
            foreach (var entry in _saveData.shop.entries)
            {
                if (entry.licenseId == licenseId) return entry;
            }
            return null;
        }

        /// <summary>Today's offers, one per licence, in CardDatabase.GetAllLicenses order.
        /// AlreadyOwned is re-derived fresh every call so a card obtained mid-day via a booster
        /// pull shows correctly greyed-out.</summary>
        public List<ShopOffer> GetTodaysOffers()
        {
            EnsureTodaysOffers();
            BackfillEmptyOffers();

            var result = new List<ShopOffer>();
            foreach (var license in _database.GetAllLicenses())
            {
                var entry = FindEntry(license.licenseId);
                CardData card = entry != null && !string.IsNullOrEmpty(entry.cardId)
                    ? _database.GetById(entry.cardId)
                    : null;
                bool alreadyOwned = card != null && _collection.GetOwnedCount(card) > 0;
                result.Add(new ShopOffer(license, card, alreadyOwned));
            }
            return result;
        }

        /// <summary>Buys today's offered card for the given licence, paying CardRarity.shopPrice.
        /// Returns false (nothing spent or granted) if there's no offer, the card is already
        /// owned, or the balance is insufficient. Does not save to disk on its own.</summary>
        public bool TryPurchaseOffer(License license)
        {
            EnsureTodaysOffers();
            BackfillEmptyOffers();
            if (license == null) return false;

            var entry = FindEntry(license.licenseId);
            if (entry == null || string.IsNullOrEmpty(entry.cardId)) return false;

            var card = _database.GetById(entry.cardId);
            if (card == null || card.rarity == null) return false;
            if (_collection.GetOwnedCount(card) > 0) return false;

            if (!_collection.TrySpendCurrency(card.rarity.shopPrice)) return false;

            _collection.AddCards(new List<CardData> { card });
            return true;
        }
    }
}