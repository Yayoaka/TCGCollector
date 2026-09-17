using UnityEngine;

namespace TCGCollector.Data
{
    /// <summary>
    /// A single card definition. Create instances via Assets > Create > TCG Collector > Card.
    /// One asset per card (including variants — a variant is its own CardData with isVariant = true).
    /// </summary>
    [CreateAssetMenu(fileName = "Card_", menuName = "TCG Collector/Card", order = 2)]
    public class CardData : ScriptableObject
    {
        [Tooltip("Unique stable id across the whole game, used as a save-data key. Do not change once players can own this card. Suggested format: '<setId>_<number>' e.g. 'sololeveling_045'.")]
        public string cardId;

        public string displayName;

        public CardSet set;

        public CardRarity rarity;

        [Tooltip("Card number within its set, e.g. 45 for '045/300'. Used for sorting/display, not uniqueness.")]
        public int cardNumber;

        [Tooltip("Deprecated - no longer read at runtime. Real artwork now lives at " +
            "Resources/Cards/<cardId>.<ext> and is loaded on demand by CardArtworkLoader (see " +
            "Systems/CardArtworkLoader.cs) so it can actually be released from memory when nothing " +
            "is showing it, instead of staying loaded for the rest of the session just because this " +
            "field points at it. Kept only so TCG Collector > Migrate Artwork To Resources has " +
            "something to read from on old cards that still have it set - don't populate it on new " +
            "cards, drop the image straight into Resources/Cards/<cardId>.<ext> instead.")]
        public Sprite artwork;

        [TextArea(2, 4)]
        public string flavorText;

        [Tooltip("True if this is an alternate-art / special variant of another card.")]
        public bool isVariant;

        public override string ToString() => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}
