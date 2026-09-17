using System.Collections.Generic;
using UnityEngine;
using TCGCollector.Data;

namespace TCGCollector.Systems
{
    /// <summary>One upgrade step within a BoosterSlotGroup - e.g. "Epic" at 25% means a 25% chance
    /// the slot is Epic-or-rarer instead of the group's Base Rarity. See BoosterSlotGroup.</summary>
    [System.Serializable]
    public class BoosterUpgradeTier
    {
        public CardRarity rarity;

        [Tooltip("Chance (0-100) that this slot is AT LEAST this tier - cumulative, includes any rarer tier listed above it in the list. E.g. for Riftbound: Signed 0.139, Overnumbered 1.389, Alt Art 8.333, Epic 25 (in that order, rarest first) matches the real print ratios.")]
        [Range(0f, 100f)]
        public float atLeastChancePercent;
    }

    /// <summary>One "family" of pack slots sharing the same upgrade ladder - e.g. Riftbound's
    /// "Rare ou mieux" slots or Pokemon's Reverse slot. Each of the 'count' cards independently
    /// rolls the ladder.</summary>
    [System.Serializable]
    public class BoosterSlotGroup
    {
        [Tooltip("Label shown here in the Inspector only, to help tell slot groups apart (e.g. 'Reverse Holo', 'Rare ou mieux'). Not shown to players.")]
        public string label;

        [Tooltip("How many cards this slot group draws per pack. Set to 0 to disable this group entirely (e.g. leave Second Slot Group at 0 for a game that only needs one upgradeable slot family, like Riftbound).")]
        [Min(0)]
        public int count;

        [Tooltip("Rarity drawn when none of the Upgrade Tiers below trigger. List more than one (e.g. Reverse Commune / Reverse Peu Commune / Reverse Rare) to pick between them using each rarity's Drop Weight, same as everywhere else in the game - list just one for a single fixed base rarity (e.g. just 'Rare').")]
        public List<CardRarity> baseRarities = new List<CardRarity>();

        [Tooltip("Upgrade chances, ordered from RAREST to MOST COMMON (smallest percent first). Each is the chance the card is AT LEAST that tier (cumulative) - see BoosterUpgradeTier. Leave empty for a group with no upgrades at all (every card is just a Base Rarity).")]
        public List<BoosterUpgradeTier> upgradeTiers = new List<BoosterUpgradeTier>();
    }

    /// <summary>Configurable "recipe" for a booster pack. Reusable across several Sets.</summary>
    [CreateAssetMenu(fileName = "BoosterConfig_", menuName = "TCG Collector/Booster Config", order = 3)]
    public class BoosterConfig : ScriptableObject
    {
        [Header("Legacy / simple mode (used when Use Slot Structure below is off)")]
        [Min(1)]
        public int packSize = 10;

        [Tooltip("If true, the same card can appear more than once in a single pack.")]
        public bool allowDuplicatesInSamePack = true;

        [Tooltip("If true, the LAST card of the pack is guaranteed to be at least 'guaranteedMinimumRarity'.")]
        public bool hasGuaranteedSlot = true;

        [Tooltip("Minimum rarity (by rank) guaranteed for the guaranteed slot. Ignored if hasGuaranteedSlot is false.")]
        public CardRarity guaranteedMinimumRarity;

        [Header("Realistic pack structure (e.g. Riftbound: 7 Common + 3 Uncommon + 2 Rare-or-better + 1 Token, Pokemon: 4 Commune + 3 Peu Commune + 1 Reverse-or-better + 1 Rare-or-better)")]
        [Tooltip("If true, ignores packSize/hasGuaranteedSlot above and instead builds every pack from the fixed slots below, matching a real product's pack structure.")]
        public bool useSlotStructure = false;

        [Header("Fixed slots (always the same rarity, no upgrade chance)")]
        public CardRarity commonSlotRarity;
        [Min(0)] public int commonSlotCount = 7;

        public CardRarity uncommonSlotRarity;
        [Min(0)] public int uncommonSlotCount = 3;

        [Header("Slot Group 1 - main 'Rare ou mieux' family (e.g. Riftbound's 2 Foil Rare-or-better slots, Pokemon's Rare/Double Rare/Ultra Rare slot)")]
        public BoosterSlotGroup rareSlotGroup = new BoosterSlotGroup();

        [Header("Slot Group 2 - optional second upgradeable family (e.g. Pokemon's Reverse/Illustration Rare/Special Illustration Rare/Mega Hyper Rare slot). Leave Count at 0 for a game that doesn't need one, like Riftbound.")]
        public BoosterSlotGroup secondSlotGroup = new BoosterSlotGroup();

        [Header("Bonus slot (one extra card on top of everything above)")]
        [Tooltip("One extra card - by default drawn from the full weighted rarity pool (any rarity in the set). Restrict it with Bonus Slot Restrict To Rarities below for a closed pool like Riftbound's Token/Rune/Rune Alt cards.")]
        public bool includeBonusSlot = true;

        [Tooltip("If set, the bonus slot only draws from these rarities instead of any rarity in the set - use this for a closed pool that should NEVER appear anywhere else in the pack (e.g. Riftbound: create Token/Rune/Rune Alt rarities, list them here, and give cards 271-274/007.../007a... that rarity so they only ever come from this slot). Weighted by each rarity's Drop Weight, same as everywhere else. Leave empty for the old 'any rarity in the set' behaviour.")]
        public List<CardRarity> bonusSlotRestrictToRarities = new List<CardRarity>();

        [Header("Shop pricing (V0.5)")]
        [Tooltip("Currency cost to buy and open a single booster of this config from the shop.")]
        [Min(0)] public int purchasePrice = 100;

        [Header("Bundle tier (optional, between Booster and Display - e.g. Pokemon: 6 boosters)")]
        [Tooltip("How many boosters come in one Bundle purchase (e.g. 6). Leave at 0 or 1 to disable the Bundle tier entirely for this config - the Buy Bundle button simply won't be shown.")]
        [Min(0)] public int bundleSize = 0;

        [Tooltip("Discount applied to the Bundle's total price vs. buying bundleSize boosters one by one (0.05 = 5% off). Usually smaller than displayDiscount since a Bundle is a smaller commitment than a full Display.")]
        [Range(0f, 0.9f)] public float bundleDiscount = 0.05f;

        [Header("Display (e.g. 24 for a real Riftbound booster box, 36 for Pokemon)")]
        [Tooltip("How many boosters come in one Display/box purchase (e.g. 24 for a real Riftbound booster box).")]
        [Min(1)] public int displaySize = 24;

        [Tooltip("Discount applied to the Display's total price vs. buying displaySize boosters one by one (0.08 = 8% off).")]
        [Range(0f, 0.9f)] public float displayDiscount = 0.08f;

        /// <summary>True if this config defines a real Bundle tier - UI uses this to decide whether
        /// to show the "Buy Bundle" button at all.</summary>
        public bool HasBundle => bundleSize > 1;

        /// <summary>Total price of a full Bundle, after the discount above. 0 if HasBundle is false.</summary>
        public int BundlePrice => Mathf.RoundToInt(purchasePrice * bundleSize * (1f - bundleDiscount));

        /// <summary>Total price of a full Display, after the discount above.</summary>
        public int DisplayPrice => Mathf.RoundToInt(purchasePrice * displaySize * (1f - displayDiscount));
    }
}

