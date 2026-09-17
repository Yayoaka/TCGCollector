using System;
using System.Collections.Generic;
using System.Linq;
using TCGCollector.Data;
using UnityEngine;
using Random = System.Random;

namespace TCGCollector.Systems
{
    /// <summary>Pure logic for drawing the cards in a booster pack. No MonoBehaviour/UI, so it's
    /// unit-testable on its own (optionally with a seeded System.Random). Call OpenBooster.
    /// Supports two modes per-BoosterConfig: legacy weighted (packSize independent draws by
    /// dropWeight) and slot-structure (fixed Common/Uncommon slots plus upgradeable slot groups
    /// and an optional bonus slot, matching a real physical pack).</summary>
    public class BoosterOpeningService
    {
        private readonly Random _rng;

        public BoosterOpeningService(Random rng = null)
        {
            _rng = rng ?? new Random();
        }

        public List<CardData> OpenBooster(CardSet set, CardDatabase database, BoosterConfig config)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var cardsInSet = database.GetCardsInSet(set);
            if (cardsInSet.Count == 0)
            {
                Debug.LogWarning($"[BoosterOpeningService] Set '{set.displayName}' has no cards in the database. Did you rebuild the Card Database (Tools > TCG Collector > Rebuild Card Database)?");
                return new List<CardData>();
            }

            var cardsByRarity = new Dictionary<CardRarity, List<CardData>>();
            foreach (var card in cardsInSet)
            {
                if (card.rarity == null) continue;
                if (!cardsByRarity.TryGetValue(card.rarity, out var list))
                {
                    list = new List<CardData>();
                    cardsByRarity.Add(card.rarity, list);
                }
                list.Add(card);
            }

            if (cardsByRarity.Count == 0)
            {
                Debug.LogWarning($"[BoosterOpeningService] No card in set '{set.displayName}' has a Rarity assigned.");
                return new List<CardData>();
            }

            // Only needed when duplicates aren't allowed within a single pack.
            var workingPools = config.allowDuplicatesInSamePack
                ? null
                : cardsByRarity.ToDictionary(kv => kv.Key, kv => new List<CardData>(kv.Value));

            return config.useSlotStructure
                ? OpenBoosterFromSlots(config, cardsByRarity, workingPools)
                : OpenBoosterWeighted(config, cardsByRarity, workingPools);
        }

        // --- Legacy weighted mode ---

        private List<CardData> OpenBoosterWeighted(
            BoosterConfig config,
            Dictionary<CardRarity, List<CardData>> cardsByRarity,
            Dictionary<CardRarity, List<CardData>> workingPools)
        {
            var result = new List<CardData>(config.packSize);

            for (int slot = 0; slot < config.packSize; slot++)
            {
                bool isGuaranteedSlot = config.hasGuaranteedSlot
                    && slot == config.packSize - 1
                    && config.guaranteedMinimumRarity != null;

                CardRarity chosenRarity = null;

                if (isGuaranteedSlot)
                {
                    chosenRarity = PickWeightedRarity(cardsByRarity.Keys, r => r.rank >= config.guaranteedMinimumRarity.rank);
                }

                // Not a guaranteed slot, or the guarantee filter matched nothing - fall back to normal draw.
                if (chosenRarity == null)
                {
                    chosenRarity = PickWeightedRarity(cardsByRarity.Keys, _ => true);
                }

                if (chosenRarity == null) continue; // should not happen given the guards above

                var drawnCard = DrawCard(chosenRarity, cardsByRarity, workingPools, config.allowDuplicatesInSamePack);
                if (drawnCard != null) result.Add(drawnCard);
            }

            return result;
        }

        // --- Slot-structure mode (real pack layout) ---

        private List<CardData> OpenBoosterFromSlots(
            BoosterConfig config,
            Dictionary<CardRarity, List<CardData>> cardsByRarity,
            Dictionary<CardRarity, List<CardData>> workingPools)
        {
            var result = new List<CardData>();

            AddFixedSlots(result, config.commonSlotRarity, config.commonSlotCount, cardsByRarity, workingPools, config.allowDuplicatesInSamePack);
            AddFixedSlots(result, config.uncommonSlotRarity, config.uncommonSlotCount, cardsByRarity, workingPools, config.allowDuplicatesInSamePack);

            // Second Slot Group first so both groups are drawn in a consistent order.
            AddSlotGroup(result, config.secondSlotGroup, cardsByRarity, workingPools, config.allowDuplicatesInSamePack);
            AddSlotGroup(result, config.rareSlotGroup, cardsByRarity, workingPools, config.allowDuplicatesInSamePack);

            if (config.includeBonusSlot)
            {
                // Restricted to a closed pool when set, otherwise any rarity in the set.
                var bonusRarity = config.bonusSlotRestrictToRarities != null && config.bonusSlotRestrictToRarities.Count > 0
                    ? PickWeightedRarity(config.bonusSlotRestrictToRarities, _ => true)
                    : PickWeightedRarity(cardsByRarity.Keys, _ => true);

                var drawn = DrawSingleSlot(bonusRarity, cardsByRarity, workingPools, config.allowDuplicatesInSamePack);
                if (drawn != null) result.Add(drawn);
            }

            return result;
        }

        private void AddFixedSlots(
            List<CardData> result,
            CardRarity rarity,
            int count,
            Dictionary<CardRarity, List<CardData>> cardsByRarity,
            Dictionary<CardRarity, List<CardData>> workingPools,
            bool allowDuplicates)
        {
            for (int i = 0; i < count; i++)
            {
                var drawn = DrawSingleSlot(rarity, cardsByRarity, workingPools, allowDuplicates);
                if (drawn != null) result.Add(drawn);
            }
        }

        /// <summary>Draws every card in a BoosterSlotGroup - each card rolls the upgrade ladder
        /// (rarest tier first, first threshold to clear wins), falling back to a weighted pick
        /// among baseRarities if no tier triggers. No-op if group.count is 0.</summary>
        private void AddSlotGroup(
            List<CardData> result,
            BoosterSlotGroup group,
            Dictionary<CardRarity, List<CardData>> cardsByRarity,
            Dictionary<CardRarity, List<CardData>> workingPools,
            bool allowDuplicates)
        {
            if (group == null) return;

            for (int i = 0; i < group.count; i++)
            {
                CardRarity chosen = null;

                if (group.upgradeTiers != null && group.upgradeTiers.Count > 0)
                {
                    double roll = _rng.NextDouble() * 100.0;
                    foreach (var tier in group.upgradeTiers)
                    {
                        if (tier.rarity != null && roll < tier.atLeastChancePercent)
                        {
                            chosen = tier.rarity;
                            break;
                        }
                    }
                }

                if (chosen == null && group.baseRarities != null && group.baseRarities.Count > 0)
                    chosen = PickWeightedRarity(group.baseRarities, _ => true);

                var drawn = DrawSingleSlot(chosen, cardsByRarity, workingPools, allowDuplicates);
                if (drawn != null) result.Add(drawn);
            }
        }

        /// <summary>Draws one card of the requested rarity, falling back to a weighted pick across
        /// whatever rarities are present if the requested one has no cards yet - a slot never gets
        /// silently dropped.</summary>
        private CardData DrawSingleSlot(
            CardRarity rarity,
            Dictionary<CardRarity, List<CardData>> cardsByRarity,
            Dictionary<CardRarity, List<CardData>> workingPools,
            bool allowDuplicates)
        {
            if (rarity == null || !cardsByRarity.ContainsKey(rarity))
            {
                rarity = PickWeightedRarity(cardsByRarity.Keys, _ => true);
                if (rarity == null) return null;
            }

            return DrawCard(rarity, cardsByRarity, workingPools, allowDuplicates);
        }

        // --- Shared helpers ---

        private CardRarity PickWeightedRarity(IEnumerable<CardRarity> candidates, Func<CardRarity, bool> filter)
        {
            var eligible = candidates.Where(r => r != null).Where(filter).Where(r => r.dropWeight > 0f).ToList();
            if (eligible.Count == 0) return null;

            float total = eligible.Sum(r => r.dropWeight);
            double roll = _rng.NextDouble() * total;

            double cumulative = 0;
            foreach (var rarity in eligible)
            {
                cumulative += rarity.dropWeight;
                if (roll < cumulative) return rarity;
            }
            return eligible[eligible.Count - 1]; // floating point safety net
        }

        private CardData DrawCard(
            CardRarity rarity,
            Dictionary<CardRarity, List<CardData>> fullPools,
            Dictionary<CardRarity, List<CardData>> workingPools,
            bool allowDuplicates)
        {
            if (allowDuplicates)
            {
                var pool = fullPools[rarity];
                return pool[_rng.Next(pool.Count)];
            }

            var working = workingPools[rarity];
            if (working.Count == 0)
            {
                // Ran out of unique cards of this rarity - fall back to a duplicate.
                var fallbackPool = fullPools[rarity];
                return fallbackPool[_rng.Next(fallbackPool.Count)];
            }

            int index = _rng.Next(working.Count);
            var card = working[index];
            working.RemoveAt(index);
            return card;
        }
    }
}

