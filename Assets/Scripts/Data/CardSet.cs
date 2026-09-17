using UnityEngine;
using TCGCollector.Systems;

namespace TCGCollector.Data
{
    /// <summary>
    /// A Set corresponds to a license/universe (e.g. "League of Legends", "Solo Leveling").
    /// Create instances via Assets > Create > TCG Collector > Set.
    /// </summary>
    [CreateAssetMenu(fileName = "Set_", menuName = "TCG Collector/Set", order = 1)]
    public class CardSet : ScriptableObject
    {
        [Tooltip("Unique stable id, used as a save-data key. Do not change once players own cards from this set.")]
        public string setId;

        public string displayName;

        [TextArea(2, 5)]
        public string description;

        [Tooltip("Pack artwork shown on the 2D booster pack before it's torn open (see " +
            "BoosterOpenScreen.ShowBoosterPackVisual) - also reusable anywhere else this Set needs " +
            "a cover image. Leave empty to fall back to a plain colored placeholder panel.")]
        public Sprite coverArt;

        [Tooltip("Used only to order sets in menus (lower = shown first).")]
        public int sortOrder;

        [Tooltip("The License/franchise this set belongs to (e.g. Riftbound, Solo Leveling). Used to group " +
            "sets together on the Profile screen — a license can have several sets (e.g. Solo Leveling's " +
            "MapUniverse and Union Card lines). Optional: sets with no license assigned still work fine, " +
            "they just won't be grouped under anything on the Profile screen.")]
        public License license;

        [Tooltip("The booster pack recipe used whenever THIS set is opened (pack size, rarity slots, prices...). " +
            "Lets each licensed set have its own real pack structure — e.g. Riftbound = 14 cards, a future " +
            "Solo Leveling set = 8 cards — without touching any code. Leave empty to fall back to GameManager's " +
            "shared Default Booster Config.")]
        public BoosterConfig boosterConfig;

        public override string ToString() => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}
