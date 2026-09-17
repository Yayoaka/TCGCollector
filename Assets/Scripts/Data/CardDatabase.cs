using System.Collections.Generic;
using UnityEngine;

namespace TCGCollector.Data
{
    /// <summary>
    /// Master list of every CardData in the game (one instance per project). "allCards" is
    /// populated automatically by Tools > TCG Collector > Rebuild Card Database — don't fill
    /// it in by hand.
    /// </summary>
    [CreateAssetMenu(fileName = "CardDatabase", menuName = "TCG Collector/Card Database", order = 100)]
    public class CardDatabase : ScriptableObject
    {
        public List<CardData> allCards = new List<CardData>();

        private Dictionary<string, CardData> _byId;
        private Dictionary<CardSet, List<CardData>> _bySet;

        private void EnsureIndexBuilt()
        {
            if (_byId != null && _bySet != null) return;

            _byId = new Dictionary<string, CardData>();
            _bySet = new Dictionary<CardSet, List<CardData>>();

            foreach (var card in allCards)
            {
                if (card == null) continue;

                if (!string.IsNullOrEmpty(card.cardId))
                {
                    if (_byId.ContainsKey(card.cardId))
                        Debug.LogWarning($"[CardDatabase] Duplicate cardId '{card.cardId}' on '{card.name}' — ids must be unique.");
                    else
                        _byId.Add(card.cardId, card);
                }

                if (card.set == null) continue;

                if (!_bySet.TryGetValue(card.set, out var list))
                {
                    list = new List<CardData>();
                    _bySet.Add(card.set, list);
                }
                list.Add(card);
            }
        }

        /// <summary>Call this after modifying allCards at runtime (e.g. after a hot-reload) to force re-indexing.</summary>
        public void InvalidateIndex()
        {
            _byId = null;
            _bySet = null;
        }

        public CardData GetById(string cardId)
        {
            EnsureIndexBuilt();
            _byId.TryGetValue(cardId, out var card);
            return card;
        }

        public IReadOnlyList<CardData> GetCardsInSet(CardSet set)
        {
            EnsureIndexBuilt();
            return _bySet.TryGetValue(set, out var list) ? list : System.Array.Empty<CardData>();
        }

        public List<CardData> GetCardsInSetWithRarity(CardSet set, CardRarity rarity)
        {
            var result = new List<CardData>();
            foreach (var card in GetCardsInSet(set))
            {
                if (card.rarity == rarity) result.Add(card);
            }
            return result;
        }

        /// <summary>Every distinct CardSet referenced by at least one card, in no particular
        /// order. Used to populate set-selection UI.</summary>
        public List<CardSet> GetAllSets()
        {
            EnsureIndexBuilt();
            return new List<CardSet>(_bySet.Keys);
        }

        /// <summary>Every distinct CardRarity referenced by at least one card, ordered by rank
        /// (ascending — common first). Used to populate rarity-filter UI.</summary>
        public List<CardRarity> GetAllRarities()
        {
            var set = new HashSet<CardRarity>();
            foreach (var card in allCards)
            {
                if (card != null && card.rarity != null) set.Add(card.rarity);
            }

            var list = new List<CardRarity>(set);
            list.Sort((a, b) => a.rank.CompareTo(b.rank));
            return list;
        }

        /// <summary>Every distinct License with at least one Set that has cards in the database,
        /// ordered by sortOrder. Sets with no license assigned are excluded — see
        /// GetSetsInLicense.</summary>
        public List<License> GetAllLicenses()
        {
            EnsureIndexBuilt();
            var found = new HashSet<License>();
            foreach (var set in _bySet.Keys)
            {
                if (set != null && set.license != null) found.Add(set.license);
            }

            var list = new List<License>(found);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }

        /// <summary>Every Set under the given License that has at least one card in the database,
        /// ordered by sortOrder.</summary>
        public List<CardSet> GetSetsInLicense(License license)
        {
            EnsureIndexBuilt();
            var list = new List<CardSet>();
            foreach (var set in _bySet.Keys)
            {
                if (set != null && set.license == license) list.Add(set);
            }

            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }
    }
}
